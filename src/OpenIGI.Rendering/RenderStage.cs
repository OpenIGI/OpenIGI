namespace OpenIGI.Rendering;

/// <summary>
/// The stages of a frame that are timed separately.
/// </summary>
/// <remarks>
/// <para>
/// One entry per stage the remaster track set a budget for, in the order the frame runs them, plus the
/// two that are not optional — the G-buffer fill and the forward pass — because a budget for the
/// effects means nothing without the cost of the frame they are added to.
/// </para>
/// <para>
/// These are <em>coarse</em> on purpose. A timer query is a synchronisation point for the driver's
/// pipelining, so one per draw call would change the thing it is measuring; one per stage answers the
/// question the budget asks ("what does the fog cost") without perturbing it.
/// </para>
/// </remarks>
public enum RenderStage
{
    /// <summary>Framebuffer binds, viewport changes and clears before scene rasterisation.</summary>
    Setup,

    /// <summary>Every shadow cascade's depth render, together.</summary>
    Shadows,

    /// <summary>Opaque world rasterisation, including Vanilla's direct forward path.</summary>
    Geometry,

    /// <summary>Multisample or G-buffer resolve work after opaque rasterisation.</summary>
    GeometryResolve,

    /// <summary>The low-resolution horizon search for ambient occlusion and screen-space bounce.</summary>
    AmbientOcclusionSearch,

    /// <summary>Depth- and normal-aware reconstruction of the occlusion result.</summary>
    AmbientOcclusionReconstruct,

    /// <summary>The deferred lighting pass, including the probe field's contribution.</summary>
    Lighting,

    /// <summary>The screen-space reflection march.</summary>
    Reflections,

    /// <summary>The low-resolution volumetric fog integration.</summary>
    FogIntegrate,

    /// <summary>The depth-aware full-resolution fog composite.</summary>
    FogComposite,

    /// <summary>The forward pass — translucent surfaces and the view model.</summary>
    Forward,

    /// <summary>The temporal resolve, reprojection and resolution transfer.</summary>
    TemporalResolve,

    /// <summary>Depth of field, motion blur and other camera/lens effects.</summary>
    CameraEffects,

    /// <summary>Bloom extraction and blur.</summary>
    Bloom,

    /// <summary>Luminance reduction and exposure adaptation.</summary>
    Exposure,

    /// <summary>Tone mapping, colour grading, vignette and final encode.</summary>
    ToneMapGrade,

    /// <summary>The display-space anti-aliasing filter.</summary>
    SpatialAntiAliasing,

    /// <summary>Desktop-owned HUD, menus and other two-dimensional rasterisation.</summary>
    UserInterface,

    /// <summary>Debug panels, primitives and developer overlays.</summary>
    DebugRendering,
}
