using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Temporal;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The volumetric fog: the haze a level is seen through, lit by the sun and the sky.</summary>
public sealed partial class GraphicsDevice
{
    private VolumetricFogSettings _fogSettings = VolumetricFogSettings.Off;
    private readonly LocalFogVolume[] _fogVolumes = new LocalFogVolume[LocalFogVolume.MaxPerLevel];
    private int _fogVolumeCount;
    private VolumetricFog? _volumetricFog;

    // Whether this frame's fog was recorded, and so whether the shading may read it.
    private bool _fogRan;

    /// <inheritdoc/>
    /// <remarks>
    /// Held here and acted on between the geometry and the shading, where the volume is lit. The volumes
    /// are rebased against the camera on the way in, because everything downstream works camera-relative:
    /// a level sits tens of kilometres from the world origin, and a froxel's offset from the camera is a
    /// number with digits to spare where its absolute position is not.
    /// </remarks>
    public void SetVolumetricFog(
        in VolumetricFogSettings fog,
        Vector3f cameraPosition,
        ReadOnlySpan<LocalFogVolume> volumes)
    {
        ThrowIfDisposed();
        _fogSettings = fog;

        _fogVolumeCount = Math.Min(volumes.Length, LocalFogVolume.MaxPerLevel);
        for (int i = 0; i < _fogVolumeCount; i++)
        {
            LocalFogVolume volume = volumes[i];
            _fogVolumes[i] = volume with
            {
                Centre = new Vector3f(
                    volume.Centre.X - cameraPosition.X,
                    volume.Centre.Y - cameraPosition.Y,
                    volume.Centre.Z - cameraPosition.Z),
            };
        }

        Array.Clear(_fogVolumes, _fogVolumeCount, _fogVolumes.Length - _fogVolumeCount);
    }

    /// <summary>Lights and integrates this frame's fog, after the geometry and before the shading reads it.</summary>
    /// <remarks>
    /// Skipped under a debug view: those isolate one term of the lighting, and a term is not isolated if
    /// the whole scene's fog is composited over it. The medium is lit by the sun, the sky and the lamps.
    /// </remarks>
    private void RecordFog(SceneTargetSet scene)
    {
        _fogRan = false;

        if (!_fogSettings.Enabled || _debugView is not DebugView.None || _sky is not { } sky)
        {
            return;
        }

        _volumetricFog ??= new VolumetricFog(_device, FramesInFlight, _retired);
        _volumetricFog.Ensure(scene.Width, scene.Height, _fogSettings.Steps);

        TemporalFrameState state = TemporalState(scene);
        VolumetricFogFrame frame = new(
            _fogSettings,
            _fogVolumes.AsMemory(0, _fogVolumeCount),
            _localLights,
            _localLightCount,
            _lighting.SunDirection,
            _lighting.SunColour * _lighting.Scale,
            _lighting.AmbientColour,
            SkyValid: _lighting.Model != LightingModel.Baked,
            _fogSettings.BaseHeight - _cameraPosition.Z,
            _flashlight);

        bool cascades = _cascadeCount > 0 && _shadowsDrawn;

        using (IRhiComputePassEncoder pass = Encoder().BeginComputePass(new RhiComputePassDescriptor(
            _profiler.Time(RenderStage.FogIntegrate, "fog"),
            "fog")))
        {
            _volumetricFog.Record(
                pass,
                FrameSlot,
                frame,
                new VolumetricFogCamera(
                    state.ViewToWorld,
                    state.PreviousViewToWorld,
                    state.CameraDelta,
                    state.Unprojection,
                    state.PreviousUnprojection,
                    _cameraPosition),
                cascades ? _shadowArray : null,
                sky.IrradianceTable,
                CascadeMatrices,
                CascadeSplits,
                cascades ? _cascadeCount : 0,
                _shelterTransform.IsPresent ? ViewOf(_shelterMap) : null,
                _shelterTransform.IsPresent ? _shelterTransform : ShelterMapTransform.None);
        }

        _fogRan = true;
    }

    /// <summary>What the shading is told about the fog this frame: where its slices are, or that there is none.</summary>
    private DeferredLightingState WithFog(in DeferredLightingState state) => state with
    {
        Fog = _fogRan && _volumetricFog is { } fog ? fog.Slices : FogSliceDistribution.None,
    };

    /// <summary>Points the shading at the fog volume, or at nothing for a frame without one.</summary>
    private void BindFog() =>
        _shading?.BindFog(_fogRan ? _volumetricFog?.Integrated : null);

    private void DisposeFog()
    {
        _volumetricFog?.Dispose();
        _volumetricFog = null;
    }
}
