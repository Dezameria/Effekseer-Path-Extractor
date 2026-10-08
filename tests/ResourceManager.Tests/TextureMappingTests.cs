using System.Buffers.Binary;
using System.Xml.Linq;
using ResourceManager.Core.Models;
using ResourceManager.Infrastructure.Effekseer;
using ResourceManager.Infrastructure.Mapping;

namespace ResourceManager.Tests;

public sealed class TextureMappingTests
{
    [SampleFact]
    public async Task MovedCorpusEffectsCanMapToExistingTextureLibrary()
    {
        var service = new TextureMappingService();
        var parser = new EffekseerResourceParser();
        foreach (var sourceEffect in Directory.GetFiles(Samples.Root!, "*.efkefc"))
        {
            using var fixture = new TestWorkspace();
            var moved = fixture.File(Path.GetFileName(sourceEffect));
            File.Copy(sourceEffect, moved, true);
            var sourceHash = await TextureMappingService.HashAsync(moved);
            var before = await parser.ParseAsync(moved);
            var discovery = await service.DiscoverAsync(moved, Samples.Root!);
            Assert.All(discovery.Requests.Where(r => r.Required), r => Assert.Single(r.Candidates));
            var plan = await service.PlanAsync(discovery, ChooseUnique(discovery), TextureMappingMode.LinkExisting, "Unused", "mapped.efkefc");
            Assert.True(plan.CanApply, string.Join("\n", plan.Diagnostics));
            var result = await service.ApplyAsync(plan);
            Assert.Equal(0, result.FilesCopied);
            Assert.Equal(sourceHash, await TextureMappingService.HashAsync(moved));
            var after = await parser.ParseAsync(result.OutputEffectPath);
            Assert.DoesNotContain(after.Diagnostics, d => d.IsError);
            Assert.All(result.Audit.Resources.Where(r => r.Type == ResourceType.Texture && r.References.Any(a => a.Reference.Role == ReferenceRole.Runtime)),
                r => Assert.True(r.Exists));
            Assert.Equal(before.Chunks.Select(c => (c.Id, c.Version)), after.Chunks.Select(c => (c.Id, c.Version)));
            VerifyRuntimeTailPreserved(await File.ReadAllBytesAsync(moved), before, await File.ReadAllBytesAsync(result.OutputEffectPath), after);
            var oldXml = XDocument.Parse(before.EditorXml!);
            var newXml = XDocument.Parse(after.EditorXml!);
            var mapping = plan.Items.ToDictionary(i => i.OriginalReference, i => i.NewReference);
            foreach (var leaf in oldXml.Descendants().Where(e => !e.HasElements))
                if (mapping.TryGetValue(leaf.Value, out var value)) leaf.Value = value;
            Assert.True(XNode.DeepEquals(oldXml, newXml));
        }
    }

    [SampleFact]
    public async Task CopyModeCreatesNamedFolderAndMovedRelativeReferences()
    {
        using var fixture = new TestWorkspace();
        var effect = CopySimpleEffect(fixture);
        var service = new TextureMappingService();
        var discovery = await service.DiscoverAsync(effect, Samples.Root!);
        var plan = await service.PlanAsync(discovery, ChooseUnique(discovery), TextureMappingMode.CopyIntoProject, "MyTextures", "repaired.efkefc");
        Assert.True(plan.CanApply);
        var result = await service.ApplyAsync(plan);
        Assert.True(result.FilesCopied > 0);
        Assert.All(plan.Items, i => Assert.StartsWith("MyTextures/", i.NewReference));
        Assert.All(plan.Items, i => Assert.Equal(i.SourceHash, TextureMappingService.HashAsync(i.TargetPath).GetAwaiter().GetResult()));
        Assert.All(result.Audit.Resources.Where(r => r.Type == ResourceType.Texture), r => Assert.Equal("VALID", r.Status));
        var relocated = Path.Combine(fixture.Root, "MovedAgain");
        Directory.CreateDirectory(relocated);
        File.Copy(result.OutputEffectPath, Path.Combine(relocated, "repaired.efkefc"));
        Directory.CreateDirectory(Path.Combine(relocated, "MyTextures"));
        foreach (var item in plan.Items.DistinctBy(i => i.TargetPath)) File.Copy(item.TargetPath, Path.Combine(relocated, "MyTextures", Path.GetFileName(item.TargetPath)));
        var audit = await new ResourceManager.Core.Scanning.ResourceScanner(new EffekseerResourceParser(), new ResourceManager.Infrastructure.FileSystem.WindowsPathResolver())
            .ScanAsync([Path.Combine(relocated, "repaired.efkefc")], relocated);
        Assert.All(audit.Resources.Where(r => r.Type == ResourceType.Texture && r.References.Any(a => a.Reference.Role == ReferenceRole.Runtime)), r => Assert.Equal("VALID", r.Status));
    }

