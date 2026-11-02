using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Game.World;

namespace OpenIGI.Editor.Terrain;

/// <summary>What a terrain brush does to the ground under it.</summary>
internal enum TerrainBrushMode
{
    /// <summary>Lifts the ground.</summary>
    Raise,

    /// <summary>Sinks the ground.</summary>
    Lower,

    /// <summary>Pulls each sample toward the average of its neighbours.</summary>
    Smooth,

    /// <summary>Pulls the ground toward the height where the stroke began.</summary>
    Flatten,
}

/// <summary>
/// A round brush that shapes any <see cref="IEditableTerrain"/>: strongest in the middle and fading smoothly to nothing at
/// its edge, applied over time while the button is held, so holding longer shapes more.
/// </summary>
internal sealed class TerrainBrush
{
    /// <summary>Gets or sets what the brush does.</summary>
    public TerrainBrushMode Mode { get; set; } = TerrainBrushMode.Raise;

    /// <summary>Gets or sets the brush's radius, in metres.</summary>
    public double RadiusMetres { get; set; } = 10.0;

    /// <summary>Gets or sets how fast it works: metres a second at its middle when raising or lowering.</summary>
    public double Strength { get; set; } = 4.0;

    /// <summary>Gets how much of the brush's effect a point gets, by its distance from the middle.</summary>
    /// <param name="distance">The distance, in metres.</param>
    /// <returns>One at the middle, falling smoothly to nought at the radius.</returns>
    public double Weight(double distance)
    {
        double t = distance / Math.Max(1e-6, RadiusMetres);
        if (t >= 1.0)
        {
            return 0.0;
        }

        double u = 1.0 - (t * t);
        return u * u;
    }

    /// <summary>Applies the brush for a moment.</summary>
    /// <param name="terrain">The ground.</param>
    /// <param name="centre">Where the brush is, in world metres; only X and Y matter.</param>
    /// <param name="seconds">How long it was held there.</param>
    /// <param name="flattenTo">The height <see cref="TerrainBrushMode.Flatten"/> pulls toward, in metres.</param>
    /// <param name="stroke">Records each sample's height before the stroke first touched it.</param>
    /// <param name="mode">What to do this time, when not <see cref="Mode"/>: Shift paints the opposite of raise or lower.</param>
    public void Apply(IEditableTerrain terrain, Vector3d centre, double seconds, double flattenTo, TerrainStroke stroke, TerrainBrushMode? mode = null)
    {
        TerrainBrushMode doing = mode ?? Mode;
        foreach (ITerrainHeightGrid grid in terrain.Grids)
        {
            if (!Bounds(grid, centre, out int left, out int top, out int right, out int bottom))
            {
                continue;
            }

            // Work out every new height from the old ones before writing any, so smoothing reads an unchanged
            // neighbourhood.
            List<(int X, int Y, double Height)> changes = [];
            for (int y = top; y <= bottom; y++)
            {
                for (int x = left; x <= right; x++)
                {
                    (double sx, double sy) = grid.SampleMetres(x, y);
                    double weight = Weight(Math.Sqrt(((sx - centre.X) * (sx - centre.X)) + ((sy - centre.Y) * (sy - centre.Y))));
                    if (weight > 0.0)
                    {
                        double now = grid.GetHeight(x, y);
                        double next = Shaped(doing, grid, x, y, now, weight * seconds, flattenTo);
                        if (next != now)
                        {
                            changes.Add((x, y, next));
                        }
                    }
                }
            }

            foreach ((int x, int y, double height) in changes)
            {
                stroke.Remember(grid, x, y);
                grid.SetHeight(x, y, height);
            }

            if (changes.Count > 0)
            {
                terrain.Refresh(grid, left, top, right, bottom);
            }
        }
    }

