using System.Security.Cryptography;
using System.Text.Json;
using ResourceManager.Core.Models;
using ResourceManager.Core.Scanning;
using ResourceManager.Infrastructure.Effekseer;
using ResourceManager.Infrastructure.FileSystem;

namespace ResourceManager.Infrastructure.Mapping;

public sealed class TextureMappingService
{
    private readonly EffekseerResourceParser parser = new();
    public async Task<TextureDiscovery> DiscoverAsync(string effectPath, string searchRoot, CancellationToken token = default)
    {
        return await DiscoverIndexedAsync(effectPath, searchRoot, null, token);
    }

    private async Task<TextureDiscovery> DiscoverIndexedAsync(string effectPath, string searchRoot, DiscoveryResult? index,
        CancellationToken token, ILookup<string, SearchCandidate>? indexedCandidates = null)
    {
        effectPath = Path.GetFullPath(effectPath);
        searchRoot = Path.GetFullPath(searchRoot);
        if (!Directory.Exists(searchRoot)) throw new ArgumentException("Resource folder does not exist.");
        var effectHash = await HashAsync(effectPath, token);
        var effect = await parser.ParseAsync(effectPath, token);
        var compatibility = EffectFormatCompatibility.Evaluate(effect);
        if (!compatibility.CanMap) throw new InvalidDataException(compatibility.Rejection(effect));
        var groups = effect.References.Where(r => r.Type == ResourceType.Texture).GroupBy(r => r.OriginalReference, StringComparer.Ordinal).ToArray();
        var names = groups.Select(g => Path.GetFileName(g.Key.Replace('/', Path.DirectorySeparatorChar))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var discovery = index ?? await Task.Run(() => DocumentDiscovery.Enumerate(searchRoot, p => names.Contains(Path.GetFileName(p)), token), token);
        var candidates = indexedCandidates ?? discovery.Files.Select(p => new SearchCandidate(p, new FileInfo(p).Length)).ToLookup(c => Path.GetFileName(c.Path), StringComparer.OrdinalIgnoreCase);
        var requests = groups.Select(g =>
        {
            var name = Path.GetFileName(g.Key.Replace('/', Path.DirectorySeparatorChar));
            var required = g.Any(r => r.Role is ReferenceRole.Metadata or ReferenceRole.Runtime or ReferenceRole.CompatibilityRuntime);
            return new TextureRequest(g.Key, name, required, candidates[name].ToArray());
        }).OrderBy(r => r.FileName, StringComparer.OrdinalIgnoreCase).ToArray();
        if (await HashAsync(effectPath, token) != effectHash) throw new IOException("Effect changed while matching. Find matches again.");
        return new(effectPath, searchRoot, effectHash, requests, discovery.Diagnostics, effect.EditorVersion, compatibility.Profile!.Name);
    }

    public async Task<BatchDiscovery> DiscoverBatchAsync(string input, string searchRoot, string? excludedOutputRoot = null,
        IProgress<string>? progress = null, CancellationToken token = default)
    {
        input = Path.GetFullPath(input);
        searchRoot = Path.GetFullPath(searchRoot);
        if (!Directory.Exists(searchRoot)) throw new ArgumentException("Asset folder does not exist.");
        if (!Directory.Exists(input) && (!File.Exists(input) || !input.EndsWith(".efkefc", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Choose an effect or a folder of effects.");
        var files = Directory.Exists(input)
            ? await Task.Run(() => DocumentDiscovery.Enumerate(input, p => p.EndsWith(".efkefc", StringComparison.OrdinalIgnoreCase), token, excludedOutputRoot), token)
            : new DiscoveryResult([input], []);
        progress?.Report("กำลังค้นไฟล์ในคลัง Asset และโฟลเดอร์ย่อย…");
        var index = await Task.Run(() => DocumentDiscovery.Enumerate(searchRoot, _ => true, token, excludedOutputRoot), token);
        var candidates = await Task.Run(() => index.Files.Select(p =>
        {
            token.ThrowIfCancellationRequested();
            return new SearchCandidate(p, new FileInfo(p).Length);
        }).ToLookup(c => Path.GetFileName(c.Path), StringComparer.OrdinalIgnoreCase), token);
        var cases = new List<EffectDiscoveryCase>();
        foreach (var file in files.Files)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report($"ตรวจ Effect {cases.Count + 1}/{files.Files.Count}: {Path.GetFileName(file)}");
            try { cases.Add(new(file, await DiscoverIndexedAsync(file, searchRoot, index, token, candidates), null)); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
            { cases.Add(new(file, null, ex.Message)); }
        }
        return new(input, searchRoot, cases, files.Diagnostics);
    }

    public async Task<TextureMappingPlan> PlanAsync(TextureDiscovery discovery, IEnumerable<TextureSelection> selections,
        TextureMappingMode mode, string textureFolderName, string outputFileName, CancellationToken token = default)
        => await PlanAtAsync(discovery, selections, mode, textureFolderName, outputFileName,
            Path.GetDirectoryName(discovery.EffectPath)!, EffectWriteMode.NewCopy, null, null, token);

    public async Task<TextureMappingPlan> PlanAdvancedAsync(TextureDiscovery discovery, IEnumerable<TextureSelection> selections,
        TextureMappingMode mode, string textureFolderName, EffectWriteMode writeMode, string inputRoot, string outputRoot,
        CancellationToken token = default)
    {
        if (writeMode == EffectWriteMode.NewCopy) throw new ArgumentException("Use the single-copy workflow for NewCopy.");
        var effectDirectory = Path.GetDirectoryName(discovery.EffectPath)!;
        if (writeMode == EffectWriteMode.ReplaceOriginal)
            return await PlanAtAsync(discovery, selections, mode, textureFolderName, Path.GetFileName(discovery.EffectPath),
                effectDirectory, writeMode, null, null, token);
        inputRoot = Path.GetFullPath(inputRoot);
        if (File.Exists(inputRoot)) inputRoot = Path.GetDirectoryName(inputRoot)!;
        outputRoot = Path.GetFullPath(outputRoot);
        if (WindowsPathResolver.HasReparsePoint(outputRoot)) throw new ArgumentException("Output folder contains a link.");
        if (WindowsPathResolver.IsInside(discovery.EffectPath, outputRoot)) throw new ArgumentException("Output root contains the source effect. Choose a separate output folder.");
        var relative = Path.GetRelativePath(inputRoot, discovery.EffectPath);
        if (Path.IsPathFullyQualified(relative) || relative.StartsWith(".." + Path.DirectorySeparatorChar)) throw new ArgumentException("Effect is outside input root.");
        var packageRoot = Path.Combine(outputRoot, Path.ChangeExtension(relative, null) + ".assets");
        var audit = await new ResourceScanner(parser, new WindowsPathResolver()).ScanAsync([discovery.EffectPath], effectDirectory, cancellationToken: token);
        // Preserve relative references in unchanged materials/models/sounds by retaining their source hierarchy.
        var dependencies = audit.Resources.SelectMany(r => r.References)
            .Where(r => (r.Reference.Owner != discovery.EffectPath || r.Reference.Type != ResourceType.Texture)
                && r.Audit.Exists && !r.Audit.IsAbsolute && !r.Audit.HasLink && r.Audit.ResolvedPath is not null).ToArray();
        // Missing editor-only paths can point to distant former projects. They have nothing
        // to copy and must not pull the package hierarchy up to an unrelated drive ancestor.
        var common = effectDirectory;
        foreach (var dependency in dependencies)
            while (!WindowsPathResolver.IsInside(dependency.Audit.ResolvedPath!, common))
                common = Path.GetDirectoryName(common) ?? throw new ArgumentException("Cannot preserve dependency hierarchy.");
        var output = Path.Combine(packageRoot, Path.GetRelativePath(common, discovery.EffectPath));
        var copies = new List<ResourceCopy>();
        foreach (var dependency in dependencies.DistinctBy(r => r.Audit.ResolvedPath, StringComparer.OrdinalIgnoreCase))
        {
            var source = dependency.Audit.ResolvedPath!;
            if (source.Equals(discovery.EffectPath, StringComparison.OrdinalIgnoreCase)) continue;
            copies.Add(new(source, await HashAsync(source, token), Path.Combine(packageRoot, Path.GetRelativePath(common, source))));
        }
        var plan = await PlanAtAsync(discovery, selections, mode, textureFolderName, Path.GetFileName(output),
            Path.GetDirectoryName(output)!, writeMode, packageRoot, copies, token);
        var diagnostics = plan.Diagnostics.ToList();
        if (Directory.Exists(packageRoot) || File.Exists(packageRoot)) diagnostics.Add(new("PackageExists", "โฟลเดอร์ของ Effect นี้มีอยู่แล้ว เลือกโฟลเดอร์ผลลัพธ์ใหม่", true));
        if (audit.Resources.Any(r => r.References.Any(a => (a.Reference.Owner != discovery.EffectPath || a.Reference.Type != ResourceType.Texture)
                && (a.Audit.IsAbsolute || !a.Audit.Exists))))
            diagnostics.Add(new("OtherDependencies", "ทรัพยากรอื่นบางรายการยังหายหรือใช้ path แบบเต็ม ดูรายละเอียดผลตรวจหลังทำงาน"));
        foreach (var copy in copies)
            if (plan.Items.Any(i => i.TargetPath.Equals(copy.TargetPath, StringComparison.OrdinalIgnoreCase) && i.SourceHash != copy.SourceHash))
                diagnostics.Add(new("DependencyCollision", "Texture ที่ Map ชนกับทรัพยากรอื่น: " + copy.TargetPath, true));
        return plan with { Diagnostics = diagnostics };
    }

    public static IReadOnlyList<Diagnostic> ValidateBatchPlans(IEnumerable<TextureMappingPlan> plans)
    {
        var items = plans.ToArray();
        var diagnostics = new List<Diagnostic>();
        foreach (var group in items.SelectMany(p => p.Items).GroupBy(i => i.TargetPath, StringComparer.OrdinalIgnoreCase))
            if (group.Select(i => i.SourceHash).Distinct().Count() > 1)
                diagnostics.Add(new("BatchTextureCollision", "Texture จากคนละ Effect ชนชื่อปลายทาง ใช้โฟลเดอร์แยกหรือเปลี่ยนชื่อ: " + group.Key, true));
        if (items.Select(p => p.OutputEffectPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Length)
            diagnostics.Add(new("BatchOutputCollision", "Effect หลายไฟล์ใช้ตำแหน่งผลลัพธ์เดียวกัน", true));
        for (var i = 0; i < items.Length; i++)
            for (var j = i + 1; j < items.Length; j++)
            {
                var a = items[i].PackageRoot;
                var b = items[j].PackageRoot;
                if (a is not null && b is not null && (WindowsPathResolver.IsInside(a, b) || WindowsPathResolver.IsInside(b, a)))
                    diagnostics.Add(new("BatchPackageOverlap", "โฟลเดอร์ผลลัพธ์ของ Effect ซ้อนกัน: " + a + " / " + b, true));
            }
        return diagnostics;
    }

    private async Task<TextureMappingPlan> PlanAtAsync(TextureDiscovery discovery, IEnumerable<TextureSelection> selections,
        TextureMappingMode mode, string textureFolderName, string outputFileName, string effectDirectory,
        EffectWriteMode writeMode, string? packageRoot, IReadOnlyList<ResourceCopy>? additionalCopies, CancellationToken token)
    {
        ValidateLeaf(outputFileName);
        if (!outputFileName.EndsWith(".efkefc", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Output filename must end in .efkefc.");
        var output = Path.Combine(effectDirectory, outputFileName);
        var id = Guid.NewGuid().ToString("N");
        var backup = writeMode == EffectWriteMode.ReplaceOriginal ? discovery.EffectPath + ".backup-" + id + ".bak" : null;
        var journal = writeMode == EffectWriteMode.ReplaceOriginal ? output + ".mapping-" + id + ".json" : output + ".mapping.json";
        var diagnostics = discovery.Diagnostics.ToList();
        if ((writeMode != EffectWriteMode.ReplaceOriginal && File.Exists(output)) || Directory.Exists(output) || File.Exists(journal))
            diagnostics.Add(new("OutputExists", "Output or mapping manifest already exists. Choose another output filename.", true));
        string? textureDirectory = null;
        if (mode == TextureMappingMode.CopyIntoProject)
        {
            ValidateLeaf(textureFolderName);
            textureDirectory = Path.Combine(effectDirectory, textureFolderName);
            if (File.Exists(textureDirectory)) diagnostics.Add(new("FolderConflict", "Texture folder name is already occupied by a file.", true));
        }
        var selected = selections.ToDictionary(s => s.OriginalReference, StringComparer.Ordinal);
        var items = new List<TextureMappingItem>();
        foreach (var request in discovery.Requests)
        {
            token.ThrowIfCancellationRequested();
            if (!selected.TryGetValue(request.OriginalReference, out var choice) || choice.CandidatePath is null)
            {
                diagnostics.Add(new(request.Required ? "UnmatchedRuntimeTexture" : "SkippedEditorTexture",
                    $"{request.FileName}: {(request.Candidates.Count > 1 ? "choose a candidate" : "no candidate selected")}; {(request.Required ? "required texture" : "editor-only field left unchanged")}", request.Required));
                continue;
            }
            var source = Path.GetFullPath(choice.CandidatePath);
            if (!request.Candidates.Any(c => c.Path.Equals(source, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Selection must be one of the filename candidates within the selected resource folder.");
            if (!WindowsPathResolver.IsInside(source, discovery.SearchRoot) || WindowsPathResolver.HasReparsePoint(source))
                throw new ArgumentException("Selected texture leaves the resource folder or uses a link.");
            var hash = await HashAsync(source, token);
            var target = source;
            var copy = false;
            if (textureDirectory is not null)
            {
                var destinationName = string.IsNullOrWhiteSpace(choice.DestinationName) ? Path.GetFileName(source) : choice.DestinationName;
                ValidateLeaf(destinationName);
                if (!Path.GetExtension(destinationName).Equals(Path.GetExtension(source), StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Destination texture extension must match the source.");
                target = Path.Combine(textureDirectory, destinationName);
                if (Directory.Exists(target)) diagnostics.Add(new("DestinationConflict", $"Destination is a directory: {target}", true));
                else if (File.Exists(target))
                {
                    if (await HashAsync(target, token) != hash) diagnostics.Add(new("DestinationConflict", $"Destination has different content: {target}", true));
                }
                else copy = true;
            }
            if (WindowsPathResolver.HasReparsePoint(target)) diagnostics.Add(new("LinkedTarget", $"Destination uses a link: {target}", true));
            var relative = Path.GetRelativePath(effectDirectory, target).Replace('\\', '/');
            // Cross-drive links cannot be relative, so make the external nature explicit.
            if (Path.IsPathFullyQualified(relative)) diagnostics.Add(new("AbsoluteLink", $"Cross-drive reference remains absolute: {target}"));
            items.Add(new(request.OriginalReference, source, hash, target, relative, copy));
        }
        foreach (var group in items.GroupBy(i => i.TargetPath, StringComparer.OrdinalIgnoreCase))
            if (group.Select(i => i.SourceHash).Distinct().Count() > 1)
                diagnostics.Add(new("NameCollision", $"Different textures would occupy {group.Key}. Rename a destination in the preview.", true));
        if (items.Count == 0) diagnostics.Add(new("NoMappings", "No selected textures can be mapped.", true));
        return new(discovery.EffectPath, discovery.EffectHash, discovery.SearchRoot, output, mode, textureDirectory, items, diagnostics,
            writeMode, packageRoot, backup, journal, additionalCopies);
    }

    public async Task<TextureMappingResult> ApplyAsync(TextureMappingPlan plan, IProgress<string>? progress = null, CancellationToken token = default)
    {
        if (!plan.CanApply) throw new InvalidOperationException("Resolve mapping conflicts before Apply.");
        var sourceDirectory = Path.GetDirectoryName(plan.EffectPath)!;
        var directory = Path.GetDirectoryName(plan.OutputEffectPath)!;
        var replacing = plan.WriteMode == EffectWriteMode.ReplaceOriginal;
        if (plan.WriteMode == EffectWriteMode.NewCopy && !directory.Equals(sourceDirectory, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Mapped copies must stay beside the original effect to preserve other relative references.");
        if (replacing && (!plan.OutputEffectPath.Equals(plan.EffectPath, StringComparison.OrdinalIgnoreCase)
            || plan.BackupPath is null || !Path.GetDirectoryName(plan.BackupPath)!.Equals(sourceDirectory, StringComparison.OrdinalIgnoreCase)
            || File.Exists(plan.BackupPath) || WindowsPathResolver.HasReparsePoint(plan.BackupPath)))
            throw new ArgumentException("Invalid original-file backup destination.");
        if (plan.WriteMode == EffectWriteMode.SeparateFolder && (plan.PackageRoot is null
            || !WindowsPathResolver.IsInside(plan.OutputEffectPath, plan.PackageRoot)
            || WindowsPathResolver.HasReparsePoint(plan.PackageRoot) || Directory.Exists(plan.PackageRoot) || File.Exists(plan.PackageRoot)))
            throw new ArgumentException("Choose a new, ordinary package folder.");
        var manifest = plan.JournalPath ?? plan.OutputEffectPath + ".mapping.json";
        if (!Path.GetDirectoryName(manifest)!.Equals(directory, StringComparison.OrdinalIgnoreCase) || WindowsPathResolver.HasReparsePoint(manifest))
            throw new ArgumentException("Invalid mapping journal path.");
        ValidateLeaf(Path.GetFileName(plan.OutputEffectPath));
        if ((!replacing && File.Exists(plan.OutputEffectPath)) || Directory.Exists(plan.OutputEffectPath) || File.Exists(manifest))
            throw new IOException("Output already exists; it will not be overwritten.");
        if (WindowsPathResolver.HasReparsePoint(plan.OutputEffectPath)) throw new IOException("Output contains a link.");
        if (plan.TextureDirectory is not null && (Path.GetDirectoryName(plan.TextureDirectory) != directory || WindowsPathResolver.HasReparsePoint(plan.TextureDirectory)))
            throw new ArgumentException("Texture folder must be an ordinary child folder beside the effect.");
        if (await HashAsync(plan.EffectPath, token) != plan.EffectHash) throw new IOException("Effect changed since preview. Scan and preview again.");
        foreach (var item in plan.Items)
        {
            token.ThrowIfCancellationRequested();
            if (!WindowsPathResolver.IsInside(item.SourcePath, plan.SearchRoot) || WindowsPathResolver.HasReparsePoint(item.SourcePath)
                || await HashAsync(item.SourcePath, token) != item.SourceHash) throw new IOException("Source texture changed or left the search folder: " + item.SourcePath);
            var expected = plan.Mode == TextureMappingMode.LinkExisting ? item.SourcePath
                : Path.Combine(plan.TextureDirectory ?? throw new ArgumentException("Missing texture directory."), Path.GetFileName(item.TargetPath));
            if (!expected.Equals(item.TargetPath, StringComparison.OrdinalIgnoreCase) || WindowsPathResolver.HasReparsePoint(item.TargetPath)
                || Path.GetRelativePath(directory, item.TargetPath).Replace('\\', '/') != item.NewReference)
                throw new ArgumentException("Mapping destination does not match the preview mode.");
            if (File.Exists(item.TargetPath) && await HashAsync(item.TargetPath, token) != item.SourceHash)
                throw new IOException("Destination changed since preview: " + item.TargetPath);
        }
        var additionalCopies = plan.AdditionalCopies ?? [];
        foreach (var copy in additionalCopies)
        {
            if (plan.WriteMode != EffectWriteMode.SeparateFolder || plan.PackageRoot is null
                || !WindowsPathResolver.IsInside(copy.TargetPath, plan.PackageRoot) || WindowsPathResolver.HasReparsePoint(copy.TargetPath)
                || WindowsPathResolver.HasReparsePoint(copy.SourcePath) || File.Exists(copy.TargetPath)
                || await HashAsync(copy.SourcePath, token) != copy.SourceHash)
                throw new IOException("Additional resource changed or has invalid destination: " + copy.SourcePath);
        }
        var source = await File.ReadAllBytesAsync(plan.EffectPath, token);
        var document = await parser.ParseAsync(plan.EffectPath, token);
        if (Convert.ToHexString(SHA256.HashData(source)) != plan.EffectHash || await HashAsync(plan.EffectPath, token) != plan.EffectHash)
            throw new IOException("Effect changed while preparing the mapped copy.");
        var mappings = plan.Items.ToDictionary(i => i.OriginalReference, i => i.NewReference, StringComparer.Ordinal);
        var rewritten = await Task.Run(() => new TextureReferenceWriter().Rewrite(source, document, mappings), token);
        var packageCreated = false;
        if (plan.PackageRoot is not null) { Directory.CreateDirectory(directory); packageCreated = true; }
        var stage = Path.Combine(directory, ".resource-map-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        var stageEffect = Path.Combine(stage, Path.GetFileName(plan.OutputEffectPath));
        var created = new List<(string Path, string Hash)>();
        var createdDirectory = false;
        var committed = false;
        var rolledBack = false;
        var replaced = false;
        var rewrittenHash = Convert.ToHexString(SHA256.HashData(rewritten));
        try
        {
            await File.WriteAllBytesAsync(stageEffect, rewritten, token);
            await File.WriteAllTextAsync(Path.Combine(stage, "plan.json"), JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true }), token);
            var parsed = await parser.ParseAsync(stageEffect, token);
            ValidateReferences(document, parsed, mappings);
            if (plan.TextureDirectory is not null && !Directory.Exists(plan.TextureDirectory))
            { Directory.CreateDirectory(plan.TextureDirectory); createdDirectory = true; }
            foreach (var copy in additionalCopies)
            {
                token.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(copy.TargetPath)!);
                var stagedCopy = Path.Combine(stage, Guid.NewGuid().ToString("N"));
                await using (var input = new FileStream(copy.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                await using (var output = new FileStream(stagedCopy, FileMode.CreateNew, FileAccess.Write))
                    await input.CopyToAsync(output, token);
                if (await HashAsync(stagedCopy, token) != copy.SourceHash) throw new IOException("Additional resource integrity check failed.");
                File.Move(stagedCopy, copy.TargetPath, overwrite: false);
                created.Add((copy.TargetPath, copy.SourceHash));
                progress?.Report("Copied resource " + Path.GetFileName(copy.TargetPath));
            }
            foreach (var item in plan.Items.DistinctBy(i => i.TargetPath, StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                if (File.Exists(item.TargetPath)) continue; // Hash already checked; reuse only identical content.
                var stageTexture = Path.Combine(stage, Guid.NewGuid().ToString("N"));
                await using (var input = new FileStream(item.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                await using (var output = new FileStream(stageTexture, FileMode.CreateNew, FileAccess.Write))
                    await input.CopyToAsync(output, token);
                if (await HashAsync(stageTexture, token) != item.SourceHash) throw new IOException("Copied texture integrity check failed.");
                File.Move(stageTexture, item.TargetPath, overwrite: false);
                created.Add((item.TargetPath, item.SourceHash));
                progress?.Report("Copied " + Path.GetFileName(item.TargetPath));
            }
            token.ThrowIfCancellationRequested();
            // Resolve against final owner location, not the staging directory.
            foreach (var item in plan.Items)
                if (!new WindowsPathResolver().Audit(plan.OutputEffectPath, item.NewReference, directory).Exists
                    || await HashAsync(item.TargetPath, token) != item.SourceHash) throw new IOException("Final target validation failed.");
            if (await HashAsync(plan.EffectPath, token) != plan.EffectHash) throw new IOException("Original effect changed while applying.");
            progress?.Report("Committing mapped copy");
            token.ThrowIfCancellationRequested();
            if (replacing)
            {
                // Windows replacement is atomic and produces the backup in the same operation.
                File.Replace(stageEffect, plan.OutputEffectPath, plan.BackupPath, ignoreMetadataErrors: false);
                replaced = true;
                if (await HashAsync(plan.BackupPath!, token) != plan.EffectHash) throw new IOException("Backup integrity check failed.");
                progress?.Report("Replaced original; backup: " + plan.BackupPath);
            }
            else
            {
                File.Move(stageEffect, plan.OutputEffectPath, overwrite: false);
                created.Add((plan.OutputEffectPath, rewrittenHash));
            }
            var journal = Path.Combine(stage, "plan.json");
            var journalHash = await HashAsync(journal, token);
            File.Move(journal, manifest, overwrite: false);
            created.Add((manifest, journalHash));
            var audit = await new ResourceScanner(parser, new WindowsPathResolver()).ScanAsync([plan.OutputEffectPath], directory, cancellationToken: token);
            ValidateReferences(document, audit.Documents.First(), mappings);
            committed = true;
            return new(plan.OutputEffectPath, manifest,
                created.Count(f => f.Path != plan.OutputEffectPath && f.Path != manifest && !additionalCopies.Any(c => c.TargetPath == f.Path)),
                plan.Items.Count, audit, plan.BackupPath);
        }
        catch
        {
            var recoveryErrors = new List<string>();
            if (replaced)
            {
                try
                {
                    if (WindowsPathResolver.HasReparsePoint(plan.OutputEffectPath) || WindowsPathResolver.HasReparsePoint(plan.BackupPath!)
                        || await HashAsync(plan.OutputEffectPath, CancellationToken.None) != rewrittenHash
                        || await HashAsync(plan.BackupPath!, CancellationToken.None) != plan.EffectHash)
                        recoveryErrors.Add("Original or backup changed; backup retained: " + plan.BackupPath);
                    else File.Replace(plan.BackupPath!, plan.OutputEffectPath, null);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { recoveryErrors.Add(ex.Message); }
            }
            if (recoveryErrors.Count > 0) throw new IOException($"Recovery required. Files and preview manifest retained at {stage}: " + string.Join("; ", recoveryErrors));
            foreach (var file in created.AsEnumerable().Reverse())
            {
                try
                {
                    if (!File.Exists(file.Path)) continue;
                    if (WindowsPathResolver.HasReparsePoint(file.Path) || await HashAsync(file.Path, CancellationToken.None) != file.Hash)
                        recoveryErrors.Add("Changed file retained: " + file.Path);
                    else File.Delete(file.Path);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { recoveryErrors.Add(ex.Message); }
            }
            if (recoveryErrors.Count > 0) throw new IOException($"Recovery required. Preview manifest retained at {stage}: " + string.Join("; ", recoveryErrors));
            if (createdDirectory && plan.TextureDirectory is not null && !Directory.EnumerateFileSystemEntries(plan.TextureDirectory).Any()) Directory.Delete(plan.TextureDirectory);
            rolledBack = true;
            throw;
        }
        finally
        {
            // Only our own staging directory is removed; source/output folders are never recursively removed.
            if ((committed || rolledBack) && Directory.Exists(stage) && !WindowsPathResolver.HasReparsePoint(stage)) Directory.Delete(stage, true);
            if (rolledBack) progress?.Report("Operation rolled back; original effect unchanged.");
            if (rolledBack && packageCreated && plan.PackageRoot is not null)
                RemoveEmptyDirectories(plan.PackageRoot);
        }
    }

    private static void RemoveEmptyDirectories(string root)
    {
        if (!Directory.Exists(root) || WindowsPathResolver.HasReparsePoint(root)) return;
        foreach (var child in Directory.EnumerateDirectories(root)) RemoveEmptyDirectories(child);
        if (!Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
    }

    private static void ValidateReferences(ParsedDocument before, ParsedDocument after, IReadOnlyDictionary<string, string> mappings)
    {
        if (!TextureReferenceWriter.Supports(after)) throw new InvalidDataException("Rewritten effect failed parser validation.");
        var newReferences = after.References.ToDictionary(r => r.Locator, StringComparer.Ordinal);
        foreach (var old in before.References)
        {
            var expected = old.Type == ResourceType.Texture && mappings.TryGetValue(old.OriginalReference, out var path) ? path : old.OriginalReference;
            if (!newReferences.TryGetValue(old.Locator, out var updated) || updated.OriginalReference != expected || updated.Type != old.Type)
                throw new InvalidDataException("Reference verification failed at " + old.Locator);
        }
        if (newReferences.Count != before.References.Count) throw new InvalidDataException("Reference count changed unexpectedly.");
    }

    public static async Task<string> HashAsync(string path, CancellationToken token = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
    }

    internal static void ValidateLeaf(string name)
    {
        var stem = name.Split('.')[0].ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || Path.GetFileName(name) != name
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith(' ') || name.EndsWith('.')
            || stem is "CON" or "PRN" or "AUX" or "NUL" || System.Text.RegularExpressions.Regex.IsMatch(stem, "^(COM|LPT)[1-9]$"))
            throw new ArgumentException("Use a single valid Windows filename/folder name: " + name);
    }
}
