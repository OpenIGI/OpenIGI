using OpenIGI.Rendering.D3D12.Interop;
using OpenIGI.Rendering.D3D12.Sync;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace OpenIGI.Rendering.D3D12.Rhi;

/// <summary>Creating resources, bindings and pipelines.</summary>
internal sealed unsafe partial class D3D12RhiDevice
{
    /// <summary><c>D3D12_HEAP_TYPE_GPU_UPLOAD</c>: device-local memory the CPU writes through the BAR.</summary>
    private const HeapType GpuUploadHeap = (HeapType)5;

    /// <inheritdoc/>
    public IRhiBuffer CreateBuffer(in RhiBufferDescriptor descriptor)
    {
        ThrowIfDisposed();
        return CreateBufferCore(descriptor, pooled: true);
    }

    /// <summary>Creates a buffer, in a pool's shared resource where its uses allow and asked to.</summary>
    /// <param name="descriptor">What it is created as.</param>
    /// <param name="pooled">Whether it may share a resource; staging is placed on its own boundaries.</param>
    /// <returns>The buffer.</returns>
    internal D3D12RhiBuffer CreateBufferCore(in RhiBufferDescriptor descriptor, bool pooled)
    {
        RhiBufferUsage usage = descriptor.Usage;

        // Readback memory takes copies and nothing else; upload memory is read by the GPU and nothing
        // else. What the host writes and a build reads -- placements, hit records -- goes to device-local
        // memory the CPU can write where the adapter offers it, as Vulkan puts it in BAR memory.
        HeapType heap = usage.HasFlag(RhiBufferUsage.MapRead)
            ? HeapType.Readback
            : usage.HasFlag(RhiBufferUsage.MapWrite)
                ? (Context.Capabilities.GpuUploadHeap && usage.HasFlag(RhiBufferUsage.RayTracingInput) ? GpuUploadHeap : HeapType.Upload)
                : HeapType.Default;

        // A constant buffer view covers whole 256-byte blocks, and a raw view whole words.
        ulong size = Math.Max(4UL, descriptor.SizeInBytes);
        size = usage.HasFlag(RhiBufferUsage.Uniform) ? (size + 255) & ~255UL : (size + 3) & ~3UL;

        // Pooled where nothing stores to it and nothing views it by element: see D3D12BufferPool.
        if (pooled
            && size <= Memory.D3D12BufferPool.MaxPooledSize
            && (usage & (RhiBufferUsage.Storage | RhiBufferUsage.AccelerationStructureScratch | RhiBufferUsage.MapRead)) == 0
            && PoolFor(heap) is { } pool)
        {
            return Finish(new D3D12RhiBuffer(this, pool, pool.Allocate(size), descriptor, heap));
        }

        ResourceFlags flags = ResourceFlags.None;
        if (usage.HasFlag(RhiBufferUsage.Storage) && heap is HeapType.Default or GpuUploadHeap)
        {
            flags |= ResourceFlags.AllowUnorderedAccess;
        }

        if (usage.HasFlag(RhiBufferUsage.AccelerationStructureScratch))
        {
            flags |= ResourceFlags.AllowUnorderedAccess;
        }

        ResourceDesc1 description = new()
        {
            Dimension = ResourceDimension.Buffer,
            Width = size,
            Height = 1,
            DepthOrArraySize = 1,
            MipLevels = 1,
            Format = Format.FormatUnknown,
            SampleDesc = new SampleDesc(1, 0),
            Layout = TextureLayout.LayoutRowMajor,
            Flags = flags,
        };

        HeapProperties properties = new() { Type = heap };
        ID3D12Resource* resource = null;
        Guid iid = ID3D12Resource.Guid;
        D3D12Check.Ok(
            Native->CreateCommittedResource3(
                &properties,
                HeapFlags.CreateNotZeroed,
                &description,
                BarrierLayout.Undefined,
                null,
                null,
                0,
                null,
                &iid,
                (void**)&resource),
            $"CreateCommittedResource3 (buffer '{descriptor.Label}', {size} bytes)");
        D3D12Names.Set((ID3D12Object*)resource, descriptor.Label);

        return Finish(new D3D12RhiBuffer(this, resource, descriptor, heap, size));
    }

