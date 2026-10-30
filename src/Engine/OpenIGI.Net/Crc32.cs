namespace OpenIGI.Net;

/// <summary>The IEEE 802.3 CRC-32 every packet is checked with.</summary>
public static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    /// <summary>Continues a CRC over more bytes.</summary>
    /// <param name="crc">The running value; start from <see cref="uint.MaxValue"/>.</param>
    /// <param name="data">The bytes.</param>
    /// <returns>The running value after them; complement it to finish.</returns>
    public static uint Append(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    /// <summary>Computes the CRC of some bytes.</summary>
    /// <param name="data">The bytes.</param>
    /// <returns>The finished CRC.</returns>
    public static uint Compute(ReadOnlySpan<byte> data) => ~Append(uint.MaxValue, data);

    private static uint[] BuildTable()
    {
        uint[] table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[i] = c;
        }

        return table;
    }
}
