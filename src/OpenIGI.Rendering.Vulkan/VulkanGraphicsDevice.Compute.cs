using System.Runtime.InteropServices;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Compute;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Textures;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan;

/// <summary>
/// The compute half of the Vulkan backend: programs, shader-writable buffers, storage images and volumes,
/// and the dispatches and barriers built out of them.
/// </summary>
/// <remarks>
/// <para>
/// Compute here is run by <em>immediate submission</em>: a dispatch is recorded into a one-off command
/// buffer, submitted, and waited on before the next is prepared. That matches the seam's contract — the
/// engine's compute is load-time work whose reads block by construction — and it keeps the resource a
/// program owns to one descriptor set and one globals buffer, since neither is ever in flight when the
/// following dispatch rewrites it. Overlapping dispatches on a batched command buffer is the later
/// refinement, and the seam is shaped so that change stays behind it.
/// </para>
/// <para>
/// A generous memory barrier brackets every dispatch. It is coarser than the scope a caller names through
/// <see cref="MemoryBarrier"/>, and deliberately so: with each dispatch on its own submission the cost of
/// a full barrier is paid once against work that has already been made synchronous, in exchange for never
/// having a load-time field come out wrong because a dependency was under-specified. The named barrier is
/// then a no-op that the ordering already guarantees.
/// </para>
/// </remarks>
public sealed partial class VulkanGraphicsDevice
{
    private readonly Dictionary<uint, VulkanComputeShader> _computeShaders = [];
    private readonly Dictionary<uint, VulkanBuffer> _storageBuffers = [];
    private readonly Dictionary<uint, VulkanStorageImage> _storageImages = [];
    private readonly Dictionary<int, VulkanBuffer> _boundStorageBuffers = [];
    private readonly Dictionary<int, VulkanStorageImage> _boundImages = [];
    private readonly Dictionary<int, VulkanStorageImage> _boundVolumes = [];
    private uint _nextComputeId = 1;
    private uint _nextStorageBufferId = 1;
    private bool _computeInFrame;
    private VulkanStorageImage? _dummyVolume;

