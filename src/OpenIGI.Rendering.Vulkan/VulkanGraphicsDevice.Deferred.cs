using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Deferred;
using OpenIGI.Rendering.Vulkan.Textures;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan;

/// <summary>
/// The deferred capture path for <see cref="VulkanGraphicsDevice"/> — begin capture, the geometry,
/// lighting and forward subpasses, end capture, and resolve to the back buffer.
/// </summary>
/// <remarks>
/// The whole of deferred is one render pass. Geometry writes the G-buffer in subpass 0; ending that pass
/// advances to the lighting subpass and draws the full-screen light; the forward pass is subpass 2; and
/// ending the capture closes the render pass, leaving the lit scene in a layout the resolve samples.
/// </remarks>
public sealed partial class VulkanGraphicsDevice
{
    private SceneLighting _lighting = SceneLighting.Baked;
    private SurfaceMaterial _material = SurfaceMaterial.Default;
    private float _lightmapScale = 1.0f;

    // The probe-field lighting inputs, captured from their feeds and written into the lighting uniform.
    private Vector3f _cameraPosition;
    private Matrix3f _viewToWorld = Matrix3f.Identity;
    private DepthUnprojection _unprojection = new(1f, 1f, 0.1f, 1000f);

    // The previous frame's camera, which is the whole of what a static world needs to be reprojected onto
    // it. Captured at the end of each captured frame rather than when the seam hands the camera over, so
    // a frame that renders nothing does not shift the reference the next one measures against.
    private Matrix3f _previousViewToWorld = Matrix3f.Identity;
    private Vector3f _previousCameraPosition;
    private DepthUnprojection _previousUnprojection = new(1f, 1f, 0.1f, 1000f);

    // The previous frame's sub-pixel projection offset, in the same history-space pixels the temporal
    // passes use. Anything reprojecting onto a buffer the previous frame wrote needs it: that frame
    // stored its answers at ITS jitter, not at this one's.
    private float _previousJitterX;
    private float _previousJitterY;
    private bool _hasPreviousFrame;
    private IrradianceVolumeSettings _volumeSettings = IrradianceVolumeSettings.Off;
    private bool _hasProbeField;

    // The irradiance field waiting to be uploaded into the probe volumes. Set out of frame during the
    // probe collect and applied at the top of the frame, so the transfer lands in the frame's own command
    // buffer -- one submission, in order -- rather than on a private submit racing the frame in flight.
    private IrradianceVolume? _pendingProbeVolume;

    // The screen-probe gather's near-field atlas, fed each frame it is traced.
    private TextureHandle _screenIndirect;
    private float _screenIndirectStrength;

    // The level's lamps for the frame being recorded. Held rather than acted on, and uploaded whole when
    // the lighting pass writes its uniforms; see SetLocalLights.
    private readonly DeferredLocalLight[] _localLights = new DeferredLocalLight[DeferredLocalLight.Maximum];
    private int _localLightCount;
    private bool _screenIndirectFullResolution;

    // The sun's cascaded shadow map: the composed world-to-cascade-clip matrices, the view depths the
    // cascades stop at, and the pass state while a cascade is being rendered.
    private int _shadowCascadeCount;
    private readonly Matrix4f[] _cascadeMatrices = new Matrix4f[ShadowSettings.MaxCascades];
    private readonly float[] _cascadeSplits = new float[ShadowSettings.MaxCascades];

    // Each cascade's world-to-light rotation. The shadow pass draws through a camera that sits where the
    // eye does but looks along the light, so undoing this is what recovers a page's offset from the eye.
    private readonly Matrix3f[] _cascadeLightRotations = new Matrix3f[ShadowSettings.MaxCascades];
    private ShadowSettings _shadowSettings = ShadowSettings.Off;
    private bool _inShadowPass;
    private int _shadowCascade;

    // Ambient occlusion, gathered by a compute pass after the deferred pass and applied a frame later.
    private AmbientOcclusionSettings _aoSettings = AmbientOcclusionSettings.Off;
    private WetnessSettings _wetness = WetnessSettings.Dry;
    private ReflectionSettings _reflectionSettings = ReflectionSettings.Off;
    private readonly WetShelter[] _wetShelters = new WetShelter[WetShelter.Capacity];
    private int _wetShelterCount;

    /// <summary>Gets a value indicating whether a shadow cascade is currently being rendered.</summary>
    internal bool InShadowPass => _inShadowPass;

    private readonly bool _deferredEnabled;
    private bool _capturing;
    private bool _deferredPassActive;
    // Which stage of the capture is open. Geometry and lighting are both the first subpass of their own
    // render pass now, so a subpass index no longer identifies where we are.
    private DeferredStage _deferredStage = DeferredStage.None;

    // Whether this frame actually traced the sun's shadow. Until the world has been seen there is nothing
    // to trace against, and the lighting keeps reading the cascades.
    private bool _sunShadowTraced;
    private bool _sunShadowTracedLastFrame;
    private TemporalAaSettings _taaSettings;
    private bool _temporalResolved;
    private int _captureWidth;
    private int _captureHeight;
    private int _presentWidth;
    private int _presentHeight;
    private int _lastCaptureWidth;
    private int _lastCaptureHeight;
    private Viewport _sceneViewport;
    private ScenePass _currentScenePass = ScenePass.None;
    private SceneTarget? _sceneTarget;

    // The exposure the last resolve settled on. The temporal passes run BEFORE the resolve that computes
    // this frame's, so a pass that needs to judge contrast at display levels -- the spatial anti-aliaser
    // does, its edge test being a threshold -- reads the previous frame's. Exposure eases over about a
    // second, so a frame's worth of lag is far below what the threshold can notice.
    private float _lastAdaptedExposure = 1f;

    /// <inheritdoc/>
    public ScenePass CurrentPass => _capturing ? _currentScenePass : ScenePass.None;

    /// <summary>Gets a value indicating whether the deferred render pass is currently recording.</summary>
    private bool DeferredPassActive => _deferredPassActive;

    /// <summary>Gets the extent of whichever render pass is currently active.</summary>
    private Extent2D CurrentExtent =>
        _deferredPassActive && _sceneTarget is not null ? _sceneTarget.Extent : _presentTarget.Extent;

    /// <inheritdoc/>
    public bool BeginSceneCapture(in SceneTargetDescription description)
    {
        ThrowIfDisposed();
        if (_capturing)
        {
            throw new GraphicsDeviceException("A scene capture is already in progress.");
        }

        // Only the deferred, HDR path is offered, and only when it has been opted into for validation;
        // otherwise the engine falls back to the direct path, which it handles as a declined capture.
        if (!_deferredEnabled || !description.Deferred)
        {
            return false;
        }

        EnsureFrame();
        _captureWidth = Math.Max(1, description.Width);
        _captureHeight = Math.Max(1, description.Height);
        _presentWidth = Math.Max(1, description.PresentWidth);
        _presentHeight = Math.Max(1, description.PresentHeight);

        // A resize recreates the views the object-velocity framebuffers point at; a stale framebuffer
        // over a destroyed view must never be found by a later lookup that got a recycled handle value.
        if (_captureWidth != _lastCaptureWidth || _captureHeight != _lastCaptureHeight)
        {
            _deferred.ObjectVelocity.InvalidateFramebuffers();
            _lastCaptureWidth = _captureWidth;
            _lastCaptureHeight = _captureHeight;
        }

        _sceneTarget = _deferred.Target(
            _frameLoop.CurrentFrame, _captureWidth, _captureHeight, _presentWidth, _presentHeight);

        // The scene's own passes draw at the render size, whatever the window's viewport says: the app's
        // viewport tracks the WINDOW, and under a render scale the two differ — rasterising a
        // window-sized viewport onto a smaller target is a zoomed-in crop, which is exactly what
        // r_scale used to produce here. Viewports the app sets DURING the capture (the map inset) are
        // already in render coordinates and land on top of this default.
        _sceneViewport = new Viewport(0, 0, _captureWidth, _captureHeight);

        // The gather holds its scene-texture handles for the life of the level, so what they point
        // at is refreshed here rather than the handles themselves changing.
        RefreshSceneTextures();

        // Before anything opens a render pass, because these are compute dispatches and a render pass is
        // the one place they cannot be recorded. Timed, because until they were the overlay could report
        // a frame far shorter than the one being drawn -- these run before every stage the table knew
        // about, so their cost showed up only in the gap nobody was measuring.
        {
            using StageScope setup = Measure(RenderStage.Setup);
            RecordSkyUpdate();
            ApplyPendingProbeVolume();
        }

        // The structure a shadow ray traces against, from everything the geometry pass has ever been asked
        // to draw. Recorded here for the same reason the sky is: a build cannot happen inside a render
        // pass, and the lighting subpass that traces against it is inside one.
        {
            using StageScope structures = Measure(RenderStage.AccelerationStructure);
            _worldScene?.Record(_frameCommandBuffer, _device.GraphicsQueue);
        }

        _capturing = true;
        _deferredStage = DeferredStage.None;
        _sunShadowTracedLastFrame = _sunShadowTraced;
        _sunShadowTraced = false;
        return true;
    }

    /// <inheritdoc/>
    public void BeginPass(ScenePass pass)
    {
        // A pipeline is built against one render pass, so nothing bound for the last one carries over.
        InvalidateBoundState();

        ThrowIfDisposed();
        if (!_capturing)
        {
            throw new GraphicsDeviceException("No scene capture is in progress.");
        }

        _currentScenePass = pass;
        if (pass == ScenePass.Geometry)
        {
            BeginGeometryPass();
            _deferredStage = DeferredStage.Geometry;

            // The game brackets geometry itself only on the vanilla direct path; on this deferred path it
            // does not, so the backend owns the timing. Closed in CloseGeometryForCompute, where the pass
            // that fills the G-buffer ends.
            BeginStageTiming(RenderStage.Geometry);
        }
        else if (pass == ScenePass.Forward)
        {
            // The shading pass is already open and its lighting subpass already drawn; translucency is its
            // second subpass.
            _instance.Vk.CmdNextSubpass(_frameCommandBuffer, SubpassContents.Inline);
            _deferredStage = DeferredStage.Forward;

            // Closed in EndSceneCapture, where the shading pass ends. Same reason as geometry: on the
            // deferred path the game leaves this to the backend.
            BeginStageTiming(RenderStage.Forward);
        }

        if (_deferredPassActive)
        {
            ApplyViewportAndScissor(_frameCommandBuffer, _sceneViewport);
        }
    }

