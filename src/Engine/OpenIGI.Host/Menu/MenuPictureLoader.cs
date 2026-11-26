using OpenIGI.Formats.Textures;
using OpenIGI.Frontend;
using OpenIGI.Rendering;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Rhi;

namespace OpenIGI.Host.Menu;

/// <summary>Makes pictures the menu draws from JPEG and PNG files, and lets them go: the missions' pictures and the player's avatar.</summary>
/// <param name="device">The device the textures are made on.</param>
internal sealed class MenuPictureLoader(IGraphicsDevice device)
{
    private readonly RhiImageFactory _images = new(device);

    /// <summary>Reads a JPEG or PNG file into a picture, told apart by its first bytes.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The picture, or <see langword="null"/> for a file that isn't there or isn't one.</returns>
    public UiPicture? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            TextureImage image = PngImage.IsPng(bytes) ? PngImage.Read(bytes) : JpegImage.Read(bytes);
            return new UiPicture(_images.CreateImage(image.Width, image.Height, image.GetLevel(0)), image.Width, image.Height);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or TextureFormatException)
        {
            return null;
        }
    }

    /// <summary>Lets a picture's texture go.</summary>
    /// <param name="picture">The picture.</param>
    public void Release(UiPicture picture) => _images.DestroyImage(picture.Image);
}