    [SampleFact]
    public async Task AmbiguousNamesRequireSelectionAndMissingRequiredBlocksApply()
    {
        using var fixture = new TestWorkspace();
        var effect = CopySimpleEffect(fixture);
        var library = Path.Combine(fixture.Root, "Library");
        var service = new TextureMappingService();
        Directory.CreateDirectory(library);
        var original = await service.DiscoverAsync(effect, Samples.Root!);
        var one = original.Requests.First(r => r.Required);
        fixture.File("Library/One/" + one.FileName, "first");
        fixture.File("Library/Two/" + one.FileName, "second");
        var discovery = await service.DiscoverAsync(effect, library);
        Assert.Equal(2, Assert.Single(discovery.Requests, r => r.OriginalReference == one.OriginalReference).Candidates.Count);
        var plan = await service.PlanAsync(discovery, ChooseUnique(discovery), TextureMappingMode.CopyIntoProject, "Textures", "repaired.efkefc");
        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, d => d.Code == "UnmatchedRuntimeTexture" && d.IsError);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(plan));
        Assert.False(File.Exists(plan.OutputEffectPath));
    }

    [SampleFact]
    public async Task ExistingDifferentTextureIsNeverOverwritten()
    {
        using var fixture = new TestWorkspace();
        var effect = CopySimpleEffect(fixture);
        var service = new TextureMappingService();
        var discovery = await service.DiscoverAsync(effect, Samples.Root!);
        var request = discovery.Requests.First(r => r.Required);
        var occupied = fixture.File("MyTextures/" + request.FileName, "do not overwrite");
        var plan = await service.PlanAsync(discovery, ChooseUnique(discovery), TextureMappingMode.CopyIntoProject, "MyTextures", "repaired.efkefc");
        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, d => d.Code == "DestinationConflict");
        Assert.Equal("do not overwrite", await File.ReadAllTextAsync(occupied));
    }

    [SampleFact]
    public async Task ChangedEffectAfterPreviewIsRejected()
    {
        using var fixture = new TestWorkspace();
        var effect = CopySimpleEffect(fixture);
        var service = new TextureMappingService();
        var discovery = await service.DiscoverAsync(effect, Samples.Root!);
        var plan = await service.PlanAsync(discovery, ChooseUnique(discovery), TextureMappingMode.CopyIntoProject, "MyTextures", "repaired.efkefc");
        await File.AppendAllTextAsync(effect, "changed");
        await Assert.ThrowsAsync<IOException>(() => service.ApplyAsync(plan));
        Assert.False(Directory.Exists(plan.TextureDirectory));
    }

    [SampleFact]
    public async Task CancellationAfterCopyRollsBackOnlyCreatedFiles()
    {
        using var fixture = new TestWorkspace();
        var effect = CopySimpleEffect(fixture);
        var service = new TextureMappingService();
        var discovery = await service.DiscoverAsync(effect, Samples.Root!);
        var selected = ChooseUnique(discovery).ToArray();
        var first = discovery.Requests.First(r => r.Candidates.Count == 1);
        var reused = fixture.File("MyTextures/" + first.FileName);
        File.Copy(first.Candidates[0].Path, reused, true);
        var reusedHash = await TextureMappingService.HashAsync(reused);
        var plan = await service.PlanAsync(discovery, selected, TextureMappingMode.CopyIntoProject, "MyTextures", "repaired.efkefc");
        using var cancellation = new CancellationTokenSource();
        var progress = new ImmediateProgress(message => { if (message.StartsWith("Copied")) cancellation.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ApplyAsync(plan, progress, cancellation.Token));
        Assert.False(File.Exists(plan.OutputEffectPath));
        Assert.Equal(reusedHash, await TextureMappingService.HashAsync(reused));
        Assert.Single(Directory.GetFiles(plan.TextureDirectory!));
        Assert.Equal(discovery.EffectHash, await TextureMappingService.HashAsync(effect));
    }

    [SampleFact]
    public async Task InvalidFolderNamesAndOutputSourceAreBlocked()
    {
        using var fixture = new TestWorkspace();
        var effect = CopySimpleEffect(fixture);
        var service = new TextureMappingService();
        var discovery = await service.DiscoverAsync(effect, Samples.Root!);
        await Assert.ThrowsAsync<ArgumentException>(() => service.PlanAsync(discovery, ChooseUnique(discovery), TextureMappingMode.CopyIntoProject, "../outside", "mapped.efkefc"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.PlanAsync(discovery, ChooseUnique(discovery), TextureMappingMode.CopyIntoProject, "CON", "mapped.efkefc"));
        var overwrite = await service.PlanAsync(discovery, ChooseUnique(discovery), TextureMappingMode.LinkExisting, "unused", Path.GetFileName(effect));
        Assert.False(overwrite.CanApply);
    }

    [SampleFact]
    public async Task IdenticalExistingTargetsAreReused()
    {
        using var fixture = new TestWorkspace();
        var effect = CopySimpleEffect(fixture);
        var service = new TextureMappingService();
        var discovery = await service.DiscoverAsync(effect, Samples.Root!);
        foreach (var request in discovery.Requests.Where(r => r.Candidates.Count == 1))
        {
            var target = fixture.File("MyTextures/" + request.FileName);
            File.Copy(request.Candidates[0].Path, target, true);
        }
        var plan = await service.PlanAsync(discovery, ChooseUnique(discovery), TextureMappingMode.CopyIntoProject, "MyTextures", "repaired.efkefc");
        Assert.True(plan.CanApply);
        Assert.All(plan.Items, i => Assert.False(i.CopyRequired));
        var result = await service.ApplyAsync(plan);
        Assert.Equal(0, result.FilesCopied);
    }

    [SampleFact]
    public async Task RenamedDestinationCollisionBlocksApply()
    {
        using var fixture = new TestWorkspace();
        var effect = CopySimpleEffect(fixture);
        var service = new TextureMappingService();
        var discovery = await service.DiscoverAsync(effect, Samples.Root!);
        var choices = ChooseUnique(discovery).ToArray();
        choices[0] = choices[0] with { DestinationName = "same.png" };
        choices[1] = choices[1] with { DestinationName = "same.png" };
        var plan = await service.PlanAsync(discovery, choices, TextureMappingMode.CopyIntoProject, "MyTextures", "repaired.efkefc");
        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, d => d.Code == "NameCollision");
    }

    [SampleFact]
    public async Task TextureChangedAfterPreviewIsRejectedWithoutModifyingLibrary()
    {
        using var fixture = new TestWorkspace();
        var effect = CopySimpleEffect(fixture);
        var library = Path.Combine(fixture.Root, "Library");
        Directory.CreateDirectory(library);
        foreach (var source in Directory.GetFiles(Path.Combine(Samples.Root!, "Texture"), "*.png")) File.Copy(source, Path.Combine(library, Path.GetFileName(source)));
        var service = new TextureMappingService();
        var discovery = await service.DiscoverAsync(effect, library);
        var plan = await service.PlanAsync(discovery, ChooseUnique(discovery), TextureMappingMode.CopyIntoProject, "MyTextures", "repaired.efkefc");
        var changed = plan.Items[0].SourcePath;
        await File.AppendAllTextAsync(changed, "changed after preview");
        var changedHash = await TextureMappingService.HashAsync(changed);
        await Assert.ThrowsAsync<IOException>(() => service.ApplyAsync(plan));
        Assert.Equal(changedHash, await TextureMappingService.HashAsync(changed));
        Assert.False(File.Exists(plan.OutputEffectPath));
    }

    [SampleFact]
    public async Task CopyLegacyEffectPreservesBothRuntimeVersions()
    {
        using var fixture = new TestWorkspace();
        var effect = fixture.File("legacy.efkefc");
        File.Copy(Path.Combine(Samples.Root!, "EVFXForge15_01_ShineInvocation.efkefc"), effect, true);
        var service = new TextureMappingService();
        var before = await new EffekseerResourceParser().ParseAsync(effect);
        var discovery = await service.DiscoverAsync(effect, Samples.Root!);
        var plan = await service.PlanAsync(discovery, ChooseUnique(discovery), TextureMappingMode.CopyIntoProject, "MyTextures", "repaired.efkefc");
        var result = await service.ApplyAsync(plan);
        var after = await new EffekseerResourceParser().ParseAsync(result.OutputEffectPath);
        Assert.Equal(new int?[] { 1610, 1500 }, after.Chunks.Where(c => c.Id == "BIN_").Select(c => c.Version));
        VerifyRuntimeTailPreserved(await File.ReadAllBytesAsync(effect), before, await File.ReadAllBytesAsync(result.OutputEffectPath), after);
        Assert.All(result.Audit.Resources.Where(r => r.Type == ResourceType.Texture && r.References.Any(a => a.Reference.Role == ReferenceRole.Runtime)), r => Assert.Equal("VALID", r.Status));
    }

    [SampleFact]
    public async Task ReplaceOriginalKeepsVerifiedBackupAndOriginalTextureNames()
    {
        using var fixture = new TestWorkspace();
        var effect = CopySimpleEffect(fixture);
        var service = new TextureMappingService();
        var discovery = await service.DiscoverAsync(effect, Samples.Root!);
        var choices = ChooseUnique(discovery).Select(s => s with { DestinationName = "" });
        var plan = await service.PlanAdvancedAsync(discovery, choices, TextureMappingMode.CopyIntoProject,
            "Textures", EffectWriteMode.ReplaceOriginal, fixture.Root, "");
        var result = await service.ApplyAsync(plan);
        Assert.Equal(effect, result.OutputEffectPath);
        Assert.Equal(discovery.EffectHash, await TextureMappingService.HashAsync(result.BackupPath!));
        Assert.NotEqual(discovery.EffectHash, await TextureMappingService.HashAsync(effect));
        Assert.All(plan.Items, i => Assert.Equal(Path.GetFileName(i.SourcePath), Path.GetFileName(i.TargetPath)));
        Assert.All(result.Audit.Resources.Where(r => r.Type == ResourceType.Texture), r => Assert.True(r.Exists));
        Assert.True(File.Exists(result.ManifestPath));
        // A second edit gets a distinct backup and journal, while retaining the first backup.
        var second = await service.DiscoverAsync(effect, Samples.Root!);
        var next = await service.PlanAdvancedAsync(second, ChooseUnique(second), TextureMappingMode.LinkExisting,
            "Textures", EffectWriteMode.ReplaceOriginal, fixture.Root, "");
        var nextResult = await service.ApplyAsync(next);
        Assert.NotEqual(result.BackupPath, nextResult.BackupPath);
        Assert.Equal(discovery.EffectHash, await TextureMappingService.HashAsync(result.BackupPath!));
    }

    [SampleFact]
    public async Task CancellationAfterReplacementRestoresOriginalBytes()
    {
        using var fixture = new TestWorkspace();
        var effect = CopySimpleEffect(fixture);
        var service = new TextureMappingService();
        var discovery = await service.DiscoverAsync(effect, Samples.Root!);
        var plan = await service.PlanAdvancedAsync(discovery, ChooseUnique(discovery), TextureMappingMode.CopyIntoProject,
            "Textures", EffectWriteMode.ReplaceOriginal, fixture.Root, "");
        using var cancellation = new CancellationTokenSource();
        var progress = new ImmediateProgress(p => { if (p.StartsWith("Replaced original")) cancellation.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ApplyAsync(plan, progress, cancellation.Token));
        Assert.Equal(discovery.EffectHash, await TextureMappingService.HashAsync(effect));
        Assert.False(Directory.Exists(plan.TextureDirectory));
        Assert.False(File.Exists(plan.JournalPath));
    }

    [SampleFact]
    public async Task ChangedOriginalRejectsInPlaceWriteWithoutCreatingBackup()
    {
        using var fixture = new TestWorkspace();
        var effect = CopySimpleEffect(fixture);
        var service = new TextureMappingService();
        var discovery = await service.DiscoverAsync(effect, Samples.Root!);
        var plan = await service.PlanAdvancedAsync(discovery, ChooseUnique(discovery), TextureMappingMode.LinkExisting,
            "Textures", EffectWriteMode.ReplaceOriginal, fixture.Root, "");
        await File.AppendAllTextAsync(effect, "changed");
        var changedHash = await TextureMappingService.HashAsync(effect);
        await Assert.ThrowsAsync<IOException>(() => service.ApplyAsync(plan));
        Assert.Equal(changedHash, await TextureMappingService.HashAsync(effect));
        Assert.False(File.Exists(plan.BackupPath));
    }

    [SampleFact]
    public async Task FolderBatchHandlesSameEffectNamesAndExcludesItsOutputFolder()
    {
        using var fixture = new TestWorkspace();
        var input = Path.Combine(fixture.Root, "Input");
        var output = Path.Combine(input, "MappedEffects");
        File.Copy(Path.Combine(Samples.Root!, "annihilation_flare.efkefc"), fixture.File("Input/A/fire.efkefc"), true);
        File.Copy(Path.Combine(Samples.Root!, "annihilation_flare.efkefc"), fixture.File("Input/B/fire.efkefc"), true);
        var service = new TextureMappingService();
        var batch = await service.DiscoverBatchAsync(input, Samples.Root!, output);
        Assert.Equal(2, batch.Effects.Count);
        var plans = new List<TextureMappingPlan>();
        foreach (var row in batch.Effects)
        {
            var discovery = row.Discovery!;
            var plan = await service.PlanAdvancedAsync(discovery, ChooseUnique(discovery), TextureMappingMode.CopyIntoProject,
                "Textures", EffectWriteMode.SeparateFolder, input, output);
            plans.Add(plan);
            var result = await service.ApplyAsync(plan);
            Assert.Equal(discovery.EffectHash, await TextureMappingService.HashAsync(row.EffectPath));
            Assert.All(result.Audit.Resources.Where(r => r.Type == ResourceType.Texture), r => Assert.True(r.Exists));
            Assert.All(plan.Items, i => Assert.StartsWith(plan.PackageRoot!, i.TargetPath));
        }
        Assert.Empty(TextureMappingService.ValidateBatchPlans(plans));
        Assert.NotEqual(plans[0].OutputEffectPath, plans[1].OutputEffectPath);
        Assert.Equal(2, (await service.DiscoverBatchAsync(input, Samples.Root!, output)).Effects.Count);
        var existing = await service.PlanAdvancedAsync(batch.Effects[0].Discovery!, ChooseUnique(batch.Effects[0].Discovery!),
            TextureMappingMode.CopyIntoProject, "Textures", EffectWriteMode.SeparateFolder, input, output);
        Assert.False(existing.CanApply);
    }

    [SampleFact]
    public async Task BatchReportsUnsupportedFilesAndDuplicateTextureCases()
    {
        using var fixture = new TestWorkspace();
        var effect = CopySimpleEffect(fixture);
        fixture.File("bad.efkefc", "unsupported");
        var library = Path.Combine(fixture.Root, "Library");
        Directory.CreateDirectory(library);
        var service = new TextureMappingService();
        var original = await service.DiscoverAsync(effect, Samples.Root!);
        var request = original.Requests.First();
        fixture.File("Library/A/" + request.FileName);
        fixture.File("Library/B/" + request.FileName);
        var batch = await service.DiscoverBatchAsync(fixture.Root, library);
        Assert.Contains(batch.Effects, e => e.Error is not null && e.Discovery is null);
        var supported = Assert.Single(batch.Effects, e => e.Discovery is not null).Discovery!;
        Assert.Equal(2, supported.Requests.First(r => r.FileName == request.FileName).Candidates.Count);
        var plan = await service.PlanAdvancedAsync(supported, ChooseUnique(supported), TextureMappingMode.CopyIntoProject,
            "Textures", EffectWriteMode.ReplaceOriginal, fixture.Root, "");
        Assert.False(plan.CanApply);
    }

    [SampleFact]
    public async Task SeparatePackagesPreserveNonTextureReferencesAndTheirMaterialHierarchy()
    {
        using var fixture = new TestWorkspace();
        var sourceRoot = Path.Combine(fixture.Root, "Source");
        foreach (var source in Directory.GetFiles(Samples.Root!, "*", SearchOption.AllDirectories))
            File.Copy(source, fixture.File("Source/" + Path.GetRelativePath(Samples.Root!, source)), true);
        var service = new TextureMappingService();
        var parser = new EffekseerResourceParser();
        var tested = 0;
        foreach (var effect in Directory.GetFiles(sourceRoot, "*.efkefc"))
        {
            var before = await parser.ParseAsync(effect);
            if (!before.References.Any(r => r.Type == ResourceType.Material)) continue;
            var discovery = await service.DiscoverAsync(effect, sourceRoot);
            var plan = await service.PlanAdvancedAsync(discovery, ChooseUnique(discovery), TextureMappingMode.CopyIntoProject,
                "MappedTextures", EffectWriteMode.SeparateFolder, sourceRoot, Path.Combine(fixture.Root, "Export"));
            Assert.True(plan.CanApply, string.Join("\n", plan.Diagnostics));
            var result = await service.ApplyAsync(plan);
            var after = await parser.ParseAsync(result.OutputEffectPath);
            Assert.Equal(before.References.Where(r => r.Type != ResourceType.Texture).Select(r => (r.Locator, r.OriginalReference)),
                after.References.Where(r => r.Type != ResourceType.Texture).Select(r => (r.Locator, r.OriginalReference)));
            Assert.NotEmpty(plan.AdditionalCopies!);
            Assert.All(plan.AdditionalCopies!, c => Assert.Equal(c.SourceHash, TextureMappingService.HashAsync(c.TargetPath).GetAwaiter().GetResult()));
            Assert.All(result.Audit.Resources.Where(r => r.Type == ResourceType.Material), r => Assert.True(r.Exists));
            Assert.Equal(discovery.EffectHash, await TextureMappingService.HashAsync(effect));
            tested++;
        }
        Assert.True(tested > 0);
    }

    [SampleFact]
    public async Task PackageCancellationRemovesCreatedFilesAndAllowsRetry()
    {
        using var fixture = new TestWorkspace();
        var effect = CopySimpleEffect(fixture);
        var service = new TextureMappingService();
        var discovery = await service.DiscoverAsync(effect, Samples.Root!);
        var plan = await service.PlanAdvancedAsync(discovery, ChooseUnique(discovery), TextureMappingMode.CopyIntoProject,
            "Textures", EffectWriteMode.SeparateFolder, fixture.Root, Path.Combine(fixture.Root, "Export"));
        using var cancellation = new CancellationTokenSource();
        var progress = new ImmediateProgress(p => { if (p.StartsWith("Copied")) cancellation.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ApplyAsync(plan, progress, cancellation.Token));
        Assert.False(Directory.Exists(plan.PackageRoot));
        Assert.Equal(discovery.EffectHash, await TextureMappingService.HashAsync(effect));
        Assert.True(File.Exists((await service.ApplyAsync(plan)).OutputEffectPath));
    }

    [Fact]
    public void BatchValidationDetectsCrossEffectTextureAndPackageCollisions()
    {
        using var fixture = new TestWorkspace();
        var target = Path.Combine(fixture.Root, "Textures", "same.png");
        var a = new TextureMappingPlan("a", "hash", fixture.Root, "a.efkefc", TextureMappingMode.CopyIntoProject, null,
            [new("same.png", "source-a", "first", target, "same.png", true)], [], EffectWriteMode.ReplaceOriginal);
        var b = a with { EffectPath = "b", OutputEffectPath = "b.efkefc", Items = [a.Items[0] with { SourceHash = "second" }] };
        Assert.Contains(TextureMappingService.ValidateBatchPlans([a, b]), d => d.Code == "BatchTextureCollision");
        a = a with { PackageRoot = Path.Combine(fixture.Root, "a") };
        b = b with { PackageRoot = Path.Combine(fixture.Root, "a", "b") };
        Assert.Contains(TextureMappingService.ValidateBatchPlans([a, b]), d => d.Code == "BatchPackageOverlap");
    }

    private static string CopySimpleEffect(TestWorkspace fixture)
    {
        var effect = fixture.File("annihilation_flare.efkefc");
        File.Copy(Path.Combine(Samples.Root!, "annihilation_flare.efkefc"), effect, true);
        return effect;
    }
    private static IEnumerable<TextureSelection> ChooseUnique(TextureDiscovery discovery) => discovery.Requests.Select(r =>
        new TextureSelection(r.OriginalReference, r.Candidates.Count == 1 ? r.Candidates[0].Path : null, r.FileName));
    private sealed class ImmediateProgress(Action<string> action) : IProgress<string>
    { public void Report(string value) => action(value); }

    private static void VerifyRuntimeTailPreserved(byte[] before, ParsedDocument oldDocument, byte[] after, ParsedDocument newDocument)
    {
        var oldChunks = oldDocument.Chunks.Where(c => c.Id == "BIN_").ToArray();
        var newChunks = newDocument.Chunks.Where(c => c.Id == "BIN_").ToArray();
        for (var i = 0; i < oldChunks.Length; i++)
        {
            static byte[] Tail(byte[] bytes, ChunkInfo chunk)
            {
                var offset = chunk.Offset + 8;
                var tableCount = chunk.Version == 1500 ? 6 : 7;
                for (var table = 0; table < tableCount; table++)
                {
                    var count = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
                    offset += 4;
                    for (var n = 0; n < count; n++)
                    {
                        var chars = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
                        offset += 4 + chars * 2;
                    }
                }
                return bytes.AsSpan(offset, chunk.Offset + chunk.Size - offset).ToArray();
            }
            Assert.Equal(Tail(before, oldChunks[i]), Tail(after, newChunks[i]));
        }
    }
}
