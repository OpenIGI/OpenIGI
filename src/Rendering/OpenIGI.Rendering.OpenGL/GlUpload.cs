using OpenIGI.Rendering.Rhi;
using Silk.NET.OpenGL;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>Texel uploads, from host memory or from a bound unpack buffer: one path for both.</summary>
/// <remarks>
/// The pointer is either the host data, with no unpack buffer bound, or an offset into the one bound, which is how
/// OpenGL tells the two apart. The texture is already bound on the edit unit.
/// </remarks>
internal static unsafe class GlUpload
{
    private const int BlockSize = 4;

    /// <summary>Writes texels into a region of a texture.</summary>
    /// <param name="gl">The entry points.</param>
    /// <param name="texture">The texture, bound on the edit unit.</param>
    /// <param name="region">Where the texels land.</param>
    /// <param name="bytesPerRow">How far apart rows are in the source.</param>
    /// <param name="rowsPerImage">How many rows apart layers are in the source, or nought for the copy's height.</param>
    /// <param name="size">How much.</param>
    /// <param name="data">The host data, or an offset into the bound unpack buffer.</param>
    public static void Upload(
        GL gl,
        GlTexture texture,
        in RhiTextureRegion region,
        int bytesPerRow,
        int rowsPerImage,
        in RhiExtent3D size,
        void* data)
    {
        gl.PixelStore(GLEnum.UnpackAlignment, 1);

        if (texture.Gl.Compressed)
        {
            UploadBlocks(gl, texture, region, bytesPerRow, rowsPerImage, size, (byte*)data);
            return;
        }

        int texel = texture.Format.BytesPerTexel();
        gl.PixelStore(GLEnum.UnpackRowLength, texel > 0 ? bytesPerRow / texel : 0);
        gl.PixelStore(GLEnum.UnpackImageHeight, rowsPerImage);

        GlFormat format = texture.Gl;
        if (texture.Target is GLEnum.Texture2D)
        {
            gl.TexSubImage2D(
                texture.Target,
                region.MipLevel,
                region.Origin.X,
                region.Origin.Y,
                (uint)size.Width,
                (uint)size.Height,
                format.Format,
                format.Type,
                data);
        }
        else
        {
            gl.TexSubImage3D(
                texture.Target,
                region.MipLevel,
                region.Origin.X,
                region.Origin.Y,
                region.Origin.Z,
                (uint)size.Width,
                (uint)size.Height,
                (uint)size.DepthOrArrayLayers,
                format.Format,
                format.Type,
                data);
        }

        gl.PixelStore(GLEnum.UnpackRowLength, 0);
        gl.PixelStore(GLEnum.UnpackImageHeight, 0);
    }

    // Block formats: a whole image at once where its rows are packed, one row of blocks at a time where they are not,
    // since OpenGL reads a compressed source's rows packed unless told block sizes it then checks for every format.
    private static void UploadBlocks(
        GL gl,
        GlTexture texture,
        in RhiTextureRegion region,
        int bytesPerRow,
        int rowsPerImage,
        in RhiExtent3D size,
        byte* data)
    {
        int blockBytes = texture.Format.BlockBytes();
        int blocksWide = (size.Width + BlockSize - 1) / BlockSize;
        int blocksHigh = (size.Height + BlockSize - 1) / BlockSize;
        int packed = blocksWide * blockBytes;
        int blockRowsPerImage = rowsPerImage > 0 ? (rowsPerImage + BlockSize - 1) / BlockSize : blocksHigh;

        for (int z = 0; z < size.DepthOrArrayLayers; z++)
        {
            byte* image = data + ((long)z * blockRowsPerImage * bytesPerRow);

            if (bytesPerRow == packed)
            {
                Write(gl, texture, region, region.Origin.Y, size.Width, size.Height, region.Origin.Z + z, packed * blocksHigh, image);
                continue;
            }

            for (int row = 0; row < blocksHigh; row++)
            {
                int y = row * BlockSize;
                int height = Math.Min(BlockSize, size.Height - y);
                Write(gl, texture, region, region.Origin.Y + y, size.Width, height, region.Origin.Z + z, packed, image + ((long)row * bytesPerRow));
            }
        }
    }

    private static void Write(GL gl, GlTexture texture, in RhiTextureRegion region, int y, int width, int height, int layer, int bytes, byte* data)
    {
        if (texture.Target is GLEnum.Texture2D)
        {
            gl.CompressedTexSubImage2D(
                texture.Target, region.MipLevel, region.Origin.X, y, (uint)width, (uint)height, texture.Gl.Internal, (uint)bytes, data);
        }
        else
        {
            gl.CompressedTexSubImage3D(
                texture.Target, region.MipLevel, region.Origin.X, y, layer, (uint)width, (uint)height, 1, texture.Gl.Internal, (uint)bytes, data);
        }
    }
}
