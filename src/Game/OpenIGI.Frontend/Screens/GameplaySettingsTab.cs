using OpenIGI.Frontend.Settings;
using OpenIGI.Game.Player;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>The settings page's Gameplay category, with the interface and effect options that are not quality levels.</summary>
internal static class GameplaySettingsTab
{
    // The player cheats, their gate first: none of them does anything until it is on, and none outlasts the session.
    private static readonly GraphicsSetting[] CheatSettings =
    [
        new(PlayerCheatCVars.CheatsEnabled.Name, "Enable Cheats", "Puts the cheats below in force. Off, every one of them is ignored. Not saved: every launch starts with cheats off."),
        new(PlayerCheatCVars.InfiniteHealth.Name, "Infinite Health", PlayerCheatCVars.InfiniteHealth.Help),
        new(PlayerCheatCVars.InfiniteAmmo.Name, "Infinite Ammo", PlayerCheatCVars.InfiniteAmmo.Help),
        new(PlayerCheatCVars.NoReload.Name, "No Reload", PlayerCheatCVars.NoReload.Help),
        new(PlayerCheatCVars.NoRecoil.Name, "No Recoil", PlayerCheatCVars.NoRecoil.Help),
        new(PlayerCheatCVars.AllWeapons.Name, "Give All Weapons", PlayerCheatCVars.AllWeapons.Help),
        new(PlayerCheatCVars.UnlockAllLevels.Name, "Unlock All Levels", PlayerCheatCVars.UnlockAllLevels.Help),
        new(PlayerCheatCVars.Speed.Name, "Speed Multiplier", PlayerCheatCVars.Speed.Help),
        new(PlayerCheatCVars.Jump.Name, "High Jump Multiplier", PlayerCheatCVars.Jump.Help),
        new(PlayerCheatCVars.HealthScale.Name, "Health Multiplier", PlayerCheatCVars.HealthScale.Help),
    ];

    /// <summary>Builds the category's groups.</summary>
    /// <param name="rowBuilder">What the rows are built with.</param>
    /// <param name="theme">The theme.</param>
    public static List<SettingsGroup> Build(SettingsRowBuilder rowBuilder, UiTheme theme)
    {
        List<SettingsGroup> groups = CvarGroupBuilder.CvarGroups(rowBuilder, theme, CvarGroupBuilder.GameplayNames(rowBuilder.Context.Cvars), "General");
        groups.Add(SettingsGroup.Of("Interface and effects", rowBuilder.SettingRows(theme, GraphicsMenu.InterfaceSettings)));
        groups.Add(SettingsGroup.Of("Cheats", rowBuilder.SettingRows(theme, CheatSettings)));
        return groups;
    }
}
