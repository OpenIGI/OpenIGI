using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;

namespace OpenIGI.Rendering;

/// <summary>The renderer's console variables for the geometry it draws: terrain detail and caching, culling and visibility queries.</summary>
/// <remarks>One of the renderer's variable containers; <see cref="FrameCVars"/> says how they are reached.</remarks>
[CVarContainer]
public static class GeometryCVars
{
    /// <summary>Whether the world pass culls back faces of eligible meshes, as the original culls every model.</summary>
    /// <remarks>
    /// The mesh builder turns each model's stored triangle order over into this renderer's front, the
    /// facing the original culls to (CCW in every look: Direct3DRender_SetLitState 0x49D480 and
    /// Direct3DRender_SetUnlitState 0x49D810). On in every preset: a thin surface such as a roller door
    /// is two faces back to back on one plane, and drawn two-sided the pair fights for depth and flickers
    /// as the eye moves. Interior sectors and meshes without usable normals stay two-sided: in particular,
    /// IGI2 lightmapped geometry carries no normals. Not archived, so a value saved by an older build
    /// cannot turn it off again; off is for comparing in the console only.
    /// </remarks>
    public static readonly NumericCVar<int> WorldCull = Presets.Define(
        CVar.Int(
            "r_world_cull",
            1,
            0,
            1,
            CVarOptions.None,
            "Cull back faces of normal-bearing world meshes, as the original does; interior sectors remain two-sided."),
        vanilla: "1",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>How far the terrain keeps subdividing before it settles for a coarser mesh.</summary>
    /// <remarks>
    /// <para>
    /// Not an optimisation that can be switched off. The terrain <em>is</em> an octree, so there is no
    /// underlying full-detail mesh to stop simplifying — this is the depth the descent runs to, and
    /// removing the limit would mean subdividing a cube 262 km on a side.
    /// </para>
    /// <para>
    /// Retail runs 96, 129 or 163 for low, medium and high, and 163 is the shipped default
    /// (<c>sub_406390</c>). The remaster's tiers go well past it, which is what actually removes the
    /// popping as ground resolves ahead of the player.
    /// </para>
    /// <para>
    /// <b>Zero means "as retail derives it"</b>, and is not the same as writing 163: the renderer
    /// takes 163 as a <em>base</em> and divides it by the square root of the horizontal half-angle
    /// tangent, so at anything but a 4:3 window the two differ. Vanilla therefore asks for zero, not
    /// for a number that only looks like the default.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<int> TerrainDetail = Presets.Define(
        CVar.Int(
            "r_terrain_detail",
            0,
            0,
            600,
            CVarOptions.Archive,
            "How far terrain keeps subdividing; 0 derives it as retail does. Higher resolves ground further out."),
        vanilla: "0",
        enhanced: "220",
        high: "320",
        ultra: "450");

