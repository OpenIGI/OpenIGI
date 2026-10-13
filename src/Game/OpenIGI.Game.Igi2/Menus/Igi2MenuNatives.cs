using OpenIGI.Game.Igi2.Config;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Menus;

/// <summary>
/// The master volumes the in-game menu sets as its sliders move, <c>Sound_SetVolumeCD</c> and <c>Sound_vSSMasterVolume</c>: the music's
/// and the sound effects', each nought to one.
/// </summary>
/// <remarks>The OpenIGI shell owns the mix it plays at; these are the game's own values, which its scripts set and read.</remarks>
public sealed class Igi2SoundMix
{
    /// <summary>Gets or sets the music's volume.</summary>
    public float MusicVolume { get; set; } = 1f;

    /// <summary>Gets or sets the sound effects' volume.</summary>
    public float EffectsVolume { get; set; } = 1f;
}

/// <summary>The game and configuration natives the in-game menu's scripts call (game.c, config.c).</summary>
public static class Igi2MenuNatives
{
    /// <summary>The thin crosshair's picture, the first <c>Config_GetCrossHairs</c> offers.</summary>
    public const string CrossHair = "crosshair.spr";

    /// <summary>The thick crosshair's picture, the second.</summary>
    public const string ThickCrossHair = "crosshair-t.spr";

    /// <summary>The 3D sound providers the sound page lists: the one OpenIGI plays through.</summary>
    public static IReadOnlyList<string> SoundProviders { get; } = ["OpenIGI"];

    /// <summary>Registers the natives.</summary>
    /// <param name="natives">The namespace.</param>
    /// <param name="functions">The function table the menu's level expressions call.</param>
    /// <param name="menu">The in-game menu, whose active window the filling natives write to.</param>
    /// <param name="config">The game's configuration.</param>
    /// <param name="mix">The master volumes.</param>
    /// <param name="saves">The session's save games.</param>
    public static void Register(
        NativeRegistry natives,
        Scripting.Igi2ScriptFunctions? functions,
        Igi2InGameMenu menu,
        Igi2GameConfig config,
        Igi2SoundMix mix,
        Saves.Igi2SaveSession saves)
    {
        ArgumentNullException.ThrowIfNull(natives);
        void Fn(string name, Scripting.Igi2ScriptFunction function, int arguments) =>
            Scripting.Igi2ScriptCall.Register(natives, functions, name, function, arguments);

        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(mix);
        ArgumentNullException.ThrowIfNull(saves);

        // Game_GetGameModeCB: single player, here always.
        Fn("Game_GetGameMode", _ => QvmValue.FromInt32(0), 0);

        // Game_SetMusicVolumeCB, Game_SetSFXVolumeCB: the master volumes as the sliders move; Game_UpdateVolumeCB: back to the profile's.
        Fn("Game_SetMusicVolume", c => Done(() => mix.MusicVolume = c.GetSingle(0)), 1);
        Fn("Game_SetSFXVolume", c => Done(() => mix.EffectsVolume = c.GetSingle(0)), 1);
        Fn("Game_UpdateVolume", _ => Done(() =>
        {
            if (config.Active is { } profile)
            {
                mix.MusicVolume = profile.Music.Volume;
                mix.EffectsVolume = profile.SoundEffects.Volume;
            }
        }), 0);

        // Config_GetCrossHairs: the two crosshairs into the active picture box; Config_GetActiveCrossHair: the thick one while chosen;
        // Config_GetRGBA: the active picture box coloured as the crosshair is.
        Fn("Config_GetCrossHairs", _ => Done(() =>
        {
            menu.AddItem(new Igi2MenuItem(CrossHair, 0));
            menu.AddItem(new Igi2MenuItem(ThickCrossHair, 1));
        }), 0);
        Fn("Config_GetActiveCrossHair", _ => QvmValue.FromInt32(config.Active?.ThickCrossHair == true ? 1 : 0), 0);
        Fn("Config_GetRGBA", _ => Done(() =>
        {
            if (config.Active is { } profile)
            {
                menu.SetPictureColour((profile.CrossHairColour.R, profile.CrossHairColour.G, profile.CrossHairColour.B, profile.CrossHairAlpha));
            }
        }), 0);

        // Config_Fill3DProviderScrollListBox: each provider into the active list, by its number.
        Fn("Config_Fill3DProviderScrollListBox", _ => Done(() =>
        {
            for (int i = 0; i < SoundProviders.Count; i++)
            {
                menu.AddItem(new Igi2MenuItem(SoundProviders[i], i));
            }
        }), 0);

        // Game_GetNumberOfValidSaveGames: the mission's saves in the five menu slots. Game_FillSaveGameScrollListBox: each save into the
        // active list by its slot, then the restart line. Game_SetMenuLoadFromSlot: the slot the next level loads, none for the restart
        // line. Game_GetNewestSaveGameSlot: the newest save's slot, or the restart line's id.
        Fn("Game_GetNumberOfValidSaveGames", c => QvmValue.FromInt32(
            saves.Slots?.CountValid(saves.Owner with { Mission = c.GetInt32(0) }) ?? 0), 1);
        Fn("Game_FillSaveGameScrollListBox", _ => Done(() =>
        {
            foreach (Saves.Igi2SaveListing line in saves.Slots?.Listings(saves.Owner, saves.Translate)
                ?? [new Saves.Igi2SaveListing(Saves.Igi2SaveSlots.RestartMission, Saves.Igi2SaveSlots.RestartMissionKey, true)])
            {
                menu.AddItem(new Igi2MenuItem(line.Text, line.Slot, line.IsTranslated));
            }
        }), 0);
        Fn("Game_SetMenuLoadFromSlot", c => Done(() => saves.SetMenuLoadSlot(c.GetInt32(0))), 1);
        Fn("Game_GetNewestSaveGameSlot", _ => QvmValue.FromInt32(
            saves.Slots is { } slots && slots.TryFindNewest(saves.Owner, out int newest) ? newest : Saves.Igi2SaveSlots.RestartMission), 0);

        Igi2MultiplayerNatives.Register(natives, functions, menu);
    }

