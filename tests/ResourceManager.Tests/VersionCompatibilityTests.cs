using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ResourceManager.Core.Models;
using ResourceManager.Infrastructure.Effekseer;
using ResourceManager.Infrastructure.Mapping;

namespace ResourceManager.Tests;

public sealed class VersionCompatibilityTests
{
    private static readonly string[][] RuntimePaths = [
        ["Old/สี color.png"], ["Old/normal.png"], ["Old/distort.png"],
        ["Old/sound.wav"], ["Old/model.efkmodel"], ["Old/material.efkmat"], ["Old/curve.efkcurve"]
    ];
    private static readonly byte[] NodePayload = [0, 255, 17, 34, 51, 68, 0, 128, 123];

    [VersionSampleFact]
    public async Task Provided1803EffectMapsRealTexturesWithoutChangingSourceOrNonTextureReferences()
    {
        var sampleRoot = Environment.GetEnvironmentVariable("EFFEKSEER_VERSION_SAMPLE_ROOT")!;
        var source = Path.Combine(sampleRoot, "mega.efkefc");
        var originalHash = await TextureMappingService.HashAsync(source);
        using var fixture = new TestWorkspace();
        var moved = fixture.File("mega.efkefc");
        File.Copy(source, moved, true);
        var parser = new EffekseerResourceParser();
        var before = await parser.ParseAsync(moved);
        Assert.Equal("1.80.3", before.EditorVersion);
        var service = new TextureMappingService();
        var discovery = await service.DiscoverAsync(moved, sampleRoot);
        Assert.Equal(24, discovery.Requests.Count(r => r.Required));
        Assert.All(discovery.Requests.Where(r => r.Required), r => Assert.NotEmpty(r.Candidates));
        // Explicit selection for this fixture. The UI still requires a choice for duplicates.
        foreach (var duplicate in discovery.Requests.Where(r => r.Candidates.Count > 1))
        {
            var hashes = await Task.WhenAll(duplicate.Candidates.Select(c => TextureMappingService.HashAsync(c.Path)));
            Assert.Single(hashes.Distinct());
        }
        var selections = discovery.Requests.Select(r => new TextureSelection(r.OriginalReference,
            r.Candidates.OrderByDescending(c => c.Path.Replace('\\', '/').EndsWith(r.OriginalReference, StringComparison.OrdinalIgnoreCase)).FirstOrDefault()?.Path, ""));
        var plan = await service.PlanAdvancedAsync(discovery, selections, TextureMappingMode.CopyIntoProject,
            "Textures", EffectWriteMode.ReplaceOriginal, fixture.Root, "");
        Assert.True(plan.CanApply);
        var result = await service.ApplyAsync(plan);
        Assert.Equal(originalHash, await TextureMappingService.HashAsync(result.BackupPath!));
        var after = await parser.ParseAsync(moved);
        Assert.DoesNotContain(after.Diagnostics, d => d.IsError);
        Assert.Equal(before.References.Where(r => r.Type != ResourceType.Texture), after.References.Where(r => r.Type != ResourceType.Texture));
        Assert.All(result.Audit.Resources.Where(r => r.Type == ResourceType.Texture && r.References.Any(a => a.Reference.Role == ReferenceRole.Runtime)),
            r => Assert.Equal("VALID", r.Status));
        var oldBytes = await File.ReadAllBytesAsync(result.BackupPath!);
        var newBytes = await File.ReadAllBytesAsync(moved);
        var oldRuntime = before.Chunks.Where(c => c.Id == "BIN_").ToArray();
        var newRuntime = after.Chunks.Where(c => c.Id == "BIN_").ToArray();
        Assert.Equal(oldRuntime.Select(c => c.Version), newRuntime.Select(c => c.Version));
        for (var i = 0; i < oldRuntime.Length; i++) Assert.Equal(ReadRuntimeTail(oldBytes, oldRuntime[i]), ReadRuntimeTail(newBytes, newRuntime[i]));
        Assert.Equal(ReadInfoFlags(oldBytes, before), ReadInfoFlags(newBytes, after));
        var editor = XDocument.Parse(before.EditorXml!);
        var mappings = plan.Items.ToDictionary(i => i.OriginalReference, i => i.NewReference);
        foreach (var leaf in editor.Descendants().Where(e => !e.HasElements))
            if (mappings.TryGetValue(leaf.Value, out var replacement)) leaf.Value = replacement;
        Assert.True(XNode.DeepEquals(editor, XDocument.Parse(after.EditorXml!)));
        Assert.Equal(originalHash, await TextureMappingService.HashAsync(source));
    }

