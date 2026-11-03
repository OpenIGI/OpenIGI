using System.Runtime.CompilerServices;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// Project I.G.I.'s ground opened for editing: the <c>terrain.hmp</c> grids the level's <c>HeightMap</c> tasks lay over
/// the octree, each one a grid the editor's brushes shape.
/// </summary>
/// <remarks>
/// <para>
/// The octree's own geometry (<c>terrain.cmd</c>) stays as it is. What changes is the heightmap the original adds on top
/// of it for the fine levels of detail, the ground queries and the ray tests (<see cref="TerrainHeightBindings"/>), so
/// a changed sample moves the ground the level stands on at once and runs unchanged in the original game.
/// </para>
/// <para>
/// A grid sample is a signed byte, a sixteenth of a metre a step about a bias of 64: the ground can go up to
/// <see cref="MostRaise"/> above and <see cref="MostSink"/> below the octree's surface and no further. Heights are kept
/// unrounded while the editor runs, so a slow brush builds up instead of rounding back each frame.
/// </para>
/// <para>
/// Only the grids' squares can be shaped; the coarse levels of detail beyond the heightmap's reach keep the octree's
/// shape, as they do for the retail heightmaps.
/// </para>
/// </remarks>
public sealed partial class Igi1EditableTerrain : IEditableTerrain
{
    /// <summary>The heightmap's path relative to the mission's folder.</summary>
    public const string HeightmapMissionPath = "terrain/terrain.hmp";

    /// <summary>The highest a sample can lift the ground above the octree's surface, in metres.</summary>
    public const double MostRaise = (sbyte.MaxValue - TerrainHeightGrid.Bias) * MetresPerStep;

    /// <summary>The furthest a sample can sink the ground below the octree's surface, in metres (negative).</summary>
    public const double MostSink = (sbyte.MinValue - TerrainHeightGrid.Bias) * MetresPerStep;

    private const double MetresPerStep = TerrainHeightGrid.UnitsPerSample / WorldScene.UnitsPerMetre;

    // How far from its loaded height the ground may move per sample in from a grid's edge, in metres.
    private const double EdgeSlopeMetres = 1.0;

    private static readonly ConditionalWeakTable<TerrainSurface, Igi1EditableTerrain> Opened = new();

    // A new grid as level 1's are laid: a node of depth 11 (256 m across) sampled every 2 m.
    private const int AreaLevel = 11;
    private const int AreaSize = 128;

    private static readonly KeyValuePair<string, string>[] HeightMapDeclaration =
    [
        new("Position", "ObjectPos"),
        new("Level", "Int32"),
        new("Size", "Int32"),
        new("isEdit", "bool8"),
        new("Bitmap ID", "Int32"),
        new("NORMALSMOOTH", "Int32"),
        new("BLURSMOOTH", "Int32"),
    ];

    private readonly TerrainSurface _surface;
    private readonly List<Grid> _grids;
    private int _savedRevision;

    private Igi1EditableTerrain(TerrainSurface surface)
    {
        _surface = surface;
        TerrainHeightmap? map = surface.Terrain.Heightmap;
        _grids = map is null
            ? []
            : [.. surface.Heights.Entries
                .GroupBy(static e => e.BitmapId)
                .Select(static g => g.First())
                .OrderBy(static e => e.BitmapId)
                .Select(e => new Grid(this, map, e))];
        _savedRevision = map?.Revision ?? 0;
    }

    /// <summary>Raised when samples changed: the ground rectangle they move, in engine units (minimum X, minimum Y, maximum X, maximum Y).</summary>
    public event Action<double, double, double, double>? Changed;

    /// <inheritdoc/>
    /// <remarks>The heightmap grids, then the mountain areas.</remarks>
    public IReadOnlyList<ITerrainHeightGrid> Grids => _allGrids ??= [.. _grids, .. _mountains];

    /// <inheritdoc/>
    /// <remarks>Only the squares the level's <c>HeightMap</c> tasks cover.</remarks>
    public bool IsPartial => true;

    /// <inheritdoc/>
    public string LimitDescription =>
        FormattableString.Invariant($"Project I.G.I.'s heightmap can lift the ground at most {MostRaise:0.#} m above its original shape and sink it {-MostSink:0} m below.");

    /// <inheritdoc/>
    public int TakeClampCount()
    {
        int count = 0;
        foreach (Grid grid in _grids)
        {
            count += grid.TakeClamps();
        }

        count += _mountainClamps;
        _mountainClamps = 0;
        return count;
    }

