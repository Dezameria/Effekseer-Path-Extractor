using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using ResourceManager.Infrastructure.Effekseer;
using ResourceManager.Core.Models;

namespace ResourceManager.Tests;

public sealed class ResourceFormatTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(1610)]
    [InlineData(1710)]
    [InlineData(1800)]
    public async Task MaterialDefaultCatalogueAndTextureNodesRewriteWithoutChangingShaderOrOtherValues(int version)
    {
        using var fixture = new TestWorkspace(); var path = fixture.File("material.efkmat");
        const string oldPath = "../Old/สี.png", newPath = "../Textures/new texture.png";
        var bytes = Material(version, oldPath); await File.WriteAllBytesAsync(path, bytes);
        var parser = new EffekseerResourceParser(); var before = await parser.ParseAsync(path);
        Assert.DoesNotContain(before.Diagnostics, d => d.IsError); Assert.True(MaterialReferenceWriter.Supports(before));
        Assert.Equal(4, before.References.Count);
        var changed = new MaterialReferenceWriter().Rewrite(bytes, before, new Dictionary<string, string> { [oldPath] = newPath });
        var output = fixture.File("changed.efkmat"); await File.WriteAllBytesAsync(output, changed);
        var after = await parser.ParseAsync(output); Assert.DoesNotContain(after.Diagnostics, d => d.IsError);
        Assert.All(after.References, r => Assert.Equal(newPath, r.OriginalReference));
        Assert.Equal(bytes.AsSpan(0, 16).ToArray(), changed.AsSpan(0, 16).ToArray());
        var gene = before.Chunks.Single(c => c.Id == "GENE"); var newGene = after.Chunks.Single(c => c.Id == "GENE");
        Assert.Equal(bytes.AsSpan(gene.Offset, gene.Size).ToArray(), changed.AsSpan(newGene.Offset, newGene.Size).ToArray());
        var data = after.Chunks.Single(c => c.Id == "DATA"); var json = JsonNode.Parse(changed.AsSpan(data.Offset, data.Size - 1))!;
        Assert.Equal(oldPath, json["Comment"]!.GetValue<string>());
        Assert.Equal(17, json["Nodes"]![0]!["UnrelatedValue"]!.GetValue<int>());
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task UnknownMaterialChunkIsReadableButNotWritable()
    {
        using var fixture = new TestWorkspace(); var path = fixture.File("future.efkmat");
        var bytes = Material(1800, "texture.png");
        using var stream = new MemoryStream(); stream.Write(bytes);
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) { writer.Write(Encoding.ASCII.GetBytes("NEW_")); writer.Write(1); writer.Write((byte)7); }
        await File.WriteAllBytesAsync(path, stream.ToArray()); var document = await new EffekseerResourceParser().ParseAsync(path);
        Assert.False(MaterialReferenceWriter.Supports(document));
        Assert.Throws<InvalidDataException>(() => new MaterialReferenceWriter().Rewrite(stream.ToArray(), document, new Dictionary<string, string>()));
    }

    [Fact]
    public async Task GlbEmbeddedPayloadAndPercentEncodedExternalUrisAreInspectedWithoutModification()
    {
        using var fixture = new TestWorkspace(); var embedded = fixture.File("embedded.glb"); var bytes = Glb(null); await File.WriteAllBytesAsync(embedded, bytes);
        var parser = new EffekseerResourceParser(); var document = await parser.ParseAsync(embedded);
        Assert.Equal("GLB", document.Format); Assert.Empty(document.References); Assert.DoesNotContain(document.Diagnostics, d => d.IsError);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(embedded));
        var external = fixture.File("external.glb"); await File.WriteAllBytesAsync(external, Glb("Textures/image%20file.png"));
        var parsed = await parser.ParseAsync(external); var reference = Assert.Single(parsed.References);
        Assert.Equal("Textures/image file.png", reference.OriginalReference); Assert.Equal(ResourceType.Texture, reference.Type);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("length")]
    [InlineData("chunk-length")]
    [InlineData("truncated")]
    public async Task CorruptGlbHasExplicitError(string defect)
    {
        using var fixture = new TestWorkspace(); var path = fixture.File("bad.glb"); var bytes = Glb(null);
        if (defect == "version") BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 3);
        if (defect == "length") BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), bytes.Length + 4);
        if (defect == "chunk-length") BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), int.MaxValue);
        if (defect == "truncated") bytes = bytes[..^4];
        await File.WriteAllBytesAsync(path, bytes); var result = await new EffekseerResourceParser().ParseAsync(path);
        Assert.Equal("GLB", result.Format); Assert.Contains(result.Diagnostics, d => d.IsError); Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Theory]
    [InlineData(6, false)]
    [InlineData(6, true)]
    [InlineData(5, false)]
    public async Task ModelSixGeometryIsBoundedAndOtherLayoutsAreRejected(int version, bool truncated)
    {
        using var fixture = new TestWorkspace(); var path = fixture.File("model.efkmodel");
        using var stream = new MemoryStream(); using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        { writer.Write(version); writer.Write(1f); writer.Write(1); writer.Write(1); writer.Write(1); writer.Write(new byte[68]); writer.Write(1); writer.Write(new byte[12]); }
        var bytes = truncated ? stream.ToArray()[..^1] : stream.ToArray(); await File.WriteAllBytesAsync(path, bytes);
        var parsed = await new EffekseerResourceParser().ParseAsync(path); Assert.Empty(parsed.References);
        Assert.Equal(truncated || version != 6, parsed.Diagnostics.Any(d => d.IsError)); Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    private static byte[] Material(int version, string path)
    {
        using var parameter = new MemoryStream();
        using (var writer = new BinaryWriter(parameter, Encoding.UTF8, true))
        {
            writer.Write(new byte[20]); if (version >= 1710) { writer.Write(1); writer.Write(42); }
            writer.Write(1);
            foreach (var text in new[] { "Color texture", "uColor", path }) { var utf8 = Encoding.UTF8.GetBytes(text); writer.Write(utf8.Length + 1); writer.Write(utf8); writer.Write((byte)0); }
            writer.Write(new byte[20]); writer.Write(0x12345678);
        }
        var json = new JsonObject { ["Project"] = "EffekseerMaterial", ["Comment"] = path,
            ["Textures"] = new JsonArray(new JsonObject { ["Path"] = path }),
            ["Nodes"] = new JsonArray(new JsonObject { ["Type"] = "SampleTexture", ["UnrelatedValue"] = 17, ["Props"] = new JsonArray(new JsonObject { ["Value"] = path }) },
                new JsonObject { ["Type"] = "TextureObjectParameter", ["Props"] = new JsonArray(new JsonObject { ["Value"] = 0 }, new JsonObject { ["Value"] = 1 }, new JsonObject { ["Value"] = path }) }) };
        using var output = new MemoryStream(); using var binary = new BinaryWriter(output);
        binary.Write(Encoding.ASCII.GetBytes("EFKM")); binary.Write(version); binary.Write(123456789L);
        void Chunk(string id, byte[] payload) { binary.Write(Encoding.ASCII.GetBytes(id)); binary.Write(payload.Length); binary.Write(payload); }
        Chunk("PRM_", parameter.ToArray()); Chunk("GENE", Encoding.UTF8.GetBytes("shader code\0")); Chunk("DATA", [.. Encoding.UTF8.GetBytes(json.ToJsonString()), 0]);
        return output.ToArray();
    }
    private static byte[] Glb(string? uri)
    {
        var root = new JsonObject { ["asset"] = new JsonObject { ["version"] = "2.0" }, ["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = 4 }) };
        if (uri is not null) root["images"] = new JsonArray(new JsonObject { ["uri"] = uri });
        var data = Encoding.UTF8.GetBytes(root.ToJsonString()); var padded = (data.Length + 3) / 4 * 4;
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(0x46546c67); writer.Write(2); writer.Write(12 + 8 + padded + 8 + 4); writer.Write(padded); writer.Write(0x4e4f534a); writer.Write(data);
        for (var i = data.Length; i < padded; i++) writer.Write((byte)32);
        writer.Write(4); writer.Write(0x004e4942); writer.Write(new byte[] { 1, 2, 3, 4 }); return stream.ToArray();
    }
}
