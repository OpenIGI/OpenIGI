using System.Globalization;
using OpenIGI.Game.Config;
using OpenIGI.Game.Menus;
using OpenIGI.Game.World;
using OpenIGI.Platform;
using OpenIGI.Rendering;

namespace OpenIGI.Host.Configuration;

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
public sealed class HostConfigHost(IGraphicsDevice graphics, IGameWindow window) : IConfigHost
{
    /// <summary>The id the mission chain starts at. <c>0x417090</c> returns this constant.</summary>
    private const int FirstMissionId = MissionRegistry.FirstMissionId;

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

    /// <summary>Gets or sets the mission table the Select Mission screen reads.</summary>
    /// <remarks>
    /// Populated by the startup scan. Left null the screen shows nothing, which is the honest rendering
    /// of an installation whose missions could not be read.
    /// </remarks>
    public MissionRegistry? Missions { get; set; }

    /// <summary>Gets or sets the table that resolves a descriptor's name and description keys.</summary>
    public MenuStringTable? Strings { get; set; }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>Config_FillMissionSelectionBox</c> (<c>0x405660</c>) walks the mission chain from the first id,
    /// adding each mission's <em>name</em> keyed by its id, and <b>stops after the one the profile has
    /// reached</b> — which is what keeps a fresh profile from starting mission 14.
    /// </remarks>
    public IEnumerable<MenuListItem> GetMissions(int reached)
    {
        foreach (MissionDescriptor mission in Chain())
        {
            yield return new MenuListItem(mission.Id, Localise(mission.Name));
            if (mission.Id == reached)
            {
                yield break;
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>Config_FillMissionPictureBox</c> (<c>0x4056F0</c>) walks the whole chain — no progress stop —
    /// adding each mission's briefing sprite keyed by id, and the box then picks one.
    /// </remarks>
    public void FillMissionPictures(MenuWindowTask? target)
    {
        if (target is not PictureBoxTask box)
        {
            return;
        }

        foreach (MissionDescriptor mission in Chain())
        {
            box.Entries[mission.Id] = mission.BriefingSprite;
        }
    }

    /// <inheritdoc/>
    /// <remarks><c>Config_FillMissionTextBox</c> (<c>0x405760</c>), the same walk with the description.</remarks>
    public void FillMissionText(MenuWindowTask? target)
    {
        if (target is not TextBoxTask box)
        {
            return;
        }

        foreach (MissionDescriptor mission in Chain())
        {
            box.Entries[mission.Id] = Localise(mission.Description);
        }
    }

    /// <summary>Walks the mission chain from the first id, the way all three fill natives do.</summary>
    /// <remarks>
    /// The order is the chain's, not the table's — see <see cref="MissionRegistry.Chain"/>, which is where
    /// the walk lives now that the cinematic shell's mission list needs the same order.
    /// </remarks>
    private IEnumerable<MissionDescriptor> Chain() =>
        Missions?.Chain(FirstMissionId) ?? [];

    /// <summary>Resolves a descriptor's name or description, which are language keys rather than text.</summary>
    private string Localise(string key) => Strings is null ? key : Strings.Get(key);
}