    /// <summary>Gets the editable ground of a level's terrain, the same one for everything that asks.</summary>
    /// <param name="surface">The level's ground.</param>
    /// <returns>The editable ground.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is <see langword="null"/>.</exception>
    public static Igi1EditableTerrain For(TerrainSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return Opened.GetValue(surface, static s => new Igi1EditableTerrain(s));
    }

    /// <inheritdoc/>
    public bool TryGetHeight(double xMetres, double yMetres, out double zMetres)
    {
        // A mountain area's samples are the ground it is being built to.
        foreach (MountainGrid mountain in _mountains)
        {
            if (mountain.TrySample(xMetres, yMetres, out zMetres))
            {
                return true;
            }
        }

        // Grids only meet along their edges, where neighbouring samples agree, so any grid covering the point answers.
        for (int i = _grids.Count - 1; i >= 0; i--)
        {
            if (_grids[i].TrySample(xMetres, yMetres, out zMetres))
            {
                return true;
            }
        }

        // Outside the grids the ground is the octree's alone: it cannot be shaped there, but it can be pointed at and painted.
        double units = WorldScene.UnitsPerMetre;
        if (TryGround(xMetres * units, yMetres * units, TerrainOctree.RootExtent, 2.0 * TerrainOctree.RootExtent, out double ground))
        {
            zMetres = ground / units;
            return true;
        }

        zMetres = 0.0;
        return false;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The octree is traced exactly through only the cells the ray crosses (<see cref="TerrainRayTrace"/>), the way shots
    /// meet it, rather than marched by height: a march asks the octree for the ground from its top down at every step, which
    /// cost a cast a fifth of a second. The height grids and mountain areas, whose shape is the editor's until the level is
    /// built again, are marched on their own, and an octree hit under one of them is passed through.
    /// </remarks>
    public bool Raycast(Vector3d origin, Vector3d direction, double maximumMetres, out Vector3d hit)
    {
        hit = default;
        double length = direction.Length;
        if (length <= 0.0)
        {
            return false;
        }

        Vector3d step = direction / length;
        bool shaped = TerrainHeightGrids.March(TryShapedHeight, origin, step, maximumMetres, out Vector3d shapedHit);
        double reach = shaped ? (shapedHit - origin).Length : maximumMetres;
        if (TryTraceOctree(origin, step, reach, out Vector3d octreeHit))
        {
            hit = octreeHit;
            return true;
        }

        hit = shapedHit;
        return shaped;
    }

    // The ground only the height grids and mountain areas give, nothing where neither covers the point.
    private bool TryShapedHeight(double xMetres, double yMetres, out double zMetres)
    {
        foreach (MountainGrid mountain in _mountains)
        {
            if (mountain.TrySample(xMetres, yMetres, out zMetres))
            {
                return true;
            }
        }

        for (int i = _grids.Count - 1; i >= 0; i--)
        {
            if (_grids[i].TrySample(xMetres, yMetres, out zMetres))
            {
                return true;
            }
        }

        zMetres = 0.0;
        return false;
    }

    // The first octree surface along the ray outside the shaped areas, traced again past any hit inside one.
    private bool TryTraceOctree(Vector3d origin, Vector3d step, double reachMetres, out Vector3d hit)
    {
        const int MostPasses = 16;
        const double PastMetres = 0.05;
        hit = default;
        if (!_surface.Terrain.IsUsable)
        {
            return false;
        }

        double units = WorldScene.UnitsPerMetre;
        double travelled = 0.0;
        for (int pass = 0; pass < MostPasses && travelled < reachMetres; pass++)
        {
            Vector3d from = origin + (step * travelled);
            Vector3d to = origin + (step * reachMetres);
            if (!TerrainRayTrace.TryTrace(_surface.Terrain, new WorldTraceRequest(from * units, to * units, WorldTraceMask.Terrain), out WorldTraceHit found))
            {
                return false;
            }

            Vector3d at = found.Position / units;
            if (!TryShapedHeight(at.X, at.Y, out _))
            {
                hit = at;
                return true;
            }

            travelled = (at - origin).Length + PastMetres;
        }

        return false;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A flat grid over the 256 m node of the octree the point's ground stands in, as level 1's are laid, sampled every
    /// 2 m; the task is a retail <c>HeightMap</c> with level 1's settings, written into <c>terrain.hmp</c> (made, on a level
    /// that ships none) when the mission is saved.
    /// </remarks>
    public TerrainArea? AddArea(Vector3d pointMetres, int? id = null)
    {
        Vector3d point = new(pointMetres.X * WorldScene.UnitsPerMetre, pointMetres.Y * WorldScene.UnitsPerMetre, pointMetres.Z * WorldScene.UnitsPerMetre);
        if (!_surface.Terrain.IsUsable || _surface.Heights.Resolve(point).IsBound)
        {
            return null;
        }

        TerrainHeightmap map = _surface.Terrain.EnsureHeightmap();
        int bitmap = id ?? map.FirstFreeId();
        if (bitmap is < 0 or >= TerrainGridTable.RecordCount || map.Table.SizeOf(bitmap) != 0)
        {
            return null;
        }

        map.AddGrid(bitmap, AreaSize);
        _surface.Heights.Regrid(map);
        if (!TerrainHeightBindings.TryBind(_surface.Terrain, point, AreaLevel, AreaSize, bitmap, out TerrainHeightBindings.Entry? entry))
        {
            map.RemoveGrid(bitmap);
            _surface.Heights.Regrid(map);
            return null;
        }

        _surface.Heights.Add(entry);
        Grid grid = new(this, map, entry);
        _grids.Add(grid);
        _allGrids = null;
        Refresh(grid, 0, 0, grid.Width - 1, grid.Height - 1);
        return new TerrainArea(
            bitmap,
            TerrainHeightBindings.HeightMapType,
            pointMetres,
            HeightMapDeclaration,
            [new("Level", AreaLevel), new("Size", AreaSize), new("Bitmap ID", bitmap), new("NORMALSMOOTH", 8), new("BLURSMOOTH", 7)],
            grid.Name);
    }

    /// <inheritdoc/>
    public void RemoveArea(TerrainArea area)
    {
        ArgumentNullException.ThrowIfNull(area);
        int index = _grids.FindIndex(g => g.Id == area.Id);
        if (index < 0 || _surface.Terrain.Heightmap is not { } map)
        {
            return;
        }

        Grid grid = _grids[index];
        (double x0, double y0) = grid.SampleUnits(-1, -1);
        (double x1, double y1) = grid.SampleUnits(grid.Width, grid.Height);
        _grids.RemoveAt(index);
        _allGrids = null;
        _surface.Heights.Remove(area.Id);
        map.RemoveGrid(area.Id);
        _surface.Heights.Regrid(map);
        Changed?.Invoke(x0, y0, x1, y1);
    }

    /// <inheritdoc/>
    public void Refresh(ITerrainHeightGrid grid, int left, int top, int right, int bottom)
    {
        if (grid is MountainGrid mountain && _mountains.Contains(mountain))
        {
            Reshaped();
            return;
        }

        if (grid is not Grid own || !ReferenceEquals(own.Owner, this))
        {
            return;
        }

        // A sample moves the triangles on both sides of it, so the rectangle reaches one sample past the changed ones.
        (double x0, double y0) = own.SampleUnits(left - 1, top - 1);
        (double x1, double y1) = own.SampleUnits(right + 1, bottom + 1);
        Changed?.Invoke(Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1));
    }

    /// <inheritdoc/>
    public void MarkSaved()
    {
        _savedRevision = _surface.Terrain.Heightmap?.Revision ?? 0;
        MarkMasksSaved();
        MarkMountainsSaved();
        foreach (MountainGrid mountain in _mountains)
        {
            mountain.MarkSaved();
        }

        foreach (Grid grid in _grids)
        {
            grid.MarkSaved();
        }
    }

    // The ground's height at a point from the octree and every grid, as the game finds it, in engine units.
    private bool TryGround(double x, double y, double top, double depth, out double z)
    {
        z = 0.0;
        if (!_surface.Terrain.IsUsable || !_surface.Terrain.TryDrop(x, y, top, depth, upward: false, out TerrainGround ground))
        {
            return false;
        }

        z = ground.Height;
        return true;
    }

    private sealed class Grid : ITerrainHeightGrid
    {
        private readonly TerrainHeightmap _map;
        private readonly TerrainHeightBindings.Entry _entry;
        private readonly double[] _offsets;
        private readonly double[] _loaded;
        private readonly double[] _bases;
        private int _changes;
        private int _savedChanges;
        private int _clamps;

        public Grid(Igi1EditableTerrain owner, TerrainHeightmap map, TerrainHeightBindings.Entry entry)
        {
            Owner = owner;
            _map = map;
            _entry = entry;
            TerrainHeightGrid grid = entry.Binding.Grid;
            Width = grid.Stride;
            Height = grid.Stride;
            _offsets = new double[Width * Height];
            _bases = new double[Width * Height];
            Array.Fill(_bases, double.NaN);
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    _offsets[(y * Width) + x] = (grid.Corner(x, y) - TerrainHeightGrid.Bias) * MetresPerStep;
                }
            }

            _loaded = (double[])_offsets.Clone();
        }