    [Theory]
    [InlineData("1.60", 1610)]
    [InlineData("1.61a", 1610)]
    [InlineData("1.62a", 1610)]
    [InlineData("1.70", 1710)]
    [InlineData("1.70e", 1710)]
    [InlineData("1.80", 1810)]
    [InlineData("1.80.0", 1810)]
    [InlineData("1.80.2", 1810)]
    [InlineData("1.80.3", 1810)]
    [InlineData("1.80.7", 1810)]
    public async Task StableFamiliesMapEveryTextureTableAndPreserveOtherResources(string release, int layout)
    {
        using var fixture = new TestWorkspace();
        var effect = fixture.File("moved.efkefc");
        var bytes = BuildEffect(release, layout, [layout, 1500]);
        await File.WriteAllBytesAsync(effect, bytes);
        foreach (var path in RuntimePaths.Take(3).SelectMany(p => p)) fixture.File("Library/Nested/" + Path.GetFileName(path));
        var parser = new EffekseerResourceParser();
        var before = await parser.ParseAsync(effect);
        Assert.DoesNotContain(before.Diagnostics, d => d.IsError);
        var service = new TextureMappingService();
        var discovery = await service.DiscoverAsync(effect, Path.Combine(fixture.Root, "Library"));
        Assert.Equal(release, discovery.EditorVersion);
        Assert.All(discovery.Requests, r => Assert.Single(r.Candidates));
        var choices = discovery.Requests.Select(r => new TextureSelection(r.OriginalReference, r.Candidates[0].Path, ""));
        var plan = await service.PlanAdvancedAsync(discovery, choices, TextureMappingMode.CopyIntoProject,
            "NewTextures", EffectWriteMode.ReplaceOriginal, fixture.Root, "");
        Assert.True(plan.CanApply, string.Join("\n", plan.Diagnostics));
        var result = await service.ApplyAsync(plan);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(result.BackupPath!));
        var after = await parser.ParseAsync(effect);
        Assert.Equal(release, after.EditorVersion);
        Assert.Equal(before.Chunks.Select(c => (c.Id, c.Version)), after.Chunks.Select(c => (c.Id, c.Version)));
        Assert.All(after.References.Where(r => r.Type == ResourceType.Texture), r => Assert.StartsWith("NewTextures/", r.OriginalReference));
        Assert.Equal(before.References.Where(r => r.Type != ResourceType.Texture), after.References.Where(r => r.Type != ResourceType.Texture));
        Assert.All(result.Audit.Resources.Where(r => r.Type == ResourceType.Texture), r => Assert.Equal("VALID", r.Status));
        var rewritten = await File.ReadAllBytesAsync(effect);
        foreach (var chunk in after.Chunks.Where(c => c.Id == "BIN_"))
            Assert.Equal(NodePayload, ReadRuntimeTail(rewritten, chunk));
        if (layout >= 1710) Assert.Equal(ReadInfoFlags(bytes, before), ReadInfoFlags(rewritten, after));
        var oldEditor = XDocument.Parse(before.EditorXml!);
        foreach (var element in oldEditor.Descendants().Where(e => !e.HasElements && e.Name.LocalName is "ColorTexture" or "NormalTexture" or "Texture"))
            element.Value = plan.Items.Single(i => i.OriginalReference == element.Value).NewReference;
        Assert.True(XNode.DeepEquals(oldEditor, XDocument.Parse(after.EditorXml!)));
    }

    [Theory]
    [InlineData("1.70alpha5", 1705)]
    [InlineData("1.80beta1", 1810)]
    [InlineData("1.81.0", 1810)]
    [InlineData("1.90", 1900)]
    [InlineData("", 1810)]
    [InlineData("1.70e", 1810)]
    [InlineData("1.80.3", 1710)]
    public async Task UnknownAndMismatchedVersionsAreExplainedAndNeverWritten(string release, int layout)
    {
        using var fixture = new TestWorkspace();
        var effect = fixture.File("unsupported.efkefc");
        var bytes = BuildEffect(release, layout, [layout]);
        await File.WriteAllBytesAsync(effect, bytes);
        var document = await new EffekseerResourceParser().ParseAsync(effect);
        Assert.Equal(release, document.EditorVersion);
        Assert.False(TextureReferenceWriter.Supports(document));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new TextureMappingService().DiscoverAsync(effect, fixture.Root));
        Assert.Contains("INFO " + layout, error.Message);
        Assert.Contains("BIN_ " + layout, error.Message);
        if (release.Length > 0) Assert.Contains(release, error.Message);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(effect));
        Assert.Single(Directory.GetFiles(fixture.Root));
    }

    [Theory]
    [InlineData("unknown-chunk")]
    [InlineData("future-runtime")]
    [InlineData("mismatched-runtime")]
    [InlineData("unknown-type")]
    [InlineData("metadata-mismatch")]
    public async Task RecognizedReleaseCannotBypassStructuralChecks(string defect)
    {
        using var fixture = new TestWorkspace();
        var bytes = BuildEffect("1.80.3", 1810,
            defect == "future-runtime" ? [1810, 1900] : defect == "mismatched-runtime" ? [1710] : [1810], defect);
        var effect = fixture.File("blocked.efkefc");
        await File.WriteAllBytesAsync(effect, bytes);
        var before = await new EffekseerResourceParser().ParseAsync(effect);
        Assert.False(TextureReferenceWriter.Supports(before));
        await Assert.ThrowsAsync<InvalidDataException>(() => new TextureMappingService().DiscoverAsync(effect, fixture.Root));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(effect));
    }

    [Fact]
    public async Task UnexpectedEditorFieldCannotBeSilentlyRewritten()
    {
        using var fixture = new TestWorkspace();
        var effect = fixture.File("comment.efkefc");
        var bytes = BuildEffect("1.80.3", 1810, [1810], "unknown-editor-field");
        await File.WriteAllBytesAsync(effect, bytes);
        foreach (var path in RuntimePaths.Take(3).SelectMany(p => p)) fixture.File("Library/" + Path.GetFileName(path));
        var service = new TextureMappingService();
        var discovery = await service.DiscoverAsync(effect, Path.Combine(fixture.Root, "Library"));
        var plan = await service.PlanAsync(discovery, discovery.Requests.Select(r => new TextureSelection(r.OriginalReference, r.Candidates[0].Path, "")),
            TextureMappingMode.CopyIntoProject, "Textures", "mapped.efkefc");
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.ApplyAsync(plan));
        Assert.Contains("unverified EDIT field Comment", error.Message);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(effect));
        Assert.False(File.Exists(plan.OutputEffectPath));
        Assert.False(Directory.Exists(plan.TextureDirectory));
    }

    [Fact]
    public async Task MissingDistantEditorResourceDoesNotExpandPackageHierarchy()
    {
        using var fixture = new TestWorkspace();
        var effect = fixture.File("Input/moved.efkefc");
        await File.WriteAllBytesAsync(effect, BuildEffect("1.80.3", 1810, [1810], "distant-missing-editor"));
        foreach (var path in RuntimePaths.Take(3).SelectMany(p => p)) fixture.File("Library/" + Path.GetFileName(path));
        var service = new TextureMappingService();
        var discovery = await service.DiscoverAsync(effect, Path.Combine(fixture.Root, "Library"));
        var plan = await service.PlanAdvancedAsync(discovery, discovery.Requests.Select(r => new TextureSelection(r.OriginalReference, r.Candidates[0].Path, "")),
            TextureMappingMode.CopyIntoProject, "Textures", EffectWriteMode.SeparateFolder, Path.GetDirectoryName(effect)!, Path.Combine(fixture.Root, "Output"));
        Assert.True(plan.CanApply);
        Assert.Equal(Path.Combine(plan.PackageRoot!, "moved.efkefc"), plan.OutputEffectPath);
        Assert.Contains(plan.Diagnostics, d => d.Code == "OtherDependencies");
        Assert.Empty(plan.AdditionalCopies!);
        Assert.True(File.Exists((await service.ApplyAsync(plan)).OutputEffectPath));
    }

    [Fact]
    public async Task MixedVersionFolderReportsUnsupportedFileWithoutBlockingOtherDiscoveries()
    {
        using var fixture = new TestWorkspace();
        foreach (var (release, layout) in new[] { ("1.62a", 1610), ("1.70e", 1710), ("1.80.3", 1810), ("1.90", 1900) })
            await File.WriteAllBytesAsync(fixture.File("Input/" + release + ".efkefc"), BuildEffect(release, layout, [layout]));
        foreach (var path in RuntimePaths.Take(3).SelectMany(p => p)) fixture.File("Library/Nested/" + Path.GetFileName(path));
        var service = new TextureMappingService();
        var batch = await service.DiscoverBatchAsync(Path.Combine(fixture.Root, "Input"), Path.Combine(fixture.Root, "Library"));
        Assert.Equal(3, batch.Effects.Count(e => e.Discovery is not null));
        Assert.Contains("1.90", Assert.Single(batch.Effects, e => e.Error is not null).Error!);
        foreach (var item in batch.Effects.Where(e => e.Discovery is not null))
        {
            var discovery = item.Discovery!;
            var plan = await service.PlanAdvancedAsync(discovery, discovery.Requests.Select(r => new TextureSelection(r.OriginalReference, r.Candidates[0].Path, "")),
                TextureMappingMode.CopyIntoProject, "Textures", EffectWriteMode.SeparateFolder, batch.InputPath, Path.Combine(fixture.Root, "Output"));
            Assert.True(File.Exists((await service.ApplyAsync(plan)).OutputEffectPath));
            Assert.Equal(discovery.EffectHash, await TextureMappingService.HashAsync(item.EffectPath));
        }
    }

    // Independent format fixtures exercise old ordered INFO tables, new typed INFO with flags,
    // all seven runtime tables, Unicode paths and opaque node bytes. They are not editor exports.
    private static byte[] BuildEffect(string release, int layout, int[] runtimes, string? defect = null)
    {
        static byte[] Payload(Action<BinaryWriter> write)
        { using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); write(writer); return stream.ToArray(); }
        static void PathString(BinaryWriter writer, string path)
        { writer.Write(path.Length + 1); writer.Write(Encoding.Unicode.GetBytes(path)); writer.Write((ushort)0); }
        var info = Payload(writer =>
        {
            writer.Write(layout);
            if (layout == 1610)
            {
                foreach (var index in new[] { 0, 1, 2, 4, 3, 5, 6 })
                { writer.Write(RuntimePaths[index].Length); foreach (var path in RuntimePaths[index]) PathString(writer, path); }
            }
            else
            {
                writer.Write(7);
                for (var i = 0; i < RuntimePaths.Length; i++)
                {
                    writer.Write(defect == "unknown-type" && i == 6 ? 99 : i < 3 ? 1 : i - 1);
                    writer.Write(0x13570000 + i); // Arbitrary flags must survive exactly.
                    PathString(writer, defect == "metadata-mismatch" && i == 0 ? "Old/other.png" : RuntimePaths[i][0]);
                }
            }
        });
        var editor = Payload(writer =>
        {
            string[] keys = ["EffekseerProject", "ToolVersion", "ColorTexture", "NormalTexture", "Texture", "MaterialPath", "ModelPath", "Comment"];
            string[] values = [release, RuntimePaths[0][0], RuntimePaths[1][0], RuntimePaths[2][0],
                defect == "distant-missing-editor" ? "../../../../missing-former-project/material.efkmat" : RuntimePaths[5][0], RuntimePaths[4][0],
                defect == "unknown-editor-field" ? RuntimePaths[0][0] : "preserve comment"];
            foreach (var strings in new[] { keys, values })
            {
                writer.Write((short)strings.Length);
                for (short i = 0; i < strings.Length; i++)
                { var utf8 = Encoding.UTF8.GetBytes(strings[i]); writer.Write((ushort)utf8.Length); writer.Write(utf8); writer.Write(i); }
            }
            writer.Write((short)1); writer.Write((short)0); writer.Write(0); writer.Write(1); writer.Write((short)values.Length);
            for (short i = 0; i < values.Length; i++)
            { writer.Write((short)(i + 1)); writer.Write(1); writer.Write(i); writer.Write(0); }
        });
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true)) zlib.Write(editor);
        return Payload(writer =>
        {
            void Chunk(string id, byte[] data) { writer.Write(Encoding.ASCII.GetBytes(id)); writer.Write(data.Length); writer.Write(data); }
            writer.Write(Encoding.ASCII.GetBytes("EFKE")); writer.Write(0);
            Chunk("INFO", info); Chunk("EDIT", compressed.ToArray());
            foreach (var runtime in runtimes)
                Chunk("BIN_", Payload(binary =>
                {
                    binary.Write(Encoding.ASCII.GetBytes("SKFE")); binary.Write(runtime);
                    foreach (var paths in RuntimePaths.Take(runtime == 1500 ? 6 : 7))
                    { binary.Write(paths.Length); foreach (var path in paths) PathString(binary, path); }
                    binary.Write(NodePayload);
                }));
            if (defect == "unknown-chunk") Chunk("NEW_", [1, 2, 3]);
        });
    }

    private static byte[] ReadRuntimeTail(byte[] bytes, ChunkInfo chunk)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes));
        reader.BaseStream.Position = chunk.Offset + 8;
        for (var table = 0; table < (chunk.Version == 1500 ? 6 : 7); table++)
        {
            var count = reader.ReadInt32();
            for (var i = 0; i < count; i++) reader.ReadBytes(reader.ReadInt32() * 2);
        }
        return reader.ReadBytes((int)(chunk.Offset + chunk.Size - reader.BaseStream.Position));
    }

    private static int[] ReadInfoFlags(byte[] bytes, ParsedDocument document)
    {
        var info = document.Chunks.Single(c => c.Id == "INFO");
        var offset = info.Offset + 4;
        var count = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
        offset += 4;
        var flags = new int[count];
        for (var i = 0; i < count; i++)
        {
            flags[i] = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 4));
            offset += 8;
            offset += 4 + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset)) * 2;
        }
        return flags;
    }
}

public sealed class VersionSampleFactAttribute : FactAttribute
{
    public VersionSampleFactAttribute()
    {
        var root = Environment.GetEnvironmentVariable("EFFEKSEER_VERSION_SAMPLE_ROOT");
        if (root is null || !File.Exists(Path.Combine(root, "mega.efkefc")))
            Skip = "Set EFFEKSEER_VERSION_SAMPLE_ROOT to the local Mega 1.80.3 effect/asset folder for real-file integration validation.";
    }
}
