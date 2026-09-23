using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Memory;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Rhi;

/// <summary>A buffer, as the RHI hands it out.</summary>
internal sealed class VulkanRhiBuffer : IRhiBuffer
{
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanRhiBuffer"/> class.</summary>
    /// <param name="buffer">The allocation behind it.</param>
    /// <param name="descriptor">What it was created as.</param>
    public VulkanRhiBuffer(VulkanBuffer buffer, in RhiBufferDescriptor descriptor)
    {
        Buffer = buffer;
        Label = descriptor.Label;
        SizeInBytes = descriptor.SizeInBytes;
        Usage = descriptor.Usage;
    }

    /// <summary>Gets the allocation behind it.</summary>
    public VulkanBuffer Buffer { get; }

    /// <summary>Gets or sets the submission count after which the GPU's last write to this buffer is done.</summary>
    /// <remarks>
    /// Stamped when a command buffer that copies into it is submitted. A readback waits for this and not
    /// for the newest submission, which may be a frame still being recorded.
    /// </remarks>
    internal ulong WriteTicket { get; set; }

    /// <inheritdoc/>
    public string Label { get; }

    /// <inheritdoc/>
    public ulong SizeInBytes { get; }

    /// <inheritdoc/>
    public RhiBufferUsage Usage { get; }

    /// <summary>Gets or sets which stages last touched it, so a barrier can wait on the right one.</summary>
    public PipelineStageFlags LastStage { get; set; } = PipelineStageFlags.TopOfPipeBit;

    /// <summary>Gets or sets how it was last accessed.</summary>
    public AccessFlags LastAccess { get; set; }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Buffer.Dispose();
    }
}