        /// <summary>Gets whether any sample stands somewhere other than where the level was loaded with it.</summary>
        public bool HasMoved => !_offsets.AsSpan().SequenceEqual(_loaded);

        /// <summary>Gets the square the grid covers, in engine units.</summary>
        public (double MinX, double MinY, double MaxX, double MaxY) Square
        {
            get
            {
                (double x0, double y0) = SampleUnits(0, 0);
                (double x1, double y1) = SampleUnits(Width - 1, Height - 1);
                return (x0, y0, x1, y1);
            }
        }

        /// <summary>Gets the grid's sample spacing, in metres.</summary>
        public double SpacingMetres => 1.0 / _entry.Binding.Scale / WorldScene.UnitsPerMetre;

        /// <summary>Gets the ground's height between samples, bilinearly, as it is now or as it was loaded, in metres.</summary>
        /// <param name="column">The fractional column, clamped to the grid.</param>
        /// <param name="row">The fractional row, clamped to the grid.</param>
        /// <param name="now">Whether to read the shaped ground rather than the loaded one.</param>
        /// <returns>The height.</returns>
        public double HeightAt(double column, double row, bool now)
        {
            column = Math.Clamp(column, 0.0, Width - 1);
            row = Math.Clamp(row, 0.0, Height - 1);
            int x = Math.Min((int)column, Width - 2);
            int y = Math.Min((int)row, Height - 2);
            double fx = column - x;
            double fy = row - y;
            double At(int i, int j) => Base(i, j) + (now ? _offsets[(j * Width) + i] : _loaded[(j * Width) + i]);
            double top = At(x, y) + ((At(x + 1, y) - At(x, y)) * fx);
            double bottom = At(x, y + 1) + ((At(x + 1, y + 1) - At(x, y + 1)) * fx);
            return top + ((bottom - top) * fy);
        }