    /// <inheritdoc/>
    public void EndPass()
    {
        ThrowIfDisposed();
        if (!_capturing)
        {
            throw new GraphicsDeviceException("No pass is open.");
        }

        // Ending the geometry pass is what closes it, runs everything that needs a finished G-buffer and
        // cannot run inside a render pass, and then opens the shading pass and draws the light.
        if (_deferredStage is DeferredStage.Geometry or DeferredStage.Gap)
        {
            // Already closed if a compute pass asked to read the G-buffer; closing it here is the ordinary
            // case, where nothing did.
            CloseGeometryForCompute();

            if (_deferredStage == DeferredStage.Geometry)
            {
                _instance.Vk.CmdEndRenderPass(_frameCommandBuffer);
                _deferredPassActive = false;
                RecordBetweenPasses();
            }

            // The lamps, separately from everything else that traces, and HERE rather than in
            // RecordBetweenPasses. That runs when the geometry pass closes, and at ultra the screen-probe
            // gather closes it early to read the G-buffer -- which is before the game has submitted this
            // frame's lamps, so the trace saw a count of nought and quietly did nothing. This spot is
            // after the lamps are known and still outside the render pass, which is what a dispatch needs.
            //
            // Timed as part of the shadows, which is not decoration. Moving the dispatch here took it out
            // of the scope it used to sit in, and nothing else covers this point in the frame -- so the
            // trace, both filters and every barrier between them were costing a millisecond of wall clock
            // that no stage timer counted, and the overlay read as though the lamps were free. A pass
            // nobody can see the cost of is a pass nobody will ever optimise.
            using (StageScope lamps = Measure(RenderStage.Shadows))
            {
                DispatchLampShadow();
            }

            BeginShadingPass();
            RecordLighting();
            _deferredStage = DeferredStage.Lighting;
        }

        _currentScenePass = ScenePass.None;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Asked for here and carried out at the next frame boundary, in <see cref="ReleaseWorldIfAsked"/>.
    /// A mission is released from the middle of a loop pass, and by then this frame's command buffer is
    /// usually open and already carries the scene build and the traces that read it. Waiting for the
    /// device to go idle does not make that safe: an open buffer has not been submitted, so there is
    /// nothing to wait for, and the structures freed underneath it are read the moment it is. Measured
    /// on the abandon-and-replay cycle, freeing them on the spot lost the device on five runs of eight,
    /// against one for the same build that left the world alone.
    /// </remarks>
    public void ReleaseWorld()
    {
        ThrowIfDisposed();
        _worldReleasePending = true;

        // These name nothing the GPU reads, so they go now. Pairings made against the level that is
        // leaving: mesh identifiers are never reused, so nothing would be mispaired -- but nothing would
        // ever be matched again either, and both maps would carry every animated draw of every mission
        // the session had entered.
        _objectVelocityPrevious.Clear();
        _objectVelocityCurrent.Clear();
        _objectVelocityOccurrence.Clear();
        _objectVelocityDraws.Clear();
    }

    /// <summary>Carries out a pending world release, on a frame boundary with nothing recording.</summary>
    /// <remarks>
    /// Called from <c>EnsureFrame</c> with no frame open, which is the only moment both conditions hold:
    /// no command buffer is recording references to what is about to be freed, and everything already
    /// submitted can be waited on. A level change has stopped for hundreds of milliseconds by this point,
    /// so the wait costs nothing anybody can see.
    /// </remarks>
    private void ReleaseWorldIfAsked()
    {
        if (!_worldReleasePending)
        {
            return;
        }

        _worldReleasePending = false;
        _device.WaitIdle();
        _worldScene?.Reset();
    }

    /// <inheritdoc/>
    public void EndSceneCapture()
    {
        ThrowIfDisposed();
        if (!_capturing)
        {
            throw new GraphicsDeviceException("No scene capture is in progress.");
        }

        if (_deferredPassActive)
        {
            // Every subpass must be entered before the render pass can end; a capture with no forward
            // draws still advances through the empty forward subpass.
            if (_deferredStage == DeferredStage.Lighting)
            {
                _instance.Vk.CmdNextSubpass(_frameCommandBuffer, SubpassContents.Inline);
                _deferredStage = DeferredStage.Forward;
            }

            // Close the Forward range the BeginPass opened, before the shading pass ends. A no-op on the
            // empty-forward path above, where nothing opened it.
            EndStageTiming();

            _instance.Vk.CmdEndRenderPass(_frameCommandBuffer);
            _deferredPassActive = false;

            // The lamps have now been read by both the lighting subpass (their whole point) and the
            // forward subpass that shades the first-person rig (LampLightAtEye). Cleared here rather than
            // at the end of RecordLighting so the rig can read them: a frame that submits none still
            // lights from none, because nothing re-adds them before the next SetLocalLights.
            _localLightCount = 0;

            // The pass has stored the depth; gather this frame's occlusion from it now, for the next frame's
            // lighting to read. The lighting already sampled the occlusion image this frame (its previous
            // contents), so a read-before-write barrier separates that from the compute write below.
            DispatchOcclusion();
            DispatchVelocity();

            // The reflections, after the frame is lit and before the temporal pass -- so the pass reads
            // the finished scene as the reflection's source, and so the temporal resolve then denoises
            // and stabilises what the single-ray march finds. Composited into the lit scene in place, so
            // everything downstream reads it with the reflections already in.
            DispatchReflections();

            DispatchTemporalResolve();

            // Everything temporal measures against this, so it moves on exactly once per captured frame.
            _previousViewToWorld = _viewToWorld;
            _previousCameraPosition = _cameraPosition;
            _previousUnprojection = _unprojection;
            _previousJitterX = _taaSettings.JitterX;
            _previousJitterY = _taaSettings.JitterY * VerticalJitterSign;
            _hasPreviousFrame = true;
            RollObjectVelocityFrame();
        }

        _capturing = false;
    }

    /// <summary>Rebuilds the atmosphere's tables for where the sun is and where the camera stands.</summary>
    /// <remarks>
    /// The renderer seam hands the sun over in view space, because that is what the forward paths want.
    /// The atmosphere is a world in its own right — a planet with air above it — so the direction is
    /// rotated into world space here, once, rather than inside every table that reads it.
    /// </remarks>
    private void RecordSkyUpdate()
    {
        Vector3f worldSun = WorldSunDirection();

        // The atmosphere's own source sun. This is deliberately independent of the direct-light sun:
        // scaling a surface response must not also scale the visible sky and the SH projected from it.
        // Older callers omit it and retain the previous shared-source behaviour.
        Vector3f source = _lighting.AtmosphereSunColour ?? _lighting.SunColour;
        Vector3f illuminance = new(
            source.X * _lighting.Scale,
            source.Y * _lighting.Scale,
            source.Z * _lighting.Scale);

        _deferred.Sky.Record(
            _frameCommandBuffer,
            _frameLoop.CurrentFrame,
            worldSun,
            illuminance,
            Sky.SkyAtmosphere.ViewHeightKm(2f),
            Math.Max(0f, _lighting.SkyLightIntensity));
    }

    /// <summary>
    /// Accumulates this jittered frame onto the temporal history, so the tone map reads a stable image.
    /// </summary>
    /// <remarks>
    /// The core of the reference's temporal upscaler: reproject the history by the velocity pass's motion, reject what
    /// no longer matches the neighbourhood, blend the new sample in, and carry the result forward as the
    /// next frame's history. Runs after the shading pass has ended and the velocity is written; the
    /// resolve then samples the accumulated output instead of the raw lit colour. Without this every
    /// silhouette shimmers, because the projection is deliberately jittered every frame and nothing was
    /// averaging the jitter out.
    /// </remarks>
    /// <summary>
    /// The vertical jitter's sign once it reaches history space.
    /// </summary>
    /// <remarks>
    /// The app hands over a jitter in the OpenGL clip convention, and this backend's G-buffer vertex
    /// stage is what flips Y into Vulkan's — so a positive vertical jitter displaces the rasterised image
    /// <em>up</em> the framebuffer, and the history has to look for that frame's sample in the opposite
    /// direction from the one the number reads as. Horizontal is untouched by the flip.
    ///
    /// Settled by measurement rather than by reading back through the projection chain, because getting
    /// it wrong does not break the picture — it quietly blurs it, by attributing each sample to the wrong
    /// sub-pixel texel. All four sign pairs were rendered at the office camera and scored on detail: the
    /// two axes came out independent and consistent (x positive won in both y cases, y negative in both
    /// x cases), over a 4.4% spread between best and worst.
    /// </remarks>
    private const float VerticalJitterSign = -1f;

    // How far, in metres, a reflected ray is marched through the depth buffer. Far enough to catch the
    // buildings and figures a wet street shows standing over it, short enough that the fixed number of
    // steps stays dense enough to land on them.
    private const float ReflectionReachMetres = 120f;

    private unsafe void DispatchTemporalResolve()
    {
        _temporalResolved = false;
        if (_sceneTarget is null || !_taaSettings.Accumulates)
        {
            return;
        }

        using StageScope temporal = Measure(RenderStage.TemporalResolve);

        Deferred.SceneTarget target = _sceneTarget;
        CommandBuffer cmd = _frameCommandBuffer;

        // The history is the OTHER frame slot's — written exactly one frame ago, which is the frame the
        // velocity spans. Reading this slot's own history would be TWO frames old, and reprojecting two
        // frames of motion with one frame's velocity smears every edge into a double image the moment the
        // camera moves. When the other slot has not been built at this size (first frames, a resize),
        // this slot's own cleared history stands in: its zero sample count makes the pass restart from
        // the current frame, which is the honest answer there anyway.
        Deferred.SceneTarget previous = _deferred.PreviousTarget(_frameLoop.CurrentFrame);
        bool previousUsable = previous.Extent.Width == target.Extent.Width
            && previous.Extent.Height == target.Extent.Height
            && previous.UpscaleHistoryColourView.Handle != 0;
        ImageView historyColourView = previousUsable ? previous.UpscaleHistoryColourView : target.UpscaleHistoryColourView;
        ImageView historyMetadataView = previousUsable ? previous.UpscaleHistoryMetadataView : target.UpscaleHistoryMetadataView;

        // The shading pass's colour write, ordered before the compute read of the lit image.
        LitReadBarrier(cmd, target.LitImage, PipelineStageFlags.ComputeShaderBit);

        // The velocity pass wrote the motion this frame; the history slot's images were written by the
        // previous frame's update and read by its resolve. One barrier over each covers all of it — same
        // queue, so the ordering reaches back across submissions.
        OcclusionBarrier(
            cmd, target.VelocityImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
        if (previousUsable)
        {
            OcclusionBarrier(
                cmd, previous.UpscaleHistoryColourImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
                PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
            OcclusionBarrier(
                cmd, previous.UpscaleHistoryMetadataImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
                PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
        }

        // This slot's own history was last read a frame ago, by its resolve.
        OcclusionBarrier(
            cmd, target.UpscaleHistoryColourImage, AccessFlags.ShaderReadBit, AccessFlags.ShaderWriteBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
        OcclusionBarrier(
            cmd, target.UpscaleHistoryMetadataImage, AccessFlags.ShaderReadBit, AccessFlags.ShaderWriteBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);

        // Thin geometry first, because the shading rejection below reads it. A hardware rasteriser draws
        // a triangle only where it covers a pixel's centre, so a wire narrower than a pixel blinks in and
        // out with the jitter -- which is the distant fence crawling, and then fading. This finds those
        // one-pixel runs; the two passes below each use the finding differently, one to stop the
        // reprojection being called a disocclusion and the other to stop the colour test rejecting a
        // history the frame simply missed.
        OcclusionBarrier(
            cmd, target.ThinRelaxedRejectionImage, AccessFlags.ShaderReadBit, AccessFlags.ShaderWriteBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);

        _deferred.ThinGeometry.Record(
            cmd,
            _frameLoop.CurrentFrame,
            target.DepthView,
            target.HistoryRejectionView,
            target.ThinRelaxedRejectionView,
            _captureWidth,
            _captureHeight,
            _unprojection,
            _historyDecimated,
            RenderCVars.ThinGeometry.Value,
            _velocityDilated ? target.DilatedVelocityView : target.VelocityView,
            hasVelocity: true);

        OcclusionBarrier(
            cmd, target.ThinRelaxedRejectionImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);

        // What this frame's shading makes of the history, before the history is touched. It reprojects
        // the previous frame's guide, so it has to run while that image still holds it.
        bool shadingRejected = false;
        if (previousUsable)
        {
            OcclusionBarrier(
                cmd, target.ShadingRejectionImage, AccessFlags.ShaderReadBit, AccessFlags.ShaderWriteBit,
                PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
            OcclusionBarrier(
                cmd, previous.GuideImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
                PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
            OcclusionBarrier(
                cmd, target.GuideImage, AccessFlags.ShaderReadBit, AccessFlags.ShaderWriteBit,
                PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);

            _deferred.RejectShading.Record(
                cmd,
                _frameLoop.CurrentFrame,
                target.LitView,
                _velocityDilated ? target.DilatedVelocityView : target.VelocityView,
                previous.GuideView,
                target.HistoryRejectionView,
                target.ShadingRejectionView,
                target.GuideView,
                target.ThinRelaxedRejectionView,
                _captureWidth,
                _captureHeight,
                _lastAdaptedExposure,
                1f / 16f,
                _historyDecimated,
                RenderCVars.ThinGeometry.Value);

            OcclusionBarrier(
                cmd, target.ShadingRejectionImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
                PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);

            // The pessimistic filter of that verdict, once, at this resolution. The history update needs
            // it per texel and runs at twice this size in each axis, so building it there evaluated a
            // frame-resolution filter four times over -- and the expensive way, since a min over a
            // three-by-three of medians reaches across a five-by-five footprint.
            OcclusionBarrier(
                cmd, target.RejectionBlendImage, AccessFlags.ShaderReadBit, AccessFlags.ShaderWriteBit,
                PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);

            _deferred.RejectionDilate.Record(
                cmd,
                _frameLoop.CurrentFrame,
                target.ShadingRejectionView,
                target.RejectionBlendView,
                _captureWidth,
                _captureHeight);

            OcclusionBarrier(
                cmd, target.RejectionBlendImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
                PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);

            shadingRejected = true;
        }

        // The spatial anti-aliaser, before the history update it feeds. It finds where each edge really
        // lies between pixel centres, so a wire thinner than a pixel is sampled from where it is rather
        // than being missed or drawn whole depending on which side of the centre it fell.
        OcclusionBarrier(
            cmd, target.SpatialAaImage, AccessFlags.ShaderReadBit, AccessFlags.ShaderWriteBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);

        _deferred.SpatialAntiAliasing.Record(
            cmd,
            _frameLoop.CurrentFrame,
            target.LitView,
            target.ThinRelaxedRejectionView,
            target.SpatialAaView,
            _captureWidth,
            _captureHeight,
            _lastAdaptedExposure,
            _historyDecimated);

        OcclusionBarrier(
            cmd, target.SpatialAaImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);

        // How far the camera has moved the picture since last frame, in screen pixels, from BOTH its
        // rotation and its translation -- the gate that decides whether the history may dead-zone the
        // jittered-depth velocity on grazing terrain. Near zero when the camera is held (that velocity is
        // then all artifact and worth zeroing); it must open on ANY real camera motion, and translation is
        // as real as rotation. Leaving translation out was the bug that smeared forward and backward
        // motion: a dolly has no rotation, so the gate read it as held, dead-zoned the far terrain's small
        // real velocity, and the picture ghosted along the direction of travel.
        //
        // Rotation: the Frobenius product of the two view rotations is 1 + 2cos(theta), which recovers the
        // swing angle without forming the relative rotation; scaled to pixels by the projection's own
        // vertical scale. Translation: the camera-to-camera move, taken as the pixel motion it gives a
        // surface a few metres away (near enough to catch a walk, far enough that the metre or so of float
        // slop a held camera carries at these world coordinates stays well under a pixel).
        Matrix3f cur = _viewToWorld;
        Matrix3f prv = _previousViewToWorld;
        float frob =
            (cur.M11 * prv.M11) + (cur.M12 * prv.M12) + (cur.M13 * prv.M13) +
            (cur.M21 * prv.M21) + (cur.M22 * prv.M22) + (cur.M23 * prv.M23) +
            (cur.M31 * prv.M31) + (cur.M32 * prv.M32) + (cur.M33 * prv.M33);
        float cosSwing = Math.Clamp((frob - 1f) * 0.5f, -1f, 1f);
        float swingRadians = MathF.Acos(cosSwing);
        float pixelsPerRadian = _unprojection.YScale * 0.5f * _captureHeight;

        float dx = _cameraPosition.X - _previousCameraPosition.X;
        float dy = _cameraPosition.Y - _previousCameraPosition.Y;
        float dz = _cameraPosition.Z - _previousCameraPosition.Z;
        float translationMetres = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        const float ReferenceDepthMetres = 5f;
        float translationPixels = translationMetres / ReferenceDepthMetres * pixelsPerRadian;

        float cameraPixelSpeed = (swingRadians * pixelsPerRadian) + translationPixels;

        _deferred.UpdateHistory.Record(
            cmd,
            _frameLoop.CurrentFrame,
            target.LitView,
            _velocityDilated ? target.DilatedVelocityView : target.VelocityView,
            historyColourView,
            historyMetadataView,
            target.UpscaleHistoryColourView,
            target.UpscaleHistoryMetadataView,
            target.ThinRelaxedRejectionView,
            _historyDecimated,
            target.SpatialAaView,
            hasSpatialAa: true,
            target.ShadingRejectionView,
            target.RejectionBlendView,
            shadingRejected,
            _captureWidth,
            _captureHeight,
            Deferred.SceneTarget.HistoryScale,
            temporalOn: true,
            maxSamples: 16f,
            _taaSettings.JitterX,
            _taaSettings.JitterY * VerticalJitterSign,
            cameraPixelSpeed,
            target.DepthView,
            _unprojection.Near,
            _unprojection.Far);

        // The resolve reads the history the update just wrote, and writes the frame-sized image.
        OcclusionBarrier(
            cmd, target.UpscaleHistoryColourImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
        OcclusionBarrier(
            cmd, target.UpscaleOutputImage,
            AccessFlags.ShaderReadBit, AccessFlags.ShaderWriteBit,
            PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.ComputeShaderBit,
            PipelineStageFlags.ComputeShaderBit);

        _deferred.ResolveHistory.Record(
            cmd,
            _frameLoop.CurrentFrame,
            target.UpscaleHistoryColourView,
            target.UpscaleOutputView,
            _captureWidth,
            _captureHeight,
            Deferred.SceneTarget.HistoryScale,
            _presentWidth,
            _presentHeight);

        // The tone-map resolve samples the output next.
        OcclusionBarrier(
            cmd, target.UpscaleOutputImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
            PipelineStageFlags.ComputeShaderBit,
            PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.ComputeShaderBit);

        _temporalResolved = true;
    }

    /// <summary>Orders the shading pass's colour write before a later read of the lit image.</summary>
    private unsafe void LitReadBarrier(CommandBuffer cmd, Image lit, PipelineStageFlags destinationStage)
    {
        ImageMemoryBarrier barrier = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = ImageLayout.ShaderReadOnlyOptimal,
            NewLayout = ImageLayout.ShaderReadOnlyOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = lit,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
        };
        _instance.Vk.CmdPipelineBarrier(
            cmd, PipelineStageFlags.ColorAttachmentOutputBit, destinationStage, 0, 0, null, 0, null, 1, &barrier);
    }

    /// <summary>Moves the lit scene between the read layout the passes sample it in and the general layout
    /// the reflection composite writes it in.</summary>
    private unsafe void LitLayoutBarrier(
        CommandBuffer cmd, Image lit, ImageLayout oldLayout, ImageLayout newLayout,
        AccessFlags srcAccess, AccessFlags dstAccess, PipelineStageFlags srcStage, PipelineStageFlags dstStage)
    {
        ImageMemoryBarrier barrier = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = lit,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
            SrcAccessMask = srcAccess,
            DstAccessMask = dstAccess,
        };
        _instance.Vk.CmdPipelineBarrier(cmd, srcStage, dstStage, 0, 0, null, 0, null, 1, &barrier);
    }

    /// <summary>Reflects the scene into the wet ground and the glossy surfaces, and adds it to the lit
    /// colour before the temporal pass reads it.</summary>
    /// <remarks>
    /// Runs on every adapter -- the march is screen space, no ray query in it -- and only when the
    /// reflection setting asks for marching steps. The two dispatches are separated by the barriers a
    /// read-then-write of the lit image needs: the march samples the lit scene, the composite writes it,
    /// and between them the image moves from the read layout to the general layout a storage write wants
    /// and back again for the temporal resolve.
    /// </remarks>
    private unsafe void DispatchReflections()
    {
        // Not while a debug view is up: it composites into the lit image, and a view that isolates one
        // term of the lighting must not have the reflections of the whole scene added back over it.
        if (_sceneTarget is null || !_reflectionSettings.Marches || _debugView is not DebugView.None)
        {
            return;
        }

        Deferred.SceneTarget target = _sceneTarget;
        CommandBuffer cmd = _frameCommandBuffer;
        int width = (int)target.Extent.Width;
        int height = (int)target.Extent.Height;

        using StageScope reflections = Measure(RenderStage.Reflections);

        // The shading pass's colour write, ordered before the march's sampled read of it.
        LitReadBarrier(cmd, target.LitImage, PipelineStageFlags.ComputeShaderBit);

        // The reflection image was read by the previous frame's composite; separate that from this
        // frame's write.
        OcclusionBarrier(
            cmd, target.ReflectionImage, AccessFlags.ShaderReadBit, AccessFlags.ShaderWriteBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);

        _deferred.Reflection.RecordReflect(
            cmd,
            _frameLoop.CurrentFrame,
            target.LitView,
            target.DepthView,
            target.NormalView,
            target.MaterialView,
            target.ReflectionView,
            width,
            height,
            _viewToWorld,
            _unprojection,
            _reflectionSettings,
            _wetness.Amount,
            _wetness.PuddleCoverage,
            _wetness.PuddleSizeMetres,
            _cameraPosition.X,
            _cameraPosition.Y,
            (int)(_frameCounter % 64UL),
            ReflectionReachMetres);

        // The march's write, before the composite reads it; and the march's sampled read of the lit
        // scene, before the composite writes it -- for which the lit image moves to the general layout.
        OcclusionBarrier(
            cmd, target.ReflectionImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
        LitLayoutBarrier(
            cmd, target.LitImage, ImageLayout.ShaderReadOnlyOptimal, ImageLayout.General,
            AccessFlags.ShaderReadBit, AccessFlags.ShaderWriteBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);

        _deferred.Reflection.RecordComposite(
            cmd,
            _frameLoop.CurrentFrame,
            target.ReflectionView,
            target.LitView,
            width,
            height);

        // Back to the read layout the temporal pass samples the lit scene in.
        LitLayoutBarrier(
            cmd, target.LitImage, ImageLayout.General, ImageLayout.ShaderReadOnlyOptimal,
            AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
            PipelineStageFlags.ComputeShaderBit,
            PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.FragmentShaderBit);
    }

    /// <summary>Meters the lit scene for the eye adaptation, before the present pass opens.</summary>
    private unsafe void RecordExposureMetering()
    {
        if (_sceneTarget is not { } target)
        {
            return;
        }

        // The histogram build and its reduction, which the reference runs every frame and so do we. It
        // had no stage, so the one pass that reads the whole lit image back was invisible on the overlay.
        using StageScope exposure = Measure(RenderStage.Exposure);

        // The shading pass wrote the lit colour and its render pass left it shader-readable for the
        // resolve; this orders that write before the metering's compute read of the same image.
        ImageMemoryBarrier barrier = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = ImageLayout.ShaderReadOnlyOptimal,
            NewLayout = ImageLayout.ShaderReadOnlyOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = target.LitImage,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
        };
        _instance.Vk.CmdPipelineBarrier(
            _frameCommandBuffer,
            PipelineStageFlags.ColorAttachmentOutputBit,
            PipelineStageFlags.ComputeShaderBit,
            0, 0, null, 0, null, 1, &barrier);

        _deferred.AutoExposure.Record(
            _frameCommandBuffer, _frameLoop.CurrentFrame, target.LitView, _captureWidth, _captureHeight);
    }

    /// <summary>Runs the ambient-occlusion compute over the stored depth, writing this frame's occlusion.</summary>
    private unsafe void DispatchOcclusion()
    {
        if (!_aoSettings.Enabled || _sceneTarget is null)
        {
            return;
        }

        using StageScope occlusion = Measure(RenderStage.AmbientOcclusionSearch);

        Deferred.SceneTarget target = _sceneTarget;
        CommandBuffer cmd = _frameCommandBuffer;
        Image ao = target.AoImage;

        // Make the depth the pass just stored visible to the occlusion sample. The render pass left it in
        // the depth read-only layout; this barrier orders the attachment write before the compute read so
        // the sample returns the stored depth rather than memory the store had not reached.
        DepthReadBarrier(cmd, target.DepthImage);

        // This slot's occlusion was last read a frame ago — by that frame's lighting, and by its own
        // occlusion pass as history.
        OcclusionBarrier(cmd, ao, AccessFlags.ShaderReadBit, AccessFlags.ShaderWriteBit,
            PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.ComputeShaderBit,
            PipelineStageFlags.ComputeShaderBit);

        // The temporal history: the OTHER slot's occlusion and depth, written exactly one captured frame
        // ago. Usable only while the two slots are the same shape.
        Deferred.SceneTarget previous = _deferred.PreviousTarget(_frameLoop.CurrentFrame);
        bool previousUsable = _hasPreviousFrame
            && previous.Extent.Width == target.Extent.Width
            && previous.Extent.Height == target.Extent.Height
            && previous.AoView.Handle != 0
            && previous.DepthView.Handle != 0;

        if (previousUsable)
        {
            OcclusionBarrier(cmd, previous.AoImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
                PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
        }

        _deferred.Ssao.Record(
            cmd,
            _frameLoop.CurrentFrame,
            target.DepthView,
            target.AoView,
            previousUsable ? previous.AoView : target.AoView,
            previousUsable ? previous.DepthView : target.DepthView,
            (int)target.AoWidth,
            (int)target.AoHeight,
            _captureWidth,
            _captureHeight,
            _unprojection,
            _aoSettings.Radius,
            _aoSettings.Intensity,
            _viewToWorld,
            _previousViewToWorld,
            new Vector3f(
                _cameraPosition.X - _previousCameraPosition.X,
                _cameraPosition.Y - _previousCameraPosition.Y,
                _cameraPosition.Z - _previousCameraPosition.Z),
            (int)(_frameCounter % 1021UL),
            previousUsable);

        OcclusionBarrier(cmd, ao, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
            PipelineStageFlags.ComputeShaderBit,
            PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.ComputeShaderBit);
    }

    /// <summary>Works out this frame's screen motion from the stored depth and the two frames' cameras.</summary>
    private unsafe void DispatchVelocity()
    {
        _velocityDilated = false;
        _historyDecimated = false;
        if (_sceneTarget is null)
        {
            return;
        }

        using StageScope velocityTiming = Measure(RenderStage.TemporalResolve);

        Deferred.SceneTarget target = _sceneTarget;
        CommandBuffer cmd = _frameCommandBuffer;

        // The very first captured frame has nothing to have moved from. Leaving the image at its cleared
        // zero says exactly that, and is what a consumer should see rather than motion measured against an
        // identity camera that was never real.
        if (!_hasPreviousFrame)
        {
            return;
        }

        DepthReadBarrier(cmd, target.DepthImage);

        Image velocity = target.VelocityImage;
        OcclusionBarrier(
            cmd, velocity, AccessFlags.ShaderReadBit, AccessFlags.ShaderWriteBit,
            PipelineStageFlags.FragmentShaderBit, PipelineStageFlags.ComputeShaderBit);

        _deferred.Velocity.Record(
            cmd,
            _frameLoop.CurrentFrame,
            target.DepthView,
            target.VelocityView,
            _captureWidth,
            _captureHeight,
            _viewToWorld,
            _previousViewToWorld,
            new Vector3f(
                _cameraPosition.X - _previousCameraPosition.X,
                _cameraPosition.Y - _previousCameraPosition.Y,
                _cameraPosition.Z - _previousCameraPosition.Z),
            _unprojection,
            _previousUnprojection);

        OcclusionBarrier(
            cmd, velocity, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.FragmentShaderBit);

        // Everything that moves under its own power stamps its true motion over the camera-derived
        // field, before the dilation spreads it. Without this a running soldier reprojects as though he
        // were the wall behind him, and the temporal accumulation smears him along his own path.
        if (_objectVelocityDraws.Count > 0)
        {
            _deferred.ObjectVelocity.Record(
                cmd,
                target.VelocityView,
                target.VelocityView.Handle,
                target.DepthView,
                target.DepthView.Handle,
                (uint)_captureWidth,
                (uint)_captureHeight,
                _objectVelocityDraws);

            // The pass binds its own pipeline and buffers with raw commands, so nothing the draw path
            // cached about bindings is still true.
            InvalidateBoundState();
        }

        // Straight on into the dilation, which is the form of the motion everything temporal actually
        // reads: a silhouette has to reproject with its own surface's motion rather than the
        // background's, or the temporal filter drags its outline along with whatever stands behind it.
        OcclusionBarrier(
            cmd, target.DilatedVelocityImage, AccessFlags.ShaderReadBit, AccessFlags.ShaderWriteBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
        OcclusionBarrier(
            cmd, target.ClosestDepthImage, AccessFlags.ShaderReadBit, AccessFlags.ShaderWriteBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);

        _deferred.DilateVelocity.Record(
            cmd,
            _frameLoop.CurrentFrame,
            target.DepthView,
            target.VelocityView,
            target.DilatedVelocityView,
            target.ClosestDepthView,
            _captureWidth,
            _captureHeight,
            _unprojection);

        OcclusionBarrier(
            cmd, target.DilatedVelocityImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
        OcclusionBarrier(
            cmd, target.ClosestDepthImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);

        // And then the verdict on the history the update is about to read. It compares against the
        // OTHER slot's closest depth -- the previous frame's -- so it needs that slot to have been built
        // at this size; on the first frames it has not, and the update falls back to trusting the
        // reprojection, which is what it did before this pass existed.
        Deferred.SceneTarget previousFrame = _deferred.PreviousTarget(_frameLoop.CurrentFrame);
        if (previousFrame.Extent.Width == target.Extent.Width
            && previousFrame.Extent.Height == target.Extent.Height
            && previousFrame.DepthView.Handle != 0)
        {
            OcclusionBarrier(
                cmd, target.HistoryRejectionImage, AccessFlags.ShaderReadBit, AccessFlags.ShaderWriteBit,
                PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);

            _deferred.DecimateHistory.Record(
                cmd,
                _frameLoop.CurrentFrame,
                target.DepthView,
                target.DilatedVelocityView,
                previousFrame.DepthView,
                target.HistoryRejectionView,
                _captureWidth,
                _captureHeight,
                _viewToWorld,
                _previousViewToWorld,
                new Vector3f(
                    _cameraPosition.X - _previousCameraPosition.X,
                    _cameraPosition.Y - _previousCameraPosition.Y,
                    _cameraPosition.Z - _previousCameraPosition.Z),
                _unprojection,
                _previousUnprojection);

            OcclusionBarrier(
                cmd, target.HistoryRejectionImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
                PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);

            _historyDecimated = true;
        }

        _velocityDilated = true;
    }

    // Whether this frame ran the dilation. The first captured frame has no motion to dilate, and the
    // history update must then read the cleared raw velocity rather than a stale dilation.
    private bool _velocityDilated;

    // Whether this frame produced a history-rejection verdict. Without one the update trusts the
    // reprojection everywhere, which is what it did before that pass existed.
    private bool _historyDecimated;

    private unsafe void DepthReadBarrier(CommandBuffer cmd, Image depth)
    {
        ImageMemoryBarrier barrier = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = ImageLayout.ShaderReadOnlyOptimal,
            NewLayout = ImageLayout.ShaderReadOnlyOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = depth,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.DepthBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
            SrcAccessMask = AccessFlags.DepthStencilAttachmentWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
        };
        _instance.Vk.CmdPipelineBarrier(
            cmd,
            PipelineStageFlags.LateFragmentTestsBit | PipelineStageFlags.EarlyFragmentTestsBit,
            PipelineStageFlags.ComputeShaderBit,
            0, 0, null, 0, null, 1, &barrier);
    }

    private unsafe void OcclusionBarrier(
        CommandBuffer cmd, Image image, AccessFlags srcAccess, AccessFlags dstAccess,
        PipelineStageFlags srcStage, PipelineStageFlags dstStage)
    {
        ImageMemoryBarrier barrier = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = ImageLayout.General,
            NewLayout = ImageLayout.General,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
            SrcAccessMask = srcAccess,
            DstAccessMask = dstAccess,
        };
        _instance.Vk.CmdPipelineBarrier(cmd, srcStage, dstStage, 0, 0, null, 0, null, 1, &barrier);
    }

    /// <inheritdoc/>
    public unsafe void ResolveScene(in PostProcessSettings post)
    {
        ThrowIfDisposed();
        if (_sceneTarget is null)
        {
            throw new GraphicsDeviceException("Nothing has been captured this frame.");
        }

        // The metering, before the present pass opens: a dispatch cannot be recorded inside a render
        // pass. The histogram it copies down is read a couple of frames later, and the exposure applied
        // below eases toward what the metering reports — the reference's histogram eye adaptation, with
        // r_exposure as the compensation on top of the adapted value rather than the whole of it.
        CommandBuffer cmd = _frameCommandBuffer;

        // The frame's last word on colour before any of the lens work: the temporal pass's stable output
        // when it ran, the raw lit image when it did not.
        ImageView sceneColour = _temporalResolved ? _sceneTarget.UpscaleOutputView : _sceneTarget.LitView;
        bool sceneIsShaded = _debugView is DebugView.None or DebugView.Lighting or DebugView.LampLight;

        // The shutter, first, because everything after it is lens behaviour and a lens sees the smeared
        // picture. The reference orders it the same way: motion blur, then bloom, then the tone map --
        // which is why a bright moving highlight blooms along its own smear rather than from where it
        // no longer is.
        // Everything from the temporal resolve on works at the PRESENTED size: under a render scale the
        // resolve has already reconstructed to it, and the raw-lit fallback simply runs 1:1.
        int postWidth = _temporalResolved ? _presentWidth : _captureWidth;
        int postHeight = _temporalResolved ? _presentHeight : _captureHeight;

        // The motion-blur image is presented-size; without the temporal resolve a scaled frame would
        // fill only a corner of it and the resolve would read the rest as garbage. TAA off plus a render
        // scale is not a sensible configuration, and it simply skips the shutter.
        bool postSizedChainUsable = _temporalResolved
            || (_presentWidth == _captureWidth && _presentHeight == _captureHeight);

        bool motionBlurred = false;
        if (post.MotionBlur > 0.001f && sceneIsShaded && postSizedChainUsable && _sceneTarget is { } motionTarget)
        {
            using StageScope motionTiming = Measure(RenderStage.CameraEffects);
            LitReadBarrier(cmd, motionTarget.LitImage, PipelineStageFlags.ComputeShaderBit);
            _deferred.MotionBlur.Record(
                cmd,
                _frameLoop.CurrentFrame,
                motionTarget,
                sceneColour,
                postWidth,
                postHeight,
                post.MotionBlur,
                MotionBlurPass.MaxPercentOfScreenWidth,
                post.FrameDeltaSeconds,
                _unprojection.Near,
                _unprojection.Far);

            sceneColour = motionTarget.MotionBlurredView;
            motionBlurred = true;
        }

        // The lens scatter, built from the scene before it is tone mapped. A mask debug view is a
        // code value rather than radiance and gets neither this nor the vignette.
        bool bloomRan = false;
        if (post.WantsBloom && _sceneTarget is { } bloomTarget && sceneIsShaded)
        {
            using StageScope bloomTiming = Measure(RenderStage.Bloom);
            _deferred.Bloom.Resize(postWidth, postHeight);
            bloomTarget.WriteBloom(_deferred.Bloom.ResultView(_frameLoop.CurrentFrame));

            if (!motionBlurred)
            {
                LitReadBarrier(cmd, bloomTarget.LitImage, PipelineStageFlags.ComputeShaderBit);
            }

            _deferred.Bloom.Record(cmd, _frameLoop.CurrentFrame, sceneColour, post.BloomIntensity, post.BloomThreshold);
            bloomRan = true;
        }

        float adaptedExposure = 1f;
        if (post.AutoExposure && _debugView is DebugView.None or DebugView.Lighting or DebugView.LampLight)
        {
            RecordExposureMetering();
            adaptedExposure = _deferred.AutoExposure.Adapt(_frameLoop.CurrentFrame, 1f / 60f);
            _lastAdaptedExposure = adaptedExposure;
        }

        EnsurePresentPass();

        // Exposure and the tone-map curve are the one fullscreen draw that resolves the scene into the back
        // buffer; it reports as ToneMapGrade. It must close before the UI draws into the same present pass,
        // which the method returning here does. Bloom, camera effects and spatial AA are not ported, so
        // there is nothing to fold into it and those stages stay dark.
        using StageScope tone = Measure(RenderStage.ToneMapGrade);

        Vk vk = _instance.Vk;
        vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _deferred.Resolve.Pipeline);

        // The stable accumulated colour when the temporal pass ran this frame; the raw lit colour when
        // nothing accumulated and the lit image is already the final word.
        DescriptorSet set = motionBlurred
            ? _sceneTarget.ResolveSetMotionBlur
            : _temporalResolved ? _sceneTarget.ResolveSetTsr : _sceneTarget.ResolveSet;
        vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _deferred.Resolve.PipelineLayout, 0, 1, &set, 0, null);
        InvalidateBoundState();

        // A mask view is a code value rather than radiance, so it keeps neither the curve nor the lens.
        bool maskView = _debugView is not DebugView.None and not DebugView.Lighting and not DebugView.LampLight;

        float* parameters = stackalloc float[8]
        {
            post.Exposure * adaptedExposure,
            (int)post.ToneMap,
            post.WhitePoint,

            // The mask debug views are code values, not radiance; the neutral-grey lighting view is the
            // one debug view that is still a shaded scene and keeps the full chain.
            maskView ? 1f : 0f,

            maskView ? 0f : post.Vignette,
            _captureHeight / (float)Math.Max(1, _captureWidth),
            bloomRan ? 1f : 0f,
            0f,
        };
        vk.CmdPushConstants(cmd, _deferred.Resolve.PipelineLayout, ShaderStageFlags.FragmentBit, 0, sizeof(float) * 8, parameters);

        // The resolve fills the whole back buffer regardless of any scene sub-viewport that was set.
        ApplyViewportAndScissor(cmd, new Viewport(0, 0, (int)_presentTarget.Extent.Width, (int)_presentTarget.Extent.Height));
        vk.CmdDraw(cmd, 3, 1, 0, 0);
    }

    /// <inheritdoc/>
    public void SetLighting(in SceneLighting lighting)
    {
        ThrowIfDisposed();
        _lighting = lighting;
    }

    /// <inheritdoc/>
    public void SetMaterialOverride(in SurfaceMaterial material)
    {
        ThrowIfDisposed();
        _material = material;
    }

    /// <inheritdoc/>
    public void SetLightmapScale(float scale)
    {
        ThrowIfDisposed();
        _lightmapScale = scale;
    }

    // Uploads the field the probe collect handed over, into this frame's command buffer. Called at the
    // top of a scene capture, before any render pass opens, so the transfer is a plain pre-pass copy on
    // the frame's own timeline; the staging is freed once the frames that sample it have cycled.
    private void ApplyPendingProbeVolume()
    {
        if (_pendingProbeVolume is not { } volume)
        {
            return;
        }

        _pendingProbeVolume = null;
        _deferred.Probes.Update(volume, _frameCommandBuffer, QueueDeletion);
    }

    /// <inheritdoc/>
    /// <remarks>Uploads the field into the probe volumes the lighting pass samples; null clears it.</remarks>
    public void SetIrradianceVolume(IrradianceVolume? volume)
    {
        ThrowIfDisposed();
        if (volume is null || volume.ProbeCount == 0)
        {
            _pendingProbeVolume = null;
            _deferred.Probes.Clear();
            _hasProbeField = false;
            return;
        }

        // Not uploaded here: this is called during the probe collect, before the frame's command buffer is
        // open. Held instead and uploaded at the top of the frame (ApplyPendingProbeVolume) into that
        // command buffer, so the transfer shares the frame's submission and in-order timeline rather than
        // a private submit that would write the field a frame in flight is still reading -- which lost the
        // device under a moving camera. The last field set before a frame opens is the one that frame uses.
        _pendingProbeVolume = volume;
        _hasProbeField = true;
    }

    /// <inheritdoc/>
    public void SetFirstPersonGeometry(bool firstPerson)
    {
        ThrowIfDisposed();
        if (_firstPersonGeometry != firstPerson)
        {
            // Ends the foreground depth sliver ClearDepth put the rig into, so the world's own draws --
            // and, more importantly, the depth the post passes read once this pass closes -- are back on
            // the full range. See ClearDepth for why the sliver exists instead of a cleared buffer.
            ApplyForegroundDepthRange(firstPerson);
        }

        _firstPersonGeometry = firstPerson;
    }

    // Whether the draws being recorded are the view model rather than the world. Read by the forward
    // draw path, which puts it in the lit target's alpha so the passes that read that colour back can
    // tell the two apart.
    private bool _firstPersonGeometry;

    /// <inheritdoc/>
    public void SetIrradianceVolumeSettings(in IrradianceVolumeSettings settings)
    {
        ThrowIfDisposed();
        _volumeSettings = settings;
    }

    /// <inheritdoc/>
    /// <remarks>Kept for the occlusion compute pass that runs after the deferred pass; the lighting applies it a frame later.</remarks>
    public void SetAmbientOcclusion(in AmbientOcclusionSettings settings)
    {
        ThrowIfDisposed();
        _aoSettings = settings;
    }

    /// <inheritdoc/>
    /// <remarks>The near-field atlas the game traced this frame; the lighting samples it in place of the flat fill.</remarks>
    public void SetScreenIndirect(TextureHandle indirect, float strength, bool fullResolution)
    {
        ThrowIfDisposed();
        _screenIndirect = indirect;
        _screenIndirectStrength = strength;
        _screenIndirectFullResolution = fullResolution;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Keeps the depth unprojection and view rotation the probe and shadow lookups both need, and composes
    /// each cascade's world-to-clip matrix — the orthographic projection after the world-to-light rotation,
    /// camera-relative so single precision holds up at range — for the lighting to project into. Sizes the
    /// shadow map to the cascade count and resolution.
    /// </remarks>
    public void SetShadows(
        in ShadowSettings settings,
        ReadOnlySpan<ShadowCascade> cascades,
        in DepthUnprojection unprojection,
        Matrix3f viewToWorld)
    {
        ThrowIfDisposed();
        _unprojection = unprojection;
        _viewToWorld = viewToWorld;
        _shadowSettings = settings;
        _shadowCascadeCount = Math.Min(cascades.Length, ShadowSettings.MaxCascades);

        for (int i = 0; i < _shadowCascadeCount; i++)
        {
            _cascadeSplits[i] = cascades[i].SplitDistance;
            _cascadeMatrices[i] = ComposeCascade(cascades[i]);
            _cascadeLightRotations[i] = cascades[i].LightRotation;
        }

        if (_shadowCascadeCount > 0 && _deferredEnabled)
        {
            _deferred.ShadowMap.Ensure(settings.Resolution, _shadowCascadeCount);
        }
    }

    /// <summary>Composes a cascade's camera-relative-world to light-clip matrix.</summary>
    /// <remarks>
    /// <b>The rotation goes in untransposed, and the two engine conventions are why that looks wrong.</b>
    /// <see cref="Matrix3f.Transform(Vector3f)"/> is a row-vector multiply, so dotting a cascade's light axes with a
    /// position is spelled <c>LightRotation.Transposed().Transform(p)</c> — which is how
    /// <see cref="ShadowCascade.ToLightClip"/> reads. A <see cref="Matrix4f"/> is a column-vector multiply
    /// whose rows ARE the axes, so the same rotation is spelled <c>CreateRigid(LightRotation, ...)</c>,
    /// with no transpose. This composed the Matrix3f spelling into a Matrix4f and so applied the INVERSE
    /// light rotation: the shadow map was rasterised through <see cref="Camera3D.CreateModelView(Vector3d, Matrix3f)"/>, which
    /// gets this right, and the lighting then looked it up through a frame rotated the other way. Shadows
    /// landed somewhere other than their casters, by an amount that grew with a point's distance from the
    /// camera and therefore slid across the world as the camera moved. The OpenGL backend never had it —
    /// it composes exactly the expression below — and the ray-traced path does not use this matrix at all,
    /// which is why the default preset hid it.
    /// </remarks>
    private static Matrix4f ComposeCascade(in ShadowCascade cascade) =>

        // Camera-relative, so the light view carries no translation.
        cascade.Projection * Matrix4f.CreateRigid(cascade.LightRotation, default);

    /// <inheritdoc/>
    public unsafe bool BeginShadowCascade(int cascade)
    {
        ThrowIfDisposed();
        if (!_deferredEnabled || _shadowCascadeCount <= 0 || cascade < 0 || cascade >= _shadowCascadeCount)
        {
            return false;
        }

        Deferred.ShadowMap map = _deferred.ShadowMap;
        if (!map.Ready)
        {
            return false;
        }

        // Nothing reads this map on a frame whose sun is traced -- the deferred lighting is its only
        // consumer and it takes the traced mask instead -- so rasterising every cascade into it was work
        // the frame threw away. Skipped, that is half a millisecond of a thirteen millisecond frame.
        //
        // The map itself stays allocated at full size, because r_rt and r_rt_shadows are live keys and a
        // map that had to be reallocated on the keystroke would stall on it. And the guard is deliberately
        // the same predicate DispatchSunShadow uses, read this frame, so the two agree WITHIN the frame:
        // the moment either cvar goes off, or the traced world stops being ready, this returns true again
        // and the cascades are drawn for the very frame that needs them. Requiring last frame to have
        // traced as well costs one warm-up frame when the traced world first comes up, and buys never
        // being caught between the two.
        if (_sunShadowTracedLastFrame && WillTraceSunShadow())
        {
            return false;
        }

        EnsureFrame();
        FlushComputeBeforePass();

        uint size = (uint)map.Resolution;
        ClearValue clear = new() { DepthStencil = new ClearDepthStencilValue(1.0f, 0) };
        RenderPassBeginInfo beginInfo = new()
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = map.RenderPass,
            Framebuffer = map.Framebuffer(cascade),
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D(size, size)),
            ClearValueCount = 1,
            PClearValues = &clear,
        };

        _instance.Vk.CmdBeginRenderPass(_frameCommandBuffer, &beginInfo, SubpassContents.Inline);
        _inShadowPass = true;
        _shadowCascade = cascade;
        ApplyViewportAndScissor(_frameCommandBuffer, new Viewport(0, 0, (int)size, (int)size));

        // Each cascade is its own range; the profiler sums them into one Shadows figure for the frame.
        BeginStageTiming(RenderStage.Shadows);
        return true;
    }

    /// <inheritdoc/>
    public void EndShadowCascade()
    {
        ThrowIfDisposed();
        if (!_inShadowPass)
        {
            return;
        }

        EndStageTiming();
        _instance.Vk.CmdEndRenderPass(_frameCommandBuffer);
        _inShadowPass = false;
    }

    /// <inheritdoc/>
    /// <remarks>Only the camera position and view rotation are taken — the temporal resolve itself is not yet ported.</remarks>
    public void SetTemporalAntiAliasing(
        in TemporalAaSettings settings,
        in Matrix4f projection,
        Vector3f cameraPosition,
        Matrix3f viewToWorld)
    {
        ThrowIfDisposed();
        _taaSettings = settings;
        _cameraPosition = cameraPosition;
        _viewToWorld = viewToWorld;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Held rather than acted on here: it is two vectors in the lighting pass's own uniform, so the whole
    /// of it arrives with the frame's GI parameters and costs no binding, no pass and no extra upload.
    /// </remarks>
    public void SetWetness(in WetnessSettings wetness, ReadOnlySpan<WetShelter> shelters)
    {
        ThrowIfDisposed();
        _wetness = wetness;

        _wetShelterCount = Math.Min(shelters.Length, WetShelter.Capacity);
        shelters[.._wetShelterCount].CopyTo(_wetShelters);
    }

    /// <inheritdoc/>
    /// <remarks>Only the camera position is taken — the fog march itself is not yet ported.</remarks>
    public void SetVolumetricFog(
        in VolumetricFogSettings fog,
        Vector3f cameraPosition,
        ReadOnlySpan<LocalFogVolume> volumes)
    {
        ThrowIfDisposed();
        _cameraPosition = cameraPosition;
    }

    private unsafe void BeginGeometryPass()
    {
        if (_sceneTarget is null)
        {
            throw new GraphicsDeviceException("The scene target is not configured.");
        }

        ClearValue* clears = stackalloc ClearValue[5];
        for (int i = 0; i < 4; i++)
        {
            clears[i] = new ClearValue { Color = new ClearColorValue(0f, 0f, 0f, 0f) };
        }

        clears[4] = new ClearValue { DepthStencil = new ClearDepthStencilValue(0.0f, 0) };

        RenderPassBeginInfo beginInfo = new()
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _deferred.GeometryPass,
            Framebuffer = _sceneTarget.GeometryFramebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), _sceneTarget.Extent),
            ClearValueCount = 5,
            PClearValues = clears,
        };

        FlushComputeBeforePass();
        _instance.Vk.CmdBeginRenderPass(_frameCommandBuffer, &beginInfo, SubpassContents.Inline);
        _deferredPassActive = true;
    }

    private unsafe void BeginShadingPass()
    {
        if (_sceneTarget is null)
        {
            throw new GraphicsDeviceException("The scene target is not configured.");
        }

        // The five the geometry pass filled are loaded rather than cleared, so only the scene colour has a
        // clear value -- but a render pass wants one entry per attachment regardless.
        ClearValue* clears = stackalloc ClearValue[6];
        for (int i = 0; i < 5; i++)
        {
            clears[i] = default;
        }

        clears[5] = new ClearValue
        {
            Color = new ClearColorValue(_clearColour.R / 255f, _clearColour.G / 255f, _clearColour.B / 255f, 1f),
        };

        RenderPassBeginInfo beginInfo = new()
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _deferred.ShadingPass,
            Framebuffer = _sceneTarget.ShadingFramebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), _sceneTarget.Extent),
            ClearValueCount = 6,
            PClearValues = clears,
        };

