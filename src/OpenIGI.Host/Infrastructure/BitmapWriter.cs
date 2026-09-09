using System.Buffers.Binary;
using OpenIGI.Core.Graphics;

namespace OpenIGI.Host.Infrastructure;

/// <summary>
/// Writes a captured frame to an uncompressed 32-bit BMP.
/// </summary>
/// <remarks>
/// BMP because its 32-bit pixel layout <em>is</em> <see cref="Bgra32"/> — blue first — so a capture
/// writes out with no conversion, and because it needs no image library. This exists so a render can
/// be checked without a human at the window; it is a diagnostic, not an asset pipeline.
/// </remarks>
public static class BitmapWriter
{
    private const int FileHeaderSize = 14;
    private const int InfoHeaderSize = 40;

    /// <summary>Writes a frame to a BMP file.</summary>
    /// <param name="path">The file to write.</param>
    /// <param name="width">The frame width in pixels.</param>
    /// <param name="height">The frame height in pixels.</param>
    /// <param name="pixels">The frame pixels, row-major and top row first.</param>
    /// <exception cref="ArgumentException"><paramref name="pixels"/> is shorter than <paramref name="width"/> × <paramref name="height"/>.</exception>
    public static void Write(string path, int width, int height, ReadOnlySpan<Bgra32> pixels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (pixels.Length < width * height)
        {
            throw new ArgumentException(
                $"A {width}x{height} bitmap needs {width * height} pixels, got {pixels.Length}.",
                nameof(pixels));
        }

        int pixelBytes = width * height * Bgra32.Size;
        byte[] file = new byte[FileHeaderSize + InfoHeaderSize + pixelBytes];
        Span<byte> span = file;

        span[0] = (byte)'B';
        span[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(span[2..], (uint)file.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[10..], FileHeaderSize + InfoHeaderSize);

        Span<byte> info = span[FileHeaderSize..];
        BinaryPrimitives.WriteUInt32LittleEndian(info, InfoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(info[4..], width);

        // A negative height means the rows are stored top-down, which is the order captures arrive in.
        BinaryPrimitives.WriteInt32LittleEndian(info[8..], -height);
        BinaryPrimitives.WriteUInt16LittleEndian(info[12..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(info[14..], 32);
        BinaryPrimitives.WriteUInt32LittleEndian(info[20..], (uint)pixelBytes);

        Span<byte> target = span[(FileHeaderSize + InfoHeaderSize)..];
        for (int i = 0; i < width * height; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(target[(i * 4)..], pixels[i].ToPacked());
        }

        File.WriteAllBytes(path, file);
    }
}
