using System.Buffers.Binary;
using System.Text;

namespace RuntimePackage.Engine.Packaging;

internal sealed record SetupBundleFooter(long PayloadOffset, long PayloadLength, string PayloadSha256)
{
    private const int Sha256HexLength = 64;
    private static readonly byte[] MagicBytes = Encoding.ASCII.GetBytes("VRPSETUP");
    public static int Length => MagicBytes.Length + sizeof(long) + sizeof(long) + Sha256HexLength;

    public byte[] ToBytes()
    {
        if (PayloadSha256.Length != Sha256HexLength)
        {
            throw new InvalidOperationException("payload sha256 长度无效。");
        }

        var bytes = new byte[Length];
        MagicBytes.CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(MagicBytes.Length), PayloadOffset);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(MagicBytes.Length + sizeof(long)), PayloadLength);
        Encoding.ASCII.GetBytes(PayloadSha256, bytes.AsSpan(MagicBytes.Length + sizeof(long) + sizeof(long)));
        return bytes;
    }

    public static SetupBundleFooter FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Length)
        {
            throw new InvalidOperationException("setup bundle footer 长度无效。");
        }

        if (!bytes[..MagicBytes.Length].SequenceEqual(MagicBytes))
        {
            throw new InvalidOperationException("未找到 setup bundle payload。");
        }

        var payloadOffset = BinaryPrimitives.ReadInt64LittleEndian(bytes.Slice(MagicBytes.Length, sizeof(long)));
        var payloadLength = BinaryPrimitives.ReadInt64LittleEndian(bytes.Slice(MagicBytes.Length + sizeof(long), sizeof(long)));
        var sha256 = Encoding.ASCII.GetString(bytes.Slice(MagicBytes.Length + sizeof(long) + sizeof(long), Sha256HexLength));

        if (payloadOffset < 0 || payloadLength <= 0)
        {
            throw new InvalidOperationException("setup bundle payload 位置无效。");
        }

        return new SetupBundleFooter(payloadOffset, payloadLength, sha256);
    }
}
