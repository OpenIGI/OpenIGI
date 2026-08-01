using OpenIGI.Core.Configuration;
using OpenIGI.Rendering.Presets;

namespace OpenIGI.Rendering;

/// <summary>
/// The renderer's console variables.
/// </summary>
/// <remarks>
/// <para>
/// Declared here, next to the renderer seam, rather than in a central settings type. The console, the
/// settings file and the options menu all reach these through
/// <see cref="CVarRegistry.Default"/>, so there is one value and three views onto it.
/// </para>
/// <para>
/// Each remaster stage adds its own variables to this file and, where the value differs by quality
/// tier, registers them with <see cref="Presets.GraphicsPresetRegistry"/> in the same place.
/// </para>
/// </remarks>
[CVarContainer]
public static class RenderCVars
{
    /// <summary>Whether presentation waits for the vertical blank.</summary>
    public static readonly BoolCVar VSync = CVar.Bool(
        "r_vsync",
        true,
        CVarOptions.Archive,
        "Wait for the vertical blank when presenting. Off uncaps the frame rate and adds tearing.");

    /// <summary>How the picture is scaled relative to the window before being presented.</summary>
    /// <remarks>
    /// Below one this is supersampling in reverse — the scene is rendered smaller and upscaled, which
    /// is the cheapest way to buy back frame time. Above one it is supersampling proper. The temporal
    /// upscaler added in stage R5 is what makes values below one look acceptable.
    /// </remarks>
    public static readonly NumericCVar<float> RenderScale = CVar.Float(
        "r_scale",
        1.0f,
        0.5f,
        2.0f,
        CVarOptions.Archive,
        "Render resolution as a fraction of the window: below 1 is faster, above 1 is supersampled.");

    /// <summary>Whether the scene is rendered offscreen instead of straight to the back buffer.</summary>
    /// <remarks>
    /// The switch between the original's path and the modern one. Off, the frame is drawn and
    /// presented exactly as the game shipped, and none of the remaster track's effects can run,
    /// because every one of them needs the frame and its depth to still exist after being drawn.
    /// </remarks>
    public static readonly BoolCVar OffscreenScene = Presets.Define(
        CVar.Bool(
            "r_hdr",
            true,
            CVarOptions.Archive,
            "Render the scene offscreen so it can be tone mapped and post-processed. Off is the original's own path."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>How out-of-range light is brought back into what a display can show.</summary>
    public static readonly EnumCVar<ToneMapOperator> ToneMap = Presets.Define(
        CVar.Choice(
            "r_tonemap",
            ToneMapOperator.Neutral,
            CVarOptions.Archive,
            "Tone map: none reproduces the original exactly, neutral stays out of the way, aces and agx are filmic."),
        vanilla: "none",
        enhanced: "neutral",
        high: "neutral",
        ultra: "agx");

    /// <summary>A linear multiplier applied to the scene before tone mapping.</summary>
    public static readonly NumericCVar<float> Exposure = CVar.Float(
        "r_exposure",
        1.0f,
        0.05f,
        16.0f,
        CVarOptions.Archive,
        "Scene brightness before tone mapping. 1 leaves the level's own lighting alone.");

    /// <summary>How many samples per pixel the scene is rasterised with.</summary>
    /// <remarks>
    /// Resolved down before anything samples the target, because the effects that follow read depth
    /// and a multisampled depth buffer is not something a shader can read usefully.
    /// </remarks>
    public static readonly NumericCVar<int> MultiSampling = Presets.Define(
        CVar.Int(
            "r_msaa",
            4,
            0,
            8,
            CVarOptions.Archive,
            "Multisample count for the scene: 0 or 1 is off. Only applies when r_hdr is on."),
        vanilla: "0",
        enhanced: "2",
        high: "4",
        ultra: "8");

    /// <summary>The registry the renderer's own settings describe themselves to.</summary>
    private static GraphicsPresetRegistry Presets => GraphicsPresetRegistry.Default;
}
