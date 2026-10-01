using System.Runtime.InteropServices;
using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using SkiaSharp;

namespace OpenIGI.UI.Skia;

/// <summary>The launcher's image table: interface images held as Skia images, keyed by <see cref="UiImage.Id"/>.</summary>
/// <remarks>
/// Images are made from straight-alpha BGRA pixels — the same pixels the game would upload as a texture —
/// or registered directly from an <see cref="SKImage"/> the launcher decoded itself, such as its hero
/// picture. Each also carries a shader, made once, that a textured mesh samples it through.
/// </remarks>
public sealed class SkiaImageStore : IUiImageFactory, IDisposable
{
    private readonly Dictionary<uint, Entry> _images = [];
    private uint _next = 1;

    /// <inheritdoc/>
    public UiImage CreateImage(int width, int height, ReadOnlySpan<Bgra32> pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (pixels.Length < width * height)
        {
            throw new ArgumentException("Fewer pixels than the image's size.", nameof(pixels));
        }

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(pixels[..(width * height)]);
        SKImage image = SKImage.FromPixelCopy(info, bytes)
            ?? throw new InvalidOperationException("Skia could not make an image from the pixels.");
        return Add(image);
    }

    /// <summary>Registers an image the caller made, taking ownership of it.</summary>
    /// <param name="image">The image.</param>
    /// <returns>The interface handle that draws it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    public UiImage Add(SKImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        uint id = _next++;
        SKShader shader = image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        _images[id] = new Entry(image, shader);
        return new UiImage(id);
    }

    /// <inheritdoc/>
    public void DestroyImage(UiImage image)
    {
        if (_images.Remove(image.Id, out Entry entry))
        {
            entry.Shader.Dispose();
            entry.Image.Dispose();
        }
    }

    /// <summary>Looks an image up.</summary>
    /// <param name="image">The handle.</param>
    /// <param name="skia">The image.</param>
    /// <param name="shader">A shader sampling it with clamped, linear filtering.</param>
    /// <returns><see langword="false"/> when the handle is not one of this store's.</returns>
    public bool TryGet(UiImage image, out SKImage skia, out SKShader shader)
    {
        if (_images.TryGetValue(image.Id, out Entry entry))
        {
            skia = entry.Image;
            shader = entry.Shader;
            return true;
        }

        skia = null!;
        shader = null!;
        return false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (Entry entry in _images.Values)
        {
            entry.Shader.Dispose();
            entry.Image.Dispose();
        }

        _images.Clear();
    }

    private readonly record struct Entry(SKImage Image, SKShader Shader);
}
