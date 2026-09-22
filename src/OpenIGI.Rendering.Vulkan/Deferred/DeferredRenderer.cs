using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Pipelines;
using OpenIGI.Rendering.Vulkan.Shaders;
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
    /// <param name="compiler">The shader compiler.</param>
    /// <param name="textureSetLayout">The mesh texture set, shared with the forward and shadow paths.</param>
    /// <param name="presentRenderPass">The back-buffer render pass the resolve draws into.</param>
    /// <param name="framesInFlight">How many scene targets to keep — one per frame in flight.</param>
    /// <exception cref="GraphicsDeviceException">A resource could not be created.</exception>
    public DeferredRenderer(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        VulkanCommands commands,
        ShaderCompiler compiler,
        DescriptorSetLayout textureSetLayout,
        RenderPass presentRenderPass,
        int framesInFlight)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(compiler);
        _rayQuery = device.Capabilities.RayQuery;
        _vk = vk;
        _device = device.Device;

        _probes = new ProbeVolumes(vk, device, allocator, commands);
        _shadowMap = new ShadowMap(vk, device, allocator, commands);
        _shadowPipeline = new ShadowPipeline(vk, device, _shadowMap.RenderPass, textureSetLayout);
        _ssao = new SsaoPass(vk, device, allocator, compiler, framesInFlight);
        _fog = new VolumetricFogPass(vk, device, allocator, commands, framesInFlight);
        // The reflections, with their traced half wherever the adapter can trace. Compiled in rather
        // than switched on: the extension has to be declared for the shader to parse at all, so whether
        // there IS a trace is settled here and whether a frame USES it is settled per frame.
        _reflection = new ReflectionPass(vk, device, allocator, compiler, framesInFlight, _rayQuery);
        _velocity = new VelocityPass(vk, device, allocator, compiler, framesInFlight);
        _objectVelocity = new ObjectVelocityPass(vk, device, compiler, DeferredRenderPass.DepthFormat);
        _bloom = new BloomPass(vk, device, allocator, commands, framesInFlight);
        _motionBlur = new MotionBlurPass(vk, device, allocator, framesInFlight);
        _dilateVelocity = new Tsr.DilateVelocityPass(vk, device, allocator, compiler, framesInFlight);
        _decimateHistory = new Tsr.DecimateHistoryPass(vk, device, allocator, framesInFlight);
        _updateHistory = new Tsr.UpdateHistoryPass(vk, device, allocator, compiler, framesInFlight);
        _spatialAntiAliasing = new Tsr.SpatialAntiAliasingPass(vk, device, allocator, framesInFlight);
        _thinGeometry = new Tsr.ThinGeometryPass(vk, device, allocator, framesInFlight);
        _rejectShading = new Tsr.RejectShadingPass(vk, device, allocator, compiler, framesInFlight);
        _rejectionDilate = new Tsr.RejectionDilatePass(vk, device, allocator, framesInFlight);
        _resolveHistory = new Tsr.ResolveHistoryPass(vk, device, allocator, framesInFlight);
        _sky = new Sky.SkyAtmosphere(vk, device, allocator, commands, compiler, framesInFlight);
        _autoExposure = new AutoExposurePass(vk, device, allocator, commands, compiler, framesInFlight);

        // The sun's shadow, traced between the two render passes. Only where the adapter can trace at all.
        _sunShadow = _rayQuery
            ? new RayTracedShadowPass(vk, device, allocator, compiler, framesInFlight)
            : null;
        _sunShadowDenoise = _rayQuery
            ? new ShadowDenoisePass(vk, device, allocator, compiler, framesInFlight)
            : null;
        _sunShadowAccumulate = _rayQuery
            ? new ShadowAccumulatePass(vk, device, allocator, framesInFlight)
            : null;
        _lampShadow = _rayQuery
            ? new LampShadowPass(vk, device, allocator, compiler, framesInFlight)
            : null;
        _lampDenoise = _rayQuery
            ? new LampDenoisePass(vk, device, allocator, compiler, framesInFlight)
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
        Lighting = new FullscreenPipeline(
            vk,
            device,
            compiler,
            DeferredShaderSource.LightingFragment(_rayQuery),
            "deferred-lighting.frag",
            _lightingLayout,
            sizeof(float) * 12,
            _renderPass.Shading,
            DeferredRenderPass.LightingSubpass);
        Resolve = new FullscreenPipeline(
            vk,
            device,
            compiler,
            DeferredShaderSource.ResolveFragment,
            "resolve.frag",
            _resolveLayout,

            // Five vectors: the exposure and curve, the vignette, the film and the grade's table, the
            // lens, and the grade's own knobs.
            sizeof(float) * 20,
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
            compiler,
            _renderPass.Shading,
            DeferredRenderPass.ForwardSubpass,
            _renderPass.FrontLayer,
            _sampler,
            textureSetLayout,
            framesInFlight);

        GlassReflection = new GlassReflectionPass(vk, device, allocator, compiler, framesInFlight, _rayQuery);

        _targets = new SceneTarget[framesInFlight];
        for (int i = 0; i < framesInFlight; i++)
        {
            _targets[i] = new SceneTarget(
                vk, device, allocator, commands,
                _renderPass.Geometry, _renderPass.Shading, _renderPass.FrontLayer,
                _lightingLayout, _resolveLayout, _pool, _sampler);

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

    private DescriptorSetLayout CreateLightingLayout()
    {
        // 0-3 the G-buffer input attachments, 4 the depth input attachment, 5-8 the four probe volumes as
        // sampled 3D textures, 9 the GI parameter uniform block, 10 the screen-GI near-field atlas, 11 the
        // cascaded shadow map, 12 the ambient-occlusion image, 13-15 the atmosphere's tables: the sky's
        // luminance by direction, the sun's transmittance, and the sky's light as harmonics, 16 the
        // traced sun-shadow mask.
        // 21 the integrated fog volume the shading pass looks up, 22 the level's top-down roof map.
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[23];
        for (uint i = 0; i < 5; i++)
        {
            bindings[i] = new DescriptorSetLayoutBinding
            {
                Binding = i,
                DescriptorType = DescriptorType.InputAttachment,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.FragmentBit,
            };
        }

        for (uint i = 5; i < 9; i++)
        {
            bindings[i] = new DescriptorSetLayoutBinding
            {
                Binding = i,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.FragmentBit,
            };
        }

        bindings[9] = new DescriptorSetLayoutBinding
        {
            Binding = 9,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit,
        };

        bindings[10] = new DescriptorSetLayoutBinding
        {
            Binding = 10,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit,
        };

        bindings[11] = new DescriptorSetLayoutBinding
        {
            Binding = 11,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit,
        };

        bindings[12] = new DescriptorSetLayoutBinding
        {
            Binding = 12,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit,
        };

        for (uint i = 13; i < 16; i++)
        {
            bindings[i] = new DescriptorSetLayoutBinding
            {
                Binding = i,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.FragmentBit,
            };
        }

        bindings[16] = new DescriptorSetLayoutBinding
        {
            Binding = 16,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit,
        };

        // The level's lamps: the lights it implies and never declares.
        bindings[17] = new DescriptorSetLayoutBinding
        {
            Binding = 17,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit,
        };

        // And how much of each pixel's lamp reaches it, traced once per pixel by its own pass rather
        // than once per lamp inside the shading. Only where the adapter can trace: without ray query
        // there is no pass to write it and the shading has no term to read.
        uint next = 18;
        if (_rayQuery)
        {
            bindings[next++] = new DescriptorSetLayoutBinding
            {
                Binding = 18,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.FragmentBit,
            };
        }

        // 19 the scene depth as a sampled texture, for the lamps' screen-space occlusion march. Always
        // present -- the march is what shadows lamps where there is no ray query -- so it sits after the
        // trace-only binding rather than being numbered around it, and the count follows whichever were
        // added.
        bindings[next++] = new DescriptorSetLayoutBinding
        {
            Binding = 19,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit,
        };

        // 20 the world the lamps trace against, for the off-screen half of their shadow. Only where the
        // adapter can trace; the ray is still fired only when the RT switch is on, which is a runtime
        // choice the shader makes, so the binding exists whenever the trace COULD run.
        if (_rayQuery)
        {
            bindings[next++] = new DescriptorSetLayoutBinding
            {
                Binding = 20,
                DescriptorType = DescriptorType.AccelerationStructureKhr,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.FragmentBit,
            };
        }

        // 21 the integrated fog volume, which the shading pass looks its own pixel up in. Always present:
        // a frame with no fog binds the volume anyway and is told to ignore it, because a binding that
        // comes and goes would need two pipelines for the one shader.
        bindings[next++] = new DescriptorSetLayoutBinding
        {
            Binding = 21,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit,
        };

        // 22 the level's roofs seen from above, which decides what the rain reaches. Always present for
        // the same reason as the fog volume: a level with none binds a placeholder and the shading is
        // told to ignore it, rather than the shader needing two pipelines for the two cases.
        bindings[next++] = new DescriptorSetLayoutBinding
        {
            Binding = 22,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit,
        };

        DescriptorSetLayoutCreateInfo createInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = next,
            PBindings = bindings,
        };

        DescriptorSetLayout layout;
        VkCheck.Ok(_vk.CreateDescriptorSetLayout(_device, &createInfo, null, &layout), "vkCreateDescriptorSetLayout (lighting)");
        return layout;
    }

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

    private DescriptorSetLayout CreateResolveLayout()
    {
        // Binding 0 is the resolved scene, binding 1 the bloom the reference composites into it, and
        // binding 2 the authored grade as a cube. All three are always declared: a frame with no grade
        // binds a stand-in and the push block tells the shader to leave the colour alone, because a
        // binding that came and went would need a second pipeline for the one shader.
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[3];
        for (uint i = 0; i < 3; i++)
        {
            bindings[i] = new DescriptorSetLayoutBinding
            {
                Binding = i,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.FragmentBit,
            };
        }

        DescriptorSetLayoutCreateInfo createInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 3,
            PBindings = bindings,
        };

        DescriptorSetLayout layout;
        VkCheck.Ok(_vk.CreateDescriptorSetLayout(_device, &createInfo, null, &layout), "vkCreateDescriptorSetLayout (resolve)");
        return layout;
    }

    private DescriptorPool CreatePool(int framesInFlight)
    {
        // Each frame's target uses four input attachments plus one sampler; a resize frees and
        // reallocates, so the pool allows freeing and is sized with headroom.
        DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[6];
        sizes[0] = new DescriptorPoolSize { Type = DescriptorType.InputAttachment, DescriptorCount = (uint)(framesInFlight * 5 * 2) };
        // Thirteen combined samplers per lighting set -- four probe volumes, the screen-GI atlas, the
        // cascades, the occlusion, three atmosphere tables, the traced sun mask, the scene depth and the
        // fog volume -- with headroom, and two sets per frame.
        sizes[1] = new DescriptorPoolSize { Type = DescriptorType.CombinedImageSampler, DescriptorCount = (uint)(framesInFlight * 16 * 2) };
        // Two uniform buffers per lighting set -- the GI block and the lamps -- plus the geometry pass's
        // own surface block, one per frame in flight.
        sizes[2] = new DescriptorPoolSize { Type = DescriptorType.UniformBuffer, DescriptorCount = (uint)(framesInFlight * 6) };

        // The three resolve sets -- the raw lit, the temporally-resolved and the motion-blurred -- each
        // carry the scene, the bloom and the grade.
        sizes[3] = new DescriptorPoolSize { Type = DescriptorType.CombinedImageSampler, DescriptorCount = (uint)(framesInFlight * 12) };

        // The lamp visibility each lighting set reads, where the adapter can trace one.
        sizes[4] = new DescriptorPoolSize
        {
            Type = DescriptorType.CombinedImageSampler,
            DescriptorCount = (uint)(framesInFlight * 2),
        };

        // The world each lighting set traces its lamp shadow rays against, where the adapter can trace.
        sizes[5] = new DescriptorPoolSize
        {
            Type = DescriptorType.AccelerationStructureKhr,
            DescriptorCount = (uint)(framesInFlight * 2),
        };

        DescriptorPoolCreateInfo createInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit,
            MaxSets = (uint)(framesInFlight * 4 * 2),
            PoolSizeCount = _rayQuery ? 6u : 4u,
            PPoolSizes = sizes,
        };

        DescriptorPool pool;
        VkCheck.Ok(_vk.CreateDescriptorPool(_device, &createInfo, null, &pool), "vkCreateDescriptorPool (deferred)");
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
