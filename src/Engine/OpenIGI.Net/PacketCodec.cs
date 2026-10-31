using System.Buffers.Binary;
using System.Text;

namespace OpenIGI.Net;

/// <summary>Seals and checks the five bytes every packet starts with.</summary>
/// <remarks>
/// The CRC covers the protocol id too, which is never sent: a packet from another game, another version of this one, or
/// line noise fails the check and is dropped before anything parses it.
/// </remarks>
internal static class PacketCodec
{
    public static void Seal(Span<byte> packet, PacketKind kind, ulong protocolId)
    {
        packet[0] = (byte)kind;
        BinaryPrimitives.WriteUInt32LittleEndian(packet[1..], Checksum(packet, protocolId));
    }

    public static bool TryOpen(ReadOnlySpan<byte> packet, ulong protocolId, out PacketKind kind)
    {
        kind = default;
        if (packet.Length < NetConstants.PacketPrefixSize)
        {
            return false;
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(packet[1..]) != Checksum(packet, protocolId))
        {
            return false;
        }

        kind = (PacketKind)packet[0];
        return true;
    }

    public static int WriteString(Span<byte> destination, string value)
    {
        int max = Math.Min(255, destination.Length - 1);
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        int length = Math.Min(bytes.Length, max);
        destination[0] = (byte)length;
        bytes.AsSpan(0, length).CopyTo(destination[1..]);
        return length + 1;
    }

    public static string ReadString(ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty)
        {
            return string.Empty;
        }

        int length = Math.Min(source[0], source.Length - 1);
        return Encoding.UTF8.GetString(source.Slice(1, length));
    }

    private static uint Checksum(ReadOnlySpan<byte> packet, ulong protocolId)
    {
        Span<byte> id = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(id, protocolId);
        uint crc = Crc32.Append(uint.MaxValue, id);
        crc = Crc32.Append(crc, packet[..1]);
        crc = Crc32.Append(crc, packet[NetConstants.PacketPrefixSize..]);
        return ~crc;
    }
}
