using System.Diagnostics.CodeAnalysis;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Sky;
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

    // The first-person lens's own x and y scales, captured from the projection the rig is actually drawn
    // with, and zero on a frame that drew no rig.
    //
    // The rig does NOT share the frame's projection: it has a retail foreground lens with a fixed
    // horizontal half-tangent and a five centimetre near plane, so it is rasterised at screen positions
    // the world projection disagrees with. A reflection looking for the weapon through the world lens
    // reads pixels that are not the weapon, which is what made a reflected gun a smeared patch. Captured
    // rather than plumbed through a new device call, because the projection in force while the rig draws
    // IS that lens.
    private float _viewModelLensX;
    private float _viewModelLensY;

    // The camera in full double precision, and the frame-to-frame delta formed from it. This world's
    // coordinates run to tens of thousands of units, and every temporal pass reprojects the static world
    // by the camera's motion between two frames -- a motion that is often a fraction of a unit. Forming
    // that delta by subtracting two single-precision positions of that magnitude loses all but a few bits
    // of it to catastrophic cancellation, and the leftover error is proportional to the translation, so it
    // is invisible on a held or purely rotating camera and grows with strafing/walking speed into a
    // smeared reprojection -- the reference composes the whole reprojection in double and casts to float
    // only at the end for exactly this reason. So the delta is differenced in double from the double
    // camera and only then narrowed to float, where a value this small carries full precision.
    private Vector3d _cameraPositionPrecise;
    private Vector3d _previousCameraPositionPrecise;
    private Vector3f _cameraDelta;

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

    // The medium the frame is seen through, and the level's exceptions to it already rebased against
    // the camera. Whether the volume was actually built this frame decides whether the shading reads it.
    private VolumetricFogSettings _fogSettings = VolumetricFogSettings.Off;
    private readonly LocalFogVolume[] _fogVolumes = new LocalFogVolume[LocalFogVolume.MaxPerLevel];
    private int _fogVolumeCount;
    private bool _fogActive;
    private ReflectionSettings _reflectionSettings = ReflectionSettings.Off;

    // The level's lit cards, handed over every frame. What a reflected ray that leaves the screen is
    // resolved against; empty while no mission is loaded, which is what makes the traced half stand down.
    private SurfaceCacheBinding _surfaceCache = SurfaceCacheBinding.None;

    // The level's distance field, for the reflection passes on an adapter that cannot trace. Handed over
    // every frame because its boxes are camera-relative; resolved only on a frame that marches.
    private DistanceFieldBinding _distanceField = DistanceFieldBinding.None;

    // The last thing the software glass report said, so it is logged on change rather than every frame.
    private string _glassMarchState = string.Empty;

    // One texel of everything the marched glazing declares, for the frames that have none of it. Built on
    // first use rather than at start-up: a machine that always has a field and a cache never pays for it.
    private TextureHandle _glassStandInField;
    private TextureHandle _glassStandInCard;
    private StorageBufferHandle _glassStandInRecords;

    // How many glazing draws the forward subpass took, and the last thing said about them. Counted
    // rather than inferred because "no reflection on the glass" has two completely different causes --
    // no pane reached the pass at all, or every pane did and traced nothing -- and nothing in the picture
    // tells the two apart. See ReportGlass.
    private int _glassDraws;

    // Whether this frame drew a front layer, so the translucency pass does not redo its bookkeeping.
    private bool _glassLayerRan;
    private string _glassState = string.Empty;

    // The last thing said about what the traced world holds. See ReportTracedWorld.
    private string _tracedWorldState = string.Empty;

    // The last thing said about whether reflections are being traced, so the next frame only speaks when
    // the answer has changed. See ReportReflectionTrace.
    private string _reflectionTraceState = string.Empty;

    // The level's roofs, seen from above, and how to read them. Set once when a mission loads and held
    // until the next one replaces it: where the rain reaches is a property of the level, not the frame.
    private TextureHandle _shelterMap = TextureHandle.None;
    private ShelterMapTransform _shelterTransform = ShelterMapTransform.None;

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
    private Matrix4f _temporalProjection = Matrix4f.Identity;
    private bool _temporalResolved;
    private int _captureWidth;
    private int _captureHeight;
    private int _presentWidth;
    private int _presentHeight;
    private Viewport _sceneViewport;
    private ScenePass _currentScenePass = ScenePass.None;
    private SceneTarget? _sceneTarget;

    // The exposure the last resolve settled on. The temporal passes run BEFORE the resolve that computes
    // this frame's, so a pass that needs to judge contrast at display levels -- the spatial anti-aliaser
    // does, its edge test being a threshold -- reads the previous frame's. Exposure eases over about a
    // second, so a frame's worth of lag is far below what the threshold can notice.
    private float _lastAdaptedExposure = 1f;

    // Which frame the film grain is on. Not the frame-in-flight index, which only counts to the number of
    // buffered frames and would repeat the same handful of patterns forever.
    private int _grainFrame;

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

        // Asserted again by every frame whose title draws its own sky, so a title swap cannot leave the
        // last one's answer behind.
        _frameHasPaintedSky = false;
        _captureWidth = Math.Max(1, description.Width);
        _captureHeight = Math.Max(1, description.Height);
        _presentWidth = Math.Max(1, description.PresentWidth);
        _presentHeight = Math.Max(1, description.PresentHeight);

        // The object-velocity framebuffers over a target's views are dropped by the target itself when it
        // rebuilds (SceneTarget.Rebuilt). Deciding it here, from the capture size alone, missed the rebuild
        // a change of OUTPUT size causes: under a resolution scale a one-pixel window change can leave the
        // capture size as it was, the target still rebuilt, and a framebuffer over destroyed views was
        // found again through a recycled handle value.

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
        else if (pass == ScenePass.GlassLayer)
        {
            // The geometry pass has to be closed and its depth readable before this can start: the front
            // layer tests every pane against what the opaque world drew, and it does that in the shader
            // because its own depth buffer is private. Closed exactly the way EndPass closes it.
            CloseGeometryForCompute();
            if (_deferredStage == DeferredStage.Geometry)
            {
                _instance.Vk.CmdEndRenderPass(_frameCommandBuffer);
                _deferredPassActive = false;
                RecordBetweenPasses();
            }

            // Set 1 points at the scene depth for the whole of this pass and the translucency one after
            // it. Written here because a descriptor write is a host call and must not land while a set is
            // bound, and no glazing has been drawn yet.
            ReportGlass();
            ReportTracedWorld();
            _glassDraws = 0;
            UpdateGlassResources();

            BeginFrontLayerPass();
            _deferredStage = DeferredStage.GlassLayer;
        }
        else if (pass == ScenePass.Forward)
        {
            // The shading pass is already open and its lighting subpass already drawn; translucency is its
            // second subpass.
            // Only where no front-layer pass ran -- it does this itself, and doing it twice would reset
            // the pane count after the panes had already been counted.
            if (_glassLayerRan)
            {
                _glassLayerRan = false;
            }
            else
            {
                ReportGlass();
                ReportTracedWorld();
                _glassDraws = 0;
                UpdateGlassResources();
            }

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

        // The glazing front layer is a render pass of its own, between the geometry pass and the shading
        // one. Closing it drops the stage back to the gap, and everything below then runs exactly as it
        // does when no panes were drawn at all.
        if (_deferredStage == DeferredStage.GlassLayer)
        {
            _instance.Vk.CmdEndRenderPass(_frameCommandBuffer);
            _deferredPassActive = false;
            _deferredStage = DeferredStage.Gap;
            _glassLayerRan = true;
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

            // The fog, after the lamps it lights from and before the shading pass that reads it.
            DispatchVolumetricFog();

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

        // And the fog it accumulated, for the same reason: the next level's first frame would otherwise
        // blend nine parts of the air of the mission being left.
        _deferred.Fog.Invalidate();
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
            // The frame-to-frame camera motion, differenced in double from the double camera and narrowed
            // to float only now -- see the field for why this cannot be a single-precision subtraction of
            // two absolute positions. Formed once and read by every reprojecting pass below.
            UpdateCameraDelta();
            DispatchVelocity();

            // The reflections, after the frame is lit and before the temporal pass -- so the pass reads
            // the finished scene as the reflection's source, and so the temporal resolve then denoises
            // and stabilises what the single-ray march finds. Composited into the lit scene in place, so
            // everything downstream reads it with the reflections already in.
            DispatchReflections();

            // The glazing, after them. It reads the lit scene the reflections have just composited into,
            // which is right: a window reflecting a wet road should show the road with its reflections in
            // it, exactly as the eye would.
            DispatchGlassReflections();

            DispatchTemporalResolve();

            // Everything temporal measures against this, so it moves on exactly once per captured frame.
            _previousViewToWorld = _viewToWorld;
            _previousCameraPosition = _cameraPosition;
            _previousCameraPositionPrecise = _cameraPositionPrecise;
            _viewModelLensX = 0f;
            _viewModelLensY = 0f;
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
        SkyConditions sky = SkyConditions.ForScene(_lighting, _viewToWorld, altitudeMetres: 2f);

        _deferred.Sky.Record(
            _frameCommandBuffer,
            _frameLoop.CurrentFrame,
            sky.SunDirection,
            sky.SunIlluminance,
            sky.ViewHeightKm,
            sky.SkyLightIntensity);
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

    // How far, in metres, a TRACED reflected ray reaches before it counts as having left for the sky.
    //
    // Much further than the march, and for a reason that is not "the trace is cheaper per metre" -- it is
    // that the two fail differently. A march that runs out of steps has simply stopped looking, so its
    // reach is a budget; a ray that reaches the end of this has genuinely found nothing, and what it says
    // about the world past this point is that there is sky there. So the number is set by how far the
    // surface cache can actually answer rather than by cost: past the cards there is nothing to resolve a
    // hit against, and a hit beyond them would read the lookup's empty answer and fall through to the sky
    // anyway. A kilometre covers every level's built area with room to spare.
    private const float TracedReflectionReachMetres = 1000f;

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

        // The per-texel sample ceiling. The reference divides its sixteen-per-output-pixel budget by the
        // history's texel count and lands on four, which also zeroes its velocity weight clamp; this
        // pipeline measured better with a deeper history at rest and the clamp yielding it under motion,
        // so the ceiling is its own cvar -- see RenderCVars.TemporalHistorySamples for the numbers.
        const float MaxOutputHistorySamples = 16f;
        float historySampleCount = RenderCVars.TemporalHistorySamples.Value;

        // The shading guide follows the output-pixel sample budget, adjusted by the square of the
        // input-to-output fraction. During an upscale each rendered pixel represents more output area,
        // so the guide must move more slowly than it does at native resolution; using the per-history-
        // texel ceiling here made a 0.7-scale guide replace a quarter of itself every frame instead of
        // roughly three percent, quickly teaching the rejection pass to accept a motion-softened frame.
        float inputToOutput = _captureWidth / (float)Math.Max(1, _presentWidth);
        float theoricGuideBlend = 1f
            / (1f + (MaxOutputHistorySamples / Math.Max(inputToOutput * inputToOutput, 1e-6f)));

        // The history is the OTHER frame slot's — written exactly one frame ago, which is the frame the
        // velocity spans. Reading this slot's own history would be TWO frames old, and reprojecting two
        // frames of motion with one frame's velocity smears every edge into a double image the moment the
        // camera moves. When the other slot has not been built at this size (first frames, a resize),
        // this slot's own cleared history stands in: its zero sample count makes the pass restart from
        // the current frame, which is the honest answer there anyway.
        Deferred.SceneTarget previous = _deferred.PreviousTarget(_frameLoop.CurrentFrame);
        bool previousUsable = previous.Extent.Width == target.Extent.Width
            && previous.Extent.Height == target.Extent.Height
            && previous.OutputExtent.Width == target.OutputExtent.Width
            && previous.OutputExtent.Height == target.OutputExtent.Height
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

        Mark(VulkanPassMarker.ThinGeometry);
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

            Mark(VulkanPassMarker.RejectShading);
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
                theoricGuideBlend,
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

            Mark(VulkanPassMarker.RejectionDilate);
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

        Mark(VulkanPassMarker.SpatialAntiAliasing);
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

        Mark(VulkanPassMarker.UpdateHistory);
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
            _presentWidth,
            _presentHeight,
            temporalOn: true,
            maxSamples: historySampleCount,
            _taaSettings.JitterX,
            _taaSettings.JitterY * VerticalJitterSign,
            target.DepthView);

        // The resolve reads the history the update just wrote, and writes the frame-sized image.
        OcclusionBarrier(
            cmd, target.UpscaleHistoryColourImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
        OcclusionBarrier(
            cmd, target.UpscaleOutputImage,
            AccessFlags.ShaderReadBit, AccessFlags.ShaderWriteBit,
            PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.ComputeShaderBit,
            PipelineStageFlags.ComputeShaderBit);

        Mark(VulkanPassMarker.ResolveHistory);
        _deferred.ResolveHistory.Record(
            cmd,
            _frameLoop.CurrentFrame,
            target.UpscaleHistoryColourView,
            target.UpscaleOutputView,
            _captureWidth,
            _captureHeight,
            Deferred.SceneTarget.HistoryScale,
            _presentWidth,
            _presentHeight,
            _taaSettings.MotionSharpening,
            _velocityDilated ? target.DilatedVelocityView : target.VelocityView,
            target.HistoryRejectionView,
            target.ShadingRejectionView,
            target.UpscaleHistoryMetadataView,
            _debugView switch
            {
                DebugView.Motion => 1,
                DebugView.UpscaleParallax => 2,
                DebugView.UpscaleShading => 3,
                DebugView.UpscaleHistory => 4,
                _ => 0,
            });

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
    /// <para>
    /// Two ways to answer one question, and the pass uses exactly one of them per frame: a ray against the
    /// world where there is a world to trace, and a march through the depth buffer everywhere else. The
    /// march runs on every adapter and needs no ray query; the trace needs one, plus the switches and a
    /// lit surface cache. See the shader for why the two are never mixed.
    /// </para>
    /// <para>
    /// The two dispatches are separated by the barriers a read-then-write of the lit image needs: the
    /// reflection samples the lit scene, the composite writes it, and between them the image moves from
    /// the read layout to the general layout a storage write wants and back again for the temporal
    /// resolve.
    /// </para>
    /// </remarks>
    private unsafe void DispatchReflections()
    {
        // Not while a debug view is up: it composites into the lit image, and a view that isolates one
        // term of the lighting must not have the reflections of the whole scene added back over it.
        //
        // The reflection view itself is the exception, and the reason it exists. What a surface reflects
        // is a small addition to an already lit picture -- a term nobody can see on its own, and therefore
        // one nobody can tell is missing. Under this view the pass runs as usual and the composite REPLACES
        // the lit scene instead of adding to it, so the reflection is the picture. It is the only way to
        // answer "is the traced half contributing anything" by looking rather than by arithmetic.
        bool isolate = _debugView is DebugView.Ssr;

        if (_sceneTarget is null || (_debugView is not DebugView.None && !isolate))
        {
            return;
        }

        // What this frame has to trace against, asked once. It decides whether the pass runs at all, so it
        // cannot wait until the parameters are packed.
        //
        // And what it has to MARCH against where it has nothing to trace -- which is the ordinary case,
        // not merely the no-hardware one: every gate in TracedReflectionResources is a setting or a state
        // that moves during a session. Behind the screen-space march there is now a second answer for the
        // rays the screen cannot follow, instead of nothing at all.
        Deferred.TracedResources traced = TracedReflectionResources();

        if (!traced.Traces)
        {
            traced = SoftwareReflectionResources();
        }

        // The pass is worth running when EITHER answer is available. Reflections being wanted at all is
        // r_ssr_intensity and r_ssr_max_roughness -- a surface reflecting nothing, or no surface smooth
        // enough to reflect, and there is nothing to do either way.
        //
        // r_ssr is then the march's OWN step count, and no longer speaks for the pass as a whole: it used
        // to, because the march was the only answer there was. Setting it to zero now means "do not march"
        // rather than "no reflections", so a player who traces can turn the screen-space path off outright
        // and keep their reflections -- which is the setting the name has always implied and never meant.
        if (!_reflectionSettings.Enabled
            || (!_reflectionSettings.Marches && !traced.Traces && !traced.Marches))
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
            _deferred.Sky.SkyViewLut,
            target.BaseColourView,
            target.ReflectionView,
            width,
            height,
            _viewToWorld,
            _unprojection,
            _reflectionSettings,
            _wetness,
            ShelterMapView(),
            HasShelterMap() ? _shelterTransform : ShelterMapTransform.None,
            _cameraPosition.X,
            _cameraPosition.Y,
            _cameraPosition.Z,
            Sky.SkyAtmosphere.ViewHeightKm(2f),
            (int)(_frameCounter % 64UL),
            ReflectionReachMetres,
            traced,
            RenderCVars.TracedReflectionClamp.Value,
            TracedReflectionReachMetres,
            RenderCVars.TracedReflectionMaxRoughness.Value,
            _viewModelLensX,
            _viewModelLensY);

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
            height,
            isolate);

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
            _cameraDelta,
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

        Mark(VulkanPassMarker.CameraVelocity);
        _deferred.Velocity.Record(
            cmd,
            _frameLoop.CurrentFrame,
            target.DepthView,
            target.VelocityView,
            _captureWidth,
            _captureHeight,
            _viewToWorld,
            _previousViewToWorld,
            _cameraDelta,
            _unprojection,
            _previousUnprojection,
            _taaSettings.JitterX,
            _taaSettings.JitterY * VerticalJitterSign);

        OcclusionBarrier(
            cmd, velocity, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.FragmentShaderBit);

        // Everything that moves under its own power stamps its true motion over the camera-derived
        // field, before the dilation spreads it. Without this a running soldier reprojects as though he
        // were the wall behind him, and the temporal accumulation smears him along his own path.
        if (_objectVelocityDraws.Count > 0)
        {
            Mark(VulkanPassMarker.ObjectVelocity);
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

        // The closest-occluder scatter starts every frame as "nothing landed here": all-ones, which no
        // float's bits exceed, so the dilation's atomic minimum can only lower it. The image lives in
        // General, where a clear is legal, so no layout transition is needed around it.
        OcclusionBarrier(
            cmd, target.OccluderImage, AccessFlags.ShaderReadBit, AccessFlags.TransferWriteBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.TransferBit);
        Mark(VulkanPassMarker.OccluderClear);
        ClearOccluderScatter(cmd, target.OccluderImage);
        OcclusionBarrier(
            cmd, target.OccluderImage, AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
            PipelineStageFlags.TransferBit, PipelineStageFlags.ComputeShaderBit);

        Mark(VulkanPassMarker.DilateVelocity);
        _deferred.DilateVelocity.Record(
            cmd,
            _frameLoop.CurrentFrame,
            target.DepthView,
            target.VelocityView,
            target.DilatedVelocityView,
            target.ClosestDepthView,
            target.OccluderView,
            _captureWidth,
            _captureHeight,
            _viewToWorld,
            _previousViewToWorld,
            _cameraDelta,
            _unprojection,
            _previousUnprojection,
            _taaSettings.JitterX,
            _taaSettings.JitterY * VerticalJitterSign,
            _previousJitterX,
            _previousJitterY);

        OcclusionBarrier(
            cmd, target.DilatedVelocityImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
        OcclusionBarrier(
            cmd, target.ClosestDepthImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
        OcclusionBarrier(
            cmd, target.OccluderImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
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

            Mark(VulkanPassMarker.DecimateHistory);
            _deferred.DecimateHistory.Record(
                cmd,
                _frameLoop.CurrentFrame,
                target.DepthView,
                target.DilatedVelocityView,
                previousFrame.DepthView,
                target.HistoryRejectionView,
                target.OccluderView,
                _captureWidth,
                _captureHeight,
                _viewToWorld,
                _previousViewToWorld,
                _cameraDelta,
                _unprojection,
                _previousUnprojection,
                _taaSettings.JitterX,
                _taaSettings.JitterY * VerticalJitterSign,
                _previousJitterX,
                _previousJitterY);

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

    /// <summary>Records which pass the GPU is about to run, for a device lost inside it.</summary>
    /// <remarks>
    /// The stage brackets are too coarse to place a fault: the velocity pass and the temporal resolve both
    /// open <c>TemporalResolve</c>, so a lost device could only say it was somewhere in one of them. This
    /// names the individual pass, and costs a marker write on a path that already writes one per stage.
    /// </remarks>
    private void Mark(VulkanPassMarker pass) =>
        _device.Checkpoint(_frameCommandBuffer, VulkanCheckpointMarkers.Pass(pass));

    /// <summary>Resets the closest-occluder scatter to "nothing landed here", which is all-ones.</summary>
    private unsafe void ClearOccluderScatter(CommandBuffer cmd, Image occluder)
    {
        ClearColorValue allOnes = new()
        {
            Uint32_0 = uint.MaxValue,
            Uint32_1 = uint.MaxValue,
            Uint32_2 = uint.MaxValue,
            Uint32_3 = uint.MaxValue,
        };
        ImageSubresourceRange range = new()
        {
            AspectMask = ImageAspectFlags.ColorBit,
            BaseMipLevel = 0,
            LevelCount = 1,
            BaseArrayLayer = 0,
            LayerCount = 1,
        };
        _instance.Vk.CmdClearColorImage(cmd, occluder, ImageLayout.General, &allOnes, 1, &range);
    }

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
    /// <summary>Traces what the level's glazing reflects and adds it into the lit scene.</summary>
    /// <remarks>
    /// <para>
    /// Beside the screen-space reflections and for the same reason: the render pass has closed, so the
    /// lit image is a readable texture holding the finished frame — the room, the soldiers, and the
    /// first-person rig the forward subpass drew a moment ago. That is the only state of the frame in
    /// which a window can reflect anything that moves.
    /// </para>
    /// <para>
    /// Runs whether or not the screen-space reflections did. They are a different question about
    /// different surfaces, and a player who has turned the march off has not asked for windows to stop
    /// being windows.
    /// </para>
    /// </remarks>
    private unsafe void DispatchGlassReflections()
    {
        if (_sceneTarget is not { } target || _glassDraws == 0)
        {
            return;
        }

        // Not while a debug view is up, exactly as the screen-space reflections stand down: a view that
        // isolates one term of the lighting must not have a window's reflection added back over it.
        //
        // The glazing view is the exception and the reason it exists. Under it the pass runs as usual, the
        // trace writes what it found WITHOUT the Fresnel weight, and the composite replaces the lit scene
        // instead of adding to it -- so the panes are the picture. Head-on a reflection is four per cent of
        // a pixel, which is small enough that a trace returning the room and a trace returning nothing look
        // identical in the finished frame; this is the only way to tell them apart by looking.
        // Both glazing views isolate: each replaces the lit scene with something that is not light, so
        // neither may have the rest of the picture left underneath it.
        float route = _debugView switch
        {
            DebugView.GlassRoute => 1f,
            DebugView.GlassAlbedo => 2f,
            DebugView.GlassRay => 3f,
            DebugView.GlassNormal => 4f,
            _ => 0f,
        };

        bool isolate = _debugView is DebugView.Glass || route > 0f;
        if (_debugView is not DebugView.None && !isolate)
        {
            return;
        }

        CommandBuffer cmd = _frameCommandBuffer;
        int width = (int)target.Extent.Width;
        int height = (int)target.Extent.Height;

        using StageScope scope = Measure(RenderStage.Reflections);

        // Traced where this frame can, MARCHED where it cannot -- and the second is asked for whenever the
        // first comes back empty, not only on hardware that could never trace. Every gate in
        // TracedReflectionResources is a setting or a state that moves during a session, so "no world to
        // fire at" is the ordinary case on an adapter that owns a perfectly good acceleration structure
        // and has been told, this frame, not to use it. The march is not a lesser version of the trace: it
        // is a different way of finding the hit, resolved against the same lit frame and the same cards.
        Deferred.TracedResources traced = _deferred.GlassReflection.Traces
            ? TracedReflectionResources()
            : Deferred.TracedResources.None;

        if (!traced.Traces)
        {
            traced = SoftwareReflectionResources();
        }

        // The panes' own writes, before this reads them.
        OcclusionBarrier(
            cmd, target.GlassLayerImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
            PipelineStageFlags.FragmentShaderBit, PipelineStageFlags.ComputeShaderBit);

        LitReadBarrier(cmd, target.LitImage, PipelineStageFlags.ComputeShaderBit);

        _deferred.GlassReflection.RecordReflect(
            cmd,
            _frameLoop.CurrentFrame,
            target.LitView,
            target.DepthView,
            target.GlassLayerView,
            _deferred.Sky.SkyViewLut,
            target.ReflectionView,
            width,
            height,
            _viewToWorld,
            _unprojection,
            traced,
            TracedReflectionReachMetres,
            RenderCVars.TracedReflectionClamp.Value,
            Sky.SkyAtmosphere.ViewHeightKm(2f),
            RenderCVars.GlassReflection.Value,
            isolate && route <= 0f,
            route,
            new Vector3f(_cameraPosition.X, _cameraPosition.Y, _cameraPosition.Z),
            _viewModelLensX,
            _viewModelLensY,

            // For the one route that shades a hit itself. WORLD space, like the ray: the lighting record
            // carries the sun in VIEW space, which would swing a reflected body's shading around as the
            // camera turned.
            WorldSunDirection(),
            _lighting.SunColour * _lighting.Scale,
            _lighting.AmbientColour,

            // The level's roofs, for the one question the marched half cannot answer without them:
            // whether a ray that left the distance field found the sky or lost a ceiling.
            ShelterMapView(),
            HasShelterMap() ? _shelterTransform : ShelterMapTransform.None,

            // And the albedo array's own WRAPPING sampler. The pass's shared one clamps, which is right
            // for the tables it mostly reads and wrong for a world texture whose UV runs past one.
            _tracedAlbedo?.Sampler ?? default);

        // The trace's write, before the composite reads it; and the lit image into the layout a storage
        // write wants, and back again for the temporal resolve.
        OcclusionBarrier(
            cmd, target.ReflectionImage, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);
        LitLayoutBarrier(
            cmd, target.LitImage, ImageLayout.ShaderReadOnlyOptimal, ImageLayout.General,
            AccessFlags.ShaderReadBit, AccessFlags.ShaderWriteBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.ComputeShaderBit);

        _deferred.GlassReflection.RecordComposite(
            cmd,
            _frameLoop.CurrentFrame,
            target.ReflectionView,
            target.LitView,
            width,
            height,
            isolate);

        LitLayoutBarrier(
            cmd, target.LitImage, ImageLayout.General, ImageLayout.ShaderReadOnlyOptimal,
            AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
            PipelineStageFlags.ComputeShaderBit,
            PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.FragmentShaderBit);
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

        // A mask view is a channel being read rather than a picture being looked at, so it keeps neither
        // the curve, the lens, the film nor the grade.
        bool maskViewPending = _debugView.IsMask();

        // The stable accumulated colour when the temporal pass ran this frame; the raw lit colour when
        // nothing accumulated and the lit image is already the final word.
        // The grade, into all three resolve sets. Written here rather than when it was uploaded because
        // the sets outlive neither a resize nor a table change and this is the one point both have
        // certainly happened by. The lit view stands in when there is no grade, so the binding a layout
        // declares is never left unwritten -- reading one that is, is undefined even where nothing does.
        bool graded = _colourLut.Ready && post.ColourLookup > 0f && !maskViewPending;
        _sceneTarget.WriteColourLut(
            graded ? _colourLut.View : default, _colourLut.Sampler, _colourLut.StandIn, _sceneTarget.LitView);

        DescriptorSet set = motionBlurred
            ? _sceneTarget.ResolveSetMotionBlur
            : _temporalResolved ? _sceneTarget.ResolveSetTsr : _sceneTarget.ResolveSet;
        vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _deferred.Resolve.PipelineLayout, 0, 1, &set, 0, null);
        InvalidateBoundState();

        // A mask view is a code value rather than radiance, so it keeps neither the curve nor the lens.
        bool maskView = maskViewPending;

        // The grain's phase advances once per resolved frame, wrapped so the counter cannot drift into
        // a magnitude where a float has no fractional bits left to give the shader.
        _grainFrame = (_grainFrame + 1) & 1023;

        // One texel of the resolved picture, which is what the lens effects step by. The resolve draws
        // at the PRESENT size whatever the scene was rendered at, so this is the presented extent.
        float presentWidth = Math.Max(1, (int)_presentTarget.Extent.Width);
        float presentHeight = Math.Max(1, (int)_presentTarget.Extent.Height);

        float* parameters = stackalloc float[20]
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

            // The quantization dither: one 8-bit code value, none on a code-value view.
            maskView ? 0f : ResolvePushBlock.QuantizationDither,

            // A mask view is a code value and gets no film on it, for the same reason it gets no curve.
            // The phase is stepped by the golden ratio so successive frames land far apart in the hash
            // rather than walking through it, which is what stops the grain crawling in one direction.
            maskView ? 0f : Math.Clamp(post.Grain, 0f, 1f),
            _grainFrame * 0.61803399f,

            // The authored grade and the cube it was sampled onto. A mask view gets none of it, for the
            // same reason it gets no curve: it is a channel being read, not a picture being looked at.
            graded ? Math.Clamp(post.ColourLookup, 0f, 1f) : 0f,
            graded ? _colourLut.Size : 0f,

            // The lens. A mask view is a channel being read rather than a picture being looked at, so
            // it keeps none of it -- the same rule the curve, the film and the grade follow.
            maskView ? 0f : MathF.Max(post.ChromaticAberration, 0f),
            maskView ? 0f : MathF.Max(post.Sharpen, 0f),
            1f / presentWidth,
            1f / presentHeight,

            // The grade's knobs, which trim whatever the authored table set.
            maskView ? 1f : post.Contrast,
            maskView ? 1f : post.Saturation,
            !maskView && post.GreenPhosphor ? 1f : 0f,
            0f,
        };
        vk.CmdPushConstants(cmd, _deferred.Resolve.PipelineLayout, ShaderStageFlags.FragmentBit, 0, sizeof(float) * 20, parameters);

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
    public void SetPaintedSky(bool painted)
    {
        ThrowIfDisposed();
        _paintedSky = painted;
        _frameHasPaintedSky |= painted;
    }

    // Whether the draws being recorded are the sky itself. A bracket, and it has to be one: what it
    // turns on is the division by the exposure, and that belongs to the dome's own draws. Left set for
    // the rest of the frame it divided every LAMP in the level by the exposure as well, which under a
    // night adaptation is most of a lamp's brightness gone.
    private bool _paintedSky;

    // And whether the frame HAS one, which is a different question with a different lifetime. The
    // lighting pass is recorded after every draw in the frame, so a bracket would always have closed by
    // the time it asks; this is what it reads. Reset when the capture opens.
    private bool _frameHasPaintedSky;

    /// <summary>The exposure a display-referred draw has to be divided by to survive the resolve.</summary>
    /// <remarks>
    /// The player's setting less the night, times whatever the eye has adapted to. Floored so a frame
    /// that has adapted to near-black cannot divide by nothing.
    /// </remarks>
    private float ResolveExposure() =>
        MathF.Max(RenderCVars.EffectiveExposure * _lastAdaptedExposure, 1e-3f);

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
        Vector3d cameraPosition,
        Matrix3f viewToWorld)
    {
        ThrowIfDisposed();
        _taaSettings = settings;
        _temporalProjection = projection;
        _cameraPositionPrecise = cameraPosition;
        _cameraPosition = new Vector3f((float)cameraPosition.X, (float)cameraPosition.Y, (float)cameraPosition.Z);
        _viewToWorld = viewToWorld;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Held rather than acted on here: it is two vectors in the lighting pass's own uniform, so the whole
    /// of it arrives with the frame's GI parameters and costs no binding, no pass and no extra upload.
    /// </remarks>
    public void SetWetness(in WetnessSettings wetness)
    {
        ThrowIfDisposed();
        _wetness = wetness;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Held rather than bound here: the descriptor sets are written as part of the frame, and the map is
    /// a texture like any other until then. A handle that no longer names a live texture -- a mission
    /// that has unloaded -- falls back to the placeholder, so a stale map cannot outlive its level.
    /// </remarks>
    public void SetShelterMap(TextureHandle map, in ShelterMapTransform transform)
    {
        ThrowIfDisposed();
        _shelterMap = map;
        _shelterTransform = transform;
    }

    // The view the shading and the reflection fetch their roofs from: the level's map where one is
    // loaded, and a one-texel placeholder otherwise. A binding the layout declares has to be written
    // whether or not there is anything to say, and the transform's own "present" flag is what stops the
    // shader believing the placeholder.
    private ImageView ShelterMapView()
    {
        if (_shelterTransform.IsPresent
            && _shelterMap.IsValid
            && _textures.TryGetValue(_shelterMap.Id, out Textures.VulkanTexture? map))
        {
            return map.View;
        }

        // The device's own one-texel white, which every other optional binding falls back to. What it
        // decodes to does not matter: the shader is told the map is absent and never fetches from it.
        return _textures[_whiteTexture.Id].View;
    }

    // Whether what ShelterMapView is about to hand over is the level's own map rather than the
    // placeholder. The shader is told this and nothing else about the map's validity.
    private bool HasShelterMap() =>
        _shelterTransform.IsPresent && _shelterMap.IsValid && _textures.ContainsKey(_shelterMap.Id);

    /// <inheritdoc/>
    /// <remarks>
    /// Held here and acted on between the two render passes, once the frame's lamps are known — see
    /// <see cref="DispatchVolumetricFog"/>. The volumes are rebased against the camera on the way in,
    /// because everything downstream of the seam works camera-relative: a level sits tens of kilometres
    /// from the world origin, and a froxel's offset from the camera is a number with digits to spare
    /// where its absolute position is not.
    /// </remarks>
    public void SetVolumetricFog(
        in VolumetricFogSettings fog,
        Vector3f cameraPosition,
        ReadOnlySpan<LocalFogVolume> volumes)
    {
        ThrowIfDisposed();
        _cameraPosition = cameraPosition;
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

        for (int i = _fogVolumeCount; i < _fogVolumes.Length; i++)
        {
            _fogVolumes[i] = default;
        }
    }

    /// <summary>Lights and integrates this frame's fog volume, between the geometry and shading passes.</summary>
    /// <remarks>
    /// <para>
    /// Here rather than earlier because it needs the frame's lamps, which the game submits after the
    /// G-buffer is filled; and here rather than later because the shading pass is what reads the result,
    /// and a compute dispatch cannot run inside a render pass. The cascades it shadows against were
    /// filled before the capture even opened, so they are ready whichever way the sun's shadow is drawn.
    /// </para>
    /// <para>
    /// Skipped under a debug view for the same reason the reflections are: those views isolate one term
    /// of the lighting, and a term is not isolated if the whole scene's fog is composited over it.
    /// </para>
    /// </remarks>
    private void DispatchVolumetricFog()
    {
        _fogActive = false;
        if (_sceneTarget is not { } target
            || !_capturing
            || !_fogSettings.Enabled
            || _debugView is not DebugView.None)
        {
            return;
        }

        int width = (int)target.Extent.Width;
        int height = (int)target.Extent.Height;
        // Retired rather than freed: this runs inside a frame, and the volumes a quality change
        // replaces are still named by the frames the GPU has not finished.
        if (!_deferred.Fog.Ensure(width, height, _fogSettings.Steps, QueueDeletion))
        {
            return;
        }

        using StageScope timing = Measure(RenderStage.FogIntegrate);

        // The sky's harmonics carry the ambient a froxel scatters, when the atmosphere has run at all;
        // otherwise the flat ambient the seam handed over stands in for it.
        Sky.SkyAtmosphere sky = _deferred.Sky;
        bool skyValid = sky.IrradianceLut.Handle != 0 && _lighting.Model != LightingModel.Baked;

        VolumetricFogFrame frame = new(
            _fogSettings,
            _fogVolumes.AsMemory(0, _fogVolumeCount),
            _localLights,
            _localLightCount,
            _lighting.SunDirection,
            new Vector3f(
                _lighting.SunColour.X * _lighting.Scale,
                _lighting.SunColour.Y * _lighting.Scale,
                _lighting.SunColour.Z * _lighting.Scale),
            _lighting.AmbientColour,
            skyValid,
            _fogSettings.BaseHeight - _cameraPosition.Z);

        bool cascades = _shadowCascadeCount > 0 && _deferred.ShadowMap.Ready;

        _deferred.Fog.Record(
            _frameCommandBuffer,
            _frameLoop.CurrentFrame,
            frame,
            _deferred.ShadowMap.ArrayView,
            sky.IrradianceLut,
            _viewToWorld,
            _hasPreviousFrame ? _previousViewToWorld : _viewToWorld,
            _cameraDelta,
            _unprojection,
            _hasPreviousFrame ? _previousUnprojection : _unprojection,
            _cascadeMatrices,
            _cascadeSplits,
            cascades ? _shadowCascadeCount : 0,
            ShelterMapView(),
            HasShelterMap() ? _shelterTransform : ShelterMapTransform.None,
            _cameraPosition);

        _fogActive = true;
    }

    private unsafe void BeginGeometryPass()
    {
        if (_sceneTarget is null)
        {
            throw new GraphicsDeviceException("The scene target is not configured.");
        }

        // The glazing layer, wiped before anything can write it.
        //
        WriteSurfaceUniform();

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

    // The glazing front layer: one colour attachment for the pane, one depth buffer nobody else shares.
    //
    // Both are cleared. The colour has to be, because the reflection pass reads a pixel with no pane as
    // nought and last frame's pane would otherwise still be sitting there -- a window reflecting in a
    // place a window no longer is. The depth has to be, because it is what decides which pane is nearest
    // and it must start further away than any of them; REVERSE-Z, so that is nought and not one.
    private unsafe void BeginFrontLayerPass()
    {
        if (_sceneTarget is null)
        {
            throw new GraphicsDeviceException("The scene target is not configured.");
        }

        ClearValue* clears = stackalloc ClearValue[2];
        clears[0] = new ClearValue { Color = new ClearColorValue(0f, 0f, 0f, 0f) };
        clears[1] = new ClearValue { DepthStencil = new ClearDepthStencilValue(0f, 0) };

        RenderPassBeginInfo beginInfo = new()
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _deferred.FrontLayerPass,
            Framebuffer = _sceneTarget.FrontLayerFramebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), _sceneTarget.Extent),
            ClearValueCount = 2,
            PClearValues = clears,
        };

        _instance.Vk.CmdBeginRenderPass(_frameCommandBuffer, &beginInfo, SubpassContents.Inline);
        _deferredPassActive = true;
        ApplyViewportAndScissor(_frameCommandBuffer, _sceneViewport);
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
        // The frame-to-frame camera motion, formed HERE, at the first point in the frame anything
        // reprojects. It used to be formed at the end of the capture, after the shading pass -- and the
        // sun-shadow accumulation and the lamp denoiser, which both run in this gap, were reprojecting
        // every frame with the PREVIOUS frame's delta: at rest a harmless zero, on a walk a shadow
        // history dragged to where the camera was a frame ago. Differenced in double from the double
        // camera and narrowed to float only now -- see the field for why.
        UpdateCameraDelta();

        // The ray-traced sun shadow is the compute that lives in the gap between the two passes. It is a
        // shadow, so it reports under Shadows alongside any rasterised cascades.
        using (StageScope shadow = Measure(RenderStage.Shadows))
        {
            DispatchSunShadow();
        }

        // The ambient occlusion, likewise in the gap, so THIS frame's lighting shades with this frame's
        // occlusion. It used to be gathered after the shading pass and read by the next frame's lighting,
        // a frame late and never reprojected: on a walk every contact shadow and every dark halo sat
        // several pixels behind the thing that cast it, and the pass's own 87% history smeared that into
        // a trail behind every strut and pole. At rest the latency was invisible, which is how it stayed.
        DispatchOcclusion();
    }

    /// <summary>Forms this frame's camera motion from the two frames' double-precision positions.</summary>
    private void UpdateCameraDelta()
    {
        _cameraDelta = _hasPreviousFrame
            ? new Vector3f(
                (float)(_cameraPositionPrecise.X - _previousCameraPositionPrecise.X),
                (float)(_cameraPositionPrecise.Y - _previousCameraPositionPrecise.Y),
                (float)(_cameraPositionPrecise.Z - _previousCameraPositionPrecise.Z))
            : default;
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
            new ReadOnlySpan<float>(packed, floats),

            // The cut-out shapes and the records naming them, as the sun's trace takes them: without a
            // mask array the pass is handed the white texture and a zero address, and takes every hit as
            // solid.
            _opacityMasks?.View ?? _textures[_whiteTexture.Id].View,
            _opacityMasks is not null && _worldScene is { IsReady: true } records ? records.RecordAddress : 0UL);

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
        float jitterX = _taaSettings.JitterX * LampShadowParameters.JitterScale;
        float jitterY = _taaSettings.JitterY * VerticalJitterSign * LampShadowParameters.JitterScale;

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
            _cameraDelta,
            _unprojection,
            _previousUnprojection,
            hasHistory,
            jitterX,
            jitterY,
            _previousJitterX * LampShadowParameters.JitterScale,
            _previousJitterY * LampShadowParameters.JitterScale,
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

    /// <inheritdoc/>
    /// <remarks>
    /// The same product the first-person rig's lighting (<c>0x…</c> — see
    /// <c>VulkanGraphicsDevice.Draw3D</c>) and the selection glow divide by: the player's exposure less
    /// the night, times whatever the eye has adapted to. One outside the deferred pass, where a draw
    /// lands on a display-encoded target.
    /// </remarks>
    public float SceneExposure => _deferredPassActive
        ? MathF.Max(RenderCVars.EffectiveExposure * _lastAdaptedExposure, 1e-3f)
        : 1f;

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
                _cameraDelta,
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
    private Vector3f WorldSunDirection() => _lighting.WorldSunDirection(_viewToWorld);

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

        Span<float> light = stackalloc float[DeferredLightingParameters.LightFloats];

        DeferredLightingParameters.WriteLight(
            light,
            _lighting.SunDirection,
            (int)_lighting.Model,
            _lighting.SunColour,
            _lighting.Scale,
            _lighting.AmbientColour,
            (int)_debugView,

            // Whether the lighting pass should keep what the game drew where there is no depth, rather
            // than substituting the atmosphere for it. The frame's fact, not the bracket's -- the sky
            // was drawn and finished long before this pass is recorded.
            _frameHasPaintedSky,

            // Whether the hybrid model may add a sun to the bake, or whether the bake already has one.
            _lighting.CompleteBake,
            _lighting.Night);

        fixed (float* pushed = light)
        {
            vk.CmdPushConstants(
                cmd,
                _deferred.Lighting.PipelineLayout,
                ShaderStageFlags.FragmentBit,
                0,
                sizeof(float) * (uint)DeferredLightingParameters.LightFloats,
                pushed);
        }

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

    /// <summary>Fills this frame's surface block: how far the detail maps are trusted, and the floor sheen.</summary>
    /// <remarks>
    /// Written once as the geometry pass opens, into the buffer belonging to this frame in flight — whose
    /// fence the frame loop has already waited on, so nothing the GPU is still reading is being changed
    /// underneath it. The set that names the buffer was written when the renderer was built and never
    /// changes.
    /// </remarks>
    private unsafe void WriteSurfaceUniform()
    {
        Memory.VulkanBuffer surface = _deferred.SurfaceUniform(_frameLoop.CurrentFrame);
        float* u = stackalloc float[Deferred.DeferredRenderer.SurfaceUniformSize / sizeof(float)];
        u[0] = _materialDetail;
        u[1] = _floorGloss;
        u[2] = 0f;
        u[3] = 0f;
        surface.Write<float>(new ReadOnlySpan<float>(u, Deferred.DeferredRenderer.SurfaceUniformSize / sizeof(float)));
    }

    /// <summary>Fills this frame's GI uniform with the probe-field parameters and points the set at it.</summary>
    private unsafe void WriteGiUniform()
    {
        int frame = _frameLoop.CurrentFrame;
        Memory.VulkanBuffer gi = _deferred.GiUniform(frame);
        ProbeVolumes probes = _deferred.Probes;

        // The world-space radiance cache stands in for the baked field when one is offered, and it
        // describes its own grid: a different origin, spacing and count from the baked field's, so the
        // parameters have to come from whichever is actually bound or every lookup lands in the wrong
        // cell. It needs no baked data behind it -- it IS the field for this frame.
        RealtimeProbeField? realtime = _realtimeProbe;
        bool enabled = _volumeSettings.Enabled
            && (realtime is not null || (_hasProbeField && probes.HasData));

        Vector3f probeOrigin = realtime?.Origin ?? probes.Origin;
        float spacing = realtime?.Spacing ?? probes.Spacing;
        Vector3f inverseCount = realtime is { } live
            ? new Vector3f(1f / live.CountX, 1f / live.CountY, 1f / live.CountZ)
            : probes.InverseCount;

        float intensity = MathF.Max(_volumeSettings.Intensity, 0f);
        Matrix3f m = _viewToWorld;

        // Whether the screen atlas has anything in it. A strength with no texture behind it would have
        // the shading weight a slot holding a stand-in.
        bool screenGi = _screenIndirectStrength > 0f && _screenIndirect.IsValid;

        // Whether the cascades have been drawn yet. Kept apart from the count, because the splits and
        // the matrices describe how this frame divided the view either way.
        bool shadowsReady = _shadowCascadeCount > 0 && _deferred.ShadowMap.Ready;
        bool roofs = HasShelterMap();

        DeferredLightingState state = new(
            _unprojection,
            probeOrigin,
            enabled,
            inverseCount,
            spacing,
            _volumeSettings.Blend,
            _lighting.Scale,
            _sunShadowTraced,
            _volumeSettings.SkyColour,
            intensity,
            _cameraPosition,
            screenGi ? _screenIndirectStrength : 0f,
            _screenIndirectFullResolution,
            _aoSettings.Enabled,

            // The camera's height from the planet's centre, so the lighting reads the atmosphere's
            // tables with the same parameterisation the passes that built them used.
            Sky.SkyAtmosphere.ViewHeightKm(2f),
            m,
            _shadowCascadeCount,
            shadowsReady,
            _shadowSettings.DepthBias,
            _shadowSettings.NormalBias,
            _deferred.ShadowMap.Resolution > 0 ? 1f / _deferred.ShadowMap.Resolution : 0f,
            _wetness,
            roofs,
            roofs ? _shelterTransform : ShelterMapTransform.None,
            _fogActive ? _deferred.Fog.ZParams : FogSliceDistribution.None);

        Span<float> block = stackalloc float[DeferredLightingParameters.GiFloats];
        DeferredLightingParameters.WriteGi(block, state, _cascadeSplits, _cascadeMatrices);
        gi.Write<float>(block);


        (ImageView giView, Sampler giSampler, ImageLayout giLayout) = ResolveScreenGi();
        Deferred.ShadowMap shadowMap = _deferred.ShadowMap;
        Sky.SkyAtmosphere sky = _deferred.Sky;

        // This frame's own occlusion: gathered in the gap between the geometry and shading passes, from
        // the depth this frame stored, so it sits exactly under this frame's geometry.
        ImageView freshestAo = _sceneTarget!.AoView;

        Memory.VulkanBuffer lamps = WriteLampUniform(frame);

        // The four views the lighting samples as the probe field: the radiance cache's when one is
        // offered, the baked field's otherwise. A storage volume lives in the general layout for good,
        // which is where the compute that fills it leaves it.
        Deferred.ProbeFieldViews probeViews = _realtimeProbe is { } field
            ? new Deferred.ProbeFieldViews(
                ResolveStorageImage(field.Red).View,
                ResolveStorageImage(field.Green).View,
                ResolveStorageImage(field.Blue).View,
                ResolveStorageImage(field.Distance).View,
                ImageLayout.General)
            : default;

        _sceneTarget.WriteGi(
            probes, gi.Handle, (ulong)DeferredRenderer.GiUniformSize,
            lamps.Handle, (ulong)DeferredRenderer.LampUniformSize,
            _device.Capabilities.RayQuery ? _sceneTarget.LampFilteredView : default,
            giView, giSampler, giLayout,
            shadowMap.ArrayView, shadowMap.Sampler,
            new Deferred.SkyTables(sky.SkyViewLut, sky.TransmittanceLut, sky.IrradianceLut, sky.Sampler),
            freshestAo,
            TracedWorld(),
            _fogActive ? _deferred.Fog.Integrated?.View ?? default : default,
            _fogActive ? _deferred.Fog.Integrated?.Sampler ?? default : default,
            probeViews,
            ShelterMapView());
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
        DeferredLampState state = new(
            _frameCounter,

            // On wherever lamp shadows are asked for AT ALL: this needs no ray query, so it is what
            // keeps a lamp from lighting through a wall on an adapter that cannot trace, and what
            // shadows the SECOND lamp on a pixel the traced reservoir's one ray could not reach.
            RenderCVars.LocalLightShadows.Value != 0,

            // Needs an adapter that can trace AND the RT switch on, so it is OPTIONAL on hardware that
            // could trace but has been asked not to; and off until the world is built, so a level's
            // first frames trace against nothing rather than being shadowed by nothing.
            RenderCVars.LocalLightShadows.Value != 0
                && RenderCVars.HardwareRayTracing.Value
                && RenderCVars.TracedLampShadows.Value
                && _device.Capabilities.RayQuery
                && _worldScene is { IsReady: true } traced
                && traced.TopLevel is not null,

            // See r_local_light_source for why a cap belongs here rather than on the fixture itself.
            RenderCVars.LocalLightSource.Value);

        DeferredLampBlock.Write(u, state, _localLights.AsSpan(0, _localLightCount));
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

    /// <summary>Resolves what a reflected ray is traced against, or nothing for a frame that is not to trace.</summary>
    /// <remarks>
    /// <para>
    /// Three things have to be true at once and any of them can be false on a given frame: the adapter can
    /// trace, the player has asked for traced reflections, and the level has both a built world and a lit
    /// surface cache. The first two are the switches; the third is why this is asked every frame rather
    /// than once — a mission being loaded or torn down has a cache for only part of its life, and a trace
    /// against half of one does not come out coarse, it comes out wrong.
    /// </para>
    /// <para>
    /// <c>r_rt</c> gates this as well as <c>r_rt_reflections</c>, so that the one switch stands every trace
    /// in the renderer down together, exactly as it does for the sun and the lamps. With either off the
    /// reflections keep the screen march alone, which is what they were before this existed.
    /// </para>
    /// </remarks>
    /// <summary>Points the glazing pass's descriptor set at this frame's layer and parameters.</summary>
    /// <remarks>
    /// Written before the subpass opens and once for however many panes the level draws into it. The
    /// write is a host-side call rather than a command, so it has to land while this frame's set is not
    /// yet bound anywhere — which is exactly here, since the first glass draw is still to come and the
    /// frame before last has been waited on.
    /// </remarks>
    private void UpdateGlassResources()
    {
        if (_sceneTarget is not { } target)
        {
            return;
        }

        Vector3f fog = _fog.ColourFor(BlendMode.AlphaBlend);
        Span<float> p = stackalloc float[Pipelines.GlassPipeline.ParamFloats];

        p[0] = fog.X;
        p[1] = fog.Y;
        p[2] = fog.Z;
        p[3] = _fog.DensityPerMetre;

        _deferred.Glass.Update(_frameLoop.CurrentFrame, target.DepthView, p);
    }

    private Deferred.TracedResources TracedReflectionResources()
    {
        if (!_device.Capabilities.RayQuery)
        {
            ReportReflectionTrace("off: this adapter cannot trace");
            return Deferred.TracedResources.None;
        }

        if (!RenderCVars.HardwareRayTracing.Value)
        {
            ReportReflectionTrace("off: r_rt is 0");
            return Deferred.TracedResources.None;
        }

        if (!RenderCVars.TracedReflections.Value)
        {
            ReportReflectionTrace("off: r_rt_reflections is 0");
            return Deferred.TracedResources.None;
        }

        if (!_deferred.Reflection.Traces)
        {
            ReportReflectionTrace("off: the reflection pass was built without a traced half");
            return Deferred.TracedResources.None;
        }

        if (!_surfaceCache.IsPresent)
        {
            ReportReflectionTrace("off: no lit surface cache (SetSurfaceCache handed over nothing)");
            return Deferred.TracedResources.None;
        }

        if (_worldScene is not { IsReady: true } scene || scene.TopLevel is not { } topLevel)
        {
            ReportReflectionTrace("off: the world's acceleration structure is not built yet");
            return Deferred.TracedResources.None;
        }

        // Resolved only now, and only because the checks above passed. A handle whose resource the engine
        // has already released throws on resolution, and a frame that was never going to trace has no
        // business finding that out.
        if (!TryResolveCache(
            out Memory.VulkanBuffer? pages,
            out Memory.VulkanBuffer? cells,
            out Memory.VulkanBuffer? cellPages,
            out Compute.VulkanStorageImage? albedo,
            out Compute.VulkanStorageImage? cardDepth,
            out Compute.VulkanStorageImage? lighting))
        {
            ReportReflectionTrace("off: the surface cache named a resource this device no longer holds");
            return Deferred.TracedResources.None;
        }

        ReportReflectionTrace("on");

        return new Deferred.TracedResources(
            topLevel.Handle,
            pages.Handle,
            pages.Size,
            cells.Handle,
            cells.Size,
            cellPages.Handle,
            cellPages.Size,
            albedo.View,
            cardDepth.View,
            lighting.View,
            _surfaceCache.CameraToCache,
            _surfaceCache.LookupMinimum,
            _surfaceCache.LookupCell,
            _surfaceCache.LookupBuckets,
            _surfaceCache.AtlasWidth,
            _surfaceCache.AtlasHeight,
            _surfaceCache.BounceScale,

            // The third resolve route. Both of these may be absent without the trace standing down: a
            // pass that finds no records or no array shades the way it did before, out of the frame and
            // the cards, which is a reflection that is incomplete rather than one that is wrong.
            _tracedAlbedo is null ? 0UL : scene.RecordAddress,
            _tracedAlbedo?.View ?? default);
    }

    /// <summary>The cards and the distance field, for a reflection that MARCHES rather than traces.</summary>
    /// <returns>Everything the software half needs, or nothing when it cannot run.</returns>
    /// <remarks>
    /// <para>
    /// The software counterpart of <see cref="TracedReflectionResources"/>, and it resolves the SAME
    /// surface cache. That is the whole shape of the thing: an adapter without ray tracing is not missing
    /// the cards, only the acceleration structure — so the only substitution is the level's distance
    /// field, which a shader can walk to find where a ray stops.
    /// </para>
    /// <para>
    /// Resolved only now, as the traced one is, because a handle whose resource the engine has already
    /// released throws when resolved and a frame that was never going to march has no business finding
    /// that out.
    /// </para>
    /// </remarks>
    private Deferred.TracedResources SoftwareReflectionResources()
    {
        if (!_distanceField.IsPresent)
        {
            ReportGlassMarch("off: the level has no distance field yet");
            return GlassStandIns();
        }

        if (!_surfaceCache.IsPresent)
        {
            ReportGlassMarch("off: no lit surface cache (SetSurfaceCache handed over nothing)");
            return GlassStandIns();
        }

        if (!TryFindStorageImage(_distanceField.Clipmaps, out Compute.VulkanStorageImage? clipmaps))
        {
            ReportGlassMarch("off: the distance field named a volume this device no longer holds");
            return GlassStandIns();
        }

        if (!TryResolveCache(
            out Memory.VulkanBuffer? pages,
            out Memory.VulkanBuffer? cells,
            out Memory.VulkanBuffer? cellPages,
            out Compute.VulkanStorageImage? albedo,
            out Compute.VulkanStorageImage? cardDepth,
            out Compute.VulkanStorageImage? lighting))
        {
            ReportGlassMarch("off: the surface cache named a resource this device no longer holds");
            return GlassStandIns();
        }

        ReportGlassMarch("on: marching the distance field");

        return new Deferred.TracedResources(
            default,
            pages.Handle,
            pages.Size,
            cells.Handle,
            cells.Size,
            cellPages.Handle,
            cellPages.Size,
            albedo.View,
            cardDepth.View,
            lighting.View,
            _surfaceCache.CameraToCache,
            _surfaceCache.LookupMinimum,
            _surfaceCache.LookupCell,
            _surfaceCache.LookupBuckets,
            _surfaceCache.AtlasWidth,
            _surfaceCache.AtlasHeight,
            _surfaceCache.BounceScale,
            0UL,
            default,
            clipmaps.View,
            _distanceField);
    }

    /// <summary>A complete set of resources that describe nothing, for a frame that marches nothing.</summary>
    /// <returns>Handles that are all real, and a field of nought levels.</returns>
    /// <remarks>
    /// <para>
    /// <b>Why a frame with nothing to say still needs real handles.</b> A descriptor set is bound whole.
    /// Leaving a binding unwritten — or written with a null handle — is undefined at the moment of
    /// BINDING, not merely at the moment something reads it, so "the shader will not touch these anyway"
    /// is not an argument: the set is invalid before the shader runs. The marched half declares seven
    /// resources beyond the screen's own, and the ordinary case for a pane is that none of them exist yet
    /// — a mission still loading, baked lighting with no surface cache at all, a level whose distance
    /// field has not been built. Every one of those frames still wants the pass, because a pane with
    /// nothing to march still reflects the sky, and a window that goes black while a level loads is worse
    /// than one that shows daylight.
    /// </para>
    /// <para>
    /// One texel each, and shared by every binding that wants that shape. Nothing reads them: the field
    /// carries nought levels, which is what the march reads as "no field to walk", and the cards are only
    /// consulted after a hit the march can never report.
    /// </para>
    /// </remarks>
    private Deferred.TracedResources GlassStandIns()
    {
        if (!_glassStandInField.IsValid)
        {
            _glassStandInField = CreateVolumeTexture(1, 1, 1, VolumeFormat.R8, "glass.standin.field");
            _glassStandInCard = CreateStorageTexture(1, 1, VolumeFormat.R8, "glass.standin.card");
            _glassStandInRecords = CreateStorageBuffer(sizeof(float) * 4, "glass.standin.records");
        }

        if (!TryFindStorageImage(_glassStandInField, out Compute.VulkanStorageImage? field)
            || !TryFindStorageImage(_glassStandInCard, out Compute.VulkanStorageImage? card)
            || !TryFindStorageBuffer(_glassStandInRecords, out Memory.VulkanBuffer? records))
        {
            return Deferred.TracedResources.None;
        }

        return new Deferred.TracedResources(
            default,
            records.Handle,
            records.Size,
            records.Handle,
            records.Size,
            records.Handle,
            records.Size,
            card.View,
            card.View,
            card.View,
            default,
            default,
            0f,
            0,
            0,
            0,
            0f,
            0UL,
            default,
            field.View,
            DistanceFieldBinding.None);
    }

    /// <summary>Says whether glass is marching the field, and when it is not, which gate stopped it.</summary>
    /// <param name="state">The answer, as a line a player can read in the console.</param>
    /// <remarks>
    /// On CHANGE, for the reason the traced report is: every gate here opens and closes during a session,
    /// and the field and the cache both arrive part of the way through a mission load.
    /// </remarks>
    private void ReportGlassMarch(string state)
    {
        if (string.Equals(_glassMarchState, state, StringComparison.Ordinal))
        {
            return;
        }

        _glassMarchState = state;
        _logger.Log(LogLevel.Information, "Vulkan", $"Glass reflections (software): {state}.");
    }

    /// <summary>Says whether reflections are being traced, and when they are not, which gate stopped it.</summary>
    /// <param name="state">The answer, as a line a player can read in the console.</param>
    /// <remarks>
    /// Logged on CHANGE rather than once, and rather than every frame. Every gate here can open and close
    /// during a session — the switch is meant to be flipped live, and the world and the cards arrive part
    /// of the way through a mission load — so a once-only line would report whichever answer happened to
    /// be true on the first frame after a level opened and never correct itself. Every frame would be a
    /// wall of text. What a player toggling the setting actually needs is one line each time the answer
    /// moves, and that is this.
    /// </remarks>
    private void ReportReflectionTrace(string state)
    {
        if (string.Equals(_reflectionTraceState, state, StringComparison.Ordinal))
        {
            return;
        }

        _reflectionTraceState = state;
        _logger.Log(LogLevel.Information, "Vulkan", $"Traced reflections: {state}.");
    }

    /// <summary>Says, once per change, what the traced world actually contains.</summary>
    /// <remarks>
    /// Skinned geometry was excluded from the acceleration structure for most of this renderer's life,
    /// and the way that fails is a silence rather than an error: rays pass through every soldier and
    /// through the player, so a window reflects an empty room and a traced shadow falls where a body is
    /// standing. Nothing in the picture separates that from a reflection that works and simply has nobody
    /// in front of it, which is why the count is said out loud.
    /// </remarks>
    private void ReportTracedWorld()
    {
        if (_worldScene is not { } scene)
        {
            return;
        }

        string state =
            $"{scene.GeometryCount} geometry(ies), {scene.SkinnedGeometryCount} skinned, "
            + $"{scene.CasterGeometryCount} terrain caster(s), "
            + $"{scene.PlacementCount} placement(s), "

            // How many textures a ray can actually shade from. Nought here with a populated world means
            // every material-route hit reads the array's clear value, which looks like flat grey geometry
            // rather than like a registration that never happened.
            + $"{_tracedAlbedo?.Count ?? 0} albedo layer(s)"
            + (_tracedAlbedo?.Overflowed == true
                ? " (EXHAUSTED -- geometry admitted after that shades from the frame and the cards only)"
                : string.Empty);

        if (string.Equals(_tracedWorldState, state, StringComparison.Ordinal))
        {
            return;
        }

        _tracedWorldState = state;
        _logger.Log(LogLevel.Information, "Vulkan", $"Traced world: {state}.");
    }

    /// <summary>Says, once per change, whether any glazing was drawn and whether it traced.</summary>
    /// <remarks>
    /// Reported for the previous frame's count, at the point the next frame's glazing is about to be set
    /// up, because that is the one place in the frame where both numbers are known and neither has been
    /// reset yet. Rate-limited to changes for the reason the traced-reflection report is: it would
    /// otherwise be a line a frame forever.
    /// </remarks>
    private void ReportGlass()
    {
        string state = _glassDraws == 0
            ? "no pane reached the forward pass (nothing in view is marked glass in materials.json)"
            : _deferred.GlassReflection.Traces
                ? $"{_glassDraws} pane draw(s), reflections traced against the lit frame"
                : $"{_glassDraws} pane draw(s), sky reflection only (this adapter cannot trace)";

        if (string.Equals(_glassState, state, StringComparison.Ordinal))
        {
            return;
        }

        _glassState = state;
        _logger.Log(LogLevel.Information, "Vulkan", $"Glazing: {state}.");
    }

    /// <summary>Looks up every resource the surface cache named, or reports that one of them is gone.</summary>
    /// <remarks>
    /// Looked up rather than resolved: the resolving forms throw, which is right for a caller that has
    /// been handed a handle it was promised, and wrong here. The engine hands this over every frame,
    /// including the frames on either side of a mission change, so a stale handle is an ordinary event
    /// and the answer to it is to trace nothing this frame.
    /// </remarks>
    private bool TryResolveCache(
        [NotNullWhen(true)] out Memory.VulkanBuffer? pages,
        [NotNullWhen(true)] out Memory.VulkanBuffer? cells,
        [NotNullWhen(true)] out Memory.VulkanBuffer? cellPages,
        [NotNullWhen(true)] out Compute.VulkanStorageImage? albedo,
        [NotNullWhen(true)] out Compute.VulkanStorageImage? cardDepth,
        [NotNullWhen(true)] out Compute.VulkanStorageImage? lighting)
    {
        pages = null;
        cells = null;
        cellPages = null;
        albedo = null;
        cardDepth = null;
        lighting = null;

        return TryFindStorageBuffer(_surfaceCache.Pages, out pages)
            && TryFindStorageBuffer(_surfaceCache.Cells, out cells)
            && TryFindStorageBuffer(_surfaceCache.CellPages, out cellPages)
            && TryFindStorageImage(_surfaceCache.Albedo, out albedo)
            && TryFindStorageImage(_surfaceCache.Depth, out cardDepth)
            && TryFindStorageImage(_surfaceCache.Lighting, out lighting);
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

        /// <summary>The glazing front layer, in a pass of its own between the two.</summary>
        GlassLayer,

        /// <summary>The shading pass, on its translucency subpass.</summary>
        Forward,
    }
}
