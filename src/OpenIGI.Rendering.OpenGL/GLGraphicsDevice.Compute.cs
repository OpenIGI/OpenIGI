using OpenIGI.Core.Graphics;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using Silk.NET.OpenGL;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>
/// The compute half of the GL backend: programs, shader-writable buffers, dispatches and barriers.
/// </summary>
/// <remarks>
/// <para>
/// Kept in its own file rather than added to an already-long one, and the split is along a real seam:
/// nothing here takes part in drawing a frame. These are the primitives the distance fields, the
/// surface cache and the gather are built out of, and every one of them needs GL 4.3.
/// </para>
/// <para>
/// <b>Everything here refuses rather than degrades.</b> A backend that silently did nothing when
/// compute was missing would leave a caller believing its volume had been filled, and the failure
/// would surface as a black picture several stages later. The capability is published on
/// <see cref="GraphicsCapabilities.SupportsCompute"/> so a caller can ask before it commits.
/// </para>
/// </remarks>
public sealed partial class GLGraphicsDevice
{
    private readonly Dictionary<uint, GLComputeProgram> _computePrograms = [];
    private readonly Dictionary<uint, GLStorageBuffer> _storageBuffers = [];
    private uint _boundComputeProgram;

    /// <inheritdoc/>
    public unsafe void ReadStorageTexture(TextureHandle texture, Span<Bgra32> destination)
    {
        ThrowIfDisposed();
        GLTexture entry = Resolve(texture, nameof(texture));

        int needed = entry.Width * entry.Height;

        if (destination.Length < needed)
        {
            throw new GraphicsDeviceException(
                $"{texture} is {entry.Width}x{entry.Height}; {needed} texels were needed, "
                + $"{destination.Length} offered.");
        }

        // Through a framebuffer rather than glGetTexImage, for the reason measured in L1: on this
        // driver glGetTexImage does not observe a compute shader's image stores at all. A read buffer
        // does.
        uint framebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        _gl.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
            entry.Name,
            0);

        _gl.MemoryBarrier(MemoryBarrierMask.AllBarrierBits);
        _gl.ReadBuffer(ReadBufferMode.ColorAttachment0);
        _gl.PixelStore(PixelStoreParameter.PackAlignment, 4);

        // Read as bytes even from a float target: GL converts and clamps, which is what a look at the
        // result wants. Recovering the values themselves would need a float span and a different call.
        fixed (Bgra32* target = destination)
        {
            _gl.ReadPixels(
                0, 0, (uint)entry.Width, (uint)entry.Height,
                PixelFormat.Bgra, PixelType.UnsignedByte, target);
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.DeleteFramebuffer(framebuffer);

        // GL reads bottom-up; every consumer of this expects top-down.
        for (int y = 0; y < entry.Height / 2; y++)
        {
            Span<Bgra32> top = destination.Slice(y * entry.Width, entry.Width);
            Span<Bgra32> bottom = destination.Slice((entry.Height - 1 - y) * entry.Width, entry.Width);

            for (int x = 0; x < entry.Width; x++)
            {
                (top[x], bottom[x]) = (bottom[x], top[x]);
            }
        }
    }

    /// <inheritdoc/>
    public TextureHandle CreateStorageTexture(
        int width, int height, VolumeFormat format, string name)
    {
        ThrowIfDisposed();
        RequireCompute();

        if (width <= 0 || height <= 0)
        {
            throw new GraphicsDeviceException(
                $"A storage texture must have positive dimensions; '{name}' asked for {width}x{height}.");
        }

        (InternalFormat internalFormat, _, _) = Describe(format);

        uint texture = _gl.GenTexture();
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, texture);