        _instance.Vk.CmdBeginRenderPass(_frameCommandBuffer, &beginInfo, SubpassContents.Inline);
        _deferredPassActive = true;
        ApplyViewportAndScissor(_frameCommandBuffer, _sceneViewport);
    }

    /// <summary>
    /// Everything that needs a finished G-buffer and cannot run inside a render pass.
    /// </summary>
    /// <remarks>
    /// The whole reason the deferred path is two passes rather than one. A ray query is only available to
    /// a compute shader here, and a dispatch cannot be recorded inside a render pass, so anything that
    /// traces against the scene has to happen in the gap between the pass that fills the G-buffer and the
    /// pass that shades it. The sun's shadow is the first thing to live here; the indirect-lighting
    /// gathers will join it.
    /// </remarks>
    private void RecordBetweenPasses()
    {
        // The ray-traced sun shadow is the compute that lives in the gap between the two passes. It is a
        // shadow, so it reports under Shadows alongside any rasterised cascades.
        using StageScope shadow = Measure(RenderStage.Shadows);
        DispatchSunShadow();
    }

    /// <summary>Traces how much of each pixel's chosen lamp reaches it, for the shading to read.</summary>
    /// <remarks>
    /// One ray per pixel at half resolution, whatever the level's lamp count -- see LampShadowPass for
    /// why that is the shape rather than a ray per lamp inside the shading.
    /// </remarks>
    private unsafe void DispatchLampShadow()
    {
        if (_sceneTarget is null || _deferred.LampShadow is null)
        {
            return;
        }

        if (_localLightCount <= 0 || RenderCVars.LocalLightShadows.Value == 0)
        {
            return;
        }

        if (_worldScene is not { IsReady: true } scene || scene.TopLevel is not { } topLevel)
        {
            return;
        }

        Deferred.SceneTarget target = _sceneTarget;
        CommandBuffer cmd = _frameCommandBuffer;

        // The denoiser read this on the previous frame; separate that read from the write about to
        // happen.
        OcclusionBarrier(
            cmd, target.LampVisibilityImage, AccessFlags.ShaderReadBit, AccessFlags.ShaderWriteBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);

        int floats = DeferredRenderer.LampUniformSize / sizeof(float);
        float* packed = stackalloc float[floats];
        PackLamps(new Span<float>(packed, floats));

        _deferred.LampShadow.Record(
            cmd,
            _frameLoop.CurrentFrame,
            target.DepthView,
            target.NormalView,
            topLevel.Handle,
            target.LampVisibilityView,
            target.LampVisibilityWidth,
            target.LampVisibilityHeight,
            _viewToWorld,
            _cameraPosition,
            _unprojection,
            (int)(_frameCounter % 64UL),
            new ReadOnlySpan<float>(packed, floats));

        DenoiseLampShadow(target, cmd);
    }

    /// <summary>Cleans up the lamp trace, so what the shading reads is a shadow rather than grain.</summary>
    /// <remarks>
    /// The reference's two halves in the reference's order: accumulate onto a reprojected history whose
    /// value is first clamped to what this frame's neighbourhood says, then filter what is left where
    /// the signal's own variance says there is still noise in it. See LampDenoisePass for why that order
    /// is what makes a lamp being switched answer immediately rather than resolving over a second.
    /// </remarks>
    private void DenoiseLampShadow(Deferred.SceneTarget target, CommandBuffer cmd)
    {
        if (_deferred.LampDenoise is not { } denoise)
        {
            return;
        }

        // The other slot's accumulation, which is the previous frame's. A slot of a different shape --
        // the frame after a resize -- holds an image of the wrong size, so this frame starts afresh
        // rather than reprojecting onto nonsense.
        Deferred.SceneTarget previous = _deferred.PreviousTarget(_frameLoop.CurrentFrame);
        bool hasHistory = !ReferenceEquals(previous, target)
            && previous.Extent.Width == target.Extent.Width
            && previous.Extent.Height == target.Extent.Height
            && previous.LampDenoisedView.Handle != 0;
        ImageView history = hasHistory ? previous.LampDenoisedView : target.LampDenoisedView;

        // The trace's write, before the accumulation reads it.
        OcclusionBarrier(
            cmd, target.LampVisibilityImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);

        if (hasHistory)
        {
            OcclusionBarrier(
                cmd, previous.LampDenoisedImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
                PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
        }

        OcclusionBarrier(
            cmd, target.LampDenoisedImage, AccessFlags.ShaderReadBit, AccessFlags.ShaderWriteBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);

        // Half a pixel of full-resolution jitter is a quarter of one here, and the buffers this pass
        // works in are half the frame -- so the offset has to be halved with them or the depth it looks
        // up belongs to the wrong sample and a still camera shimmers.
        float jitterX = _taaSettings.JitterX * 0.5f;
        float jitterY = _taaSettings.JitterY * VerticalJitterSign * 0.5f;

        // Both halves still run with this off -- the accumulation carries the frame count and the filter
        // carries the depth the shading's upsample reads, and neither is optional. What stops is the
        // filtering itself, so the raw trace can be measured against its own denoised self in one build.
        bool filtering = RenderCVars.LampDenoise.Value != 0;

        denoise.RecordTemporal(
            cmd,
            _frameLoop.CurrentFrame,
            target.LampVisibilityView,
            target.DepthView,
            history,
            target.LampDenoisedView,
            target.LampVisibilityWidth,
            target.LampVisibilityHeight,
            _viewToWorld,
            _previousViewToWorld,
            new Vector3f(
                _cameraPosition.X - _previousCameraPosition.X,
                _cameraPosition.Y - _previousCameraPosition.Y,
                _cameraPosition.Z - _previousCameraPosition.Z),
            _unprojection,
            _previousUnprojection,
            hasHistory,
            jitterX,
            jitterY,
            _previousJitterX * 0.5f,
            _previousJitterY * 0.5f,
            filtering);

        OcclusionBarrier(
            cmd, target.LampDenoisedImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
        OcclusionBarrier(
            cmd, target.LampFilteredImage, AccessFlags.ShaderReadBit, AccessFlags.ShaderWriteBit,
            PipelineStageFlags.FragmentShaderBit, PipelineStageFlags.ComputeShaderBit);

        denoise.RecordSpatial(
            cmd,
            _frameLoop.CurrentFrame,
            target.LampDenoisedView,
            target.DepthView,
            target.NormalView,
            target.LampFilteredView,
            target.LampVisibilityWidth,
            target.LampVisibilityHeight,
            _viewToWorld,
            _unprojection,
            jitterX,
            jitterY,
            (int)(_frameCounter % 64UL),
            filtering);

        // And into the shading, which is the only thing that reads it.
        OcclusionBarrier(
            cmd, target.LampFilteredImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.FragmentShaderBit);
    }

    /// <summary>Whether this frame's sun shadow comes from the ray query rather than the cascaded map.</summary>
    /// <remarks>
    /// The conditions <see cref="DispatchSunShadow"/> returns on, asked ahead of it: the cascades are
    /// rasterised earlier in the frame than the trace is dispatched, so the question has to be answerable
    /// before the answer is produced. Kept beside it so the two cannot drift apart.
    /// </remarks>
    private bool WillTraceSunShadow()
        => _deferred.SunShadow is not null
            && _shadowSettings.Enabled
            && _shadowSettings.TracedSun
            && _shadowSettings.SunLit
            && _worldScene is { IsReady: true } scene
            && scene.TopLevel is not null;

    /// <inheritdoc/>
    public bool SunShadowTraced => WillTraceSunShadow();

    /// <summary>Traces the sun's shadow against the scene, for the shading pass to read this same frame.</summary>
    private unsafe void DispatchSunShadow()
    {
        if (_sceneTarget is null || _deferred.SunShadow is null)
        {
            return;
        }

        // The app's word governs: shadows off means no sun shadow of any kind, the software mode
        // leaves the sun to the cascades, and a sun faded to nothing shadows nothing — the trace never
        // runs, and the lighting's flag stays off.
        if (!_shadowSettings.Enabled || !_shadowSettings.TracedSun || !_shadowSettings.SunLit)
        {
            return;
        }

        if (_worldScene is not { IsReady: true } scene || scene.TopLevel is not { } topLevel)
        {
            return;
        }

        Deferred.SceneTarget target = _sceneTarget;
        CommandBuffer cmd = _frameCommandBuffer;

        // The shading pass sampled this mask on the previous frame; separate that read from the write
        // about to happen.
        OcclusionBarrier(
            cmd, target.SunShadowImage, AccessFlags.ShaderReadBit, AccessFlags.ShaderWriteBit,
            PipelineStageFlags.FragmentShaderBit, PipelineStageFlags.ComputeShaderBit);

        _deferred.SunShadow.Record(
            cmd,
            _frameLoop.CurrentFrame,
            target.DepthView,
            target.NormalView,
            topLevel.Handle,
            target.SunShadowRawView,

            // The cut-out shapes, and where to find the record that names one. Without a mask array there
            // is nothing for a candidate to test against, so the pass is handed the white texture and a
            // zero address and takes every hit as it stands -- which is the behaviour it had before.
            _opacityMasks?.View ?? _textures[_whiteTexture.Id].View,
            _opacityMasks is null ? 0UL : scene.RecordAddress,
            _captureWidth,
            _captureHeight,
            _viewToWorld,
            _cameraPosition,
            WorldSunDirection(),
            _unprojection,
            RenderCVars.SunAngle.Value,

            // Cycled over a short period rather than handed the raw counter: the ray direction is built
            // from this through a fract(), and a float large enough to be a frame count has lost the
            // low bits that were the whole point of varying it.
            (float)(_frameCounter % 64UL));

        // The reconstruction, before anything reads the mask. One ray a pixel answers lit or not lit, so
        // the penumbra arrives as noise that merely averages to the right thing; this spreads each
        // pixel's answer over the width its own occluder distance says the penumbra actually has. The
        // reference denoises here too, ahead of its temporal pass rather than instead of it.
        OcclusionBarrier(
            cmd, target.SunShadowRawImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
        OcclusionBarrier(
            cmd, target.SunShadowImage, AccessFlags.ShaderReadBit, AccessFlags.ShaderWriteBit,
            PipelineStageFlags.FragmentShaderBit, PipelineStageFlags.ComputeShaderBit);

        // The other slot's accumulation, which is last frame's -- the same one the temporal pass below
        // will reproject onto. The denoise only reads how many frames it holds, to decide how much of the
        // penumbra it still has to reconstruct spatially, so a slot that does not match this frame's size
        // simply reads this frame's own cleared history and the reconstruction runs at full width.
        Deferred.SceneTarget previousForDenoise = _deferred.PreviousTarget(_frameLoop.CurrentFrame);
        ImageView denoiseHistory = previousForDenoise.Extent.Width == target.Extent.Width
            && previousForDenoise.Extent.Height == target.Extent.Height
            && previousForDenoise.ShadowHistoryView.Handle != 0
                ? previousForDenoise.ShadowHistoryView
                : target.ShadowHistoryView;

        // That history was written by an earlier frame's accumulate, so it has to be made visible to this
        // read the same way the accumulate below makes it visible to its own.
        if (!ReferenceEquals(previousForDenoise, target))
        {
            OcclusionBarrier(
                cmd, previousForDenoise.ShadowHistoryImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
                PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
        }

        _deferred.SunShadowDenoise?.Record(
            cmd,
            _frameLoop.CurrentFrame,
            target.SunShadowRawView,
            target.DepthView,
            denoiseHistory,
            target.SunShadowView,
            _captureWidth,
            _captureHeight,
            _unprojection,
            RenderCVars.SunAngle.Value,
            RenderCVars.ShadowAccumulate.Value);

        // And the temporal half of the same denoiser. Eight spatial samples of a binary signal still
        // leave a mask that differs every frame, and leaving that to the filter downstream cannot work:
        // it reaches that filter as a colour that changed, a changed colour reads as a stale history, and
        // rejecting the history is exactly refusing to average. So the mask is averaged here instead,
        // before it is ever turned into light.
        float shadowFrames = RenderCVars.ShadowAccumulate.Value;
        if (_deferred.SunShadowAccumulate is { } accumulate && shadowFrames > 1f)
        {
            Deferred.SceneTarget previousShadow = _deferred.PreviousTarget(_frameLoop.CurrentFrame);
            bool hasShadowHistory = previousShadow.Extent.Width == target.Extent.Width
                && previousShadow.Extent.Height == target.Extent.Height
                && previousShadow.ShadowHistoryView.Handle != 0;
            ImageView previousHistory = hasShadowHistory
                ? previousShadow.ShadowHistoryView
                : target.ShadowHistoryView;

            OcclusionBarrier(
                cmd, target.SunShadowImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
            if (hasShadowHistory)
            {
                OcclusionBarrier(
                    cmd, previousShadow.ShadowHistoryImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
                    PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
            }

            OcclusionBarrier(
                cmd, target.ShadowHistoryImage, AccessFlags.ShaderReadBit, AccessFlags.ShaderWriteBit,
                PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);

            accumulate.Record(
                cmd,
                _frameLoop.CurrentFrame,
                target.DepthView,
                previousHistory,
                target.SunShadowView,
                target.ShadowHistoryView,
                _captureWidth,
                _captureHeight,
                _viewToWorld,
                _previousViewToWorld,
                new Vector3f(
                    _cameraPosition.X - _previousCameraPosition.X,
                    _cameraPosition.Y - _previousCameraPosition.Y,
                    _cameraPosition.Z - _previousCameraPosition.Z),
                _unprojection,
                _previousUnprojection,
                shadowFrames,
                hasShadowHistory,
                _taaSettings.JitterX,
                _taaSettings.JitterY * VerticalJitterSign,
                _previousJitterX,
                _previousJitterY);

            OcclusionBarrier(
                cmd, target.ShadowHistoryImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
                PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
        }

        OcclusionBarrier(
            cmd, target.SunShadowImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.FragmentShaderBit);

        _sunShadowTraced = true;
    }


    /// <summary>The world direction towards the sun, from the view-space one the renderer seam hands over.</summary>
    private Vector3f WorldSunDirection()
    {
        Vector3f viewSun = _lighting.SunDirection;
        Matrix3f m = _viewToWorld;
        Vector3f world = new(
            (m.M11 * viewSun.X) + (m.M12 * viewSun.Y) + (m.M13 * viewSun.Z),
            (m.M21 * viewSun.X) + (m.M22 * viewSun.Y) + (m.M23 * viewSun.Z),
            (m.M31 * viewSun.X) + (m.M32 * viewSun.Y) + (m.M33 * viewSun.Z));

        float length = MathF.Sqrt((world.X * world.X) + (world.Y * world.Y) + (world.Z * world.Z));
        return length > 1e-5f
            ? new Vector3f(world.X / length, world.Y / length, world.Z / length)
            : new Vector3f(0f, 0f, 1f);
    }

    private unsafe void RecordLighting()
    {
        if (_sceneTarget is null)
        {
            return;
        }

        using StageScope lighting = Measure(RenderStage.Lighting);

        Vk vk = _instance.Vk;
        CommandBuffer cmd = _frameCommandBuffer;

        WriteGiUniform();

        vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _deferred.Lighting.Pipeline);

        DescriptorSet set = _sceneTarget.LightingSet;
        vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _deferred.Lighting.PipelineLayout, 0, 1, &set, 0, null);
        InvalidateBoundState();

        float* light = stackalloc float[12];
        light[0] = _lighting.SunDirection.X;
        light[1] = _lighting.SunDirection.Y;
        light[2] = _lighting.SunDirection.Z;
        light[3] = (int)_lighting.Model;
        light[4] = _lighting.SunColour.X;
        light[5] = _lighting.SunColour.Y;
        light[6] = _lighting.SunColour.Z;
        light[7] = _lighting.Scale;
        light[8] = _lighting.AmbientColour.X;
        light[9] = _lighting.AmbientColour.Y;
        light[10] = _lighting.AmbientColour.Z;
        light[11] = (int)_debugView;
        vk.CmdPushConstants(cmd, _deferred.Lighting.PipelineLayout, ShaderStageFlags.FragmentBit, 0, sizeof(float) * 12, light);

        // The light covers the whole G-buffer regardless of the geometry viewport.
        ApplyViewportAndScissor(cmd, new Viewport(0, 0, (int)_sceneTarget.Extent.Width, (int)_sceneTarget.Extent.Height));
        vk.CmdDraw(cmd, 3, 1, 0, 0);

        // The screen-GI atlas is consumed here and then forgotten, which is the same contract the OpenGL
        // path states: a frame that gathers nothing lights from nothing. Holding it instead meant that the
        // moment the gather stopped running -- r_screen_gi 0, a card probe, a frame that captured nothing --
        // the lighting went on sampling the LAST atlas that was written, a screen-space image of the
        // indirect frozen at that frame's camera and dragged across the world by every later one. That is
        // what "turning the screen GI off makes the picture ghost" was.
        _screenIndirect = default;
        _screenIndirectStrength = 0f;
        _screenIndirectFullResolution = false;

        // The lamps are NOT cleared here, though the same contract applies to them -- a frame that
        // submits none must light from none. The clear is deferred to EndSceneCapture instead, because
        // the forward subpass that draws the first-person rig runs AFTER this one and reads the lamp set
        // through LampLightAtEye: cleared here, the weapon saw zero lamps and fell to a night silhouette
        // while the wall behind it was lit by the very fittings it could not see. EndSceneCapture clears
        // them once the forward draws that need them are done, which keeps the no-carry-over guarantee.
    }

    /// <summary>Fills this frame's GI uniform with the probe-field parameters and points the set at it.</summary>
    private unsafe void WriteGiUniform()
    {
        int frame = _frameLoop.CurrentFrame;
        Memory.VulkanBuffer gi = _deferred.GiUniform(frame);
        ProbeVolumes probes = _deferred.Probes;
        bool enabled = _hasProbeField && probes.HasData && _volumeSettings.Enabled;

        float spacing = probes.Spacing;
        float intensity = MathF.Max(_volumeSettings.Intensity, 0f);
        Matrix3f m = _viewToWorld;

        float* u = stackalloc float[DeferredRenderer.GiUniformSize / sizeof(float)];

        // uUnproject: xScale, yScale, near, far.
        u[0] = _unprojection.XScale; u[1] = _unprojection.YScale; u[2] = _unprojection.Near; u[3] = _unprojection.Far;

        // uProbeOrigin: xyz origin, w whether the field is on.
        u[4] = probes.Origin.X; u[5] = probes.Origin.Y; u[6] = probes.Origin.Z; u[7] = enabled ? 1f : 0f;

        // uProbeInvCount: xyz one-over-count, w spacing.
        u[8] = probes.InverseCount.X; u[9] = probes.InverseCount.Y; u[10] = probes.InverseCount.Z; u[11] = spacing;

        // uProbeMisc: half-spacing normal offset, blend, light scale, and whether the sun's shadow comes
        // from a ray rather than from the cascades. The world is accumulated from what has been drawn, so
        // for the first frames of a level there is nothing to trace and the shadow map still answers.
        u[12] = spacing * 0.5f; u[13] = Math.Clamp(_volumeSettings.Blend, 0f, 1f);
        u[14] = MathF.Max(_lighting.Scale, 0f);
        u[15] = _sunShadowTraced ? 1f : 0f;

        // uProbeSky: the sky colour the grey visibility is tinted by, times its intensity.
        u[16] = _volumeSettings.SkyColour.X * intensity;
        u[17] = _volumeSettings.SkyColour.Y * intensity;
        u[18] = _volumeSettings.SkyColour.Z * intensity;
        u[19] = 0f;

        // uCamera: the camera's world position, added to the rotated view position.
        u[20] = _cameraPosition.X; u[21] = _cameraPosition.Y; u[22] = _cameraPosition.Z; u[23] = 0f;

        // uGiParams: the screen-GI strength (0 disables it), whether the atlas is full-resolution, and
        // whether ambient occlusion is on.
        bool screenGi = _screenIndirectStrength > 0f && _screenIndirect.IsValid;
        u[24] = screenGi ? _screenIndirectStrength : 0f;
        u[25] = screenGi && _screenIndirectFullResolution ? 1f : 0f;
        u[26] = _aoSettings.Enabled ? 1f : 0f;

        // The camera's height from the planet's centre, so the lighting reads the atmosphere's tables with
        // the same parameterisation the passes that built them used.
        u[27] = Sky.SkyAtmosphere.ViewHeightKm(2f);

        // uViewToWorld: the row-major view-to-world rotation, written column-major as a mat4.
        u[28] = m.M11; u[29] = m.M21; u[30] = m.M31; u[31] = 0f;
        u[32] = m.M12; u[33] = m.M22; u[34] = m.M32; u[35] = 0f;
        u[36] = m.M13; u[37] = m.M23; u[38] = m.M33; u[39] = 0f;
        u[40] = 0f; u[41] = 0f; u[42] = 0f; u[43] = 1f;

        // uCascadeSplits: the view depth each cascade stops at.
        bool shadows = _shadowCascadeCount > 0 && _deferred.ShadowMap.Ready;
        for (int i = 0; i < 4; i++)
        {
            u[44 + i] = i < _shadowCascadeCount ? _cascadeSplits[i] : 0f;
        }

        // uShadowConfig: cascade count (0 disables), the depth and normal biases, and the texel size.
        u[48] = shadows ? _shadowCascadeCount : 0f;
        u[49] = _shadowSettings.DepthBias;
        u[50] = _shadowSettings.NormalBias;
        u[51] = _deferred.ShadowMap.Resolution > 0 ? 1f / _deferred.ShadowMap.Resolution : 0f;

        // uCascadeMatrix[4]: each cascade's camera-relative-world to light-clip, column-major.
        for (int i = 0; i < 4; i++)
        {
            Matrix4f cascade = i < _shadowCascadeCount ? _cascadeMatrices[i] : Matrix4f.Identity;
            cascade.CopyColumnMajorTo(new Span<float>(u + 52 + (i * 16), 16));
        }

        // uWetness0: how wet, how much of the ground stands under water, how hard the rain strikes it,
        // and the clock the ripples run on.
        u[116] = MathF.Max(_wetness.Amount, 0f);
        u[117] = Math.Clamp(_wetness.PuddleCoverage, 0f, 1f);
        u[118] = Math.Clamp(_wetness.RippleStrength, 0f, 1f);
        u[119] = _wetness.Seconds;

        // uWetness1: the two lattice spacings, in metres, and how much the wind textures the water.
        u[120] = MathF.Max(_wetness.PuddleSizeMetres, 0.05f);
        u[121] = MathF.Max(_wetness.RippleSizeMetres, 0.02f);
        u[122] = Math.Clamp(_wetness.WindRippleStrength, 0f, 1f);
        u[123] = _wetShelterCount;

        // uShelter[]: a low corner and a high corner each, camera-relative. Written past the count so a
        // frame that submits fewer than last time leaves stale boxes behind the count, where the shader
        // never reads them.
        for (int i = 0; i < _wetShelterCount; i++)
        {
            WetShelter shelter = _wetShelters[i];
            int at = 124 + (i * 8);
            u[at] = shelter.Minimum.X; u[at + 1] = shelter.Minimum.Y; u[at + 2] = shelter.Minimum.Z; u[at + 3] = 0f;
            u[at + 4] = shelter.Maximum.X; u[at + 5] = shelter.Maximum.Y; u[at + 6] = shelter.Maximum.Z; u[at + 7] = 0f;
        }

        gi.Write<float>(new ReadOnlySpan<float>(u, DeferredRenderer.GiUniformSize / sizeof(float)));

        (ImageView giView, Sampler giSampler, ImageLayout giLayout) = ResolveScreenGi();
        Deferred.ShadowMap shadowMap = _deferred.ShadowMap;
        Sky.SkyAtmosphere sky = _deferred.Sky;

        // The freshest occlusion this frame can read is the OTHER slot's, written one captured frame ago;
        // this slot's own is a frame older still. Only while the two slots are the same shape.
        Deferred.SceneTarget previousForAo = _deferred.PreviousTarget(_frameLoop.CurrentFrame);
        ImageView freshestAo = previousForAo.Extent.Width == _sceneTarget!.Extent.Width
            && previousForAo.Extent.Height == _sceneTarget.Extent.Height
            && previousForAo.AoView.Handle != 0
                ? previousForAo.AoView
                : _sceneTarget.AoView;

        Memory.VulkanBuffer lamps = WriteLampUniform(frame);

        _sceneTarget.WriteGi(
            probes, gi.Handle, (ulong)DeferredRenderer.GiUniformSize,
            lamps.Handle, (ulong)DeferredRenderer.LampUniformSize,
            _device.Capabilities.RayQuery ? _sceneTarget.LampFilteredView : default,
            giView, giSampler, giLayout,
            shadowMap.ArrayView, shadowMap.Sampler,
            new Deferred.SkyTables(sky.SkyViewLut, sky.TransmittanceLut, sky.IrradianceLut, sky.Sampler),
            freshestAo,
            TracedWorld());
    }

    /// <summary>The world the lamps shadow against, or nothing when there is none to trace.</summary>
    /// <remarks>
    /// The level's own structure once it is built, and the placeholder before that -- a single degenerate
    /// triangle a billion units away, which every ray misses. Binding the placeholder rather than nothing
    /// is what lets the shading pass keep one code path: the descriptor is always valid, and during the
    /// two seconds of a level load the lamps simply cast no shadows instead of the pass having to be
    /// compiled twice over.
    /// </remarks>
    private AccelerationStructureKHR TracedWorld()
    {
        if (!_device.Capabilities.RayQuery)
        {
            return default;
        }

        if (_worldScene is { IsReady: true } scene && scene.TopLevel is { } topLevel)
        {
            return topLevel.Handle;
        }

        return _placeholderScene?.TopLevel?.Handle ?? default;
    }

    /// <summary>Writes this frame's local lights into the lighting pass's own block.</summary>
    /// <remarks>
    /// Uploaded whole every frame, count included, so a frame that set none lights from none — the same
    /// contract the OpenGL backend's array uniforms have, and the reason neither backend needs a way to
    /// say "clear the lamps". The layout mirrors the shader's block exactly: a control vector, then three
    /// parallel arrays rather than an array of structs, because a <c>vec4</c> array is the one layout
    /// whose std140 padding is the same on every implementation.
    /// </remarks>
    private unsafe Memory.VulkanBuffer WriteLampUniform(int frame)
    {
        Memory.VulkanBuffer lamps = _deferred.LampUniform(frame);
        int floats = DeferredRenderer.LampUniformSize / sizeof(float);
        float* u = stackalloc float[floats];

        PackLamps(new Span<float>(u, floats));
        lamps.Write<float>(new ReadOnlySpan<float>(u, floats));
        return lamps;
    }

    /// <summary>Packs this frame's lamps into the layout both the trace and the shading read.</summary>
    /// <remarks>
    /// One routine for both, because they have to agree about which lamp is which: the trace picks a
    /// lamp by index and the shading applies the answer to the same index, and a layout that drifted
    /// between them would shadow one lamp with another's occlusion.
    /// </remarks>
    private void PackLamps(Span<float> u)
    {
        u.Clear();

        u[0] = _localLightCount;

        // Whether those lamps shadow. Off on an adapter that cannot trace, because the shader compiled
        // for it has no trace in it at all and would ignore this anyway; off when the world has not been
        // built yet, so a level's first frames light unshadowed rather than shadowed by nothing.
        // Which frame this is, so the shadow samples move: successive frames test different points on
        // each fixture and the temporal resolve averages them into a smooth penumbra. Wrapped short --
        // the dither only needs the pattern to differ from the last frame's, not to never repeat.
        u[2] = _frameCounter % 64UL;

        // Whether the shading marches the depth buffer to occlude each lamp. On wherever lamp shadows
        // are asked for AT ALL -- unlike the traced reservoir above, this needs no ray query, so it is
        // what keeps a lamp from lighting through a wall on an adapter that cannot trace, and what
        // shadows the SECOND lamp on a pixel the reservoir's one ray could not reach. It rides on the
        // count being non-zero for the same reason the trace does: no lamps, nothing to occlude.
        u[3] = _localLightCount > 0 && RenderCVars.LocalLightShadows.Value != 0 ? 1f : 0f;

        // Whether the shading traces a ray per lamp against the world -- the off-screen half of the lamp
        // shadow. Needs an adapter that can trace AND the RT switch to be on, so it is OPTIONAL on
        // hardware that could trace but has been asked not to: r_rt off leaves the screen march above as
        // the whole of the lamp shadow, exactly as on a card that cannot trace at all. Off, too, until the
        // world is built, so a level's first frames trace against nothing rather than being shadowed by
        // nothing.
        u[1] = _localLightCount > 0
            && RenderCVars.LocalLightShadows.Value != 0
            && RenderCVars.HardwareRayTracing.Value
            && RenderCVars.TracedLampShadows.Value
            && _device.Capabilities.RayQuery
            && _worldScene is { IsReady: true } traced
            && traced.TopLevel is not null
                ? 1f
                : 0f;

        // The largest source a shadow ray may sample, whatever the fixture measures. See
        // r_local_light_source for why a cap belongs here rather than on the fixture itself.
        u[4] = MathF.Max(RenderCVars.LocalLightSource.Value, 0.02f);

        // Where each array starts, in floats: the two control vectors, then a vec4 per light per array.
        int position = 8;
        int colour = position + (DeferredLocalLight.Maximum * 4);
        int direction = colour + (DeferredLocalLight.Maximum * 4);

        for (int i = 0; i < _localLightCount; i++)
        {
            DeferredLocalLight light = _localLights[i];
            int at = i * 4;

            u[position + at] = light.ViewPosition.X;
            u[position + at + 1] = light.ViewPosition.Y;
            u[position + at + 2] = light.ViewPosition.Z;
            u[position + at + 3] = light.Radius;

            u[colour + at] = light.Colour.X;
            u[colour + at + 1] = light.Colour.Y;
            u[colour + at + 2] = light.Colour.Z;
            u[colour + at + 3] = light.Intensity;

            u[direction + at] = light.ViewDirection.X;
            u[direction + at + 1] = light.ViewDirection.Y;
            u[direction + at + 2] = light.ViewDirection.Z;

            // How big the fixture is, which is what gives its shadow a penumbra. Rides beside the facing
            // because both describe the SOURCE rather than its falloff, and the fourth component there
            // was spare.
            u[direction + at + 3] = light.SourceRadius;
        }

    }

    /// <inheritdoc/>
    public void SetLocalLights(ReadOnlySpan<DeferredLocalLight> lights)
    {
        _localLightCount = Math.Min(lights.Length, DeferredLocalLight.Maximum);

        for (int i = 0; i < _localLightCount; i++)
        {
            _localLights[i] = lights[i];
        }
    }

    /// <summary>Resolves the screen-GI atlas to a view, sampler and layout, or the white texture when unset.</summary>
    private (ImageView View, Sampler Sampler, ImageLayout Layout) ResolveScreenGi()
    {
        if (_screenIndirect.IsValid
            && _storageImages.TryGetValue(_screenIndirect.Id, out Compute.VulkanStorageImage? storage))
        {
            return (storage.View, storage.Sampler, ImageLayout.General);
        }

        if (_screenIndirect.IsValid && _textures.TryGetValue(_screenIndirect.Id, out VulkanTexture? texture))
        {
            return (texture.View, texture.Sampler, ImageLayout.ShaderReadOnlyOptimal);
        }

        VulkanTexture white = _textures[_whiteTexture.Id];
        return (white.View, white.Sampler, ImageLayout.ShaderReadOnlyOptimal);
    }

    /// <summary>Which part of a deferred capture is currently open.</summary>
    private enum DeferredStage
    {
        /// <summary>Nothing is open.</summary>
        None,

        /// <summary>The geometry pass, filling the G-buffer.</summary>
        Geometry,

        /// <summary>The G-buffer is filled and its pass closed, with the shading pass not yet open.</summary>
        Gap,

        /// <summary>The shading pass, on its lighting subpass.</summary>
        Lighting,

        /// <summary>The shading pass, on its translucency subpass.</summary>
        Forward,
    }
}
