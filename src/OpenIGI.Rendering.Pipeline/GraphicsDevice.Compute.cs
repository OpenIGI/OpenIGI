using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Pipeline.Draw2D;
using OpenIGI.Rendering.Pipeline.Passes;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The game's compute seam: the distance field, the surface cache and its gathers, over the RHI.</summary>
/// <remarks>
/// <para>
/// The seam is shaped the way the port's first backend was — storage buffers and images bound to
/// numbered slots, textures to units, parameters set one at a time by name — and every module in the
/// library keeps to one binding space so that shape maps onto it without a table per module: images at
/// 0 to 15, storage buffers at 16 to 31, textures at 32 to 47 with their samplers at 64 and up, and the
/// parameter block at 49 (see <see cref="ComputeBindingSpace"/>). So a dispatch here reads the module's
/// reflection and fills each slot from what the seam bound to it, and nothing about any one module is
/// written down twice.
/// </para>
/// <para>
/// A dispatch inside a frame is recorded into it, in a compute pass of its own — the pass boundary is the
/// barrier, which is why <see cref="MemoryBarrier"/> has nothing to add. Outside a frame, which is a level
/// loading, it is submitted on its own and waited for.
/// </para>
/// <para>
/// The parameter block is snapshotted per dispatch, into a buffer of its own, because the game sets a
/// shader's parameters and dispatches it several times a frame with different ones: one buffer written
/// in place would give every dispatch of the frame the last one's values.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    private readonly Dictionary<uint, ComputeKernel> _kernels = [];
    private readonly Dictionary<uint, IRhiBuffer> _storageBuffers = [];
    private readonly Dictionary<uint, IRhiTexture> _storageTextures = [];

    // What each storage texture was asked for, which is the layout of the texels the game hands over and
    // wants back. A backend may allocate a wider format than this and says so in the texture's own.
    private readonly Dictionary<uint, RhiTextureFormat> _requestedFormats = [];
    private readonly Dictionary<int, IRhiBuffer> _boundBuffers = [];
    private readonly Dictionary<int, IRhiTexture> _boundImages = [];
    private readonly Dictionary<int, TextureHandle> _boundVolumes = [];
    private readonly Dictionary<int, TextureHandle> _boundTextures = [];
    private uint _nextKernel = 1;
    private uint _nextStorageBuffer = 1;
    private IRhiTexture? _standInVolume;
    private IRhiSampler? _computeSampler;
    private ImageHistories? _histories;
    private StandInTextures? _standIns;

    /// <inheritdoc/>
    public ComputeShaderHandle CreateComputeShader(ShaderModuleId shaderModule, ShaderVariant variant, string name)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!ShaderCatalog.Compiles(shaderModule, variant, _device.ShaderTarget))
        {
            throw new GraphicsDeviceException(
                $"The compute module {shaderModule} ({variant.Name}) has no {_device.ShaderTarget} form, so "
                + "this device cannot run it. The manifest says why.");
        }

        ComputeKernel kernel = new(_device, shaderModule, variant, name, FramesInFlight);
        uint id = _nextKernel++;
        _kernels[id] = kernel;
        return new ComputeShaderHandle(id);
    }

    /// <inheritdoc/>
    public void DestroyComputeShader(ComputeShaderHandle shader)
    {
        ThrowIfDisposed();

        if (shader.IsValid && _kernels.Remove(shader.Id, out ComputeKernel? kernel))
        {
            _retired.Retire(kernel);
        }
    }

    /// <inheritdoc/>
    public StorageBufferHandle CreateStorageBuffer(int sizeInBytes, string name)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeInBytes);

        IRhiBuffer buffer = _device.CreateBuffer(new RhiBufferDescriptor(
            (ulong)sizeInBytes,
            RhiBufferUsage.Storage | RhiBufferUsage.CopySource | RhiBufferUsage.CopyDestination | RhiBufferUsage.Indirect,
            name));

        // Zero-filled as the seam's contract says: almost every storage buffer is an append list with its
        // count at the front, and a count left at whatever the memory held reads as garbage on the first
        // frame.
        _device.WriteBuffer<byte>(buffer, 0, new byte[sizeInBytes]);

        uint id = _nextStorageBuffer++;
        _storageBuffers[id] = buffer;
        return new StorageBufferHandle(id);
    }

    /// <inheritdoc/>
    public void UpdateStorageBuffer<T>(StorageBufferHandle buffer, ReadOnlySpan<T> data)
        where T : unmanaged
    {
        ThrowIfDisposed();
        _device.WriteBuffer(StorageBuffer(buffer), 0, data);
    }

    /// <inheritdoc/>
    public void UpdateStorageBuffer<T>(StorageBufferHandle buffer, int elementOffset, ReadOnlySpan<T> data)
        where T : unmanaged
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(elementOffset);
        _device.WriteBuffer(StorageBuffer(buffer), (ulong)elementOffset * (ulong)Unsafe.SizeOf<T>(), data);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Only between frames, which is where the game reads anything back — a validation run, a debug dump.
    /// Inside one, the work that would have to finish first has not been submitted, and waiting for it
    /// would wait for a frame that cannot end.
    /// </remarks>
    public void ReadStorageBuffer<T>(StorageBufferHandle buffer, Span<T> destination)
        where T : unmanaged
    {
        ThrowIfDisposed();

        IRhiBuffer source = StorageBuffer(buffer);
        Span<byte> bytes = MemoryMarshal.AsBytes(destination);
        int length = (int)Math.Min((ulong)bytes.Length, source.SizeInBytes);

        byte[] data = ReadBack(
            (int)source.SizeInBytes,
            (encoder, staging) => encoder.CopyBufferToBuffer(source, 0, staging, 0, source.SizeInBytes));

        data.AsSpan(0, length).CopyTo(bytes);
    }

    /// <inheritdoc/>
    public void DestroyStorageBuffer(StorageBufferHandle buffer)
    {
        ThrowIfDisposed();

        if (buffer.IsValid && _storageBuffers.Remove(buffer.Id, out IRhiBuffer? resource))
        {
            Unbind(_boundBuffers, resource);
            _retired.Retire(resource);
        }
    }

    /// <summary>Takes a destroyed resource out of every compute slot still naming it.</summary>
    /// <remarks>
    /// The slots hold the resource itself, not its handle, so nothing else notices it is gone: a later
    /// kernel that does not rebind that slot would bind it after its retirement had disposed it. An empty
    /// slot fails loudly, or resolves to the dummy of its kind, instead.
    /// </remarks>
    private static void Unbind<T>(Dictionary<int, T> slots, T resource)
        where T : class
    {
        foreach (KeyValuePair<int, T> slot in slots)
        {
            if (ReferenceEquals(slot.Value, resource))
            {
                slots.Remove(slot.Key);
            }
        }
    }

    /// <inheritdoc/>
    public void BindStorageBuffer(int slot, StorageBufferHandle buffer)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(slot);

        if (buffer.IsValid)
        {
            _boundBuffers[slot] = StorageBuffer(buffer);
        }
        else
        {
            _boundBuffers.Remove(slot);
        }
    }

    /// <inheritdoc/>
    public void BindImage(int slot, TextureHandle texture, ImageAccess access, int level = 0)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(slot);

        if (texture.IsValid && _storageTextures.TryGetValue(texture.Id, out IRhiTexture? image))
        {
            if (level != 0)
            {
                throw new GraphicsDeviceException(
                    $"{texture} has one level; level {level} of it cannot be bound for storage.");
            }

            _boundImages[slot] = image;
        }
        else
        {
            _boundImages.Remove(slot);
        }
    }

    /// <inheritdoc/>
    public void SetTexture(int unit, TextureHandle texture)
    {
        ThrowIfDisposed();

        // Only the compute seam reads units on this path; a draw names its textures itself.
        if (texture.IsValid)
        {
            _boundTextures[unit] = texture;
        }
        else
        {
            _boundTextures.Remove(unit);
        }
    }

    /// <inheritdoc/>
    public void SetVolumeTexture(int unit, TextureHandle texture)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(unit);

        if (texture.IsValid)
        {
            _boundVolumes[unit] = texture;
        }
        else
        {
            _boundVolumes.Remove(unit);
        }
    }

    /// <inheritdoc/>
    public void Dispatch(ComputeShaderHandle shader, int groupsX, int groupsY = 1, int groupsZ = 1)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(groupsX);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(groupsY);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(groupsZ);

        ComputeKernel kernel = Kernel(shader);
        RunCompute(kernel, pass => kernel.Pass.DispatchGroups(pass, groupsX, groupsY, groupsZ));
    }

    /// <inheritdoc/>
    public void DispatchIndirect(ComputeShaderHandle shader, StorageBufferHandle arguments, int offsetInBytes = 0)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(offsetInBytes);

        ComputeKernel kernel = Kernel(shader);
        IRhiBuffer counts = StorageBuffer(arguments);
        RunCompute(kernel, pass => kernel.Pass.DispatchIndirect(pass, counts, (ulong)offsetInBytes));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Nothing to record: every dispatch here is a compute pass of its own, and a pass boundary orders
    /// everything before it against everything after — the indirect counts included.
    /// </remarks>
    public void MemoryBarrier(MemoryBarrierScope scope) => ThrowIfDisposed();

    /// <inheritdoc/>
    public void SetComputeUniform(ComputeShaderHandle shader, string name, float value)
    {
        ThrowIfDisposed();
        Kernel(shader).Set(name, MemoryMarshal.AsBytes(new ReadOnlySpan<float>(in value)));
    }

    /// <inheritdoc/>
    public void SetComputeUniform(ComputeShaderHandle shader, string name, int value)
    {
        ThrowIfDisposed();
        ComputeKernel kernel = Kernel(shader);

        // A texture the parameter block also names is read through a UNIT, and this is how the game says
        // which one; the number is not a parameter the shader reads.
        if (!kernel.TrySetUnit(name, value))
        {
            kernel.Set(name, MemoryMarshal.AsBytes(new ReadOnlySpan<int>(in value)));
        }
    }

    /// <inheritdoc/>
    public void SetComputeUniform(ComputeShaderHandle shader, string name, in Vector3f value)
    {
        ThrowIfDisposed();
        Span<float> floats = [value.X, value.Y, value.Z];
        Kernel(shader).Set(name, MemoryMarshal.AsBytes(floats));
    }

    /// <inheritdoc/>
    public void SetComputeUniform(ComputeShaderHandle shader, string name, float x, float y, float z, float w)
    {
        ThrowIfDisposed();
        Span<float> floats = [x, y, z, w];
        Kernel(shader).Set(name, MemoryMarshal.AsBytes(floats));
    }

    /// <inheritdoc/>
    public TextureHandle CreateStorageTexture(int width, int height, VolumeFormat format, string name)
    {
        ThrowIfDisposed();

        if (width <= 0 || height <= 0)
        {
            throw new GraphicsDeviceException($"A storage texture '{name}' needs positive dimensions.");
        }

        return CreateImage(width, height, 1, format, RhiTextureViewDimension.Texture2D, name);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A volume no compute pass writes is allocated without storage usage, so a backend that has to widen
    /// a storage format -- WebGPU, for a one-byte texel -- keeps it as it was asked for.
    /// </remarks>
    public TextureHandle CreateVolumeTexture(
        int width,
        int height,
        int depth,
        VolumeFormat format,
        string name,
        bool shaderWritten = true)
    {
        ThrowIfDisposed();

        if (width <= 0 || height <= 0 || depth <= 0)
        {
            throw new GraphicsDeviceException($"A volume '{name}' needs positive dimensions.");
        }

        return CreateImage(width, height, depth, format, RhiTextureViewDimension.Texture3D, name, shaderWritten);
    }

    /// <inheritdoc/>
    public void UpdateVolumeTexture<T>(TextureHandle texture, ReadOnlySpan<T> data)
        where T : unmanaged
    {
        ThrowIfDisposed();

        IRhiTexture image = StorageImage(texture);
        int bytesPerTexel = _requestedFormats[texture.Id].BytesPerTexel();
        int expected = image.Width * image.Height * image.DepthOrArrayLayers * bytesPerTexel;
        int offered = data.Length * Marshal.SizeOf<T>();

        if (offered != expected)
        {
            throw new GraphicsDeviceException($"{texture} holds {expected} bytes; {offered} were offered.");
        }

        _device.WriteTexture(
            new RhiTextureRegion(image),
            MemoryMarshal.AsBytes(data),
            image.Width * bytesPerTexel,
            new RhiExtent3D(image.Width, image.Height, image.DepthOrArrayLayers));
    }

    /// <inheritdoc/>
    public void ReadVolumeTexture<T>(TextureHandle texture, Span<T> destination)
        where T : unmanaged
    {
        ThrowIfDisposed();
        ReadImage(StorageImage(texture), MemoryMarshal.AsBytes(destination));
    }

    /// <inheritdoc/>
    public void ReadStorageTexture(TextureHandle texture, Span<Bgra32> destination)
    {
        ThrowIfDisposed();

        // The image's own bytes, carried in whatever the caller's texels are, as the other backends do.
        ReadImage(StorageImage(texture), MemoryMarshal.AsBytes(destination));
    }

    /// <summary>Starts a frame's compute over: every shader's parameter buffers for this slot are free again.</summary>
    private void ResetComputeFrame()
    {
        foreach (ComputeKernel kernel in _kernels.Values)
        {
            kernel.Reset(FrameSlot);
        }
    }

    /// <summary>Fills every slot the kernel's module declares from what the seam bound, then records the dispatch.</summary>
    /// <remarks>
    /// <para>
    /// Recorded into the frame whenever there is one to record into -- including a frame that has not
    /// opened yet, once the game is presenting. The surface cache's lighting and the distance field's
    /// clipmaps are dispatched before a frame's first draw, and the menu does that every frame: taken
    /// for load-time work, each of those dispatches was submitted on its own and waited on until the
    /// device was idle, which stalled the CPU on the GPU twice a frame and left the two never working
    /// at once -- half the menu's main thread, on both backends.
    /// </para>
    /// <para>
    /// Before the first frame has been presented it is load time: submitted and waited on, so the slot
    /// past the frames in flight is always free.
    /// </para>
    /// </remarks>
    private void RunCompute(ComputeKernel kernel, Action<IRhiComputePassEncoder> dispatch)
    {
        bool inFrame = _inFrame || (_frameCounter > 0 && !CapturingCards);

        // Load time submits and waits, so the slot past the frames in flight is always free.
        int slot = inFrame ? FrameSlot : FramesInFlight;

        if (inFrame)
        {
            // A render pass cannot hold a dispatch; whichever one is open ends here and the next draw
            // opens it again. Encoder() below opens the frame itself if it has not begun.
            ClosePass();
            CloseScenePass();
            CloseShadowPass();

            IRhiCommandEncoder frame = Encoder();
            BindKernel(kernel, slot, RecordHistories(kernel, frame));

            // Timed under the GI stage, which is what every dispatch through the seam is: the surface cache's
            // lighting, its radiance cache, the screen gather and the distance field. Untimed, the stack was
            // invisible to the stage timings and the benchmark's GPU total alike.
            using IRhiComputePassEncoder pass = frame.BeginComputePass(new RhiComputePassDescriptor(
                _profiler.Time(RenderStage.GlobalIllumination),
                kernel.Label));
            dispatch(pass);
            return;
        }

        using (IRhiCommandEncoder encoder = _device.CreateCommandEncoder(kernel.Label))
        {
            BindKernel(kernel, slot, RecordHistories(kernel, encoder));

            using (IRhiComputePassEncoder pass = encoder.BeginComputePass(new RhiComputePassDescriptor(Label: kernel.Label)))
            {
                dispatch(pass);
            }

            _device.Submit([encoder.Finish(kernel.Label)]);
        }

        _device.WaitIdle();
        kernel.Reset(slot);
    }

    /// <summary>
    /// Copies every image the module reads back into its history, where the module's form reads it from
    /// there rather than from the image.
    /// </summary>
    /// <returns>Each history's view by the name of its slot, or null where this form reads none.</returns>
    /// <remarks>See <see cref="ImageHistories"/>; on a SPIR-V device the image is read directly and this does nothing.</remarks>
    private Dictionary<string, IRhiTextureView>? RecordHistories(ComputeKernel kernel, IRhiCommandEncoder encoder)
    {
        if (!ImageHistories.AreRead(_device))
        {
            return null;
        }

        Dictionary<string, IRhiTextureView>? views = null;

        foreach (ShaderBinding binding in kernel.Reflection.Bindings)
        {
            if (ImageHistories.ImageOf(kernel.Reflection, binding) is not { } image)
            {
                continue;
            }

            _histories ??= new ImageHistories(_device, _retired);
            views ??= new Dictionary<string, IRhiTextureView>(StringComparer.Ordinal);
            views[binding.Name] = _histories.Record(
                encoder,
                BoundImage(kernel, image),
                image.Dimensions == 3 ? RhiTextureViewDimension.Texture3D : RhiTextureViewDimension.Texture2D);
        }

        return views;
    }

    /// <summary>Points each of the module's slots at what the seam has bound to it.</summary>
    /// <param name="kernel">The kernel about to be dispatched.</param>
    /// <param name="slot">Which of its parameter buffers this dispatch writes.</param>
    /// <param name="histories">What each image read back held, where the module's form reads it from a copy.</param>
    private void BindKernel(ComputeKernel kernel, int slot, Dictionary<string, IRhiTextureView>? histories)
    {
        HashSet<IRhiTexture> written = [.. kernel.Reflection.Bindings
            .Where(binding => binding.Kind == ShaderBindingKind.StorageTexture)
            .Select(binding => BoundImage(kernel, binding))];

        foreach (ShaderBinding binding in kernel.Reflection.Bindings)
        {
            switch (binding.Kind)
            {
                case ShaderBindingKind.StorageTexture:
                    kernel.Pass.Bind(binding.Name, BoundImage(kernel, binding).View);
                    break;

                // Declared on every form and read only where the form cannot read the image itself.
                case ShaderBindingKind.Texture when ImageHistories.ImageOf(kernel.Reflection, binding) is not null:
                    kernel.Pass.Bind(
                        binding.Name,
                        histories?.GetValueOrDefault(binding.Name) ?? StandInFor(binding));
                    break;

                case ShaderBindingKind.StorageBuffer:
                    kernel.Pass.Bind(binding.Name, BoundBuffer(kernel, binding));
                    break;

                case ShaderBindingKind.Texture:
                    kernel.Pass.Bind(binding.Name, SampledOrStandIn(kernel, binding, written));
                    break;

                case ShaderBindingKind.Sampler:
                    kernel.Pass.Bind(binding.Name, SamplerAt(kernel, binding));
                    break;

                case ShaderBindingKind.UniformBuffer when binding.Binding == ComputeBindingSpace.GlobalsBinding:
                    kernel.Pass.Bind(binding.Name, kernel.WriteGlobals(slot));
                    break;

                case ShaderBindingKind.AccelerationStructure:
                    kernel.Pass.Bind(
                        binding.Name,
                        TracedWorld?.TraceableScene ?? throw new GraphicsDeviceException(
                            $"'{kernel.Label}' traces rays, which this device does not offer; its software "
                            + "variant is the one to create here."));
                    break;
            }
        }
    }

    /// <summary>What a sampled slot reads, unless nothing correct could come of reading it.</summary>
    /// <remarks>
    /// <para>
    /// The game keeps the original's texture units: what it set on a unit stays there until it sets
    /// something else, across every program and every mode of one, and a program's slot a mode never
    /// samples still finds whatever the last dispatch left. GL did not care. WebGPU validates every slot
    /// of every dispatch, so two leftovers it refuses read a stand-in instead:
    /// </para>
    /// <list type="bullet">
    /// <item>an image the same dispatch writes, which WebGPU will not also bind for sampling and which on
    /// Vulkan a sample would race;</item>
    /// <item>on WebGPU, a texture of the wrong kind for the slot — a depth image on a colour slot, or the
    /// reverse — which it refuses outright.</item>
    /// </list>
    /// <para>
    /// The kind is only checked where the device runs WGSL, because only there is it known and only there
    /// does it matter. What a slot declares is read off the WGSL a module compiles to, so a variant with no
    /// WGSL form -- the ray-query ones, which WebGPU cannot run -- reports every texture as a colour slot.
    /// Vulkan reads a depth image through either kind correctly, and checking there swapped the scene's own
    /// depth out of the traced screen gather for a stand-in: with tracing on, the RHI's screen GI found no
    /// sky and no real surfaces, smeared its history, and shifted colour as the camera moved.
    /// </para>
    /// </remarks>
    private IRhiTextureView SampledOrStandIn(ComputeKernel kernel, ShaderBinding binding, HashSet<IRhiTexture> written)
    {
        IRhiTextureView view = SampledAt(kernel, binding).View;
        bool depthSlot = binding.SampleType == ShaderSampleType.Depth;
        bool wrongKind = _device.ShaderTarget == ShaderTarget.Wgsl && depthSlot != view.Format.IsDepth();

        return written.Contains(view.Texture) || wrongKind
            ? StandInFor(binding)
            : view;
    }

    private IRhiTexture BoundImage(ComputeKernel kernel, ShaderBinding binding) =>
        _boundImages.TryGetValue(ComputeBindingSpace.SlotOf(binding.Binding), out IRhiTexture? image)
            ? image
            : throw new GraphicsDeviceException(
                $"'{kernel.Label}' needs an image at binding {binding.Binding} ({binding.Name}); none is bound.");

    private IRhiBuffer BoundBuffer(ComputeKernel kernel, ShaderBinding binding) =>
        _boundBuffers.TryGetValue(ComputeBindingSpace.SlotOf(binding.Binding), out IRhiBuffer? buffer)
            ? buffer
            : throw new GraphicsDeviceException(
                $"'{kernel.Label}' needs a storage buffer at binding {binding.Binding} ({binding.Name}); none is bound.");

    /// <summary>What a sampled slot reads: the volume or the texture on its unit, or a stand-in of its shape.</summary>
    /// <remarks>
    /// A unit can hold a flat texture and a volume at once, as the original's API allowed, and the slot's
    /// own dimension is what chooses between them. A slot nothing is bound to reads a stand-in of the
    /// right shape rather than being left undefined.
    /// </remarks>
    private RhiTextureBinding SampledAt(ComputeKernel kernel, ShaderBinding binding)
    {
        int unit = kernel.UnitOf(binding);

        if (binding.Dimensions == 3)
        {
            return _boundVolumes.TryGetValue(unit, out TextureHandle volume) && _textures.Find(volume) is not null
                ? _textures.Resolve(volume)
                : new RhiTextureBinding(0, StandInVolume().View, ComputeSampler());
        }

        if (!_boundTextures.TryGetValue(unit, out TextureHandle texture))
        {
            return _textures.Resolve(TextureHandle.None);
        }

        // One of the frame's own images, handed to the game as a handle it keeps across resizes.
        return TryResolveAlias(texture, out RhiTextureBinding alias) ? alias : _textures.Resolve(texture);
    }

    /// <summary>The sampler a slot reads through: its texture's own, wherever that texture was found.</summary>
    /// <remarks>
    /// The library names a sampler after its texture, so the pair is found by name — which is also what
    /// keeps them together when the texture is read through a unit rather than its own slot.
    /// </remarks>
    private IRhiSampler SamplerAt(ComputeKernel kernel, ShaderBinding sampler)
    {
        string textureName = sampler.Name.EndsWith(ShaderBindings.SamplerSuffix, StringComparison.Ordinal)
            ? sampler.Name[..^ShaderBindings.SamplerSuffix.Length]
            : sampler.Name;

        ShaderBinding? texture = kernel.Reflection.Bindings.FirstOrDefault(
            binding => binding.Kind is ShaderBindingKind.Texture
                && string.Equals(binding.Name, textureName, StringComparison.Ordinal));

        return texture is null ? ComputeSampler() : SampledAt(kernel, texture).Sampler;
    }

    /// <summary>A texture of a slot's shape and kind, for a slot that must read nothing it was given.</summary>
    private IRhiTextureView StandInFor(ShaderBinding binding) =>
        (_standIns ??= new StandInTextures(_device, "compute.standin")).For(binding);

    private IRhiSampler ComputeSampler() =>
        _computeSampler ??= _device.CreateSampler(new RhiSamplerDescriptor(Label: "compute"));

    /// <summary>A two-deep volume for a 3D slot nothing is bound to; one deep would not be a volume.</summary>
    private IRhiTexture StandInVolume()
    {
        if (_standInVolume is { } made)
        {
            return made;
        }

        _standInVolume = _device.CreateTexture(new RhiTextureDescriptor(
            1,
            1,
            RhiTextureFormat.Rgba8Unorm,
            RhiTextureUsage.Sampled | RhiTextureUsage.CopyDestination,
            DepthOrArrayLayers: 2,
            Dimension: RhiTextureViewDimension.Texture3D,
            Label: "compute.volume"));

        _device.WriteTexture<byte>(
            new RhiTextureRegion(_standInVolume),
            new byte[8],
            4,
            new RhiExtent3D(1, 1, 2));

        return _standInVolume;
    }

    private TextureHandle CreateImage(
        int width,
        int height,
        int depth,
        VolumeFormat format,
        RhiTextureViewDimension dimension,
        string name,
        bool shaderWritten = true)
    {
        RhiTextureUsage usage = RhiTextureUsage.Sampled | RhiTextureUsage.CopySource | RhiTextureUsage.CopyDestination;

        IRhiTexture image = _device.CreateTexture(new RhiTextureDescriptor(
            width,
            height,
            FormatOf(format),
            shaderWritten ? usage | RhiTextureUsage.Storage : usage,
            DepthOrArrayLayers: depth,
            Dimension: dimension,
            Label: name));

        // Adopted, so it samples through the ordinary texture path as well as being written as storage.
        TextureHandle handle = _textures.Adopt(image, new TextureDescription(width, height));
        _storageTextures[handle.Id] = image;
        _requestedFormats[handle.Id] = FormatOf(format);
        return handle;
    }

    private static RhiTextureFormat FormatOf(VolumeFormat format) => format switch
    {
        VolumeFormat.R8 => RhiTextureFormat.R8Unorm,
        VolumeFormat.Rgba8 => RhiTextureFormat.Rgba8Unorm,
        VolumeFormat.Rgba16F => RhiTextureFormat.Rgba16Float,
        _ => throw new GraphicsDeviceException($"No texture format stands for {format}."),
    };

    /// <summary>Copies a whole image to the host.</summary>
    private void ReadImage(IRhiTexture image, Span<byte> destination)
    {
        int bytesPerTexel = image.Format.BytesPerTexel();
        int row = RhiBufferTextureCopy.AlignRow(image.Width * bytesPerTexel);
        int tight = image.Width * bytesPerTexel;
        int slices = image.DepthOrArrayLayers;

        if (destination.Length < tight * image.Height * slices)
        {
            throw new GraphicsDeviceException($"{image.Label} holds {tight * image.Height * slices} bytes; the destination is shorter.");
        }

        byte[] data = ReadBack(
            row * image.Height * slices,
            (encoder, staging) => encoder.CopyTextureToBuffer(
                new RhiTextureRegion(image),
                new RhiBufferTextureCopy(staging, 0, row, image.Height),
                new RhiExtent3D(image.Width, image.Height, slices)));

        // Rows come back padded to the copy alignment; the caller wants them tight.
        for (int line = 0; line < image.Height * slices; line++)
        {
            data.AsSpan(line * row, tight).CopyTo(destination[(line * tight)..]);
        }
    }

    /// <summary>Copies something into a staging buffer, waits for the copy, and reads it.</summary>
    /// <remarks>
    /// Outside a frame the copy goes on a submission of its own. Inside one, what it reads may have been
    /// written by the frame itself, so the copy is recorded after everything the frame has drawn and the
    /// frame is flushed with it; see <see cref="FlushFrame"/>.
    /// </remarks>
    private byte[] ReadBack(int bytes, Action<IRhiCommandEncoder, IRhiBuffer> copy)
    {
        using IRhiBuffer staging = _device.CreateBuffer(new RhiBufferDescriptor(
            (ulong)bytes,
            RhiBufferUsage.CopyDestination | RhiBufferUsage.MapRead,
            "compute.readback"));

        if (_inFrame)
        {
            FlushFrame(encoder => copy(encoder, staging));
        }
        else
        {
            using IRhiCommandEncoder encoder = _device.CreateCommandEncoder("compute.readback");
            copy(encoder, staging);
            _device.Submit([encoder.Finish("compute.readback")]);
        }

        using IRhiReadback readback = _device.ReadBuffer(staging);

        if (!readback.Wait(TimeSpan.FromSeconds(10)))
        {
            throw new GraphicsDeviceException("A compute result did not come back.");
        }

        return readback.GetData().ToArray();
    }

    private ComputeKernel Kernel(ComputeShaderHandle shader) =>
        shader.IsValid && _kernels.TryGetValue(shader.Id, out ComputeKernel? kernel)
            ? kernel
            : throw new GraphicsDeviceException($"{shader} names no compute shader on this device.");

    private IRhiBuffer StorageBuffer(StorageBufferHandle buffer) =>
        buffer.IsValid && _storageBuffers.TryGetValue(buffer.Id, out IRhiBuffer? resource)
            ? resource
            : throw new GraphicsDeviceException($"{buffer} names no storage buffer on this device.");

    private IRhiTexture StorageImage(TextureHandle texture) =>
        texture.IsValid && _storageTextures.TryGetValue(texture.Id, out IRhiTexture? image)
            ? image
            : throw new GraphicsDeviceException($"{texture} names no storage texture on this device.");

    /// <summary>Forgets a storage texture the table has just let go of.</summary>
    private void ForgetStorageTexture(TextureHandle texture)
    {
        _requestedFormats.Remove(texture.Id);

        if (_storageTextures.Remove(texture.Id, out IRhiTexture? image))
        {
            Unbind(_boundImages, image);
            _histories?.Forget(image);
        }
    }

    private void DisposeCompute()
    {
        foreach (ComputeKernel kernel in _kernels.Values)
        {
            kernel.Dispose();
        }

        foreach (IRhiBuffer buffer in _storageBuffers.Values)
        {
            buffer.Dispose();
        }

        _kernels.Clear();
        _storageBuffers.Clear();
        _storageTextures.Clear();
        _requestedFormats.Clear();
        _standInVolume?.Dispose();
        _computeSampler?.Dispose();
        _histories?.Dispose();
        _standIns?.Dispose();
    }

    /// <summary>One compute shader as the seam knows it: its pass, and the parameters set on it by name.</summary>
    private sealed class ComputeKernel : IDisposable
    {
        private readonly IRhiDevice _device;
        private readonly byte[] _values;
        private readonly Dictionary<string, ShaderUniformField> _fields = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _units = new(StringComparer.Ordinal);
        private readonly HashSet<string> _byUnit = new(StringComparer.Ordinal);
        private readonly List<IRhiBuffer>[] _pool;
        private readonly int[] _used;
        private readonly string _label;

        public ComputeKernel(IRhiDevice device, ShaderModuleId module, ShaderVariant variant, string label, int framesInFlight)
        {
            _device = device;
            _label = label;
            Pass = new ComputePass(device, module, label, variant);
            Reflection = Pass.Program.Reflection;

            ShaderUniformBlock? globals = Reflection.Globals;
            _values = new byte[globals?.Size ?? 0];

            foreach (ShaderUniformField field in globals?.Fields ?? [])
            {
                _fields[field.Name] = field;
            }

            // A sampled texture the parameter block also names is read through a unit: sharing the name
            // IS the declaration.
            foreach (ShaderBinding binding in Reflection.Bindings)
            {
                if (binding.Kind is ShaderBindingKind.Texture && _fields.ContainsKey(binding.Name))
                {
                    _byUnit.Add(binding.Name);
                }
            }

            // One more slot than there are frames in flight, for the dispatches a load submits and waits on.
            _pool = new List<IRhiBuffer>[framesInFlight + 1];
            _used = new int[framesInFlight + 1];

            for (int i = 0; i < _pool.Length; i++)
            {
                _pool[i] = [];
            }
        }

        /// <summary>Gets the pass the dispatches go through.</summary>
        public ComputePass Pass { get; }

        /// <summary>Gets what the module declares.</summary>
        public ShaderReflection Reflection { get; }

        /// <summary>Gets the name the game gave it.</summary>
        public string Label => _label;

        /// <summary>Sets one parameter's bytes.</summary>
        /// <remarks>
        /// A name the block does not have is ignored, as the original's API ignored it: the game sets the
        /// same parameters on every variant of a module, and a variant compiled without one has no field.
        /// </remarks>
        public void Set(string name, ReadOnlySpan<byte> bytes)
        {
            if (_fields.TryGetValue(name, out ShaderUniformField? field))
            {
                bytes[..Math.Min(bytes.Length, field.Size)].CopyTo(_values.AsSpan(field.Offset));
            }
        }

        /// <summary>Records which unit a texture read through a unit reads, if the name is one.</summary>
        /// <returns><see langword="true"/> when it was a unit and was recorded.</returns>
        public bool TrySetUnit(string name, int unit)
        {
            if (!_byUnit.Contains(name))
            {
                return false;
            }

            _units[name] = unit;
            return true;
        }

        /// <summary>The unit a sampled texture reads: the one set through its name, or its own slot.</summary>
        public int UnitOf(ShaderBinding texture) =>
            _byUnit.Contains(texture.Name)
                ? _units.GetValueOrDefault(texture.Name, 0)
                : ComputeBindingSpace.SlotOf(texture.Binding);

        /// <summary>This dispatch's snapshot of the parameters, in a buffer no other dispatch of the frame uses.</summary>
        public IRhiBuffer WriteGlobals(int slot)
        {
            List<IRhiBuffer> pool = _pool[slot];

            if (_used[slot] == pool.Count)
            {
                pool.Add(_device.CreateBuffer(new RhiBufferDescriptor(
                    (ulong)Math.Max(16, _values.Length),
                    RhiBufferUsage.Uniform | RhiBufferUsage.CopyDestination,
                    $"{_label}.globals")));
            }

            IRhiBuffer buffer = pool[_used[slot]++];
            _device.WriteBuffer<byte>(buffer, 0, _values);
            return buffer;
        }

        /// <summary>Frees a slot's buffers for reuse, once the frame that used them has finished.</summary>
        public void Reset(int slot) => _used[slot] = 0;

        /// <inheritdoc/>
        public void Dispose()
        {
            Pass.Dispose();

            foreach (List<IRhiBuffer> pool in _pool)
            {
                foreach (IRhiBuffer buffer in pool)
                {
                    buffer.Dispose();
                }
            }
        }
    }
}
