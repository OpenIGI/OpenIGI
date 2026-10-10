using OpenIGI.Rendering.D3D12.Interop;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace OpenIGI.Rendering.D3D12.Rhi;

/// <summary>A buffer: a committed resource of its own, or a run of a pool's shared one.</summary>
/// <remarks>
/// Everything that names it to Direct3D goes through <see cref="Resource"/> and <see cref="Offset"/>: a pooled
/// buffer starts part-way into a resource other buffers share.
/// </remarks>
internal sealed unsafe class D3D12RhiBuffer : IRhiBuffer
{
    private readonly D3D12RhiDevice _device;
    private readonly Memory.D3D12BufferPool? _pool;
    private readonly Memory.D3D12BufferRange _range;
    private ID3D12Resource* _resource;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="D3D12RhiBuffer"/> class, committed on its own.</summary>
    /// <param name="device">The device that made it.</param>
    /// <param name="resource">The resource, which this owns.</param>
    /// <param name="descriptor">What it was created as.</param>
    /// <param name="heap">The heap it lives in.</param>
    /// <param name="allocatedBytes">How many bytes were allocated, at least the size asked for.</param>
    public D3D12RhiBuffer(D3D12RhiDevice device, ID3D12Resource* resource, in RhiBufferDescriptor descriptor, HeapType heap, ulong allocatedBytes)
    {
        _device = device;
        _resource = resource;
        Label = descriptor.Label;
        SizeInBytes = descriptor.SizeInBytes;
        Usage = descriptor.Usage;
        Heap = heap;
        AllocatedBytes = allocatedBytes;
        GpuAddress = resource->GetGPUVirtualAddress();

        // Upload and readback memory stays mapped for the buffer's life: mapping per write is a kernel call
        // for nothing, and the CPU and the GPU never touch the same bytes at once by the RHI's own rules.
        if (heap != HeapType.Default)
        {
            void* mapped = null;
            Silk.NET.Direct3D12.Range none = default;
            D3D12Check.Ok(resource->Map(0, heap == HeapType.Readback ? null : &none, &mapped), "ID3D12Resource::Map");
            Mapped = (byte*)mapped;
        }
    }

    /// <summary>Initialises a new instance of the <see cref="D3D12RhiBuffer"/> class, as a run of a pool.</summary>
    /// <param name="device">The device that made it.</param>
    /// <param name="pool">The pool, which the run goes back to.</param>
    /// <param name="range">The run.</param>
    /// <param name="descriptor">What it was created as.</param>
    /// <param name="heap">The heap the pool lives in.</param>
    public D3D12RhiBuffer(D3D12RhiDevice device, Memory.D3D12BufferPool pool, Memory.D3D12BufferRange range, in RhiBufferDescriptor descriptor, HeapType heap)
    {
        _device = device;
        _pool = pool;
        _range = range;
        _resource = range.Block.Resource;
        Offset = range.Offset;
        Label = descriptor.Label;
        SizeInBytes = descriptor.SizeInBytes;
        Usage = descriptor.Usage;
        Heap = heap;
        AllocatedBytes = range.Size;
        GpuAddress = range.Block.GpuAddress + range.Offset;
        Mapped = range.Block.Mapped is null ? null : range.Block.Mapped + range.Offset;
    }

    /// <summary>Gets the resource it lives in.</summary>
    public ID3D12Resource* Resource => _resource;

    /// <summary>Gets where in <see cref="Resource"/> it starts: nought unless it is pooled.</summary>
    public ulong Offset { get; }

    /// <summary>Gets the heap it lives in.</summary>
    public HeapType Heap { get; }

    /// <summary>Gets how many bytes were allocated: the size, rounded up for a constant or raw view.</summary>
    public ulong AllocatedBytes { get; }

    /// <summary>Gets its GPU virtual address.</summary>
    public ulong GpuAddress { get; }

    /// <summary>Gets where its memory is mapped, or null for device-local memory.</summary>
    public byte* Mapped { get; }

    /// <summary>Gets or sets the fence value after which the GPU's last write to it is done.</summary>
    public ulong WriteTicket { get; set; }

    /// <inheritdoc/>
    public string Label { get; }

    /// <inheritdoc/>
    public ulong SizeInBytes { get; }

    /// <inheritdoc/>
    public RhiBufferUsage Usage { get; }

