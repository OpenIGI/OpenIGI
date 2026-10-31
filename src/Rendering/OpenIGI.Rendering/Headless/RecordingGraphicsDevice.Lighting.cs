using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering.Headless;

public sealed partial class RecordingGraphicsDevice
{
    /// <summary>The lighting each frame was set to, in order.</summary>
    public List<SceneLighting> Lightings { get; } = [];

    /// <inheritdoc/>
    public bool TryGetSkyIrradiance(Span<float> coefficients) => false;

    /// <inheritdoc/>
    public void SetScreenIndirect(TextureHandle indirect, float strength, bool fullResolution = false)
    {
    }

    /// <summary>Gets the local lights the last frame submitted, in the order they were given.</summary>
    /// <remarks>
    /// Recorded rather than dropped: a lamp reaching the device at all is the thing that was silently
    /// missing before -- the fixtures were proposed, written to a file and handed to the surface cache,
    /// and the field the deferred pass read from was never assigned, so no punctual light was ever
    /// submitted by anything.
    /// </remarks>
    public List<DeferredLocalLight> LocalLights { get; } = [];

    /// <inheritdoc/>
    public void SetLocalLights(ReadOnlySpan<DeferredLocalLight> lights)
    {
        LocalLights.Clear();

        foreach (DeferredLocalLight light in lights)
        {
            Record(LocalLights, light);
        }
    }

    /// <summary>Gets every flashlight the frames have set, in order.</summary>
    public List<FlashlightLight> Flashlights { get; } = [];

    /// <inheritdoc/>
    public void SetFlashlight(in FlashlightLight light) => Record(Flashlights, light);

    /// <inheritdoc/>
    public void SetLighting(in SceneLighting lighting) => Record(Lightings, lighting);

    /// <summary>The surface caches handed over, in order.</summary>
    public List<SurfaceCacheBinding> SurfaceCaches { get; } = [];

    /// <inheritdoc/>
    public void SetSurfaceCache(in SurfaceCacheBinding binding) => Record(SurfaceCaches, binding);

    /// <summary>The probe volumes handed over, in order; a null entry is a clear.</summary>
    public List<IrradianceVolume?> Volumes { get; } = [];

    /// <summary>The volume settings each frame was given, in order.</summary>
    public List<IrradianceVolumeSettings> VolumeSettings { get; } = [];

    /// <inheritdoc/>
    public void SetIrradianceVolume(IrradianceVolume? volume) => Record(Volumes, volume);

    /// <inheritdoc/>
    public void SetRealtimeProbeVolume(
        TextureHandle red,
        TextureHandle green,
        TextureHandle blue,
        TextureHandle distance,
        Vector3f origin,
        float spacing,
        int countX,
        int countY,
        int countZ)
    {
    }

    /// <inheritdoc/>
    public void ClearRealtimeProbeVolume()
    {
    }

    /// <inheritdoc/>
    public void SetIrradianceVolumeSettings(in IrradianceVolumeSettings settings) =>
        Record(VolumeSettings, settings);

    /// <summary>The occlusion settings each frame was given, in order.</summary>
    public List<AmbientOcclusionSettings> Occlusion { get; } = [];

    /// <inheritdoc/>
    public void SetAmbientOcclusion(in AmbientOcclusionSettings settings) => Record(Occlusion, settings);
}
