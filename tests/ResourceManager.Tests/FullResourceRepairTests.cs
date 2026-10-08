using System.Buffers.Binary;
using System.Text.Json.Nodes;
using ResourceManager.Core.Models;
using ResourceManager.Core.Scanning;
using ResourceManager.Infrastructure.Effekseer;
using ResourceManager.Infrastructure.FileSystem;
using ResourceManager.Infrastructure.Mapping;

namespace ResourceManager.Tests;

public sealed class FullResourceRepairTests
{
    private static string MegaRoot => Environment.GetEnvironmentVariable("EFFEKSEER_VERSION_SAMPLE_ROOT")!;
    private static IEnumerable<TextureSelection> Choose(TextureDiscovery discovery) => discovery.Requests.Select(r => new TextureSelection(r.SelectionKey,
        r.Candidates.OrderByDescending(c => c.Path.Replace('\\', '/').EndsWith(r.OriginalReference.Replace('\\', '/').TrimStart('.', '/'), StringComparison.OrdinalIgnoreCase)).FirstOrDefault()?.Path, ""));

    [VersionSampleFact]
    public async Task MegaCreatesCompleteRelocatablePackageWithMaterialAndBothModelFormats()
    {
        using var fixture = new TestWorkspace();
        var sources = Directory.GetFiles(MegaRoot, "*", SearchOption.AllDirectories).ToDictionary(p => p, p => TextureMappingService.HashAsync(p).GetAwaiter().GetResult());
        var moved = fixture.File("Input/mega.efkefc"); File.Copy(Path.Combine(MegaRoot, "mega.efkefc"), moved, true);
        var parser = new EffekseerResourceParser(); var before = await parser.ParseAsync(moved);
        var service = new ResourceMappingService(); var discovery = await service.DiscoverAsync(moved, MegaRoot);
        Assert.Equal(16, discovery.Requests.Count(r => r.OwnerPath is null && r.Type == ResourceType.Material && r.Required));
        Assert.Equal(20, discovery.Requests.Count(r => r.OwnerPath is null && r.Type == ResourceType.Model));
        Assert.All(discovery.Requests.Where(r => r.Required), r => Assert.NotEmpty(r.Candidates));
        var plan = await service.PlanAdvancedAsync(discovery, Choose(discovery), TextureMappingMode.CopyIntoProject, "Textures", EffectWriteMode.SeparateFolder,
            Path.GetDirectoryName(moved)!, Path.Combine(fixture.Root, "Output"));
        Assert.True(plan.CanApply, string.Join("\n", plan.Diagnostics)); Assert.Equal(16, plan.DocumentEdits!.Count);
        Assert.Contains(plan.Diagnostics, d => d.Code == "SkippedEditorResource" && d.Message.Contains("trail_col.efkmat"));
        var result = await service.ApplyAsync(plan);
        Assert.Equal(60, result.FilesCopied);
        Assert.Equal(37, result.Audit.Documents.Count);
        Assert.DoesNotContain(result.Audit.Diagnostics, d => d.IsError);
        Assert.All(result.Audit.Resources.Where(r => r.Exists).SelectMany(r => r.References), r => { Assert.True(r.Audit.InsideBoundary); Assert.False(r.Audit.IsAbsolute); });
        Assert.Equal("trail_col.efkmat", Assert.Single(result.Audit.Resources, r => !r.Exists).Name);
        Assert.All(plan.Items.Where(i => i.Type == ResourceType.Model), i => Assert.Equal(i.SourceHash, TextureMappingService.HashAsync(i.TargetPath).GetAwaiter().GetResult()));
        foreach (var edit in plan.DocumentEdits)
        {
            var oldBytes = File.ReadAllBytes(edit.SourcePath); var newBytes = File.ReadAllBytes(edit.TargetPath);
            Assert.Equal(oldBytes.AsSpan(0, 16).ToArray(), newBytes.AsSpan(0, 16).ToArray());
            var oldMaterial = await parser.ParseAsync(edit.SourcePath); var newMaterial = await parser.ParseAsync(edit.TargetPath);
            foreach (var chunk in oldMaterial.Chunks.Where(c => c.Id is not ("PRM_" or "DATA")))
            { var target = newMaterial.Chunks.Single(c => c.Id == chunk.Id); Assert.Equal(oldBytes.AsSpan(chunk.Offset, chunk.Size).ToArray(), newBytes.AsSpan(target.Offset, target.Size).ToArray()); }
            var oldData = oldMaterial.Chunks.Single(c => c.Id == "DATA"); var newData = newMaterial.Chunks.Single(c => c.Id == "DATA");
            var expected = JsonNode.Parse(oldBytes.AsSpan(oldData.Offset, oldData.Size - 1))!;
            foreach (var reference in oldMaterial.References.Where(r => r.Locator.StartsWith("DATA/")))
            {
                JsonNode node = expected; var segments = reference.Locator.Split('/')[1..];
                foreach (var key in segments[..^1]) node = node is JsonArray array ? array[int.Parse(key)]! : node[key]!;
                node[segments[^1]] = edit.Mappings.GetValueOrDefault(reference.OriginalReference, reference.OriginalReference);
            }
            Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(newBytes.AsSpan(newData.Offset, newData.Size - 1))));
        }
        var after = await parser.ParseAsync(result.OutputEffectPath); var oldEffect = File.ReadAllBytes(moved); var newEffect = File.ReadAllBytes(result.OutputEffectPath);
        foreach (var chunk in before.Chunks.Where(c => c.Id == "BIN_"))
            Assert.Equal(RuntimeTail(oldEffect, chunk), RuntimeTail(newEffect, after.Chunks.Single(c => c.Id == "BIN_" && c.Version == chunk.Version)));
        var relocated = Path.Combine(fixture.Root, "MovedAgain"); CopyTree(plan.PackageRoot!, relocated);
        var audit = await new ResourceScanner(parser, new WindowsPathResolver()).ScanAsync([Path.Combine(relocated, "mega.efkefc")], relocated);
        Assert.DoesNotContain(audit.Diagnostics, d => d.IsError);
        Assert.All(audit.Resources.Where(r => r.References.Any(a => a.Reference.Role is ReferenceRole.Metadata or ReferenceRole.Runtime or ReferenceRole.CompatibilityRuntime)), r => Assert.Equal("VALID", r.Status));
        Assert.Single(audit.Resources, r => !r.Exists);
        foreach (var pair in sources) Assert.Equal(pair.Value, await TextureMappingService.HashAsync(pair.Key));
        Assert.Equal(discovery.EffectHash, await TextureMappingService.HashAsync(moved));
    }

    [VersionSampleFact]
    public async Task FullRepairKeepsDuplicatesAsCasesAndBlocksMissingModel()
    {
        using var fixture = new TestWorkspace(); var moved = fixture.File("mega.efkefc"); File.Copy(Path.Combine(MegaRoot, "mega.efkefc"), moved, true);
        var service = new ResourceMappingService(); var discovery = await service.DiscoverAsync(moved, MegaRoot);
        var choices = discovery.Requests.Select(r => new TextureSelection(r.SelectionKey, r.Candidates.Count == 1 ? r.Candidates[0].Path : null, "")).ToArray();
        var plan = await service.PlanAdvancedAsync(discovery, choices, TextureMappingMode.LinkExisting, "Textures", EffectWriteMode.ReplaceOriginal, fixture.Root, "");
        Assert.False(plan.CanApply); Assert.Contains(plan.Diagnostics, d => d.IsError && d.Message.Contains("color.png"));
        choices = Choose(discovery).ToArray();
        var model = discovery.Requests.First(r => r.Type == ResourceType.Model);
        choices[Array.FindIndex(choices, c => c.OriginalReference == model.SelectionKey)] = new(model.SelectionKey, null, "");
        plan = await service.PlanAdvancedAsync(discovery, choices, TextureMappingMode.LinkExisting, "Textures", EffectWriteMode.ReplaceOriginal, fixture.Root, "");
        Assert.False(plan.CanApply); await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(plan));
        Assert.Equal(discovery.EffectHash, await TextureMappingService.HashAsync(moved));
    }

    [VersionSampleFact]
    public async Task InPlaceFullRepairBacksUpEffectAndNeverEditsLibraryMaterials()
    {
        using var fixture = new TestWorkspace(); var moved = fixture.File("mega.efkefc"); File.Copy(Path.Combine(MegaRoot, "mega.efkefc"), moved, true);
        var service = new ResourceMappingService(); var discovery = await service.DiscoverAsync(moved, MegaRoot);
        var plan = await service.PlanAdvancedAsync(discovery, Choose(discovery), TextureMappingMode.LinkExisting, "Textures", EffectWriteMode.ReplaceOriginal, fixture.Root, "");
        var result = await service.ApplyAsync(plan);
        Assert.Equal(moved, result.OutputEffectPath); Assert.Equal(discovery.EffectHash, await TextureMappingService.HashAsync(result.BackupPath!));
        Assert.Empty(plan.DocumentEdits!);
        Assert.All(plan.Items, i => Assert.Equal(i.SourceHash, TextureMappingService.HashAsync(i.SourcePath).GetAwaiter().GetResult()));
        Assert.DoesNotContain(result.Audit.Resources, r => !r.Exists && r.References.Any(a => a.Reference.Role == ReferenceRole.Runtime));
    }

    [VersionSampleFact]
    public async Task LinkModeReusesEveryAssetWithoutCreatingDirectoriesOrEditingMaterials()
    {
        using var fixture = new TestWorkspace(); var moved = fixture.File("mega.efkefc"); File.Copy(Path.Combine(MegaRoot, "mega.efkefc"), moved, true);
        var service = new ResourceMappingService(); var discovery = await service.DiscoverAsync(moved, MegaRoot);
        var plan = await service.PlanAdvancedAsync(discovery, Choose(discovery), TextureMappingMode.LinkExisting, "Textures", EffectWriteMode.ReplaceOriginal, fixture.Root, "");
        var result = await service.ApplyAsync(plan);
        Assert.Equal(0, result.FilesCopied); Assert.All(plan.Items, i => { Assert.Equal(i.SourcePath, i.TargetPath); Assert.False(i.CopyRequired); Assert.Equal(i.SourceHash, TextureMappingService.HashAsync(i.SourcePath).GetAwaiter().GetResult()); });
        Assert.Empty(plan.DocumentEdits!); Assert.Null(plan.TextureDirectory);
        Assert.Empty(Directory.GetDirectories(fixture.Root));
        Assert.Equal(37, result.Audit.Documents.Count);
        Assert.All(plan.Items.Where(i => i.OwnerPath is not null), i => Assert.Equal(i.OriginalReference, i.NewReference));
        Assert.DoesNotContain(result.Audit.Resources, r => !r.Exists && r.References.Any(a => a.Reference.Role == ReferenceRole.Runtime));
    }

    [VersionSampleFact]
    public async Task ChangedMaterialAfterPreviewIsRejectedBeforeAnyWrite()
    {
        using var fixture = new TestWorkspace(); var library = Path.Combine(fixture.Root, "Library"); CopyTree(MegaRoot, library);
        var moved = fixture.File("Input/mega.efkefc"); File.Copy(Path.Combine(MegaRoot, "mega.efkefc"), moved, true);
        var service = new ResourceMappingService(); var discovery = await service.DiscoverAsync(moved, library);
        var plan = await service.PlanAdvancedAsync(discovery, Choose(discovery), TextureMappingMode.CopyIntoProject, "Textures", EffectWriteMode.SeparateFolder, Path.GetDirectoryName(moved)!, Path.Combine(fixture.Root, "Output"));
        await File.AppendAllTextAsync(plan.DocumentEdits![0].SourcePath, "changed");
        await Assert.ThrowsAsync<IOException>(() => service.ApplyAsync(plan)); Assert.False(Directory.Exists(plan.PackageRoot));
        Assert.Equal(discovery.EffectHash, await TextureMappingService.HashAsync(moved));
    }

    [VersionSampleFact]
    public async Task CancelAfterCopyRollsBackFullPackage()
    {
        using var fixture = new TestWorkspace(); var moved = fixture.File("Input/mega.efkefc"); File.Copy(Path.Combine(MegaRoot, "mega.efkefc"), moved, true);
        var service = new ResourceMappingService(); var discovery = await service.DiscoverAsync(moved, MegaRoot);
        var plan = await service.PlanAdvancedAsync(discovery, Choose(discovery), TextureMappingMode.CopyIntoProject, "Textures", EffectWriteMode.SeparateFolder, Path.GetDirectoryName(moved)!, Path.Combine(fixture.Root, "Output"));
        using var cancelled = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ApplyAsync(plan, new ImmediateProgress(p => { if (p.EndsWith(".efkmat")) cancelled.Cancel(); }), cancelled.Token));
        Assert.False(Directory.Exists(plan.PackageRoot)); Assert.Equal(discovery.EffectHash, await TextureMappingService.HashAsync(moved));
    }

    [VersionSampleFact]
    public async Task CancelAfterInPlaceCommitRestoresEffectAndRemovesOwnedAssets()
    {
        using var fixture = new TestWorkspace(); var moved = fixture.File("mega.efkefc"); File.Copy(Path.Combine(MegaRoot, "mega.efkefc"), moved, true);
        var service = new ResourceMappingService(); var discovery = await service.DiscoverAsync(moved, MegaRoot);
        var plan = await service.PlanAdvancedAsync(discovery, Choose(discovery), TextureMappingMode.LinkExisting, "Textures", EffectWriteMode.ReplaceOriginal, fixture.Root, "");
        using var cancelled = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ApplyAsync(plan, new ImmediateProgress(p => { if (p.StartsWith("Replaced")) cancelled.Cancel(); }), cancelled.Token));
        Assert.Equal(discovery.EffectHash, await TextureMappingService.HashAsync(moved)); Assert.Empty(Directory.GetDirectories(fixture.Root)); Assert.False(File.Exists(plan.JournalPath));
    }

    [VersionSampleFact]
    public async Task LinkModeBlocksChangedNestedTextureSelectionAndPackageModeCanRepairIt()
    {
        using var fixture = new TestWorkspace(); var moved = fixture.File("Input/mega.efkefc"); File.Copy(Path.Combine(MegaRoot, "mega.efkefc"), moved, true);
        var library = Path.Combine(fixture.Root, "Library"); CopyTree(Path.Combine(MegaRoot, "mega"), library);
        var service = new ResourceMappingService();
        var before = await service.DiscoverAsync(moved, library);
        var nested = before.Requests.First(r => r.OwnerPath is not null && r.Candidates.Count == 1);
        var texture = nested.Candidates[0].Path;
        var material = nested.OwnerPath!;
        var materialHash = await TextureMappingService.HashAsync(material);
        var newFolder = Path.Combine(library, "RelocatedTextures"); Directory.CreateDirectory(newFolder);
        File.Move(texture, Path.Combine(newFolder, Path.GetFileName(texture)));
        var discovery = await service.DiscoverAsync(moved, library);
        var choices = Choose(discovery).ToArray();
        var link = await service.PlanAdvancedAsync(discovery, choices, TextureMappingMode.LinkExisting, "Textures", EffectWriteMode.ReplaceOriginal, Path.GetDirectoryName(moved)!, "");
        Assert.False(link.CanApply); Assert.Contains(link.Diagnostics, d => d.Code == "MaterialNeedsRepair");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(link));
        Assert.Equal(discovery.EffectHash, await TextureMappingService.HashAsync(moved));
        Assert.Equal(materialHash, await TextureMappingService.HashAsync(material));
        var package = await service.PlanAdvancedAsync(discovery, choices, TextureMappingMode.CopyIntoProject, "Textures", EffectWriteMode.SeparateFolder, Path.GetDirectoryName(moved)!, Path.Combine(fixture.Root, "Output"));
        Assert.True(package.CanApply, string.Join("\n", package.Diagnostics));
        var result = await service.ApplyAsync(package);
        Assert.All(result.Audit.Resources.Where(r => r.Exists), r => Assert.Equal("VALID", r.Status));
        Assert.Equal(materialHash, await TextureMappingService.HashAsync(material));
    }

    [VersionSampleFact]
    public async Task ExistingModeIgnoresCopyNamesAndRejectsCopyOrMaterialEditPlans()
    {
        using var fixture = new TestWorkspace(); var moved = fixture.File("mega.efkefc"); File.Copy(Path.Combine(MegaRoot, "mega.efkefc"), moved, true);
        var service = new ResourceMappingService(); var discovery = await service.DiscoverAsync(moved, MegaRoot);
        var choices = Choose(discovery).Select(c => c with { DestinationName = "ignored-name.png" }).ToArray();
        var plan = await service.PlanAdvancedAsync(discovery, choices, TextureMappingMode.LinkExisting, "ignored", EffectWriteMode.ReplaceOriginal, fixture.Root, "");
        Assert.True(plan.CanApply); Assert.All(plan.Items, i => Assert.Equal(i.SourcePath, i.TargetPath));
        await Assert.ThrowsAsync<ArgumentException>(() => service.PlanAdvancedAsync(discovery, choices, TextureMappingMode.CopyIntoProject, "Textures", EffectWriteMode.ReplaceOriginal, fixture.Root, ""));
        var material = plan.Items.First(i => i.Type == ResourceType.Material && i.OwnerPath is null);
        var tampered = plan with { DocumentEdits = [new(material.SourcePath, material.SourceHash, material.SourcePath, new Dictionary<string, string>())] };
        await Assert.ThrowsAsync<ArgumentException>(() => service.ApplyAsync(tampered));
        Assert.Equal(discovery.EffectHash, await TextureMappingService.HashAsync(moved)); Assert.Empty(Directory.GetDirectories(fixture.Root));
    }

    [VersionSampleFact]
    public async Task ExportedAndMovedPackageRemapsWithPreviousOutputAsExplicitInputAndLibrary()
    {
        using var fixture = new TestWorkspace();
        var source = fixture.File("Input/mega.efkefc"); File.Copy(Path.Combine(MegaRoot, "mega.efkefc"), source, true);
        var service = new ResourceMappingService(); var original = await service.DiscoverAsync(source, MegaRoot);
        var outputRoot = Path.Combine(fixture.Root, "PreviousOutput");
        var copyPlan = await service.PlanAdvancedAsync(original, Choose(original), TextureMappingMode.CopyIntoProject, "Textures",
            EffectWriteMode.SeparateFolder, Path.GetDirectoryName(source)!, outputRoot);
        var copied = await service.ApplyAsync(copyPlan);
        var library = Path.Combine(copyPlan.PackageRoot!, "MovedAssets"); Directory.CreateDirectory(library);
        foreach (var name in new[] { "Materials", "Models", "Textures" })
            Directory.Move(Path.Combine(copyPlan.PackageRoot!, name), Path.Combine(library, name));
        var assetHashes = Directory.GetFiles(library, "*", SearchOption.AllDirectories)
            .ToDictionary(p => p, p => TextureMappingService.HashAsync(p).GetAwaiter().GetResult());
        foreach (var input in new[] { copied.OutputEffectPath, copyPlan.PackageRoot! })
        {
            var batch = await service.DiscoverBatchAsync(input, library, outputRoot);
            var item = Assert.Single(batch.Effects); Assert.Null(item.Error); Assert.NotNull(item.Discovery);
            Assert.All(item.Discovery!.Requests.Where(r => r.Required), r => Assert.NotEmpty(r.Candidates));
        }
        var rediscovery = (await service.DiscoverBatchAsync(copyPlan.PackageRoot!, library, outputRoot)).Effects[0].Discovery!;
        var link = await service.PlanAdvancedAsync(rediscovery, Choose(rediscovery), TextureMappingMode.LinkExisting, "Textures",
            EffectWriteMode.ReplaceOriginal, copyPlan.PackageRoot!, outputRoot);
        Assert.True(link.CanApply, string.Join("\n", link.Diagnostics));
        var result = await service.ApplyAsync(link);
        Assert.Equal(0, result.FilesCopied); Assert.Empty(link.DocumentEdits!);
        Assert.All(result.Audit.Resources.Where(r => r.Exists), r => Assert.Equal("VALID", r.Status));
        foreach (var pair in assetHashes) Assert.Equal(pair.Value, await TextureMappingService.HashAsync(pair.Key));
    }

    [VersionSampleFact]
    public async Task BroadBatchStillSkipsOutputDescendantsButExplicitOutputRootCanBeScanned()
    {
        using var fixture = new TestWorkspace();
        var input = fixture.File("Input/mega.efkefc"); File.Copy(Path.Combine(MegaRoot, "mega.efkefc"), input, true);
        var oldOutput = fixture.File("Input/Output/mega.efkefc"); File.Copy(input, oldOutput, true);
        var outputRoot = Path.GetDirectoryName(oldOutput)!; var service = new ResourceMappingService();
        var broad = await service.DiscoverBatchAsync(Path.GetDirectoryName(input)!, MegaRoot, outputRoot);
        Assert.Equal(input, Assert.Single(broad.Effects).EffectPath);
        var explicitOutput = await service.DiscoverBatchAsync(outputRoot, MegaRoot, outputRoot);
        Assert.Equal(oldOutput, Assert.Single(explicitOutput.Effects).EffectPath);
    }

    private sealed class ImmediateProgress(Action<string> action) : IProgress<string> { public void Report(string value) => action(value); }
    private static void CopyTree(string source, string destination)
    { Directory.CreateDirectory(destination); foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file))); foreach (var child in Directory.GetDirectories(source)) CopyTree(child, Path.Combine(destination, Path.GetFileName(child))); }
    private static byte[] RuntimeTail(byte[] bytes, ChunkInfo chunk)
    {
        var offset = chunk.Offset + 8;
        for (var table = 0; table < (chunk.Version == 1500 ? 6 : 7); table++)
        { var count = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset)); offset += 4; for (var i = 0; i < count; i++) offset += 4 + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset)) * 2; }
        return bytes.AsSpan(offset, chunk.Offset + chunk.Size - offset).ToArray();
    }
}
