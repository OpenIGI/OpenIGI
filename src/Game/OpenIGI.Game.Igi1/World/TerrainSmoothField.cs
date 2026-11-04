using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// The smooth ground the finest terrain is refined onto: a bicubic surface through the retail ground,
/// sampled on the four-metre lattice the finest level of detail is laid out on.
/// </summary>
/// <remarks>
/// <para>
/// The finest chunks are eight metres across and carry about eight vertices apiece, so almost every
/// vertex sits on a chunk's border. Smoothing a chunk from its own triangles therefore changes nearly
/// nothing, and whatever it did change would open a crack against the neighbour, which smooths from
/// its own. This surface is a function of the world position alone. Two chunks that share an edge
/// evaluate it at the same points and get the same answer, so the refined ground stays watertight
/// without either chunk knowing about the other.
/// </para>
/// <para>
/// The lattice heights come from the same ground query the player stands on
/// (<see cref="TerrainHeightQuery.TryDrop"/>), and a monotone bicubic (see <c>Monotone</c>) passes
/// through every one of them without overshooting any. Wherever a retail vertex sits on a lattice point, which on the finest level is nearly
/// all of them, the refined ground keeps it exactly. <see cref="Relax"/> blends towards a uniform
/// B-spline, which no longer passes through the samples and so also rounds off a lone spike.
/// </para>
/// <para>
/// Visual only. Gameplay still stands on the retail triangles, and
/// <see cref="Displace"/> never moves the drawn ground further than <see cref="LimitUnits"/> from
/// them, so a foot is never visibly sunk or floating.
/// </para>
/// </remarks>
public sealed class TerrainSmoothField
{
    /// <summary>The finest level of detail a field can refine: the finest the descent resolves.</summary>
    public const int FinestExponent = TerrainTraversal.MaximumExponent;

    /// <summary>The most refinement steps along one triangle edge.</summary>
    public const int MaximumSubdivisions = 8;

    /// <summary>How far above and below the asking chunk's centre a lattice sample looks for ground.</summary>
    /// <remarks>
    /// Tall enough for the top of a cliff beside the chunk: sixteen metres missed them on level 7, and
    /// every sample missed was a hole in the surface (see <see cref="Sample"/>).
    /// </remarks>
    private const double SearchUnits = 48.0 * 4096.0;

    /// <summary>How many lattice samples are remembered before the memory starts over.</summary>
    /// <remarks>
    /// A quarter of a million samples is a square of ground two kilometres on a side, far more than the
    /// finest level ever reaches around the camera.
    /// </remarks>
    private const int SampleCapacity = 1 << 18;

    /// <summary>The side of the window <see cref="Focus"/> fetches: every sample a finest chunk's points can need.</summary>
    /// <remarks>
    /// A chunk spans the two cells either side of its centre's lattice point, and a point on its far
    /// border floors into the cell beyond, so its points need lattice indices from two below the centre
    /// to three above.
    /// </remarks>
    private const int WindowSide = 6;

    private readonly TerrainData _terrain;
    private readonly Dictionary<(long, long), float> _samples = [];
    private readonly Dictionary<(long, long), float> _filled = [];

    // The lattice around the chunk being refined, so its points read a flat array rather than looking
    // each of their sixteen samples up. See Focus.
    private readonly double[] _window = new double[WindowSide * WindowSide];
    private long _windowX = long.MinValue;
    private long _windowY = long.MinValue;