    /// <inheritdoc/>
    /// <remarks>The index of a raw view of it in the shader-visible heap, where it is ray-tracing input.</remarks>
    public ulong ShaderHandle { get; set; }

    /// <summary>Gets or sets the descriptor its <see cref="ShaderHandle"/> names, freed with it.</summary>
    public int BindlessDescriptor { get; set; } = -1;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ID3D12Resource* resource = _resource;
        _resource = null;
        int bindless = BindlessDescriptor;
        Memory.D3D12BufferPool? pool = _pool;
        Memory.D3D12BufferRange range = _range;
        _device.Retire(() =>
        {
            if (bindless >= 0)
            {
                _device.ShaderHeap.Free(bindless, 1);
            }

            if (pool is not null)
            {
                pool.Free(range);
            }
            else
            {
                ((Silk.NET.Core.Native.IUnknown*)resource)->Release();
            }
        });
    }
}

/// <summary>A texture: a committed resource, and the layout it was last left in.</summary>
internal sealed unsafe class D3D12RhiTexture : IRhiTexture
{
    private readonly D3D12RhiDevice _device;
    private readonly List<D3D12RhiTextureView> _views = [];
    private readonly bool _owned;
    private ID3D12Resource* _resource;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="D3D12RhiTexture"/> class.</summary>
    /// <param name="device">The device that made it.</param>
    /// <param name="resource">The resource.</param>
    /// <param name="descriptor">What it was created as.</param>
    /// <param name="layout">The layout it starts in.</param>
    /// <param name="owned">Whether this releases it; a swapchain's buffers belong to the swapchain.</param>
    public D3D12RhiTexture(D3D12RhiDevice device, ID3D12Resource* resource, in RhiTextureDescriptor descriptor, BarrierLayout layout, bool owned = true)
    {
        _device = device;
        _resource = resource;
        _owned = owned;
        Descriptor = descriptor;
        Layout = layout;
        Label = descriptor.Label;
        View = CreateView(new RhiTextureViewDescriptor(
            descriptor.Format,
            Dimension: descriptor.Dimension,
            Label: descriptor.Label));
    }

    /// <summary>Gets the resource.</summary>
    public ID3D12Resource* Resource => _resource;

    /// <summary>Gets what it was created as.</summary>
    public RhiTextureDescriptor Descriptor { get; }

    /// <summary>Gets or sets the layout the last SUBMITTED list left it in.</summary>
    /// <remarks>
    /// Not the layout of the last command recorded: lists are recorded in one order and may be submitted in
    /// another -- the upload list goes first whenever it was recorded. Each list says what layout it needs a
    /// texture in first and leaves it in, and its submission reconciles the two with this.
    /// </remarks>
    public BarrierLayout Layout { get; set; }

    /// <inheritdoc/>
    public string Label { get; }

    /// <inheritdoc/>
    public int Width => Descriptor.Width;

    /// <inheritdoc/>
    public int Height => Descriptor.Height;

    /// <inheritdoc/>
    public int DepthOrArrayLayers => Descriptor.DepthOrArrayLayers;

    /// <inheritdoc/>
    public int MipLevelCount => Descriptor.MipLevelCount;

    /// <inheritdoc/>
    public int SampleCount => Descriptor.SampleCount;

    /// <inheritdoc/>
    public RhiTextureFormat Format => Descriptor.Format;

    /// <inheritdoc/>
    public RhiTextureUsage Usage => Descriptor.Usage;

    /// <inheritdoc/>
    public IRhiTextureView View { get; }

    /// <summary>Gets how many array slices its subresources are counted over: one for a volume.</summary>
    public int ArraySlices => Descriptor.Dimension == RhiTextureViewDimension.Texture3D ? 1 : Descriptor.DepthOrArrayLayers;

    /// <inheritdoc/>
    public IRhiTextureView CreateView(in RhiTextureViewDescriptor descriptor)
    {
        RhiTextureViewDescriptor resolved = descriptor with
        {
            Format = descriptor.Format == RhiTextureFormat.Undefined ? Descriptor.Format : descriptor.Format,
            MipLevelCount = descriptor.MipLevelCount == 0 ? Descriptor.MipLevelCount - descriptor.BaseMipLevel : descriptor.MipLevelCount,
            ArrayLayerCount = descriptor.ArrayLayerCount == 0 ? ArraySlices - descriptor.BaseArrayLayer : descriptor.ArrayLayerCount,
        };

        D3D12RhiTextureView view = new(_device, this, resolved);
        _views.Add(view);
        return view;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (D3D12RhiTextureView view in _views)
        {
            view.Release();
        }

        _views.Clear();
        ID3D12Resource* resource = _resource;
        _resource = null;
        if (_owned && resource is not null)
        {
            _device.Retire(() => ((Silk.NET.Core.Native.IUnknown*)resource)->Release());
        }
    }
}

