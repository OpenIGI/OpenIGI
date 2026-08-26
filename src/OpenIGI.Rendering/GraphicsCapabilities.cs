namespace OpenIGI.Rendering;

/// <summary>
/// What a device can do, as reported by the original's <c>GetGFXCaps</c> render-mode method.
/// </summary>
/// <param name="TextureUnits">The number of simultaneously bindable texture units. The original calls these TMUs and needs at least two for lightmapping.</param>
/// <param name="MaxTextureSize">The largest supported texture dimension in pixels.</param>
/// <param name="SupportsNonPowerOfTwoTextures">Whether textures whose dimensions are not powers of two can be created without padding.</param>
/// <param name="Renderer">A human-readable backend and adapter description, for logs and the options screen.</param>
public readonly record struct GraphicsCapabilities(
    int TextureUnits,
    int MaxTextureSize,
    bool SupportsNonPowerOfTwoTextures,
    string Renderer)
{
    /// <summary>
    /// Gets a value indicating whether the backend can render the scene offscreen.
    /// </summary>
    /// <remarks>
    /// Everything the remaster track adds — shadows, ambient occlusion, reflections, volumetrics,
    /// temporal anti-aliasing — needs the frame and its depth to still exist after they have been
    /// drawn. A backend that cannot offer that runs the original's direct path and nothing else.
    /// </remarks>
    public bool SupportsSceneCapture { get; init; }

    /// <summary>Gets a value indicating whether colour can be stored with range beyond <c>[0, 1]</c>.</summary>
    /// <remarks>Without it the modern path still runs, but bright light clips instead of rolling off.</remarks>
    public bool SupportsFloatRenderTargets { get; init; }

    /// <summary>Gets a value indicating whether compute shaders are available.</summary>
    /// <remarks>Only ever an accelerator: every effect has a path that does not need it.</remarks>
    public bool SupportsCompute { get; init; }

    /// <summary>Whether a compute shader on this device can trace rays against the world directly.</summary>
    /// <remarks>
    /// The difference between marching a distance field towards a surface and asking the hardware where
    /// the ray actually lands. Both answer the same question; the ray answers it exactly, and against
    /// geometry rather than an approximation of it. A backend that says yes here is expected to accept a
    /// compute shader that declares an acceleration structure and to have one bound for it.
    /// </remarks>
    public bool SupportsRayQuery { get; init; }

    /// <summary>Gets the highest sample count the backend will honour for a scene target.</summary>
    public int MaxSampleCount { get; init; }
}
