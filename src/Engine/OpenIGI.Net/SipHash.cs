using System.Buffers.Binary;
using System.Numerics;

namespace OpenIGI.Net;

/// <summary>SipHash-2-4, the keyed hash the server signs its connection challenges with.</summary>
/// <remarks>
/// Written out rather than taken from the cryptography namespace so it runs the same everywhere, the browser included.
/// It need only stop a client forging a challenge for an address it does not hold, which a keyed PRF does.
/// </remarks>
public static class SipHash
{
    /// <summary>Hashes some bytes under a 128-bit key.</summary>
    /// <param name="key0">The key's low half.</param>
    /// <param name="key1">The key's high half.</param>
    /// <param name="data">The bytes.</param>
    /// <returns>The 64-bit tag.</returns>
    public static ulong Compute(ulong key0, ulong key1, ReadOnlySpan<byte> data)
    {
        ulong v0 = 0x736f6d6570736575UL ^ key0;
        ulong v1 = 0x646f72616e646f6dUL ^ key1;
        ulong v2 = 0x6c7967656e657261UL ^ key0;
        ulong v3 = 0x7465646279746573UL ^ key1;

        int whole = data.Length & ~7;
        for (int i = 0; i < whole; i += 8)
        {
            ulong m = BinaryPrimitives.ReadUInt64LittleEndian(data[i..]);
            v3 ^= m;
            Round(ref v0, ref v1, ref v2, ref v3);
            Round(ref v0, ref v1, ref v2, ref v3);
            v0 ^= m;
        }

        ulong last = (ulong)data.Length << 56;
        for (int i = 0; i < data.Length - whole; i++)
        {
            last |= (ulong)data[whole + i] << (8 * i);
        }

        v3 ^= last;
        Round(ref v0, ref v1, ref v2, ref v3);
        Round(ref v0, ref v1, ref v2, ref v3);
        v0 ^= last;

        v2 ^= 0xFF;
        for (int i = 0; i < 4; i++)
        {
            Round(ref v0, ref v1, ref v2, ref v3);
        }

        return v0 ^ v1 ^ v2 ^ v3;
    }

    private static void Round(ref ulong v0, ref ulong v1, ref ulong v2, ref ulong v3)
    {
        v0 += v1;
        v1 = BitOperations.RotateLeft(v1, 13);
        v1 ^= v0;
        v0 = BitOperations.RotateLeft(v0, 32);
        v2 += v3;
        v3 = BitOperations.RotateLeft(v3, 16);
        v3 ^= v2;
        v0 += v3;
        v3 = BitOperations.RotateLeft(v3, 21);
        v3 ^= v0;
        v2 += v1;
        v1 = BitOperations.RotateLeft(v1, 17);
        v1 ^= v2;
        v2 = BitOperations.RotateLeft(v2, 32);
    }
}
