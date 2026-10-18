using OpenIGI.Host.Services;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    // The level behind the menu: the preview, its soldiers, the lite profile and the frosted glass.
    private readonly MenuBackdrop _menuBackdrop;

    /// <summary>Gets whether the level on screen is the menu's backdrop, drawn lite.</summary>
    private bool BackdropLite => _menuBackdrop.IsLite(missionActive: _activeMission is not null);

    /// <summary>Applies or clears the backdrop's lite profile to match <see cref="BackdropLite"/>. Cheap; call freely.</summary>
    private void UpdateBackdropProfile() => _menuBackdrop.UpdateProfile(missionActive: _activeMission is not null);
}
