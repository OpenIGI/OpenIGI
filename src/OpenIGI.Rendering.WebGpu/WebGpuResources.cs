using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.WebGpu.Native;

namespace OpenIGI.Rendering.WebGpu;

/// <summary>
/// What every resource here has: a label, a handle, and a release that happens once.
/// </summary>
/// <typeparam name="THandle">The WebGPU handle type.</typeparam>
/// <remarks>
/// WebGPU reference-counts every object. Releasing one twice drops a count that was never taken, which
/// corrupts the library's own table and reports itself somewhere else entirely — so the handle is
/// cleared as it is released and a second disposal does nothing.
/// </remarks>
public abstract class WebGpuResource<THandle> : IRhiResource
    where THandle : struct
{
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="WebGpuResource{THandle}"/> class.</summary>
    /// <param name="handle">The WebGPU handle.</param>
    /// <param name="label">The name it was created with.</param>
    protected WebGpuResource(THandle handle, string label)
    {
        Handle = handle;
        Label = label;
    }

    /// <summary>Gets the WebGPU handle.</summary>
    public THandle Handle { get; private set; }

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
        Release(Handle);
        Handle = default;
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the handle.</summary>
    /// <param name="handle">The handle to release.</param>
    protected abstract void Release(THandle handle);
}

/// <summary>A WebGPU buffer.</summary>
public sealed unsafe class WebGpuBuffer : WebGpuResource<WGPUBuffer>, IRhiBuffer
{
    /// <summary>Initialises a new instance of the <see cref="WebGpuBuffer"/> class.</summary>
    /// <param name="handle">The WebGPU handle.</param>
    /// <param name="descriptor">What it was created as.</param>
    public WebGpuBuffer(WGPUBuffer handle, in RhiBufferDescriptor descriptor)
        : base(handle, descriptor.Label)
    {
        SizeInBytes = descriptor.SizeInBytes;
        Usage = descriptor.Usage;
    }

    /// <inheritdoc/>
    public ulong SizeInBytes { get; }

    /// <inheritdoc/>
    public RhiBufferUsage Usage { get; }

    /// <inheritdoc/>
    protected override void Release(WGPUBuffer handle) => Wgpu.wgpuBufferRelease(handle);
}

/// <summary>A WebGPU texture, and the views taken of it.</summary>
public sealed unsafe class WebGpuTexture : WebGpuResource<WGPUTexture>, IRhiTexture
{
    private readonly List<WebGpuTextureView> _views = [];
    private WebGpuTextureView? _whole;

    /// <summary>Initialises a new instance of the <see cref="WebGpuTexture"/> class.</summary>
    /// <param name="handle">The WebGPU handle.</param>
    /// <param name="descriptor">What it was created as, after any widening.</param>
    /// <param name="requested">The format that was asked for, which may be narrower than the one allocated.</param>
    public WebGpuTexture(WGPUTexture handle, in RhiTextureDescriptor descriptor, RhiTextureFormat requested)
        : base(handle, descriptor.Label)
    {
        Width = descriptor.Width;
        Height = descriptor.Height;
        DepthOrArrayLayers = descriptor.DepthOrArrayLayers;
        MipLevelCount = descriptor.MipLevelCount;
        SampleCount = descriptor.SampleCount;
        Format = descriptor.Format;
        RequestedFormat = requested;
        Usage = descriptor.Usage;
        Dimension = descriptor.Dimension;
    }

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

    /// <summary>Gets the format that was asked for, which the backend may have widened.</summary>
    /// <remarks>See <see cref="WebGpuFormatPolicy"/> for when and why the two differ.</remarks>
    public RhiTextureFormat RequestedFormat { get; }

    /// <inheritdoc/>
    public RhiTextureUsage Usage { get; }

    /// <summary>Gets how a shader addresses it.</summary>
    public RhiTextureViewDimension Dimension { get; }

    /// <inheritdoc/>
    public IRhiTextureView View => _whole ??= (WebGpuTextureView)CreateView(new RhiTextureViewDescriptor
    {
        Dimension = Dimension,
        Label = Label,
    });

