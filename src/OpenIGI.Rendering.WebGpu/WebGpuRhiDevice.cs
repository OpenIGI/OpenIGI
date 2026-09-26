using System.Runtime.InteropServices;
using OpenIGI.Core.Threading;
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
    private readonly WebGpuSurface? _surface;
    private readonly WebGpuSurfaceSupport _support;
    private readonly IWorkScheduler _finishing;
    private WebGpuPushRing? _pushes;
    private WebGpuTexture? _frameTexture;

    // Where a frame is drawn when the surface's own texture will not last until it is presented: see
    // WebGpuSurface.TexturesLastOneTask. Copied into the surface's texture as the frame is presented.
    // The one the frame being recorded was handed is kept apart, because a resize replaces the texture
    // while that frame may still be drawing into the old one; the old one goes once it has been copied.
    private WebGpuTexture? _drawnFrame;
    private WebGpuTexture? _frameDrawnInto;
    private readonly List<WebGpuTexture> _replacedFrames = [];
    private int _frameIndex;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="WebGpuRhiDevice"/> class.</summary>
    /// <param name="context">The instance, adapter, device and queue to work through.</param>
    /// <param name="ownsContext">Whether disposing this disposes the context too.</param>
    /// <param name="surface">The surface to present to, or null for a headless device.</param>
    /// <param name="finishing">
    /// Where encoders are finished (<see cref="WebGpuCommandEncoder.Finish"/>); inline where none is given.
    /// </param>
    public WebGpuRhiDevice(
        WebGpuContext context,
        bool ownsContext = true,
        WebGpuSurface? surface = null,
        IWorkScheduler? finishing = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _ownsContext = ownsContext;
        _surface = surface;
        _support = surface?.Describe(context.Adapter) ?? default;
        _finishing = finishing ?? InlineWorkScheduler.Instance;
    }

    /// <summary>Gets where this device's encoders are finished.</summary>
    internal IWorkScheduler Finishing => _finishing;

    /// <summary>Creates a device over a context of its own.</summary>
    /// <param name="surface">The surface the adapter must present to, or the null handle for headless.</param>
    /// <param name="lowPower">Whether to prefer the integrated adapter.</param>
    /// <returns>The device.</returns>
    public static WebGpuRhiDevice Create(WGPUSurface surface = default, bool lowPower = false) =>
        new(WebGpuContext.Create(surface, lowPower));

    /// <summary>Creates a device that presents to a window.</summary>
    /// <param name="handles">The window's native handles.</param>
    /// <param name="width">The width to configure the surface at.</param>
    /// <param name="height">The height to configure it at.</param>
    /// <param name="vsync">Whether to wait for the display.</param>
    /// <param name="lowPower">Whether to prefer the integrated adapter.</param>
    /// <param name="finishing">
    /// Where encoders are finished (<see cref="WebGpuCommandEncoder.Finish"/>); inline where none is given.
    /// </param>
    /// <returns>The device, with its surface configured.</returns>
    /// <remarks>
    /// The surface is made before the adapter is asked for, because the adapter has to be one that can
    /// present to it -- on a machine with two, asking the other way round can pick the one the window is
    /// not on.
    /// </remarks>
    public static WebGpuRhiDevice CreateForWindow(
        in WebGpuWindowHandles handles,
        int width,
        int height,
        bool vsync = true,
        bool lowPower = false,
        IWorkScheduler? finishing = null)
    {
        WgpuLibrary.Register();

        WGPUInstance instance = Wgpu.wgpuCreateInstance(null);
        if (!instance.IsValid)
        {
            throw new InvalidOperationException("wgpu-native would not create an instance.");
        }

        WebGpuSurface surface = WebGpuSurface.Create(instance, handles, "openigi");

        try
        {
            WebGpuContext context = WebGpuContext.CreateOn(instance, surface.Handle, lowPower);
            WebGpuRhiDevice device = new(context, ownsContext: true, surface, finishing);
            device.ConfigureSurface(width, height, vsync);
            return device;
        }
        catch
        {
            surface.Dispose();
            Wgpu.wgpuInstanceRelease(instance);
            throw;
        }
    }

    /// <inheritdoc/>
    public string AdapterName => _context.Report.Name;

    /// <inheritdoc/>
    public RhiFeatures Features => _context.Report.Features;

    /// <inheritdoc/>
    public ShaderTarget ShaderTarget => ShaderTarget.Wgsl;

    /// <inheritdoc/>
    public RhiLimits Limits => _context.Report.Limits;

    /// <inheritdoc/>
    /// <remarks>
    /// What the canvas or the offscreen target was configured as. A browser hands out an sRGB image
    /// where this backend's desktop counterpart hands out an unsigned-normalised one, which is exactly
    /// why the shared renderer asks rather than assuming.
    /// </remarks>
    public RhiTextureFormat SurfaceFormat => _offscreen.Count > 0
        ? _offscreen[0].Format
        : _surface?.Format ?? RhiTextureFormat.Bgra8Unorm;

    /// <inheritdoc/>
    /// <remarks>
    /// Null for now. wgpu-native does offer ray query as an extension of its own, so this is a gap to
    /// fill rather than a thing the backend cannot do — see the adapter report, which already says
    /// whether the feature is there.
    /// </remarks>
    public IRhiRayTracing? RayTracing => null;

    /// <summary>Gets the context this works through.</summary>
    public WebGpuContext Context => _context;

    /// <summary>Gets the ring push data is written into, made on first use.</summary>
    /// <remarks>
    /// WebGPU has no push constants, so a push is a slice of a uniform buffer bound by dynamic offset.
    /// See <see cref="WebGpuPushRing"/> for what that costs and when it has to be reset.
    /// </remarks>
    public WebGpuPushRing PushRing => _pushes ??= new WebGpuPushRing(this);

    /// <summary>Makes the group push data is read through over one chunk of the ring.</summary>
    /// <param name="buffer">The chunk's buffer.</param>
    /// <param name="sliceBytes">How much of it one push occupies.</param>
    /// <returns>One dynamic uniform binding over the chunk.</returns>
    /// <remarks>
    /// Bound by the pass rather than by the caller, because the caller knows none of the three things it
    /// takes — the ring, the group index and where the write landed.
    /// </remarks>
    internal IRhiBindGroup CreatePushGroup(IRhiBuffer buffer, int sliceBytes) => CreateBindGroup(new RhiBindGroupDescriptor(
        PushGroupLayout(),
        [RhiBindGroupEntry.Of(WebGpuPushBlocks.Binding, buffer, 0, (ulong)sliceBytes)],
        "push"));

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

        ShaderVariantEntry? entry = ShaderCatalog.Manifest.Module(descriptor.Module).Variant(variant);

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

        // Trusted: the WGSL is the library's own, compiled from the same Slang as the SPIR-V the Vulkan
        // backend runs without a single runtime check. Left in, wgpu guards every array and texture access
        // and puts a counter on every loop -- and the screen gather marches sixteen rays of up to 256 steps
        // for every probe, so the counters alone made its temporal filter two-thirds slower than on
        // Vulkan. A browser has no trusted modules and is never asked for one: there the call reaches the
        // native side through the runtime's interpreter (do_icall), which traps on its 64-bit flags
        // argument as a function signature mismatch -- the page died building its first pipeline.
        WGPUShaderModule handle = OperatingSystem.IsBrowser()
            ? Wgpu.wgpuDeviceCreateShaderModule(_context.Device, &native)
            : Wgpu.wgpuDeviceCreateShaderModuleTrusted(_context.Device, &native, WGPUShaderRuntimeChecks.None);
        return handle.IsValid
            ? new WebGpuShaderModule(handle, name, ShaderCatalog.GetReflection(descriptor.Module, variant))
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

        // A layout that declares push data has to declare the group the ring is bound at as well. The
        // modules read it as an ordinary uniform binding -- there are no push constants here -- so a
        // pipeline whose layout left it out is refused by the driver with a complaint about a global
        // the shader wants and the layout does not have. Groups are positional, so the ones before it
        // are filled with empty layouts where the caller supplied fewer.
        int push = descriptor.PushDataBytes > 0 ? WebGpuPushBlocks.Group : -1;
        int count = push >= 0 ? Math.Max(descriptor.BindGroupLayouts.Count, push + 1) : descriptor.BindGroupLayouts.Count;

        WGPUBindGroupLayout[] groups = new WGPUBindGroupLayout[count];
        List<WebGpuBindGroupLayout> filled = [];

        for (int i = 0; i < count; i++)
        {
            if (i < descriptor.BindGroupLayouts.Count)
            {
                groups[i] = ((WebGpuBindGroupLayout)descriptor.BindGroupLayouts[i]).Handle;
                continue;
            }

            WebGpuBindGroupLayout made = i == push
                ? (WebGpuBindGroupLayout)PushGroupLayout()
                : (WebGpuBindGroupLayout)CreateBindGroupLayout(
                    new RhiBindGroupLayoutDescriptor([], $"{descriptor.Label}-empty{i}"));

            filled.Add(made);
            groups[i] = made.Handle;
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
                ? new WebGpuPipelineLayout(handle, descriptor, filled)
                : throw new InvalidOperationException(
                    $"The pipeline layout '{descriptor.Label}' could not be created.");
        }
    }

    /// <summary>The layout of the group push data is emulated through.</summary>
    /// <returns>One uniform binding, addressed by a dynamic offset into the frame's ring.</returns>
    /// <remarks>
    /// Dynamic because one buffer serves every push of the frame: the ring is written at a running
    /// offset and each draw binds the slice it wrote.
    /// </remarks>
    private IRhiBindGroupLayout PushGroupLayout() => CreateBindGroupLayout(new RhiBindGroupLayoutDescriptor(
        [
            new RhiBindGroupLayoutEntry(
                WebGpuPushBlocks.Binding,
                RhiBindingType.UniformBuffer,
                RhiShaderStages.All,
                HasDynamicOffset: true),
        ],
        "push"));

    /// <inheritdoc/>
    public IRhiComputePipeline CreateComputePipeline(RhiComputePipelineDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ThrowIfDisposed();

        using WebGpuString label = new(descriptor.Label);
        using WebGpuString entryPoint = new(descriptor.Compute.EntryPoint);
        using WebGpuConstants constants = new(descriptor.Compute);

        WGPUComputePipelineDescriptor native = new()
        {
            label = label.View,
            layout = ((WebGpuPipelineLayout)descriptor.Layout).Handle,
            compute = new WGPUComputeState
            {
                module = ((WebGpuShaderModule)descriptor.Compute.Module).Handle,
                entryPoint = entryPoint.View,
                constantCount = constants.Count,
                constants = constants.Entries,
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
            WebGpuHotCalls.wgpuQueueWriteBuffer(
                _context.Queue.Handle,
                ((WebGpuBuffer)buffer).Handle.Handle,
                offset,
                first,
                (nuint)(data.Length * sizeof(T)));
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Texels come in the format the texture was asked for. Where it was allocated wider, because no WGSL
    /// module can bind the narrow one for storage, they are widened on the way in (see
    /// <see cref="WebGpuTexelWidening"/>), so the widening stays this backend's own business.
    /// </remarks>
    public void WriteTexture<T>(
        in RhiTextureRegion destination,
        ReadOnlySpan<T> data,
        int bytesPerRow,
        in RhiExtent3D size)
        where T : unmanaged
    {
        ThrowIfDisposed();

        if (destination.Texture is WebGpuTexture { } texture && texture.RequestedFormat != texture.Format)
        {
            byte[] widened = WebGpuTexelWidening.Widen(
                MemoryMarshal.AsBytes(data),
                texture.RequestedFormat,
                texture.Format,
                size,
                bytesPerRow,
                out int widenedBytesPerRow);

            WriteTexels<byte>(destination, widened, widenedBytesPerRow, size);
            return;
        }

        WriteTexels(destination, data, bytesPerRow, size);
    }

    /// <summary>Writes texels as they are, in the format the texture was allocated in.</summary>
    private void WriteTexels<T>(
        in RhiTextureRegion destination,
        ReadOnlySpan<T> data,
        int bytesPerRow,
        in RhiExtent3D size)
        where T : unmanaged
    {
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

        // The pushes this submission's draws read, uploaded ahead of it in one write a chunk.
        _pushes?.Flush();

        WGPUCommandBuffer[] handles = new WGPUCommandBuffer[commands.Length];
        for (int i = 0; i < commands.Length; i++)
        {
            handles[i] = commands[i] switch
            {
                WebGpuFinishingCommandBuffer finishing => finishing.Finished().Handle,
                IRhiCommandBuffer finished => ((WebGpuCommandBuffer)finished).Handle,
            };
        }

        fixed (WGPUCommandBuffer* first = handles)
        {
            Wgpu.wgpuQueueSubmit(_context.Queue, (nuint)handles.Length, first);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Nothing is held here: every submit goes to the queue as it is made, so there is nothing to send
    /// early, and the wait belongs to the readback that follows -- which polls the queue until it drains,
    /// and on a browser, which will not block, reports what it has instead.
    /// </remarks>
    public void Flush() => ThrowIfDisposed();

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
    /// The acquired texture is owned by this frame and released when it ends: a surface hands one out
    /// anew each time, and holding one past its present keeps the whole rotation alive.
    /// </remarks>
    public bool BeginFrame(out RhiFrame frame)
    {
        ThrowIfDisposed();
        frame = default;

        if (_offscreen.Count > 0)
        {
            frame = new RhiFrame(
                _offscreen[_frameIndex % _offscreen.Count].View,
                _frameIndex,
                _frameIndex % FramesInFlight);

            return true;
        }

        WebGpuSurface surface = _surface
            ?? throw new InvalidOperationException("This device has no surface, so it presents no frames.");

        // The engine presents a frame at the start of the next one, so a frame is recorded across two
        // tasks -- and a canvas's texture does not outlive the first. Drawn here, it is copied into the
        // canvas's texture of the task that presents it (EndFrame).
        if (surface.TexturesLastOneTask)
        {
            _drawnFrame ??= (WebGpuTexture)CreateTexture(new RhiTextureDescriptor(
                surface.Width,
                surface.Height,
                surface.Format,
                RhiTextureUsage.RenderTarget | RhiTextureUsage.CopySource | RhiTextureUsage.Sampled,
                Label: "frame"));

            _frameDrawnInto = _drawnFrame;
            frame = new RhiFrame(_drawnFrame.View, _frameIndex, _frameIndex % FramesInFlight);
            return true;
        }

        WGPUSurfaceGetCurrentTextureStatus status = surface.Acquire(out WebGpuTexture? texture);

        if (texture is null)
        {
            // Outdated is what a resize looks like from here, and the answer to it is to reconfigure and
            // ask again rather than to report a failure.
            NeedsReconfigure = status is WGPUSurfaceGetCurrentTextureStatus.Outdated
                or WGPUSurfaceGetCurrentTextureStatus.Lost;

            return false;
        }

        // Suboptimal presents correctly and says this configuration has been outgrown, so the frame is
        // drawn and the surface reconfigured after it.
        NeedsReconfigure = status == WGPUSurfaceGetCurrentTextureStatus.SuccessSuboptimal;

        _frameTexture = texture;
        frame = new RhiFrame(texture.View, _frameIndex, _frameIndex % FramesInFlight);
        return true;
    }

    /// <inheritdoc/>
    public bool EndFrame(in RhiFrame frame)
    {
        ThrowIfDisposed();

        if (_offscreen.Count > 0)
        {
            // Nothing is presented and nothing waits on a display; the work is already submitted.
            _frameIndex++;
            _pushes?.Reset();
            return true;
        }

        WebGpuSurface surface = _surface
            ?? throw new InvalidOperationException("This device has no surface, so it presents no frames.");

        if (surface.TexturesLastOneTask)
        {
            CopyIntoSurface(surface);
            _frameDrawnInto = null;

            // Copied, and the copy submitted: nothing will be recorded into these again.
            foreach (WebGpuTexture replaced in _replacedFrames)
            {
                replaced.Dispose();
            }

            _replacedFrames.Clear();
        }

        bool presented = surface.Present();

        _frameTexture?.Dispose();
        _frameTexture = null;
        _frameIndex++;

        // The ring is spent for the frame just recorded and free for the next, which is only true
        // because a frame is presented before the next one is begun.
        _pushes?.Reset();

        return presented && !NeedsReconfigure;
    }

    /// <inheritdoc/>
    public void ConfigureSurface(int width, int height, bool vsync)
    {
        ThrowIfDisposed();

        if (_offscreen.Count > 0)
        {
            AttachOffscreen(width, height, _offscreen[0].Format);
            return;
        }

        WebGpuSurface surface = _surface
            ?? throw new InvalidOperationException("This device has no surface to configure.");

        surface.Configure(_context.Device, _support, width, height, vsync);
        NeedsReconfigure = false;

        // Made again at the new size by the next frame. The old one may be what the frame being recorded
        // draws into, so it goes once that frame has been copied out of it.
        if (_drawnFrame is { } replaced)
        {
            _replacedFrames.Add(replaced);
            _drawnFrame = null;
        }
    }

    /// <summary>Copies the frame drawn for a surface whose textures last one task into its texture now.</summary>
    private void CopyIntoSurface(WebGpuSurface surface)
    {
        if (_frameDrawnInto is not { } drawn)
        {
            return;
        }

        WGPUSurfaceGetCurrentTextureStatus status = surface.Acquire(out WebGpuTexture? shown);
        if (shown is null)
        {
            NeedsReconfigure = status is WGPUSurfaceGetCurrentTextureStatus.Outdated
                or WGPUSurfaceGetCurrentTextureStatus.Lost;
            return;
        }

        using (shown)
        {
            using IRhiCommandEncoder encoder = CreateCommandEncoder("present");
            encoder.CopyTextureToTexture(
                new RhiTextureRegion(drawn),
                new RhiTextureRegion(shown),
                new RhiExtent3D(Math.Min(drawn.Width, shown.Width), Math.Min(drawn.Height, shown.Height)));
            Submit([encoder.Finish("present")]);
        }
    }

    /// <summary>Gets whether the surface has to be reconfigured before the next frame.</summary>
    /// <remarks>
    /// Set when the surface says its textures are outdated, which is what a resize looks like from here.
    /// The host answers it by configuring again at the new size.
    /// </remarks>
    public bool NeedsReconfigure { get; private set; }

    /// <summary>Gets the surface this presents to, or null for a headless device.</summary>
    public WebGpuSurface? Surface => _surface;

    /// <summary>Gives this device somewhere to draw when there is no surface to present to.</summary>
    /// <param name="width">The frame's width in pixels.</param>
    /// <param name="height">Its height.</param>
    /// <param name="format">What the frame is drawn in.</param>
    /// <remarks>
    /// A rotation of ordinary textures in place of a surface's. Everything above sees the same frame,
    /// and what changes is that nothing is presented — which is what a headless capture wants, and what
    /// lets the whole renderer be run and read back on a machine with no window.
    /// </remarks>
    public void AttachOffscreen(int width, int height, RhiTextureFormat format)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        ReleaseOffscreen();

        for (int i = 0; i < FramesInFlight; i++)
        {
            _offscreen.Add((WebGpuTexture)CreateTexture(new RhiTextureDescriptor(
                width,
                height,
                format,
                RhiTextureUsage.RenderTarget | RhiTextureUsage.CopySource | RhiTextureUsage.Sampled,
                Label: $"offscreen-{i}")));
        }
    }

    /// <summary>Gets the texture a frame drawn offscreen landed in.</summary>
    /// <param name="index">The frame's index, as it was handed out.</param>
    /// <returns>The texture.</returns>
    public IRhiTexture OffscreenTarget(int index) => _offscreen[index % _offscreen.Count];

    private readonly List<WebGpuTexture> _offscreen = [];

    private void ReleaseOffscreen()
    {
        foreach (WebGpuTexture texture in _offscreen)
        {
            texture.Dispose();
        }

        _offscreen.Clear();
    }

    /// <summary>How many frames may be recorded before one completes.</summary>
    /// <remarks>
    /// Two, which is what the per-frame resources are sized for. WebGPU does not expose the surface's
    /// own image count and does not need to: what this number decides is how many sets of host-written
    /// resources exist, not how many images the surface rotates through.
    /// </remarks>
    public const int FramesInFlight = 2;

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
        _frameTexture?.Dispose();
        _drawnFrame?.Dispose();
        foreach (WebGpuTexture replaced in _replacedFrames)
        {
            replaced.Dispose();
        }

        ReleaseOffscreen();
        _pushes?.Dispose();
        _surface?.Dispose();

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
                native.texture.sampleType = WebGpuTranslation.SampleType(entry.SampleType, entry.StorageFormat, Features);
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