/// <summary>A texture, as the RHI hands it out.</summary>
/// <remarks>
/// <para>
/// Its resting layout is General. Vulkan would prefer a specific one for each use and the difference is
/// measurable on some hardware, but a resting layout that every use accepts is what lets the RHI promise
/// there are no barriers to think about: a pass that reads what another wrote simply reads it. The
/// exception is an attachment, which has no General form, so a render pass transitions in and out.
/// </para>
/// <para>
/// One image, however many views. A view is created on demand and kept, because a bind group holds one
/// and creating a fresh view per frame is a leak with extra steps.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanRhiTexture : IRhiTexture
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly VulkanAllocator _allocator;
    private readonly DeviceMemory _memory;
    private readonly bool _owned;
    private readonly Dictionary<RhiTextureViewDescriptor, VulkanRhiTextureView> _views = [];
    private VulkanRhiTextureView? _whole;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanRhiTexture"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device it lives on.</param>
    /// <param name="allocator">The allocator its memory came from.</param>
    /// <param name="image">The image.</param>
    /// <param name="memory">The memory bound to it, or the null handle for an image it does not own.</param>
    /// <param name="descriptor">What it was created as.</param>
    /// <param name="owned">
    /// Whether this destroys the image. False for a swapchain's, which belongs to the swapchain and
    /// outlives every texture that wraps one -- destroying it here would take the rotation with it.
    /// </param>
    public VulkanRhiTexture(
        Vk vk,
        Device device,
        VulkanAllocator allocator,
        Image image,
        DeviceMemory memory,
        in RhiTextureDescriptor descriptor,
        bool owned = true)
    {
        _owned = owned;
        _vk = vk;
        _device = device;
        _allocator = allocator;
        _memory = memory;

        Image = image;
        Label = descriptor.Label;
        Width = descriptor.Width;
        Height = descriptor.Height;
        DepthOrArrayLayers = descriptor.DepthOrArrayLayers;
        MipLevelCount = Math.Max(1, descriptor.MipLevelCount);
        SampleCount = Math.Max(1, descriptor.SampleCount);
        Format = descriptor.Format;
        Usage = descriptor.Usage;
        Dimension = descriptor.Dimension;
        Aspects = VulkanRhiTranslation.Aspects(descriptor.Format);
    }

    /// <summary>Gets the image.</summary>
    public Image Image { get; }

    /// <summary>Gets which aspects it has, which a copy and a view both need.</summary>
    public ImageAspectFlags Aspects { get; }

    /// <summary>Gets or sets the layout it is currently in.</summary>
    public ImageLayout Layout { get; set; } = ImageLayout.Undefined;

    /// <inheritdoc/>
    public string Label { get; }

    /// <inheritdoc/>
    public int Width { get; }

    /// <inheritdoc/>
    public int Height { get; }

    /// <inheritdoc/>
    public int DepthOrArrayLayers { get; }

    /// <inheritdoc/>
    public int MipLevelCount { get; }

    /// <inheritdoc/>
    public int SampleCount { get; }

    /// <inheritdoc/>
    public RhiTextureFormat Format { get; }

    /// <inheritdoc/>
    public RhiTextureUsage Usage { get; }

    /// <summary>Gets how a shader addresses it.</summary>
    public RhiTextureViewDimension Dimension { get; }

    /// <summary>Gets how many array layers it has, which a volume does not.</summary>
    public int Layers => Dimension == RhiTextureViewDimension.Texture3D ? 1 : DepthOrArrayLayers;

    /// <inheritdoc/>
    public IRhiTextureView View => _whole ??= (VulkanRhiTextureView)CreateView(
        new RhiTextureViewDescriptor { Dimension = Dimension, Label = Label });

    /// <inheritdoc/>
    public IRhiTextureView CreateView(in RhiTextureViewDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_views.TryGetValue(descriptor, out VulkanRhiTextureView? existing))
        {
            return existing;
        }

        RhiTextureFormat format = descriptor.Format == RhiTextureFormat.Undefined ? Format : descriptor.Format;

        ImageViewCreateInfo info = new()
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = Image,
            ViewType = VulkanRhiTranslation.ViewDimension(descriptor.Dimension),
            Format = VulkanRhiTranslation.TextureFormat(format),
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = Aspects,
                BaseMipLevel = (uint)descriptor.BaseMipLevel,
                LevelCount = (uint)(descriptor.MipLevelCount > 0
                    ? descriptor.MipLevelCount
                    : MipLevelCount - descriptor.BaseMipLevel),
                BaseArrayLayer = (uint)descriptor.BaseArrayLayer,
                LayerCount = (uint)(descriptor.ArrayLayerCount > 0
                    ? descriptor.ArrayLayerCount
                    : Layers - descriptor.BaseArrayLayer),
            },
        };

        ImageView view;
        VkCheck.Ok(_vk.CreateImageView(_device, &info, null, &view), "vkCreateImageView");

        VulkanRhiTextureView created = new(
            view,
            this,
            format,
            descriptor.Label.Length > 0 ? descriptor.Label : Label);

        _views[descriptor] = created;
        return created;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (VulkanRhiTextureView view in _views.Values)
        {
            _vk.DestroyImageView(_device, view.Handle, null);
        }

        _views.Clear();
        _whole = null;

        // The views are always this one's to destroy; the image only when it was made here.
        if (_owned)
        {
            _vk.DestroyImage(_device, Image, null);
            _allocator.Free(_memory);
        }
    }
}

/// <summary>A view of a texture.</summary>
internal sealed class VulkanRhiTextureView : IRhiTextureView
{
    /// <summary>Initialises a new instance of the <see cref="VulkanRhiTextureView"/> class.</summary>
    /// <param name="handle">The view.</param>
    /// <param name="texture">The texture it looks at.</param>
    /// <param name="format">The format it reads through.</param>
    /// <param name="label">The name it was created with.</param>
    public VulkanRhiTextureView(ImageView handle, VulkanRhiTexture texture, RhiTextureFormat format, string label)
    {
        Handle = handle;
        Owner = texture;
        Format = format;
        Label = label;
    }

    /// <summary>Gets the view.</summary>
    public ImageView Handle { get; }

    /// <summary>Gets the texture it looks at, as this backend knows it.</summary>
    public VulkanRhiTexture Owner { get; }

    /// <inheritdoc/>
    public IRhiTexture Texture => Owner;

    /// <inheritdoc/>
    public RhiTextureFormat Format { get; }

    /// <inheritdoc/>
    public string Label { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// A view belongs to its texture and is destroyed with it. Disposing one on its own would leave the
    /// texture holding a handle it no longer owns, so this does nothing on purpose.
    /// </remarks>
    public void Dispose()
    {
    }
}

/// <summary>A sampler.</summary>
internal sealed unsafe class VulkanRhiSampler : IRhiSampler
{
    private readonly Vk _vk;
    private readonly Device _device;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanRhiSampler"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device it lives on.</param>
    /// <param name="handle">The sampler.</param>
    /// <param name="label">The name it was created with.</param>
    public VulkanRhiSampler(Vk vk, Device device, Sampler handle, string label)
    {
        _vk = vk;
        _device = device;
        Handle = handle;
        Label = label;
    }

