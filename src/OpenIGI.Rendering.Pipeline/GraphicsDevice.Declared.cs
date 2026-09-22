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
    /// <summary>The settings this device records and does not act on.</summary>
    /// <remarks>
    /// Console-variable names, so a reader can match them against what the settings file holds. Every
    /// one of them belongs to the deferred stack, which has not moved onto this path yet.
    /// </remarks>
    private static readonly string[] UnimplementedSettingNames =
    [
        "r_shadows",
        "r_ssao",
        "r_reflections",
        "r_rt_reflections",
        "r_rt_shadows",
        "r_volumetric_fog",
        "r_taa",
        "r_bloom",
        "r_motion_blur",
        "r_exposure",
        "r_screen_gi",
        "r_surface_cache",
        "r_wetness",
        "r_storm",
        "r_material_detail",
        "r_debug_view",
    ];

    /// <inheritdoc/>
    public bool SunShadowTraced => false;

    /// <inheritdoc/>
    /// <remarks>One, because the direct path never tone-maps: what it draws is what is shown.</remarks>
    public float SceneExposure => 1f;

    /// <inheritdoc/>
    public TextureHandle CardAlbedo => TextureHandle.None;

    /// <inheritdoc/>
    public TextureHandle CardNormal => TextureHandle.None;

    /// <inheritdoc/>
    public TextureHandle CardMaterial => TextureHandle.None;

    /// <inheritdoc/>
    public TextureHandle CardDepth => TextureHandle.None;

    /// <inheritdoc/>
    public TextureHandle SceneDepth => TextureHandle.None;

    /// <inheritdoc/>
    public TextureHandle SceneNormal => TextureHandle.None;

    /// <inheritdoc/>
    public TextureHandle SceneColour => TextureHandle.None;

    /// <inheritdoc/>
    /// <remarks>
    /// False, always. It is the question everything above asks before drawing the remaster's frame, and
    /// answering it truthfully is what makes a device that draws only the direct path usable rather than
    /// broken.
    /// </remarks>
    public bool BeginSceneCapture(in SceneTargetDescription description) => false;

    /// <inheritdoc/>
    public void EndSceneCapture()
    {
        // Nothing was begun, so there is nothing to end.
    }

    /// <inheritdoc/>
    public void ResolveScene(in PostProcessSettings post)
    {
        // The direct path draws straight into the back buffer; there is nothing to resolve.
    }

    /// <inheritdoc/>
    public void BeginPass(ScenePass pass) => CurrentPass = pass;

    /// <inheritdoc/>
    public void EndPass() => CurrentPass = ScenePass.None;

    /// <inheritdoc/>
    public bool BeginShadowCascade(int cascade) => false;

    /// <inheritdoc/>
    public void EndShadowCascade()
    {
        // No cascade was begun.
    }

    /// <inheritdoc/>
    public bool BeginCardCapture(int width, int height) => false;

    /// <inheritdoc/>
    public void SetCardPage(int x, int y, int width, int height)
    {
        // No capture is open.
    }

    /// <inheritdoc/>
    public void EndCardCapture()
    {
        // No capture is open.
    }

    /// <inheritdoc/>
    public void ReadCardAtlas(int attachment, Span<Bgra32> destination) => destination.Clear();

    /// <inheritdoc/>
    public bool TryGetSkyIrradiance(Span<float> coefficients) => false;

    /// <inheritdoc/>
    public void ReleaseWorld()
    {
        // Every mesh and texture is released by its own handle; nothing here holds the world besides.
    }

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
    public void SetMaterialOverride(in SurfaceMaterial material)
    {
    }

    /// <inheritdoc/>
    public void SetPaintedSky(bool painted)
    {
    }

    /// <inheritdoc/>
    public void SetAlphaHash(float seed)
    {
    }

    /// <inheritdoc/>
    public void SetTextureDetail(TextureHandle diffuse, TextureHandle detail)
    {
    }

    /// <inheritdoc/>
    public void SetMaterialDetail(float strength)
    {
    }

    /// <inheritdoc/>
    public void SetFloorGloss(float gloss)
    {
    }

    /// <inheritdoc/>
    public void SetLightmapModulates(bool modulate)
    {
    }

    /// <inheritdoc/>
    public void SetWetness(in WetnessSettings wetness)
    {
    }

    /// <inheritdoc/>
    public void SetShelterMap(TextureHandle map, in ShelterMapTransform transform)
    {
    }

    /// <inheritdoc/>
    public void SetReflections(in ReflectionSettings settings)
    {
    }

    /// <inheritdoc/>
    public void SetSurfaceCache(in SurfaceCacheBinding binding)
    {
    }

    /// <inheritdoc/>
    public void SetDistanceField(in DistanceFieldBinding binding)
    {
    }

    /// <inheritdoc/>
    public void SetColourLookup(ColourLookupTable? table)
    {
    }

    /// <inheritdoc/>
    public void SetScreenIndirect(TextureHandle indirect, float strength, bool fullResolution = false)
    {
    }

    /// <inheritdoc/>
    public void SetLocalLights(ReadOnlySpan<DeferredLocalLight> lights)
    {
    }

    /// <inheritdoc/>
    public void SetEnvironmentCube(TextureHandle cube)
    {
    }

    /// <inheritdoc/>
    public void SetDebugView(DebugView view)
    {
    }

    /// <inheritdoc/>
    public void SetIrradianceVolume(IrradianceVolume? volume)
    {
    }

    /// <inheritdoc/>
    public void ClearRealtimeProbeVolume()
    {
    }

    /// <inheritdoc/>
    public void SetIrradianceVolumeSettings(in IrradianceVolumeSettings settings)
    {
    }

    /// <inheritdoc/>
    public void SetAmbientOcclusion(in AmbientOcclusionSettings settings)
    {
    }

    /// <inheritdoc/>
    public void SubmitShadowCaster(MeshHandle mesh, in Matrix4f modelView, int indexCount)
    {
    }
}
