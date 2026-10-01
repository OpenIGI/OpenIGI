namespace OpenIGI.Formats.Ai;

/// <summary>
/// The name hash a GraphStream addresses its fields by — <c>0x4F9E70</c>. Field names appear nowhere in a
/// <c>graph&lt;n&gt;.dat</c>; only this hash does.
/// </summary>
/// <remarks>
/// <para>
/// The hash is a weighted sum of the upper-cased characters: character <c>k</c> is multiplied by
/// <c>1 + 317k</c> and the products are added. The multiplier is kept in <b>sixteen bits</b> and wraps,
/// so it repeats every 207 characters; no shipped field name is remotely long enough to reach that.
/// </para>
/// <para>
/// Case is folded by subtracting 32 from <c>a</c>–<c>z</c> only, so a name containing any other
/// lower-case-adjacent byte hashes as written. Every shipped name is plain ASCII.
/// </para>
/// <para>
/// A collision is <b>fatal at registration</b>, not at read time: <c>0x4F9DF0</c> prints
/// <c>"GraphStream - collision in hash for %s"</c> when a second field hashes to a value already
/// registered. That is why the reader can treat the hash as an identity.
/// </para>
/// </remarks>
public static class GraphFieldHash
{
    /// <summary>The amount the per-character multiplier advances by, kept to sixteen bits.</summary>
    public const int MultiplierStep = 317;

    /// <summary>Hashes a field name.</summary>
    /// <param name="name">The field name, as the engine registers it.</param>
    /// <returns>The 32-bit hash the stream stores.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static uint Of(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        uint hash = 0;
        ushort multiplier = 1;
        foreach (char c in name)
        {
            // The original narrows to a signed char and folds only the ASCII lower-case run.
            int ch = (sbyte)c;
            if (ch is >= 'a' and <= 'z')
            {
                ch -= 32;
            }

            hash += (uint)(multiplier * ch);
            multiplier = (ushort)(multiplier + MultiplierStep);
        }

        return hash;
    }
}
