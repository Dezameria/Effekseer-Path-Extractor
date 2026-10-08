using System.Buffers.Binary;
using System.Text.Json;
using ResourceManager.Core.Models;

namespace ResourceManager.Infrastructure.Effekseer;

internal static class ModelResourceReader
{
    public static ParsedDocument ReadGlb(string path, byte[] bytes)
    {
        var reader = new BoundedBinaryReader(bytes);
        if (reader.Int32() != 0x46546c67 || reader.Int32() != 2 || reader.Int32() != bytes.Length)
            throw new InvalidDataException("Invalid GLB 2 header/length.");
        var chunks = new List<ChunkInfo>();
        var references = new List<ResourceReference>();
        var jsonSeen = false;
        var binCount = 0;
        while (reader.Remaining > 0)
        {
            var size = reader.Int32(); var type = reader.Int32();
            if (size < 0 || size % 4 != 0 || chunks.Count >= 4096) throw new InvalidDataException("Invalid GLB chunk length/count.");
            var chunk = new ChunkInfo(type == 0x4e4f534a ? "JSON" : type == 0x004e4942 ? "BIN" : "Unknown", reader.Position, size);
            var payload = reader.ReadBytes(size); chunks.Add(chunk);
            if (type == 0x004e4942 && ++binCount > 1) throw new InvalidDataException("Duplicate GLB BIN chunk.");
            if (type != 0x4e4f534a) continue;
            if (jsonSeen || chunks.Count != 1) throw new InvalidDataException("GLB must start with a single JSON chunk.");
            jsonSeen = true;
            using var json = JsonDocument.Parse(payload);
            if (json.RootElement.GetProperty("asset").GetProperty("version").GetString() != "2.0") throw new InvalidDataException("Unsupported glTF asset version.");
            foreach (var table in new[] { "buffers", "images" })
            {
                if (!json.RootElement.TryGetProperty(table, out var entries)) continue;
                var i = 0;
                foreach (var entry in entries.EnumerateArray())
                {
                    if (entry.TryGetProperty("uri", out var uriValue) && uriValue.GetString() is { } uri && !uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        if (Uri.TryCreate(uri, UriKind.Absolute, out _)) throw new InvalidDataException("Remote/absolute GLB URI is not supported: " + uri);
                        references.Add(new(path, $"JSON/{table}/{i}/uri", Uri.UnescapeDataString(uri), table == "images" ? ResourceType.Texture : ResourceType.Model, ReferenceRole.Runtime));
                    }
                    i++;
                }
            }
        }
        if (!jsonSeen) throw new InvalidDataException("Missing GLB JSON.");
        return new(path, "GLB", 2, null, null, ScanCoverage.MetadataOnly, false, chunks, references,
            [new("GlbInspection", "GLB buffer/image URIs inspected; embedded binary payload is preserved. Geometry/rendering not certified.")]);
    }

    public static ParsedDocument ReadModel(string path, byte[] bytes)
    {
        if (bytes.Length < 20) throw new InvalidDataException("Truncated efkmodel header.");
        var version = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        if (version != 6) return new(path, "EFKMODEL", version, null, null, ScanCoverage.Unsupported, false, [], [],
            [new("UnsupportedModel", $"efkmodel version {version} is not verified.", true)]);
        var reader = new BoundedBinaryReader(bytes);
        reader.Int32(); reader.Int32(); reader.Int32();
        var frames = reader.Count();
        for (var frame = 0; frame < frames; frame++)
        {
            var vertices = reader.Count(); reader.ReadBytes(checked(vertices * 68));
            var faces = reader.Count(); reader.ReadBytes(checked(faces * 12));
        }
        reader.End();
        return new(path, "EFKMODEL", version, null, null, ScanCoverage.MetadataOnly, false, [], [],
            [new("ModelInspection", "efkmodel 6 geometry structure inspected. No resource paths in this layout; geometry bytes are preserved.")]);
    }
}
