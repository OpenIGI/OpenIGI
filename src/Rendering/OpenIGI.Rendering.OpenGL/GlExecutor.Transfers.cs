using OpenIGI.Rendering.Rhi;
using Silk.NET.OpenGL;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>Copies, clears and queries.</summary>
internal sealed partial class GlExecutor
{
    private const int QueryResultBytes = sizeof(ulong);

    private void ExecuteTransfer(in GlCommand c)
    {
        Barrier();

        switch (c.Op)
        {
            case GlOp.CopyBufferToBuffer:
                _gl.BindBuffer(GLEnum.CopyReadBuffer, ((GlBuffer)c.A!).Name);
                _gl.BindBuffer(GLEnum.CopyWriteBuffer, ((GlBuffer)c.B!).Name);
                _gl.CopyBufferSubData(GLEnum.CopyReadBuffer, GLEnum.CopyWriteBuffer, (nint)c.I0, (nint)c.I1, (nuint)c.I2);
                break;
            case GlOp.CopyBufferToTexture:
                CopyBufferToTexture((GlTextureCopy)c.A!);
                break;
            case GlOp.CopyTextureToBuffer:
                CopyTextureToBuffer((GlTextureCopy)c.A!);
                break;
            case GlOp.CopyTextureToTexture:
                CopyTextureToTexture((GlTextureCopy)c.A!);
                break;
            case GlOp.ClearBuffer:
                ClearBuffer((GlBuffer)c.A!, c.I0, c.I1);
                break;
            case GlOp.ResolveQuerySet:
                ResolveQuerySet((GlQuerySet)c.A!, (int)c.I0, (int)c.I1, (GlBuffer)c.B!, c.I2);
                break;
            default:
                throw new InvalidOperationException($"{c.Op} is not a command the OpenGL executor knows.");
        }
    }

    private unsafe void CopyBufferToTexture(GlTextureCopy copy)
    {
        RhiBufferTextureCopy source = copy.Buffer!.Value;
        RhiTextureRegion destination = copy.Destination!.Value;
        GlTexture texture = (GlTexture)destination.Texture;

        _gl.BindBuffer(GLEnum.PixelUnpackBuffer, ((GlBuffer)source.Buffer).Name);
        device.BindForEdit(texture.Target, texture.Name);
        GlUpload.Upload(_gl, texture, destination, source.BytesPerRow, source.RowsPerImage, copy.Size, (void*)source.Offset);
        _gl.BindBuffer(GLEnum.PixelUnpackBuffer, 0);
    }

    private unsafe void CopyTextureToBuffer(GlTextureCopy copy)
    {
        RhiTextureRegion source = copy.Source!.Value;
        RhiBufferTextureCopy destination = copy.Buffer!.Value;
        GlTexture texture = (GlTexture)source.Texture;
        GlFormat format = texture.Gl;
        int texel = texture.Format.BytesPerTexel();
        GlBuffer buffer = (GlBuffer)destination.Buffer;

        _gl.BindBuffer(GLEnum.PixelPackBuffer, buffer.Name);
        _gl.PixelStore(GLEnum.PackAlignment, 1);
        _gl.PixelStore(GLEnum.PackRowLength, texel > 0 ? destination.BytesPerRow / texel : 0);

        int rowsPerImage = destination.RowsPerImage > 0 ? destination.RowsPerImage : copy.Size.Height;
        _gl.PixelStore(GLEnum.PackImageHeight, rowsPerImage);

        if (device.Caps.TextureSubImage)
        {
            // Straight from the texture into the buffer: no framebuffer, so nothing for the driver to wait on. A read
            // made through a framebuffer made and deleted for it cost three milliseconds a frame for eight texels of sky.
            bool layered = texture.Target is not (GLEnum.Texture2D or GLEnum.Texture2DMultisample);
            _gl.GetTextureSubImage(
                texture.Name,
                source.MipLevel,
                source.Origin.X,
                source.Origin.Y,
                layered ? source.Origin.Z : 0,
                (uint)copy.Size.Width,
                (uint)copy.Size.Height,
                (uint)(layered ? copy.Size.DepthOrArrayLayers : 1),
                format.Format,
                format.Type,
                (uint)(buffer.SizeInBytes - destination.Offset),
                (void*)(nint)destination.Offset);
        }
        else
        {
            ReadThroughFramebuffer(copy, texture, format, rowsPerImage);
        }

        _gl.PixelStore(GLEnum.PackImageHeight, 0);
        _gl.PixelStore(GLEnum.PackRowLength, 0);
        _gl.BindBuffer(GLEnum.PixelPackBuffer, 0);
    }

