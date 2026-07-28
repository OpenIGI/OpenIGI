using System.Globalization;
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
    private readonly List<(int Width, int Height)> _modes = [];

    /// <summary>Gets or sets what to do when the player picks a resolution.</summary>
    /// <remarks>
    /// The original stores the choice on the profile and applies it when the screen is committed.
    /// Applying it is the shell's job, so the shell supplies the handler.
    /// </remarks>
    public Action<int, int>? ApplyResolution { get; set; }

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
    /// The distinct sizes the monitor reports, smallest first, de-duplicated across refresh rates —
    /// the menu's list is of resolutions, not of modes, so two entries differing only in Hz would
    /// read as a repeat. The id is the index, which is what the profile stores.
    /// </remarks>
    public IEnumerable<MenuListItem> GetDisplayModes(int device)
    {
        if (_modes.Count == 0)
        {
            _modes.AddRange(window.GetDisplayModes()
                .Select(mode => (mode.Width, mode.Height))
                .Distinct()
                .OrderBy(mode => mode.Width)
                .ThenBy(mode => mode.Height));
        }

        return _modes.Select((mode, index) => new MenuListItem(
            index,
            string.Create(CultureInfo.InvariantCulture, $"{mode.Width}x{mode.Height}")));
    }

    /// <inheritdoc/>
    public void SetResolution(int mode)
    {
        if (mode >= 0 && mode < _modes.Count)
        {
            ApplyResolution?.Invoke(_modes[mode].Width, _modes[mode].Height);
        }
    }

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
