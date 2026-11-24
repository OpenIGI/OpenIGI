using OpenIGI.Core.Configuration.Presets;
using OpenIGI.Core.Configuration;

namespace OpenIGI.Game.Player.Aim;

/// <summary>The console variables of modern aim: aiming down the sights, the lean with the weapon up, and mounting.</summary>
/// <remarks>
/// <para>
/// <b>Not retail, and off unless a player turns it on.</b> With <see cref="Enabled"/> off the alternate-fire button, the peek and
/// every weapon behave exactly as the original's do, and the four actions modern aim adds are never read.
/// </para>
/// <para>
/// Like <see cref="GameplayCVars.RealisticDrops"/> this changes play -- where rounds go, how fast the player moves -- so in a
/// multiplayer session the server's values are the ones every player plays with.
/// </para>
/// </remarks>
[CVarContainer]
public static class ModernAimCVars
{
    /// <summary>Whether modern aim is on.</summary>
    public static readonly BoolCVar Enabled = Experience.Define(
        CVar.Bool(
            "g_modern_aim",
            false,
            CVarOptions.Archive | CVarOptions.Simulation,
            "Modern aiming: the right mouse button aims down the sights, Q and E lean with the weapon up, and the weapon braces on "
            + "ledges and tables. Off plays the original's sight, zoom and peek."),
        vanilla: "0",
        enhanced: null);

    /// <summary>Whether a press of the aim button stays aimed until the next, rather than for as long as it is held.</summary>
    public static readonly BoolCVar Toggle = CVar.Bool(
        "g_ads_toggle",
        false,
        CVarOptions.Archive | CVarOptions.Simulation,
        "Aim down the sights with one press and leave them with the next. Off aims for as long as the button is held.");

    /// <summary>How fast the look turns through the sights, as a share of the speed that keeps the aim's feel at every zoom.</summary>
    public static readonly NumericCVar<float> Sensitivity = CVar.Float(
        "g_ads_sensitivity",
        1f,
        0.2f,
        2f,
        CVarOptions.Archive | CVarOptions.Simulation,
        "Look speed while aiming down the sights, relative to the speed that matches the hip at every zoom.");

    /// <summary>How much of each weapon's sight magnification is used, nought (none) to one (all of it).</summary>
    public static readonly NumericCVar<float> Zoom = CVar.Float(
        "g_ads_zoom",
        1f,
        0f,
        1f,
        CVarOptions.Archive | CVarOptions.Simulation,
        "How much the view narrows through the sights: 0 not at all, 1 each weapon's own magnification.");

    /// <summary>Whether aiming at a ledge, table or edge in front braces the weapon on it without the mount key.</summary>
    public static readonly BoolCVar AutoMount = CVar.Bool(
        "g_auto_mount",
        true,
        CVarOptions.Archive | CVarOptions.Simulation,
        "Aiming down the sights at a ledge, table or wall edge braces the weapon on it. Off braces only on the mount key.");

    private static ExperiencePresetRegistry Experience => ExperiencePresetRegistry.Default;
}
