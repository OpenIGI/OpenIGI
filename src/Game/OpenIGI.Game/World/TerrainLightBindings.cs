using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>
/// The terrain lightmap bindings a level's <c>TerrainLightMap</c> tasks publish: which <c>terrain.lmp</c>
/// page lights the ground inside which octree cell, and the colour ramp its 8-bit texels are read through.
/// </summary>
/// <remarks>
/// <para>
/// The ground's light. The original draws every chunk's material passes and then this page over them as a
/// last pass that multiplies the frame (<c>ZERO, SRCCOLOR</c>, <c>sub_5144E0</c> mode 2); a chunk with no
/// binding is drawn unlit, its tiles at their own colour. Without it the vanilla ground is exactly that:
/// the tile pictures with no sun on them, a cool grey where the original shows warm sunlit dirt.
/// </para>
/// <para>
/// <b>Binding.</b> <c>sub_4F8780</c> activates each task once at load: the cell is the octree node of its
/// <c>Level</c>, <c>2^(31 - Level)</c> units across, the position snapped down to it; the page is
/// <c>TextureIndex</c> into <c>terrain.lmp</c>; <c>Size</c> must be 8 to 256. <c>sub_4F8EF0</c> publishes a
/// 48-byte entry at <c>0xBA80A0</c> (the cell's minimum X and Y, <c>2^(L - 23)</c> and <c>128 / Size</c>),
/// and the descent pushes its slot on entering the cell's node (class slot 15, <c>sub_4F82B0</c>) and pops
/// it on leaving (slot 16), so a chunk takes the innermost cell enclosing its own node
/// (<c>0x4D2226</c>). A chunk coarser than every cell over it is not enclosed by any, and stays unlit.
/// </para>
/// <para>
/// <b>Coordinates.</b> <c>sub_510010</c>, per vertex (<c>0x510200</c>): <c>u = (X - minX) / cell + 0.5 / Size</c>
/// and the same in Y, so texel (c, r) is centred on <c>min + (c, r) * cell / Size</c>, row-major, unflipped.
/// </para>
/// <para>
/// <b>Colour.</b> The texels are indices into a ramp <c>sub_50FAE0</c> rebuilds from the level's light
/// whenever it changes: <c>min(255, sun * i + ambient * 255)</c> per channel, the sun the first
/// <c>Dirlight</c>'s keyframe colour and the ambient the <c>GlobalLight</c>'s terrain band; alpha 255 but
/// for index zero (<c>sub_517FD0</c>). A level with no <c>Dirlight</c> keeps the load-time placeholder,
/// <c>i &lt;&lt; 16</c>: red alone.
/// </para>
/// <para>
/// <b>Divergence, recorded.</b> The original drops each task onto the ground by ray before snapping its
/// height, and adds proxy copies one cell above or below where the ground spans more than one; a chunk
/// is therefore covered at whatever height it stands. The port matches the cell in X and Y alone, which
/// is the same answer for every retail level: no two tasks share a column of cells at one level.
/// </para>
/// </remarks>
public sealed class TerrainLightBindings
{
    /// <summary>The task type that publishes a binding.</summary>
    public const string TaskType = "TerrainLightMap";

    private static readonly ConditionalWeakTable<TerrainData, TerrainLightBindings> Bindings = new();
    private static readonly TerrainLightBindings Empty = new([], 0, PlaceholderRamp());

    private readonly Entry[] _entries;

    private TerrainLightBindings(Entry[] entries, int skipped, Bgra32[] ramp)
    {
        _entries = entries;
        Entries = new ReadOnlyCollection<Entry>(entries);
        Skipped = skipped;
        Ramp = ramp;
    }

    /// <summary>Gets the bindings, innermost (deepest level) first.</summary>
    public IReadOnlyList<Entry> Entries { get; }

    /// <summary>Gets how many tasks named a page the level does not carry, or an illegal size.</summary>
    public int Skipped { get; }

    /// <summary>Gets the 256 colours a page's texels index, as the level's light makes them.</summary>
    public IReadOnlyList<Bgra32> Ramp { get; }

    /// <summary>Gets a value indicating whether any binding exists.</summary>
    public bool IsEmpty => _entries.Length == 0;

    /// <summary>Walks a level's task tree for its <c>TerrainLightMap</c> tasks and remembers the result.</summary>
    /// <param name="terrain">The loaded terrain, whose <see cref="TerrainData.Lightmaps"/> supply the pages.</param>
    /// <param name="levelRoot">The task the level's subtree hangs off.</param>
    /// <returns>The bindings; empty when the level ships no <c>terrain.lmp</c> or declares no task.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static TerrainLightBindings Bind(TerrainData terrain, GameTask levelRoot)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(levelRoot);

