using System.Runtime.InteropServices;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.WebGpu.Native;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.WebGpu;

/// <summary>The resources of the device: buffers, textures, samplers, shader modules, compute pipelines and query sets, and the queue writes that fill them.</summary>
public sealed unsafe partial class WebGpuRhiDevice
{
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

        // A module from outside the library -- a mod's -- carries its default variant alone, with or without its WGSL form.
        if (!ShaderCatalog.TryGetExternal(descriptor.Module, out _)
            && ShaderCatalog.Manifest.Module(descriptor.Module).Variant(variant) is null)
        {
            throw new InvalidOperationException(
                $"The module {descriptor.Module} has no variant named '{variant.Name}'.");
        }

        if (!ShaderCatalog.Compiles(descriptor.Module, variant, ShaderTarget.Wgsl))
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
    public void WriteBuffer(IRhiBuffer buffer, ulong offset, ReadOnlySpan<byte> data)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ThrowIfDisposed();

        fixed (byte* first = data)
        {
            WebGpuHotCalls.wgpuQueueWriteBuffer(
                _context.Queue.Handle,
                ((WebGpuBuffer)buffer).Handle.Handle,
                offset,
                first,
                (nuint)data.Length);
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

        // A block format is copied in whole blocks: the extent rounded up to the four-by-four block the
        // last row and column fall in -- WebGPU refuses a copy that ends part way through one, which is
        // every mip below four texels -- and the rows counted in blocks rather than texels.
        bool blocks = ((WebGpuTexture)destination.Texture).Format.IsBlockCompressed();
        int copyWidth = blocks ? (size.Width + 3) & ~3 : size.Width;
        int copyHeight = blocks ? (size.Height + 3) & ~3 : size.Height;

        WGPUTexelCopyBufferLayout layout = new()
        {
            bytesPerRow = (uint)bytesPerRow,
            rowsPerImage = (uint)(blocks ? copyHeight / 4 : size.Height),
        };

        WGPUExtent3D extent = new()
        {
            width = (uint)copyWidth,
            height = (uint)copyHeight,
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
}
