using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;

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
/// <b>Divergence, recorded.</b> The original drops a ray from the task's position to find the ground
/// before snapping its Z to the node; the port snaps the authored Z. Both land in the same cube unless
/// the authored point is more than a node's height off the ground, which no retail level does.
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

    private readonly Entry[] _entries;

    private TerrainHeightBindings(Entry[] entries, int skipped)
    {
        _entries = entries;
        Entries = new ReadOnlyCollection<Entry>(entries);
        Skipped = skipped;
    }

    /// <summary>Gets the bindings, innermost (deepest level) first.</summary>
    public IReadOnlyList<Entry> Entries { get; }

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

                if (TryCreate(
                        terrain.Heightmap,
                        position,
                        GetInt32(declared, "Level"),
                        GetInt32(declared, "Size"),
                        GetInt32(declared, "Bitmap ID"),
                        out Entry? entry)
                    && entry is not null)
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

    private static int GetInt32(DeclaredTask task, string parameter) =>
        task.TryGetValue(parameter, out object? value) && value is int number ? number : -1;

    /// <summary>One published binding: a grid over one octree node.</summary>
    /// <param name="Level">The node's depth, which is the task's <c>Level</c>.</param>
    /// <param name="BitmapId">The grid's id in <c>terrain.hmp</c>.</param>
    /// <param name="Centre">The node's centre in engine units.</param>
    /// <param name="Binding">The grid, placed and scaled.</param>
    public sealed record Entry(int Level, int BitmapId, Vector3d Centre, TerrainHeightBinding Binding)
    {
        /// <summary>Gets the node's half-width in engine units.</summary>
        public long Extent => TerrainOctree.RootExtent >> Level;

        /// <summary>Returns whether the node's cube contains a point.</summary>
        /// <param name="point">The point in engine units.</param>
        /// <returns><see langword="true"/> when inside on all three axes.</returns>
        /// <remarks>
        /// A node is a cube, so Z counts: the task is registered in the node its snapped position falls
        /// in, and the descent only enters that node for chunks at its height.
        /// </remarks>
        public bool Contains(Vector3d point)
        {
            long extent = Extent;
            return point.X >= Centre.X - extent && point.X < Centre.X + extent
                && point.Y >= Centre.Y - extent && point.Y < Centre.Y + extent
                && point.Z >= Centre.Z - extent && point.Z < Centre.Z + extent;
        }
    }
}
