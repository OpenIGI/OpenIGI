namespace OpenIGI.Core.Numerics;

/// <summary>
/// Marsaglia's 32-bit xorshift (13, 17, 5): the small stream the remaster's own effects draw from -- lamp flicker, the
/// flashlight's chatter, the synthesised clicks -- where nothing of the original's random state may be touched.
/// </summary>
/// <remarks>
/// Never a substitute for <see cref="IgiRandom"/>: the original's streams are part of the simulation, and drawing from one for
/// an effect would move every soldier's next decision. A caller keeps its own state word, seeded to anything but nought.
/// </remarks>
public static class XorShift32
{
    /// <summary>Steps the state once.</summary>
    /// <param name="state">The state, which must not be nought; it is advanced.</param>
    /// <returns>The new state.</returns>
    public static uint Next(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    /// <summary>Steps the state and returns its top 24 bits as a fraction in [0, 1).</summary>
    /// <param name="state">The state; it is advanced.</param>
    /// <returns>The fraction.</returns>
    public static float NextUnit(ref uint state) => (Next(ref state) >> 8) * (1f / 16777216f);

    /// <summary>Steps the state and returns its low 16 bits spread over [-1, 1]: white noise for a synthesised sound.</summary>
    /// <param name="state">The state; it is advanced.</param>
    /// <returns>The sample.</returns>
    public static float NextSigned(ref uint state) => ((Next(ref state) & 0xFFFF) / 32767.5f) - 1f;
}
