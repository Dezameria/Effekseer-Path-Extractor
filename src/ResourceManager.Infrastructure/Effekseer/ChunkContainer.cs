using System.Text;
using ResourceManager.Core.Models;

namespace ResourceManager.Infrastructure.Effekseer;

internal sealed record ChunkBlock(ChunkInfo Info, byte[] Data);

internal static class ChunkContainer
{
    public static IReadOnlyList<ChunkBlock> Read(byte[] data, int headerSize)
    {
        if (data.Length < headerSize) throw new InvalidDataException("Truncated container header.");
        var reader = new BoundedBinaryReader(data);
        reader.ReadBytes(headerSize);
        var chunks = new List<ChunkBlock>();
        while (reader.Remaining > 0)
        {
            if (chunks.Count >= 4096) throw new InvalidDataException("Too many chunks.");
            var idBytes = reader.ReadBytes(4);
            if (idBytes.Any(b => b < 32 || b > 126)) throw new InvalidDataException("Invalid chunk identifier.");
            var id = Encoding.ASCII.GetString(idBytes);
            var size = reader.Int32();
            var offset = reader.Position;
            chunks.Add(new(new(id, offset, size), reader.ReadBytes(size)));
        }
        return chunks;
    }

    public static ChunkBlock RequiredSingle(IReadOnlyList<ChunkBlock> chunks, string id)
    {
        var matches = chunks.Where(c => c.Info.Id == id).ToArray();
        return matches.Length == 1 ? matches[0] : throw new InvalidDataException($"Expected one {id} chunk; found {matches.Length}.");
    }
}
