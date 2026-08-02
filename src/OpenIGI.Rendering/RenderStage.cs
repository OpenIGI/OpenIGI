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
    /// <summary>Every shadow cascade's depth render, together.</summary>
    Shadows,

    /// <summary>The G-buffer fill: albedo, normals, material, motion and depth.</summary>
    Geometry,

    /// <summary>The horizon search — ambient occlusion and its screen-space bounce.</summary>
    AmbientOcclusion,

    /// <summary>The deferred lighting pass, including the probe field's contribution.</summary>
    Lighting,

    /// <summary>The screen-space reflection march.</summary>
    Reflections,

    /// <summary>The volumetric fog march.</summary>
    Fog,

    /// <summary>The forward pass — translucent surfaces and the view model.</summary>
    Forward,

    /// <summary>The temporal resolve and upsample.</summary>
    Temporal,

    /// <summary>Everything after the scene: defocus, bloom, tone map, grade and edge filter.</summary>
    Post,
}
