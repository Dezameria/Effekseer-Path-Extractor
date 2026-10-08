using System.Text;
using System.Xml.Linq;
using ResourceManager.Core.Models;

namespace ResourceManager.Infrastructure.Effekseer;

/// <summary>Only rewrites texture path fields in verified EFKE layouts. Runtime node payloads remain byte-identical.</summary>
public sealed class TextureReferenceWriter
{
    public static bool Supports(ParsedDocument document) => EffectFormatCompatibility.Evaluate(document).CanMap;

    public byte[] Rewrite(byte[] source, ParsedDocument document, IReadOnlyDictionary<string, string> mappings)
        => Rewrite(source, document, mappings, false);

    internal byte[] Rewrite(byte[] source, ParsedDocument document, IReadOnlyDictionary<string, string> mappings, bool allResources)
    {
        var compatibility = EffectFormatCompatibility.Evaluate(document);
        if (!compatibility.CanMap) throw new InvalidDataException(compatibility.Rejection(document));
        var known = document.References.Where(r => allResources || r.Type == ResourceType.Texture).Select(r => r.OriginalReference).ToHashSet(StringComparer.Ordinal);
        if (mappings.Keys.Any(key => !known.Contains(key))) throw new InvalidDataException("Mapping contains a texture reference absent from the effect.");
        var chunks = ChunkContainer.Read(source, 8);
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        writer.Write(source.AsSpan(0, 8));
        foreach (var chunk in chunks)
        {
            var payload = chunk.Info.Id switch
            {
                "INFO" => RewriteTables(chunk.Data, false, mappings, allResources),
                "BIN_" => RewriteTables(chunk.Data, true, mappings, allResources),
                "EDIT" => RewriteEditor(chunk.Data, mappings, allResources),
                _ => throw new InvalidDataException("Unknown chunks are not writable.")
            };
            writer.Write(Encoding.ASCII.GetBytes(chunk.Info.Id));
            writer.Write(payload.Length);
            writer.Write(payload);
        }
        return output.ToArray();
    }

    internal static byte[] RewriteTables(byte[] bytes, bool runtime, IReadOnlyDictionary<string, string> mappings, bool allResources = false)
    {
        var reader = new BoundedBinaryReader(bytes);
        using var result = new MemoryStream();
        using var writer = new BinaryWriter(result, Encoding.UTF8, true);
        if (runtime)
        {
            var signature = reader.ReadBytes(4);
            if (Encoding.ASCII.GetString(signature) != "SKFE") throw new InvalidDataException("Invalid runtime signature.");
            writer.Write(signature);
        }
        var version = reader.Int32();
        writer.Write(version);
        void WritePath(bool texture)
        {
            var old = reader.Utf16Zero();
            var path = (texture || allResources) && mappings.TryGetValue(old, out var replacement) ? replacement : old;
            if (path.Contains('\0')) throw new InvalidDataException("Invalid texture path.");
            var buffer = Encoding.Unicode.GetBytes(path);
            writer.Write(buffer.Length / 2 + 1);
            writer.Write(buffer);
            writer.Write((ushort)0);
        }
        if (!runtime && EffectFormatCompatibility.IsTypedInfo(version))
        {
            var count = reader.Count();
            writer.Write(count);
            for (var i = 0; i < count; i++)
            {
                var type = reader.Int32();
                writer.Write(type);
                writer.Write(reader.Int32());
                WritePath(type == 1);
            }
        }
        else
        {
            if (runtime ? !EffectFormatCompatibility.IsKnownRuntime(version) : version != 1610)
                throw new InvalidDataException("Unsupported resource table version.");
            var tableCount = version == 1500 ? 6 : 7;
            for (var table = 0; table < tableCount; table++)
            {
                var count = reader.Count();
                writer.Write(count);
                for (var i = 0; i < count; i++) WritePath(table < 3);
            }
        }
        if (runtime) writer.Write(reader.ReadBytes(reader.Remaining)); // Node bytes and indices: unchanged.
        else reader.End();
        return result.ToArray();
    }

    private static byte[] RewriteEditor(byte[] bytes, IReadOnlyDictionary<string, string> mappings, bool allResources)
    {
        var original = EditorDataDecoder.Decode(bytes);
        var modified = new XDocument(original);
        foreach (var element in modified.Descendants().Where(e => !e.HasElements && mappings.ContainsKey(e.Value)))
        {
            if (!(IsTextureField(element) || allResources && IsResourceField(element))) throw new InvalidDataException($"Resource text appears in unverified EDIT field {element.Name}; manual inspection required.");
            element.Value = mappings[element.Value];
        }
        var encoded = EditorDataEncoder.Encode(modified);
        if (!XNode.DeepEquals(modified, EditorDataDecoder.Decode(encoded))) throw new InvalidDataException("EDIT round-trip validation failed.");
        return encoded;
    }

    private static bool IsTextureField(XElement element) => element.Name.LocalName is "ColorTexture" or "NormalTexture" or "Texture"
        || element.Name.LocalName == "Value" && element.Parent?.Name.LocalName == "KeyValue"
            && element.Parent.Parent?.Name.LocalName == "Texture"
        || element.Name.LocalName == "Path" && element.Ancestors().Any(e => e.Name.LocalName.Contains("Texture", StringComparison.Ordinal));

    private static bool IsResourceField(XElement element) => element.Name.LocalName == "Path" && element.Parent?.Name.LocalName == "MaterialFile"
        || element.Name.LocalName == "Model" && element.Parent?.Name.LocalName == "Model"
        || element.Name.LocalName == "ModelPath" && element.Ancestors().Any(e => e.Name.LocalName == "GpuParticles")
        || element.Name.LocalName == "Wave" && element.Ancestors().Any(e => e.Name.LocalName.Contains("Sound", StringComparison.Ordinal))
        || element.Name.LocalName == "Path" && element.Parent?.Name.LocalName == "Curve";
}