    /// <summary>Gives a new buffer what its uses need beyond its memory.</summary>
    private D3D12RhiBuffer Finish(D3D12RhiBuffer buffer)
    {
        // A traced hit reaches its mesh's buffers by index into the shader-visible heap: a raw view of the
        // whole buffer, made once. Index nought is the null view, so nought keeps meaning "none".
        if (buffer.Usage.HasFlag(RhiBufferUsage.RayTracingInput))
        {
            int index = ShaderHeap.Allocate(1);
            WriteRawView(buffer, 0, buffer.AllocatedBytes, ShaderHeap.Cpu(index));
            buffer.BindlessDescriptor = index;
            buffer.ShaderHandle = (ulong)index;
        }

        return buffer;
    }

    /// <inheritdoc/>
    public IRhiTexture CreateTexture(in RhiTextureDescriptor descriptor)
    {
        ThrowIfDisposed();

        bool depth = descriptor.Format.IsDepth();
        bool volume = descriptor.Dimension == RhiTextureViewDimension.Texture3D;
        bool attachment = descriptor.Usage.HasFlag(RhiTextureUsage.RenderTarget);

        ResourceDesc1 description = new()
        {
            Dimension = volume ? ResourceDimension.Texture3D : ResourceDimension.Texture2D,
            Width = (ulong)Math.Max(1, descriptor.Width),
            Height = (uint)Math.Max(1, descriptor.Height),
            DepthOrArraySize = (ushort)Math.Max(1, descriptor.DepthOrArrayLayers),
            MipLevels = (ushort)Math.Max(1, descriptor.MipLevelCount),
            Format = D3D12Translation.ResourceFormat(descriptor.Format),
            SampleDesc = new SampleDesc((uint)Math.Max(1, descriptor.SampleCount), 0),
            Layout = TextureLayout.LayoutUnknown,
            Flags = D3D12Translation.TextureFlags(descriptor.Usage, depth),
        };

        // Reverse Z clears to nought, every time: telling the driver lets it clear without a fill.
        ClearValue clear = default;
        bool optimised = attachment && depth;
        if (optimised)
        {
            clear.Format = D3D12Translation.ViewFormat(descriptor.Format);
            clear.DepthStencil = new DepthStencilValue(0f, 0);
        }

        HeapProperties properties = new() { Type = HeapType.Default };
        ID3D12Resource* resource = null;
        Guid iid = ID3D12Resource.Guid;
        D3D12Check.Ok(
            Native->CreateCommittedResource3(
                &properties,
                HeapFlags.CreateNotZeroed,
                &description,
                BarrierLayout.Undefined,
                optimised ? &clear : null,
                null,
                0,
                null,
                &iid,
                (void**)&resource),
            $"CreateCommittedResource3 (texture '{descriptor.Label}', {descriptor.Width}x{descriptor.Height} {descriptor.Format})");
        D3D12Names.Set((ID3D12Object*)resource, descriptor.Label);

        if (attachment)
        {
            D3D12Residency.High(Native, resource);
        }

        // Undefined until the first list that uses it is submitted, which moves it into the layout that
        // use needs -- with a discard, for a render or depth target.
        return new D3D12RhiTexture(this, resource, descriptor, BarrierLayout.Undefined);
    }

    /// <inheritdoc/>
    public IRhiSampler CreateSampler(in RhiSamplerDescriptor descriptor)
    {
        ThrowIfDisposed();
        return new D3D12RhiSampler(descriptor);
    }