/// <summary>A way of looking at a texture, and the attachment descriptors it was drawn into through.</summary>
/// <remarks>
/// A shader-resource or unordered-access descriptor is not kept here: a bind group writes its own straight
/// into its run of the shader-visible heap. Only the render-target and depth descriptors live with the
/// view, in heaps the GPU never sees, made the first time the view is an attachment.
/// </remarks>
internal sealed unsafe class D3D12RhiTextureView : IRhiTextureView
{
    private readonly D3D12RhiDevice _device;
    private int _rtv = -1;
    private int _dsv = -1;
    private int _dsvReadOnly = -1;

    /// <summary>Initialises a new instance of the <see cref="D3D12RhiTextureView"/> class.</summary>
    /// <param name="device">The device.</param>
    /// <param name="texture">The texture.</param>
    /// <param name="descriptor">The view, with every count resolved.</param>
    public D3D12RhiTextureView(D3D12RhiDevice device, D3D12RhiTexture texture, in RhiTextureViewDescriptor descriptor)
    {
        _device = device;
        Owner = texture;
        Descriptor = descriptor;
        Label = descriptor.Label;
    }

    /// <summary>Gets the texture, as this backend's type.</summary>
    public D3D12RhiTexture Owner { get; }

    /// <summary>Gets the view, every count resolved.</summary>
    public RhiTextureViewDescriptor Descriptor { get; }

    /// <inheritdoc/>
    public IRhiTexture Texture => Owner;

    /// <inheritdoc/>
    public RhiTextureFormat Format => Descriptor.Format;

    /// <inheritdoc/>
    public string Label { get; }

    /// <summary>Writes a shader-resource descriptor for the view.</summary>
    /// <param name="destination">Where.</param>
    /// <param name="dimension">How the slot addresses it, which may differ from how the view was made.</param>
    public void WriteShaderResource(CpuDescriptorHandle destination, RhiTextureViewDimension dimension)
    {
        ShaderResourceViewDesc description = new()
        {
            Format = D3D12Translation.ShaderReadFormat(Descriptor.Format),
            Shader4ComponentMapping = DefaultMapping,
        };

        uint mip = (uint)Descriptor.BaseMipLevel;
        uint mips = (uint)Descriptor.MipLevelCount;
        bool multisampled = Owner.SampleCount > 1;

        switch (dimension)
        {
            case RhiTextureViewDimension.Texture3D:
                description.ViewDimension = SrvDimension.Texture3D;
                description.Texture3D = new Tex3DSrv { MostDetailedMip = mip, MipLevels = mips };
                break;
            case RhiTextureViewDimension.Cube:
                description.ViewDimension = SrvDimension.Texturecube;
                description.TextureCube = new TexcubeSrv { MostDetailedMip = mip, MipLevels = mips };
                break;
            case RhiTextureViewDimension.Texture2DArray:
                description.ViewDimension = multisampled ? SrvDimension.Texture2Dmsarray : SrvDimension.Texture2Darray;
                if (multisampled)
                {
                    description.Texture2DMSArray = new Tex2DmsArraySrv
                    {
                        FirstArraySlice = (uint)Descriptor.BaseArrayLayer,
                        ArraySize = (uint)Descriptor.ArrayLayerCount,
                    };
                }
                else
                {
                    description.Texture2DArray = new Tex2DArraySrv
                    {
                        MostDetailedMip = mip,
                        MipLevels = mips,
                        FirstArraySlice = (uint)Descriptor.BaseArrayLayer,
                        ArraySize = (uint)Descriptor.ArrayLayerCount,
                    };
                }

                break;
            default:
                if (Descriptor.BaseArrayLayer > 0 || Owner.ArraySlices > 1)
                {
                    // One slice of an array, read as a flat texture.
                    description.ViewDimension = SrvDimension.Texture2Darray;
                    description.Texture2DArray = new Tex2DArraySrv
                    {
                        MostDetailedMip = mip,
                        MipLevels = mips,
                        FirstArraySlice = (uint)Descriptor.BaseArrayLayer,
                        ArraySize = 1,
                    };
                }
                else if (multisampled)
                {
                    description.ViewDimension = SrvDimension.Texture2Dms;
                }
                else
                {
                    description.ViewDimension = SrvDimension.Texture2D;
                    description.Texture2D = new Tex2DSrv { MostDetailedMip = mip, MipLevels = mips };
                }

                break;
        }

        _device.Native->CreateShaderResourceView(Owner.Resource, &description, destination);
    }