        /// <summary>Gets the fractional sample coordinates of a point given in engine units.</summary>
        /// <param name="xUnits">The point's X.</param>
        /// <param name="yUnits">The point's Y.</param>
        /// <returns>The column and row, unbounded.</returns>
        public (double Column, double Row) GridOf(double xUnits, double yUnits) =>
            ((xUnits - _entry.Binding.OriginX) * _entry.Binding.Scale, (yUnits - _entry.Binding.OriginY) * _entry.Binding.Scale);

        /// <summary>Gets a sample's displacement now, in metres.</summary>
        public double OffsetAt(int x, int y) => _offsets[(y * Width) + x];

        /// <summary>Gets the octree's own surface under a sample, in metres.</summary>
        public double BaseAt(int x, int y) => Base(x, y);

        /// <summary>Gets how far the ground has moved since the level was loaded at a point, bilinearly between samples, in metres.</summary>
        /// <param name="xUnits">The point's X, in engine units.</param>
        /// <param name="yUnits">The point's Y, in engine units.</param>
        /// <param name="metres">Receives the change; nought outside the grid.</param>
        /// <returns>Whether the grid covers the point.</returns>
        public bool TryMoved(double xUnits, double yUnits, out double metres)
        {
            metres = 0.0;
            double column = (xUnits - _entry.Binding.OriginX) * _entry.Binding.Scale;
            double row = (yUnits - _entry.Binding.OriginY) * _entry.Binding.Scale;
            if (column < 0.0 || row < 0.0 || column > Width - 1 || row > Height - 1)
            {
                return false;
            }

            int x = Math.Min((int)column, Width - 2);
            int y = Math.Min((int)row, Height - 2);
            double fx = column - x;
            double fy = row - y;
            double Moved(int i, int j) => _offsets[(j * Width) + i] - _loaded[(j * Width) + i];
            double top = Moved(x, y) + ((Moved(x + 1, y) - Moved(x, y)) * fx);
            double bottom = Moved(x, y + 1) + ((Moved(x + 1, y + 1) - Moved(x, y + 1)) * fx);
            metres = top + ((bottom - top) * fy);
            return true;
        }

