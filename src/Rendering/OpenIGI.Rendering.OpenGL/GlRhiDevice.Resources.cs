using System.Buffers;
using System.Runtime.InteropServices;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;
using Silk.NET.OpenGL;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>Making resources and moving host data into and out of them.</summary>
public sealed partial class GlRhiDevice
{
    /// <inheritdoc/>
    public IRhiBuffer CreateBuffer(in RhiBufferDescriptor descriptor)
    {
        ThrowIfDisposed();
        return new GlBuffer(this, descriptor);
    }

    /// <inheritdoc/>
    public IRhiTexture CreateTexture(in RhiTextureDescriptor descriptor)
    {
        ThrowIfDisposed();
        return new GlTexture(this, descriptor);
    }

    /// <inheritdoc/>
    public IRhiSampler CreateSampler(in RhiSamplerDescriptor descriptor)
    {
        ThrowIfDisposed();
        return new GlSampler(this, descriptor);
    }

    /// <inheritdoc/>
    public IRhiShaderModule CreateShaderModule(in RhiShaderModuleDescriptor descriptor)
    {
        ThrowIfDisposed();

        ShaderVariant variant = descriptor.Variant;
        if (!ShaderCatalog.Compiles(descriptor.Module, variant, ShaderTarget.Glsl))
        {
            throw new InvalidOperationException(
                $"The shader module {descriptor.Module} ({variant.Name}) has no GLSL form. A pass that needs it asks "
                + "ShaderCatalog.Compiles for the device's target first and stands down where it has none.");
        }

        return new GlShaderModule(
            this,
            descriptor.Module,
            variant,
            ShaderCatalog.GetReflection(descriptor.Module, variant),
            descriptor.Label);
    }

    /// <inheritdoc/>
    public IRhiBindGroupLayout CreateBindGroupLayout(RhiBindGroupLayoutDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ThrowIfDisposed();

        if (descriptor.Entries.Any(e => e.Type == RhiBindingType.AccelerationStructure))
        {
            throw new InvalidOperationException(
                "OpenGL has no acceleration structures. A pass that wants one asks IRhiDevice.AccelerationStructures "
                + "first and stands down when it is not there.");
        }

        return new GlBindGroupLayout(this, descriptor);
    }

    /// <inheritdoc/>
    public IRhiBindGroup CreateBindGroup(RhiBindGroupDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ThrowIfDisposed();
        return new GlBindGroup(this, descriptor);
    }

    /// <inheritdoc/>
    public IRhiPipelineLayout CreatePipelineLayout(RhiPipelineLayoutDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ThrowIfDisposed();

        if (descriptor.PushDataBytes > Limits.MaxPushDataBytes)
        {
            throw new InvalidOperationException(
                $"The layout '{descriptor.Label}' wants {descriptor.PushDataBytes} bytes of push data and this backend "
                + $"offers {Limits.MaxPushDataBytes}.");
        }

        return new GlPipelineLayout(this, descriptor);
    }

    /// <inheritdoc/>
    public IRhiRenderPipeline CreateRenderPipeline(RhiRenderPipelineDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ThrowIfDisposed();
        return new GlRenderPipeline(this, descriptor);
    }

    /// <inheritdoc/>
    public IRhiComputePipeline CreateComputePipeline(RhiComputePipelineDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ThrowIfDisposed();
        return new GlComputePipeline(this, descriptor);
    }

    /// <inheritdoc/>
    public IRhiQuerySet CreateQuerySet(in RhiQuerySetDescriptor descriptor)
    {
        ThrowIfDisposed();
        return new GlQuerySet(this, descriptor);
    }

    /// <inheritdoc/>
    public IRhiCommandEncoder CreateCommandEncoder(string label = "")
    {
        ThrowIfDisposed();
        return new GlCommandEncoder(this, label);
    }

    /// <inheritdoc/>
    public void WriteBuffer(IRhiBuffer buffer, ulong offset, ReadOnlySpan<byte> data)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ThrowIfDisposed();

        if (data.IsEmpty)
        {
            return;
        }

        GlBuffer target = (GlBuffer)buffer;
        if (Thread.IsOwner)
        {
            Thread.Drain();
            Upload(target, offset, data);
            return;
        }

        // Held for the OpenGL thread, so the bytes are copied now: the span is the caller's only until this returns.
        int length = data.Length;
        byte[] copy = ArrayPool<byte>.Shared.Rent(length);
        data.CopyTo(copy);
        Thread.Run(() =>
        {
            try
            {
                Upload(target, offset, copy.AsSpan(0, length));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(copy);
            }
        });
    }

    /// <inheritdoc/>
    public void WriteTexture<T>(in RhiTextureRegion destination, ReadOnlySpan<T> data, int bytesPerRow, in RhiExtent3D size)
        where T : unmanaged
    {
        ThrowIfDisposed();

        GlTexture texture = (GlTexture)destination.Texture;
        RhiTextureRegion region = destination;
        RhiExtent3D extent = size;
        int rows = size.Height;

        if (Thread.IsOwner)
        {
            Thread.Drain();
            Upload(texture, region, MemoryMarshal.AsBytes(data), bytesPerRow, rows, extent);
            return;
        }

        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(data);
        int length = bytes.Length;
        byte[] copy = ArrayPool<byte>.Shared.Rent(length);
        bytes.CopyTo(copy);
        Thread.Run(() =>
        {
            try
            {
                Upload(texture, region, copy.AsSpan(0, length), bytesPerRow, rows, extent);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(copy);
            }
        });
    }

    /// <inheritdoc/>
    public IRhiReadback ReadBuffer(IRhiBuffer buffer, ulong offset = 0, ulong sizeInBytes = 0)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ThrowIfDisposed();

        ulong size = sizeInBytes > 0 ? sizeInBytes : buffer.SizeInBytes - offset;
        return new GlReadback(this, (GlBuffer)buffer, offset, size);
    }

    private unsafe void Upload(GlBuffer buffer, ulong offset, ReadOnlySpan<byte> data)
    {
        Gl.BindBuffer(GLEnum.CopyWriteBuffer, buffer.Name);
        fixed (byte* bytes = data)
        {
            Gl.BufferSubData(GLEnum.CopyWriteBuffer, (nint)offset, (nuint)data.Length, bytes);
        }
    }

    private unsafe void Upload(GlTexture texture, in RhiTextureRegion region, ReadOnlySpan<byte> data, int bytesPerRow, int rowsPerImage, in RhiExtent3D size)
    {
        BindForEdit(texture.Target, texture.Name);
        fixed (byte* bytes = data)
        {
            GlUpload.Upload(Gl, texture, region, bytesPerRow, rowsPerImage, size, bytes);
        }
    }
}
