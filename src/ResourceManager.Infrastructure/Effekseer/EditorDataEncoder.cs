using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace ResourceManager.Infrastructure.Effekseer;

internal static class EditorDataEncoder
{
    public static byte[] Encode(XDocument document)
    {
        var keys = new Dictionary<string, short>(StringComparer.Ordinal);
        var values = new Dictionary<string, short>(StringComparer.Ordinal);
        using var tree = new MemoryStream();
        using var writer = new BinaryWriter(tree, Encoding.UTF8, true);
        short Index(Dictionary<string, short> table, string text)
        {
            if (table.TryGetValue(text, out var index)) return index;
            if (table.Count >= short.MaxValue) throw new InvalidDataException("EDIT string table exceeds supported size.");
            index = (short)table.Count;
            table.Add(text, index);
            return index;
        }
        void WriteElements(IEnumerable<XElement> elements)
        {
            var children = elements.ToArray();
            if (children.Length > short.MaxValue) throw new InvalidDataException("Too many EDIT siblings.");
            writer.Write((short)children.Length);
            foreach (var element in children)
            {
                if (element.HasAttributes) throw new InvalidDataException("Unsupported EDIT attributes.");
                writer.Write(Index(keys, element.Name.LocalName));
                var hasValue = !element.HasElements && !string.IsNullOrEmpty(element.Value);
                writer.Write(hasValue ? 1 : 0);
                if (hasValue) writer.Write(Index(values, element.Value));
                writer.Write(element.HasElements ? 1 : 0);
                if (element.HasElements) WriteElements(element.Elements());
            }
        }
        WriteElements(document.Elements());
        using var encoded = new MemoryStream();
        using (var binary = new BinaryWriter(encoded, Encoding.UTF8, true))
        {
            void WriteTable(Dictionary<string, short> table)
            {
                binary.Write((short)table.Count);
                foreach (var (text, index) in table)
                {
                    var bytes = Encoding.UTF8.GetBytes(text);
                    if (bytes.Length > ushort.MaxValue) throw new InvalidDataException("EDIT string too long.");
                    binary.Write((ushort)bytes.Length);
                    binary.Write(bytes);
                    binary.Write(index);
                }
            }
            WriteTable(keys);
            WriteTable(values);
            binary.Write(tree.ToArray());
        }
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true)) zlib.Write(encoded.ToArray());
        return compressed.ToArray();
    }
}