    private static QvmValue Done(Action action)
    {
        action();
        return QvmValue.Zero;
    }
}

/// <summary>
/// The multiplayer natives the in-game menu's shared script names, as a single-player game answers them: the network modules are open
/// (<c>NetManager_Open</c>, <c>Netshop_Open</c>, <c>RadioCom_Open</c>, <c>SlotSelector_Open</c> and <c>NetMission_Open</c> run whatever
/// the mode) but have no local client, no shop and no active slot selector, so the calls that need one do nothing.
/// </summary>
/// <remarks>Multiplayer itself is outside this port; only the menu's multiplayer screens, which a single-player game never opens, call these.</remarks>
public static class Igi2MultiplayerNatives
{
    /// <summary>The network manager's modes, <c>NETMANAGER_MODE_*</c>, as <c>NetManager_Open</c> registers them.</summary>
    public static IReadOnlyList<(string Name, int Value)> Modes { get; } =
    [
        ("NETMANAGER_MODE_SELECTTEAM", 0),
        ("NETMANAGER_MODE_SELECTSKIN", 1),
        ("NETMANAGER_MODE_SPECTATE", 2),
        ("NETMANAGER_MODE_SHOP", 3),
        ("NETMANAGER_MODE_SPAWN", 5),
        ("NETMANAGER_MODE_PLAY", 7),
        ("NETMANAGER_MODE_DEAD", 8),
    ];

    /// <summary>Registers the natives.</summary>
    /// <param name="natives">The namespace.</param>
    /// <param name="functions">The function table the menu's level expressions call.</param>
    /// <param name="menu">The in-game menu, whose active input box the player's name is written to.</param>
    public static void Register(NativeRegistry natives, Scripting.Igi2ScriptFunctions? functions, Igi2InGameMenu menu)
    {
        ArgumentNullException.ThrowIfNull(natives);
        void Fn(string name, Scripting.Igi2ScriptFunction function, int arguments = 0) =>
            Scripting.Igi2ScriptCall.Register(natives, functions, name, function, arguments);

        ArgumentNullException.ThrowIfNull(menu);
        foreach ((string name, int value) in Modes)
        {
            natives.RegisterConstant(name, value);
        }

        int mode = 0, team = -1;
        string playerName = string.Empty;

        // NetManager_SetModeCB keeps the mode; NetManager_GetNameCB writes the configured player name into the active input box, and
        // NetManager_SetNameCB keeps it; the team is the local client's, of which there is none.
        Fn("NetManager_SetMode", c => Zero(() => mode = c.GetInt32(0)), 1);
        Fn("NetManager_GetName", _ => Zero(() => menu.SetInputText(playerName)), 0);
        Fn("NetManager_SetName", c => Zero(() => playerName = c.GetString(0)), 1);
        Fn("NetManager_GetTeam", _ => QvmValue.FromInt32(team), 0);
        natives.RegisterVariable("NetManager_eTeam", new QvmIntegerVariable(() => team, value => team = value));
        natives.RegisterVariable("NetMission_eCurrentObjective", new QvmIntegerVariable(static () => 0, static _ => { }));

        // Without a local client: no pictures to collect, nothing active, nothing to set (each is gated on multiplayer or a client).
        foreach (string name in (string[])["NetManager_CollectBodyPics", "NetManager_CollectFacePics", "NetManager_SetFace", "NetManager_SetModel",
            "NetManager_SetTeam", "Netshop_Activate", "Netshop_SetInventoryText", "RadioCom_Activate", "SlotSelector_Deactivate",
            "SlotSelector_ItemClicked", "SlotSelector_SetActiveCategory", "SlotSelector_SetSlotCategoryName", "SlotSelector_SetSlotText"])
        {
            Fn(name, static _ => QvmValue.Zero);
        }

        foreach (string name in (string[])["NetManager_GetActiveBody", "NetManager_GetActiveFace", "NetManager_IsModelSelect",
            "SlotSelector_isCategoryEmpty", "SlotSelector_isCategorySelected", "SlotSelector_isSlotEnabled", "SlotSelector_isSlotVisible",
            "SlotSelector_isThisCategorySelected"])
        {
            Fn(name, static _ => QvmValue.FromInt32(0));
        }

        _ = mode;
    }

    private static QvmValue Zero(Action action)
    {
        action();
        return QvmValue.Zero;
    }
}
