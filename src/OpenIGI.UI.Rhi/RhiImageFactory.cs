using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI.Drawing;

namespace OpenIGI.UI.Rhi;

/// <summary>Makes interface images as textures on the game's graphics device.</summary>
/// <remarks>
/// A <see cref="UiImage"/> made here carries the device's texture handle as its identifier, so it maps
/// back to that handle with no table lookup when the <see cref="RhiUiRenderer"/> draws it.
/// </remarks>
public sealed class RhiImageFactory : IUiImageFactory
{
    private readonly IGraphicsDevice _device;

    /// <summary>Initializes a new instance of the <see cref="RhiImageFactory"/> class.</summary>
    /// <param name="device">The device to create textures on.</param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public RhiImageFactory(IGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
    }

    /// <inheritdoc/>
    public UiImage CreateImage(int width, int height, ReadOnlySpan<Bgra32> pixels) =>
        _device.CreateTexture(new TextureDescription(width, height), pixels).ToUiImage();

    /// <inheritdoc/>
    public void DestroyImage(UiImage image)
    {
        if (image.IsValid)
        {
            _device.DestroyTexture(image.ToTextureHandle());
        }
    }
}