    // The samples the brush's square covers, clamped to the grid; false when it misses the grid.
    private bool Bounds(ITerrainHeightGrid grid, Vector3d centre, out int left, out int top, out int right, out int bottom)
    {
        (double c0, double r0) = grid.ToGrid(centre.X - RadiusMetres, centre.Y - RadiusMetres);
        (double c1, double r1) = grid.ToGrid(centre.X + RadiusMetres, centre.Y + RadiusMetres);
        left = Math.Max(0, (int)Math.Floor(Math.Min(c0, c1)));
        right = Math.Min(grid.Width - 1, (int)Math.Ceiling(Math.Max(c0, c1)));
        top = Math.Max(0, (int)Math.Floor(Math.Min(r0, r1)));
        bottom = Math.Min(grid.Height - 1, (int)Math.Ceiling(Math.Max(r0, r1)));
        return left <= right && top <= bottom;
    }

    /// <summary>Gets the mode that undoes a mode's work: raise for lower and lower for raise; the others are their own.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>Its opposite.</returns>
    public static TerrainBrushMode Opposite(TerrainBrushMode mode) => mode switch
    {
        TerrainBrushMode.Raise => TerrainBrushMode.Lower,
        TerrainBrushMode.Lower => TerrainBrushMode.Raise,
        _ => mode,
    };

    private double Shaped(TerrainBrushMode mode, ITerrainHeightGrid grid, int x, int y, double now, double amount, double flattenTo)
    {
        double pull = Math.Min(1.0, amount * Strength * 0.5);
        return mode switch
        {
            TerrainBrushMode.Raise => now + (amount * Strength),
            TerrainBrushMode.Lower => now - (amount * Strength),
            TerrainBrushMode.Flatten => now + ((flattenTo - now) * pull),
            _ => now + ((Neighbourhood(grid, x, y) - now) * pull),
        };
    }

    private static double Neighbourhood(ITerrainHeightGrid grid, int x, int y)
    {
        double sum = 0.0;
        int count = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx;
                int ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < grid.Width && ny < grid.Height)
                {
                    sum += grid.GetHeight(nx, ny);
                    count++;
                }
            }
        }

        return sum / count;
    }
}

/// <summary>
/// One stroke of a terrain brush as an undoable step: every sample it touched, with its height before the stroke and after.
/// </summary>
/// <remarks>
/// The stroke is applied as it is painted; it is recorded once the button comes up (<see cref="Finish"/>), and undoing and
/// redoing it puts the heights it remembers back.
/// </remarks>
internal sealed class TerrainStroke(IEditableTerrain terrain) : IEditorCommand
{
    private readonly Dictionary<(ITerrainHeightGrid Grid, int X, int Y), (double Before, double After)> _samples = [];

    /// <inheritdoc/>
    public string Name => "Sculpt Terrain";

    /// <summary>Gets whether the stroke changed anything.</summary>
    public bool IsEmpty => _samples.Count == 0;

    /// <summary>Remembers a sample's height before the stroke first changes it.</summary>
    /// <param name="grid">The grid.</param>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    public void Remember(ITerrainHeightGrid grid, int x, int y)
    {
        if (!_samples.ContainsKey((grid, x, y)))
        {
            double height = grid.GetHeight(x, y);
            _samples[(grid, x, y)] = (height, height);
        }
    }

    /// <summary>Records where every touched sample ended up, once the stroke is over.</summary>
    public void Finish()
    {
        foreach ((ITerrainHeightGrid grid, int x, int y) in _samples.Keys.ToArray())
        {
            _samples[(grid, x, y)] = (_samples[(grid, x, y)].Before, grid.GetHeight(x, y));
        }
    }

    /// <inheritdoc/>
    public void Apply() => Put(after: true);

    /// <inheritdoc/>
    public void Undo() => Put(after: false);

    private void Put(bool after)
    {
        Dictionary<ITerrainHeightGrid, (int Left, int Top, int Right, int Bottom)> touched = [];
        foreach (((ITerrainHeightGrid grid, int x, int y), (double before, double later)) in _samples)
        {
            grid.SetHeight(x, y, after ? later : before);
            touched[grid] = touched.TryGetValue(grid, out var box)
                ? (Math.Min(box.Left, x), Math.Min(box.Top, y), Math.Max(box.Right, x), Math.Max(box.Bottom, y))
                : (x, y, x, y);
        }

        foreach ((ITerrainHeightGrid grid, (int left, int top, int right, int bottom)) in touched)
        {
            terrain.Refresh(grid, left, top, right, bottom);
        }
    }
}
