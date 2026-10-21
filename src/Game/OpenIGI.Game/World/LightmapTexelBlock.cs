namespace OpenIGI.Game.World;

/// <summary>A run of one page's texels: the piece of work a phase is divided into.</summary>
/// <param name="Target">Which page.</param>
/// <param name="From">Its first texel.</param>
/// <param name="To">One past its last.</param>
internal readonly record struct LightmapTexelBlock(int Target, int From, int To)
{
    /// <summary>How many texels a block holds: small enough for a slice of a frame to trace.</summary>
    private const int Texels = 64;

    /// <summary>Every page's texels, in blocks, in page order.</summary>
    public static LightmapTexelBlock[] Of(IReadOnlyList<LightmapTarget> targets)
    {
        List<LightmapTexelBlock> blocks = [];

        for (int t = 0; t < targets.Count; t++)
        {
            int count = targets[t].Texels.Length;

            for (int from = 0; from < count; from += Texels)
            {
                blocks.Add(new LightmapTexelBlock(t, from, Math.Min(count, from + Texels)));
            }
        }

        return [.. blocks];
    }
}
