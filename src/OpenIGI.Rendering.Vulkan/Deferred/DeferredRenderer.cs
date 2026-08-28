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
    private readonly ShadowMap _shadowMap;
    private readonly ShadowPipeline _shadowPipeline;
    private readonly bool _rayQuery;
    private readonly RayTracedShadowPass? _sunShadow;
    private readonly ShadowDenoisePass? _sunShadowDenoise;
    private readonly ShadowAccumulatePass? _sunShadowAccumulate;
    private readonly SsaoPass _ssao;
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
    public const int GiUniformSize = 464;

    /// <summary>Initialises a new instance of the <see cref="DeferredRenderer"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device everything lives on.</param>
    /// <param name="allocator">The allocator backing the scene targets.</param>
    /// <param name="commands">The command pool the probe uploads come from.</param>
    /// <param name="compiler">The shader compiler.</param>
    /// <param name="textureSetLayout">The two-sampler layout shared with the mesh path, for geometry.</param>
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
        _shadowPipeline = new ShadowPipeline(vk, device, compiler, _shadowMap.RenderPass, textureSetLayout);
        _ssao = new SsaoPass(vk, device, allocator, compiler, framesInFlight);
        _velocity = new VelocityPass(vk, device, allocator, compiler, framesInFlight);
        _objectVelocity = new ObjectVelocityPass(vk, device, compiler, DeferredRenderPass.DepthFormat);
        _bloom = new BloomPass(vk, device, allocator, commands, compiler, framesInFlight);
        _motionBlur = new MotionBlurPass(vk, device, allocator, compiler, framesInFlight);
        _dilateVelocity = new Tsr.DilateVelocityPass(vk, device, allocator, compiler, framesInFlight);
        _decimateHistory = new Tsr.DecimateHistoryPass(vk, device, allocator, compiler, framesInFlight);
        _updateHistory = new Tsr.UpdateHistoryPass(vk, device, allocator, compiler, framesInFlight);
        _spatialAntiAliasing = new Tsr.SpatialAntiAliasingPass(vk, device, allocator, compiler, framesInFlight);
        _thinGeometry = new Tsr.ThinGeometryPass(vk, device, allocator, compiler, framesInFlight);
        _rejectShading = new Tsr.RejectShadingPass(vk, device, allocator, compiler, framesInFlight);
        _rejectionDilate = new Tsr.RejectionDilatePass(vk, device, allocator, compiler, framesInFlight);
        _resolveHistory = new Tsr.ResolveHistoryPass(vk, device, allocator, compiler, framesInFlight);
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
            ? new ShadowAccumulatePass(vk, device, allocator, compiler, framesInFlight)
            : null;
        _giUniforms = new VulkanBuffer[framesInFlight];
        for (int i = 0; i < framesInFlight; i++)
        {
            _giUniforms[i] = new VulkanBuffer(
                vk, device, allocator, GiUniformSize,
                BufferUsageFlags.UniformBufferBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        }

        _renderPass = new DeferredRenderPass(vk, _device);
        _lightingLayout = CreateLightingLayout();
        _resolveLayout = CreateResolveLayout();
        _pool = CreatePool(framesInFlight);
        _sampler = CreateSampler();

        Geometry = new GBufferPipeline(vk, device, compiler, _renderPass.Geometry, textureSetLayout);
        Lighting = new FullscreenPipeline(
            vk,
            device,
            compiler,
            DeferredShaderSource.LightingFragment(),
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
            sizeof(float) * 8,
            presentRenderPass,
            0);
        Forward = new Mesh3DPipeline(vk, device, compiler, _renderPass.Shading, DeferredRenderPass.ForwardSubpass);

        _targets = new SceneTarget[framesInFlight];
        for (int i = 0; i < framesInFlight; i++)
        {
            _targets[i] = new SceneTarget(
                vk, device, allocator, commands,
                _renderPass.Geometry, _renderPass.Shading,
                _lightingLayout, _resolveLayout, _pool, _sampler);
        }
    }

    /// <summary>Gets the pass that fills the G-buffer.</summary>
    public RenderPass GeometryPass => _renderPass.Geometry;

    /// <summary>Gets the pass that shades the G-buffer and draws translucency over it.</summary>
    public RenderPass ShadingPass => _renderPass.Shading;

    /// <summary>Gets the geometry (G-buffer) pipeline.</summary>
    public GBufferPipeline Geometry { get; }

    /// <summary>Gets the lighting pipeline.</summary>
    public FullscreenPipeline Lighting { get; }

    /// <summary>Gets the resolve pipeline.</summary>
    public FullscreenPipeline Resolve { get; }

    /// <summary>Gets the forward pipeline for translucency, drawn into the lit scene.</summary>
    public Mesh3DPipeline Forward { get; }

    /// <summary>Gets the irradiance probe field the lighting pass samples.</summary>
    public ProbeVolumes Probes => _probes;

    /// <summary>Gets a frame-in-flight's GI uniform buffer, for the lighting pass's probe parameters.</summary>
    /// <param name="frame">The frame-in-flight index.</param>
    /// <returns>The buffer.</returns>
    public VulkanBuffer GiUniform(int frame) => _giUniforms[frame];

    /// <summary>Gets the sun's cascaded shadow map.</summary>
    public ShadowMap ShadowMap => _shadowMap;

    /// <summary>Gets the depth-only pipeline the shadow cascades render with.</summary>
    public ShadowPipeline ShadowPipeline => _shadowPipeline;

    /// <summary>Gets the ambient-occlusion compute pass.</summary>
    public SsaoPass Ssao => _ssao;

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
        _spatialAntiAliasing.Dispose();
        _thinGeometry.Dispose();
        _rejectShading.Dispose();
        _rejectionDilate.Dispose();
        _dilateVelocity.Dispose();
        _decimateHistory.Dispose();
        _updateHistory.Dispose();
        _resolveHistory.Dispose();
        _ssao.Dispose();
        _shadowPipeline.Dispose();
        _shadowMap.Dispose();
        _probes.Dispose();
        Forward.Dispose();
        Resolve.Dispose();
        Lighting.Dispose();
        Geometry.Dispose();
        _vk.DestroySampler(_device, _sampler, null);
        _vk.DestroyDescriptorPool(_device, _pool, null);
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
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[17];
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

        DescriptorSetLayoutCreateInfo createInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 17,
            PBindings = bindings,
        };

        DescriptorSetLayout layout;
        VkCheck.Ok(_vk.CreateDescriptorSetLayout(_device, &createInfo, null, &layout), "vkCreateDescriptorSetLayout (lighting)");
        return layout;
    }

    private DescriptorSetLayout CreateResolveLayout()
    {
        // Binding 0 is the resolved scene, binding 1 the bloom the reference composites into it.
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[2];
        for (uint i = 0; i < 2; i++)
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
            BindingCount = 2,
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
        DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[4];
        sizes[0] = new DescriptorPoolSize { Type = DescriptorType.InputAttachment, DescriptorCount = (uint)(framesInFlight * 5 * 2) };
        sizes[1] = new DescriptorPoolSize { Type = DescriptorType.CombinedImageSampler, DescriptorCount = (uint)(framesInFlight * 8 * 2) };
        sizes[2] = new DescriptorPoolSize { Type = DescriptorType.UniformBuffer, DescriptorCount = (uint)(framesInFlight * 2) };

        // The two resolve sets — the raw lit and the temporally-resolved — each carry one sampler.
        sizes[3] = new DescriptorPoolSize { Type = DescriptorType.CombinedImageSampler, DescriptorCount = (uint)(framesInFlight * 4) };

        DescriptorPoolCreateInfo createInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit,
            MaxSets = (uint)(framesInFlight * 3 * 2),
            PoolSizeCount = 4,
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
