using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using static OpenIGI.Game.World.DeclaredTaskValues;

namespace OpenIGI.Game.World;

/// <summary>
/// The heightmap bindings a level's <c>HeightMap</c> tasks publish: which <c>terrain.hmp</c> grid
/// displaces the fine terrain inside which octree cube, and at what origin and scale.
/// </summary>
/// <remarks>
/// <para>
/// This is the 48-byte table at <c>0xBA2030</c>, indexed by heightmap id, that <c>sub_507410</c>
/// fills when a <c>HeightMap</c> task is parsed — and it is <em>not</em> in any terrain file. The
/// task's position is snapped to the octree node of its <c>Level</c>, the node's minimum corner
/// becomes the grid's origin, and the sample scale is <c>1 / 2^(31 - Level - log2(Size))</c>, so the
/// grid's cells tile the node exactly. The id is the task's texture-modifier slot plus one, which is
/// why id zero means "none".
/// </para>
/// <para>
/// Which binding a chunk gets is decided by the descent, not by the chunk: the task registers itself
/// in that node, the descent's enter hook (<c>sub_506F00</c>) pushes the id when it enters the node
/// and the leave hook (<c>sub_506F60</c>) pops it, so a chunk takes the innermost task whose cube
/// contains it — the same rule for the renderer (<c>sub_4D14E0</c> copies the id into every
/// descriptor), the ground query (<c>sub_4CA010</c>) and the ray test (<c>sub_4C9020</c>), all three
/// of which sample the same table. <see cref="Resolve(Vector3d)"/> is that rule without the stack.
/// </para>
/// <para>
/// The original drops a ray from the task's position to find the ground before snapping its Z to the node, and
/// then (with <c>Drop To Ground</c>, which every retail task leaves on) adds a child box sharing the grid for every
/// cube above or below that the ground under the node reaches into, so a grid over a slope that crosses a cube's
/// top or bottom still displaces all of it. <see cref="Entry.CubesBelow"/> and <see cref="Entry.CubesAbove"/> are
/// those children. <b>Divergence, recorded:</b> the original finds the ground's lowest and highest points from 200
/// seeded random drops over the node; the port drops on a regular 17 × 17 lattice, which can only find the same
/// extremes or more extreme ones.
/// </para>
/// <para>
/// Without these bindings the fine levels of detail are drawn as <c>terrain.cmd</c> stores them, and
/// that is not the ground: the fine blocks are authored as the surface <em>minus</em> the heightmap,
/// so a level that ships a heightmap shows its bumps inverted as sharp pits and ridges until the grid
/// is added back. Measured over level 1: 1324 fine edges steeper than 45° without the heightmap, 60
/// with it.
/// </para>
/// </remarks>
public sealed class TerrainHeightBindings
{
    /// <summary>The task type that publishes a binding.</summary>
    public const string HeightMapType = "HeightMap";

    private static readonly ConditionalWeakTable<TerrainData, TerrainHeightBindings> Bindings = new();
    private static readonly TerrainHeightBindings Empty = new([], 0);

    private Entry[] _entries;

    private TerrainHeightBindings(Entry[] entries, int skipped)
    {
        _entries = entries;
        Skipped = skipped;
    }

    /// <summary>Gets the bindings, innermost (deepest level) first.</summary>
    public IReadOnlyList<Entry> Entries => _entries;

    /// <summary>Gets how many <c>HeightMap</c> tasks named a grid the level does not carry at the declared size.</summary>
    /// <remarks>The original allocates a flat grid for those, which displaces nothing — the same as skipping them.</remarks>
    public int Skipped { get; }

    /// <summary>Gets a value indicating whether any binding exists.</summary>
    public bool IsEmpty => _entries.Length == 0;