    /// <summary>How many uploaded terrain chunks the renderer keeps before evicting the oldest.</summary>
    /// <remarks>
    /// <para>
    /// Paired with <see cref="TerrainDetail"/>, because how far the ground resolves is what decides how
    /// much of it there is to hold. The number that matters is not what one frustum holds but what a
    /// PANORAMA holds: a frustum is a quarter or so of the circle a player turns through, so a cache
    /// sized to the view drops the ground behind them and rebuilds it as they turn back. Measured on
    /// level 1 at the ultra distance, a frame draws about 2500 chunks and a full turn from one spot
    /// touches about 9000; at 4096 that is a hundred chunks rebuilt every frame for as long as the
    /// player keeps turning, and at 16384 or more the turn evicts nothing at all and settles at zero.
    /// </para>
    /// <para>
    /// A whole turn from one spot on level 1 touches 2363 chunks at 220, 4681 at 320 and 8979 at 450,
    /// costing 6.5, 12.5 and 23.0 MB of page buffers. Each tier is set to between two and three times
    /// its own figure, so
    /// that a player who walks while they turn keeps the headroom the fixed-spot measurement does not
    /// show. Vanilla keeps retail's own 4096 — it also draws retail's much shorter view, which fits.
    /// </para>
    /// <para>
    /// The cost of raising it is the geometry held, and that is only affordable because a batch page is
    /// now created with room for what a page actually holds rather than for 4096 vertices apiece: the
    /// same ultra panorama that would have charged 918 MB of buffers charges 23 MB.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<int> TerrainCache = Presets.Define(
        CVar.Int(
            "r_terrain_cache",
            TerrainCacheDefault,
            TerrainCacheDefault,
            65536,
            CVarOptions.Archive,
            "How many terrain chunks stay uploaded. Sized to a whole turn, so turning back rebuilds nothing."),
        vanilla: "4096",
        enhanced: "8192",
        high: "12288",
        ultra: "24576");

    /// <summary>How finely I.G.I.'s nearest ground is refined onto a smooth surface, or 0 to draw it as stored.</summary>
    /// <remarks>
    /// <para>
    /// The near ground is drawn as four-, eight- and sixteen-metre grids of flat triangles, and the
    /// heightmap lands on their corners only, so it reads as facets and lone spikes. One cuts each edge
    /// in two, two in four. The new points sit on a smooth surface through the retail ground, laid on
    /// each level's own grid, and are shaded with that surface's own normal.
    /// </para>
    /// <para>
    /// The three finest levels of detail are refined, which carries it about 125 metres from the camera
    /// at Enhanced and nearly 300 at Ultra. Each chunk is refined once when it is built and then cached,
    /// so a frame pays only the extra triangles. Each level eases onto the next one's smoothed ground as
    /// it recedes, so the levels still meet. Visual only: gameplay stands on the retail triangles, and
    /// the drawn ground underfoot never strays from them by more than <see cref="TerrainSmoothLimit"/>.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<int> TerrainSmooth = Presets.Define(
        CVar.Int(
            "r_terrain_smooth",
            2,
            0,
            2,
            CVarOptions.Archive,
            "Smooth the nearest ground: 0 draws it as stored, 1 a two-metre grid, 2 a one-metre grid. Visual only."),

        // Every preset, Vanilla too: it is the original's ground with its facets rounded, measured at a
        // frame or two at Ultra, and collision is untouched. Zero is still there for the stored triangles.
        vanilla: "2",
        enhanced: "2",
        high: "2",
        ultra: "2");

    /// <summary>How far the smoothed ground rounds off what it passes through, from 0 to 1.</summary>
    /// <remarks>
    /// Zero passes exactly through every retail ground height on the four-metre grid, so a foot planted
    /// on a grid point is exactly where it was. One is a B-spline that only passes near them, which
    /// also takes the point off a lone spike, at the cost of straying further from the ground the
    /// player stands on. <see cref="TerrainSmoothLimit"/> bounds either way.
    /// </remarks>
    public static readonly NumericCVar<float> TerrainSmoothRelax = Presets.Define(
        CVar.Float(
            "r_terrain_smooth_relax",
            0f,
            0f,
            1f,
            CVarOptions.Archive,
            "How much the smoothed ground rounds off spikes: 0 passes through every ground height, 1 rounds them off."),
        vanilla: "0",
        enhanced: "0",
        high: "0",
        ultra: "0");

    /// <summary>The furthest the smoothed ground may stand from the retail ground, in metres.</summary>
    /// <remarks>
    /// This is the finest level's, the ground underfoot. Each coarser level's is twice the one before,
    /// because its triangles are twice the size and nobody stands on it at that detail. The offset eases
    /// into the limit rather than stopping at it, and never passes it.
    /// </remarks>
    public static readonly NumericCVar<float> TerrainSmoothLimit = Presets.Define(
        CVar.Float(
            "r_terrain_smooth_limit",
            0.3f,
            0f,
            2f,
            CVarOptions.Archive,
            "The furthest the smoothed ground may move from the ground the player stands on, in metres."),
        vanilla: "0.3",
        enhanced: "0.3",
        high: "0.3",
        ultra: "0.3");

    /// <summary>Retail's own chunk bound, which is the smallest this cache is allowed to be.</summary>
    private const int TerrainCacheDefault = 4096;

    /// <summary>Whether terrain batch pages use asynchronous visibility queries.</summary>
    /// <remarks>
    /// The query result is consumed only after OpenGL reports it ready, so this never stalls the CPU.
    /// Keeping this as a CVar also makes the query cost directly A/B testable on a given mission.
    /// </remarks>
    public static readonly BoolCVar TerrainOcclusion = Presets.Define(
        CVar.Bool(
            "r_terrain_occlusion",
            false,
            CVarOptions.Archive,
            "Cull fully hidden terrain batch pages with asynchronous GPU visibility queries. Opt-in because sparse scenes can cost more to query than they save."),
        vanilla: "0",
        enhanced: "0",
        high: "0",
        ultra: "1");

    /// <summary>Whether the original's ground is drawn a texture and a pass at a time, as the original draws it.</summary>
    /// <remarks>
    /// The direct path's ground: a draw per texture per pass over an arena of chunks (<see cref="ITerrainArenaDevice"/>) rather
    /// than one per chunk per layer -- a few dozen draws for thousands. Off draws it chunk by chunk, for comparing the two.
    /// Not archived: it is a diagnostic, not a preference.
    /// </remarks>
    public static readonly BoolCVar TerrainArena = CVar.Bool(
        "r_terrain_arena",
        true,
        CVarOptions.None,
        "Draw the original's ground a texture and a pass at a time, as the original does. Off draws it chunk by chunk.");

    /// <summary>Whether placed world objects use asynchronous visibility queries in the geometry pass.</summary>
    /// <remarks>
    /// The frustum keeps most of a level's objects while the camera stands indoors — a wall hides
    /// them, a frustum does not. On by default because interiors are where the geometry pass was
    /// measured most over budget; a CVar so the gain is A/B testable per mission like the terrain's.
    /// </remarks>
    public static readonly BoolCVar WorldOcclusion = Presets.Define(
        CVar.Bool(
            "r_world_occlusion",
            true,
            CVarOptions.Archive,
            "Cull placed objects hidden behind walls and hills with asynchronous GPU visibility queries."),
        vanilla: "1",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>The registry the renderer's own settings describe themselves to.</summary>
    private static GraphicsPresetRegistry Presets => GraphicsPresetRegistry.Default;
}