        public Igi1EditableTerrain Owner { get; }

        public int Id => _entry.BitmapId;

        public string Name => $"heightmap {_entry.BitmapId}";

        public int Width { get; }

        public int Height { get; }

        public bool IsChanged => _changes != _savedChanges;

        public (double X, double Y) SampleUnits(int x, int y) =>
            (_entry.Binding.OriginX + (x / _entry.Binding.Scale), _entry.Binding.OriginY + (y / _entry.Binding.Scale));

        public (double X, double Y) SampleMetres(int x, int y)
        {
            (double ux, double uy) = SampleUnits(x, y);
            return (ux / WorldScene.UnitsPerMetre, uy / WorldScene.UnitsPerMetre);
        }

        public (double Column, double Row) ToGrid(double xMetres, double yMetres) =>
            (((xMetres * WorldScene.UnitsPerMetre) - _entry.Binding.OriginX) * _entry.Binding.Scale,
             ((yMetres * WorldScene.UnitsPerMetre) - _entry.Binding.OriginY) * _entry.Binding.Scale);

        public double GetHeight(int x, int y) => Base(x, y) + _offsets[(y * Width) + x];

        public void SetHeight(int x, int y, double zMetres)
        {
            // Inside a mountain area the area is the ground; a heightmap there was absorbed into it.
            (double ux, double uy) = SampleUnits(x, y);
            if (Owner.InMountain(ux, uy))
            {
                return;
            }

            int index = (y * Width) + x;
            double wanted = zMetres - Base(x, y);
            double offset = Math.Clamp(wanted, MostSink, MostRaise);

            // Near the grid's edge the ground may leave its loaded height only gradually, and not at all on the edge: the
            // chunks beyond it never take this grid (the original resolves a chunk's grid by its centre), so a moved edge
            // sample opens a crack there the sky shows through.
            int fromEdge = Math.Min(Math.Min(x, y), Math.Min(Width - 1 - x, Height - 1 - y));
            double reach = fromEdge * EdgeSlopeMetres;
            offset = Math.Clamp(offset, _loaded[index] - reach, _loaded[index] + reach);
            if (offset != wanted)
            {
                _clamps++;
            }

            if (offset == _offsets[index])
            {
                return;
            }

            _offsets[index] = offset;
            _map.SetCorner(_entry.BitmapId, x, y, (int)Math.Round(offset / MetresPerStep) + (int)TerrainHeightGrid.Bias);
            _changes++;
        }

        public int TakeClamps()
        {
            int clamps = _clamps;
            _clamps = 0;
            return clamps;
        }

        public void MarkSaved() => _savedChanges = _changes;

        /// <summary>Lets a mountain area take this grid in: no displacement any more, and none to bake, as the area's ground has it.</summary>
        public void Absorb()
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    _map.SetCorner(_entry.BitmapId, x, y, (int)TerrainHeightGrid.Bias);
                }
            }

            Array.Clear(_offsets);
            Array.Clear(_loaded);
            Array.Fill(_bases, double.NaN);
            _changes++;
        }

        // The octree's own surface under a sample, in metres: the ground the game finds there less whatever grid
        // displaces it at that point (the innermost one, which is this grid except on its far edges). Found once
        // per sample, the first time it is needed.
        private double Base(int x, int y)
        {
            int index = (y * Width) + x;
            double known = _bases[index];
            if (!double.IsNaN(known))
            {
                return known;
            }

            (double ux, double uy) = SampleUnits(x, y);
            double extent = _entry.Extent;
            double top = _entry.Centre.Z + extent + (2.0 * extent * _entry.CubesAbove);
            double depth = 2.0 * extent * (1 + _entry.CubesAbove + _entry.CubesBelow);
            double found = 0.0;
            if (Owner.TryGround(ux, uy, top, depth, out double ground))
            {
                double displaced = Owner._surface.Heights.Resolve(new Vector3d(ux, uy, ground)).Sample(ux, uy);
                found = (ground - displaced) / WorldScene.UnitsPerMetre;
            }

            _bases[index] = found;
            return found;
        }
    }
}
