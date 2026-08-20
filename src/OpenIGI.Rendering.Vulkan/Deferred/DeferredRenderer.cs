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
    private readonly SsaoPass _ssao;
    private readonly VelocityPass _velocity;
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
        _vk = vk;
        _device = device.Device;

        _probes = new ProbeVolumes(vk, device, allocator, commands);
        _shadowMap = new ShadowMap(vk, device, allocator, commands);
        _shadowPipeline = new ShadowPipeline(vk, device, compiler, _shadowMap.RenderPass);
        _ssao = new SsaoPass(vk, device, allocator, compiler, framesInFlight);
        _velocity = new VelocityPass(vk, device, allocator, compiler, framesInFlight);
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

        Geometry = new GBufferPipeline(vk, device, compiler, _renderPass.Handle, textureSetLayout);
        Lighting = new FullscreenPipeline(
            vk,
            device,
            compiler,
            DeferredShaderSource.LightingFragment,
            "deferred-lighting.frag",
            _lightingLayout,
            sizeof(float) * 12,
            _renderPass.Handle,
            DeferredRenderPass.LightingSubpass);
        Resolve = new FullscreenPipeline(
            vk,
            device,
            compiler,
            DeferredShaderSource.ResolveFragment,
            "resolve.frag",
            _resolveLayout,
            sizeof(float) * 4,
            presentRenderPass,
            0);
        Forward = new Mesh3DPipeline(vk, device, compiler, _renderPass.Handle, DeferredRenderPass.ForwardSubpass);

        _targets = new SceneTarget[framesInFlight];
        for (int i = 0; i < framesInFlight; i++)
        {
            _targets[i] = new SceneTarget(vk, device, allocator, commands, _renderPass.Handle, _lightingLayout, _resolveLayout, _pool, _sampler);
        }
    }

    /// <summary>Gets the deferred render pass handle.</summary>
    public RenderPass RenderPass => _renderPass.Handle;

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

    /// <summary>Gets the scene target for a frame in flight, built at the render size.</summary>
    /// <param name="frameIndex">The frame-in-flight index.</param>
    /// <param name="width">The render width in pixels.</param>
    /// <param name="height">The render height in pixels.</param>
    /// <returns>The ready scene target.</returns>
    public SceneTarget Target(int frameIndex, int width, int height)
    {
        SceneTarget target = _targets[frameIndex];
        target.Ensure(width, height);
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

        _velocity.Dispose();
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
        // cascaded shadow map, 12 the ambient-occlusion image.
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[13];
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

        DescriptorSetLayoutCreateInfo createInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 13,
            PBindings = bindings,
        };

        DescriptorSetLayout layout;
        VkCheck.Ok(_vk.CreateDescriptorSetLayout(_device, &createInfo, null, &layout), "vkCreateDescriptorSetLayout (lighting)");
        return layout;
    }

    private DescriptorSetLayout CreateResolveLayout()
    {
        DescriptorSetLayoutBinding binding = new()
        {
            Binding = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
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
        VkCheck.Ok(_vk.CreateDescriptorSetLayout(_device, &createInfo, null, &layout), "vkCreateDescriptorSetLayout (resolve)");
        return layout;
    }

    private DescriptorPool CreatePool(int framesInFlight)
    {
        // Each frame's target uses four input attachments plus one sampler; a resize frees and
        // reallocates, so the pool allows freeing and is sized with headroom.
        DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[3];
        sizes[0] = new DescriptorPoolSize { Type = DescriptorType.InputAttachment, DescriptorCount = (uint)(framesInFlight * 5 * 2) };
        sizes[1] = new DescriptorPoolSize { Type = DescriptorType.CombinedImageSampler, DescriptorCount = (uint)(framesInFlight * 8 * 2) };
        sizes[2] = new DescriptorPoolSize { Type = DescriptorType.UniformBuffer, DescriptorCount = (uint)(framesInFlight * 2) };

        DescriptorPoolCreateInfo createInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit,
            MaxSets = (uint)(framesInFlight * 2 * 2),
            PoolSizeCount = 3,
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
