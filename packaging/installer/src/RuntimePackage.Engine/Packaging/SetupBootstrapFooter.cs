using System.Text;

namespace RuntimePackage.Engine.Packaging;

public sealed record SetupBootstrapFooter(
    long HostOffset,
    long HostLength,
    string HostSha256,
    long PackageOffset,
    long PackageLength,
    string PackageSha256)
{
    private const int Sha256HexLength = 64;
    private static readonly byte[] MagicBytes = Encoding.ASCII.GetBytes("VRPBOOT1");
    public static int Length => MagicBytes.Length + sizeof(long) + sizeof(long) + Sha256HexLength + sizeof(long) + sizeof(long) + Sha256HexLength;

    public byte[] ToBytes()
    {
        var bytes = new byte[Length];
        MagicBytes.CopyTo(bytes, 0);

        var offset = MagicBytes.Length;
        WriteInt64(bytes, ref offset, HostOffset);
        WriteInt64(bytes, ref offset, HostLength);
        WriteSha256(bytes, ref offset, HostSha256);
        WriteInt64(bytes, ref offset, PackageOffset);
        WriteInt64(bytes, ref offset, PackageLength);
        WriteSha256(bytes, ref offset, PackageSha256);
        return bytes;
    }

    public static SetupBootstrapFooter FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Length)
        {
            throw new InvalidOperationException("bootstrapper footer 长度无效。");
        }

        if (!bytes[..MagicBytes.Length].SequenceEqual(MagicBytes))
        {
            throw new InvalidOperationException("未找到 bootstrapper 负载。");
        }

        var offset = MagicBytes.Length;
        var hostOffset = ReadInt64(bytes, ref offset);
        var hostLength = ReadInt64(bytes, ref offset);
        var hostSha256 = ReadSha256(bytes, ref offset);
        var packageOffset = ReadInt64(bytes, ref offset);
        var packageLength = ReadInt64(bytes, ref offset);
        var packageSha256 = ReadSha256(bytes, ref offset);

        return new SetupBootstrapFooter(hostOffset, hostLength, hostSha256, packageOffset, packageLength, packageSha256);
    }

    private static void WriteInt64(byte[] bytes, ref int offset, long value)
    {
        BitConverter.TryWriteBytes(bytes.AsSpan(offset, sizeof(long)), value);
        offset += sizeof(long);
    }

    private static long ReadInt64(ReadOnlySpan<byte> bytes, ref int offset)
    {
        var value = BitConverter.ToInt64(bytes.Slice(offset, sizeof(long)));
        offset += sizeof(long);
        return value;
    }

    private static void WriteSha256(byte[] bytes, ref int offset, string value)
    {
        if (value.Length != Sha256HexLength)
        {
            throw new InvalidOperationException("sha256 长度无效。");
        }

        Encoding.ASCII.GetBytes(value, bytes.AsSpan(offset, Sha256HexLength));
        offset += Sha256HexLength;
    }

    private static string ReadSha256(ReadOnlySpan<byte> bytes, ref int offset)
    {
        var value = Encoding.ASCII.GetString(bytes.Slice(offset, Sha256HexLength));
        offset += Sha256HexLength;
        return value;
    }
}
