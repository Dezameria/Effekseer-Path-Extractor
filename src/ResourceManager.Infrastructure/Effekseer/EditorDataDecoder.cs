using System.IO.Compression;
using System.Xml.Linq;

namespace ResourceManager.Infrastructure.Effekseer;

internal static class EditorDataDecoder
{
    public static XDocument Decode(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = zlib.Read(buffer)) > 0)
        {
            if (output.Length + count > 32 * 1024 * 1024) throw new InvalidDataException("EDIT exceeds 32 MiB decompression limit.");
            output.Write(buffer, 0, count);
        }
        var reader = new BoundedBinaryReader(output.ToArray());
        var keys = ReadTable(reader);
        var values = ReadTable(reader);
        var total = 0;
        List<XElement> ReadElements(int depth)
        {
            if (depth > 128) throw new InvalidDataException("EDIT nesting exceeds limit.");
            var length = reader.Int16();
            if (length < 0) throw new InvalidDataException("Invalid EDIT child count.");
            var nodes = new List<XElement>();
            for (var i = 0; i < length; i++)
            {
                if (++total > 250_000) throw new InvalidDataException("EDIT node count exceeds limit.");
                var key = reader.Int16();
                if (!keys.TryGetValue(key, out var name)) throw new InvalidDataException("Unknown EDIT name index.");
                var element = new XElement(name);
                if (reader.Boolean())
                {
                    if (!values.TryGetValue(reader.Int16(), out var value)) throw new InvalidDataException("Unknown EDIT value index.");
                    element.Value = value;
                }
                if (reader.Boolean()) element.Add(ReadElements(depth + 1));
                nodes.Add(element);
            }
            return nodes;
        }
        var roots = ReadElements(0);
        reader.End();
        if (roots.Count != 1) throw new InvalidDataException("EDIT must contain one document root.");
        return new XDocument(roots[0]);
    }

    private static Dictionary<short, string> ReadTable(BoundedBinaryReader reader)
    {
        var count = reader.Int16();
        if (count < 0) throw new InvalidDataException("Invalid EDIT string table length.");
        var table = new Dictionary<short, string>();
        for (var i = 0; i < count; i++)
        {
            var text = reader.Utf8Short();
            var key = reader.Int16();
            if (!table.TryAdd(key, text)) throw new InvalidDataException("Duplicate EDIT string table index.");
        }
        return table;
    }
}