        TerrainLightBindings bindings = Build(terrain, levelRoot);
        Bindings.Remove(terrain);
        Bindings.Add(terrain, bindings);
        return bindings;
    }

    /// <summary>Gets the bindings bound to a terrain, or none.</summary>
    /// <param name="terrain">The terrain.</param>
    /// <returns>What <see cref="Bind"/> recorded, or an empty set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="terrain"/> is <see langword="null"/>.</exception>
    public static TerrainLightBindings For(TerrainData terrain)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        return Bindings.TryGetValue(terrain, out TerrainLightBindings? bindings) ? bindings : Empty;
    }

    /// <summary>Builds the bindings without remembering them.</summary>
    /// <param name="terrain">The loaded terrain.</param>
    /// <param name="levelRoot">The task the level's subtree hangs off.</param>
    /// <returns>The bindings.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static TerrainLightBindings Build(TerrainData terrain, GameTask levelRoot)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(levelRoot);

        List<Entry> entries = [];
        int skipped = 0;
        TerrainLightmaps? pages = terrain.Lightmaps;

        if (pages is not null)
        {
            foreach (GameTask task in levelRoot.Descendants())
            {
                if (task is not DeclaredTask declared
                    || !string.Equals(declared.Type.Name, TaskType, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!WorldScene.TryGetPosition(declared, out Vector3d metres)
                    || !TryCreate(
                        pages,
                        new Vector3d(metres.X * WorldScene.UnitsPerMetre, metres.Y * WorldScene.UnitsPerMetre, 0d),
                        GetInt32(declared, "Level"),
                        GetInt32(declared, "Size"),
                        GetInt32(declared, "TextureIndex"),
                        out Entry? entry)
                    || entry is null)
                {
                    skipped++;
                    continue;
                }

                entries.Add(entry);
            }
        }

        // Innermost first, as the descent's stack has it: the most recently entered cell is the deepest
        // one enclosing the chunk. A stable sort keeps tree order within a level.
        entries = [.. entries.OrderByDescending(e => e.Level)];
        return new TerrainLightBindings([.. entries], skipped, RampFor(levelRoot));
    }

    /// <summary>The ramp <c>sub_50FAE0</c> builds: each index lit by the sun over the terrain's ambient.</summary>
    /// <param name="sun">The first <c>Dirlight</c>'s keyframe colour, each channel 0 to 1.</param>
    /// <param name="ambient">The <c>GlobalLight</c>'s terrain ambient, each channel 0 to 1.</param>
    /// <returns>The 256 colours.</returns>
    public static Bgra32[] RampFrom(Vector3f sun, Vector3f ambient)
    {
        var ramp = new Bgra32[256];
        for (int i = 0; i < ramp.Length; i++)
        {
            ramp[i] = new Bgra32(
                Channel(sun.Z, ambient.Z, i),
                Channel(sun.Y, ambient.Y, i),
                Channel(sun.X, ambient.X, i),
                i == 0 ? (byte)0 : (byte)255);
        }

        return ramp;

        // The original truncates the sum to an int and clamps it at 255; a negative one cannot occur with
        // the colours a level can author, and is clamped at zero rather than wrapped.
        static byte Channel(float sun, float ambient, int index) =>
            (byte)Math.Clamp((int)((sun * index) + (ambient * 255f)), 0, 255);
    }

    /// <summary>Finds the binding a chunk takes: the innermost cell enclosing its node.</summary>
    /// <param name="chunk">The chunk.</param>
    /// <returns>The binding, or an unbound default when no cell encloses it.</returns>
    public TerrainLightBinding Resolve(in TerrainChunk chunk)
    {
        foreach (Entry entry in _entries)
        {
            // A cell encloses a node at its own depth or deeper; a coarser chunk spans cells it is not in.
            if (chunk.LodExponent >= entry.Level && entry.Contains(chunk.Position))
            {
                return entry.Binding;
            }
        }

        return default;
    }

    private static Bgra32[] RampFor(GameTask levelRoot)
    {
        if (!DirectionalLightSettings.TryRead(levelRoot, out DirectionalLightSettings sun))
        {
            return PlaceholderRamp();
        }

        return RampFrom(sun.Colour, TerrainAmbient(levelRoot));
    }

    /// <summary>Gets the terrain band's ambient: the floor of the ground's light ramp, each channel 0 to 1.</summary>
    /// <param name="levelRoot">The loaded level's task tree.</param>
    /// <returns>The ambient, black where the level declares no keyframe.</returns>
    /// <remarks>
    /// What <c>sub_50FE40</c> publishes to <c>0xA7DA20</c> and <c>sub_50FAE0</c> adds under the sun when it builds the ramp: the
    /// <c>Ambient color terrain</c> of the <c>GlobalLightKeyframe</c> the level opens on, the one with the lowest time.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="levelRoot"/> is <see langword="null"/>.</exception>
    public static Vector3f TerrainAmbient(GameTask levelRoot)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        GameTask? opening = null;
        float lowest = float.MaxValue;
        foreach (GameTask task in levelRoot.Descendants())
        {
            if (string.Equals(task.Type.Name, "GlobalLightKeyframe", StringComparison.Ordinal)
                && task.TryGetScriptValue("Time", out object? time)
                && time is float at
                && at < lowest)
            {
                (opening, lowest) = (task, at);
            }
        }

        return opening is not null && opening.TryGetScriptValue("Ambient color terrain", out object? colour) && colour is Vector3f ambient
            ? ambient
            : default;
    }

    // What the pages are uploaded with before the level's light is known: i << 16, red alone.
    private static Bgra32[] PlaceholderRamp()
    {
        var ramp = new Bgra32[256];
        for (int i = 0; i < ramp.Length; i++)
        {
            ramp[i] = new Bgra32(0, 0, (byte)i, i == 0 ? (byte)0 : (byte)255);
        }

        return ramp;
    }

    private static bool TryCreate(
        TerrainLightmaps pages,
        Vector3d position,
        int level,
        int size,
        int page,
        out Entry? entry)
    {
        entry = null;

        // sub_4F8780 rejects a size outside 8..256 ("Illegal shift size") and binds nothing without a page.
        if (level is < 0 or > 30
            || size is < 8 or > 256
            || !int.IsPow2(size)
            || page < 0
            || page >= pages.Pages.Count
            || pages.Pages[page].IsEmpty)
        {
            return false;
        }

        // The cell is the node of the task's level: its position masked down to a multiple of the span.
        long span = 2 * (TerrainOctree.RootExtent >> level);
        long minX = (long)position.X & -span;
        long minY = (long)position.Y & -span;

        entry = new Entry(
            level,
            page,
            new TerrainLightBinding(page, minX, minY, 1.0 / span, 0.5 / size));
        return true;
    }

    private static int GetInt32(DeclaredTask task, string parameter) =>
        task.TryGetValue(parameter, out object? value) && value is int number ? number : -1;

    /// <summary>One published binding: a page over one octree cell.</summary>
    /// <param name="Level">The cell's depth, which is the task's <c>Level</c>.</param>
    /// <param name="Page">The page's index in <c>terrain.lmp</c>.</param>
    /// <param name="Binding">The page, placed and scaled.</param>
    public sealed record Entry(int Level, int Page, TerrainLightBinding Binding)
    {
        /// <summary>Gets the cell's width in engine units.</summary>
        public long Span => 2 * (TerrainOctree.RootExtent >> Level);

        /// <summary>Returns whether the cell's column contains a point.</summary>
        /// <param name="point">The point in engine units.</param>
        /// <returns><see langword="true"/> when inside in X and Y.</returns>
        public bool Contains(Vector3d point) =>
            point.X >= Binding.MinX && point.X < Binding.MinX + Span
            && point.Y >= Binding.MinY && point.Y < Binding.MinY + Span;
    }
}

/// <summary>A terrain lightmap page placed over the ground: how a vertex finds its texel.</summary>
/// <param name="Page">The page's index in <c>terrain.lmp</c>, or -1 for none.</param>
/// <param name="MinX">The cell's minimum X in engine units.</param>
/// <param name="MinY">The cell's minimum Y in engine units.</param>
/// <param name="InverseSpan">One over the cell's width in engine units.</param>
/// <param name="HalfTexel">Half a texel of the declared size, the offset that centres texels on the cell's grid.</param>
public readonly record struct TerrainLightBinding(int Page, long MinX, long MinY, double InverseSpan, double HalfTexel)
{
    /// <summary>Gets a value indicating whether a page is bound.</summary>
    public bool IsBound => InverseSpan > 0d;

    /// <summary>The texture coordinate of a world point, as <c>sub_510010</c> computes it.</summary>
    /// <param name="x">The point's X in engine units.</param>
    /// <param name="y">The point's Y in engine units.</param>
    /// <returns>The coordinate on the page.</returns>
    public (double U, double V) Project(double x, double y) =>
        (((x - MinX) * InverseSpan) + HalfTexel, ((y - MinY) * InverseSpan) + HalfTexel);
}