    /// <summary>Initialises a new instance of the <see cref="TerrainSmoothField"/> class.</summary>
    /// <param name="terrain">The level's terrain, whose ground query supplies the lattice.</param>
    /// <param name="subdivisions">How many pieces each edge of a triangle is cut into; 2 to <see cref="MaximumSubdivisions"/>.</param>
    /// <param name="relax">0 interpolates the lattice exactly; 1 is the smoother B-spline that rounds spikes off.</param>
    /// <param name="limitUnits">The furthest the refined ground may stand from the retail ground, in engine units.</param>
    /// <param name="exponent">
    /// The level of detail the field refines. Its lattice is that level's chunk half-width, so the
    /// finest level's is four metres and each coarser one doubles it.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="terrain"/> is <see langword="null"/>.</exception>
    public TerrainSmoothField(TerrainData terrain, int subdivisions, double relax, double limitUnits, int exponent = FinestExponent)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        _terrain = terrain;
        Exponent = Math.Clamp(exponent, TerrainTraversal.MinimumExponent, FinestExponent);
        LatticeShift = 30 - Exponent;
        Subdivisions = Math.Clamp(subdivisions, 2, MaximumSubdivisions);
        Relax = Math.Clamp(relax, 0.0, 1.0);
        LimitUnits = Math.Max(0.0, limitUnits);
    }

    /// <summary>Gets the level of detail the field refines.</summary>
    public int Exponent { get; }

    /// <summary>Gets the lattice's spacing as a power of two of engine units: the half-width of a chunk at <see cref="Exponent"/>.</summary>
    /// <remarks>Fourteen, four metres, at the finest level; so a chunk's centre and corners all lie on the lattice.</remarks>
    public int LatticeShift { get; }

    /// <summary>Gets how many pieces each edge of a triangle is cut into.</summary>
    public int Subdivisions { get; }

    /// <summary>Gets how far the surface leans from interpolating the lattice towards the B-spline.</summary>
    public double Relax { get; }

    /// <summary>Gets the furthest the refined ground may stand from the retail ground, in engine units.</summary>
    public double LimitUnits { get; }

    /// <summary>Gets how many lattice samples the field currently remembers.</summary>
    public int SampleCount => _samples.Count;

    /// <summary>Forgets the ground sampled over a rectangle, after the ground there changed shape.</summary>
    /// <param name="minX">The rectangle's least X, in engine units.</param>
    /// <param name="minY">The rectangle's least Y.</param>
    /// <param name="maxX">The rectangle's greatest X.</param>
    /// <param name="maxY">The rectangle's greatest Y.</param>
    /// <remarks>
    /// Reaches past the rectangle by the window the surface is fitted over and by the search that fills missing samples,
    /// since both read the samples there.
    /// </remarks>
    public void Forget(double minX, double minY, double maxX, double maxY)
    {
        double spacing = 1L << LatticeShift;
        long margin = WindowSide + (long)Math.Ceiling(SearchUnits / spacing);
        long i0 = (long)Math.Floor(minX / spacing) - margin;
        long i1 = (long)Math.Ceiling(maxX / spacing) + margin;
        long j0 = (long)Math.Floor(minY / spacing) - margin;
        long j1 = (long)Math.Ceiling(maxY / spacing) + margin;
        if ((i1 - i0 + 1) * (j1 - j0 + 1) > _samples.Count + _filled.Count)
        {
            _samples.Clear();
            _filled.Clear();
        }
        else
        {
            for (long j = j0; j <= j1; j++)
            {
                for (long i = i0; i <= i1; i++)
                {
                    _samples.Remove((i, j));
                    _filled.Remove((i, j));
                }
            }
        }

        _windowX = long.MinValue;
        _windowY = long.MinValue;
    }

    /// <summary>Fetches the lattice around a finest chunk, so that the chunk's points evaluate from it directly.</summary>
    /// <param name="centreX">The chunk's centre X, in engine units.</param>
    /// <param name="centreY">The chunk's centre Y.</param>
    /// <param name="referenceZ">Where to look for the lattice's ground: the chunk's centre Z.</param>
    /// <remarks>
    /// Only a shortcut. The window holds exactly the values the shared memory holds, so a point
    /// evaluates the same with or without it, and one outside it falls back to the memory.
    /// </remarks>
    public void Focus(double centreX, double centreY, double referenceZ)
    {
        double spacing = 1L << LatticeShift;
        long x0 = (long)Math.Floor(centreX / spacing) - 2;
        long y0 = (long)Math.Floor(centreY / spacing) - 2;
        for (int j = 0; j < WindowSide; j++)
        {
            for (int i = 0; i < WindowSide; i++)
            {
                _window[(j * WindowSide) + i] = Sample(x0 + i, y0 + j, referenceZ);
            }
        }

        _windowX = x0;
        _windowY = y0;
    }

    /// <summary>Gets the smooth ground's offset from a point on the retail mesh, and the weight to shade it with.</summary>
    /// <param name="x">The point's world X, in engine units.</param>
    /// <param name="y">The point's world Y.</param>
    /// <param name="meshZ">The retail mesh's height at the point, the triangle interpolated.</param>
    /// <param name="referenceZ">Where to look for the lattice's ground: the asking chunk's centre.</param>
    /// <param name="normal">Receives the smooth surface's unit normal, or up when there is none.</param>
    /// <returns>
    /// The vertical offset to add to <paramref name="meshZ"/>, never more than the limit either way, and
    /// how far to trust the smooth normal, from 0 to 1. The normal is trusted fully while the smooth
    /// surface is within the limit of the mesh and not at all past three times it, which is a cliff, an
    /// overhang or a hole rather than a bump: there the offset is pinned at the limit and the facets keep
    /// their own shading.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The offset saturates rather than clamps: <c>limit · tanh(Δ / limit)</c> follows small
    /// differences one for one and eases into the limit as they grow. It is continuous, which keeps a
    /// shared edge closed, and it is monotone in the difference, which matters as much. The first
    /// version faded the offset back to nothing past the limit, so across the foot of every cliff the
    /// ground rose by the limit and fell back again, a ridge neither surface has, and under a low sun
    /// each ridge threw a shadow.
    /// </para>
    /// </remarks>
    public (double Offset, double Weight) Displace(double x, double y, double meshZ, double referenceZ, out (double X, double Y, double Z) normal)
    {
        normal = (0.0, 0.0, 1.0);
        double limit = LimitUnits;
        if (limit <= 0.0 || !TryEvaluate(x, y, referenceZ, out double height, out double slopeX, out double slopeY))
        {
            return (0.0, 0.0);
        }

        double delta = height - meshZ;
        double distance = Math.Abs(delta);
        double weight;
        if (distance <= limit)
        {
            weight = 1.0;
        }
        else if (distance >= limit * 3.0)
        {
            weight = 0.0;
        }
        else
        {
            double t = (distance - limit) / (limit * 2.0);
            weight = 1.0 - (t * t * (3.0 - (2.0 * t)));
        }

        double length = Math.Sqrt((slopeX * slopeX) + (slopeY * slopeY) + 1.0);
        normal = (-slopeX / length, -slopeY / length, 1.0 / length);
        return (limit * Math.Tanh(delta / limit), weight);
    }

    /// <summary>Evaluates the smooth surface's height and slope at a point.</summary>
    /// <param name="x">The point's world X, in engine units.</param>
    /// <param name="y">The point's world Y.</param>
    /// <param name="referenceZ">Where to look for the lattice's ground.</param>
    /// <param name="height">Receives the height, in engine units.</param>
    /// <param name="slopeX">Receives the height's rate of change along X.</param>
    /// <param name="slopeY">Receives the same along Y.</param>
    /// <returns><see langword="false"/> when any of the sixteen samples around the point has no ground.</returns>
    public bool TryEvaluate(double x, double y, double referenceZ, out double height, out double slopeX, out double slopeY)
    {
        height = slopeX = slopeY = 0.0;

        double spacing = 1L << LatticeShift;
        double gx = x / spacing;
        double gy = y / spacing;
        long ix = (long)Math.Floor(gx);
        long iy = (long)Math.Floor(gy);

        long wx0 = ix - 1 - _windowX;
        long wy0 = iy - 1 - _windowY;
        bool windowed = wx0 >= 0 && wy0 >= 0 && wx0 + 4 <= WindowSide && wy0 + 4 <= WindowSide;

        Span<double> samples = stackalloc double[16];
        for (int j = 0; j < 4; j++)
        {
            for (int i = 0; i < 4; i++)
            {
                double sample = windowed
                    ? _window[((wy0 + j) * WindowSide) + wx0 + i]
                    : Sample(ix + i - 1, iy + j - 1, referenceZ);
                if (double.IsNaN(sample))
                {
                    return false;
                }

                samples[(j * 4) + i] = sample;
            }
        }

        double tx = gx - ix;
        double ty = gy - iy;

        // Monotone: along X through each of the four rows, then along Y through the four results.
        Span<double> rows = stackalloc double[4];
        Span<double> rowSlopes = stackalloc double[4];
        for (int j = 0; j < 4; j++)
        {
            (rows[j], rowSlopes[j]) = Monotone(samples[j * 4], samples[(j * 4) + 1], samples[(j * 4) + 2], samples[(j * 4) + 3], tx);
        }

        (height, slopeY) = Monotone(rows[0], rows[1], rows[2], rows[3], ty);
        (slopeX, _) = Monotone(rowSlopes[0], rowSlopes[1], rowSlopes[2], rowSlopes[3], ty, limited: false);

        if (Relax > 0.0)
        {
            Span<double> wx = stackalloc double[4];
            Span<double> dx = stackalloc double[4];
            Span<double> wy = stackalloc double[4];
            Span<double> dy = stackalloc double[4];
            BSplineWeights(tx, wx, dx);
            BSplineWeights(ty, wy, dy);
            double bHeight = 0.0, bSlopeX = 0.0, bSlopeY = 0.0;
            for (int j = 0; j < 4; j++)
            {
                for (int i = 0; i < 4; i++)
                {
                    double sample = samples[(j * 4) + i];
                    bHeight += wx[i] * wy[j] * sample;
                    bSlopeX += dx[i] * wy[j] * sample;
                    bSlopeY += wx[i] * dy[j] * sample;
                }
            }

            height += (bHeight - height) * Relax;
            slopeX += (bSlopeX - slopeX) * Relax;
            slopeY += (bSlopeY - slopeY) * Relax;
        }

        // The derivative weights are per lattice step; the slope is per engine unit.
        slopeX /= spacing;
        slopeY /= spacing;
        return true;
    }

    /// <summary>The height the surface uses at one lattice point: the ground, or where there is none, its neighbours'.</summary>
    /// <remarks>
    /// <para>
    /// A lattice point can find no ground: a hole the level cuts in the terrain, or a cliff too tall for
    /// the search. It used to leave every point whose sixteen samples included it unsmoothed, while the
    /// point beside it, whose samples did not, moved by up to the limit: a little step in the ground
    /// along the edge of every such patch, and at a low sun every step threw a shadow. Four in a hundred
    /// points on a level 7 snowfield.
    /// </para>
    /// <para>
    /// So a missing point takes the mean of its eight neighbours that have ground. That depends on the
    /// lattice point alone and not on who asks, so every chunk gets the same answer and the surface stays
    /// closed. Only a point none of whose neighbours has ground is still missing.
    /// </para>
    /// </remarks>
    private double Sample(long i, long j, double referenceZ)
    {
        double own = Ground(i, j, referenceZ);
        if (!double.IsNaN(own))
        {
            return own;
        }

        if (_filled.TryGetValue((i, j), out float filled))
        {
            return filled;
        }

        if (_filled.Count >= SampleCapacity)
        {
            _filled.Clear();
        }

        double sum = 0.0;
        int count = 0;
        for (long dj = -1; dj <= 1; dj++)
        {
            for (long di = -1; di <= 1; di++)
            {
                if (di == 0 && dj == 0)
                {
                    continue;
                }

                double neighbour = Ground(i + di, j + dj, referenceZ);
                if (!double.IsNaN(neighbour))
                {
                    sum += neighbour;
                    count++;
                }
            }
        }

        float value = count > 0 ? (float)(sum / count) : float.NaN;
        _filled[(i, j)] = value;
        return value;
    }

    /// <summary>The ground height at one lattice point, or NaN where there is none near the reference.</summary>
    /// <remarks>
    /// The first chunk to ask decides the answer, and every later one reads the same value back. That
    /// is what keeps two chunks agreeing even where their reference heights would find different
    /// surfaces, as under an overhang.
    /// </remarks>
    private double Ground(long i, long j, double referenceZ)
    {
        if (_samples.TryGetValue((i, j), out float cached))
        {
            return cached;
        }

        if (_samples.Count >= SampleCapacity)
        {
            _samples.Clear();
            _filled.Clear();
        }

        double spacing = 1L << LatticeShift;

        // From above the reference, downwards, so the topmost ground in the window is the one taken.
        float value = _terrain.TryDrop(i * spacing, j * spacing, referenceZ + SearchUnits - TerrainHeightQuery.DropLeadUnits, SearchUnits * 2.0, upward: false, out TerrainGround ground)
            ? (float)ground.Height
            : float.NaN;
        _samples[(i, j)] = value;
        return value;
    }

    /// <summary>
    /// A cubic Hermite curve through <paramref name="p1"/> and <paramref name="p2"/>, a fraction
    /// <paramref name="t"/> of the way between them, and its rate of change per lattice step.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tangent at each sample is the harmonic mean of the steps either side of it, and zero where
    /// those steps disagree in sign (Fritsch and Butland). The curve then never rises above or dips
    /// below the samples it runs between. Catmull-Rom, which takes the plain average, overshoots beside
    /// every sharp change of slope. On ground that means a new little ridge or pit next to each
    /// break, and under a low sun every one of those throws a shadow the stored ground never had.
    /// </para>
    /// <para>
    /// Each tangent depends only on its own sample's neighbours, so two cells sharing an edge build the
    /// same curve along it, and the surface stays closed. <paramref name="limited"/> is off for
    /// interpolating slopes rather than heights, where an extremum is not a defect.
    /// </para>
    /// </remarks>
    private static (double Value, double Derivative) Monotone(double p0, double p1, double p2, double p3, double t, bool limited = true)
    {
        double d0 = p1 - p0;
        double d1 = p2 - p1;
        double d2 = p3 - p2;
        double m1 = limited ? Tangent(d0, d1) : 0.5 * (d0 + d1);
        double m2 = limited ? Tangent(d1, d2) : 0.5 * (d1 + d2);

        double t2 = t * t;
        double t3 = t2 * t;
        double value = (((2.0 * t3) - (3.0 * t2) + 1.0) * p1)
            + ((t3 - (2.0 * t2) + t) * m1)
            + (((-2.0 * t3) + (3.0 * t2)) * p2)
            + ((t3 - t2) * m2);
        double derivative = (((6.0 * t2) - (6.0 * t)) * p1)
            + (((3.0 * t2) - (4.0 * t) + 1.0) * m1)
            + (((-6.0 * t2) + (6.0 * t)) * p2)
            + (((3.0 * t2) - (2.0 * t)) * m2);
        return (value, derivative);

        static double Tangent(double before, double after) =>
            before * after <= 0.0 ? 0.0 : 2.0 * before * after / (before + after);
    }

    /// <summary>The four uniform cubic B-spline weights for a point a fraction <paramref name="t"/> into its cell, and their derivatives.</summary>
    /// <remarks>Near the samples, never through them, so it rounds a lone spike off as well.</remarks>
    private static void BSplineWeights(double t, Span<double> weights, Span<double> derivatives)
    {
        double t2 = t * t;
        double t3 = t2 * t;
        double u = 1.0 - t;
        weights[0] = u * u * u / 6.0;
        weights[1] = ((3.0 * t3) - (6.0 * t2) + 4.0) / 6.0;
        weights[2] = ((-3.0 * t3) + (3.0 * t2) + (3.0 * t) + 1.0) / 6.0;
        weights[3] = t3 / 6.0;
        derivatives[0] = -0.5 * u * u;
        derivatives[1] = ((9.0 * t2) - (12.0 * t)) / 6.0;
        derivatives[2] = ((-9.0 * t2) + (6.0 * t) + 3.0) / 6.0;
        derivatives[3] = 0.5 * t2;
    }
}
