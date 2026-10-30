namespace OpenIGI.Net;

/// <summary>Comparisons of 16-bit sequence numbers that wrap around.</summary>
/// <remarks>
/// A sequence is newer than another when it is ahead of it by less than half the ring, so 2 is newer than 65534. Every
/// counter on the wire (packet sequences, reliable message ids) is compared this way and never with plain operators.
/// </remarks>
public static class SequenceMath
{
    /// <summary>Gets whether <paramref name="a"/> is ahead of <paramref name="b"/>.</summary>
    /// <param name="a">The sequence asked about.</param>
    /// <param name="b">The sequence it is compared with.</param>
    /// <returns><see langword="true"/> when <paramref name="a"/> is the newer of the two.</returns>
    public static bool IsNewer(ushort a, ushort b) =>
        ((a > b) && (a - b <= 32768)) || ((a < b) && (b - a > 32768));

    /// <summary>Gets how far <paramref name="a"/> is ahead of <paramref name="b"/>, negative when it is behind.</summary>
    /// <param name="a">The sequence asked about.</param>
    /// <param name="b">The sequence it is measured from.</param>
    /// <returns>The signed distance, from -32768 to 32767.</returns>
    public static int Distance(ushort a, ushort b) => unchecked((short)(a - b));
}
