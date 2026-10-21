using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;

namespace OpenIGI.Rendering;

/// <summary>The renderer's console variables for the geometry it draws: terrain detail and caching, culling and visibility queries.</summary>
/// <remarks>One of the renderer's variable containers; <see cref="FrameCVars"/> says how they are reached.</remarks>
[CVarContainer]
public static class GeometryCVars
{
    /// <summary>Whether the world pass culls back faces of eligible meshes after repairing triangle winding.</summary>
    /// <remarks>
    /// The mesh builder repairs winding using authored vertex normals. Interior sectors and meshes
    /// without usable normals remain two-sided even when this setting is enabled: in particular,
    /// IGI2 lightmapped geometry carries no normals. Off draws both facings for every world mesh.
    /// </remarks>
    public static readonly NumericCVar<int> WorldCull = Presets.Define(
        CVar.Int(
            "r_world_cull",
            0,
            0,
            1,
            CVarOptions.Archive,
            "Cull back faces of normal-bearing world meshes; interior sectors remain two-sided."),
        vanilla: "0",
        enhanced: "0",
        high: "0",

        // The renderer retains two-sided drawing for sectors and meshes winding repair cannot orient.
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
