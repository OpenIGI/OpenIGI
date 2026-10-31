using OpenIGI.Core.Configuration;

namespace OpenIGI.Game.Player;

/// <summary>The player cheats of issue #57, and the session gate they sit behind.</summary>
/// <remarks>
/// <para>
/// None of these is archived: every launch starts retail, and a cheat only takes effect once
/// <see cref="CheatsEnabled"/> is on -- the first switch of the Gameplay settings' Cheats group, or the
/// console's <c>cheats 1</c>. Read them through <see cref="PlayerCheatOptions.Current"/>, which applies the
/// gate, rather than one by one.
/// </para>
/// <para>
/// Nothing here is retail behaviour. Every multiplier's value of 1 and every switch's value of off is
/// the unmodified game.
/// </para>
/// </remarks>
[CVarContainer]
public static class PlayerCheatCVars
{
    /// <summary>Developer mode: the session-only developer controls in Extras. Cheats do not need it.</summary>
    public static readonly BoolCVar DeveloperMode = CVar.Bool(
        "g_developer_mode", false, CVarOptions.Developer | CVarOptions.Hidden,
        "Shows the developer controls in Extras. Not saved.");

    /// <summary>The gate: puts the cheat values in force.</summary>
    public static readonly BoolCVar CheatsEnabled = CVar.Bool(
        "g_cheats_enabled", false, CVarOptions.Developer | CVarOptions.Hidden,
        "Puts the g_cheat_* variables in force. Not saved.");

    // Listed and completed like any variable -- the console marks them blocked until `cheats 1` -- so
    // they can be found from the console, which is where issue #57 asks for them.
    private const CVarOptions CheatFlags = CVarOptions.Cheat;

    /// <summary>Scales root-motion movement, air control, and horizontal jump carry.</summary>
    public static readonly NumericCVar<float> Speed = CVar.Float(
        "g_cheat_speed", 1f, 1f, 8f, CheatFlags,
        "Player movement speed multiplier (cheat). 1 is retail.");

    /// <summary>Scales the vertical jump launch speed.</summary>
    public static readonly NumericCVar<float> Jump = CVar.Float(
        "g_cheat_jump", 1f, 1f, 8f, CheatFlags,
        "Player jump height multiplier (cheat). 1 is retail.");

    /// <summary>Scales the maximum health read from HumanPlayer QVM argument 7.</summary>
    public static readonly NumericCVar<float> HealthScale = CVar.Float(
        "g_cheat_health_scale", 1f, 1f, 1000f, CheatFlags,
        "Player maximum health multiplier (cheat). 1 is retail.");

    /// <summary>Prevents direct, hit, and fall damage from changing player health.</summary>
    public static readonly BoolCVar InfiniteHealth = CVar.Bool(
        "g_cheat_infinite_health", false, CheatFlags,
        "Make player health infinite (cheat).");

    /// <summary>Firing takes nothing, from the magazine or the M203's reserve.</summary>
    public static readonly BoolCVar InfiniteAmmo = CVar.Bool(
        "g_cheat_infinite_ammo", false, CheatFlags,
        "Give the player infinite ammunition (cheat).");

    /// <summary>Provides every loaded weapon and adds it to the switch cycle.</summary>
    public static readonly BoolCVar AllWeapons = CVar.Bool(
        "g_cheat_all_weapons", false, CheatFlags,
        "Give the player every loaded weapon (cheat).");

    /// <summary>Makes every campaign mission selectable.</summary>
    public static readonly BoolCVar UnlockAllLevels = CVar.Bool(
        "g_cheat_unlock_all_levels", false, CheatFlags,
        "Unlock every campaign mission (cheat).");

    /// <summary>Suppresses the view kick from fired rounds.</summary>
    public static readonly BoolCVar NoRecoil = CVar.Bool(
        "g_cheat_no_recoil", false, CheatFlags,
        "Disable weapon recoil (cheat).");

    /// <summary>Refills the magazine from the reserve each tick, so a reload is never needed until it runs dry.</summary>
    public static readonly BoolCVar NoReload = CVar.Bool(
        "g_cheat_no_reload", false, CheatFlags,
        "Refill the magazine from the reserve without reloading (cheat).");
}