    /// <summary>Gets the sampler.</summary>
    public Sampler Handle { get; }

    /// <inheritdoc/>
    public string Label { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _vk.DestroySampler(_device, Handle, null);
    }
}

/// <summary>A shader module, and what the library says it contains.</summary>
internal sealed unsafe class VulkanRhiShaderModule : IRhiShaderModule
{
    private readonly Vk _vk;
    private readonly Device _device;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanRhiShaderModule"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device it lives on.</param>
    /// <param name="handle">The module.</param>
    /// <param name="label">The name it was created with.</param>
    public VulkanRhiShaderModule(Vk vk, Device device, ShaderModule handle, string label)
    {
        _vk = vk;
        _device = device;
        Handle = handle;
        Label = label;
    }

    /// <summary>Gets the module.</summary>
    public ShaderModule Handle { get; }

    /// <inheritdoc/>
    public string Label { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _vk.DestroyShaderModule(_device, Handle, null);
    }
}

/// <summary>A descriptor set layout, which remembers its entries so a set can be written.</summary>
internal sealed unsafe class VulkanRhiBindGroupLayout : IRhiBindGroupLayout
{
    private readonly Vk _vk;
    private readonly Device _device;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanRhiBindGroupLayout"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device it lives on.</param>
    /// <param name="handle">The layout.</param>
    /// <param name="descriptor">What it was created as.</param>
    public VulkanRhiBindGroupLayout(
        Vk vk,
        Device device,
        DescriptorSetLayout handle,
        RhiBindGroupLayoutDescriptor descriptor)
    {
        _vk = vk;
        _device = device;
        Handle = handle;
        Label = descriptor.Label;
        Entries = descriptor.Entries;
    }

    /// <summary>Gets the layout.</summary>
    public DescriptorSetLayout Handle { get; }

    /// <summary>Gets what each slot holds.</summary>
    public IReadOnlyList<RhiBindGroupLayoutEntry> Entries { get; }

    /// <inheritdoc/>
    public string Label { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _vk.DestroyDescriptorSetLayout(_device, Handle, null);
    }
}

/// <summary>A descriptor set, and the resources it points at.</summary>
/// <remarks>
/// It remembers what it binds because the encoder has to know: the RHI promises no barriers, so
/// something has to put every bound texture into a layout the shader can read before the pass runs, and
/// the set is where that list lives.
/// </remarks>
internal sealed class VulkanRhiBindGroup : IRhiBindGroup
{
    /// <summary>Initialises a new instance of the <see cref="VulkanRhiBindGroup"/> class.</summary>
    /// <param name="set">The descriptor set.</param>
    /// <param name="label">The name it was created with.</param>
    /// <param name="textures">The textures it binds, and how each is read.</param>
    /// <param name="buffers">The buffers it binds.</param>
    public VulkanRhiBindGroup(
        DescriptorSet set,
        string label,
        IReadOnlyList<(VulkanRhiTexture Texture, bool Storage)> textures,
        IReadOnlyList<VulkanRhiBuffer> buffers)
    {
        Set = set;
        Label = label;
        Textures = textures;
        Buffers = buffers;
    }

    /// <summary>Gets the descriptor set.</summary>
    public DescriptorSet Set { get; }

    /// <summary>Gets the textures it binds, and whether each is read as a storage image.</summary>
    public IReadOnlyList<(VulkanRhiTexture Texture, bool Storage)> Textures { get; }

    /// <summary>Gets the buffers it binds.</summary>
    public IReadOnlyList<VulkanRhiBuffer> Buffers { get; }

    /// <inheritdoc/>
    public string Label { get; }

    /// <inheritdoc/>
    /// <remarks>The set belongs to the pool it came from and is freed when the device is.</remarks>
    public void Dispose()
    {
    }
}

/// <summary>A pipeline layout.</summary>
internal sealed unsafe class VulkanRhiPipelineLayout : IRhiPipelineLayout
{
    private readonly Vk _vk;
    private readonly Device _device;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanRhiPipelineLayout"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device it lives on.</param>
    /// <param name="handle">The layout.</param>
    /// <param name="descriptor">What it was created as.</param>
    public VulkanRhiPipelineLayout(
        Vk vk,
        Device device,
        PipelineLayout handle,
        RhiPipelineLayoutDescriptor descriptor)
    {
        _vk = vk;
        _device = device;
        Handle = handle;
        Label = descriptor.Label;
        PushDataBytes = descriptor.PushDataBytes;
        PushDataVisibility = VulkanRhiTranslation.ShaderStages(descriptor.PushDataVisibility);
    }

