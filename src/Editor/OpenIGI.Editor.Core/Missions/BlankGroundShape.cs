namespace OpenIGI.Editor.Core.Missions;

/// <summary>
/// The shape of a blank mission's ground: flat at the player start's height, or rolling ground from a seed, the same seed
/// always giving the same ground. Pure arithmetic on world positions, so any title's height grids can be filled from it.
/// </summary>
/// <remarks>
/// Seeded ground is fractal value noise: five octaves, the broadest about <see cref="WavelengthMetres"/> across, at most
/// <see cref="AmplitudeMetres"/> up or down. It settles to the start's height under the player and the exit, so neither
/// begins on a slope, and where a region ends (a play area on a larger level) it eases back into the ground that was there.
/// </remarks>
public static class BlankGroundShape
{
    /// <summary>The most seeded ground rises or falls from the start's height, in metres.</summary>
    public const double AmplitudeMetres = 25;

    /// <summary>The breadth of the broadest hills, in metres.</summary>
    public const double WavelengthMetres = 420;

    /// <summary>How far ground eases back into what was there at a region's edge, in metres.</summary>
    public const double EdgeMetres = 100;

    private const double PadMetres = 22;
    private const double PadEaseMetres = 60;
    private const int Octaves = 5;

    /// <summary>The ground's height at a point.</summary>
    /// <param name="ground">Flat, or seeded; <see cref="BlankGround.Keep"/> keeps what was there.</param>
    /// <param name="seed">The seed, for seeded ground.</param>
    /// <param name="x">The point's X, in metres.</param>
    /// <param name="y">The point's Y, in metres.</param>
    /// <param name="original">The ground there before, in metres.</param>
    /// <param name="startHeight">The ground at the player start, in metres: flat ground's height, seeded ground's middle.</param>
    /// <param name="pads">Where the ground is kept level: the player start and the exit, in metres.</param>
    /// <param name="edgeDistance">How far the point is inside the region being shaped, in metres; the whole map is unbounded.</param>
    /// <returns>The height, in metres.</returns>
    public static double Height(BlankGround ground, int seed, double x, double y, double original, double startHeight, IReadOnlyList<(double X, double Y)> pads, double edgeDistance = double.PositiveInfinity)
    {
        ArgumentNullException.ThrowIfNull(pads);
        if (ground == BlankGround.Keep)
        {
            return original;
        }

        double shaped = startHeight;
        if (ground == BlankGround.Seeded)
        {
            // Level ground under the pads, rolling between them.
            double level = 1;
            foreach ((double px, double py) in pads)
            {
                double distance = Math.Sqrt(((x - px) * (x - px)) + ((y - py) * (y - py)));
                level = Math.Min(level, SmoothStep(PadMetres, PadMetres + PadEaseMetres, distance));
            }

            shaped += Noise(seed, x, y) * AmplitudeMetres * level;
        }

        double keep = SmoothStep(0, EdgeMetres, edgeDistance);
        return original + ((shaped - original) * keep);
    }

    /// <summary>Fractal value noise at a point, in [-1, 1]: the same seed and point always give the same value.</summary>
    /// <param name="seed">The seed.</param>
    /// <param name="x">X, in metres.</param>
    /// <param name="y">Y, in metres.</param>
    /// <returns>The value.</returns>
    public static double Noise(int seed, double x, double y)
    {
        double sum = 0, weight = 1, total = 0, scale = 1 / WavelengthMetres;
        for (int octave = 0; octave < Octaves; octave++)
        {
            sum += weight * ValueNoise(seed + (octave * 1013), x * scale, y * scale);
            total += weight;
            weight *= 0.5;
            scale *= 2;
        }

        return sum / total;
    }

    // Smoothly interpolated noise on an integer lattice, in [-1, 1].
    private static double ValueNoise(int seed, double x, double y)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        double fx = x - ix, fy = y - iy;
        double sx = fx * fx * (3 - (2 * fx)), sy = fy * fy * (3 - (2 * fy));
        double top = double.Lerp(Lattice(seed, ix, iy), Lattice(seed, ix + 1, iy), sx);
        double bottom = double.Lerp(Lattice(seed, ix, iy + 1), Lattice(seed, ix + 1, iy + 1), sx);
        return double.Lerp(top, bottom, sy);
    }

    // A lattice point's value from a hash of it and the seed, in [-1, 1].
    private static double Lattice(int seed, int x, int y)
    {
        unchecked
        {
            uint h = (uint)seed * 0x9E3779B1u;
            h ^= (uint)x * 0x85EBCA77u;
            h = (h << 13) | (h >> 19);
            h ^= (uint)y * 0xC2B2AE3Du;
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            h *= 0x846CA68Bu;
            h ^= h >> 16;
            return (h / (double)uint.MaxValue * 2) - 1;
        }
    }

    private static double SmoothStep(double from, double to, double value)
    {
        double t = Math.Clamp((value - from) / (to - from), 0, 1);
        return t * t * (3 - (2 * t));
    }
}
