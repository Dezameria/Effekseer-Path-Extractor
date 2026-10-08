using System.Buffers.Binary;
using System.Text;

namespace ResourceManager.Infrastructure.Effekseer;

internal sealed class BoundedBinaryReader(byte[] data)
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private static readonly Encoding Utf16 = new UnicodeEncoding(false, false, true);
    public int Position { get; private set; }
    public int Remaining => data.Length - Position;
    public byte[] ReadBytes(int length)
    {
        if (length < 0 || length > Remaining) throw new InvalidDataException($"Truncated field at offset {Position}, length {length}.");
        var result = data.AsSpan(Position, length).ToArray();
        Position += length;
        return result;
    }
    public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(ReadBytes(4));
    public short Int16() => BinaryPrimitives.ReadInt16LittleEndian(ReadBytes(2));
    public ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(ReadBytes(2));
    public int Count()
    {
        var count = Int32();
        if (count is < 0 or > 100_000) throw new InvalidDataException($"Invalid count {count}.");
        return count;
    }
    public bool Boolean()
    {
        var value = Int32();
        if (value is not (0 or 1)) throw new InvalidDataException("Invalid encoded boolean.");
        return value == 1;
    }
    public string Utf8Short() => Utf8.GetString(ReadBytes(UInt16()));
    public string Utf8Zero()
    {
        var length = Int32();
        var bytes = ReadBytes(length);
        if (length < 1 || bytes[^1] != 0) throw new InvalidDataException("Invalid UTF-8 terminator.");
        return Utf8.GetString(bytes, 0, length - 1);
    }
    public string Utf16Zero()
    {
        var chars = Int32();
        if (chars < 1 || chars > Remaining / 2) throw new InvalidDataException("Invalid UTF-16 string length.");
        var bytes = ReadBytes(checked(chars * 2));
        if (bytes[^1] != 0 || bytes[^2] != 0) throw new InvalidDataException("Invalid UTF-16 terminator.");
        return Utf16.GetString(bytes, 0, bytes.Length - 2);
    }
    public void End()
    { if (Remaining != 0) throw new InvalidDataException($"Unexpected trailing bytes: {Remaining}."); }
}