    /// <summary>Writes an unordered-access descriptor for the view: one mip of it.</summary>
    /// <param name="destination">Where.</param>
    /// <param name="dimension">How the slot addresses it.</param>
    /// <param name="format">The format the slot declares, or undefined for the view's own.</param>
    public void WriteUnorderedAccess(CpuDescriptorHandle destination, RhiTextureViewDimension dimension, RhiTextureFormat format)
    {
        UnorderedAccessViewDesc description = new()
        {
            Format = D3D12Translation.ViewFormat(format == RhiTextureFormat.Undefined ? Descriptor.Format : format),
        };

        uint mip = (uint)Descriptor.BaseMipLevel;
        switch (dimension)
        {
            case RhiTextureViewDimension.Texture3D:
                description.ViewDimension = UavDimension.Texture3D;
                description.Texture3D = new Tex3DUav { MipSlice = mip, FirstWSlice = 0, WSize = uint.MaxValue };
                break;
            case RhiTextureViewDimension.Texture2DArray or RhiTextureViewDimension.Cube:
                description.ViewDimension = UavDimension.Texture2Darray;
                description.Texture2DArray = new Tex2DArrayUav
                {
                    MipSlice = mip,
                    FirstArraySlice = (uint)Descriptor.BaseArrayLayer,
                    ArraySize = (uint)Descriptor.ArrayLayerCount,
                };
                break;
            default:
                if (Descriptor.BaseArrayLayer > 0 || Owner.ArraySlices > 1)
                {
                    description.ViewDimension = UavDimension.Texture2Darray;
                    description.Texture2DArray = new Tex2DArrayUav
                    {
                        MipSlice = mip,
                        FirstArraySlice = (uint)Descriptor.BaseArrayLayer,
                        ArraySize = 1,
                    };
                }
                else
                {
                    description.ViewDimension = UavDimension.Texture2D;
                    description.Texture2D = new Tex2DUav { MipSlice = mip };
                }

                break;
        }

        _device.Native->CreateUnorderedAccessView(Owner.Resource, null, &description, destination);
    }

    /// <summary>Gets the render-target descriptor, made on first use.</summary>
    /// <returns>Its CPU handle.</returns>
    public CpuDescriptorHandle RenderTarget()
    {
        if (_rtv < 0)
        {
            _rtv = _device.RtvHeap.Allocate(1);
            RenderTargetViewDesc description = new() { Format = D3D12Translation.ViewFormat(Descriptor.Format) };
            if (Owner.SampleCount > 1)
            {
                description.ViewDimension = RtvDimension.Texture2Dms;
            }
            else if (Owner.ArraySlices > 1 || Owner.Descriptor.Dimension != RhiTextureViewDimension.Texture2D)
            {
                description.ViewDimension = RtvDimension.Texture2Darray;
                description.Texture2DArray = new Tex2DArrayRtv
                {
                    MipSlice = (uint)Descriptor.BaseMipLevel,
                    FirstArraySlice = (uint)Descriptor.BaseArrayLayer,
                    ArraySize = 1,
                };
            }
            else
            {
                description.ViewDimension = RtvDimension.Texture2D;
                description.Texture2D = new Tex2DRtv { MipSlice = (uint)Descriptor.BaseMipLevel };
            }

            _device.Native->CreateRenderTargetView(Owner.Resource, &description, _device.RtvHeap.Cpu(_rtv));
        }

        return _device.RtvHeap.Cpu(_rtv);
    }

