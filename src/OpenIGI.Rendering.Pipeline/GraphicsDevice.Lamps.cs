using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Temporal;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The level's lamps: what the shading, the fog and the first-person rig are lit by besides the sun.</summary>
/// <remarks>
/// Held for one capture, as on the Vulkan device: the shading reads them, the fog scatters them, and the
/// forward pass lights the rig from them after the shading has run, so they are cleared only as the capture
/// ends -- a frame that submits none still lights from none.
/// </remarks>
public sealed partial class GraphicsDevice
{
    private readonly DeferredLocalLight[] _localLights = new DeferredLocalLight[DeferredLocalLight.Maximum];
    private readonly float[] _lampBlock = new float[DeferredLampBlock.Floats];
    private int _localLightCount;
    private TracedLampShadow? _tracedLamps;

    // Whether this frame's lamp visibility was traced, and so whether the shading reads it.
    private bool _lampsTraced;

    /// <summary>Gets this frame's lamps.</summary>
    private ReadOnlySpan<DeferredLocalLight> LocalLights => _localLights.AsSpan(0, _localLightCount);

    /// <inheritdoc/>
    public void SetLocalLights(ReadOnlySpan<DeferredLocalLight> lights)
    {
        ThrowIfDisposed();

        _localLightCount = Math.Min(lights.Length, DeferredLocalLight.Maximum);
        lights[.._localLightCount].CopyTo(_localLights);
    }

    /// <summary>What the frame's lamps do, on the Vulkan device's terms.</summary>
    /// <remarks>
    /// The screen march is on wherever lamp shadows are asked for at all: it needs no ray query, and it is
    /// what keeps a lamp from lighting through a wall on an adapter that cannot trace. The traced
    /// visibility needs the adapter, the RT switches and a built world, as it does there.
    /// </remarks>
    private DeferredLampState LampState() => new(
        _frameCounter,
        ScreenMarch: RenderCVars.LocalLightShadows.Value != 0,
        Traced: WillTraceLamps(),
        MaxSourceRadius: RenderCVars.LocalLightSource.Value);

    /// <summary>Whether this frame's lamp shadows are traced.</summary>
    private bool WillTraceLamps() =>
        RenderCVars.LocalLightShadows.Value != 0
        && RenderCVars.HardwareRayTracing.Value
        && RenderCVars.TracedLampShadows.Value
        && _shading?.Variant == DeferredLighting.TracedVariant
        && TracedWorld is { IsWorldReady: true, WorldScene: not null };

    /// <summary>Traces how much of each pixel's chosen lamp reaches it, for the shading to read this frame.</summary>
    /// <remarks>
    /// One ray per pixel at half resolution, whatever the lamp count; timed as a shadow, as on the Vulkan
    /// device. After the lamps are known and before the fog, which scatters them.
    /// </remarks>
    private void RecordLampShadow(SceneTargetSet scene)
    {
        _lampsTraced = false;

        if (_localLightCount <= 0 || !WillTraceLamps() || TracedWorld is not { WorldScene: { } world })
        {
            return;
        }

        _tracedLamps ??= new TracedLampShadow(_device, FramesInFlight, _retired);
        _tracedLamps.Ensure(scene.Width, scene.Height);
        _tracedLamps.ClearNew(Encoder());

        DeferredLampBlock.Write(_lampBlock, LampState(), LocalLights);
        TemporalFrameState state = TemporalState(scene);

        using (IRhiComputePassEncoder pass = Encoder().BeginComputePass(new RhiComputePassDescriptor(
            _profiler.Time(RenderStage.Shadows),
            "lampshadow")))
        {
            _tracedLamps.Record(
                pass,
                FrameSlot,
                scene.View(SceneTargetId.Depth),
                scene.View(SceneTargetId.Normal),
                new TracedLampFrame(
                    world,
                    _cameraPosition,
                    _lampBlock,
                    (int)(_frameCounter % 64UL),
                    RenderCVars.LampDenoise.Value != 0,
                    TracedWorld!.CutoutMasks,
                    TracedWorld.HitRecordAddress),
                state);
        }

        _lampsTraced = true;
    }

    /// <summary>Points the shading at the traced lamp visibility and the world, or at stand-ins.</summary>
    private void BindLampShadow() =>
        _shading?.BindLampVisibility(_lampsTraced ? _tracedLamps?.Filtered : null, TracedWorld?.TraceableScene);

    private void DisposeLampShadow()
    {
        _tracedLamps?.Dispose();
        _tracedLamps = null;
    }
}
