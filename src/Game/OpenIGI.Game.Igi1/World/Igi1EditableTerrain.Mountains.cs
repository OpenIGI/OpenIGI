using System.Globalization;
using OpenIGI.Core.Threading;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// Mountain areas: squares of the level whose ground is built again from a height grid with no limit, into the octree
/// itself (<see cref="TerrainRegionBuild"/>), where the heightmap can only nudge the ground a few metres.
/// </summary>
/// <remarks>
/// <para>
/// An area is the 512 m square of the octree holding the point it is laid at, sampled every 4 m (the spacing of the
/// finest geometry the original draws), starting as the ground the game finds there now. While it is shaped its square
/// is rebuilt away from the main thread as often as a rebuild finishes (a few times a second), put in place the frame it
/// is ready, and once more when a stroke settles, so the new ground is drawn, stood on and driven over as it grows;
/// saving writes <c>terrain.ctr</c> and <c>terrain.cmd</c>.
/// </para>
/// <para>
/// The level's heightmap grids inside the square are absorbed: their displacement is part of the ground the area starts
/// from, so they are zeroed, and the brushes leave them alone there, rather than add it a second time.
/// </para>
/// </remarks>
public sealed partial class Igi1EditableTerrain
{
    private readonly List<MountainGrid> _mountains = [];
    private Task<(byte[] Ctr, byte[] Cmd, int Clamped)?>? _building;
    private (byte[] Ctr, byte[] Cmd)? _beforeMountains;
    private bool _rebuildPending;
    private int _mountainRevision;
    private int _savedMountainRevision;
    private int _mountainClamps;
    private IReadOnlyList<ITerrainHeightGrid>? _allGrids;

    /// <summary>Raised when the octree was rebuilt under a square: the square, in engine units (minimum X, minimum Y, maximum X, maximum Y).</summary>
    public event Action<double, double, double, double>? Rebuilt;

