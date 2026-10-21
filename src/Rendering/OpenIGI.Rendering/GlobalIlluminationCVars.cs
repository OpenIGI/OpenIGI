using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;

namespace OpenIGI.Rendering;

/// <summary>The renderer's console variables for baked indirect light: the probe field and the level's lightmaps.</summary>
/// <remarks>One of the renderer's variable containers; <see cref="FrameCVars"/> says how they are reached.</remarks>
[CVarContainer]
public static class GlobalIlluminationCVars
{
    /// <summary>Whether surfaces take their indirect light from the baked probe field.</summary>
    /// <remarks>
    /// Costs a trace of the level at load rather than anything per frame, which is why it is a
    /// restart-scope setting in practice even though nothing enforces that: turning it on mid-level
    /// finds no field baked and changes nothing until the next load.
    /// </remarks>
    public static readonly BoolCVar GlobalIllumination = Presets.Define(
        CVar.Bool(
            "r_gi",
            false,
            CVarOptions.Archive,
            "Light surfaces from the level's baked probe field instead of a flat ambient term."),
        vanilla: "0",
        enhanced: "0",
        high: "0",

        // TEMPORARILY off at every real-time preset, so nothing about them is traced at load and every bit
        // of their indirect comes from the real-time path -- the surface cache, the screen gather and the
        // radiance cache -- with nothing static underneath it. What that costs is the floor this field used
        // to provide: it is a coarse, visibility-aware far field, and the deferred fill takes the max of the
        // two, so a surface the screen probes cannot resolve (a merlon or a wire thinner than the probe
        // spacing, a wall the screen never saw) falls back to the flat ambient term rather than to a traced
        // estimate.
        //
        // This used to be on at high, which was the preset with no surface cache to gather from. High now
        // gathers like the others, so the question is no longer "which presets have a real-time path" but
        // "should the real-time path have a static floor at all" -- and the answer applies to all three
        // equally. It is a live question, not a settled one: restoring the floor costs a load-time trace on
        // every preset rather than one.
        ultra: "0");

    /// <summary>How the level's static object lightmaps are sourced: shipped, augmented, or our bake alone.</summary>
    /// <remarks>
    /// <para>
    /// <b>0</b> uses the shipped <c>.olm</c> object lightmaps, exactly as retail. <b>1</b> re-bakes them with
    /// the path tracer at load -- per-texel, coloured, multi-bounce, from the level's own sun and sky -- and
    /// swaps a baked page in wherever there is one, falling back to the shipped page where the bake produced
    /// nothing. <b>2</b> discards the shipped lightmaps entirely: a surface the bake did not reach reads
    /// black rather than borrowing the artists' light, so what the frame shows is ONLY our own lighting --
    /// the honest state of the system, gaps and all.
    /// </para>
    /// <para>
    /// A restart-scope setting: the bake runs once at load (on a worker, cached to disk) and only ever
    /// reaches the baked and hybrid models, which sample the lightmap; vanilla and the dynamic path are
    /// untouched. Interiors a sealed room's sun cannot reach stay dark until the lamp fixtures are baked.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<int> BakedLightmaps = Presets.Define(
        CVar.Int(
            "r_lightmap",
            0,
            0,
            2,
            CVarOptions.Archive,
            "Object lightmaps: 0 shipped, 1 path-traced bake over the shipped ones, 2 path-traced bake only."),
        vanilla: "0",
        enhanced: "0",
        high: "0",

        // Off everywhere, and on the three real-time presets it would be doing nothing anyway: they light
        // with r_lighting `dynamic`, which does not sample an object lightmap at all -- a frame drawn with
        // the shipped pages and one drawn with every page forced black come out byte-identical there. So the
        // forty seconds this spent path-tracing every level at load, across every core, holding a few
        // hundred megabytes of texel maps while it ran, would produce pages nothing ever read.
        //
        // It was 2 at ultra, which discarded the shipped pages and showed our bake alone. It earns that back
        // the moment a preset samples a lightmap again -- or on any preset set to `baked` or `hybrid` by
        // hand, where 1 and 2 work exactly as they always did.
        ultra: "0");

    /// <summary>How far apart the probes sit, in metres.</summary>
    public static readonly NumericCVar<float> GlobalIlluminationSpacing = Presets.Define(
        CVar.Float(
            "r_gi_probe_spacing",
            6f,
            1f,
            32f,
            CVarOptions.Archive,
            "Metres between irradiance probes. Finer resolves smaller rooms and takes longer to bake."),
        vanilla: "6",
        enhanced: "3",
        high: "3",
        ultra: "3");

    /// <summary>How strongly the probe field's light is applied.</summary>
    public static readonly NumericCVar<float> GlobalIlluminationIntensity = Presets.Define(
        CVar.Float(
            "r_gi_intensity",
            1f,
            0f,
            4f,
            CVarOptions.Archive,
            "How strongly the probe field lights surfaces; 1 is the amount the bake computed."),
        vanilla: "1",
        enhanced: "4",
        high: "4",
        ultra: "4");

    /// <summary>The registry the renderer's own settings describe themselves to.</summary>
    private static GraphicsPresetRegistry Presets => GraphicsPresetRegistry.Default;
}
