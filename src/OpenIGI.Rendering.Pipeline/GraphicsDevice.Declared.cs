using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>
/// What the direct path is told and does not act on, and the handful of things it genuinely does.
/// </summary>
/// <remarks>
/// <para>
/// A setting that is stored and then ignored is the worst kind of dead control: the value persists, the
/// options screen shows it, and the picture never changes — so the person changing it concludes the
/// effect is broken rather than absent, and anyone comparing two renders is comparing configurations
/// that were never the same. The names are therefore reported through
/// <see cref="GraphicsCapabilities.UnimplementedSettings"/>, where the shell says so out loud at
/// start-up, and each disappears from the list on the day its pass lands.
/// </para>
/// <para>
/// The alternative — throwing — would be worse and not more honest: the game calls these
/// unconditionally, so a device that threw would run no frame at all rather than the frame it can draw.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    /// <summary>The settings this device records and does not act on, whatever it draws through.</summary>
    /// <remarks>
    /// Console-variable names, so a reader can match them against what the settings file holds. Every
    /// one of them belongs to the deferred stack, which has not moved onto this path yet.
    /// </remarks>
    private static readonly string[] UnimplementedSettingNames =
    [
        "r_storm",
        "r_material_detail",
    ];

    /// <summary>The settings this device does not act on over the backend it was given.</summary>
    /// <param name="temporal">Whether the backend can run the temporal resolve.</param>
    /// <param name="gi">Whether it can run the game's GI stack.</param>
    /// <param name="traced">Whether it traces rays, which is what the traced sun shadow needs.</param>
    /// <param name="metered">Whether it can meter the scene for the eye adaptation.</param>
    /// <returns>The names.</returns>
    private static string[] UnimplementedOn(bool temporal, bool gi, bool traced, bool metered) =>
    [
        .. UnimplementedSettingNames,
        .. temporal ? Array.Empty<string>() : ["r_taa"],
        .. gi ? Array.Empty<string>() : ["r_screen_gi", "r_surface_cache"],
        .. traced ? Array.Empty<string>() : ["r_rt_shadows", "r_rt_lights", "r_rt_reflections"],
        .. metered ? Array.Empty<string>() : ["r_autoexposure"],
    ];

    /// <inheritdoc/>
    /// <remarks>
    /// The setting times what the eye has adapted to while a capture is open, as on the Vulkan device; one
    /// outside it, because the direct path never tone-maps: what it draws is what is shown.
    /// </remarks>
    public float SceneExposure => _capturing
        ? MathF.Max(RenderCVars.EffectiveExposure * (_autoExposure?.Adapted ?? 1f), 1e-3f)
        : 1f;

    /// <inheritdoc/>
    public void SetTextureQuality(int maxAnisotropy) => _textures.MaxAnisotropy = maxAnisotropy;

    /// <inheritdoc/>
    public void SetStageTiming(bool enabled) => _profiler.Enabled = enabled;

    /// <inheritdoc/>
    public IReadOnlyList<RenderStageTiming> StageTimings() => _profiler.Ledger.Timings();

    /// <inheritdoc/>
    public GpuFrameTiming GpuTiming() => _profiler.Ledger.TotalTiming();

    /// <inheritdoc/>
    public GpuFrameTiming GpuMeasuredTiming() => _profiler.Ledger.MeasuredTiming();

    /// <inheritdoc/>
    /// <remarks>
    /// The RHI times a pass rather than an arbitrary range, so a stage is bracketed where its pass
    /// begins. The direct path has one pass, which is already timed.
    /// </remarks>
    public void BeginStageTiming(RenderStage stage)
    {
        // Timed by the pass itself; see StageProfiler.
    }

    /// <inheritdoc/>
    public void EndStageTiming()
    {
        // Likewise.
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Kept for the card capture, which writes it into every page it draws. The shading paths that
    /// would otherwise read it have not moved onto this device yet.
    /// </remarks>
    public void SetMaterialOverride(in SurfaceMaterial material) => _material = material;

    /// <inheritdoc/>
    /// <remarks>
    /// Kept for the geometry pass, which dithers a cut-out's alpha against it so that a surface fading
    /// out of the world does so in a pattern the temporal resolve can average rather than in a hard step.
    /// </remarks>
    public void SetAlphaHash(float seed) => _alphaHashSeed = seed;

    /// <inheritdoc/>
    public void SetTextureDetail(TextureHandle diffuse, TextureHandle detail)
    {
    }

    /// <inheritdoc/>
    public void SetMaterialDetail(float strength)
    {
    }

    /// <inheritdoc/>
    public void SetEnvironmentCube(TextureHandle cube)
    {
    }

    /// <inheritdoc/>
    public void SetIrradianceVolume(IrradianceVolume? volume)
    {
    }

    /// <inheritdoc/>
    public void SetAmbientOcclusion(in AmbientOcclusionSettings settings) => _occlusionSettings = settings;
}
