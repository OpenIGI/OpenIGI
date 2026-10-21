using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;

namespace OpenIGI.Rendering;

/// <summary>The renderer's console variables for tracing: the hardware ray query, what it traces, the traced world and the distance field.</summary>
/// <remarks>One of the renderer's variable containers; <see cref="FrameCVars"/> says how they are reached.</remarks>
[CVarContainer]
public static class RayTracingCVars
{
    /// <summary>Whether tracing uses the hardware ray query rather than the software distance field.</summary>
    /// <remarks>
    /// <para>
    /// The switch between the two GI modes, live: on, every trace that can reach the acceleration
    /// structure does — the screen gather, the radiance cache, the surface-cache relight and the sun's
    /// shadow — and off, the same shaders march the signed distance field and the sun falls back to the
    /// cascaded map. It only means anything on a backend that can trace at all; elsewhere the software
    /// path is the only path and the flag is ignored. Bound to F6 by default so the two can be compared
    /// in place.
    /// </para>
    /// <para>
    /// <b>Deliberately NOT archived, and that is a fix rather than an oversight.</b> Every preset names
    /// it 1 — there is no tier that wants the software path, only a comparison that does — so the only
    /// way it is ever 0 is that somebody pressed the key. While it was archived, pressing that key wrote
    /// itself into <c>settings.cfg</c>: the next launch came up on the software path, the preset read as
    /// Custom because a managed value no longer matched, and the picture was quietly worse in a way
    /// nothing on screen explained. A key for flipping between two implementations must not be able to
    /// change what the game starts as. It is still named by the presets, so the settings screen still
    /// reports Custom while it is flipped and still puts it back when a preset is chosen.
    /// </para>
    /// </remarks>
    public static readonly BoolCVar HardwareRayTracing = Presets.Define(
        CVar.Bool(
            "r_rt",
            true,
            CVarOptions.None,
            "Trace with the hardware ray query (1) or the software distance field (0). Toggle live to compare."),
        vanilla: "1",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>Whether the sun's shadow is traced, on a backend already tracing.</summary>
    /// <remarks>
    /// The second half of the live switch. <see cref="HardwareRayTracing"/> moves every trace in the
    /// frame between the hardware ray query and the software distance field at once; this one moves the
    /// sun's shadow alone, so the ray-traced shadow can be compared against the cascaded map while the
    /// indirect light, the reflections and the surface-cache relight all stay exactly as they were. That
    /// is the comparison worth being able to make in place: the two shadow systems draw the same
    /// geometry from the same sun and should agree about where a shadow falls, and only about how it
    /// looks at its edge.
    /// <para>
    /// It does nothing where <see cref="HardwareRayTracing"/> is off, which already leaves the sun to
    /// the cascades, and nothing on a backend that cannot trace at all.
    /// </para>
    /// <para>
    /// Not archived, for the same reason as <see cref="HardwareRayTracing"/>: this exists to be flipped
    /// while looking at something, and a key that is flipped while looking at something must not decide
    /// what the game starts as tomorrow.
    /// </para>
    /// </remarks>
    public static readonly BoolCVar TracedSunShadows = Presets.Define(
        CVar.Bool(
            "r_rt_shadows",
            true,
            CVarOptions.None,
            "Trace the sun's shadow rather than reading the cascaded map. Needs r_rt; toggle live to compare."),
        vanilla: "1",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>Whether the lamps' shadow is traced against the world, on a backend already tracing.</summary>
    /// <remarks>
    /// The lights' own switch, beside the sun's. It traces a ray per lamp against the world so a lamp
    /// cannot light through a wall the camera cannot see -- the off-screen occluder the screen-space
    /// march can never find. Off, the lamps keep the screen-space march alone, which still stops the
    /// walls the view contains; so this is the difference between a lamp shadowed everywhere and one
    /// shadowed only where its wall is on screen. Needs <see cref="HardwareRayTracing"/> and does
    /// nothing without it; toggle live to weigh its cost against the leak it closes.
    /// </remarks>
    public static readonly BoolCVar TracedLampShadows = Presets.Define(
        CVar.Bool(
            "r_rt_lights",
            true,
            CVarOptions.None,
            "Trace the lamps' shadow so they cannot light through off-screen walls. Needs r_rt; toggle live."),
        vanilla: "1",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>Whether reflections are traced against the world, on a backend already tracing.</summary>
    /// <remarks>
    /// <para>
    /// The reflections' own switch. With it on, a reflected ray the screen cannot answer is traced against
    /// the world and its hit resolved -- out of the lit frame where the camera can see the hit, and out of
    /// the surface cache's lit cards where it cannot. So a puddle or a pane shows what stands off-screen,
    /// or behind something nearer, or simply never drawn, none of which a screen-space march can reach.
    /// </para>
    /// <para>
    /// It matters most on the surfaces that have no fallback. Standing water is given the sky by the
    /// shading whether or not a ray lands, so the trace fills in a wall there rather than filling in
    /// nothing; a metal or a glossy surface is given no reflection term at all, so a marched ray that
    /// missed left it mirroring the void. That is the case this exists for.
    /// </para>
    /// <para>
    /// <b>On, the trace is the whole of the reflection and the screen-space march does not run.</b> Not a
    /// hybrid: a marched hit is the first surface the depth buffer happens to record along the ray, which
    /// is the right surface only when the camera drew it, so mixing the two gives a picture that is part
    /// exact and part plausible with the seam falling wherever the march happened to succeed. Off — or on
    /// an adapter that cannot trace, or while a mission's world and cards are still being built — the
    /// march is what answers instead, which is the approximation for the machines and the moments that
    /// cannot have the real thing. Toggle live to compare.
    /// </para>
    /// </remarks>
    public static readonly BoolCVar TracedReflections = Presets.Define(
        CVar.Bool(
            "r_rt_reflections",
            true,
            CVarOptions.None,
            "Trace reflections against the world for what the screen cannot show. Needs r_rt; toggle live."),
        vanilla: "1",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>Whether the level's signed distance fields are baked and resampled into clipmaps.</summary>
    /// <remarks>
    /// <para>
    /// The structure everything in the realtime GI track traces through. A ray marched against
    /// triangles tests every triangle it might cross; a ray marched against a distance field asks how
    /// far the nearest surface is and steps exactly that far, which cannot overshoot.
    /// </para>
    /// <para>
    /// Needs compute, so it is off wherever the context cannot honour it whatever this says. Off at
    /// every preset until the stages that consume it exist — a field nothing traces is a load-time cost
    /// with nothing to show for it.
    /// </para>
    /// </remarks>
    public static readonly BoolCVar DistanceFields = Presets.Define(
        CVar.Bool(
            "r_sdf",
            false,
            CVarOptions.Archive,
            "Bake the level's signed distance fields and keep clipmaps of them around the camera."),
        vanilla: "0",
        enhanced: "1",
        high: "1",

        // The screen probe gather traces the distance fields for the rays that leave the screen, so every
        // preset bakes them for the same reason it captures the cards -- and needs both, since a gather with
        // one and not the other returns nothing. Only vanilla needs neither.
        ultra: "1");

    /// <summary>How many voxels the longest axis of each model's distance field gets.</summary>
    /// <remarks>
    /// The cost of a bake is roughly this cubed per distinct model, and the memory exactly so — which is
    /// why it is a tier ladder rather than a constant, and why the cheapest tier keeps the floor.
    /// Thirty-two already gives this game's crates and huts voxels of a few centimetres, fine enough for a
    /// trace to land on the right side of a wall; what the higher tiers buy is thin geometry a coarse field
    /// rounds away. Paid at load and in video memory, never per frame.
    /// </remarks>
    public static readonly NumericCVar<int> DistanceFieldResolution = Presets.Define(
        CVar.Int(
            "r_sdf_resolution",
            32,
            8,
            128,
            CVarOptions.Archive,
            "Voxels along the longest axis of each model's distance field."),
        vanilla: "32",
        enhanced: "32",
        high: "40",
        ultra: "46");

    /// <summary>Whether the built clipmaps are checked against the exact field once, after a load.</summary>
    /// <remarks>
    /// Reads the clipmap back and compares every voxel against <c>SceneDistanceField</c>, which answers
    /// the same question exactly. Costs a synchronising readback and a few hundred thousand CPU
    /// evaluations, so it is a diagnostic rather than something to leave on — but a wrong distance field
    /// is invisible in a rendered frame, and this is the only thing that can see it.
    /// </remarks>
    public static readonly BoolCVar DistanceFieldValidate = CVar.Bool(
        "r_sdf_validate",
        false,
        CVarOptions.Developer,
        "After a load, compare the built clipmap against the exact field and report the difference.");

    /// <summary>How far from the camera a placement is still put into the traced world.</summary>
    /// <remarks>
    /// <para>
    /// <b>Drawn by visibility, traced by distance.</b> The acceleration structure used to be filled from
    /// the draw path, so it held exactly what the camera had at some point had in frame — and a window
    /// reflects what is BEHIND the camera, which is precisely what the frustum throws away. A pane could
    /// therefore only show a place the player had already stood and looked at, and showed more of the
    /// level the longer they played. These two settings replace the frustum with a distance-and-size
    /// rule: keep a placement unless it is both too far off and too small to matter.
    /// </para>
    /// <para>
    /// Zero switches the whole mechanism off, leaving the structure to be fed by drawing alone. That is a
    /// diagnostic, not a setting: it restores the defect.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> TracedWorldRadius = Presets.Define(
        CVar.Float(
            "r_rt_world_radius",
            300f,
            0f,
            4000f,
            CVarOptions.Archive,
            "How far behind or beside the camera geometry still enters the traced world, in metres."),
        vanilla: "0",
        enhanced: "150",
        high: "300",
        ultra: "300");

    /// <summary>The projected size below which a distant placement stops being worth tracing.</summary>
    /// <remarks>
    /// The companion to <see cref="TracedWorldRadius"/>, and what keeps that radius from being a hard
    /// wall. A placement is dropped only when it is BOTH beyond the radius and smaller than this angle,
    /// so a hillside or a hangar stays in the structure at any distance while a crate three hundred
    /// metres back does not. One degree keeps anything a reflection could plausibly resolve.
    /// </remarks>
    public static readonly NumericCVar<float> TracedWorldAngle = Presets.Define(
        CVar.Float(
            "r_rt_world_angle",
            1f,
            0.01f,
            45f,
            CVarOptions.Archive,
            "Projected angle, in degrees, under which distant geometry is left out of the traced world."),
        vanilla: "1",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>The registry the renderer's own settings describe themselves to.</summary>
    private static GraphicsPresetRegistry Presets => GraphicsPresetRegistry.Default;
}