        // Immutable storage, for the reason recorded on CreateVolumeTexture: a mutable texture bound as
        // an image is accepted without complaint and the stores go nowhere.
        _gl.TexStorage2D(TextureTarget.Texture2D, 1, (SizedInternalFormat)internalFormat, (uint)width, (uint)height);

        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);

        _gl.BindTexture(TextureTarget.Texture2D, 0);
        InvalidateTextureUnit(0);

        uint id = _nextTextureId++;
        _textures[id] = new GLTexture(
            texture, width, height, 1, 0, 1, TextureTarget.Texture2D, internalFormat);

        _logger.Debug(LogCategory, $"Storage texture '{name}' allocated as {width}x{height} {format}.");
        return new TextureHandle(id);
    }

    /// <inheritdoc/>
    public unsafe TextureHandle CreateVolumeTexture(
        int width, int height, int depth, VolumeFormat format, string name)
    {
        ThrowIfDisposed();

        if (width <= 0 || height <= 0 || depth <= 0)
        {
            throw new GraphicsDeviceException(
                $"A volume must have positive dimensions; '{name}' asked for {width}x{height}x{depth}.");
        }

        (InternalFormat internalFormat, PixelFormat pixelFormat, PixelType pixelType) = Describe(format);

        uint texture = _gl.GenTexture();
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture3D, texture);

        // Immutable storage, not TexImage3D. A mutable texture can be bound as an image and the driver
        // will accept it without complaint, but the stores go nowhere -- which is exactly how this
        // presented: a dispatch that ran, reported no GL error, and left the volume at its allocated
        // contents. Immutable storage is the documented path for image load/store and costs nothing,
        // since these volumes are allocated once and never resized.
        _gl.TexStorage3D(
            TextureTarget.Texture3D,
            1,
            (SizedInternalFormat)internalFormat,
            (uint)width,
            (uint)height,
            (uint)depth);

        // Linear, because the hardware's trilinear filter IS the blend between eight neighbouring
        // voxels that a field lookup wants, and it costs nothing. Clamped on all three axes, because a
        // volume does not tile: the space past its edge is more of the same emptiness rather than the
        // far side of the level, and wrapping would let a trace leaving one side of a clipmap read
        // geometry from the other.
        _gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapR, (int)GLEnum.ClampToEdge);

        _gl.BindTexture(TextureTarget.Texture3D, 0);
        InvalidateTextureUnit(0);

        // The handle id comes from the SAME counter 2D textures use, not from the GL name. The two are
        // different spaces -- CreateTexture hands out a running id and stores the GL name inside the
        // entry -- so keying a volume by its GL name puts it at an id the counter will later hand to a
        // 2D texture, which then overwrites the volume's entry in the shared table.
        //
        // The symptom was a volume that existed, uploaded correctly, and then reported itself as a
        // 128x128 2D texture the moment a level finished loading its art.
        uint id = _nextTextureId++;

        _textures[id] = new GLTexture(
            texture, width, height, 1, 0, depth, TextureTarget.Texture3D, internalFormat);

        _logger.Debug(
            LogCategory,
            $"Volume '{name}' allocated as {width}x{height}x{depth} {format}.");

        return new TextureHandle(id);
    }

    /// <inheritdoc/>
    public unsafe void UpdateVolumeTexture<T>(TextureHandle texture, ReadOnlySpan<T> data)
        where T : unmanaged
    {
        ThrowIfDisposed();
        GLTexture entry = Resolve(texture, nameof(texture));

        if (entry.Target != TextureTarget.Texture3D)
        {
            throw new GraphicsDeviceException($"{texture} is not a volume; it is a {entry.Target} of {entry.Width}x{entry.Height}x{entry.Depth}.");
        }

        (_, PixelFormat pixelFormat, PixelType pixelType) = Describe(entry.Format);
        int expected = entry.Width * entry.Height * entry.Depth * BytesPerVoxel(entry.Format);
        int offered = data.Length * sizeof(T);

        if (offered != expected)
        {
            throw new GraphicsDeviceException(
                $"{texture} holds {expected} bytes; {offered} were offered.");
        }

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture3D, entry.Name);

        // Rows are byte-tight. The default is four, so a single-channel volume whose width is not a
        // multiple of four is read with padding that is not there -- every row after the first slides,
        // and the volume shears diagonally.
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);

        fixed (T* source = data)
        {
            _gl.TexSubImage3D(
                TextureTarget.Texture3D,
                0,
                0,
                0,
                0,
                (uint)entry.Width,
                (uint)entry.Height,
                (uint)entry.Depth,
                pixelFormat,
                pixelType,
                source);
        }

        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
        _gl.BindTexture(TextureTarget.Texture3D, 0);
        InvalidateTextureUnit(0);
    }

    /// <inheritdoc/>
    public unsafe void ReadVolumeTexture<T>(TextureHandle texture, Span<T> destination)
        where T : unmanaged
    {
        ThrowIfDisposed();
        GLTexture entry = Resolve(texture, nameof(texture));

        if (entry.Target != TextureTarget.Texture3D)
        {
            throw new GraphicsDeviceException($"{texture} is not a volume; it is a {entry.Target} of {entry.Width}x{entry.Height}x{entry.Depth}.");
        }

        (_, PixelFormat pixelFormat, PixelType pixelType) = Describe(entry.Format);
        int expected = entry.Width * entry.Height * entry.Depth * BytesPerVoxel(entry.Format);
        int offered = destination.Length * sizeof(T);

        if (offered < expected)
        {
            throw new GraphicsDeviceException(
                $"{texture} holds {expected} bytes; only {offered} were offered to receive it.");
        }

        // Everything, because this is a diagnostic path and being sure costs nothing here.
        //
        // KNOWN LIMITATION, measured rather than assumed: on the driver this was developed against,
        // glGetTexImage does NOT observe a compute shader's imageStore writes AT ALL -- not with
        // TEXTURE_UPDATE, not with ALL_BARRIER_BITS, not after glFinish, and with no GL error raised.
        // A volume uploaded through UpdateVolumeTexture reads back perfectly through this same path, so
        // what fails is specifically image-store visibility.
        //
        // That was established by having the shader imageLoad its own store back: it returned exactly
        // the value written while this returned zeros. So a zero here means "cannot see it", not
        // "nothing was written", and anything that needs to VERIFY a compute-written volume must read
        // it with a compute shader into a storage buffer instead.
        _gl.MemoryBarrier(MemoryBarrierMask.AllBarrierBits);

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture3D, entry.Name);

        // Byte-tight, for the same reason the upload is: the default of four would have GL write each
        // row padded, and a single-channel volume whose width is not a multiple of four would come back
        // sheared.
        _gl.PixelStore(PixelStoreParameter.PackAlignment, 1);

        while (_gl.GetError() != GLEnum.NoError)
        {
        }

        fixed (T* target = destination)
        {
            _gl.GetTexImage(TextureTarget.Texture3D, 0, pixelFormat, pixelType, target);
        }

        if (_gl.GetError() is var readError && readError != GLEnum.NoError)
        {
            _logger.Warning(LogCategory, $"Reading {texture} back was rejected: {readError}.");
        }

        _gl.PixelStore(PixelStoreParameter.PackAlignment, 4);
        _gl.BindTexture(TextureTarget.Texture3D, 0);
        InvalidateTextureUnit(0);
    }

    /// <inheritdoc/>
    public void SetVolumeTexture(int unit, TextureHandle texture)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(unit);

        uint name = 0;

        if (texture.IsValid)
        {
            GLTexture entry = Resolve(texture, nameof(texture));

            if (entry.Target != TextureTarget.Texture3D)
            {
                throw new GraphicsDeviceException($"{texture} is not a volume; it is a {entry.Target} of {entry.Width}x{entry.Height}x{entry.Depth}.");
            }

            name = entry.Name;
        }

        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        _gl.BindTexture(TextureTarget.Texture3D, name);
        InvalidateTextureUnit(unit);
    }

    /// <summary>Forgets what the 2D binding cache believes is on a unit.</summary>
    /// <remarks>
    /// The drawing path skips a bind when the unit already holds the texture it wants. Binding a volume
    /// to that unit changes what is there without going through that cache, so the next 2D bind would
    /// skip itself and the draw would sample whatever the volume left behind. Clearing the entry costs
    /// one redundant bind and removes a class of fault that only appears once a compute pass and a draw
    /// pass happen to want the same unit.
    /// </remarks>
    private void InvalidateTextureUnit(int unit)
    {
        if (unit >= 0 && unit < _textureNames.Length)
        {
            _textureNames[unit] = 0;
        }

        _activeTextureUnit = -1;
    }

    /// <summary>The GL formats one voxel description maps to.</summary>
    private static (InternalFormat Internal, PixelFormat Pixel, PixelType Type) Describe(VolumeFormat format) =>
        format switch
        {
            VolumeFormat.R8 => (InternalFormat.R8, PixelFormat.Red, PixelType.UnsignedByte),
            VolumeFormat.Rgba16F => (InternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.HalfFloat),
            _ => (InternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte),
        };

    /// <summary>The GL formats one internal format maps back to, for an upload.</summary>
    private static (InternalFormat Internal, PixelFormat Pixel, PixelType Type) Describe(InternalFormat format) =>
        format switch
        {
            InternalFormat.R8 => (InternalFormat.R8, PixelFormat.Red, PixelType.UnsignedByte),
            InternalFormat.Rgba16f => (InternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.HalfFloat),
            _ => (InternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte),
        };

    private static int BytesPerVoxel(InternalFormat format) =>
        format switch
        {
            InternalFormat.R8 => 1,
            InternalFormat.Rgba16f => 8,
            _ => 4,
        };

    /// <inheritdoc/>
    public ComputeShaderHandle CreateComputeShader(string source, string name)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        RequireCompute();

        uint shader = _gl.CreateShader(ShaderType.ComputeShader);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compiled);

        if (compiled == 0)
        {
            string log = _gl.GetShaderInfoLog(shader);
            _gl.DeleteShader(shader);
            throw new GraphicsDeviceException($"The compute shader '{name}' did not compile: {log}");
        }

        uint program = _gl.CreateProgram();
        _gl.AttachShader(program, shader);
        _gl.LinkProgram(program);
        _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linked);

        // Detached and deleted whether or not the link succeeded: the program holds its own copy once
        // linked, and leaving the shader object attached leaks it for the life of the program.
        _gl.DetachShader(program, shader);
        _gl.DeleteShader(shader);

        if (linked == 0)
        {
            string log = _gl.GetProgramInfoLog(program);
            _gl.DeleteProgram(program);
            throw new GraphicsDeviceException($"The compute shader '{name}' did not link: {log}");
        }

        _computePrograms[program] = new GLComputeProgram(program, name, []);
        _logger.Debug(LogCategory, $"Compute program '{name}' linked as {program}.");
        return new ComputeShaderHandle(program);
    }

    /// <inheritdoc/>
    public void DestroyComputeShader(ComputeShaderHandle shader)
    {
        ThrowIfDisposed();

        if (!shader.IsValid || !_computePrograms.Remove(shader.Id))
        {
            return;
        }

        if (_boundComputeProgram == shader.Id)
        {
            _boundComputeProgram = 0;
        }

        _gl.DeleteProgram(shader.Id);
    }

    /// <inheritdoc/>
    public StorageBufferHandle CreateStorageBuffer(int sizeInBytes, string name)
    {
        ThrowIfDisposed();
        RequireCompute();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeInBytes);

        uint buffer = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, buffer);

        // Zero-filled, and that is part of the contract rather than a courtesy. Almost every buffer
        // here is an append list with its count at the front, and a count that starts as whatever the
        // driver last had in that memory is a pass that reads garbage on its first frame -- once, at
        // load, which is exactly the kind of fault that reproduces on one machine in ten.
        unsafe
        {
            _gl.BufferData(
                BufferTargetARB.ShaderStorageBuffer, (nuint)sizeInBytes, null, BufferUsageARB.DynamicDraw);

            void* mapped = _gl.MapBuffer(BufferTargetARB.ShaderStorageBuffer, BufferAccessARB.WriteOnly);

            if (mapped is not null)
            {
                new Span<byte>(mapped, sizeInBytes).Clear();
                _gl.UnmapBuffer(BufferTargetARB.ShaderStorageBuffer);
            }
        }

        _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, 0);

        _storageBuffers[buffer] = new GLStorageBuffer(buffer, sizeInBytes, name);
        return new StorageBufferHandle(buffer);
    }

    /// <inheritdoc/>
    public unsafe void UpdateStorageBuffer<T>(StorageBufferHandle buffer, ReadOnlySpan<T> data)
        where T : unmanaged
    {
        ThrowIfDisposed();
        GLStorageBuffer entry = ResolveStorage(buffer);

        int bytes = data.Length * sizeof(T);

        if (bytes > entry.SizeInBytes)
        {
            throw new GraphicsDeviceException(
                $"'{entry.Name}' holds {entry.SizeInBytes} bytes; {bytes} were offered.");
        }

        if (bytes == 0)
        {
            return;
        }

        _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, entry.Name32);

        fixed (T* source = data)
        {
            _gl.BufferSubData(BufferTargetARB.ShaderStorageBuffer, 0, (nuint)bytes, source);
        }

        _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, 0);
    }

    /// <inheritdoc/>
    public unsafe void ReadStorageBuffer<T>(StorageBufferHandle buffer, Span<T> destination)
        where T : unmanaged
    {
        ThrowIfDisposed();
        GLStorageBuffer entry = ResolveStorage(buffer);

        int bytes = Math.Min(destination.Length * sizeof(T), entry.SizeInBytes);

        if (bytes == 0)
        {
            return;
        }

        // Synchronising by construction: this waits for every write the GPU has outstanding on the
        // buffer. That is why the seam's documentation confines it to load-time work and diagnostics.
        _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, entry.Name32);

        fixed (T* target = destination)
        {
            _gl.GetBufferSubData(BufferTargetARB.ShaderStorageBuffer, 0, (nuint)bytes, target);
        }

        _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, 0);
    }

    /// <inheritdoc/>
    public void DestroyStorageBuffer(StorageBufferHandle buffer)
    {
        ThrowIfDisposed();

        if (buffer.IsValid && _storageBuffers.Remove(buffer.Id))
        {
            _gl.DeleteBuffer(buffer.Id);
        }
    }

    /// <inheritdoc/>
    public void BindStorageBuffer(int slot, StorageBufferHandle buffer)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(slot);

        uint name = buffer.IsValid ? ResolveStorage(buffer).Name32 : 0;
        _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, (uint)slot, name);
    }

    /// <inheritdoc/>
    public void BindImage(int slot, TextureHandle texture, ImageAccess access, int level = 0)
    {
        ThrowIfDisposed();
        RequireCompute();
        ArgumentOutOfRangeException.ThrowIfNegative(slot);

        if (!texture.IsValid)
        {
            _gl.BindImageTexture(
                (uint)slot, 0, 0, false, 0, BufferAccessARB.ReadWrite, InternalFormat.Rgba8);
            return;
        }

        GLTexture entry = Resolve(texture, nameof(texture));

        BufferAccessARB gl = access switch
        {
            ImageAccess.Read => BufferAccessARB.ReadOnly,
            ImageAccess.Write => BufferAccessARB.WriteOnly,
            _ => BufferAccessARB.ReadWrite,
        };

        // Layered ONLY for a texture that has layers -- a 3D volume or an array -- so the whole thing is
        // bound at once rather than one slice per bind. A plain 2D texture is NOT layered, and binding it
        // with layered=true declares to the driver a layered image the shader then reads as a non-layered
        // image2D: a mismatch that is undefined per the spec even where a driver tolerates it. A 2D image
        // binds non-layered, layer 0.
        bool layered = entry.Target is TextureTarget.Texture3D or TextureTarget.Texture2DArray;

        // The format is the texture's OWN, not a constant. An image binding reinterprets the storage
        // through whatever format it is given, and GL does not report a mismatch: declaring an R8
        // volume as RGBA8 reads a quarter of it as four channels of noise, and the pass looks like it
        // ran while producing nothing usable.
        while (_gl.GetError() != GLEnum.NoError)
        {
        }

        _gl.BindImageTexture((uint)slot, entry.Name, level, layered, 0, gl, entry.Format);

        if (_gl.GetError() is var bindError && bindError != GLEnum.NoError)
        {
            _logger.Warning(
                LogCategory,
                $"Image binding of {texture} ({entry.Format}) to unit {slot} was rejected: {bindError}.");
        }
    }

    /// <inheritdoc/>
    public void Dispatch(ComputeShaderHandle shader, int groupsX, int groupsY = 1, int groupsZ = 1)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(groupsX);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(groupsY);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(groupsZ);

        UseCompute(shader);

        // Cleared first, so what is read afterwards belongs to this dispatch rather than to whatever
        // happened earlier in the frame.
        while (_gl.GetError() != GLEnum.NoError)
        {
        }

        _gl.DispatchCompute((uint)groupsX, (uint)groupsY, (uint)groupsZ);

        // GL reports a rejected dispatch here and nowhere else. Without this the pass simply does
        // nothing, the volume keeps whatever it was allocated with, and the fault surfaces as lighting
        // that is wrong several stages later.
        if (_gl.GetError() is var error && error != GLEnum.NoError)
        {
            _logger.Warning(
                LogCategory,
                $"A compute dispatch of {groupsX}x{groupsY}x{groupsZ} groups was rejected: {error}.");
        }
    }

    /// <inheritdoc/>
    public void DispatchIndirect(
        ComputeShaderHandle shader, StorageBufferHandle arguments, int offsetInBytes = 0)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(offsetInBytes);

        GLStorageBuffer entry = ResolveStorage(arguments);
        UseCompute(shader);

        // The same buffer, bound a second time through the indirect-parameter target. A storage buffer
        // and a dispatch-argument buffer are the same object to GL; only the binding point differs,
        // which is what lets a culling pass write the counts its successor is launched with.
        _gl.BindBuffer(BufferTargetARB.DispatchIndirectBuffer, entry.Name32);
        _gl.DispatchComputeIndirect(offsetInBytes);
        _gl.BindBuffer(BufferTargetARB.DispatchIndirectBuffer, 0);
    }

    /// <inheritdoc/>
    public void MemoryBarrier(MemoryBarrierScope scope)
    {
        ThrowIfDisposed();

        if (scope == MemoryBarrierScope.None)
        {
            return;
        }

        MemoryBarrierMask mask = 0;

        if (scope.HasFlag(MemoryBarrierScope.StorageBuffer))
        {
            mask |= MemoryBarrierMask.ShaderStorageBarrierBit;
        }

        if (scope.HasFlag(MemoryBarrierScope.Image))
        {
            mask |= MemoryBarrierMask.ShaderImageAccessBarrierBit;
        }

        if (scope.HasFlag(MemoryBarrierScope.IndirectArguments))
        {
            mask |= MemoryBarrierMask.CommandBarrierBit;
        }

        if (scope.HasFlag(MemoryBarrierScope.TextureFetch))
        {
            mask |= MemoryBarrierMask.TextureFetchBarrierBit;
        }

        if (scope.HasFlag(MemoryBarrierScope.TextureUpdate))
        {
            mask |= MemoryBarrierMask.TextureUpdateBarrierBit;
        }

        _gl.MemoryBarrier(mask);
    }

    /// <inheritdoc/>
    public void SetComputeUniform(ComputeShaderHandle shader, string name, float value)
    {
        UseCompute(shader);
        _gl.Uniform1(LocationOf(shader, name), value);
    }

    /// <inheritdoc/>
    public void SetComputeUniform(ComputeShaderHandle shader, string name, int value)
    {
        UseCompute(shader);
        _gl.Uniform1(LocationOf(shader, name), value);
    }

    /// <inheritdoc/>
    public void SetComputeUniform(ComputeShaderHandle shader, string name, in Vector3f value)
    {
        UseCompute(shader);
        _gl.Uniform3(LocationOf(shader, name), value.X, value.Y, value.Z);
    }

    /// <inheritdoc/>
    public void SetComputeUniform(
        ComputeShaderHandle shader, string name, float x, float y, float z, float w)
    {
        UseCompute(shader);
        _gl.Uniform4(LocationOf(shader, name), x, y, z, w);
    }

    /// <summary>Releases every compute program and buffer this device owns.</summary>
    private void DisposeComputeResources()
    {
        foreach (uint program in _computePrograms.Keys)
        {
            _gl.DeleteProgram(program);
        }

        _computePrograms.Clear();

        foreach (uint buffer in _storageBuffers.Keys)
        {
            _gl.DeleteBuffer(buffer);
        }

        _storageBuffers.Clear();
        _boundComputeProgram = 0;
    }

    /// <summary>Looks a uniform up, remembering it so the driver is asked once per name.</summary>
    /// <remarks>
    /// A uniform location never changes for the life of a program, and <c>glGetUniformLocation</c> is a
    /// string lookup the driver performs every time it is asked. These are set per dispatch, so caching
    /// turns a per-frame string comparison into a dictionary hit.
    /// </remarks>
    private int LocationOf(ComputeShaderHandle shader, string name)
    {
        GLComputeProgram program = ResolveCompute(shader);

        if (program.Locations.TryGetValue(name, out int cached))
        {
            return cached;
        }

        int location = _gl.GetUniformLocation(program.Name32, name);
        program.Locations[name] = location;

        if (location < 0)
        {
            // A warning rather than a throw: a uniform the compiler removed because nothing read it is
            // a normal thing to find while a pass is being written, and GL ignores writes to -1.
            _logger.Debug(LogCategory, $"Compute program '{program.Label}' has no uniform '{name}'.");
        }

        return location;
    }

    private void UseCompute(ComputeShaderHandle shader)
    {
        GLComputeProgram program = ResolveCompute(shader);

        if (_boundComputeProgram == program.Name32)
        {
            return;
        }

        _boundComputeProgram = program.Name32;
        _gl.UseProgram(program.Name32);

        // The drawing path caches which program is bound, and this has just invalidated that cache.
        // Leaving it primed would let the next mesh draw skip its own UseProgram and rasterise with a
        // compute program bound -- which GL rejects, per draw, for the rest of the frame.
        _currentProgram = 0;
    }

    private GLComputeProgram ResolveCompute(ComputeShaderHandle shader)
    {
        if (!shader.IsValid || !_computePrograms.TryGetValue(shader.Id, out GLComputeProgram? program))
        {
            throw new GraphicsDeviceException($"{shader} does not belong to this device.");
        }

        return program;
    }

    private GLStorageBuffer ResolveStorage(StorageBufferHandle buffer)
    {
        if (!buffer.IsValid || !_storageBuffers.TryGetValue(buffer.Id, out GLStorageBuffer entry))
        {
            throw new GraphicsDeviceException($"{buffer} does not belong to this device.");
        }

        return entry;
    }

    private void RequireCompute()
    {
        if (!Features.SupportsCompute)
        {
            throw new GraphicsDeviceException(
                $"Compute needs OpenGL 4.3; this context is {Features}. "
                + "Check GraphicsCapabilities.SupportsCompute before asking for it.");
        }
    }

    /// <summary>A linked compute program and the uniform locations resolved from it so far.</summary>
    private sealed record GLComputeProgram(uint Name32, string Label, Dictionary<string, int> Locations);

    /// <summary>A shader-writable buffer and how large it is.</summary>
    private readonly record struct GLStorageBuffer(uint Name32, int SizeInBytes, string Name);
}
