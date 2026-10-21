using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.WebGpu.Native;

namespace OpenIGI.Rendering.WebGpu;

/// <summary>The binding half of the device: bind group layouts, bind groups, pipeline layouts, and the group push data is emulated through.</summary>
public sealed unsafe partial class WebGpuRhiDevice
{
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
                    + "for IRhiDevice.AccelerationStructures first and stands down when it is not there.");

            default:
                throw new ArgumentOutOfRangeException(nameof(entry), entry.Type, "Not a binding type.");
        }

        return native;
    }
}