    /// <inheritdoc/>
    public IRhiShaderModule CreateShaderModule(in RhiShaderModuleDescriptor descriptor)
    {
        ThrowIfDisposed();
        ShaderVariant variant = string.IsNullOrEmpty(descriptor.Variant.Name) ? ShaderVariant.Default : descriptor.Variant;
        return new D3D12RhiShaderModule(
            descriptor.Module,
            variant,
            descriptor.Label.Length > 0 ? descriptor.Label : descriptor.Module.ToString());
    }

    /// <inheritdoc/>
    public IRhiBindGroupLayout CreateBindGroupLayout(RhiBindGroupLayoutDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ThrowIfDisposed();
        return new D3D12RhiBindGroupLayout(descriptor);
    }

    /// <inheritdoc/>
    public IRhiBindGroup CreateBindGroup(RhiBindGroupDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ThrowIfDisposed();

        D3D12RhiBindGroupLayout layout = (D3D12RhiBindGroupLayout)descriptor.Layout;
        int table = layout.TableCount > 0 ? ShaderHeap.Allocate(layout.TableCount) : -1;
        D3D12RhiSampler[] samplers = new D3D12RhiSampler[layout.SamplerCount];
        ulong[] dynamic = new ulong[layout.DynamicCount];
        List<(object Resource, D3D12Use Use)> uses = [];

        foreach (RhiBindGroupEntry entry in descriptor.Entries)
        {
            if (layout.Find(entry.Binding) is not { } slot)
            {
                throw new GraphicsDeviceException($"'{descriptor.Label}' binds {entry.Binding}, which its layout '{layout.Label}' has no entry for.");
            }

            RhiBindGroupLayoutEntry declared = slot.Entry;
            switch (declared.Type)
            {
                case RhiBindingType.Sampler or RhiBindingType.ComparisonSampler:
                    samplers[slot.Table] = (D3D12RhiSampler)(entry.Sampler
                        ?? throw new GraphicsDeviceException($"'{descriptor.Label}' binding {entry.Binding} wants a sampler."));
                    break;

                case RhiBindingType.UniformBuffer or RhiBindingType.ReadOnlyStorageBuffer or RhiBindingType.StorageBuffer:
                {
                    D3D12RhiBuffer buffer = (D3D12RhiBuffer)(entry.Buffer
                        ?? throw new GraphicsDeviceException($"'{descriptor.Label}' binding {entry.Binding} wants a buffer."));
                    ulong length = entry.Size == 0 ? buffer.SizeInBytes - entry.Offset : entry.Size;
                    uses.Add((buffer, declared.Type switch
                    {
                        RhiBindingType.UniformBuffer => D3D12Use.Constant,
                        RhiBindingType.ReadOnlyStorageBuffer => D3D12Use.Sampled,
                        _ => D3D12Use.StorageWrite,
                    }));
                    if (slot.Dynamic >= 0)
                    {
                        dynamic[slot.Dynamic] = buffer.GpuAddress + entry.Offset;
                        break;
                    }

                    WriteBufferView(declared, buffer, entry.Offset, length, ShaderHeap.Cpu(table + slot.Table));
                    break;
                }

                case RhiBindingType.SampledTexture:
                {
                    D3D12RhiTextureView view = (D3D12RhiTextureView)(entry.TextureView
                        ?? throw new GraphicsDeviceException($"'{descriptor.Label}' binding {entry.Binding} wants a texture."));
                    view.WriteShaderResource(ShaderHeap.Cpu(table + slot.Table), declared.ViewDimension);
                    uses.Add((view.Owner, D3D12Use.Sampled));
                    break;
                }

                case RhiBindingType.StorageTexture:
                {
                    D3D12RhiTextureView view = (D3D12RhiTextureView)(entry.TextureView
                        ?? throw new GraphicsDeviceException($"'{descriptor.Label}' binding {entry.Binding} wants a texture."));

                    view.WriteUnorderedAccess(ShaderHeap.Cpu(table + slot.Table), declared.ViewDimension, declared.StorageFormat);
                    uses.Add((view.Owner, declared.StorageAccess == RhiStorageAccess.ReadOnly ? D3D12Use.StorageRead : D3D12Use.StorageWrite));
                    break;
                }

                case RhiBindingType.AccelerationStructure:
                {
                    IRhiAccelerationStructure structure = entry.AccelerationStructure
                        ?? throw new GraphicsDeviceException($"'{descriptor.Label}' binding {entry.Binding} wants an acceleration structure.");
                    WriteAccelerationStructureView(structure, ShaderHeap.Cpu(table + slot.Table));
                    break;
                }
            }
        }

        D3D12SamplerTable? samplerTable = samplers.Length > 0 ? SamplerTableFor(samplers) : null;
        return new D3D12RhiBindGroup(this, layout, table, samplerTable, dynamic, [.. uses], descriptor.Label);
    }

