using System.Text;
using System.Text.Json.Nodes;
using ResourceManager.Core.Models;

namespace ResourceManager.Infrastructure.Effekseer;

/// <summary>Changes only material PRM_ default paths and documented DATA texture fields.
/// Shader code, node graph parameters, GUID and other chunks remain unchanged.</summary>
public sealed class MaterialReferenceWriter
{
    public static bool Supports(ParsedDocument document) => document.Format == "EFKM" && document.Version is 3 or 1610 or 1710 or 1800
        && !document.Diagnostics.Any(d => d.IsError) && document.Chunks.All(c => c.Id is "DESC" or "PRM_" or "PRM2" or "E_CD" or "GENE" or "DATA");

    public byte[] Rewrite(byte[] source, ParsedDocument document, IReadOnlyDictionary<string, string> mappings)
    {
        if (!Supports(document)) throw new InvalidDataException("Material layout is not supported: " + document.Path);
        var known = document.References.Where(r => r.Type == ResourceType.Texture).Select(r => r.OriginalReference).ToHashSet(StringComparer.Ordinal);
        if (mappings.Keys.Any(k => !known.Contains(k))) throw new InvalidDataException("Unknown material texture mapping.");
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write(source.AsSpan(0, 16));
        foreach (var chunk in ChunkContainer.Read(source, 16))
        {
            var data = chunk.Info.Id switch
            {
                "PRM_" => RewriteParameters(chunk.Data, document.Version!.Value, mappings),
                "DATA" => RewriteJson(chunk.Data, mappings),
                _ => chunk.Data
            };
            writer.Write(Encoding.ASCII.GetBytes(chunk.Info.Id)); writer.Write(data.Length); writer.Write(data);
        }
        return output.ToArray();
    }

    private static byte[] RewriteParameters(byte[] bytes, int version, IReadOnlyDictionary<string, string> mappings)
    {
        var reader = new BoundedBinaryReader(bytes);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(reader.ReadBytes(20));
        if (version >= 1710) { var functions = reader.Count(); writer.Write(functions); writer.Write(reader.ReadBytes(checked(functions * 4))); }
        var count = reader.Count(); writer.Write(count);
        void Text(string text) { if (text.Contains('\0')) throw new InvalidDataException("Invalid material path."); var data = Encoding.UTF8.GetBytes(text); writer.Write(data.Length + 1); writer.Write(data); writer.Write((byte)0); }
        for (var i = 0; i < count; i++)
        {
            Text(reader.Utf8Zero()); Text(reader.Utf8Zero());
            var path = reader.Utf8Zero(); Text(mappings.GetValueOrDefault(path, path));
            writer.Write(reader.ReadBytes(20));
        }
        writer.Write(reader.ReadBytes(reader.Remaining));
        return stream.ToArray();
    }

    private static byte[] RewriteJson(byte[] bytes, IReadOnlyDictionary<string, string> mappings)
    {
        var terminated = bytes.Length > 0 && bytes[^1] == 0;
        var json = JsonNode.Parse(bytes.AsSpan(0, bytes.Length - (terminated ? 1 : 0)))!.AsObject();
        void Replace(JsonNode node, string key)
        { if (node[key]?.GetValue<string>() is { } path && mappings.TryGetValue(path, out var replacement)) node[key] = replacement; }
        if (json["Textures"] is JsonArray textures) foreach (var texture in textures) if (texture is not null) Replace(texture, "Path");
        if (json["Nodes"] is JsonArray nodes) foreach (var node in nodes)
        {
            if (node is null) continue;
            var index = node["Type"]?.GetValue<string>() switch { "SampleTexture" or "TextureObject" => 0, "TextureObjectParameter" => 2, _ => -1 };
            if (index >= 0 && node["Props"] is JsonArray props && props.Count > index && props[index] is { } prop) Replace(prop, "Value");
        }
        var rewritten = Encoding.UTF8.GetBytes(json.ToJsonString());
        return terminated ? [.. rewritten, 0] : rewritten;
    }
}