    /// <summary>Gets the depth-stencil descriptor, made on first use.</summary>
    /// <param name="readOnly">Whether the pass only tests against it.</param>
    /// <returns>Its CPU handle.</returns>
    public CpuDescriptorHandle DepthStencil(bool readOnly)
    {
        ref int slot = ref readOnly ? ref _dsvReadOnly : ref _dsv;
        if (slot < 0)
        {
            slot = _device.DsvHeap.Allocate(1);
            DepthStencilViewDesc description = new()
            {
                Format = D3D12Translation.ViewFormat(Descriptor.Format),
                Flags = readOnly
                    ? DsvFlags.ReadOnlyDepth | (Descriptor.Format.HasStencil() ? DsvFlags.ReadOnlyStencil : DsvFlags.None)
                    : DsvFlags.None,
            };

            if (Owner.SampleCount > 1)
            {
                description.ViewDimension = DsvDimension.Texture2Dms;
            }
            else if (Owner.ArraySlices > 1)
            {
                description.ViewDimension = DsvDimension.Texture2Darray;
                description.Texture2DArray = new Tex2DArrayDsv
                {
                    MipSlice = (uint)Descriptor.BaseMipLevel,
                    FirstArraySlice = (uint)Descriptor.BaseArrayLayer,
                    ArraySize = 1,
                };
            }
            else
            {
                description.ViewDimension = DsvDimension.Texture2D;
                description.Texture2D = new Tex2DDsv { MipSlice = (uint)Descriptor.BaseMipLevel };
            }

            _device.Native->CreateDepthStencilView(Owner.Resource, &description, _device.DsvHeap.Cpu(slot));
        }

        return _device.DsvHeap.Cpu(slot);
    }

    /// <summary>Gives the attachment descriptors back, once no frame in flight can use them.</summary>
    public void Release()
    {
        int rtv = _rtv;
        int dsv = _dsv;
        int dsvReadOnly = _dsvReadOnly;
        _rtv = _dsv = _dsvReadOnly = -1;
        if (rtv < 0 && dsv < 0 && dsvReadOnly < 0)
        {
            return;
        }

        _device.Retire(() =>
        {
            if (rtv >= 0)
            {
                _device.RtvHeap.Free(rtv, 1);
            }

            if (dsv >= 0)
            {
                _device.DsvHeap.Free(dsv, 1);
            }

            if (dsvReadOnly >= 0)
            {
                _device.DsvHeap.Free(dsvReadOnly, 1);
            }
        });
    }

    /// <inheritdoc/>
    /// <remarks>Owned by the texture, and released with it.</remarks>
    public void Dispose()
    {
    }

    /// <summary><c>D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING</c>.</summary>
    private const uint DefaultMapping = 0x1688;
}

/// <summary>A sampler: its description, written into a sampler table wherever a bind group needs it.</summary>
/// <param name="descriptor">What it was created as.</param>
internal sealed class D3D12RhiSampler(in RhiSamplerDescriptor descriptor) : IRhiSampler
{
    /// <summary>Gets what it was created as.</summary>
    public RhiSamplerDescriptor Descriptor { get; } = descriptor;

    /// <inheritdoc/>
    public string Label => Descriptor.Label;

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}

/// <summary>A module of the shader library, as Direct3D 12 builds pipelines from it.</summary>
/// <param name="module">The module.</param>
/// <param name="variant">The variant.</param>
/// <param name="label">A name.</param>
internal sealed class D3D12RhiShaderModule(ShaderModuleId module, ShaderVariant variant, string label) : IRhiShaderModule
{
    /// <summary>Gets the module.</summary>
    public ShaderModuleId Module { get; } = module;

    /// <summary>Gets the variant.</summary>
    public ShaderVariant Variant { get; } = variant;

    /// <summary>Gets what it binds, and where its DXIL form binds it.</summary>
    public ShaderReflection Reflection { get; } = ShaderCatalog.GetReflection(module, variant);

    /// <inheritdoc/>
    public string Label { get; } = label;

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}

/// <summary>A query heap.</summary>
internal sealed unsafe class D3D12RhiQuerySet : IRhiQuerySet
{
    private readonly D3D12RhiDevice _device;
    private ID3D12QueryHeap* _heap;

    /// <summary>Initialises a new instance of the <see cref="D3D12RhiQuerySet"/> class.</summary>
    /// <param name="device">The device.</param>
    /// <param name="heap">The heap, which this owns.</param>
    /// <param name="descriptor">What it was created as.</param>
    public D3D12RhiQuerySet(D3D12RhiDevice device, ID3D12QueryHeap* heap, in RhiQuerySetDescriptor descriptor)
    {
        _device = device;
        _heap = heap;
        Type = descriptor.Type;
        Count = descriptor.Count;
        Label = descriptor.Label;
    }

    /// <summary>Gets the heap.</summary>
    public ID3D12QueryHeap* Heap => _heap;