    /// <summary>Gets the layout.</summary>
    public PipelineLayout Handle { get; }

    /// <summary>Gets how many bytes of push data it takes.</summary>
    public int PushDataBytes { get; }

    /// <summary>Gets which stages read that push data.</summary>
    public ShaderStageFlags PushDataVisibility { get; }

    /// <inheritdoc/>
    public string Label { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _vk.DestroyPipelineLayout(_device, Handle, null);
    }
}

/// <summary>A compute pipeline.</summary>
internal sealed unsafe class VulkanRhiComputePipeline : IRhiComputePipeline
{
    private readonly Vk _vk;
    private readonly Device _device;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanRhiComputePipeline"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device it lives on.</param>
    /// <param name="handle">The pipeline.</param>
    /// <param name="layout">The layout it was built against.</param>
    /// <param name="label">The name it was created with.</param>
    public VulkanRhiComputePipeline(
        Vk vk,
        Device device,
        Pipeline handle,
        IRhiPipelineLayout layout,
        string label)
    {
        _vk = vk;
        _device = device;
        Handle = handle;
        Layout = layout;
        Label = label;
    }

    /// <summary>Gets the pipeline.</summary>
    public Pipeline Handle { get; }

    /// <inheritdoc/>
    public IRhiPipelineLayout Layout { get; }

    /// <inheritdoc/>
    public string Label { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _vk.DestroyPipeline(_device, Handle, null);
    }
}

/// <summary>A query pool.</summary>
internal sealed unsafe class VulkanRhiQuerySet : IRhiQuerySet
{
    private readonly Vk _vk;
    private readonly Device _device;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanRhiQuerySet"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device it lives on.</param>
    /// <param name="handle">The pool.</param>
    /// <param name="descriptor">What it was created as.</param>
    public VulkanRhiQuerySet(Vk vk, Device device, QueryPool handle, in RhiQuerySetDescriptor descriptor)
    {
        _vk = vk;
        _device = device;
        Handle = handle;
        Label = descriptor.Label;
        Type = descriptor.Type;
        Count = descriptor.Count;
    }

    /// <summary>Gets the pool.</summary>
    public QueryPool Handle { get; }

    /// <summary>Gets or sets whether the pool has to be reset before a pass next writes it.</summary>
    /// <remarks>
    /// True from creation, and again once the set has been resolved: an occlusion set is written by the
    /// passes of a frame and read once at its end, so it is reset once per round of that, not per pass.
    /// </remarks>
    public bool NeedsReset { get; set; } = true;

    /// <inheritdoc/>
    public RhiQueryType Type { get; }

    /// <inheritdoc/>
    public int Count { get; }

    /// <inheritdoc/>
    public string Label { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _vk.DestroyQueryPool(_device, Handle, null);
    }
}

/// <summary>Recorded commands, ready to submit.</summary>
internal sealed class VulkanRhiCommandBuffer : IRhiCommandBuffer
{
    /// <summary>Initialises a new instance of the <see cref="VulkanRhiCommandBuffer"/> class.</summary>
    /// <param name="handle">The command buffer.</param>
    /// <param name="label">The name it was recorded under.</param>
    /// <param name="written">The buffers its commands write, to be stamped as it is submitted.</param>
    public VulkanRhiCommandBuffer(CommandBuffer handle, string label, IReadOnlyList<VulkanRhiBuffer>? written = null)
    {
        Handle = handle;
        Label = label;
        Written = written ?? [];
    }

    /// <summary>Gets the buffers its commands write.</summary>
    internal IReadOnlyList<VulkanRhiBuffer> Written { get; }

    /// <summary>Gets the command buffer.</summary>
    public CommandBuffer Handle { get; }

    /// <inheritdoc/>
    public string Label { get; }

    /// <inheritdoc/>
    /// <remarks>The buffer belongs to the pool it was allocated from and is freed with the device.</remarks>
    public void Dispose()
    {
    }
}
