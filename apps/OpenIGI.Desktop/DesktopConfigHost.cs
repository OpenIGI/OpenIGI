using OpenIGI.Game.Config;
using OpenIGI.Game.Menus;
using OpenIGI.Platform;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>
/// Supplies the configuration functions with what only the shell knows.
/// </summary>
/// <remarks>
/// The render device and display-mode lists come from the graphics layer, and the mission list from
/// the installed data. What is not known yet reports nothing rather than a plausible-looking
/// invention: an empty list box is the truthful rendering of "this has not been implemented".
/// </remarks>
/// <param name="graphics">The graphics device, for its reported capabilities.</param>
/// <param name="window">The window, for the size it is running at.</param>
public sealed class DesktopConfigHost(IGraphicsDevice graphics, IGameWindow window) : IConfigHost
{
    /// <inheritdoc/>
    public int DefaultDisplayMode => 0;

    /// <inheritdoc/>
    /// <remarks>
    /// One entry, because the shell has exactly one backend. Enumerating adapters is a graphics-layer
    /// job that does not exist yet, and inventing a second device would be a lie the menu could act on.
    /// </remarks>
    public IEnumerable<MenuListItem> GetRenderDevices() =>
        [new MenuListItem(0, graphics.Capabilities.Renderer)];

    /// <inheritdoc/>
    /// <remarks>
    /// The one mode the window is actually running at. Mode enumeration belongs with the platform
    /// layer's display support, which is not built.
    /// </remarks>
    public IEnumerable<MenuListItem> GetDisplayModes(int device) =>
        [new MenuListItem(0, $"{window.Width}x{window.Height}")];

    /// <inheritdoc/>
    public int GetDeviceIndex(string guid) => 0;

    /// <inheritdoc/>
    public string GetDeviceGuid(int device) => PlayerProfile.DefaultDeviceGuid;

    /// <inheritdoc/>
    /// <remarks>Gamma is a display-mode property the GL backend does not expose yet.</remarks>
    public void SetGamma(float gamma)
    {
    }

    /// <inheritdoc/>
    /// <remarks>Missions are M8. Nothing here knows what levels the installation holds.</remarks>
    public IEnumerable<MenuListItem> GetMissions(int reached) => [];

    /// <inheritdoc/>
    public void FillMissionPictures(MenuWindowTask? target)
    {
    }

    /// <inheritdoc/>
    public void FillMissionText(MenuWindowTask? target)
    {
    }
}