    /// <inheritdoc/>
    public ComputeShaderHandle CreateComputeShader(string source, string name)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        VulkanComputeShader shader = new(_instance.Vk, _device, _allocator, _shaderCompiler, source, name);
        uint id = _nextComputeId++;
        _computeShaders[id] = shader;
        return new ComputeShaderHandle(id);
    }

    /// <inheritdoc/>
    public void DestroyComputeShader(ComputeShaderHandle shader)
    {
        ThrowIfDisposed();
        if (shader.IsValid && _computeShaders.Remove(shader.Id, out VulkanComputeShader? resource))
        {
            QueueDeletion(resource);
        }
    }

    /// <inheritdoc/>
    public StorageBufferHandle CreateStorageBuffer(int sizeInBytes, string name)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeInBytes);

        VulkanBuffer buffer = new(
            _instance.Vk,
            _device,
            _allocator,
            (ulong)sizeInBytes,
            BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferSrcBit
                | BufferUsageFlags.TransferDstBit | BufferUsageFlags.IndirectBufferBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

        // Zero-filled as the seam's contract requires: almost every storage buffer is an append list with
        // its count at the front, and a count left at the memory's prior contents reads garbage on the
        // first frame. One clear at creation removes that.
        buffer.Write<byte>(new byte[sizeInBytes]);

        uint id = _nextStorageBufferId++;
        _storageBuffers[id] = buffer;
        return new StorageBufferHandle(id);
    }

    /// <inheritdoc/>
    public void UpdateStorageBuffer<T>(StorageBufferHandle buffer, ReadOnlySpan<T> data)
        where T : unmanaged
    {
        ThrowIfDisposed();
        VulkanBuffer resource = ResolveStorage(buffer);
        resource.WriteAt(0, data);
    }

    /// <inheritdoc/>
    public void ReadStorageBuffer<T>(StorageBufferHandle buffer, Span<T> destination)
        where T : unmanaged
    {
        ThrowIfDisposed();
        VulkanBuffer resource = ResolveStorage(buffer);

        // Every prior compute submission has already completed and been made host-visible by its trailing
        // barrier, so the host-coherent memory can be read straight back with no copy.
        resource.Read(destination);
    }

    /// <inheritdoc/>
    public void DestroyStorageBuffer(StorageBufferHandle buffer)
    {
        ThrowIfDisposed();
        if (buffer.IsValid && _storageBuffers.Remove(buffer.Id, out VulkanBuffer? resource))
        {
            QueueDeletion(resource);
        }
    }

    /// <inheritdoc/>
    public void BindStorageBuffer(int slot, StorageBufferHandle buffer)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(slot);

        if (buffer.IsValid)
        {
            _boundStorageBuffers[slot] = ResolveStorage(buffer);
        }
        else
        {
            _boundStorageBuffers.Remove(slot);
        }
    }

    /// <inheritdoc/>
    public void BindImage(int slot, TextureHandle texture, ImageAccess access, int level = 0)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(slot);

        if (texture.IsValid && _storageImages.TryGetValue(texture.Id, out VulkanStorageImage? image))
        {
            _boundImages[slot] = image;
        }
        else
        {
            _boundImages.Remove(slot);
        }
    }

    /// <inheritdoc/>
    public unsafe void Dispatch(ComputeShaderHandle shader, int groupsX, int groupsY = 1, int groupsZ = 1)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(groupsX);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(groupsY);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(groupsZ);

        VulkanComputeShader program = ResolveCompute(shader);
        int x = groupsX;
        int y = groupsY;
        int z = groupsZ;
        RunCompute(program, cmd => _instance.Vk.CmdDispatch(cmd, (uint)x, (uint)y, (uint)z));
    }

    /// <inheritdoc/>
    public unsafe void DispatchIndirect(ComputeShaderHandle shader, StorageBufferHandle arguments, int offsetInBytes = 0)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(offsetInBytes);

        VulkanComputeShader program = ResolveCompute(shader);
        VulkanBuffer args = ResolveStorage(arguments);
        ulong offset = (ulong)offsetInBytes;
        RunCompute(program, cmd => _instance.Vk.CmdDispatchIndirect(cmd, args.Handle, offset));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// When a frame is recording its compute, this records a barrier into the frame command buffer so a
    /// dependent dispatch sees the previous one's writes — the ordering the batched path relies on, since
    /// dispatches there are pipelined rather than each submitted and waited. On the load-time path each
    /// dispatch is already bracketed by a full barrier and submitted on its own, so the call is a no-op the
    /// ordering keeps.
    /// </remarks>
    public unsafe void MemoryBarrier(MemoryBarrierScope scope)
    {
        ThrowIfDisposed();
        if (scope == MemoryBarrierScope.None || !_computeInFrame || _presentPassActive || _deferredPassActive)
        {
            return;
        }

        AccessFlags dst = 0;
        if (scope.HasFlag(MemoryBarrierScope.StorageBuffer))
        {
            dst |= AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit;
        }

        if (scope.HasFlag(MemoryBarrierScope.Image) || scope.HasFlag(MemoryBarrierScope.TextureFetch))
        {
            dst |= AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit;
        }

        if (scope.HasFlag(MemoryBarrierScope.IndirectArguments))
        {
            dst |= AccessFlags.IndirectCommandReadBit;
        }

        if (dst == 0)
        {
            dst = AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit;
        }

        MemoryBarrier barrier = new()
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.ShaderWriteBit,
            DstAccessMask = dst,
        };

        PipelineStageFlags dstStage = scope.HasFlag(MemoryBarrierScope.IndirectArguments)
            ? PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.DrawIndirectBit
            : PipelineStageFlags.ComputeShaderBit;

        _instance.Vk.CmdPipelineBarrier(
            _frameCommandBuffer,
            PipelineStageFlags.ComputeShaderBit,
            dstStage,
            0,
            1,
            &barrier,
            0,
            null,
            0,
            null);
    }

    /// <inheritdoc/>
    public void SetComputeUniform(ComputeShaderHandle shader, string name, float value)
    {
        ThrowIfDisposed();
        Span<byte> bytes = stackalloc byte[4];
        MemoryMarshal.Write(bytes, in value);
        ResolveCompute(shader).SetUniform(name, bytes);
    }

    /// <inheritdoc/>
    public void SetComputeUniform(ComputeShaderHandle shader, string name, int value)
    {
        ThrowIfDisposed();
        VulkanComputeShader program = ResolveCompute(shader);

        // A GL default-block sampler is "set" to a texture unit through this same int call; record the unit
        // so the descriptor write can bind whatever texture sits on it, rather than writing it into the
        // globals block a scalar would go to.
        if (program.TrySetSamplerUnit(name, value))
        {
            return;
        }

        Span<byte> bytes = stackalloc byte[4];
        MemoryMarshal.Write(bytes, in value);
        program.SetUniform(name, bytes);
    }

    /// <inheritdoc/>
    public void SetComputeUniform(ComputeShaderHandle shader, string name, in Vector3f value)
    {
        ThrowIfDisposed();
        Span<float> floats = [value.X, value.Y, value.Z];
        ResolveCompute(shader).SetUniform(name, MemoryMarshal.AsBytes(floats));
    }

    /// <inheritdoc/>
    public void SetComputeUniform(ComputeShaderHandle shader, string name, float x, float y, float z, float w)
    {
        ThrowIfDisposed();
        Span<float> floats = [x, y, z, w];
        ResolveCompute(shader).SetUniform(name, MemoryMarshal.AsBytes(floats));
    }

    /// <inheritdoc/>
    public TextureHandle CreateStorageTexture(int width, int height, VolumeFormat format, string name)
    {
        ThrowIfDisposed();
        if (width <= 0 || height <= 0)
        {
            throw new GraphicsDeviceException($"A storage texture '{name}' needs positive dimensions.");
        }

        return RegisterStorageImage(new VulkanStorageImage(
            _instance.Vk, _device, _allocator, _commands, width, height, 1, format));
    }

    /// <inheritdoc/>
    public TextureHandle CreateVolumeTexture(int width, int height, int depth, VolumeFormat format, string name)
    {
        ThrowIfDisposed();
        if (width <= 0 || height <= 0 || depth <= 0)
        {
            throw new GraphicsDeviceException($"A volume '{name}' needs positive dimensions.");
        }

        return RegisterStorageImage(new VulkanStorageImage(
            _instance.Vk, _device, _allocator, _commands, width, height, depth, format));
    }

    /// <inheritdoc/>
    public unsafe void UpdateVolumeTexture<T>(TextureHandle texture, ReadOnlySpan<T> data)
        where T : unmanaged
    {
        ThrowIfDisposed();
        VulkanStorageImage image = ResolveStorageImage(texture);
        int expected = image.Width * image.Height * image.Depth * image.BytesPerVoxel;
        int offered = data.Length * Marshal.SizeOf<T>();
        if (offered != expected)
        {
            throw new GraphicsDeviceException($"{texture} holds {expected} bytes; {offered} were offered.");
        }

        using VulkanBuffer staging = new(
            _instance.Vk, _device, _allocator, (ulong)expected,
            BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        staging.Write(data);

        Buffer source = staging.Handle;
        _commands.SingleTime(cmd =>
        {
            image.BarrierStoreToRead(cmd);
            BufferImageCopy region = new()
            {
                ImageSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = 0,
                    BaseArrayLayer = 0,
                    LayerCount = 1,
                },
                ImageExtent = new Extent3D((uint)image.Width, (uint)image.Height, (uint)image.Depth),
            };
            _instance.Vk.CmdCopyBufferToImage(cmd, source, image.Image, ImageLayout.General, 1, &region);
        });
    }

    /// <inheritdoc/>
    public void ReadVolumeTexture<T>(TextureHandle texture, Span<T> destination)
        where T : unmanaged
    {
        ThrowIfDisposed();
        VulkanStorageImage image = ResolveStorageImage(texture);
        int expected = image.Width * image.Height * image.Depth * image.BytesPerVoxel;
        if (destination.Length * Marshal.SizeOf<T>() < expected)
        {
            throw new GraphicsDeviceException($"{texture} holds {expected} bytes; the destination is shorter.");
        }

        CopyImageToHost(image, MemoryMarshal.AsBytes(destination)[..expected]);
    }

    /// <inheritdoc/>
    public void ReadStorageTexture(TextureHandle texture, Span<Bgra32> destination)
    {
        ThrowIfDisposed();
        VulkanStorageImage image = ResolveStorageImage(texture);
        int texels = image.Width * image.Height;
        if (destination.Length < texels)
        {
            throw new GraphicsDeviceException($"{texture} is {image.Width}x{image.Height}; the destination is shorter.");
        }

        // Read into the volume's own byte layout, then let the caller's Bgra32 span carry whatever those
        // bytes were — the GL backend does the same conversion-by-readback for a look at the result.
        byte[] raw = new byte[texels * image.BytesPerVoxel];
        CopyImageToHost(image, raw);
        raw.AsSpan(0, Math.Min(raw.Length, destination.Length * 4)).CopyTo(MemoryMarshal.AsBytes(destination));
    }

    /// <inheritdoc/>
    public void SetVolumeTexture(int unit, TextureHandle texture)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(unit);

        if (texture.IsValid && _storageImages.TryGetValue(texture.Id, out VulkanStorageImage? image))
        {
            _boundVolumes[unit] = image;
        }
        else
        {
            _boundVolumes.Remove(unit);
        }
    }

    private unsafe void RunCompute(VulkanComputeShader program, Action<CommandBuffer> dispatch)
    {
        // A frame that is open and not inside a render pass takes the compute into its own command buffer:
        // no separate submit, no CPU stall, and no blocking submission to the graphics queue while the
        // frame is recording — the shape that lost the device when a per-frame gather dispatched mid-frame.
        // Its dependencies are ordered by the barriers MemoryBarrier records and the one drawn before the
        // next render pass. Outside a frame — load time — a dispatch is submitted and waited on its own,
        // which is safe because no frame is in flight to disturb.
        bool inFrame = _frameOpen && !_presentPassActive && !_deferredPassActive;
        VulkanComputeShader.Instance instance = program.Acquire(inFrame, _frameLoop.CurrentFrame);
        WriteDescriptorSet(program, instance);

        Pipeline pipeline = program.Pipeline;
        PipelineLayout layout = program.PipelineLayout;
        DescriptorSet set = instance.Set;

        if (inFrame)
        {
            CommandBuffer cmd = _frameCommandBuffer;
            _instance.Vk.CmdBindPipeline(cmd, PipelineBindPoint.Compute, pipeline);
            DescriptorSet local = set;
            _instance.Vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Compute, layout, 0, 1, &local, 0, null);
            dispatch(cmd);
            _computeInFrame = true;
            return;
        }

        _commands.SingleTime(cmd =>
        {
            GlobalBarrier(cmd);
            _instance.Vk.CmdBindPipeline(cmd, PipelineBindPoint.Compute, pipeline);
            DescriptorSet local = set;
            _instance.Vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Compute, layout, 0, 1, &local, 0, null);
            dispatch(cmd);
            GlobalBarrier(cmd);
        });
    }

    private unsafe void WriteDescriptorSet(VulkanComputeShader program, VulkanComputeShader.Instance instance)
    {
        int count = program.Resources.Count + (program.UniformBinding >= 0 ? 1 : 0);
        if (count == 0)
        {
            return;
        }

        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[count];
        DescriptorBufferInfo* buffers = stackalloc DescriptorBufferInfo[count];
        DescriptorImageInfo* images = stackalloc DescriptorImageInfo[count];
        int index = 0;

        if (program.UniformBinding >= 0)
        {
            buffers[index] = new DescriptorBufferInfo
            {
                Buffer = instance.Globals!.Handle,
                Offset = 0,
                Range = program.GlobalsSize,
            };
            writes[index] = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = instance.Set,
                DstBinding = (uint)program.UniformBinding,
                DescriptorType = DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                PBufferInfo = &buffers[index],
            };
            index++;
        }

        foreach (ComputeShaderAnalysis.Resource resource in program.Resources)
        {
            int slot = (int)resource.Slot;
            switch (resource.Kind)
            {
                case ComputeResourceKind.StorageBuffer:
                    if (!_boundStorageBuffers.TryGetValue(slot, out VulkanBuffer? storage))
                    {
                        throw new GraphicsDeviceException(
                            $"Compute shader '{program.Name}' needs a storage buffer at binding {slot}, none is bound.");
                    }

                    buffers[index] = new DescriptorBufferInfo { Buffer = storage.Handle, Offset = 0, Range = storage.Size };
                    writes[index] = BufferWrite(instance.Set, resource.Binding, DescriptorType.StorageBuffer, &buffers[index]);
                    break;

                case ComputeResourceKind.StorageImage:
                    if (!_boundImages.TryGetValue(slot, out VulkanStorageImage? bound))
                    {
                        throw new GraphicsDeviceException(
                            $"Compute shader '{program.Name}' needs an image at binding {slot}, none is bound.");
                    }

                    images[index] = new DescriptorImageInfo { ImageView = bound.View, ImageLayout = ImageLayout.General };
                    writes[index] = ImageWrite(instance.Set, resource.Binding, DescriptorType.StorageImage, &images[index]);
                    break;

                default:
                    images[index] = ResolveSampled(program, resource);
                    writes[index] = ImageWrite(instance.Set, resource.Binding, DescriptorType.CombinedImageSampler, &images[index]);
                    break;
            }

            index++;
        }

        _instance.Vk.UpdateDescriptorSets(_device.Device, (uint)count, writes, 0, null);
    }

    /// <summary>Resets every compute program's instance ring for a frame slot, and clears the in-frame flag.</summary>
    private void ResetComputeForFrame(int slot)
    {
        _computeInFrame = false;
        foreach (VulkanComputeShader shader in _computeShaders.Values)
        {
            shader.ResetFrame(slot);
        }
    }

    /// <summary>Makes this frame's recorded compute writes visible to the draws of a pass about to begin.</summary>
    /// <remarks>
    /// The compute recorded into the frame command buffer writes storage images and buffers a following
    /// draw samples — a probe volume, a GI atlas. A barrier before the render pass opens makes those writes
    /// available to the vertex and fragment fetches that read them; without it the pass would read whatever
    /// the images held before the gather ran.
    /// </remarks>
    private unsafe void FlushComputeBeforePass()
    {
        if (!_computeInFrame)
        {
            return;
        }

        MemoryBarrier barrier = new()
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.ShaderWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit | AccessFlags.UniformReadBit,
        };

        _instance.Vk.CmdPipelineBarrier(
            _frameCommandBuffer,
            PipelineStageFlags.ComputeShaderBit,
            PipelineStageFlags.VertexShaderBit | PipelineStageFlags.FragmentShaderBit,
            0,
            1,
            &barrier,
            0,
            null,
            0,
            null);
    }

    private DescriptorImageInfo ResolveSampled(VulkanComputeShader program, ComputeShaderAnalysis.Resource resource)
    {
        // A default-block sampler reads from the unit its name was set to; an explicit one from its own
        // binding number, which the API also binds textures by.
        int slot = resource.ByUnit ? program.SamplerUnit(resource.Name) : (int)resource.Slot;

        // A 3D sampler always resolves from the volume table; a 2D sampler from the volume table first
        // (a storage texture bound for sampling) then the ordinary 2D textures.
        if (_boundVolumes.TryGetValue(slot, out VulkanStorageImage? volume))
        {
            return new DescriptorImageInfo
            {
                Sampler = volume.Sampler,
                ImageView = volume.View,
                ImageLayout = ImageLayout.General,
            };
        }

        if (!resource.IsVolume && slot < _boundTextures.Length && _boundTextures[slot].IsValid
            && _textures.TryGetValue(_boundTextures[slot].Id, out VulkanTexture? texture))
        {
            return new DescriptorImageInfo
            {
                Sampler = texture.Sampler,
                ImageView = texture.View,
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            };
        }

        // Nothing is bound to the unit yet. Bind a matching-dimension dummy so the descriptor is valid
        // rather than left undefined — a 1x1x1 volume for a 3D sampler, the white texture for a 2D one.
        if (resource.IsVolume)
        {
            _dummyVolume ??= new VulkanStorageImage(_instance.Vk, _device, _allocator, _commands, 1, 1, 1, VolumeFormat.Rgba8);
            return new DescriptorImageInfo
            {
                Sampler = _dummyVolume.Sampler,
                ImageView = _dummyVolume.View,
                ImageLayout = ImageLayout.General,
            };
        }

        VulkanTexture fallback = _textures[_whiteTexture.Id];
        return new DescriptorImageInfo
        {
            Sampler = fallback.Sampler,
            ImageView = fallback.View,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
        };
    }

    private static unsafe WriteDescriptorSet BufferWrite(DescriptorSet set, uint binding, DescriptorType type, DescriptorBufferInfo* info) => new()
    {
        SType = StructureType.WriteDescriptorSet,
        DstSet = set,
        DstBinding = binding,
        DescriptorType = type,
        DescriptorCount = 1,
        PBufferInfo = info,
    };

    private static unsafe WriteDescriptorSet ImageWrite(DescriptorSet set, uint binding, DescriptorType type, DescriptorImageInfo* info) => new()
    {
        SType = StructureType.WriteDescriptorSet,
        DstSet = set,
        DstBinding = binding,
        DescriptorType = type,
        DescriptorCount = 1,
        PImageInfo = info,
    };

    private unsafe void GlobalBarrier(CommandBuffer cmd)
    {
        MemoryBarrier barrier = new()
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.MemoryWriteBit,
            DstAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit,
        };

        _instance.Vk.CmdPipelineBarrier(
            cmd,
            PipelineStageFlags.AllCommandsBit,
            PipelineStageFlags.AllCommandsBit | PipelineStageFlags.HostBit,
            0,
            1,
            &barrier,
            0,
            null,
            0,
            null);
    }

    private unsafe void CopyImageToHost(VulkanStorageImage image, Span<byte> destination)
    {
        int bytes = image.Width * image.Height * image.Depth * image.BytesPerVoxel;
        using VulkanBuffer staging = new(
            _instance.Vk, _device, _allocator, (ulong)bytes,
            BufferUsageFlags.TransferDstBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

        Buffer target = staging.Handle;
        _commands.SingleTime(cmd =>
        {
            image.BarrierStoreToRead(cmd);
            BufferImageCopy region = new()
            {
                ImageSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = 0,
                    BaseArrayLayer = 0,
                    LayerCount = 1,
                },
                ImageExtent = new Extent3D((uint)image.Width, (uint)image.Height, (uint)image.Depth),
            };
            _instance.Vk.CmdCopyImageToBuffer(cmd, image.Image, ImageLayout.General, target, 1, &region);
        });

        staging.Read(destination);
    }

    private TextureHandle RegisterStorageImage(VulkanStorageImage image)
    {
        uint id = _nextTextureId++;
        _storageImages[id] = image;
        return new TextureHandle(id);
    }

    private VulkanComputeShader ResolveCompute(ComputeShaderHandle shader)
    {
        if (!shader.IsValid || !_computeShaders.TryGetValue(shader.Id, out VulkanComputeShader? resource))
        {
            throw new GraphicsDeviceException($"{shader} does not belong to this device.");
        }

        return resource;
    }

    private VulkanBuffer ResolveStorage(StorageBufferHandle buffer)
    {
        if (!buffer.IsValid || !_storageBuffers.TryGetValue(buffer.Id, out VulkanBuffer? resource))
        {
            throw new GraphicsDeviceException($"{buffer} does not belong to this device.");
        }

        return resource;
    }

    private VulkanStorageImage ResolveStorageImage(TextureHandle texture)
    {
        if (!texture.IsValid || !_storageImages.TryGetValue(texture.Id, out VulkanStorageImage? resource))
        {
            throw new GraphicsDeviceException($"{texture} is not a storage texture or volume on this device.");
        }

        return resource;
    }

    private void DisposeComputeResources()
    {
        foreach (VulkanComputeShader shader in _computeShaders.Values)
        {
            shader.Dispose();
        }

        _computeShaders.Clear();

        foreach (VulkanBuffer buffer in _storageBuffers.Values)
        {
            buffer.Dispose();
        }

        _storageBuffers.Clear();

        foreach (VulkanStorageImage image in _storageImages.Values)
        {
            image.Dispose();
        }

        _storageImages.Clear();
        _dummyVolume?.Dispose();
    }
}
