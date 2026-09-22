using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Pipelines;
using OpenIGI.Rendering.Vulkan.Shaders;
using OpenIGI.Shaders;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Deferred;

/// <summary>
/// Owns the deferred pipeline — the render pass, the geometry, lighting, forward and resolve pipelines,
/// and one scene target per frame in flight — and hands the device what it needs to record a deferred
/// frame.
/// </summary>
/// <remarks>
/// The device drives the frame (it holds the command buffer); this holds the pieces that outlive a
/// frame. The forward pipeline is the same mesh shader the direct path uses, built against the deferred
/// render pass's translucency subpass so it draws into the lit HDR scene rather than the back buffer.
/// </remarks>
internal sealed unsafe class DeferredRenderer : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly DeferredRenderPass _renderPass;
    private readonly DescriptorSetLayout _lightingLayout;
    private readonly DescriptorSetLayout _resolveLayout;
    private readonly DescriptorPool _pool;
    private readonly Sampler _sampler;
    private readonly SceneTarget[] _targets;
    private readonly ProbeVolumes _probes;
    private readonly VulkanBuffer[] _giUniforms;
    private readonly VulkanBuffer[] _lampUniforms;
    private readonly VulkanBuffer[] _surfaceUniforms;
    private readonly DescriptorSetLayout _surfaceLayout;
    private readonly DescriptorSet[] _surfaceSets;
    private readonly ShadowMap _shadowMap;
    private readonly ShadowPipeline _shadowPipeline;
    private readonly bool _rayQuery;
    private readonly RayTracedShadowPass? _sunShadow;
    private readonly ShadowDenoisePass? _sunShadowDenoise;
    private readonly ShadowAccumulatePass? _sunShadowAccumulate;
    private readonly LampShadowPass? _lampShadow;
    private readonly LampDenoisePass? _lampDenoise;
    private readonly SsaoPass _ssao;
    private readonly VolumetricFogPass _fog;
    private readonly ReflectionPass _reflection;
    private readonly AutoExposurePass _autoExposure;
    private readonly VelocityPass _velocity;
    private readonly ObjectVelocityPass _objectVelocity;
    private readonly BloomPass _bloom;
    private readonly MotionBlurPass _motionBlur;
    private readonly Tsr.DilateVelocityPass _dilateVelocity;
    private readonly Tsr.DecimateHistoryPass _decimateHistory;
    private readonly Tsr.UpdateHistoryPass _updateHistory;
    private readonly Tsr.SpatialAntiAliasingPass _spatialAntiAliasing;
    private readonly Tsr.ThinGeometryPass _thinGeometry;
    private readonly Tsr.RejectShadingPass _rejectShading;
    private readonly Tsr.RejectionDilatePass _rejectionDilate;
    private readonly Tsr.ResolveHistoryPass _resolveHistory;
    private readonly Sky.SkyAtmosphere _sky;
    private bool _disposed;

    /// <summary>The size in bytes of the lighting pass's GI uniform block.</summary>
    public const int GiUniformSize = 576;

    /// <summary>How many resolve sets each scene target carries.</summary>
    /// <remarks>
    /// One per picture the resolve might be handed: the raw lit colour, the temporally resolved colour,
    /// and the motion-blurred one. Which a frame draws with depends on which passes ran, and all three
    /// are written ahead of the frame rather than one being rewritten inside it.
    /// </remarks>
    private const uint ResolveSetsPerTarget = 3;

    /// <summary>The size in bytes of the geometry pass's per-frame surface block.</summary>
    /// <remarks>
    /// One vector: how strongly the derived detail maps are trusted, and how far an up-facing surface's
    /// roughness is smoothed towards a floor's. Both are per FRAME rather than per draw, and the
    /// geometry pass's push block is full to the last float, so they arrive as a set of their own.
    /// </remarks>
    public const int SurfaceUniformSize = 16;

    /// <summary>The size in bytes of the lighting pass's local-light block.</summary>
    /// <remarks>
    /// Two <c>vec4</c>s of counts and control, then three per light: position and reach, colour and
    /// strength, and the direction it faces. Sized for <see cref="DeferredLocalLight.Maximum"/>, which is
    /// what the shader's arrays are declared at — well inside the sixteen kilobytes every Vulkan
    /// implementation guarantees a uniform buffer, so no device has to be asked.
    /// </remarks>
    public const int LampUniformSize = 32 + (DeferredLocalLight.Maximum * 48);

    /// <summary>Initialises a new instance of the <see cref="DeferredRenderer"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device everything lives on.</param>
    /// <param name="allocator">The allocator backing the scene targets.</param>
    /// <param name="commands">The command pool the probe uploads come from.</param>
    /// <param name="textureSetLayout">The mesh texture set, shared with the forward and shadow paths.</param>
    /// <param name="presentRenderPass">The back-buffer render pass the resolve draws into.</param>
    /// <param name="framesInFlight">How many scene targets to keep — one per frame in flight.</param>
    /// <exception cref="GraphicsDeviceException">A resource could not be created.</exception>
    public DeferredRenderer(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        VulkanCommands commands,
        DescriptorSetLayout textureSetLayout,
        RenderPass presentRenderPass,
        int framesInFlight)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(commands);
        _rayQuery = device.Capabilities.RayQuery;
        _vk = vk;
        _device = device.Device;

        _probes = new ProbeVolumes(vk, device, allocator, commands);
        _shadowMap = new ShadowMap(vk, device, allocator, commands);
        _shadowPipeline = new ShadowPipeline(vk, device, _shadowMap.RenderPass, textureSetLayout);
        _ssao = new SsaoPass(vk, device, allocator, framesInFlight);
        _fog = new VolumetricFogPass(vk, device, allocator, commands, framesInFlight);
        // The reflections, with their traced half wherever the adapter can trace. Compiled in rather
        // than switched on: the extension has to be declared for the shader to parse at all, so whether
        // there IS a trace is settled here and whether a frame USES it is settled per frame.
        _reflection = new ReflectionPass(vk, device, allocator, framesInFlight, _rayQuery);
        _velocity = new VelocityPass(vk, device, allocator, framesInFlight);
        _objectVelocity = new ObjectVelocityPass(vk, device, DeferredRenderPass.DepthFormat);
        _bloom = new BloomPass(vk, device, allocator, commands, framesInFlight);
        _motionBlur = new MotionBlurPass(vk, device, allocator, framesInFlight);
        _dilateVelocity = new Tsr.DilateVelocityPass(vk, device, allocator, framesInFlight);
        _decimateHistory = new Tsr.DecimateHistoryPass(vk, device, allocator, framesInFlight);
        _updateHistory = new Tsr.UpdateHistoryPass(vk, device, allocator, framesInFlight);
        _spatialAntiAliasing = new Tsr.SpatialAntiAliasingPass(vk, device, allocator, framesInFlight);
        _thinGeometry = new Tsr.ThinGeometryPass(vk, device, allocator, framesInFlight);
        _rejectShading = new Tsr.RejectShadingPass(vk, device, allocator, framesInFlight);
        _rejectionDilate = new Tsr.RejectionDilatePass(vk, device, allocator, framesInFlight);
        _resolveHistory = new Tsr.ResolveHistoryPass(vk, device, allocator, framesInFlight);
        _sky = new Sky.SkyAtmosphere(vk, device, allocator, commands, framesInFlight);
        _autoExposure = new AutoExposurePass(vk, device, allocator, commands, framesInFlight);

        // The sun's shadow, traced between the two render passes. Only where the adapter can trace at all.
        _sunShadow = _rayQuery
            ? new RayTracedShadowPass(vk, device, allocator, framesInFlight)
            : null;
        _sunShadowDenoise = _rayQuery
            ? new ShadowDenoisePass(vk, device, allocator, framesInFlight)
            : null;
        _sunShadowAccumulate = _rayQuery
            ? new ShadowAccumulatePass(vk, device, allocator, framesInFlight)
            : null;
        _lampShadow = _rayQuery
            ? new LampShadowPass(vk, device, allocator, framesInFlight)
            : null;
        _lampDenoise = _rayQuery
            ? new LampDenoisePass(vk, device, allocator, framesInFlight)
            : null;
        _giUniforms = new VulkanBuffer[framesInFlight];
        _lampUniforms = new VulkanBuffer[framesInFlight];
        _surfaceUniforms = new VulkanBuffer[framesInFlight];
        for (int i = 0; i < framesInFlight; i++)
        {
            _surfaceUniforms[i] = new VulkanBuffer(
                vk, device, allocator, SurfaceUniformSize,
                BufferUsageFlags.UniformBufferBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
            _giUniforms[i] = new VulkanBuffer(
                vk, device, allocator, GiUniformSize,
                BufferUsageFlags.UniformBufferBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
            _lampUniforms[i] = new VulkanBuffer(
                vk, device, allocator, LampUniformSize,
                BufferUsageFlags.UniformBufferBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        }

        _renderPass = new DeferredRenderPass(vk, _device);
        _lightingLayout = CreateLightingLayout();
        _resolveLayout = CreateResolveLayout();
        _pool = CreatePool(framesInFlight);
        _sampler = CreateSampler();
        _surfaceLayout = CreateSurfaceLayout();
        _surfaceSets = AllocateSurfaceSets(framesInFlight);

        Geometry = new GBufferPipeline(vk, device, _renderPass.Geometry, textureSetLayout, _surfaceLayout);
        // The push range comes from the module's own reflection rather than a constant written here.
        // It was written here, at forty-eight bytes, while the draw pushes sixty-four: the fourth vector
        // -- whether the title draws its own sky, and whether its bakes are whole -- lay outside the
        // declared range and was read back out of undefined memory.
        Lighting = new FullscreenPipeline(
            vk,
            device,
            ShaderModuleId.Shading,
            ShadingVariant,
            _lightingLayout,
            _renderPass.Shading,
            DeferredRenderPass.LightingSubpass);
        Resolve = new FullscreenPipeline(
            vk,
            device,
            ShaderModuleId.Resolve,
            _resolveLayout,
            presentRenderPass,
            0);
        Forward = new Mesh3DPipeline(vk, device, _renderPass.Shading, DeferredRenderPass.ForwardSubpass);

        // Glazing, on the same subpass and drawn from the same vertex buffers, and built with the same
        // set-0 layout, so a texture pairing cached for an opaque draw binds under either without being
        // built twice. It records each pane pixel into a layer; what a pane REFLECTS is
        // finished by GlassReflection, after the render pass closes and the lit frame becomes readable.
        Glass = new GlassPipeline(
            vk,
            device,
            allocator,
            _renderPass.Shading,
            DeferredRenderPass.ForwardSubpass,
            _renderPass.FrontLayer,
            _sampler,
            textureSetLayout,
            framesInFlight);

        GlassReflection = new GlassReflectionPass(vk, device, allocator, framesInFlight, _rayQuery);

        // Each target writes its descriptors by the names the two shaders declare rather than by numbers
        // kept in step with them, so the tables are built once here and shared.
        ShaderBindingTable lightingBindings = new(ShadingReflection);
        ShaderBindingTable resolveBindings = new(ResolveReflection);

        _targets = new SceneTarget[framesInFlight];
        for (int i = 0; i < framesInFlight; i++)
        {
            _targets[i] = new SceneTarget(
                vk, device, allocator, commands,
                _renderPass.Geometry, _renderPass.Shading, _renderPass.FrontLayer,
                _lightingLayout, _resolveLayout, lightingBindings, resolveBindings, _pool, _sampler);

            // The object-velocity framebuffers are keyed on the velocity and depth view handles, so they
            // are dropped the moment any target destroys its views -- on every rebuild, for every reason,
            // rather than on the one condition a caller happened to think of.
            _targets[i].Rebuilt += _objectVelocity.InvalidateFramebuffers;
        }
    }

    /// <summary>Gets the pass that fills the G-buffer.</summary>
    public RenderPass GeometryPass => _renderPass.Geometry;

    /// <summary>Gets the pass that shades the G-buffer and draws translucency over it.</summary>
    public RenderPass ShadingPass => _renderPass.Shading;

    /// <summary>Gets the pass that records which pane of glazing is nearest at each pixel.</summary>
    public RenderPass FrontLayerPass => _renderPass.FrontLayer;

    /// <summary>Gets the geometry (G-buffer) pipeline.</summary>
    public GBufferPipeline Geometry { get; }

    /// <summary>Gets the lighting pipeline.</summary>
    public FullscreenPipeline Lighting { get; }

    /// <summary>Gets the resolve pipeline.</summary>
    public FullscreenPipeline Resolve { get; }

    /// <summary>Gets the forward pipeline for translucency, drawn into the lit scene.</summary>
    public Mesh3DPipeline Forward { get; }

    /// <summary>Gets the glazing pipeline, which draws window panes over the lit scene.</summary>
    public GlassPipeline Glass { get; }

    /// <summary>Gets the pass that traces what the glazing reflects, once the frame is readable.</summary>
    public GlassReflectionPass GlassReflection { get; }

    /// <summary>Gets the irradiance probe field the lighting pass samples.</summary>
    public ProbeVolumes Probes => _probes;

    /// <summary>Gets a frame-in-flight's GI uniform buffer, for the lighting pass's probe parameters.</summary>
    /// <param name="frame">The frame-in-flight index.</param>
    /// <returns>The buffer.</returns>
    public VulkanBuffer GiUniform(int frame) => _giUniforms[frame];

    /// <summary>Gets the geometry pass's per-frame surface buffer for a frame in flight.</summary>
    /// <param name="frame">The frame-in-flight index.</param>
    /// <returns>The buffer to write before the geometry pass records.</returns>
    public VulkanBuffer SurfaceUniform(int frame) => _surfaceUniforms[frame];

    /// <summary>Gets the descriptor set that points at that buffer.</summary>
    /// <param name="frame">The frame-in-flight index.</param>
    /// <returns>The set, bound as set one under the geometry pipeline layout.</returns>
    public DescriptorSet SurfaceSet(int frame) => _surfaceSets[frame];

    /// <summary>Gets the local-light uniform buffer for a frame in flight.</summary>
    /// <param name="frame">The frame-in-flight index.</param>
    /// <returns>The buffer the lamps for that frame are written into.</returns>
    public VulkanBuffer LampUniform(int frame) => _lampUniforms[frame];

    /// <summary>Gets the sun's cascaded shadow map.</summary>
    public ShadowMap ShadowMap => _shadowMap;

    /// <summary>Gets the depth-only pipeline the shadow cascades render with.</summary>
    public ShadowPipeline ShadowPipeline => _shadowPipeline;

    /// <summary>Gets the ambient-occlusion compute pass.</summary>
    public SsaoPass Ssao => _ssao;

    /// <summary>Gets the froxel volumetric fog the shading pass reads through.</summary>
    public VolumetricFogPass Fog => _fog;

    /// <summary>Gets the screen-space reflection pass.</summary>
    public ReflectionPass Reflection => _reflection;

    /// <summary>Gets the pass that works out where each pixel's surface was on the previous frame.</summary>
    public VelocityPass Velocity => _velocity;

    /// <summary>Gets the raster pass that stamps each animated mesh's own motion over the velocity.</summary>
    public ObjectVelocityPass ObjectVelocity => _objectVelocity;

    /// <summary>Gets the bloom pyramid, the lens scatter the reference composites before its tone map.</summary>
    public BloomPass Bloom => _bloom;

    /// <summary>Gets the pass that smears the frame along what moved in it.</summary>
    public MotionBlurPass MotionBlur => _motionBlur;

    /// <summary>Gets the pass that dilates screen motion onto the closest surface around each pixel.</summary>
    public Tsr.DilateVelocityPass DilateVelocity => _dilateVelocity;

    /// <summary>Gets the pass that decides how far the reprojected history can be believed.</summary>
    public Tsr.DecimateHistoryPass DecimateHistory => _decimateHistory;

    /// <summary>Gets the pass that accumulates the frame into the super-sampled history.</summary>
    public Tsr.UpdateHistoryPass UpdateHistory => _updateHistory;

    /// <summary>Gets the pass that locates each edge between pixel centres for the history update.</summary>
    public Tsr.SpatialAntiAliasingPass SpatialAntiAliasing => _spatialAntiAliasing;

    /// <summary>Gets the pass that keeps the history alive on geometry one pixel wide.</summary>
    public Tsr.ThinGeometryPass ThinGeometry => _thinGeometry;

    /// <summary>Gets the pass that decides how much of the history this frame's shading still allows.</summary>
    public Tsr.RejectShadingPass RejectShading => _rejectShading;

    /// <summary>Gets the pass that filters the shading rejection into the history update's clamp blend.</summary>
    public Tsr.RejectionDilatePass RejectionDilate => _rejectionDilate;

    /// <summary>Gets the pass that filters the history back down to the frame's own size.</summary>
    public Tsr.ResolveHistoryPass ResolveHistory => _resolveHistory;

    /// <summary>
    /// Gets a value indicating whether the lighting traces the sun's shadow against the scene rather than
    /// reading it from the cascades.
    /// </summary>
    /// <remarks>
    /// Also says whether the lighting's descriptor layout has a slot for the scene at all, so a caller
    /// knows not to offer one it cannot hold.
    /// </remarks>
    public bool TracesShadows => _rayQuery;

    /// <summary>Gets the pass that traces the sun's shadow, or null where the adapter cannot trace.</summary>
    public RayTracedShadowPass? SunShadow => _sunShadow;

    /// <summary>Gets the pass that spreads the traced shadow across its penumbra's real width.</summary>
    public ShadowDenoisePass? SunShadowDenoise => _sunShadowDenoise;

    /// <summary>Gets the pass that averages the traced sun shadow over time, if the adapter can trace.</summary>
    public ShadowAccumulatePass? SunShadowAccumulate => _sunShadowAccumulate;

    /// <summary>Gets the pass that traces one lamp shadow ray per pixel, if the adapter can trace.</summary>
    public LampShadowPass? LampShadow => _lampShadow;

    /// <summary>Gets the pass that cleans up that trace, if the adapter can trace.</summary>
    public LampDenoisePass? LampDenoise => _lampDenoise;

    /// <summary>Gets the atmosphere: the sky's colour, the sun's transmittance and the sky's own light.</summary>
    public Sky.SkyAtmosphere Sky => _sky;

    /// <summary>Gets the histogram metering the resolve's exposure adapts by.</summary>
    public AutoExposurePass AutoExposure => _autoExposure;

    /// <summary>Gets the scene target for a frame in flight, built at the render size.</summary>
    /// <param name="frameIndex">The frame-in-flight index.</param>
    /// <param name="width">The render width in pixels.</param>
    /// <param name="height">The render height in pixels.</param>
    /// <param name="outputWidth">The presented width, or 0 for the render width.</param>
    /// <param name="outputHeight">The presented height, or 0 for the render height.</param>
    /// <returns>The ready scene target.</returns>
    public SceneTarget Target(int frameIndex, int width, int height, int outputWidth = 0, int outputHeight = 0)
    {
        SceneTarget target = _targets[frameIndex];
        target.Ensure(width, height, outputWidth, outputHeight);
        return target;
    }

    /// <summary>Gets the target the previous frame rendered into.</summary>
    /// <param name="frameIndex">This frame's frame-in-flight index.</param>
    /// <returns>The previous frame's target, holding its depth, normals and base colour.</returns>
    /// <remarks>
    /// <para>
    /// There is no separate copy of the previous frame: with two frames in flight, the slot this frame is
    /// not using is the one the previous frame used, and it still holds everything that frame wrote. That
    /// is what a temporal pass needs to tell a surface it can accumulate onto from one that has just come
    /// into view — comparing this frame's depth and normal against the previous frame's at the reprojected
    /// position, which is what the velocity buffer points at.
    /// </para>
    /// <para>
    /// Safe to read because both frames are submitted to the same queue in order, so the previous frame's
    /// writes have executed by the time this frame's reads do; a barrier is still needed to make them
    /// visible. It is only meaningful once a second frame has been rendered at the current size — before
    /// that the slot holds either nothing or an image of a different shape.
    /// </para>
    /// </remarks>
    public SceneTarget PreviousTarget(int frameIndex) =>
        _targets[(frameIndex + _targets.Length - 1) % _targets.Length];

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (SceneTarget target in _targets)
        {
            target.Dispose();
        }

        foreach (VulkanBuffer lamp in _lampUniforms)
        {
            lamp.Dispose();
        }

        foreach (VulkanBuffer uniform in _giUniforms)
        {
            uniform.Dispose();
        }

        _sunShadow?.Dispose();
        _sky.Dispose();
        _autoExposure.Dispose();
        _velocity.Dispose();
        _objectVelocity.Dispose();
        _bloom.Dispose();
        _motionBlur.Dispose();
        _sunShadowDenoise?.Dispose();
        _sunShadowAccumulate?.Dispose();
        _lampShadow?.Dispose();
        _lampDenoise?.Dispose();
        _spatialAntiAliasing.Dispose();
        _thinGeometry.Dispose();
        _rejectShading.Dispose();
        _rejectionDilate.Dispose();
        _dilateVelocity.Dispose();
        _decimateHistory.Dispose();
        _updateHistory.Dispose();
        _resolveHistory.Dispose();
        _ssao.Dispose();
        _fog.Dispose();
        _reflection.Dispose();
        _shadowPipeline.Dispose();
        _shadowMap.Dispose();
        _probes.Dispose();
        GlassReflection.Dispose();
        Glass.Dispose();
        Forward.Dispose();
        Resolve.Dispose();
        Lighting.Dispose();
        Geometry.Dispose();
        _vk.DestroySampler(_device, _sampler, null);
        _vk.DestroyDescriptorPool(_device, _pool, null);
        _vk.DestroyDescriptorSetLayout(_device, _surfaceLayout, null);
        foreach (VulkanBuffer buffer in _surfaceUniforms)
        {
            buffer.Dispose();
        }

        _vk.DestroyDescriptorSetLayout(_device, _resolveLayout, null);
        _vk.DestroyDescriptorSetLayout(_device, _lightingLayout, null);
        _renderPass.Dispose();
    }

    /// <summary>Which compiled form of the shading this adapter runs.</summary>
    /// <remarks>
    /// The traced form binds the lamp visibility and the world it traces against; the software form
    /// binds neither, because the extension has to be declared for the shader to parse at all. One
    /// property so the pipeline and its layout can never be built from different variants.
    /// </remarks>
    private ShaderVariant ShadingVariant => new(_rayQuery ? "rayquery" : "software");

    private ShaderReflection ShadingReflection =>
        ShaderCatalog.GetReflection(ShaderModuleId.Shading, ShadingVariant);

    private static ShaderReflection ResolveReflection =>
        ShaderCatalog.GetReflection(ShaderModuleId.Resolve, ShaderVariant.Default);

    /// <summary>Declares the set the shading reads, exactly as the shading declares it.</summary>
    /// <remarks>
    /// Derived rather than written: the G-buffer's five input attachments, the probe volumes and the
    /// atmosphere's tables and everything else are the shader's own list, and a layout kept in step by
    /// hand drifts from it the first time a binding is added or a combined sampler is split in two.
    /// Vulkan says nothing when it does -- it draws the wrong picture.
    /// </remarks>
    private DescriptorSetLayout CreateLightingLayout() => ReflectedSetLayout.Create(
        _vk,
        _device,
        ShadingReflection,
        ShaderStageFlags.FragmentBit,
        "lighting");

    private DescriptorSetLayout CreateSurfaceLayout()
    {
        DescriptorSetLayoutBinding binding = new()
        {
            Binding = 0,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit,
        };

        DescriptorSetLayoutCreateInfo createInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &binding,
        };

        DescriptorSetLayout layout;
        VkCheck.Ok(
            _vk.CreateDescriptorSetLayout(_device, &createInfo, null, &layout),
            "vkCreateDescriptorSetLayout (surface)");
        return layout;
    }

    /// <summary>Allocates one surface set per frame in flight and points each at its own buffer.</summary>
    /// <remarks>
    /// Written once, here. The buffer a set names never changes — only its contents do, and those are
    /// written through the mapping of a buffer whose frame has already been waited on.
    /// </remarks>
    private DescriptorSet[] AllocateSurfaceSets(int framesInFlight)
    {
        DescriptorSet[] sets = new DescriptorSet[framesInFlight];
        DescriptorSetLayout* layouts = stackalloc DescriptorSetLayout[framesInFlight];
        for (int i = 0; i < framesInFlight; i++)
        {
            layouts[i] = _surfaceLayout;
        }

        DescriptorSetAllocateInfo allocInfo = new()
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _pool,
            DescriptorSetCount = (uint)framesInFlight,
            PSetLayouts = layouts,
        };

        fixed (DescriptorSet* target = sets)
        {
            VkCheck.Ok(
                _vk.AllocateDescriptorSets(_device, &allocInfo, target),
                "vkAllocateDescriptorSets (surface)");
        }

        for (int i = 0; i < framesInFlight; i++)
        {
            DescriptorBufferInfo info = new()
            {
                Buffer = _surfaceUniforms[i].Handle,
                Offset = 0,
                Range = SurfaceUniformSize,
            };
            WriteDescriptorSet write = new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = sets[i],
                DstBinding = 0,
                DescriptorType = DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                PBufferInfo = &info,
            };
            _vk.UpdateDescriptorSets(_device, 1, &write, 0, null);
        }

        return sets;
    }

    /// <summary>Declares the set the resolve reads: the scene, the bloom and the authored grade.</summary>
    /// <remarks>
    /// All three are always declared, even for a frame that has no bloom or no grade: such a frame binds
    /// a stand-in and the push block tells the shader to leave the colour alone, because a binding that
    /// came and went would need a second pipeline for the one shader.
    /// </remarks>
    private DescriptorSetLayout CreateResolveLayout() => ReflectedSetLayout.Create(
        _vk,
        _device,
        ResolveReflection,
        ShaderStageFlags.FragmentBit,
        "resolve");

    private DescriptorPool CreatePool(int framesInFlight)
    {
        // What one frame in flight needs: its target's lighting set, that target's three resolve sets --
        // the raw lit, the temporally resolved and the motion blurred -- and the geometry pass's own
        // surface block. Counted off the shaders themselves, so a binding added to either is paid for
        // without a number here being remembered; doubled because a resize allocates the replacements
        // before it frees what they replace.
        Dictionary<DescriptorType, uint> perFrame = ReflectedSetLayout.Counts(ShadingReflection);
        foreach ((DescriptorType type, uint count) in ReflectedSetLayout.Counts(ResolveReflection))
        {
            perFrame[type] = perFrame.GetValueOrDefault(type) + (count * ResolveSetsPerTarget);
        }

        perFrame[DescriptorType.UniformBuffer] = perFrame.GetValueOrDefault(DescriptorType.UniformBuffer) + 1;

        DescriptorPoolSize[] sizes =
        [
            .. perFrame.Select(entry => new DescriptorPoolSize
            {
                Type = entry.Key,
                DescriptorCount = entry.Value * (uint)framesInFlight * 2,
            }),
        ];

        DescriptorPool pool;
        fixed (DescriptorPoolSize* first = sizes)
        {
            DescriptorPoolCreateInfo createInfo = new()
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit,
                MaxSets = (uint)(framesInFlight * (ResolveSetsPerTarget + 2) * 2),
                PoolSizeCount = (uint)sizes.Length,
                PPoolSizes = first,
            };

            VkCheck.Ok(_vk.CreateDescriptorPool(_device, &createInfo, null, &pool), "vkCreateDescriptorPool (deferred)");
        }

        return pool;
    }

    private Sampler CreateSampler()
    {
        SamplerCreateInfo createInfo = new()
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            BorderColor = BorderColor.IntOpaqueBlack,
        };

        Sampler sampler;
        VkCheck.Ok(_vk.CreateSampler(_device, &createInfo, null, &sampler), "vkCreateSampler (deferred resolve)");
        return sampler;
    }
}
