using OpenIGI.Formats.Textures;
using OpenIGI.Frontend;
using OpenIGI.Rendering;
using OpenIGI.UI.Rhi;

namespace OpenIGI.Host.Menu;

/// <summary>
/// The missions' pictures the menu shows: small JPEGs taken beforehand from each level's showcase camera and shipped beside
/// the application (<c>Assets/missions/&lt;title&gt;/&lt;mission&gt;.jpg</c>), read and made a texture the first time one is
/// asked for and kept until the host closes.
/// </summary>
/// <remarks>
/// A mission without a file has no picture, and is not looked for again: a title whose pictures have not been taken, a
/// mission a mod adds. The page that asked then draws what it drew before there were pictures.
/// </remarks>
internal sealed class MissionPictures(IGraphicsDevice device) : IDisposable
{
    private readonly RhiImageFactory _images = new(device);
    private readonly Dictionary<(string Title, int Mission), MissionPicture?> _known = [];

    /// <summary>The picture of a title's mission.</summary>
    /// <param name="title">The title's id.</param>
    /// <param name="mission">The mission's id.</param>
    /// <returns>The picture, or <see langword="null"/> when none was shipped or it could not be read.</returns>
    public MissionPicture? Get(string title, int mission)
    {
        if (!_known.TryGetValue((title, mission), out MissionPicture? picture))
        {
            picture = Load(title, mission);
            _known[(title, mission)] = picture;
        }

        return picture;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (MissionPicture? picture in _known.Values)
        {
            if (picture is { } made)
            {
                _images.DestroyImage(made.Image);
            }
        }

        _known.Clear();
    }

    private MissionPicture? Load(string title, int mission)
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "missions",
            title,
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{mission}.jpg"));
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            TextureImage image = JpegImage.Read(File.ReadAllBytes(path));
            return new MissionPicture(_images.CreateImage(image.Width, image.Height, image.GetLevel(0)), image.Width, image.Height);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or TextureFormatException)
        {
            return null;
        }
    }
}
