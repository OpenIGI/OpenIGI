namespace OpenIGI.Core.Numerics;

/// <summary>
/// Whether <see cref="SimMath"/> gives the same bits here as on the machine the boards' checker runs on: a hash of a hundred
/// thousand of its results, pinned. Where it differs, a run played here would part from the checker's playing of it, so the
/// runs recorded here are kept off the boards rather than refused there; a test holds every platform CI builds to it.
/// </summary>
public static class SimMathFingerprint
{
    /// <summary>The fingerprint, as first computed on win-x64 (2026-11-24) and every platform since.</summary>
    public const ulong Expected = 10322098589236398023UL;

    // The scales are literals, not powers of ten taken here: the platform's own pow must not choose the inputs.
    private static readonly double[] Scales = [1e-4, 1e-3, 1e-2, 1e-1, 1, 1e1, 1e2, 1e3, 1e4];

    private static readonly Lazy<bool> Matching = new(static () => Compute() == Expected);

    /// <summary>Gets whether the fingerprint here is the expected one; worked out once, the first time it is asked.</summary>
    public static bool MatchesHere => Matching.Value;

    /// <summary>Works the fingerprint out: every function over the same ten thousand pairs of inputs, their bits folded.</summary>
    /// <returns>The fingerprint.</returns>
    public static ulong Compute()
    {
        ulong hash = 14695981039346656037UL;
#pragma warning disable CA5394 // Not for security: a fixed seed, whose sequence .NET keeps the same everywhere, chooses the inputs.
        Random random = new(7);
        for (int i = 0; i < 10000; i++)
        {
            double x = (random.NextDouble() - 0.5) * Scales[random.Next(Scales.Length)];
            double y = (random.NextDouble() - 0.5) * Scales[random.Next(Scales.Length)];
#pragma warning restore CA5394
            Fold(ref hash, SimMath.Sin(x));
            Fold(ref hash, SimMath.Cos(x));
            Fold(ref hash, SimMath.Tan(x));
            Fold(ref hash, SimMath.Atan(x));
            Fold(ref hash, SimMath.Atan2(y, x));
            Fold(ref hash, SimMath.Asin(Math.Clamp(x, -1, 1)));
            Fold(ref hash, SimMath.Acos(Math.Clamp(y, -1, 1)));
            Fold(ref hash, SimMath.Exp(Math.Clamp(x, -700, 700)));
            Fold(ref hash, SimMath.Log(Math.Abs(x)));
            Fold(ref hash, SimMath.Pow(Math.Abs(x), Math.Clamp(y, -30, 30)));
            Fold(ref hash, SimMath.Tanh(x));
        }

        return hash;
    }

    private static void Fold(ref ulong hash, double value)
    {
        hash ^= (ulong)BitConverter.DoubleToInt64Bits(value);
        hash *= 1099511628211UL;
    }
}
