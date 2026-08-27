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
    private TemporalAaSettings _taaSettings;
    private bool _temporalResolved;
    private int _captureWidth;
    private int _captureHeight;
    private ScenePass _currentScenePass = ScenePass.None;
    private SceneTarget? _sceneTarget;

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
        _sceneTarget = _deferred.Target(_frameLoop.CurrentFrame, _captureWidth, _captureHeight);

        // The gather holds its scene-texture handles for the life of the level, so what they point
        // at is refreshed here rather than the handles themselves changing.
        RefreshSceneTextures();

        // Before anything opens a render pass, because these are compute dispatches and a render pass is
        // the one place they cannot be recorded.
        RecordSkyUpdate();
        ApplyPendingProbeVolume();

        // The structure a shadow ray traces against, from everything the geometry pass has ever been asked
        // to draw. Recorded here for the same reason the sky is: a build cannot happen inside a render
        // pass, and the lighting subpass that traces against it is inside one.
        _worldScene?.Record(_frameCommandBuffer, _device.GraphicsQueue);

        _capturing = true;
        _deferredStage = DeferredStage.None;
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
            ApplyViewportAndScissor(_frameCommandBuffer, _viewport);
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

            BeginShadingPass();
            RecordLighting();
            _deferredStage = DeferredStage.Lighting;
        }

        _currentScenePass = ScenePass.None;
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

            // The pass has stored the depth; gather this frame's occlusion from it now, for the next frame's
            // lighting to read. The lighting already sampled the occlusion image this frame (its previous
            // contents), so a read-before-write barrier separates that from the compute write below.
            DispatchOcclusion();
            DispatchVelocity();
            DispatchTemporalResolve();

            // Everything temporal measures against this, so it moves on exactly once per captured frame.
            _previousViewToWorld = _viewToWorld;
            _previousCameraPosition = _cameraPosition;
            _previousUnprojection = _unprojection;
            _hasPreviousFrame = true;
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

        // The sun's colour and brightness as the level authored it. The reference's own sun is a fixed
        // ten-lux white-ish disc; this follows the game's, so a mission's own time of day still drives
        // the sky it produces.
        Vector3f illuminance = new(
            _lighting.SunColour.X * _lighting.Scale,
            _lighting.SunColour.Y * _lighting.Scale,
            _lighting.SunColour.Z * _lighting.Scale);

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

        _deferred.UpdateHistory.Record(
            cmd,
            _frameLoop.CurrentFrame,
            target.LitView,
            _velocityDilated ? target.DilatedVelocityView : target.VelocityView,
            historyColourView,
            historyMetadataView,
            target.UpscaleHistoryColourView,
            target.UpscaleHistoryMetadataView,
            target.HistoryRejectionView,
            _historyDecimated,
            _captureWidth,
            _captureHeight,
            Deferred.SceneTarget.HistoryScale,
            temporalOn: true,
            maxSamples: 16f,
            _taaSettings.JitterX,
            _taaSettings.JitterY * VerticalJitterSign);

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
            Deferred.SceneTarget.HistoryScale);

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

    /// <summary>Meters the lit scene for the eye adaptation, before the present pass opens.</summary>
    private unsafe void RecordExposureMetering()
    {
        if (_sceneTarget is not { } target)
        {
            return;
        }

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

        OcclusionBarrier(cmd, ao, AccessFlags.ShaderReadBit, AccessFlags.ShaderWriteBit,
            PipelineStageFlags.FragmentShaderBit, PipelineStageFlags.ComputeShaderBit);

        _deferred.Ssao.Record(
            cmd,
            _frameLoop.CurrentFrame,
            target.DepthView,
            target.AoView,
            _captureWidth,
            _captureHeight,
            _unprojection.XScale,
            _unprojection.YScale,
            _unprojection.Near,
            _unprojection.Far,
            _aoSettings.Radius,
            _aoSettings.Intensity);

        OcclusionBarrier(cmd, ao, AccessFlags.ShaderWriteBit, AccessFlags.ShaderReadBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.FragmentShaderBit);
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
        bool sceneIsShaded = _debugView is DebugView.None or DebugView.Lighting;

        // The shutter, first, because everything after it is lens behaviour and a lens sees the smeared
        // picture. The reference orders it the same way: motion blur, then bloom, then the tone map --
        // which is why a bright moving highlight blooms along its own smear rather than from where it
        // no longer is.
        bool motionBlurred = false;
        if (post.MotionBlur > 0.001f && sceneIsShaded && _sceneTarget is { } motionTarget)
        {
            using StageScope motionTiming = Measure(RenderStage.CameraEffects);
            LitReadBarrier(cmd, motionTarget.LitImage, PipelineStageFlags.ComputeShaderBit);
            _deferred.MotionBlur.Record(
                cmd,
                _frameLoop.CurrentFrame,
                motionTarget,
                sceneColour,
                _captureWidth,
                _captureHeight,
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
            _deferred.Bloom.Resize(_captureWidth, _captureHeight);
            bloomTarget.WriteBloom(_deferred.Bloom.ResultView(_frameLoop.CurrentFrame));

            if (!motionBlurred)
            {
                LitReadBarrier(cmd, bloomTarget.LitImage, PipelineStageFlags.ComputeShaderBit);
            }

            _deferred.Bloom.Record(cmd, _frameLoop.CurrentFrame, sceneColour, post.BloomIntensity);
            bloomRan = true;
        }

        float adaptedExposure = 1f;
        if (post.AutoExposure && _debugView is DebugView.None or DebugView.Lighting)
        {
            RecordExposureMetering();
            adaptedExposure = _deferred.AutoExposure.Adapt(_frameLoop.CurrentFrame, 1f / 60f);
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
        bool maskView = _debugView is not DebugView.None and not DebugView.Lighting;

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
    private static Matrix4f ComposeCascade(in ShadowCascade cascade)
    {
        // The light view is the world-to-light rotation, camera-relative so it carries no translation.
        Matrix3f r = cascade.LightRotation.Transposed();
        Matrix4f lightView = new(
            r.M11, r.M12, r.M13, 0f,
            r.M21, r.M22, r.M23, 0f,
            r.M31, r.M32, r.M33, 0f,
            0f, 0f, 0f, 1f);
        return cascade.Projection * lightView;
    }

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
        ApplyViewportAndScissor(_frameCommandBuffer, _viewport);
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

    /// <summary>Traces the sun's shadow against the scene, for the shading pass to read this same frame.</summary>
    private unsafe void DispatchSunShadow()
    {
        if (_sceneTarget is null || _deferred.SunShadow is null)
        {
            return;
        }

        // The app's word governs: shadows off means no sun shadow of any kind, and the software mode
        // leaves the sun to the cascades — the trace never runs, and the lighting's flag stays off.
        if (!_shadowSettings.Enabled || !_shadowSettings.TracedSun)
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

        _deferred.SunShadowDenoise?.Record(
            cmd,
            _frameLoop.CurrentFrame,
            target.SunShadowRawView,
            target.DepthView,
            target.SunShadowView,
            _captureWidth,
            _captureHeight,
            _unprojection,
            RenderCVars.SunAngle.Value);

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

        gi.Write<float>(new ReadOnlySpan<float>(u, DeferredRenderer.GiUniformSize / sizeof(float)));

        (ImageView giView, Sampler giSampler, ImageLayout giLayout) = ResolveScreenGi();
        Deferred.ShadowMap shadowMap = _deferred.ShadowMap;
        Sky.SkyAtmosphere sky = _deferred.Sky;
        _sceneTarget!.WriteGi(
            probes, gi.Handle, (ulong)DeferredRenderer.GiUniformSize, giView, giSampler, giLayout,
            shadowMap.ArrayView, shadowMap.Sampler,
            new Deferred.SkyTables(sky.SkyViewLut, sky.TransmittanceLut, sky.IrradianceLut, sky.Sampler));
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