    /// <summary>Walks a level's task tree for its <c>HeightMap</c> tasks and remembers the result for the terrain.</summary>
    /// <param name="terrain">The loaded terrain, whose <see cref="TerrainData.Heightmap"/> supplies the grids.</param>
    /// <param name="levelRoot">The task the level's subtree hangs off.</param>
    /// <returns>The bindings; empty when the level ships no heightmap or declares no task.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static TerrainHeightBindings Bind(TerrainData terrain, GameTask levelRoot)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(levelRoot);

        TerrainHeightBindings bindings = Build(terrain, levelRoot);
        Bindings.Remove(terrain);
        Bindings.Add(terrain, bindings);
        return bindings;
    }

    /// <summary>Builds the bindings without remembering them.</summary>
    /// <param name="terrain">The loaded terrain.</param>
    /// <param name="levelRoot">The task the level's subtree hangs off.</param>
    /// <returns>The bindings.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static TerrainHeightBindings Build(TerrainData terrain, GameTask levelRoot)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(levelRoot);

        List<Entry> entries = [];
        int skipped = 0;

        if (terrain.Heightmap is not null)
        {
            foreach (GameTask task in levelRoot.Descendants())
            {
                if (task is not DeclaredTask declared
                    || !string.Equals(declared.Type.Name, HeightMapType, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!WorldScene.TryGetPosition(declared, out Vector3d metres))
                {
                    skipped++;
                    continue;
                }

                Vector3d position = new(
                    metres.X * WorldScene.UnitsPerMetre,
                    metres.Y * WorldScene.UnitsPerMetre,
                    metres.Z * WorldScene.UnitsPerMetre);

                if (TryBind(
                        terrain,
                        position,
                        IntStrict(declared, "Level", -1),
                        IntStrict(declared, "Size", -1),
                        IntStrict(declared, "Bitmap ID", -1),
                        out Entry? entry))
                {
                    entries.Add(entry);
                }
                else
                {
                    skipped++;
                }
            }
        }

        // Deeper is smaller and more specific; the descent's stack tests the most recently entered
        // first, and the last task registered in a shared node is entered last. A stable sort keeps
        // tree order within a level, so the later task wins the tie.
        entries = [.. entries.OrderByDescending(e => e.Level)];
        return new TerrainHeightBindings([.. entries], skipped);
    }

    /// <summary>Places a grid the way a <c>HeightMap</c> task does: dropped to the ground, snapped to its node, and reaching into the cubes above and below that the ground under it crosses.</summary>
    /// <param name="terrain">The level's terrain, whose <see cref="TerrainData.Heightmap"/> holds the grid.</param>
    /// <param name="position">The task's position, in engine units.</param>
    /// <param name="level">The task's <c>Level</c>: the depth of the node the grid covers.</param>
    /// <param name="size">The task's <c>Size</c>: the grid's cells along an edge.</param>
    /// <param name="bitmapId">The task's <c>Bitmap ID</c>.</param>
    /// <param name="entry">Receives the binding.</param>
    /// <returns><see langword="false"/> when the task names no grid the level carries at that size, or an impossible node.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="terrain"/> is <see langword="null"/>.</exception>
    public static bool TryBind(TerrainData terrain, Vector3d position, int level, int size, int bitmapId, [NotNullWhen(true)] out Entry? entry)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        entry = null;
        if (terrain.Heightmap is null)
        {
            return false;
        }

        // The ground under the authored point, as the original drops for it before snapping.
        if (terrain.IsUsable
            && terrain.TryDrop(position.X, position.Y, position.Z, GroundSearchUnits, upward: false, out TerrainGround under))
        {
            position = new Vector3d(position.X, position.Y, under.Height);
        }

        if (!TryCreate(terrain.Heightmap, position, level, size, bitmapId, out Entry? placed) || placed is null)
        {
            return false;
        }

        entry = terrain.IsUsable ? WithChildren(terrain, placed) : placed;
        return true;
    }

    /// <summary>Adds a binding while the level runs, as a <c>HeightMap</c> task made in the editor would publish it.</summary>
    /// <param name="entry">The binding, from <see cref="TryBind"/>.</param>
    /// <remarks>It goes after the bindings of its own depth, as the task registered last is the one the descent enters last.</remarks>
    public void Add(Entry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (ReferenceEquals(this, Empty))
        {
            throw new InvalidOperationException("The shared empty set of bindings cannot be added to; bind the level first.");
        }

        int index = Array.FindIndex(_entries, e => e.Level < entry.Level);
        List<Entry> entries = [.. _entries];
        entries.Insert(index < 0 ? entries.Count : index, entry);
        _entries = [.. entries];
    }

    /// <summary>Takes away every binding of a grid.</summary>
    /// <param name="bitmapId">The grid's id.</param>
    /// <returns>Whether any binding went.</returns>
    public bool Remove(int bitmapId)
    {
        int before = _entries.Length;
        _entries = [.. _entries.Where(e => e.BitmapId != bitmapId)];
        return _entries.Length != before;
    }

    /// <summary>Fetches every binding's grid again from a heightmap whose layout changed (<see cref="TerrainHeightmap.AddGrid"/>).</summary>
    /// <param name="heightmap">The heightmap.</param>
    /// <exception cref="ArgumentNullException"><paramref name="heightmap"/> is <see langword="null"/>.</exception>
    public void Regrid(TerrainHeightmap heightmap)
    {
        ArgumentNullException.ThrowIfNull(heightmap);
        _entries = [.. _entries.Select(e => heightmap.TryGetGrid(e.BitmapId, out TerrainHeightGrid grid)
            ? e with { Binding = e.Binding with { Grid = grid } }
            : e)];
    }

    /// <summary>Gets the bindings bound to a terrain, or none.</summary>
    /// <param name="terrain">The terrain.</param>
    /// <returns>What <see cref="Bind"/> recorded, or an empty set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="terrain"/> is <see langword="null"/>.</exception>
    public static TerrainHeightBindings For(TerrainData terrain)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        return Bindings.TryGetValue(terrain, out TerrainHeightBindings? bindings) ? bindings : Empty;
    }

    /// <summary>Finds the binding that applies at a point: the innermost task whose cube contains it.</summary>
    /// <param name="point">The point in engine units — a chunk's centre, or a query point.</param>
    /// <returns>The binding, or an unbound default when no cube contains the point.</returns>
    public TerrainHeightBinding Resolve(Vector3d point)
    {
        foreach (Entry entry in _entries)
        {
            if (entry.Contains(point))
            {
                return entry.Binding;
            }
        }

        return default;
    }

    /// <summary>Finds the binding that applies to a chunk.</summary>
    /// <param name="chunk">The chunk, whose centre decides.</param>
    /// <returns>The binding, or an unbound default.</returns>
    public TerrainHeightBinding Resolve(in TerrainChunk chunk) => Resolve(chunk.Position);

    /// <summary>Finds the binding that applies to a leaf the collision descent reached.</summary>
    /// <param name="leaf">The leaf.</param>
    /// <returns>The binding, or an unbound default.</returns>
    public TerrainHeightBinding Resolve(in TerrainLeaf leaf) =>
        Resolve(new Vector3d(leaf.Cursor.Centre.X, leaf.Cursor.Centre.Y, leaf.Cursor.Centre.Z));

    // How far the drops for a task's ground look, up and down: the original's are effectively unbounded, but a
    // drop walks the column a leaf at a time, and no retail terrain spans more than this.
    private const double GroundSearchUnits = 2048.0 * WorldScene.UnitsPerMetre;

    private const int ChildLattice = 16;

    // The cubes above and below a task's own that the ground under it reaches into (sub_507410's children).
    private static Entry WithChildren(TerrainData terrain, Entry entry)
    {
        double extent = entry.Extent;
        double low = entry.Centre.Z;
        double high = entry.Centre.Z;
        double top = entry.Centre.Z + Math.Min(GroundSearchUnits, 4.0 * extent);
        for (int j = 0; j <= ChildLattice; j++)
        {
            for (int i = 0; i <= ChildLattice; i++)
            {
                double x = entry.Centre.X - extent + (2.0 * extent * i / ChildLattice);
                double y = entry.Centre.Y - extent + (2.0 * extent * j / ChildLattice);
                if (terrain.TryDrop(x, y, top, Math.Min(GroundSearchUnits, 8.0 * extent), upward: false, out TerrainGround ground))
                {
                    low = Math.Min(low, ground.Height);
                    high = Math.Max(high, ground.Height);
                }
            }
        }

        int below = 0;
        for (double bottom = entry.Centre.Z - extent; low < bottom; bottom -= 2.0 * extent)
        {
            below++;
        }

        int above = 0;
        for (double ceiling = entry.Centre.Z + extent; high > ceiling; ceiling += 2.0 * extent)
        {
            above++;
        }

        return entry with { CubesBelow = below, CubesAbove = above };
    }

    private static bool TryCreate(
        TerrainHeightmap heightmap,
        Vector3d position,
        int level,
        int size,
        int bitmapId,
        out Entry? entry)
    {
        entry = null;
        if (level is < 0 or > 30 || size <= 0 || !int.IsPow2(size))
        {
            return false;
        }

        // sub_507410 accepts sizes 8 to 256 and faults on anything else ("Illegal shift size"); a grid
        // whose stored size differs from the declared one is replaced by a flat grid, so it is skipped.
        if (size is < 8 or > 256
            || bitmapId < 0
            || !heightmap.TryGetGrid(bitmapId, out TerrainHeightGrid grid)
            || grid.Size != size)
        {
            return false;
        }

        int scaleExponent = 31 - level - int.Log2(size);
        if (scaleExponent is < 0 or > 62)
        {
            return false;
        }

        // The origin is the node's minimum corner: the position truncated, then masked to a multiple
        // of the node's span — the same snap sub_507410 applies before it publishes.
        long extent = TerrainOctree.RootExtent >> level;
        long span = 2 * extent;
        long originX = (long)position.X & -span;
        long originY = (long)position.Y & -span;
        long originZ = (long)position.Z & -span;

        entry = new Entry(
            level,
            bitmapId,
            new Vector3d(originX + extent, originY + extent, originZ + extent),
            new TerrainHeightBinding(grid, originX, originY, 1.0 / (1L << scaleExponent)));
        return true;
    }

    /// <summary>One published binding: a grid over one octree node.</summary>
    /// <param name="Level">The node's depth, which is the task's <c>Level</c>.</param>
    /// <param name="BitmapId">The grid's id in <c>terrain.hmp</c>.</param>
    /// <param name="Centre">The node's centre in engine units.</param>
    /// <param name="Binding">The grid, placed and scaled.</param>
    public sealed record Entry(int Level, int BitmapId, Vector3d Centre, TerrainHeightBinding Binding)
    {
        /// <summary>Gets the node's half-width in engine units.</summary>
        public long Extent => TerrainOctree.RootExtent >> Level;

        /// <summary>Gets how many cubes directly below the node the grid also displaces, for ground that reaches down into them.</summary>
        public int CubesBelow { get; init; }

        /// <summary>Gets how many cubes directly above the node the grid also displaces.</summary>
        public int CubesAbove { get; init; }

        /// <summary>Returns whether the node's cube contains a point.</summary>
        /// <param name="point">The point in engine units.</param>
        /// <returns><see langword="true"/> when inside on all three axes.</returns>
        /// <remarks>
        /// A node is a cube, so Z counts: the task is registered in the node its snapped position falls
        /// in and in each child cube stacked above or below it, and the descent only enters those for chunks at
        /// their height.
        /// </remarks>
        public bool Contains(Vector3d point)
        {
            long extent = Extent;
            return point.X >= Centre.X - extent && point.X < Centre.X + extent
                && point.Y >= Centre.Y - extent && point.Y < Centre.Y + extent
                && point.Z >= Centre.Z - extent - (2 * extent * CubesBelow) && point.Z < Centre.Z + extent + (2 * extent * CubesAbove);
        }
    }
}