    /// <summary>Gets the query type a slot is begun or ended as.</summary>
    public QueryType NativeType => Type == RhiQueryType.Timestamp ? QueryType.Timestamp : QueryType.Occlusion;

    /// <inheritdoc/>
    public RhiQueryType Type { get; }

    /// <inheritdoc/>
    public int Count { get; }

    /// <inheritdoc/>
    public string Label { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        ID3D12QueryHeap* heap = _heap;
        _heap = null;
        if (heap is not null)
        {
            _device.Retire(() => ((Silk.NET.Core.Native.IUnknown*)heap)->Release());
        }
    }
}

/// <summary>A buffer's contents on their way back, complete once the fence passes the write.</summary>
internal sealed unsafe class D3D12RhiReadback : IRhiReadback
{
    private readonly D3D12RhiDevice _device;
    private readonly D3D12RhiBuffer _buffer;
    private readonly ulong _offset;
    private readonly ulong _ticket;

    /// <summary>Initialises a new instance of the <see cref="D3D12RhiReadback"/> class.</summary>
    /// <param name="device">The device.</param>
    /// <param name="buffer">The readback buffer.</param>
    /// <param name="offset">Where the read starts.</param>
    /// <param name="size">How many bytes.</param>
    /// <param name="ticket">The fence value its contents are final at.</param>
    public D3D12RhiReadback(D3D12RhiDevice device, D3D12RhiBuffer buffer, ulong offset, ulong size, ulong ticket)
    {
        _device = device;
        _buffer = buffer;
        _offset = offset;
        _ticket = ticket;
        SizeInBytes = size;
    }

    /// <inheritdoc/>
    public bool IsComplete => _device.Context.Completed >= _ticket;

    /// <inheritdoc/>
    public ulong SizeInBytes { get; }

    /// <inheritdoc/>
    public bool Wait(TimeSpan timeout) => _device.Context.WaitFor(_ticket, timeout);

    /// <inheritdoc/>
    public ReadOnlySpan<byte> GetData()
    {
        if (!IsComplete)
        {
            throw new InvalidOperationException("The readback has not completed.");
        }

        return new ReadOnlySpan<byte>(_buffer.Mapped + _offset, (int)SizeInBytes);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}

/// <summary>A fence value, complete once the queue has reached it.</summary>
/// <param name="context">The device's context.</param>
/// <param name="ticket">The value.</param>
internal sealed class D3D12RhiWorkMarker(D3D12Context context, ulong ticket) : IRhiWorkMarker
{
    /// <inheritdoc/>
    public bool IsComplete => ticket == 0 || context.Completed >= ticket;
}

/// <summary>A recorded command list, ready to execute, and what it wrote that a readback may wait for.</summary>
internal sealed unsafe class D3D12RhiCommandBuffer : IRhiCommandBuffer
{
    /// <summary>Initialises a new instance of the <see cref="D3D12RhiCommandBuffer"/> class.</summary>
    /// <param name="list">The closed list.</param>
    /// <param name="allocator">The allocator it was recorded from.</param>
    /// <param name="label">A name.</param>
    /// <param name="written">The buffers it copies or resolves into.</param>
    /// <param name="handOffs">The layouts its textures are needed in first and left in.</param>
    public D3D12RhiCommandBuffer(
        ID3D12GraphicsCommandList7* list,
        ID3D12CommandAllocator* allocator,
        string label,
        List<D3D12RhiBuffer> written,
        List<Sync.D3D12LayoutHandOff> handOffs)
    {
        List = list;
        HandOffs = handOffs;
        Allocator = allocator;
        Label = label;
        Written = written;
    }

    /// <summary>Gets the list.</summary>
    public ID3D12GraphicsCommandList7* List { get; }

    /// <summary>Gets the allocator, which goes back to the pool once the list has run.</summary>
    public ID3D12CommandAllocator* Allocator { get; }

    /// <summary>Gets the layouts its textures are needed in first and left in, which its submission applies.</summary>
    public List<Sync.D3D12LayoutHandOff> HandOffs { get; }

    /// <summary>Gets the buffers it writes, stamped with the fence value of its submission.</summary>
    public List<D3D12RhiBuffer> Written { get; }

    /// <summary>Gets or sets a value indicating whether it has been submitted.</summary>
    public bool Submitted { get; set; }

    /// <inheritdoc/>
    public string Label { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}
