using System.Buffers.Binary;
using OpenIGI.Core.Graphics;

namespace OpenIGI.Formats.Textures;

/// <summary>The exception thrown when bitmap bytes do not match the layout this reader supports.</summary>
public sealed class BitmapFormatException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="BitmapFormatException"/> class.</summary>
    public BitmapFormatException()
        : base("The bitmap data is malformed.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="BitmapFormatException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    public BitmapFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="BitmapFormatException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public BitmapFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// A Windows bitmap, decoded to <see cref="Bgra32"/>.
/// </summary>
/// <remarks>
/// <para>
/// Not an I.G.I. format. It is here because I.G.I. 2 stores its environment cube maps as plain
/// <c>.bmp</c> files inside a resource archive and hands them to <c>D3DXLoadSurfaceFromFileInMemory</c>,
/// which reads anything; this reader covers what the retail corpus actually ships and refuses the rest
/// by name rather than silently producing the wrong pixels.
/// </para>
/// <para>
/// Supported: a <c>BITMAPINFOHEADER</c> with 24 or 32 bits per pixel and no compression, in either row
/// order. All 186 cube-map faces in the retail tree are 24-bit <c>BI_RGB</c> with a 40-byte header and
/// pixels at offset 54, so the 32-bit case is there because it costs one branch, not because anything
/// needs it. Palettised, run-length encoded and bitfield bitmaps are refused.
/// </para>
/// <para>
/// Decoding to <see cref="Bgra32"/> is free rather than a conversion: a bitmap row already stores blue,
/// green and red in that order, which is the same layout the engine's textures and lightmaps use. A
/// 24-bit source gets an opaque alpha, matching the <c>D3DFMT_X8R8G8B8</c> surface the original loads it
/// into.
/// </para>
/// </remarks>
public sealed class BitmapImage
{
    /// <summary>The size of the file header, in bytes.</summary>
    public const int FileHeaderSize = 14;

    /// <summary>The size of the <c>BITMAPINFOHEADER</c> this reader supports.</summary>
    public const int InfoHeaderSize = 40;

    private readonly Bgra32[] _pixels;

    private BitmapImage(int width, int height, Bgra32[] pixels)
    {
        Width = width;
        Height = height;
        _pixels = pixels;
    }

    /// <summary>Gets the width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the pixels, row-major from the top-left.</summary>
    /// <remarks>
    /// Top-down regardless of how the file stored them. A bitmap with a positive height is stored bottom
    /// up, and this reader flips it, so a caller never has to ask which way round a particular file was.
    /// </remarks>
    public ReadOnlySpan<Bgra32> Pixels => _pixels;

    /// <summary>Reads a bitmap.</summary>
    /// <param name="data">The file bytes.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="BitmapFormatException">
    /// The bytes are not a bitmap, use a header or pixel format this reader does not support, or are
    /// too short for the image they declare.
    /// </exception>
    public static BitmapImage Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < FileHeaderSize + InfoHeaderSize)
        {
            throw new BitmapFormatException($"A bitmap is {data.Length} bytes, too short for its headers.");
        }

        if (data[0] != (byte)'B' || data[1] != (byte)'M')
        {
            throw new BitmapFormatException("A bitmap does not start with 'BM'.");
        }

        int pixelOffset = BinaryPrimitives.ReadInt32LittleEndian(data[10..]);
        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(data[14..]);
        if (headerSize != InfoHeaderSize)
        {
            throw new BitmapFormatException(
                $"A bitmap declares a {headerSize}-byte info header; only {InfoHeaderSize} is supported.");
        }

        int width = BinaryPrimitives.ReadInt32LittleEndian(data[18..]);
        int storedHeight = BinaryPrimitives.ReadInt32LittleEndian(data[22..]);
        int bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(data[28..]);
        int compression = BinaryPrimitives.ReadInt32LittleEndian(data[30..]);

        if (compression != 0)
        {
            throw new BitmapFormatException($"A bitmap declares compression {compression}; only BI_RGB is supported.");
        }

        if (bitsPerPixel is not (24 or 32))
        {
            throw new BitmapFormatException($"A bitmap is {bitsPerPixel} bits per pixel; only 24 and 32 are supported.");
        }

        // A negative height means the rows are already stored top-down. Height itself is never negative.
        bool bottomUp = storedHeight > 0;
        int height = Math.Abs(storedHeight);
        if (width <= 0 || height == 0)
        {
            throw new BitmapFormatException($"A bitmap declares an extent of {width}x{storedHeight}.");
        }

        // Rows are padded to a four-byte boundary, which is why a 24-bit row is not simply width * 3.
        int bytesPerPixel = bitsPerPixel / 8;
        int stride = ((width * bytesPerPixel) + 3) & ~3;
        long needed = (long)stride * height;
        if (pixelOffset < FileHeaderSize + InfoHeaderSize || pixelOffset + needed > data.Length)
        {
            throw new BitmapFormatException(
                $"A {width}x{height} bitmap needs {needed} pixel bytes at offset {pixelOffset}, past its {data.Length}-byte end.");
        }

        Bgra32[] pixels = new Bgra32[width * height];
        ReadOnlySpan<byte> rows = data.Slice(pixelOffset, (int)needed);

        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> row = rows.Slice((bottomUp ? height - 1 - y : y) * stride, stride);
            int into = y * width;

            for (int x = 0; x < width; x++)
            {
                ReadOnlySpan<byte> pixel = row[(x * bytesPerPixel)..];
                pixels[into + x] = new Bgra32(pixel[0], pixel[1], pixel[2], bitsPerPixel == 32 ? pixel[3] : (byte)255);
            }
        }

        return new BitmapImage(width, height, pixels);
    }

    /// <summary>Gets one pixel.</summary>
    /// <param name="x">The column, from the left.</param>
    /// <param name="y">The row, from the top.</param>
    /// <returns>The pixel.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The coordinates fall outside the image.</exception>
    public Bgra32 GetPixel(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);

        return _pixels[(y * Width) + x];
    }

    /// <inheritdoc/>
    public override string ToString() => $"bitmap {Width}x{Height}";
}
