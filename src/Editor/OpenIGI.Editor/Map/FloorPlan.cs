using OpenIGI.Host.Editor;

namespace OpenIGI.Editor.Map;

/// <summary>A storey the map can be cut at: its name, and how high over the ground under each face the cut is.</summary>
/// <param name="Name">What the map's bar calls it.</param>
/// <param name="Cut">The cut, in metres over the ground; infinite for the roofs (no cut).</param>
internal sealed record Floor(string Name, double Cut)
{
    /// <summary>Gets whether this floor is the level whole, roofs and all.</summary>
    public bool IsRoofs => double.IsPositiveInfinity(Cut);
}

/// <summary>What one of the level's collision triangles is, seen from above.</summary>
internal enum PlanKind : byte
{
    /// <summary>Facing up: something to stand on.</summary>
    Floor,

    /// <summary>Steep but walkable-looking: a ramp, a stair's run, a roof's slope.</summary>
    Slope,

    /// <summary>Upright: a wall, a railing, a fence.</summary>
    Wall,
}

/// <summary>
/// The level's storeys from its collision: each triangle a floor, slope or wall by how much it faces up, with its height
/// over the ground under it, and the floors by the 4 m cell they lie over, for the floor under a point at any cut.
/// </summary>
/// <remarks>The multiplayer layout page's plan, the same storeys and the same rules: a level's floors are a storey (3.2 m) apart.</remarks>
internal sealed class FloorPlan
{
    /// <summary>The storeys, the whole level first.</summary>
    public static readonly IReadOnlyList<Floor> Floors =
    [
        new("Roofs", double.PositiveInfinity),
        new("Ground", 1.6),
        new("Floor 1", 4.8),
        new("Floor 2", 8.0),
        new("Floor 3", 11.2),
        new("Floor 4", 14.4),
    ];

    private const double Cell = 4.0;

    // How far over the cut a floor still counts as under it: a step or a kerb.
    private const double FloorSlack = 0.3;

    private readonly OverheadScene _scene;
    private readonly Dictionary<long, List<int>> _cells = [];

    /// <summary>Initializes a new instance of the <see cref="FloorPlan"/> class.</summary>
    /// <param name="scene">The level from above.</param>
    public FloorPlan(OverheadScene scene)
    {
        _scene = scene;
        float[] v = scene.Collision;
        int count = v.Length / OverheadScene.CollisionStride;
        Kinds = new PlanKind[count];
        Low = new float[count];
        High = new float[count];
        for (int t = 0; t < count; t++)
        {
            int o = t * OverheadScene.CollisionStride;
            double ux = v[o + 3] - v[o], uy = v[o + 4] - v[o + 1], uz = v[o + 5] - v[o + 2];
            double wx = v[o + 6] - v[o], wy = v[o + 7] - v[o + 1], wz = v[o + 8] - v[o + 2];
            double nx = (uy * wz) - (uz * wy), ny = (uz * wx) - (ux * wz), nz = (ux * wy) - (uy * wx);
            double length = Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
            double up = length > 0 ? Math.Abs(nz) / length : 0;
            Kinds[t] = up > 0.7 ? PlanKind.Floor : up > 0.3 ? PlanKind.Slope : PlanKind.Wall;
            float under = v[o + 9];
            Low[t] = MathF.Min(v[o + 2], MathF.Min(v[o + 5], v[o + 8])) - under;
            High[t] = MathF.Max(v[o + 2], MathF.Max(v[o + 5], v[o + 8])) - under;

            double minX = Math.Min(v[o], Math.Min(v[o + 3], v[o + 6])), maxX = Math.Max(v[o], Math.Max(v[o + 3], v[o + 6]));
            double minY = Math.Min(v[o + 1], Math.Min(v[o + 4], v[o + 7])), maxY = Math.Max(v[o + 1], Math.Max(v[o + 4], v[o + 7]));

            // The level's great ground-covering faces are the ground's own business; only what is built is indexed.
            if (Kinds[t] == PlanKind.Wall || maxX - minX > 400 || maxY - minY > 400)
            {
                continue;
            }

            for (long cx = (long)Math.Floor(minX / Cell); cx <= (long)Math.Floor(maxX / Cell); cx++)
            {
                for (long cy = (long)Math.Floor(minY / Cell); cy <= (long)Math.Floor(maxY / Cell); cy++)
                {
                    long key = Key(cx, cy);
                    if (!_cells.TryGetValue(key, out List<int>? list))
                    {
                        _cells[key] = list = [];
                    }

                    list.Add(t);
                }
            }
        }
    }

    /// <summary>Gets each collision triangle's kind.</summary>
    public PlanKind[] Kinds { get; }

    /// <summary>Gets each collision triangle's lowest corner over the ground under it, in metres.</summary>
    public float[] Low { get; }

    /// <summary>Gets each collision triangle's highest corner over the ground under it, in metres.</summary>
    public float[] High { get; }

    /// <summary>Gets the level from above the plan was read from.</summary>
    public OverheadScene Scene => _scene;

    /// <summary>The bare ground's height under a point, in metres; NaN off the level.</summary>
    /// <param name="x">The world X, in metres.</param>
    /// <param name="y">The world Y.</param>
    /// <returns>The height.</returns>
    public double Ground(double x, double y) => _scene.GroundAt(x, y);

    /// <summary>The highest floor under a point that a cut keeps, or the bare ground where none does.</summary>
    /// <param name="x">The world X, in metres.</param>
    /// <param name="y">The world Y.</param>
    /// <param name="cut">The cut, in metres over the ground; infinite for the roofs.</param>
    /// <returns>The height in metres; NaN off the level.</returns>
    public double Surface(double x, double y, double cut)
    {
        double best = Ground(x, y);
        double ex = x - _scene.WestMetres, ny = y - _scene.SouthMetres;
        if (!_cells.TryGetValue(Key((long)Math.Floor(ex / Cell), (long)Math.Floor(ny / Cell)), out List<int>? cell))
        {
            return best;
        }

        float[] v = _scene.Collision;
        foreach (int t in cell)
        {
            int o = t * OverheadScene.CollisionStride;
            if (OverheadScene.Barycentric(v[o], v[o + 1], v[o + 3], v[o + 4], v[o + 6], v[o + 7], ex, ny) is not { } l)
            {
                continue;
            }

            double z = (l.A * v[o + 2]) + (l.B * v[o + 5]) + (l.C * v[o + 8]);
            if (z - v[o + 9] <= cut + FloorSlack && (double.IsNaN(best) || z > best))
            {
                best = z;
            }
        }

        return best;
    }

    /// <summary>The floor something stands on: the highest under it no higher than its own feet.</summary>
    /// <param name="x">The world X, in metres.</param>
    /// <param name="y">The world Y.</param>
    /// <param name="z">Its height.</param>
    /// <returns>The floor's height in metres; NaN off the level.</returns>
    public double FloorUnder(double x, double y, double z) =>
        Ground(x, y) is var ground && double.IsNaN(ground) ? Surface(x, y, double.PositiveInfinity) : Surface(x, y, z - ground);

    /// <summary>Whether something at a height stands on the storey a floor shows: under its cut and over the one below.</summary>
    /// <param name="floor">The storey shown.</param>
    /// <param name="overGround">How high it stands over the ground under it, in metres.</param>
    /// <returns>Whether it does; anything does on the roofs.</returns>
    public static bool IsOn(Floor floor, double overGround) =>
        floor.IsRoofs || double.IsNaN(overGround) || (overGround <= floor.Cut + 0.5 && overGround >= floor.Cut - 3.7);

    private static long Key(long cx, long cy) => (cx * 1_000_003) + cy;
}