    /// <inheritdoc/>
    public IRhiTextureView CreateView(in RhiTextureViewDescriptor descriptor)
    {
        RhiTextureFormat format = descriptor.Format == RhiTextureFormat.Undefined ? Format : descriptor.Format;
        using WebGpuString label = new(descriptor.Label.Length > 0 ? descriptor.Label : Label);

        WGPUTextureViewDescriptor native = new()
        {
            label = label.View,
            format = WebGpuTranslation.Format(format),
            dimension = WebGpuTranslation.ViewDimension(descriptor.Dimension),
            baseMipLevel = (uint)descriptor.BaseMipLevel,
            mipLevelCount = (uint)(descriptor.MipLevelCount > 0
                ? descriptor.MipLevelCount
                : MipLevelCount - descriptor.BaseMipLevel),
            baseArrayLayer = (uint)descriptor.BaseArrayLayer,
            arrayLayerCount = (uint)(descriptor.ArrayLayerCount > 0
                ? descriptor.ArrayLayerCount
                : Layers() - descriptor.BaseArrayLayer),
            aspect = WGPUTextureAspect.All,
        };

        WGPUTextureView handle = Wgpu.wgpuTextureCreateView(Handle, &native);
        if (!handle.IsValid)
        {
            throw new InvalidOperationException($"The view of '{Label}' could not be created.");
        }

        WebGpuTextureView view = new(handle, this, format, native.label.length == 0 ? Label : descriptor.Label);
        _views.Add(view);
        return view;
    }

    /// <inheritdoc/>
    protected override void Release(WGPUTexture handle)
    {
        // The views first: each holds a count on this texture, and releasing the texture while one is
        // alive leaks both rather than failing.
        foreach (WebGpuTextureView view in _views)
        {
            view.Dispose();
        }

        _views.Clear();
        _whole = null;

        Wgpu.wgpuTextureRelease(handle);
    }

    /// <summary>How many array layers the texture has, which a volume does not.</summary>
    private int Layers() => Dimension == RhiTextureViewDimension.Texture3D ? 1 : DepthOrArrayLayers;
}

/// <summary>A view of a WebGPU texture.</summary>
public sealed unsafe class WebGpuTextureView : WebGpuResource<WGPUTextureView>, IRhiTextureView
{
    /// <summary>Initialises a new instance of the <see cref="WebGpuTextureView"/> class.</summary>
    /// <param name="handle">The WebGPU handle.</param>
    /// <param name="texture">The texture it looks at.</param>
    /// <param name="format">The format it reads through.</param>
    /// <param name="label">The name it was created with.</param>
    public WebGpuTextureView(WGPUTextureView handle, IRhiTexture texture, RhiTextureFormat format, string label)
        : base(handle, label)
    {
        Texture = texture;
        Format = format;
    }

    /// <inheritdoc/>
    public IRhiTexture Texture { get; }

    /// <inheritdoc/>
    public RhiTextureFormat Format { get; }

    /// <inheritdoc/>
    protected override void Release(WGPUTextureView handle) => Wgpu.wgpuTextureViewRelease(handle);
}

/// <summary>A WebGPU sampler.</summary>
public sealed unsafe class WebGpuSampler : WebGpuResource<WGPUSampler>, IRhiSampler
{
    /// <summary>Initialises a new instance of the <see cref="WebGpuSampler"/> class.</summary>
    /// <param name="handle">The WebGPU handle.</param>
    /// <param name="label">The name it was created with.</param>
    public WebGpuSampler(WGPUSampler handle, string label)
        : base(handle, label)
    {
    }

    /// <inheritdoc/>
    protected override void Release(WGPUSampler handle) => Wgpu.wgpuSamplerRelease(handle);
}

/// <summary>A WebGPU shader module, and the entry point the library said it has.</summary>
public sealed unsafe class WebGpuShaderModule : WebGpuResource<WGPUShaderModule>, IRhiShaderModule
{
    /// <summary>Initialises a new instance of the <see cref="WebGpuShaderModule"/> class.</summary>
    /// <param name="handle">The WebGPU handle.</param>
    /// <param name="label">The name it was created with.</param>
    public WebGpuShaderModule(WGPUShaderModule handle, string label)
        : base(handle, label)
    {
    }

    /// <inheritdoc/>
    protected override void Release(WGPUShaderModule handle) => Wgpu.wgpuShaderModuleRelease(handle);
}

/// <summary>A WebGPU bind group layout, which remembers its entries so a group can be checked.</summary>
public sealed unsafe class WebGpuBindGroupLayout : WebGpuResource<WGPUBindGroupLayout>, IRhiBindGroupLayout
{
    /// <summary>Initialises a new instance of the <see cref="WebGpuBindGroupLayout"/> class.</summary>
    /// <param name="handle">The WebGPU handle.</param>
    /// <param name="descriptor">What it was created as.</param>
    public WebGpuBindGroupLayout(WGPUBindGroupLayout handle, RhiBindGroupLayoutDescriptor descriptor)
        : base(handle, descriptor.Label)
    {
        Entries = descriptor.Entries;
    }