    /// <summary>Gets or sets where a mountain area is rebuilt while it is shaped; inline, a frame at a time, when null.</summary>
    public IWorkScheduler? Work { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<ITerrainHeightGrid> Mountains => _mountains;

    /// <inheritdoc/>
    public ITerrainHeightGrid? AddMountain(Vector3d pointMetres)
    {
        double units = WorldScene.UnitsPerMetre;
        long side = 2L * (TerrainOctree.RootExtent >> TerrainRegionBuild.DefaultExponent);
        long minX = (long)Math.Floor(pointMetres.X * units / side) * side;
        long minY = (long)Math.Floor(pointMetres.Y * units / side) * side;
        if (!_surface.Terrain.IsUsable || _mountains.Any(m => m.MinX == minX && m.MinY == minY)
            || !TryGround(pointMetres.X * units, pointMetres.Y * units, TerrainOctree.RootExtent, 2.0 * TerrainOctree.RootExtent, out double middle))
        {
            return null;
        }

        // A heightmap grid wider than the square cannot be taken in by it.
        Grid[] inside = [.. _grids.Where(g => Overlaps(g.Square, minX, minY, side))];
        if (inside.Any(g => g.Square.MinX < minX || g.Square.MinY < minY || g.Square.MaxX > minX + side || g.Square.MaxY > minY + side))
        {
            return null;
        }

        MountainGrid mountain = new(this, minX, minY, side);
        mountain.Sample(middle);
        foreach (Grid grid in inside)
        {
            grid.Absorb();
            (double x0, double y0, double x1, double y1) = grid.Square;
            Changed?.Invoke(x0, y0, x1, y1);
        }

        _beforeMountains ??= (_surface.Terrain.Octree.Data.ToArray(), _surface.Terrain.Commands.Data.ToArray());
        _mountains.Add(mountain);
        _allGrids = null;
        Rebuild();
        return mountain;
    }

    /// <inheritdoc/>
    public void RemoveMountain(ITerrainHeightGrid area)
    {
        if (area is not MountainGrid mountain || !_mountains.Remove(mountain))
        {
            return;
        }

        _allGrids = null;
        Rebuild();

        // The square taken away is drawn again from the geometry it had.
        Rebuilt?.Invoke(mountain.MinX, mountain.MinY, mountain.MinX + mountain.Side, mountain.MinY + mountain.Side);
    }

    /// <inheritdoc/>
    /// <remarks>Waits for a rebuild under way and makes one more if the samples moved since it began.</remarks>
    public void Settle()
    {
        if (_building is { } building)
        {
            Take(building.GetAwaiter().GetResult());
        }

        if (_rebuildPending)
        {
            Take(Build(Snapshot()));
        }
    }

    /// <summary>Puts a rebuild that finished away from the main thread in place, and starts the next if the samples moved
    /// meanwhile. Called once a frame by whatever draws the ground.</summary>
    public void Poll()
    {
        if (_building is { IsCompleted: true } building)
        {
            Take(building.GetAwaiter().GetResult());
            if (_rebuildPending)
            {
                StartRebuild();
            }
        }
    }

    // A mountain's samples changed: rebuilt away from the main thread, or after the rebuild under way.
    private void Reshaped()
    {
        _mountainRevision++;
        if (_building is null)
        {
            StartRebuild();
        }
        else
        {
            _rebuildPending = true;
        }
    }

    // Every mountain built into the octree at once, here; for laying and taking away an area.
    private void Rebuild()
    {
        if (_building is { } building)
        {
            building.GetAwaiter().GetResult();
            _building = null;
        }

        Take(Build(Snapshot()));
    }

    private void StartRebuild()
    {
        _rebuildPending = false;
        Mountain[] snapshot = Snapshot();
        _building = (Work ?? InlineWorkScheduler.Instance).Run(() => Build(snapshot));
    }

    // The samples as they stand, for a build that does not touch the live grids.
    private Mountain[] Snapshot()
    {
        _rebuildPending = false;
        return [.. _mountains.Select(static m => m.Snapshot())];
    }

    // Every mountain built into the octree as it stood before the first: files only, safe on any thread.
    private (byte[] Ctr, byte[] Cmd, int Clamped)? Build(Mountain[] mountains)
    {
        if (_beforeMountains is not { } before)
        {
            return null;
        }

        (byte[] ctr, byte[] cmd) = before;
        int clamped = 0;
        foreach (Mountain mountain in mountains)
        {
            if (TerrainRegionBuild.Build(ctr, cmd, TerrainRegionBuild.DefaultExponent, mountain.MinX, mountain.MinY, mountain.HeightUnits) is { } built)
            {
                (ctr, cmd) = (built.Ctr, built.Cmd);
                clamped += built.Clamped;
            }
        }

        return (ctr, cmd, clamped);
    }

    // A build's files put in place, on the main thread, and every square's drawing dropped.
    private void Take((byte[] Ctr, byte[] Cmd, int Clamped)? built)
    {
        _building = null;
        if (built is not { } files)
        {
            return;
        }

        _surface.Terrain.ReplaceGeometry(TerrainOctree.Read(files.Ctr), TerrainCommands.Read(files.Cmd));
        _mountainClamps += files.Clamped;
        _mountainRevision++;
        foreach (MountainGrid mountain in _mountains)
        {
            Rebuilt?.Invoke(mountain.MinX, mountain.MinY, mountain.MinX + mountain.Side, mountain.MinY + mountain.Side);
        }
    }

    // A mountain's square and samples, copied.
    private sealed record Mountain(long MinX, long MinY, long Side, int Width, long Spacing, double[] Heights)
    {
        public double HeightUnits(long x, long y)
        {
            int column = (int)Math.Clamp((x - MinX) / Spacing, 0, Width - 1);
            int row = (int)Math.Clamp((y - MinY) / Spacing, 0, Width - 1);
            return Heights[(row * Width) + column] * WorldScene.UnitsPerMetre;
        }
    }

    // The octree, when mountains changed it since the last save.
    private (byte[] Ctr, byte[] Cmd)? ChangedMountains() =>
        _beforeMountains is not null && _mountainRevision != _savedMountainRevision
            ? (_surface.Terrain.Octree.Data.ToArray(), _surface.Terrain.Commands.Data.ToArray())
            : null;

    private void MarkMountainsSaved() => _savedMountainRevision = _mountainRevision;

    // Whether a point is in a mountain square, where the heightmap grids are absorbed.
    private bool InMountain(double xUnits, double yUnits) =>
        _mountains.Any(m => xUnits >= m.MinX && yUnits >= m.MinY && xUnits <= m.MinX + m.Side && yUnits <= m.MinY + m.Side);

    private static bool Overlaps((double MinX, double MinY, double MaxX, double MaxY) square, long minX, long minY, long side) =>
        square.MaxX > minX && square.MinX < minX + side && square.MaxY > minY && square.MinY < minY + side;

    // One mountain area: a square of free height samples, its border held to the ground beyond it.
    private sealed class MountainGrid(Igi1EditableTerrain owner, long minX, long minY, long side) : ITerrainHeightGrid
    {
        private readonly long _spacing = 2L * (TerrainOctree.RootExtent >> TerrainRegionGenerator.SampleExponent);
        private double[] _heights = [];
        private int _changes;
        private int _savedChanges;

        public long MinX { get; } = minX;

        public long MinY { get; } = minY;

        public long Side { get; } = side;

        public string Name => string.Create(
            CultureInfo.InvariantCulture,
            $"mountain area at {MinX / WorldScene.UnitsPerMetre:0}, {MinY / WorldScene.UnitsPerMetre:0}");

        public int Width => (int)(Side / _spacing) + 1;

        public int Height => Width;

        public bool IsChanged => _changes != _savedChanges;

        // Every sample from the ground the game finds there now, heightmap and all.
        public void Sample(double near)
        {
            _heights = new double[Width * Height];
            double units = WorldScene.UnitsPerMetre;
            double reach = 2048.0 * units;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    double ux = MinX + (x * _spacing), uy = MinY + (y * _spacing);
                    _heights[(y * Width) + x] = owner._surface.Terrain.TryDrop(ux, uy, near + reach, 2.0 * reach, upward: false, out TerrainGround ground)
                        ? ground.Height / units
                        : near / units;
                }
            }
        }

        public Mountain Snapshot() => new(MinX, MinY, Side, Width, _spacing, (double[])_heights.Clone());

        public (double X, double Y) SampleMetres(int x, int y) =>
            ((MinX + (x * _spacing)) / WorldScene.UnitsPerMetre, (MinY + (y * _spacing)) / WorldScene.UnitsPerMetre);

        public (double Column, double Row) ToGrid(double xMetres, double yMetres) =>
            (((xMetres * WorldScene.UnitsPerMetre) - MinX) / _spacing, ((yMetres * WorldScene.UnitsPerMetre) - MinY) / _spacing);

        public double GetHeight(int x, int y) => _heights[(y * Width) + x];

        // The border is the ground beyond the square's: it stays, so the two meet.
        public void SetHeight(int x, int y, double zMetres)
        {
            if (x <= 0 || y <= 0 || x >= Width - 1 || y >= Height - 1 || _heights[(y * Width) + x] == zMetres)
            {
                return;
            }

            _heights[(y * Width) + x] = zMetres;
            _changes++;
        }

        public void MarkSaved() => _savedChanges = _changes;
    }
}
