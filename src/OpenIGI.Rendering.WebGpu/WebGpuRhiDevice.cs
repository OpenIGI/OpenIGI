using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.WebGpu.Native;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.WebGpu;

/// <summary>
/// The RHI, answered through wgpu-native.
/// </summary>
/// <remarks>
/// <para>
/// Almost every member here is a translation followed by one call: the interface was shaped from
/// WebGPU's model precisely so that this layer has no cleverness in it, and anywhere it starts to grow
/// some is a sign the interface drifted.
/// </para>
/// <para>
/// What it does own is the widening of narrow storage formats (see <see cref="WebGpuFormatPolicy"/>)
/// and the report of when that happened, because nothing above a backend should be deciding what a
/// format is allocated as.
/// </para>
/// </remarks>
public sealed unsafe partial class WebGpuRhiDevice : IRhiDevice
{
    private readonly WebGpuContext _context;
    private readonly bool _ownsContext;
    private readonly List<string> _widened = [];
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="WebGpuRhiDevice"/> class.</summary>
    /// <param name="context">The instance, adapter, device and queue to work through.</param>
    /// <param name="ownsContext">Whether disposing this disposes the context too.</param>
    public WebGpuRhiDevice(WebGpuContext context, bool ownsContext = true)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _ownsContext = ownsContext;
    }

    /// <summary>Creates a device over a context of its own.</summary>
    /// <param name="surface">The surface the adapter must present to, or the null handle for headless.</param>
    /// <param name="lowPower">Whether to prefer the integrated adapter.</param>
    /// <returns>The device.</returns>
    public static WebGpuRhiDevice Create(WGPUSurface surface = default, bool lowPower = false) =>
        new(WebGpuContext.Create(surface, lowPower));

    /// <inheritdoc/>
    public string AdapterName => _context.Report.Name;

    /// <inheritdoc/>
    public RhiFeatures Features => _context.Report.Features;

    /// <inheritdoc/>
    public RhiLimits Limits => _context.Report.Limits;

    /// <inheritdoc/>
    /// <remarks>
    /// Null for now. wgpu-native does offer ray query as an extension of its own, so this is a gap to
    /// fill rather than a thing the backend cannot do — see the adapter report, which already says
    /// whether the feature is there.
    /// </remarks>
    public IRhiRayTracing? RayTracing => null;

    /// <summary>Gets the context this works through.</summary>
    public WebGpuContext Context => _context;

    /// <summary>Gets every texture whose format had to be widened, as sentences.</summary>
    /// <remarks>
    /// Read at start-up and logged. A texture silently costing four times what it was asked for is the
    /// kind of thing that is only ever noticed by somebody measuring something else.
    /// </remarks>
    public IReadOnlyList<string> WidenedFormats => _widened;

    /// <inheritdoc/>
    public IRhiBuffer CreateBuffer(in RhiBufferDescriptor descriptor)
    {
        ThrowIfDisposed();
        using WebGpuString label = new(descriptor.Label);

        WGPUBufferDescriptor native = new()
        {
            label = label.View,
            usage = WebGpuTranslation.BufferUsage(descriptor.Usage),
            size = descriptor.SizeInBytes,
        };

        WGPUBuffer handle = Wgpu.wgpuDeviceCreateBuffer(_context.Device, &native);
        return handle.IsValid
            ? new WebGpuBuffer(handle, descriptor)
            : throw new InvalidOperationException($"The buffer '{descriptor.Label}' could not be created.");
    }

    /// <inheritdoc/>
    public IRhiTexture CreateTexture(in RhiTextureDescriptor descriptor)
    {
        ThrowIfDisposed();

        WebGpuFormatChoice choice = WebGpuFormatPolicy.ForStorage(
            descriptor.Format,
            descriptor.Usage,
            _context.Report.ExtendedStorageFormats);

        if (choice.Widened)
        {
            _widened.Add($"{descriptor.Label}: {choice.Reason}");
        }

        RhiTextureDescriptor allocated = descriptor with { Format = choice.Format };
        using WebGpuString label = new(descriptor.Label);

        WGPUTextureDescriptor native = new()
        {
            label = label.View,
            usage = WebGpuTranslation.TextureUsage(allocated.Usage),
            dimension = WebGpuTranslation.TextureDimension(allocated.Dimension),
            size = new WGPUExtent3D
            {
                width = (uint)allocated.Width,
                height = (uint)allocated.Height,
                depthOrArrayLayers = (uint)allocated.DepthOrArrayLayers,
            },
            format = WebGpuTranslation.Format(allocated.Format),
            mipLevelCount = (uint)Math.Max(1, allocated.MipLevelCount),
            sampleCount = (uint)Math.Max(1, allocated.SampleCount),
        };

        WGPUTexture handle = Wgpu.wgpuDeviceCreateTexture(_context.Device, &native);
        return handle.IsValid
            ? new WebGpuTexture(handle, allocated, descriptor.Format)
            : throw new InvalidOperationException($"The texture '{descriptor.Label}' could not be created.");
    }

    /// <inheritdoc/>
    public IRhiSampler CreateSampler(in RhiSamplerDescriptor descriptor)
    {
        ThrowIfDisposed();
        using WebGpuString label = new(descriptor.Label);

        WGPUSamplerDescriptor native = new()
        {
            label = label.View,
            addressModeU = WebGpuTranslation.AddressMode(descriptor.AddressU),
            addressModeV = WebGpuTranslation.AddressMode(descriptor.AddressV),
            addressModeW = WebGpuTranslation.AddressMode(descriptor.AddressW),
            magFilter = WebGpuTranslation.Filter(descriptor.MagFilter),
            minFilter = WebGpuTranslation.Filter(descriptor.MinFilter),
            mipmapFilter = WebGpuTranslation.MipmapFilter(descriptor.MipFilter),
            lodMinClamp = descriptor.LodMin,
            lodMaxClamp = descriptor.LodMax,
            compare = descriptor.Compare is { } compare
                ? WebGpuTranslation.Compare(compare)
                : WGPUCompareFunction.Undefined,

            // Clamped rather than refused: an adapter reports what it will do and asking for more is a
            // validation failure, while asking for less than the caller wanted only costs sharpness at
            // a grazing angle.
            maxAnisotropy = (ushort)Math.Clamp(descriptor.MaxAnisotropy, 1, 16),
        };

        WGPUSampler handle = Wgpu.wgpuDeviceCreateSampler(_context.Device, &native);
        return handle.IsValid
            ? new WebGpuSampler(handle, descriptor.Label)
            : throw new InvalidOperationException($"The sampler '{descriptor.Label}' could not be created.");
    }

    /// <inheritdoc/>
    public IRhiShaderModule CreateShaderModule(in RhiShaderModuleDescriptor descriptor)
    {
        ThrowIfDisposed();

        // An unnamed variant is the default one, which is how the catalogue reads it too: a descriptor
        // that never mentions a variant is asking for the module every one of them has.
        ShaderVariant variant = string.IsNullOrEmpty(descriptor.Variant.Name)
            ? ShaderVariant.Default
            : descriptor.Variant;

        string name = descriptor.Label.Length > 0 ? descriptor.Label : descriptor.Module.ToString();

        ShaderVariantEntry? entry = null;
        foreach (ShaderVariantEntry candidate in ShaderCatalog.Manifest.Module(descriptor.Module).Variants)
        {
            if (string.Equals(candidate.Name, variant.Name, StringComparison.Ordinal))
            {
                entry = candidate;
                break;
            }
        }

        if (entry is null)
        {
            throw new InvalidOperationException(
                $"The module {descriptor.Module} has no variant named '{variant.Name}'.");
        }

        if (!entry.Compiles(ShaderTarget.Wgsl))
        {
            throw new InvalidOperationException(
                $"The module {descriptor.Module} has no WGSL form for the variant '{variant.Name}', so this "
                + "backend cannot run it. The manifest says why, and the reason is a limit of WGSL rather "
                + "than an oversight.");
        }

        using WebGpuString label = new(name);
        using WebGpuString code = new(ShaderCatalog.GetWgsl(descriptor.Module, variant));

        WGPUShaderSourceWGSL source = new()
        {
            chain = new WGPUChainedStruct { sType = WGPUSType.ShaderSourceWGSL },
            code = code.View,
        };

        WGPUShaderModuleDescriptor native = new()
        {
            nextInChain = (WGPUChainedStruct*)&source,
            label = label.View,
        };

        WGPUShaderModule handle = Wgpu.wgpuDeviceCreateShaderModule(_context.Device, &native);
        return handle.IsValid
            ? new WebGpuShaderModule(handle, name)
            : throw new InvalidOperationException($"The module {descriptor.Module} did not compile.");
    }

    /// <inheritdoc/>
    public IRhiBindGroupLayout CreateBindGroupLayout(RhiBindGroupLayoutDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ThrowIfDisposed();

        using WebGpuString label = new(descriptor.Label);
        WGPUBindGroupLayoutEntry[] entries = new WGPUBindGroupLayoutEntry[descriptor.Entries.Count];

        for (int i = 0; i < entries.Length; i++)
        {
            entries[i] = Translate(descriptor.Entries[i]);
        }

        fixed (WGPUBindGroupLayoutEntry* first = entries)
        {
            WGPUBindGroupLayoutDescriptor native = new()
            {
                label = label.View,
                entryCount = (nuint)entries.Length,
                entries = first,
            };

            WGPUBindGroupLayout handle = Wgpu.wgpuDeviceCreateBindGroupLayout(_context.Device, &native);
            return handle.IsValid
                ? new WebGpuBindGroupLayout(handle, descriptor)
                : throw new InvalidOperationException(
                    $"The bind group layout '{descriptor.Label}' could not be created.");
        }
    }

    /// <inheritdoc/>
    public IRhiBindGroup CreateBindGroup(RhiBindGroupDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ThrowIfDisposed();

        using WebGpuString label = new(descriptor.Label);
        WGPUBindGroupEntry[] entries = new WGPUBindGroupEntry[descriptor.Entries.Count];

        for (int i = 0; i < entries.Length; i++)
        {
            RhiBindGroupEntry entry = descriptor.Entries[i];
            entries[i] = new WGPUBindGroupEntry
            {
                binding = (uint)entry.Binding,
                buffer = entry.Buffer is WebGpuBuffer buffer ? buffer.Handle : default,
                offset = entry.Offset,

                // Zero means "the rest of it" here and "nothing" to WebGPU, so it is resolved rather
                // than passed through: a binding of size zero is a validation failure.
                size = entry.Buffer is { } target
                    ? (entry.Size > 0 ? entry.Size : target.SizeInBytes - entry.Offset)
                    : 0,
                sampler = entry.Sampler is WebGpuSampler sampler ? sampler.Handle : default,
                textureView = entry.TextureView is WebGpuTextureView view ? view.Handle : default,
            };
        }

        fixed (WGPUBindGroupEntry* first = entries)
        {
            WGPUBindGroupDescriptor native = new()
            {
                label = label.View,
                layout = ((WebGpuBindGroupLayout)descriptor.Layout).Handle,
                entryCount = (nuint)entries.Length,
                entries = first,
            };

            WGPUBindGroup handle = Wgpu.wgpuDeviceCreateBindGroup(_context.Device, &native);
            return handle.IsValid
                ? new WebGpuBindGroup(handle, descriptor.Label)
                : throw new InvalidOperationException($"The bind group '{descriptor.Label}' could not be created.");
        }
    }

    /// <inheritdoc/>
    public IRhiPipelineLayout CreatePipelineLayout(RhiPipelineLayoutDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ThrowIfDisposed();

        if (descriptor.PushDataBytes > Limits.MaxPushDataBytes)
        {
            throw new InvalidOperationException(
                $"The layout '{descriptor.Label}' wants {descriptor.PushDataBytes} bytes of push data and "
                + $"this backend offers {Limits.MaxPushDataBytes}.");
        }

        using WebGpuString label = new(descriptor.Label);
        WGPUBindGroupLayout[] groups = new WGPUBindGroupLayout[descriptor.BindGroupLayouts.Count];

        for (int i = 0; i < groups.Length; i++)
        {
            groups[i] = ((WebGpuBindGroupLayout)descriptor.BindGroupLayouts[i]).Handle;
        }

        fixed (WGPUBindGroupLayout* first = groups)
        {
            WGPUPipelineLayoutDescriptor native = new()
            {
                label = label.View,
                bindGroupLayoutCount = (nuint)groups.Length,
                bindGroupLayouts = first,
            };

            WGPUPipelineLayout handle = Wgpu.wgpuDeviceCreatePipelineLayout(_context.Device, &native);
            return handle.IsValid
                ? new WebGpuPipelineLayout(handle, descriptor)
                : throw new InvalidOperationException(
                    $"The pipeline layout '{descriptor.Label}' could not be created.");
        }
    }

    /// <inheritdoc/>
    public IRhiComputePipeline CreateComputePipeline(RhiComputePipelineDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ThrowIfDisposed();

        using WebGpuString label = new(descriptor.Label);
        using WebGpuString entryPoint = new(descriptor.Compute.EntryPoint);

        WGPUComputePipelineDescriptor native = new()
        {
            label = label.View,
            layout = ((WebGpuPipelineLayout)descriptor.Layout).Handle,
            compute = new WGPUComputeState
            {
                module = ((WebGpuShaderModule)descriptor.Compute.Module).Handle,
                entryPoint = entryPoint.View,
            },
        };

        WGPUComputePipeline handle = Wgpu.wgpuDeviceCreateComputePipeline(_context.Device, &native);
        return handle.IsValid
            ? new WebGpuComputePipeline(handle, descriptor.Layout, descriptor.Label)
            : throw new InvalidOperationException($"The compute pipeline '{descriptor.Label}' did not build.");
    }

    /// <inheritdoc/>
    public IRhiQuerySet CreateQuerySet(in RhiQuerySetDescriptor descriptor)
    {
        ThrowIfDisposed();

        if (descriptor.Type == RhiQueryType.Timestamp && !Features.TimestampQuery)
        {
            throw new InvalidOperationException(
                "This device does not offer timestamp queries, so a stage cannot be timed on it.");
        }

        using WebGpuString label = new(descriptor.Label);

        WGPUQuerySetDescriptor native = new()
        {
            label = label.View,
            type = WebGpuTranslation.QueryType(descriptor.Type),
            count = (uint)descriptor.Count,
        };

        WGPUQuerySet handle = Wgpu.wgpuDeviceCreateQuerySet(_context.Device, &native);
        return handle.IsValid
            ? new WebGpuQuerySet(handle, descriptor)
            : throw new InvalidOperationException($"The query set '{descriptor.Label}' could not be created.");
    }

    /// <inheritdoc/>
    public void WriteBuffer<T>(IRhiBuffer buffer, ulong offset, ReadOnlySpan<T> data)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ThrowIfDisposed();

        fixed (T* first = data)
        {
            Wgpu.wgpuQueueWriteBuffer(
                _context.Queue,
                ((WebGpuBuffer)buffer).Handle,
                offset,
                first,
                (nuint)(data.Length * sizeof(T)));
        }
    }

    /// <inheritdoc/>
    public void WriteTexture<T>(
        in RhiTextureRegion destination,
        ReadOnlySpan<T> data,
        int bytesPerRow,
        in RhiExtent3D size)
        where T : unmanaged
    {
        ThrowIfDisposed();

        WGPUTexelCopyTextureInfo target = new()
        {
            texture = ((WebGpuTexture)destination.Texture).Handle,
            mipLevel = (uint)destination.MipLevel,
            origin = new WGPUOrigin3D
            {
                x = (uint)destination.Origin.X,
                y = (uint)destination.Origin.Y,
                z = (uint)destination.Origin.Z,
            },
            aspect = WGPUTextureAspect.All,
        };

        WGPUTexelCopyBufferLayout layout = new()
        {
            bytesPerRow = (uint)bytesPerRow,
            rowsPerImage = (uint)size.Height,
        };

        WGPUExtent3D extent = new()
        {
            width = (uint)size.Width,
            height = (uint)size.Height,
            depthOrArrayLayers = (uint)size.DepthOrArrayLayers,
        };

        fixed (T* first = data)
        {
            Wgpu.wgpuQueueWriteTexture(
                _context.Queue,
                &target,
                first,
                (nuint)(data.Length * sizeof(T)),
                &layout,
                &extent);
        }
    }

    /// <inheritdoc/>
    public void Submit(ReadOnlySpan<IRhiCommandBuffer> commands)
    {
        ThrowIfDisposed();

        if (commands.Length == 0)
        {
            return;
        }

        WGPUCommandBuffer[] handles = new WGPUCommandBuffer[commands.Length];
        for (int i = 0; i < commands.Length; i++)
        {
            handles[i] = ((WebGpuCommandBuffer)commands[i]).Handle;
        }

        fixed (WGPUCommandBuffer* first = handles)
        {
            Wgpu.wgpuQueueSubmit(_context.Queue, (nuint)handles.Length, first);
        }
    }

    /// <inheritdoc/>
    public IRhiCommandEncoder CreateCommandEncoder(string label = "")
    {
        ThrowIfDisposed();
        using WebGpuString name = new(label);

        WGPUCommandEncoderDescriptor native = new() { label = name.View };
        WGPUCommandEncoder handle = Wgpu.wgpuDeviceCreateCommandEncoder(_context.Device, &native);

        return handle.IsValid
            ? new WebGpuCommandEncoder(this, handle, label)
            : throw new InvalidOperationException($"The command encoder '{label}' could not be created.");
    }

    /// <inheritdoc/>
    public IRhiReadback ReadBuffer(IRhiBuffer buffer, ulong offset = 0, ulong sizeInBytes = 0)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ThrowIfDisposed();

        ulong size = sizeInBytes > 0 ? sizeInBytes : buffer.SizeInBytes - offset;
        return new WebGpuReadback(_context, (WebGpuBuffer)buffer, offset, size);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Not built. The raster half of this backend arrives with the surface; what exists today is
    /// compute, copies and readback, which is what the shared pipeline's first passes need.
    /// </remarks>
    public IRhiRenderPipeline CreateRenderPipeline(RhiRenderPipelineDescriptor descriptor) =>
        throw new NotSupportedException(
            "The WebGPU backend builds compute pipelines; render pipelines arrive with the surface.");

    /// <inheritdoc/>
    /// <remarks>Not built; this device is headless until a surface is attached to it.</remarks>
    public bool BeginFrame(out RhiFrame frame) =>
        throw new NotSupportedException("This WebGPU device has no surface, so it presents no frames.");

    /// <inheritdoc/>
    /// <remarks>Not built; this device is headless until a surface is attached to it.</remarks>
    public bool EndFrame(in RhiFrame frame) =>
        throw new NotSupportedException("This WebGPU device has no surface, so it presents no frames.");

    /// <inheritdoc/>
    /// <remarks>Not built; this device is headless until a surface is attached to it.</remarks>
    public void ConfigureSurface(int width, int height, bool vsync) =>
        throw new NotSupportedException("This WebGPU device has no surface to configure.");

    /// <inheritdoc/>
    public void WaitIdle()
    {
        ThrowIfDisposed();
        _context.Poll();
        _context.ProcessEvents();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_ownsContext)
        {
            _context.Dispose();
        }
    }

    /// <summary>One layout entry in WebGPU's terms, which is four unions in a trench coat.</summary>
    /// <remarks>
    /// WebGPU decides what a slot holds by which of the four sub-structures is filled in, so exactly one
    /// is set and the rest stay at their "not used" default. Setting two is not an error it reports; it
    /// takes the first it recognises.
    /// </remarks>
    private WGPUBindGroupLayoutEntry Translate(in RhiBindGroupLayoutEntry entry)
    {
        WGPUBindGroupLayoutEntry native = new()
        {
            binding = (uint)entry.Binding,
            visibility = WebGpuTranslation.ShaderStages(entry.Visibility),
        };

        switch (entry.Type)
        {
            case RhiBindingType.UniformBuffer:
                native.buffer.type = WGPUBufferBindingType.Uniform;
                native.buffer.hasDynamicOffset = entry.HasDynamicOffset ? 1u : 0u;
                break;

            case RhiBindingType.ReadOnlyStorageBuffer:
                native.buffer.type = WGPUBufferBindingType.ReadOnlyStorage;
                native.buffer.hasDynamicOffset = entry.HasDynamicOffset ? 1u : 0u;
                break;

            case RhiBindingType.StorageBuffer:
                native.buffer.type = WGPUBufferBindingType.Storage;
                native.buffer.hasDynamicOffset = entry.HasDynamicOffset ? 1u : 0u;
                break;

            case RhiBindingType.SampledTexture:
                native.texture.sampleType = WebGpuTranslation.SampleType(entry.StorageFormat, Features);
                native.texture.viewDimension = WebGpuTranslation.ViewDimension(entry.ViewDimension);
                native.texture.multisampled = entry.MultisampledTexture ? 1u : 0u;
                break;

            case RhiBindingType.StorageTexture:
                native.storageTexture.access = WebGpuTranslation.StorageAccess(entry.StorageAccess);
                native.storageTexture.format = WebGpuTranslation.Format(
                    WebGpuFormatPolicy.ForStorage(
                        entry.StorageFormat,
                        RhiTextureUsage.Storage,
                        _context.Report.ExtendedStorageFormats).Format);
                native.storageTexture.viewDimension = WebGpuTranslation.ViewDimension(entry.ViewDimension);
                break;

            case RhiBindingType.Sampler:
                native.sampler.type = WGPUSamplerBindingType.Filtering;
                break;

            case RhiBindingType.ComparisonSampler:
                native.sampler.type = WGPUSamplerBindingType.Comparison;
                break;

            case RhiBindingType.AccelerationStructure:
                throw new InvalidOperationException(
                    "This backend offers no acceleration structure binding yet. A pass that wants one asks "
                    + "for IRhiRayTracing first and stands down when it is not there.");

            default:
                throw new ArgumentOutOfRangeException(nameof(entry), entry.Type, "Not a binding type.");
        }

        return native;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
