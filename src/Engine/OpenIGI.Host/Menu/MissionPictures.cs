using OpenIGI.Frontend;
using OpenIGI.UI.Kit;

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
internal sealed class MissionPictures(MenuPictureLoader pictures) : IDisposable
{
    private readonly Dictionary<(string Title, int Mission), UiPicture?> _known = [];

    /// <summary>The picture of a title's mission.</summary>
    /// <param name="title">The title's id.</param>
    /// <param name="mission">The mission's id.</param>
    /// <returns>The picture, or <see langword="null"/> when none was shipped or it could not be read.</returns>
    public UiPicture? Get(string title, int mission)
    {
        if (!_known.TryGetValue((title, mission), out UiPicture? picture))
        {
            picture = Load(title, mission);
            _known[(title, mission)] = picture;
        }

        return picture;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (UiPicture? picture in _known.Values)
        {
            if (picture is { } made)
            {
                pictures.Release(made);
            }
        }

        _known.Clear();
    }

    private UiPicture? Load(string title, int mission)
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "missions",
            title,
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{mission}.jpg"));
        return pictures.Load(path);
    }
}