    /// <inheritdoc/>
    public IRhiPipelineLayout CreatePipelineLayout(RhiPipelineLayoutDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ThrowIfDisposed();

        if (descriptor.PushDataBytes > Limits.MaxPushDataBytes)
        {
            throw new GraphicsDeviceException(
                $"The layout '{descriptor.Label}' wants {descriptor.PushDataBytes} bytes of push data and "
                + $"this device offers {Limits.MaxPushDataBytes}.");
        }

        return new D3D12RhiPipelineLayout(descriptor);
    }

    /// <inheritdoc/>
    public IRhiRenderPipeline CreateRenderPipeline(RhiRenderPipelineDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ThrowIfDisposed();
        return new D3D12RhiRenderPipeline(this, descriptor);
    }

    /// <inheritdoc/>
    public IRhiComputePipeline CreateComputePipeline(RhiComputePipelineDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ThrowIfDisposed();
        return new D3D12RhiComputePipeline(this, descriptor);
    }

    /// <inheritdoc/>
    public IRhiQuerySet CreateQuerySet(in RhiQuerySetDescriptor descriptor)
    {
        ThrowIfDisposed();
        QueryHeapDesc description = new()
        {
            Type = descriptor.Type == RhiQueryType.Timestamp ? QueryHeapType.Timestamp : QueryHeapType.Occlusion,
            Count = (uint)descriptor.Count,
        };

        ID3D12QueryHeap* heap = null;
        Guid iid = ID3D12QueryHeap.Guid;
        D3D12Check.Ok(Native->CreateQueryHeap(&description, &iid, (void**)&heap), $"CreateQueryHeap ({descriptor.Label})");
        D3D12Names.Set((ID3D12Object*)heap, descriptor.Label);
        return new D3D12RhiQuerySet(this, heap, descriptor);
    }

