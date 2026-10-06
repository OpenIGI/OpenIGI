using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The player's flashlight: what the shading, the fog and the first-person rig light with besides the lamps.</summary>
/// <remarks>
/// Held for one capture, as the lamps are: the shading and the fog read it and the forward pass lights the rig
/// from it afterwards, so it is cleared only as the capture ends -- a frame that sets none has none.
/// </remarks>
public sealed partial class GraphicsDevice
{
    private FlashlightLight _flashlight = FlashlightLight.Off;
    private TracedFlashlightShadow? _flashlightShadow;

    // Whether this frame made the flashlight a shadow mask, and so whether the shading reads it.
    private bool _flashlightShadowed;

    /// <inheritdoc/>
    public void SetFlashlight(in FlashlightLight light)
    {
        ThrowIfDisposed();
        _flashlight = light;
    }

    /// <summary>Whether this frame's flashlight shadow is traced against the world rather than marched.</summary>
    private bool WillTraceFlashlight() =>
        RenderCVars.HardwareRayTracing.Value
        && RenderCVars.TracedLampShadows.Value
        && TracedWorld is { IsWorldReady: true, WorldScene: not null };

    /// <summary>Casts the flashlight's shadow, for the shading to read this same frame.</summary>
    /// <remarks>
    /// Traced where the adapter and the RT switches allow and marched through the depth buffer everywhere
    /// else, so there is always a shadow when one is asked for. Nothing at all runs while the light is off.
    /// Under a debug view only for the one that shows it, since the others isolate some other term.
    /// </remarks>
    private void RecordFlashlightShadow(SceneTargetSet scene)
    {
        _flashlightShadowed = false;

        if (!_flashlight.IsOn
            || !_flashlight.Shadows
            || (_debugView is not DebugView.None and not DebugView.FlashlightShadow and not DebugView.FlashlightLight))
        {
            return;
        }

        _flashlightShadow ??= new TracedFlashlightShadow(_device, FramesInFlight, _retired);
        _flashlightShadow.Ensure(scene.Width, scene.Height);

        bool traced = WillTraceFlashlight() && TracedWorld is { WorldScene: not null };

        using (IRhiComputePassEncoder pass = Encoder().BeginComputePass(new RhiComputePassDescriptor(
            _profiler.Time(RenderStage.Shadows, "flashlight.shadow"),
            "flashlightshadow")))
        {
            _flashlightShadowed = _flashlightShadow.Record(
                pass,
                FrameSlot,
                scene.View(SceneTargetId.Depth),
                scene.View(SceneTargetId.Normal),
                new FlashlightShadowFrame(
                    traced ? TracedWorld!.WorldScene : null,
                    traced ? TracedWorld!.CutoutMasks : null,
                    traced ? TracedWorld!.HitRecordAddress : 0UL,
                    _cameraPosition,
                    _viewToWorld,
                    _unprojection,
                    _flashlight,
                    RawLighting ? (long)(_frameCounter & 0xFFFFFFUL) : (long)(_frameCounter % 65536UL),
                    RawLighting));
        }
    }

    /// <summary>What the shading is told about the flashlight this frame.</summary>
    private DeferredLightingState WithFlashlight(in DeferredLightingState state) => state with
    {
        Flashlight = _flashlight,
        FlashlightShadowed = _flashlightShadowed,
        FrameIndex = (long)(_frameCounter % 65536UL),
    };

    /// <summary>Points the shading at the flashlight's mask, or at a stand-in for a frame that made none.</summary>
    /// <remarks>Every frame: a slot left bound to an old mask would shadow the beam with somewhere it no longer is.</remarks>
    private void BindFlashlightShadow() =>
        _shading?.BindFlashlightShadow(_flashlightShadowed ? _flashlightShadow?.Mask : null);

    private void DisposeFlashlightShadow()
    {
        _flashlightShadow?.Dispose();
        _flashlightShadow = null;
    }
}
