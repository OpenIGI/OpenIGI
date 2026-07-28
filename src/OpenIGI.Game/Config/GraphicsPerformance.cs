namespace OpenIGI.Game.Config;

/// <summary>
/// The mapping between a graphics performance <em>level</em> and the flag bitmask it stands for.
/// </summary>
/// <remarks>
/// <para>
/// The graphics screen has one slider with seven stops and three switches. The slider works in levels
/// and the switches in individual bits, and the two are reconciled through this table — which is why
/// moving the slider flips the switches and flipping a switch moves the slider.
/// </para>
/// <para>
/// From <c>Config_GraphicOptionsGetPerfFlagsFromLevel</c> (<c>0x4055C0</c>), which indexes the table
/// at <c>0x537B74</c> directly and unguarded, and
/// <c>Config_GraphicOptionsGetPerfLevelFromFlags</c> (<c>0x405530</c>), which scans it.
/// </para>
/// </remarks>
public static class GraphicsPerformance
{
    /// <summary>The flags each level stands for, highest quality first.</summary>
    /// <remarks>
    /// Read out of the binary at <c>0x537B74</c>, seven entries ending at <c>0x537B90</c>. The values
    /// are not monotonic as integers — they are bit patterns, and level 6 is everything off.
    /// </remarks>
    public static IReadOnlyList<int> LevelFlags { get; } = [414, 430, 13, 96, 77, 65, 0];

    /// <summary>Gets the number of levels the slider offers.</summary>
    /// <remarks><c>Config_GraphicOptionsNumPerfLevels</c>.</remarks>
    public static int LevelCount => LevelFlags.Count;

    /// <summary>Gets the flags a level stands for.</summary>
    /// <param name="level">The level.</param>
    /// <returns>The flags, or zero for a level outside the table.</returns>
    /// <remarks>
    /// The original indexes the table with no bounds check at all, so an out-of-range level reads
    /// whatever follows it in the data segment. Returning zero is the one place this deliberately
    /// diverges: reproducing an out-of-bounds read would mean reproducing the layout of the
    /// executable's data segment, and no shipped script can reach it — the slider is built with
    /// exactly <see cref="LevelCount"/> stops.
    /// </remarks>
    public static int FlagsForLevel(int level) =>
        level >= 0 && level < LevelFlags.Count ? LevelFlags[level] : 0;

    /// <summary>Gets the level whose flags are closest to the ones given.</summary>
    /// <param name="flags">The flags.</param>
    /// <returns>The level.</returns>
    /// <remarks>
    /// <b>Nearest by Hamming distance</b>, not by equality: the original XORs the argument with each
    /// entry, counts the set bits, and keeps the smallest. So flipping one switch away from a level
    /// still reports that level, and the slider does not jump. Ties go to the earliest entry, because
    /// the comparison is strictly less-than.
    /// </remarks>
    public static int LevelForFlags(int flags)
    {
        int best = 0;

        // The original seeds its best distance at 31, not 32. A candidate differing in all 32 bits
        // therefore never wins, and with a table that has no such entry the seed is unreachable —
        // but it is what decides the answer for an empty table, so it is kept.
        int bestDistance = 31;

        for (int level = 0; level < LevelFlags.Count; level++)
        {
            int distance = System.Numerics.BitOperations.PopCount((uint)(flags ^ LevelFlags[level]));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = level;
            }
        }

        return best;
    }
}
