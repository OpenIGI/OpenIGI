using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.Deferred;

/// <summary>An image a pass keeps at the size of the picture it works over, remade when that size changes.</summary>
/// <remarks>
/// The image a resize replaces is retired rather than disposed: the frames still in flight may be reading it.
/// </remarks>
internal sealed class ScreenImage : IDisposable
{
    private readonly IRhiDevice _device;
    private readonly RetiredResources _retired;
    private readonly RhiTextureFormat _format;
    private readonly RhiTextureUsage _usage;
    private readonly string _label;
    private IRhiTexture? _image;

    /// <summary>Initialises a new instance of the <see cref="ScreenImage"/> class.</summary>
    /// <param name="device">The device the image lives on.</param>
    /// <param name="retired">Where an image a resize replaces goes until the frames reading it finish.</param>
    /// <param name="format">The image's format.</param>
    /// <param name="usage">What the image is used as.</param>
    /// <param name="label">Its name in captures.</param>
    public ScreenImage(IRhiDevice device, RetiredResources retired, RhiTextureFormat format, RhiTextureUsage usage, string label)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(retired);
        _device = device;
        _retired = retired;
        _format = format;
        _usage = usage;
        _label = label;
    }

    /// <summary>Gets the image at a size, making it anew where the size changed.</summary>
    /// <param name="width">The width wanted.</param>
    /// <param name="height">The height wanted.</param>
    /// <returns>The image.</returns>
    public IRhiTexture Ensure(int width, int height)
    {
        if (_image is { } image && image.Width == width && image.Height == height)
        {
            return image;
        }

        if (_image is not null)
        {
            _retired.Retire(_image);
        }

        _image = _device.CreateTexture(new RhiTextureDescriptor(Math.Max(width, 1), Math.Max(height, 1), _format, _usage, Label: _label));
        return _image;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _image?.Dispose();
        _image = null;
    }
}