    /// <summary>Writes the view a buffer binding declares: constant, structured, raw, read-only or not.</summary>
    private void WriteBufferView(in RhiBindGroupLayoutEntry declared, D3D12RhiBuffer buffer, ulong offset, ulong length, CpuDescriptorHandle destination)
    {
        if (declared.Type == RhiBindingType.UniformBuffer)
        {
            ConstantBufferViewDesc constant = new()
            {
                BufferLocation = buffer.GpuAddress + offset,
                SizeInBytes = (uint)((length + 255) & ~255UL),
            };

            // A view may not run past its buffer; the buffer was rounded up to whole blocks for this.
            constant.SizeInBytes = (uint)Math.Min(constant.SizeInBytes, buffer.AllocatedBytes - offset);
            Native->CreateConstantBufferView(&constant, destination);
            return;
        }

        int stride = declared.ElementStride;
        bool readOnly = declared.Type == RhiBindingType.ReadOnlyStorageBuffer;
        if (stride > 0 && (buffer.Offset + offset) % (ulong)stride != 0)
        {
            throw new GraphicsDeviceException(
                $"'{buffer.Label}' is bound {offset} bytes in, which is not a whole element of {stride} bytes.");
        }

        if (readOnly)
        {
            if (stride > 0)
            {
                ShaderResourceViewDesc view = new()
                {
                    Format = Format.FormatUnknown,
                    ViewDimension = SrvDimension.Buffer,
                    Shader4ComponentMapping = 0x1688,
                };
                view.Buffer = new BufferSrv
                {
                    FirstElement = (buffer.Offset + offset) / (ulong)stride,
                    NumElements = (uint)(length / (ulong)stride),
                    StructureByteStride = (uint)stride,
                };
                Native->CreateShaderResourceView(buffer.Resource, &view, destination);
            }
            else
            {
                WriteRawView(buffer, offset, length, destination);
            }

            return;
        }

        UnorderedAccessViewDesc writable = new()
        {
            Format = stride > 0 ? Format.FormatUnknown : Format.FormatR32Typeless,
            ViewDimension = UavDimension.Buffer,
        };
        writable.Buffer = stride > 0
            ? new BufferUav
            {
                FirstElement = (buffer.Offset + offset) / (ulong)stride,
                NumElements = (uint)(length / (ulong)stride),
                StructureByteStride = (uint)stride,
            }
            : new BufferUav
            {
                FirstElement = (buffer.Offset + offset) / 4,
                NumElements = (uint)(length / 4),
                Flags = BufferUavFlags.Raw,
            };
        Native->CreateUnorderedAccessView(buffer.Resource, null, &writable, destination);
    }

    /// <summary>Writes a raw shader-resource view of part of a buffer, as a <c>ByteAddressBuffer</c> reads it.</summary>
    private void WriteRawView(D3D12RhiBuffer buffer, ulong offset, ulong length, CpuDescriptorHandle destination)
    {
        ShaderResourceViewDesc view = new()
        {
            Format = Format.FormatR32Typeless,
            ViewDimension = SrvDimension.Buffer,
            Shader4ComponentMapping = 0x1688,
        };
        view.Buffer = new BufferSrv
        {
            FirstElement = (buffer.Offset + offset) / 4,
            NumElements = (uint)(length / 4),
            Flags = BufferSrvFlags.Raw,
        };
        Native->CreateShaderResourceView(buffer.Resource, &view, destination);
    }

    /// <summary>Writes the view a ray query reads an acceleration structure through.</summary>
    private void WriteAccelerationStructureView(IRhiAccelerationStructure structure, CpuDescriptorHandle destination)
    {
        if (structure is not ID3D12AccelerationStructureView view)
        {
            throw new GraphicsDeviceException("An acceleration structure from another device was bound.");
        }

        ShaderResourceViewDesc description = new()
        {
            Format = Format.FormatUnknown,
            ViewDimension = SrvDimension.RaytracingAccelerationStructure,
            Shader4ComponentMapping = 0x1688,
        };
        description.RaytracingAccelerationStructure = new RaytracingAccelerationStructureSrv { Location = view.GpuAddress };
        Native->CreateShaderResourceView(null, &description, destination);
    }

    /// <summary>Writes the null view every handle of nought names, at index nought of the shader heap.</summary>
    private void WriteNullView()
    {
        int index = ShaderHeap.Allocate(1);
        ShaderResourceViewDesc view = new()
        {
            Format = Format.FormatR32Typeless,
            ViewDimension = SrvDimension.Buffer,
            Shader4ComponentMapping = 0x1688,
        };
        view.Buffer = new BufferSrv { Flags = BufferSrvFlags.Raw };
        Native->CreateShaderResourceView(null, &view, ShaderHeap.Cpu(index));
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

/// <summary>What an acceleration structure of this backend is bound by.</summary>
internal interface ID3D12AccelerationStructureView
{
    /// <summary>Gets the GPU address the structure starts at.</summary>
    ulong GpuAddress { get; }
}