    /// <summary>Gets what each slot holds.</summary>
    public IReadOnlyList<RhiBindGroupLayoutEntry> Entries { get; }

    /// <inheritdoc/>
    protected override void Release(WGPUBindGroupLayout handle) => Wgpu.wgpuBindGroupLayoutRelease(handle);
}

/// <summary>A WebGPU bind group.</summary>
public sealed unsafe class WebGpuBindGroup : WebGpuResource<WGPUBindGroup>, IRhiBindGroup
{
    /// <summary>Initialises a new instance of the <see cref="WebGpuBindGroup"/> class.</summary>
    /// <param name="handle">The WebGPU handle.</param>
    /// <param name="label">The name it was created with.</param>
    public WebGpuBindGroup(WGPUBindGroup handle, string label)
        : base(handle, label)
    {
    }

    /// <inheritdoc/>
    protected override void Release(WGPUBindGroup handle) => Wgpu.wgpuBindGroupRelease(handle);
}

/// <summary>A WebGPU pipeline layout, and how much push data it declared.</summary>
public sealed unsafe class WebGpuPipelineLayout : WebGpuResource<WGPUPipelineLayout>, IRhiPipelineLayout
{
    /// <summary>Initialises a new instance of the <see cref="WebGpuPipelineLayout"/> class.</summary>
    /// <param name="handle">The WebGPU handle.</param>
    /// <param name="descriptor">What it was created as.</param>
    public WebGpuPipelineLayout(WGPUPipelineLayout handle, RhiPipelineLayoutDescriptor descriptor)
        : base(handle, descriptor.Label)
    {
        PushDataBytes = descriptor.PushDataBytes;
        GroupCount = descriptor.BindGroupLayouts.Count;
    }

    /// <summary>Gets how many bytes of push data it takes.</summary>
    public int PushDataBytes { get; }

    /// <summary>Gets how many bind groups it takes.</summary>
    public int GroupCount { get; }

    /// <inheritdoc/>
    protected override void Release(WGPUPipelineLayout handle) => Wgpu.wgpuPipelineLayoutRelease(handle);
}

/// <summary>A WebGPU compute pipeline.</summary>
public sealed unsafe class WebGpuComputePipeline : WebGpuResource<WGPUComputePipeline>, IRhiComputePipeline
{
    /// <summary>Initialises a new instance of the <see cref="WebGpuComputePipeline"/> class.</summary>
    /// <param name="handle">The WebGPU handle.</param>
    /// <param name="layout">The layout it was built against.</param>
    /// <param name="label">The name it was created with.</param>
    public WebGpuComputePipeline(WGPUComputePipeline handle, IRhiPipelineLayout layout, string label)
        : base(handle, label)
    {
        Layout = layout;
    }

    /// <inheritdoc/>
    public IRhiPipelineLayout Layout { get; }

    /// <inheritdoc/>
    protected override void Release(WGPUComputePipeline handle) => Wgpu.wgpuComputePipelineRelease(handle);
}

/// <summary>A WebGPU query set.</summary>
public sealed unsafe class WebGpuQuerySet : WebGpuResource<WGPUQuerySet>, IRhiQuerySet
{
    /// <summary>Initialises a new instance of the <see cref="WebGpuQuerySet"/> class.</summary>
    /// <param name="handle">The WebGPU handle.</param>
    /// <param name="descriptor">What it was created as.</param>
    public WebGpuQuerySet(WGPUQuerySet handle, in RhiQuerySetDescriptor descriptor)
        : base(handle, descriptor.Label)
    {
        Type = descriptor.Type;
        Count = descriptor.Count;
    }

    /// <inheritdoc/>
    public RhiQueryType Type { get; }

    /// <inheritdoc/>
    public int Count { get; }

    /// <inheritdoc/>
    protected override void Release(WGPUQuerySet handle) => Wgpu.wgpuQuerySetRelease(handle);
}

/// <summary>Recorded WebGPU commands, ready to submit.</summary>
public sealed unsafe class WebGpuCommandBuffer : WebGpuResource<WGPUCommandBuffer>, IRhiCommandBuffer
{
    /// <summary>Initialises a new instance of the <see cref="WebGpuCommandBuffer"/> class.</summary>
    /// <param name="handle">The WebGPU handle.</param>
    /// <param name="label">The name it was created with.</param>
    public WebGpuCommandBuffer(WGPUCommandBuffer handle, string label)
        : base(handle, label)
    {
    }

    /// <inheritdoc/>
    protected override void Release(WGPUCommandBuffer handle) => Wgpu.wgpuCommandBufferRelease(handle);
}
