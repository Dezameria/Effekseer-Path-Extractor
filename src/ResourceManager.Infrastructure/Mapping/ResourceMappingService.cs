using System.Security.Cryptography;
using System.Text.Json;
using ResourceManager.Core.Models;
using ResourceManager.Core.Scanning;
using ResourceManager.Infrastructure.Effekseer;
using ResourceManager.Infrastructure.FileSystem;

namespace ResourceManager.Infrastructure.Mapping;

/// <summary>Links existing assets or creates a complete package with rewritten material copies.</summary>
public sealed class ResourceMappingService
{
    private readonly EffekseerResourceParser parser = new();
    private readonly WindowsPathResolver resolver = new();

    public async Task<TextureDiscovery> DiscoverAsync(string effectPath, string searchRoot, CancellationToken token = default)
    {
        searchRoot = Path.GetFullPath(searchRoot);
        var index = await Task.Run(() => DocumentDiscovery.Enumerate(searchRoot, _ => true, token), token);
        return await DiscoverIndexedAsync(effectPath, searchRoot, index, token);
    }

    private async Task<TextureDiscovery> DiscoverIndexedAsync(string effectPath, string searchRoot, DiscoveryResult index, CancellationToken token,
        ILookup<string, SearchCandidate>? sharedCandidates = null)
    {
        effectPath = Path.GetFullPath(effectPath);
        var hash = await TextureMappingService.HashAsync(effectPath, token);
        var effect = await parser.ParseAsync(effectPath, token);
        var compatibility = EffectFormatCompatibility.Evaluate(effect);
        if (!compatibility.CanMap) throw new InvalidDataException(compatibility.Rejection(effect));
        var lookup = sharedCandidates ?? index.Files.Select(p => new SearchCandidate(p, new FileInfo(p).Length)).ToLookup(p => Path.GetFileName(p.Path), StringComparer.OrdinalIgnoreCase);
        var requests = new List<TextureRequest>();
        var documents = new List<SourceDocument> { new(effect, hash) };
        var diagnostics = index.Diagnostics.ToList();
        void AddRequests(ParsedDocument document, bool nested)
        {
            foreach (var group in document.References.GroupBy(r => (r.Type, r.OriginalReference)))
            {
                var type = group.Key.Type;
                if (type is not (ResourceType.Texture or ResourceType.Material or ResourceType.Model or ResourceType.Sound or ResourceType.Curve))
                { diagnostics.Add(new("UnsupportedResource", $"Unsupported resource type: {group.Key.OriginalReference}", true)); continue; }
                var old = group.Key.OriginalReference;
                var name = Path.GetFileName(old.Replace('\\', '/'));
                var required = nested || group.Any(r => r.Role is ReferenceRole.Runtime or ReferenceRole.Metadata or ReferenceRole.CompatibilityRuntime)
                    || type == ResourceType.Model && old.EndsWith(".glb", StringComparison.OrdinalIgnoreCase);
                var candidates = lookup[name].ToArray();
                // A valid existing reference identifies its file even if other filenames are duplicated.
                var resolved = resolver.Audit(document.Path, old, searchRoot);
                if (resolved.Exists && resolved.InsideBoundary && !resolved.HasLink && resolved.ResolvedPath is { } exact
                    && candidates.Any(c => c.Path.Equals(exact, StringComparison.OrdinalIgnoreCase)))
                    candidates = candidates.Where(c => c.Path.Equals(exact, StringComparison.OrdinalIgnoreCase)).ToArray();
                requests.Add(new(old, name, required, candidates, type, nested ? document.Path : null));
            }
        }
        AddRequests(effect, false);
        foreach (var source in requests.Where(r => r.Type == ResourceType.Material).SelectMany(r => r.Candidates).Select(c => c.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
        {
            token.ThrowIfCancellationRequested();
            var materialHash = await TextureMappingService.HashAsync(source, token);
            var material = await parser.ParseAsync(source, token);
            documents.Add(new(material, materialHash));
            if (material.Format == "EFKM" && !material.Diagnostics.Any(d => d.IsError)) AddRequests(material, true);
            if (await TextureMappingService.HashAsync(source, token) != materialHash) throw new IOException("Material changed while matching: " + source);
        }
        if (await TextureMappingService.HashAsync(effectPath, token) != hash) throw new IOException("Effect changed while matching.");
        return new(effectPath, searchRoot, hash, requests.OrderBy(r => r.OwnerPath is null ? 0 : 1).ThenBy(r => r.Type).ThenBy(r => r.FileName).ToArray(),
            diagnostics, effect.EditorVersion, compatibility.Profile!.Name, documents);
    }

    public async Task<BatchDiscovery> DiscoverBatchAsync(string input, string searchRoot, string? excludedOutputRoot = null,
        IProgress<string>? progress = null, CancellationToken token = default)
    {
        input = Path.GetFullPath(input); searchRoot = Path.GetFullPath(searchRoot);
        if (!Directory.Exists(searchRoot)) throw new ArgumentException("เลือกโฟลเดอร์คลัง Asset ที่มีอยู่");
        if (!Directory.Exists(input) && (!File.Exists(input) || !input.EndsWith(".efkefc", StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("เลือก Effect หรือโฟลเดอร์ Effect");
        // A previously exported folder may now be the explicitly selected input/library.
        // Exclude output descendants during broad scans, never the selected scope itself.
        var effectExclusion = excludedOutputRoot is not null && !WindowsPathResolver.IsInside(input, excludedOutputRoot) ? excludedOutputRoot : null;
        var libraryExclusion = excludedOutputRoot is not null && !WindowsPathResolver.IsInside(searchRoot, excludedOutputRoot) ? excludedOutputRoot : null;
        var files = Directory.Exists(input) ? await Task.Run(() => DocumentDiscovery.Enumerate(input, p => p.EndsWith(".efkefc", StringComparison.OrdinalIgnoreCase), token, effectExclusion), token) : new DiscoveryResult([input], []);
        var index = await Task.Run(() => DocumentDiscovery.Enumerate(searchRoot, _ => true, token, libraryExclusion), token);
        var candidates = await Task.Run(() => index.Files.Select(p => new SearchCandidate(p, new FileInfo(p).Length))
            .ToLookup(p => Path.GetFileName(p.Path), StringComparer.OrdinalIgnoreCase), token);
        var effects = new List<EffectDiscoveryCase>();
        foreach (var file in files.Files)
        {
            progress?.Report($"ค้น Texture / Material / Model: {Path.GetFileName(file)}");
            try { effects.Add(new(file, await DiscoverIndexedAsync(file, searchRoot, index, token, candidates), null)); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException or InvalidOperationException)
            { effects.Add(new(file, null, ex.Message)); }
        }
        return new(input, searchRoot, effects, files.Diagnostics);
    }

    public async Task<TextureMappingPlan> PlanAdvancedAsync(TextureDiscovery discovery, IEnumerable<TextureSelection> selections,
        TextureMappingMode mode, string textureFolderName, EffectWriteMode writeMode, string inputRoot, string outputRoot, CancellationToken token = default)
    {
        if (writeMode == EffectWriteMode.NewCopy) throw new ArgumentException("Full repair supports a separate package or an original-file replacement.");
        if (mode != (writeMode == EffectWriteMode.ReplaceOriginal ? TextureMappingMode.LinkExisting : TextureMappingMode.CopyIntoProject))
            throw new ArgumentException("ใช้ไฟล์ที่มีอยู่เพื่อแก้ Effect เดิม หรือเลือกสร้างชุดใหม่เพื่อคัดลอก Asset");
        if (mode == TextureMappingMode.CopyIntoProject) TextureMappingService.ValidateLeaf(textureFolderName);
        var diagnostics = discovery.Diagnostics.ToList();
        inputRoot = Path.GetFullPath(inputRoot); if (File.Exists(inputRoot)) inputRoot = Path.GetDirectoryName(inputRoot)!;
        var relative = Path.GetRelativePath(inputRoot, discovery.EffectPath);
        if (Path.IsPathFullyQualified(relative) || relative.Split(Path.DirectorySeparatorChar).Contains("..")) throw new ArgumentException("Effect is outside input root.");
        var id = Guid.NewGuid().ToString("N");
        var packageRoot = writeMode == EffectWriteMode.SeparateFolder ? Path.Combine(Path.GetFullPath(outputRoot), Path.ChangeExtension(relative, null) + ".assets") : null;
        if (packageRoot is not null && WindowsPathResolver.IsInside(discovery.EffectPath, outputRoot)) throw new ArgumentException("Choose an output folder separate from input.");
        var output = packageRoot is null ? discovery.EffectPath : Path.Combine(packageRoot, Path.GetFileName(discovery.EffectPath));
        var resourceRoot = packageRoot;
        var journal = output + ".mapping-" + id + ".json";
        if (resourceRoot is not null && (Directory.Exists(resourceRoot) || File.Exists(resourceRoot) || WindowsPathResolver.HasReparsePoint(resourceRoot))) diagnostics.Add(new("PackageExists", "โฟลเดอร์ผลลัพธ์มีอยู่แล้วหรือเป็น link", true));
        var chosen = selections.ToDictionary(s => s.OriginalReference, StringComparer.Ordinal);
        var documents = discovery.Documents ?? throw new ArgumentException("Find all resources again.");
        var activeMaterials = discovery.Requests.Where(r => r.OwnerPath is null && r.Type == ResourceType.Material)
            .Select(r => chosen.GetValueOrDefault(r.SelectionKey)?.CandidatePath).Where(p => p is not null).Select(p => Path.GetFullPath(p!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = new List<TextureMappingItem>();
        foreach (var request in discovery.Requests.Where(r => r.OwnerPath is null || activeMaterials.Contains(r.OwnerPath)))
        {
            token.ThrowIfCancellationRequested();
            var choice = chosen.GetValueOrDefault(request.SelectionKey);
            if (choice?.CandidatePath is null)
            {
                diagnostics.Add(new(request.Required ? "UnmatchedResource" : "SkippedEditorResource",
                    $"{request.FileName} ({request.Type}, {Path.GetFileName(request.OwnerPath ?? discovery.EffectPath)}): {(request.Candidates.Count > 1 ? "ชื่อซ้ำ ต้องเลือกไฟล์" : "ไม่พบไฟล์ที่เลือก")}{(request.Required ? "" : " · เฉพาะ Editor คง path เดิมไว้")}", request.Required));
                continue;
            }
            var source = Path.GetFullPath(choice.CandidatePath);
            if (!request.Candidates.Any(c => c.Path.Equals(source, StringComparison.OrdinalIgnoreCase)) || !WindowsPathResolver.IsInside(source, discovery.SearchRoot) || WindowsPathResolver.HasReparsePoint(source))
                throw new ArgumentException("Candidate must be an ordinary file within the selected library.");
            var hash = await TextureMappingService.HashAsync(source, token);
            var folder = request.Type switch { ResourceType.Texture => textureFolderName, ResourceType.Material => "Materials", ResourceType.Model => "Models", ResourceType.Sound => "Sounds", ResourceType.Curve => "Curves", _ => throw new InvalidDataException("Unsupported resource.") };
            var leaf = mode == TextureMappingMode.LinkExisting || string.IsNullOrWhiteSpace(choice.DestinationName) ? Path.GetFileName(source) : choice.DestinationName;
            TextureMappingService.ValidateLeaf(leaf);
            if (!Path.GetExtension(leaf).Equals(Path.GetExtension(source), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Destination extension must match source.");
            if (request.Type != ResourceType.Texture && leaf != Path.GetFileName(source)) throw new ArgumentException("Model/material names stay unchanged so source/runtime model pairs remain aligned.");
            var copy = mode == TextureMappingMode.CopyIntoProject;
            var target = copy ? Path.Combine(resourceRoot!, folder, leaf) : source;
            if (request.Type == ResourceType.Material)
            {
                var snapshot = documents.Single(d => d.Document.Path.Equals(source, StringComparison.OrdinalIgnoreCase));
                if (snapshot.Document.Format != "EFKM" || snapshot.Document.Diagnostics.Any(d => d.IsError)
                    || copy && !MaterialReferenceWriter.Supports(snapshot.Document)) diagnostics.Add(new("UnsupportedMaterial", "Material นี้ยังอ่านหรือจัดชุดไม่ได้: " + source, true));
                if (snapshot.Hash != hash) diagnostics.Add(new("ChangedMaterial", "Material เปลี่ยนหลังค้นหา: " + source, true));
            }
            if (request.Type == ResourceType.Model)
            {
                var model = await parser.ParseAsync(source, token);
                if (model.Diagnostics.Any(d => d.IsError) || model.Format is not ("GLB" or "EFKMODEL") || model.References.Count > 0)
                    diagnostics.Add(new("UnsupportedModel", "โมเดลมีโครงสร้างที่ไม่รองรับ หรือ GLB ใช้ไฟล์ภายนอก: " + source, true));
            }
            items.Add(new(request.OriginalReference, source, hash, target, "", copy, request.Type, request.OwnerPath));
        }
        var edits = new List<ResourceDocumentEdit>();
        foreach (var material in items.Where(i => i.OwnerPath is null && i.Type == ResourceType.Material).DistinctBy(i => i.SourcePath, StringComparer.OrdinalIgnoreCase).ToArray())
        {
            var dependent = items.Where(i => i.OwnerPath?.Equals(material.SourcePath, StringComparison.OrdinalIgnoreCase) == true).ToArray();
            foreach (var item in dependent)
            {
                var reference = material.CopyRequired ? Relative(material.TargetPath, item.TargetPath) : item.OriginalReference;
                if (!material.CopyRequired)
                {
                    var existing = resolver.Audit(material.SourcePath, reference, discovery.SearchRoot);
                    if (!existing.Exists || existing.HasLink || !string.Equals(existing.ResolvedPath, item.TargetPath, StringComparison.OrdinalIgnoreCase))
                        diagnostics.Add(new("MaterialNeedsRepair", $"{Path.GetFileName(material.SourcePath)}: path ภายใน Material ไม่ตรงกับ Texture ที่เลือก ({item.OriginalReference}) · แก้ Material ก่อน หรือเลือกสร้างชุดใหม่", true));
                }
                items[items.IndexOf(item)] = item with { NewReference = reference };
            }
            var mappings = items.Where(i => i.OwnerPath?.Equals(material.SourcePath, StringComparison.OrdinalIgnoreCase) == true).ToDictionary(i => i.OriginalReference, i => i.NewReference);
            if (material.CopyRequired) edits.Add(new(material.SourcePath, material.SourceHash, material.TargetPath, mappings));
        }
        for (var i = 0; i < items.Count; i++) if (items[i].OwnerPath is null) items[i] = items[i] with { NewReference = Relative(output, items[i].TargetPath) };
        foreach (var group in items.GroupBy(i => i.TargetPath, StringComparer.OrdinalIgnoreCase))
            if (group.Select(i => i.SourceHash).Distinct().Count() > 1) diagnostics.Add(new("NameCollision", "ทรัพยากรต่างข้อมูลชนชื่อปลายทาง: " + group.Key, true));
        foreach (var compiled in items.Where(i => i.OwnerPath is null && i.SourcePath.EndsWith(".efkmodel", StringComparison.OrdinalIgnoreCase)))
        {
            var editor = items.FirstOrDefault(i => i.OwnerPath is null && i.OriginalReference == Path.ChangeExtension(compiled.OriginalReference, ".glb"));
            if (editor is not null && !Path.ChangeExtension(compiled.SourcePath, ".glb").Equals(editor.SourcePath, StringComparison.OrdinalIgnoreCase))
                diagnostics.Add(new("ModelPairMismatch", "GLB และ efkmodel ต้องมาจากคู่โมเดลเดียวกัน: " + compiled.OriginalReference, true));
        }
        if (items.Count == 0) diagnostics.Add(new("NoMappings", "ยังไม่มีทรัพยากรที่เลือก", true));
        if (mode == TextureMappingMode.LinkExisting) diagnostics.Add(new("ExternalLibrary", "ใช้ Asset ที่มีอยู่โดยตรงทุกชนิด ไม่คัดลอก Asset · ต้องเก็บคลังไว้ที่เดิม"));
        return new(discovery.EffectPath, discovery.EffectHash, discovery.SearchRoot, output, mode, resourceRoot is null ? null : Path.Combine(resourceRoot, textureFolderName), items, diagnostics,
            writeMode, packageRoot, packageRoot is null ? output + ".backup-" + id + ".bak" : null, journal, null, edits, true);
    }

    private static string Relative(string owner, string target) => Path.GetRelativePath(Path.GetDirectoryName(owner)!, target).Replace('\\', '/');

    public async Task<TextureMappingResult> ApplyAsync(TextureMappingPlan plan, IProgress<string>? progress = null, CancellationToken token = default)
    {
        if (!plan.AllResources || !plan.CanApply) throw new InvalidOperationException("Resolve resource conflicts and preview again.");
        var replacing = plan.WriteMode == EffectWriteMode.ReplaceOriginal;
        var directory = Path.GetDirectoryName(plan.OutputEffectPath)!;
        var resourceRoot = plan.Mode == TextureMappingMode.CopyIntoProject ? plan.PackageRoot : null;
        if (plan.WriteMode is not (EffectWriteMode.ReplaceOriginal or EffectWriteMode.SeparateFolder)
            || replacing && (plan.OutputEffectPath != plan.EffectPath || plan.BackupPath is null || Path.GetDirectoryName(plan.BackupPath) != directory)
            || !replacing && (plan.PackageRoot != directory || WindowsPathResolver.IsInside(plan.EffectPath, directory))
            || plan.Mode != (replacing ? TextureMappingMode.LinkExisting : TextureMappingMode.CopyIntoProject)
            || replacing && (plan.TextureDirectory is not null || plan.PackageRoot is not null || plan.DocumentEdits?.Count > 0)
            || !replacing && (resourceRoot is null || plan.TextureDirectory is null || Path.GetDirectoryName(plan.TextureDirectory) != resourceRoot
                || !WindowsPathResolver.IsInside(resourceRoot, directory) || Directory.Exists(resourceRoot) || File.Exists(resourceRoot))
            || plan.JournalPath is null || Path.GetDirectoryName(plan.JournalPath) != directory)
            throw new ArgumentException("Invalid resource package destination.");
        foreach (var target in new[] { plan.OutputEffectPath, resourceRoot, plan.JournalPath, plan.BackupPath }.Where(p => p is not null))
            if (WindowsPathResolver.HasReparsePoint(target!) || target != plan.EffectPath && (File.Exists(target) || Directory.Exists(target))) throw new IOException("Destination exists or uses a link: " + target);
        if (WindowsPathResolver.HasReparsePoint(plan.EffectPath) || await TextureMappingService.HashAsync(plan.EffectPath, token) != plan.EffectHash) throw new IOException("Effect changed since preview.");
        foreach (var item in plan.Items)
        {
            var shouldCopy = plan.Mode == TextureMappingMode.CopyIntoProject;
            var folder = item.Type switch { ResourceType.Texture => Path.GetFileName(plan.TextureDirectory!), ResourceType.Material => "Materials", ResourceType.Model => "Models", ResourceType.Sound => "Sounds", ResourceType.Curve => "Curves", _ => throw new ArgumentException("Unknown resource type.") };
            var expectedTarget = shouldCopy ? Path.Combine(resourceRoot!, folder, Path.GetFileName(item.TargetPath)) : item.SourcePath;
            if (!expectedTarget.Equals(item.TargetPath, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Resource destination differs from preview mode.");
            if (item.CopyRequired != shouldCopy || !WindowsPathResolver.IsInside(item.SourcePath, plan.SearchRoot) || WindowsPathResolver.HasReparsePoint(item.SourcePath)
                || WindowsPathResolver.HasReparsePoint(item.TargetPath) || shouldCopy && !WindowsPathResolver.IsInside(item.TargetPath, resourceRoot!)
                || !shouldCopy && !item.TargetPath.Equals(item.SourcePath, StringComparison.OrdinalIgnoreCase)
                || await TextureMappingService.HashAsync(item.SourcePath, token) != item.SourceHash) throw new IOException("Resource changed or has invalid destination: " + item.SourcePath);
        }
        var source = await File.ReadAllBytesAsync(plan.EffectPath, token);
        if (Convert.ToHexString(SHA256.HashData(source)) != plan.EffectHash) throw new IOException("Effect changed while reading.");
        var document = await parser.ParseAsync(plan.EffectPath, token);
        var mappings = plan.Items.Where(i => i.OwnerPath is null).ToDictionary(i => i.OriginalReference, i => i.NewReference);
        var rewritten = new TextureReferenceWriter().Rewrite(source, document, mappings, true);
        var prepared = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var edit in plan.DocumentEdits ?? [])
        {
            var selected = plan.Items.FirstOrDefault(i => i.OwnerPath is null && i.Type == ResourceType.Material && i.TargetPath == edit.TargetPath);
            if (selected is null || selected.SourceHash != edit.SourceHash || selected.SourcePath != edit.SourcePath) throw new ArgumentException("Material plan does not match selected resources.");
            var materialBytes = await File.ReadAllBytesAsync(edit.SourcePath, token);
            if (Convert.ToHexString(SHA256.HashData(materialBytes)) != edit.SourceHash) throw new IOException("Material changed since preview.");
            prepared.Add(edit.TargetPath, new MaterialReferenceWriter().Rewrite(materialBytes, await parser.ParseAsync(edit.SourcePath, token), edit.Mappings));
        }
        if (plan.Items.Where(i => i.Type == ResourceType.Material && i.OwnerPath is null && i.CopyRequired).Any(i => !prepared.ContainsKey(i.TargetPath))) throw new ArgumentException("Missing material rewrite plan.");
        if (plan.Mode == TextureMappingMode.LinkExisting)
            foreach (var item in plan.Items.Where(i => i.OwnerPath is not null))
            {
                if (!plan.Items.Any(m => m.OwnerPath is null && m.Type == ResourceType.Material && m.SourcePath == item.OwnerPath)
                    || item.NewReference != item.OriginalReference)
                    throw new ArgumentException("Existing material references must stay unchanged.");
                var existing = resolver.Audit(item.OwnerPath!, item.NewReference, plan.SearchRoot);
                if (!existing.Exists || existing.HasLink || !string.Equals(existing.ResolvedPath, item.TargetPath, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Existing material texture path is invalid: " + item.OwnerPath);
            }
        var stage = Path.Combine(Path.GetDirectoryName(directory)!, ".all-resource-map-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        var created = new List<(string Path, string Hash)>();
        var ownedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var replaced = false;
        var complete = false;
        var cleaned = false;
        var outputHash = Convert.ToHexString(SHA256.HashData(rewritten));
        void EnsureDirectory(string path)
        {
            if (Directory.Exists(path)) return;
            var parent = Path.GetDirectoryName(path); if (parent is not null) EnsureDirectory(parent);
            Directory.CreateDirectory(path); ownedDirectories.Add(path);
        }
        try
        {
            await File.WriteAllTextAsync(Path.Combine(stage, "plan.json"), JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true }), token);
            var stagedEffect = Path.Combine(stage, Path.GetFileName(plan.OutputEffectPath));
            await File.WriteAllBytesAsync(stagedEffect, rewritten, token);
            VerifyReferences(document, await parser.ParseAsync(stagedEffect, token), mappings);
            foreach (var item in plan.Items.Where(i => i.CopyRequired).DistinctBy(i => i.TargetPath, StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                EnsureDirectory(Path.GetDirectoryName(item.TargetPath)!);
                var file = Path.Combine(stage, Guid.NewGuid().ToString("N") + Path.GetExtension(item.SourcePath));
                string expected;
                if (prepared.TryGetValue(item.TargetPath, out var data))
                {
                    await File.WriteAllBytesAsync(file, data, token); expected = Convert.ToHexString(SHA256.HashData(data));
                    var edit = plan.DocumentEdits!.Single(e => e.TargetPath == item.TargetPath);
                    VerifyReferences(await parser.ParseAsync(edit.SourcePath, token), await parser.ParseAsync(file, token), edit.Mappings);
                }
                else
                {
                    await using var input = new FileStream(item.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    await using var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write);
                    await input.CopyToAsync(output, token); await output.FlushAsync(token);
                    expected = item.SourceHash;
                }
                if (await TextureMappingService.HashAsync(file, token) != expected) throw new IOException("Staged resource hash mismatch.");
                File.Move(file, item.TargetPath, false); created.Add((item.TargetPath, expected));
                progress?.Report("Copied resource " + Path.GetFileName(item.TargetPath));
            }
            foreach (var item in plan.Items)
            {
                var owner = item.OwnerPath is null ? plan.OutputEffectPath : plan.Mode == TextureMappingMode.LinkExisting ? item.OwnerPath
                    : plan.DocumentEdits!.Single(e => e.SourcePath == item.OwnerPath).TargetPath;
                if (!resolver.Audit(owner, item.NewReference, directory).ResolvedPath!.Equals(item.TargetPath, StringComparison.OrdinalIgnoreCase) || !File.Exists(item.TargetPath)) throw new IOException("Resource path validation failed.");
                if (await TextureMappingService.HashAsync(item.SourcePath, token) != item.SourceHash) throw new IOException("Resource changed while applying.");
            }
            foreach (var file in created) if (await TextureMappingService.HashAsync(file.Path, token) != file.Hash) throw new IOException("Output resource changed while applying.");
            if (await TextureMappingService.HashAsync(plan.EffectPath, token) != plan.EffectHash) throw new IOException("Effect changed while applying.");
            EnsureDirectory(directory);
            token.ThrowIfCancellationRequested();
            if (replacing)
            {
                File.Replace(stagedEffect, plan.OutputEffectPath, plan.BackupPath); replaced = true;
                if (await TextureMappingService.HashAsync(plan.BackupPath!, token) != plan.EffectHash) throw new IOException("Backup hash mismatch.");
                progress?.Report("Replaced original; backup: " + plan.BackupPath);
            }
            else { File.Move(stagedEffect, plan.OutputEffectPath, false); created.Add((plan.OutputEffectPath, outputHash)); }
            token.ThrowIfCancellationRequested();
            var audit = await new ResourceScanner(parser, resolver).ScanAsync([plan.OutputEffectPath], directory, cancellationToken: token);
            VerifyReferences(document, audit.Documents.First(d => d.Path == plan.OutputEffectPath), mappings);
            if (audit.Diagnostics.Any(d => d.IsError) || audit.Resources.Any(r => r.References.Any(a => a.Reference.Role is ReferenceRole.Runtime or ReferenceRole.Metadata or ReferenceRole.CompatibilityRuntime or ReferenceRole.MaterialDefault or ReferenceRole.MaterialEditor
                && !a.Audit.Exists))) throw new InvalidDataException("Required dependencies failed final inspection.");
            if (plan.Mode == TextureMappingMode.CopyIntoProject && audit.Resources.Any(r => r.References.Any(a => a.Audit.Exists && (!a.Audit.InsideBoundary || a.Audit.IsAbsolute))))
                throw new InvalidDataException("Copied package still refers to external resources.");
            var stageJournal = Path.Combine(stage, "plan.json"); var journalHash = await TextureMappingService.HashAsync(stageJournal, token);
            File.Move(stageJournal, plan.JournalPath, false); created.Add((plan.JournalPath, journalHash));
            complete = true;
            return new(plan.OutputEffectPath, plan.JournalPath, created.Count(f => f.Path != plan.OutputEffectPath && f.Path != plan.JournalPath),
                plan.Mode == TextureMappingMode.LinkExisting ? mappings.Count : plan.Items.Count, audit, plan.BackupPath);
        }
        catch
        {
            var recovery = new List<string>();
            if (replaced)
            {
                try
                {
                    if (WindowsPathResolver.HasReparsePoint(plan.OutputEffectPath) || WindowsPathResolver.HasReparsePoint(plan.BackupPath!)
                        || await TextureMappingService.HashAsync(plan.OutputEffectPath) != outputHash || await TextureMappingService.HashAsync(plan.BackupPath!) != plan.EffectHash)
                        recovery.Add("Original/backup changed; retained " + plan.BackupPath);
                    else File.Replace(plan.BackupPath!, plan.OutputEffectPath, null);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { recovery.Add(ex.Message); }
            }
            if (recovery.Count == 0) foreach (var file in created.AsEnumerable().Reverse())
            {
                try
                {
                    if (!File.Exists(file.Path)) continue;
                    if (WindowsPathResolver.HasReparsePoint(file.Path) || await TextureMappingService.HashAsync(file.Path) != file.Hash) recovery.Add("Changed output retained: " + file.Path);
                    else File.Delete(file.Path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { recovery.Add(ex.Message); }
            }
            if (recovery.Count > 0) throw new IOException("Recovery required. Files and preview retained at " + stage + ": " + string.Join("; ", recovery));
            foreach (var path in ownedDirectories.OrderByDescending(p => p.Length)) if (Directory.Exists(path) && !WindowsPathResolver.HasReparsePoint(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
            cleaned = true; throw;
        }
        finally
        { if ((complete || cleaned) && Directory.Exists(stage) && !WindowsPathResolver.HasReparsePoint(stage)) Directory.Delete(stage, true); }
    }

    private static void VerifyReferences(ParsedDocument before, ParsedDocument after, IReadOnlyDictionary<string, string> mappings)
    {
        if (after.Diagnostics.Any(d => d.IsError) || before.References.Count != after.References.Count) throw new InvalidDataException("Rewritten document failed structural validation.");
        var references = after.References.ToDictionary(r => r.Locator);
        foreach (var old in before.References)
            if (!references.TryGetValue(old.Locator, out var updated) || old.Type != updated.Type || updated.OriginalReference != mappings.GetValueOrDefault(old.OriginalReference, old.OriginalReference))
                throw new InvalidDataException("Reference validation failed at " + old.Locator);
    }
}
