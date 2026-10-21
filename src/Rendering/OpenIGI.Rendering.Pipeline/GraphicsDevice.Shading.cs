using System.Runtime.InteropServices;
using OpenIGI.Rendering.Pipeline.Sky;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Sky;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The shading: the G-buffer read back as textures and turned into lit colour, and the sky it is lit by.</summary>
public sealed partial class GraphicsDevice
{
    // Which frame slot this frame is copying the sky's harmonics into (none until it does), and which slot's
    // copy was last sent.
    private int _skyRecordedSlot = -1;
    private int _skySubmittedSlot = -1;

    // The last harmonics that arrived, answered again while a newer copy is still in flight.
    private readonly float[] _skyIrradiance = new float[SkyAtmosphere.IrradianceFloats];
    private bool _skyIrradianceKnown;

    // Which march of the sky the held harmonics were copied after; see TryGetSkyIrradiance.
    private int _skyIrradianceBuild;

    /// <summary>Records the shading: the whole G-buffer read back as textures, over one triangle.</summary>
    /// <remarks>Once a capture, whichever of the forward pass and the capture's end asks first.</remarks>
    private void RecordShading()
    {
        if (_shaded || _scene is not { } scene || _shading is not { } shading)
        {
            return;
        }

        _shaded = true;

        // Nothing else may be open: a render pass begun inside another is not nesting, it is undefined,
        // and the encoder that ends the outer one afterwards ends something that is no longer there.
        ClosePass();
        CloseScenePass();

        // The traced world as this frame drew it, before the first ray -- if the screen GI has not already asked.
        SettleTracedWorld();

        RecordSky();
        RecordVelocityBeforeShading(scene);
        RecordOcclusion(scene);
        RecordSunShadow(scene);
        RecordLampShadow(scene);
        RecordFlashlightShadow(scene);
        RecordFog(scene);

        Span<float> gi = stackalloc float[DeferredLightingParameters.GiFloats];
        Span<float> lamps = stackalloc float[DeferredLampBlock.Floats];
        Span<float> light = stackalloc float[DeferredLightingParameters.LightFloats];

        DeferredLightingParameters.WriteGi(
            gi,
            WithReflectionCoverage(
                WithFlashlight(WithFog(WithWetness(WithGlobalIllumination(WithTracedSun(WithShadows(LightingState()))))))),
            CascadeSplits,
            CascadeMatrices);
        DeferredLampBlock.Write(lamps, LampState(), LocalLights);

        DeferredLightingParameters.WriteLight(
            light,
            _lighting.SunDirection,
            (int)_lighting.Model,
            _lighting.SunColour,
            _lighting.Scale,
            _lighting.AmbientColour,
            (int)_debugView,
            _frameHasPaintedSky,
            _lighting.CompleteBake,
            _lighting.Night);

        shading.BindFrame(scene, FrameSlot, gi, lamps);
        BindGlobalIllumination();
        BindFog();
        BindSunShadow();
        BindLampShadow();
        BindFlashlightShadow();
        BindShelterMap();

        using IRhiRenderPassEncoder pass = Encoder().BeginRenderPass(new RhiRenderPassDescriptor(
            [new RhiColorAttachment(scene.View(SceneTargetId.Lit), RhiLoadOp.Clear, RhiStoreOp.Store, RhiClearColor.Transparent)],
            Timestamps: _profiler.Time(RenderStage.Lighting, "lighting"),
            Label: "shading"));

        pass.SetViewport(0, 0, scene.Width, scene.Height, 0f, 1f);
        pass.SetScissor(0, 0, scene.Width, scene.Height);
        shading.Draw(pass, MemoryMarshal.AsBytes(light));
        pass.EndPass();
    }

    /// <summary>Marches the atmosphere for this frame's sun, and copies its light down for the game.</summary>
    /// <remarks>
    /// Here rather than as the capture opens, because the game states the frame's lighting after it has
    /// opened one. The march itself is skipped while nothing it depends on has moved, which for a sun
    /// fixed for the mission is every frame after the first; the copy is not, so every frame slot holds
    /// the answer rather than one of them.
    /// </remarks>
    private void RecordSky()
    {
        if (_sky is not { } sky)
        {
            return;
        }

        IRhiCommandEncoder encoder = Encoder();

        using (IRhiComputePassEncoder pass = encoder.BeginComputePass(new RhiComputePassDescriptor(
            _profiler.Time(RenderStage.Setup, "setup"),
            "sky")))
        {
            sky.Record(pass, FrameSlot, SkyConditions.ForScene(_lighting, _viewToWorld, altitudeMetres: 2f));
        }

        _skyRecordedSlot = sky.RecordNextReadback(encoder);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Read from the harmonics the atmosphere copies down every frame, into a ring of its own deeper than
    /// the frames in flight -- only from copies already SUBMITTED, because the game asks mid-frame, after
    /// this frame's copy is recorded and before it is sent, and a buffer mapped in that gap is one WebGPU
    /// will not let the frame copy into. Nothing is waited for: the newest copy that has arrived is read,
    /// and while none newer has, the last harmonics that did are answered again. No only before any have
    /// arrived.
    /// </remarks>
    public bool TryGetSkyIrradiance(Span<float> coefficients)
    {
        ThrowIfDisposed();
        // The newest copy that has already arrived, from any slot of the sky's ring: never a wait. The ring
        // is deeper than the frames in flight, so while the sky changes a copy two or three frames old is
        // always there, and the answer trails the sun by those frames rather than stalling every one.
        if (_sky is { } sky && sky.TryReadNewest(_skyIrradianceBuild, _skyIrradiance, out int arrived))
        {
            _skyIrradianceKnown = true;
            _skyIrradianceBuild = arrived;
        }

        // Once any have arrived, always an answer: a sky moves with the sun, not from one frame to the
        // next, and the game treats "no" as "no sky" for that frame's dispatches rather than keeping what
        // it had. On WebGPU a map lands when it lands, so "no" came at irregular intervals, and the screen
        // GI accumulated a shifting mix of sky-lit and flat-lit rays: every surface drifted in colour.
        if (!_skyIrradianceKnown || coefficients.Length < _skyIrradiance.Length)
        {
            return false;
        }

        _skyIrradiance.CopyTo(coefficients);
        return true;
    }

    /// <summary>What the shading is told about this frame.</summary>
    /// <remarks>
    /// Only the frame's own numbers. Everything else the shading reads -- the cascades, the sky's tables,
    /// the lamps, the GI, the wetness, the fog -- is bound by name by the pass that makes it, and a pass
    /// that did not run this frame leaves the value that says "not this frame" rather than a stand-in's
    /// contents.
    /// </remarks>
    private DeferredLightingState LightingState() => new(
        _unprojection,
        LightScale: _lighting.Scale,
        CameraPosition: _cameraPosition,
        OcclusionEnabled: _occlusionRan,
        ViewToWorld: _viewToWorld,
        ViewHeightKm: SkyConditions.ViewHeightFor(altitudeMetres: 2f));
}
