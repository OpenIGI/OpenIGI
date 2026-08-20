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

    // The screen-probe gather's near-field atlas, fed each frame it is traced.
    private TextureHandle _screenIndirect;
    private float _screenIndirectStrength;
    private bool _screenIndirectFullResolution;

    // The sun's cascaded shadow map: the composed world-to-cascade-clip matrices, the view depths the
    // cascades stop at, and the pass state while a cascade is being rendered.
    private int _shadowCascadeCount;
    private readonly Matrix4f[] _cascadeMatrices = new Matrix4f[ShadowSettings.MaxCascades];
    private readonly float[] _cascadeSplits = new float[ShadowSettings.MaxCascades];
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
    private int _deferredSubpass = -1;
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

        // Before anything opens a render pass, because these are compute dispatches and a render pass is
        // the one place they cannot be recorded.
        RecordSkyUpdate();

        _capturing = true;
        _deferredSubpass = -1;
        return true;
    }

    /// <inheritdoc/>
    public void BeginPass(ScenePass pass)
    {
        ThrowIfDisposed();
        if (!_capturing)
        {
            throw new GraphicsDeviceException("No scene capture is in progress.");
        }

        _currentScenePass = pass;
        if (pass == ScenePass.Geometry)
        {
            BeginDeferredPass();
            _deferredSubpass = (int)DeferredRenderPass.GeometrySubpass;
        }
        else if (pass == ScenePass.Forward)
        {
            _instance.Vk.CmdNextSubpass(_frameCommandBuffer, SubpassContents.Inline);
            _deferredSubpass = (int)DeferredRenderPass.ForwardSubpass;
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

        // Ending the geometry pass is what triggers the lighting subpass: advance to it and draw the
        // full-screen light that reads the G-buffer.
        if (_deferredSubpass == (int)DeferredRenderPass.GeometrySubpass)
        {
            _instance.Vk.CmdNextSubpass(_frameCommandBuffer, SubpassContents.Inline);
            RecordLighting();
            _deferredSubpass = (int)DeferredRenderPass.LightingSubpass;
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
            while (_deferredSubpass < (int)DeferredRenderPass.ForwardSubpass)
            {
                _instance.Vk.CmdNextSubpass(_frameCommandBuffer, SubpassContents.Inline);
                _deferredSubpass++;
            }

            _instance.Vk.CmdEndRenderPass(_frameCommandBuffer);
            _deferredPassActive = false;

            // The pass has stored the depth; gather this frame's occlusion from it now, for the next frame's
            // lighting to read. The lighting already sampled the occlusion image this frame (its previous
            // contents), so a read-before-write barrier separates that from the compute write below.
            DispatchOcclusion();
            DispatchVelocity();

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
        Vector3f viewSun = _lighting.SunDirection;
        Matrix3f m = _viewToWorld;
        Vector3f worldSun = new(
            (m.M11 * viewSun.X) + (m.M12 * viewSun.Y) + (m.M13 * viewSun.Z),
            (m.M21 * viewSun.X) + (m.M22 * viewSun.Y) + (m.M23 * viewSun.Z),
            (m.M31 * viewSun.X) + (m.M32 * viewSun.Y) + (m.M33 * viewSun.Z));

        float length = MathF.Sqrt((worldSun.X * worldSun.X) + (worldSun.Y * worldSun.Y) + (worldSun.Z * worldSun.Z));
        worldSun = length > 1e-5f
            ? new Vector3f(worldSun.X / length, worldSun.Y / length, worldSun.Z / length)
            : new Vector3f(0f, 0f, 1f);

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
            Sky.SkyAtmosphere.ViewHeightKm(2f));
    }

    /// <summary>Runs the ambient-occlusion compute over the stored depth, writing this frame's occlusion.</summary>
    private unsafe void DispatchOcclusion()
    {
        if (!_aoSettings.Enabled || _sceneTarget is null)
        {
            return;
        }

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
        if (_sceneTarget is null)
        {
            return;
        }

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

    }

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

        EnsurePresentPass();

        Vk vk = _instance.Vk;
        CommandBuffer cmd = _frameCommandBuffer;
        vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _deferred.Resolve.Pipeline);

        DescriptorSet set = _sceneTarget.ResolveSet;
        vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _deferred.Resolve.PipelineLayout, 0, 1, &set, 0, null);

        float* parameters = stackalloc float[4] { post.Exposure, (int)post.ToneMap, post.WhitePoint, 0f };
        vk.CmdPushConstants(cmd, _deferred.Resolve.PipelineLayout, ShaderStageFlags.FragmentBit, 0, sizeof(float) * 4, parameters);

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

    /// <inheritdoc/>
    /// <remarks>Uploads the field into the probe volumes the lighting pass samples; null clears it.</remarks>
    public void SetIrradianceVolume(IrradianceVolume? volume)
    {
        ThrowIfDisposed();
        if (volume is null || volume.ProbeCount == 0)
        {
            _deferred.Probes.Clear();
            _hasProbeField = false;
            return;
        }

        _deferred.Probes.Update(volume);
        _hasProbeField = true;
    }

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

    /// <inheritdoc/>
    /// <remarks>The GI gather that reads these is not yet offered on the Vulkan path.</remarks>
    public TextureHandle SceneDepth => TextureHandle.None;

    /// <inheritdoc/>
    /// <remarks>The GI gather that reads these is not yet offered on the Vulkan path.</remarks>
    public TextureHandle SceneNormal => TextureHandle.None;

    /// <inheritdoc/>
    /// <remarks>The GI gather that reads these is not yet offered on the Vulkan path.</remarks>
    public TextureHandle SceneColour => TextureHandle.None;

    private unsafe void BeginDeferredPass()
    {
        if (_sceneTarget is null)
        {
            throw new GraphicsDeviceException("The scene target is not configured.");
        }

        ClearValue* clears = stackalloc ClearValue[6];
        for (int i = 0; i < 4; i++)
        {
            clears[i] = new ClearValue { Color = new ClearColorValue(0f, 0f, 0f, 0f) };
        }

        clears[4] = new ClearValue { DepthStencil = new ClearDepthStencilValue(0.0f, 0) };
        clears[5] = new ClearValue
        {
            Color = new ClearColorValue(_clearColour.R / 255f, _clearColour.G / 255f, _clearColour.B / 255f, 1f),
        };

        RenderPassBeginInfo beginInfo = new()
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _deferred.RenderPass,
            Framebuffer = _sceneTarget.Framebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), _sceneTarget.Extent),
            ClearValueCount = 6,
            PClearValues = clears,
        };

        FlushComputeBeforePass();
        _instance.Vk.CmdBeginRenderPass(_frameCommandBuffer, &beginInfo, SubpassContents.Inline);
        _deferredPassActive = true;
    }

    private unsafe void RecordLighting()
    {
        if (_sceneTarget is null)
        {
            return;
        }

        Vk vk = _instance.Vk;
        CommandBuffer cmd = _frameCommandBuffer;

        WriteGiUniform();

        vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _deferred.Lighting.Pipeline);

        DescriptorSet set = _sceneTarget.LightingSet;
        vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _deferred.Lighting.PipelineLayout, 0, 1, &set, 0, null);

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
        light[11] = 0f;
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

        // uProbeMisc: half-spacing normal offset, blend, light scale, unused.
        u[12] = spacing * 0.5f; u[13] = Math.Clamp(_volumeSettings.Blend, 0f, 1f);
        u[14] = MathF.Max(_lighting.Scale, 0f); u[15] = 0f;

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
}