    /// <summary>Reads a texture a layer at a time through the executor's own read framebuffer, as OpenGL 4.3 has to.</summary>
    /// <remarks>The framebuffer is made once and kept: one made and deleted per read stalls the driver on every read.</remarks>
    private unsafe void ReadThroughFramebuffer(GlTextureCopy copy, GlTexture texture, GlFormat format, int rowsPerImage)
    {
        RhiTextureRegion source = copy.Source!.Value;
        RhiBufferTextureCopy destination = copy.Buffer!.Value;

        _gl.Disable(EnableCap.ScissorTest);
        if (_readFramebuffer == 0)
        {
            _readFramebuffer = _gl.GenFramebuffer();
        }

        _gl.BindFramebuffer(GLEnum.ReadFramebuffer, _readFramebuffer);
        GLEnum attachment = texture.Format.IsDepth()
            ? texture.Format.HasStencil() ? GLEnum.DepthStencilAttachment : GLEnum.DepthAttachment
            : GLEnum.ColorAttachment0;

        for (int z = 0; z < copy.Size.DepthOrArrayLayers; z++)
        {
            int layer = source.Origin.Z + z;
            if (texture.Target is GLEnum.Texture2D or GLEnum.Texture2DMultisample)
            {
                _gl.FramebufferTexture(GLEnum.ReadFramebuffer, attachment, texture.Name, source.MipLevel);
            }
            else
            {
                _gl.FramebufferTextureLayer(GLEnum.ReadFramebuffer, attachment, texture.Name, source.MipLevel, layer);
            }

            _gl.ReadBuffer(texture.Format.IsDepth() ? GLEnum.None : GLEnum.ColorAttachment0);

            long offset = (long)destination.Offset + ((long)z * rowsPerImage * destination.BytesPerRow);
            _gl.ReadPixels(
                source.Origin.X,
                source.Origin.Y,
                (uint)copy.Size.Width,
                (uint)copy.Size.Height,
                format.Format,
                format.Type,
                (void*)offset);
        }

        // Left holding nothing, so a texture it read can be deleted without the framebuffer keeping it referenced.
        _gl.FramebufferTexture(GLEnum.ReadFramebuffer, attachment, 0, 0);
        _gl.BindFramebuffer(GLEnum.ReadFramebuffer, 0);
    }

    // The framebuffer reads go through where a texture cannot be read into a buffer directly; made on first use.
    private uint _readFramebuffer;

    private void CopyTextureToTexture(GlTextureCopy copy)
    {
        RhiTextureRegion source = copy.Source!.Value;
        RhiTextureRegion destination = copy.Destination!.Value;
        GlTexture from = (GlTexture)source.Texture;
        GlTexture to = (GlTexture)destination.Texture;

        _gl.CopyImageSubData(
            from.Name,
            from.Target,
            source.MipLevel,
            source.Origin.X,
            source.Origin.Y,
            source.Origin.Z,
            to.Name,
            to.Target,
            destination.MipLevel,
            destination.Origin.X,
            destination.Origin.Y,
            destination.Origin.Z,
            (uint)copy.Size.Width,
            (uint)copy.Size.Height,
            (uint)copy.Size.DepthOrArrayLayers);
    }

    private unsafe void ClearBuffer(GlBuffer buffer, long offset, long size)
    {
        if (size <= 0)
        {
            return;
        }

        _gl.BindBuffer(GLEnum.CopyWriteBuffer, buffer.Name);

        // A null pointer is a fill of zeroes. The internal format only says how the (absent) pattern is read, so
        // any four-byte one serves; the range has to be a multiple of it, which every buffer here is.
        _gl.ClearBufferSubData(GLEnum.CopyWriteBuffer, GLEnum.R32ui, (nint)offset, (nuint)size, GLEnum.RedInteger, GLEnum.UnsignedInt, null);
    }

    private void Timestamp(GlQuerySet set, int index)
    {
        if ((uint)index >= (uint)set.Count)
        {
            return;
        }

        _gl.QueryCounter(set.Names[index], GLEnum.Timestamp);
        set.Written[index] = true;
    }

    private uint BeginOcclusion(GlQuerySet? set, int index)
    {
        if (set is null || (uint)index >= (uint)set.Count)
        {
            return 0;
        }

        uint name = set.Names[index];
        _gl.BeginQuery(GLEnum.SamplesPassed, name);
        set.Written[index] = true;
        return name;
    }

    /// <summary>Writes query results into a buffer, on the GPU where the driver can and through the host where not.</summary>
    /// <remarks>
    /// <c>GL_ARB_query_buffer_object</c> lets the GPU write a result into a buffer without the host waiting for it,
    /// which is what a resolve means everywhere else. Without it the host waits for each result and writes it,
    /// which stalls -- acceptable for the occasional timing readout, and what a driver without the extension leaves.
    /// A slot never written reads as nought, as it would on the other backends.
    /// </remarks>
    private unsafe void ResolveQuerySet(GlQuerySet set, int first, int count, GlBuffer destination, long offset)
    {
        if (device.Caps.QueryBuffer)
        {
            _gl.BindBuffer(GLEnum.QueryBuffer, destination.Name);
            for (int i = 0; i < count; i++)
            {
                long at = offset + ((long)i * QueryResultBytes);
                if (set.Written[first + i])
                {
                    _gl.GetQueryObject(set.Names[first + i], GLEnum.QueryResult, (ulong*)at);
                }
                else
                {
                    ClearBuffer(destination, at, QueryResultBytes);
                    _gl.BindBuffer(GLEnum.QueryBuffer, destination.Name);
                }
            }

            _gl.BindBuffer(GLEnum.QueryBuffer, 0);
            return;
        }

        ulong[] results = new ulong[count];
        for (int i = 0; i < count; i++)
        {
            if (set.Written[first + i])
            {
                _gl.GetQueryObject(set.Names[first + i], GLEnum.QueryResult, out results[i]);
            }
        }

        _gl.BindBuffer(GLEnum.CopyWriteBuffer, destination.Name);
        fixed (ulong* data = results)
        {
            _gl.BufferSubData(GLEnum.CopyWriteBuffer, (nint)offset, (nuint)(count * QueryResultBytes), data);
        }
    }
}
