using OpenIGI.Core.Configuration;

namespace OpenIGI.Game.Player.Aim;

/// <summary>Developer variables for tuning each weapon's sights live and capturing them: none archived, none for play.</summary>
/// <remarks>
/// <para>
/// <c>g_ads_tune 1</c> turns on the live tuning (<see cref="AdsLiveTuning"/>): the numpad moves the held weapon on its sights
/// and a panel shows where it stands, and Numpad Enter writes every tuned weapon out as aim-table entries.
/// </para>
/// <para>
/// <c>g_ads_debug_weapon</c> takes the named weapon in hand (with <c>g_cheat_all_weapons</c> it is carried),
/// <c>g_ads_debug_force</c> holds the sights up and <c>g_ads_debug_fire</c> the trigger, so a scripted capture can show every
/// weapon's sight picture, and its kick, without a hand on the mouse.
/// </para>
/// </remarks>
[CVarContainer]
public static class AdsTuningCVars
{
    private const CVarOptions Flags = CVarOptions.Developer;

    /// <summary>Whether the live tuning is on.</summary>
    public static readonly BoolCVar Tune = CVar.Bool(
        "g_ads_tune", false, Flags, "Tune the held weapon's sights from the numpad; Numpad Enter saves them (developer).");

    /// <summary>Holds the sights up whatever the button.</summary>
    public static readonly BoolCVar Force = CVar.Bool("g_ads_debug_force", false, Flags, "Hold the sights up (developer).");

    /// <summary>Holds the trigger whatever the button, for a capture of the weapon firing on its sights.</summary>
    public static readonly BoolCVar Fire = CVar.Bool("g_ads_debug_fire", false, Flags, "Hold the trigger (developer).");

    /// <summary>The weapon to take in hand, by script id, or empty.</summary>
    public static readonly StringCVar Weapon = CVar.Text("g_ads_debug_weapon", string.Empty, Flags, "Take this weapon in hand, by script id (developer).");
}
