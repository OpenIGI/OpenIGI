using OpenIGI.Core.Graphics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.UI.Widgets;
using System.Globalization;

namespace OpenIGI.Presentation.Rendering;

/// <summary>Pure presentation decisions shared by the remaster HUD and its focused tests.</summary>
public static class RemasteredHudPresentation
{
    /// <summary>Whether the dedicated HUD setting selects the remaster presentation.</summary>
    public static bool UsesRemasteredHud(bool remasteredHudEnabled) => remasteredHudEnabled;

    /// <summary>Whether binoculars use the enhanced optics interface paired with the tactical computer.</summary>
    /// <remarks>
    /// The selection must include the actual optics renderer's readiness. A tactical map can be available
    /// while the independent HUD font atlas failed to initialize; choosing enhanced optics in that state
    /// would suppress the retail device and leave the player with zoom but no overlay.
    /// </remarks>
    public static bool UsesRemasteredOptics(bool tacticalComputerEnabled, bool opticsRendererReady) =>
        tacticalComputerEnabled && opticsRendererReady;

    /// <summary>Whether a title's player's binoculars use the enhanced optics, by the same rule: the setting, and faces to draw them.</summary>
    /// <remarks>Read by the host, which draws them, and by the title's display, which then leaves its retail overlay out.</remarks>
    public static bool UsesRemasteredTitleOptics =>
        UsesRemasteredOptics(OpenIGI.Rendering.HudCVars.TacticalComputer.Value, OpenIGI.Presentation.Text.UiFontLibrary.TypographyAvailable);

    /// <summary>Whether the retail weapon layer owns the current binocular presentation.</summary>
    /// <remarks>
    /// The tactical optics switch is independent of the ordinary remastered HUD switch. When the
    /// player is looking through binoculars, exactly one optics layer must win; outside binoculars,
    /// the ordinary HUD setting continues to choose the weapon layer.
    /// </remarks>
    public static bool UsesRetailWeaponHud(
        bool remasteredHudEnabled,
        bool binocularsOpen,
        bool remasteredOpticsEnabled)
        => binocularsOpen ? !remasteredOpticsEnabled : !remasteredHudEnabled;

    /// <summary>Whether messages use the remaster's tactical-card presentation.</summary>
    public static bool UsesTacticalMessages(bool remasteredHudEnabled, bool tacticalMessagesEnabled)
        => remasteredHudEnabled && tacticalMessagesEnabled;

    /// <summary>Whether the ordinary weapon crosshair remains visible.</summary>
    public static bool ShowsCrosshair(WeaponType? weapon, bool sniperScoped) =>
        !sniperScoped && weapon?.SightDisplay == SightDisplayType.Crosshair;

    /// <summary>Converts the player's clockwise world yaw into a conventional north-based compass heading.</summary>
    public static float HeadingDegrees(float yawRadians)
    {
        float degrees = yawRadians * (180f / MathF.PI);
        return ((degrees % 360f) + 360f) % 360f;
    }

    /// <summary>Returns the nearest finite binocular contact range, or <see langword="null"/>.</summary>
    public static float? NearestContactRange(IReadOnlyList<EnemyOverlayBox>? targets)
    {
        if (targets is null)
        {
            return null;
        }

        float? nearest = null;
        foreach (EnemyOverlayBox target in targets)
        {
            if (target.DistanceMetres <= 0f || !float.IsFinite(target.DistanceMetres))
            {
                continue;
            }

            nearest = nearest is null
                ? target.DistanceMetres
                : MathF.Min(nearest.Value, target.DistanceMetres);
        }

        return nearest;
    }

    /// <summary>Chooses the health colour with deliberately broad warning bands.</summary>
    public static Bgra32 HealthColour(float fraction) => Math.Clamp(fraction, 0f, 1f) switch
    {
        > 0.55f => UiPalette.Success,
        > 0.28f => UiPalette.Accent,
        _ => UiPalette.Danger,
    };

    /// <summary>Returns the first objective the player can still complete.</summary>
    public static MissionObjective? PrimaryObjective(IEnumerable<MissionObjective> objectives)
    {
        ArgumentNullException.ThrowIfNull(objectives);
        return objectives.FirstOrDefault(objective => objective.State == MissionObjectiveState.Incomplete);
    }

    /// <summary>Turns the retail action sprite into a concise contextual action label.</summary>
    public static string InteractionLabel(string? actionSprite) => actionSprite switch
    {
        "a_door.spr" => "OPEN DOOR",
        "action_picklock.spr" => "PICK LOCK",
        "a_ladder.spr" => "CLIMB",
        "a_wire.spr" => "USE ZIPLINE",
        "a_fence.spr" => "CLIMB FENCE",
        "action_pressbutton.spr" => "USE SWITCH",
        "action_comput.spr" => "USE COMPUTER",
        "action_searchcab.spr" => "SEARCH",
        "a_gun.spr" or "stationary.spr" => "USE GUN",
        "a_vehicle.spr" => "DRIVE",
        "weapon.spr" => "PICK UP WEAPON",
        "note.spr" => "READ NOTE",
        "anya.spr" => "TALK",
        _ => actionSprite is null ? string.Empty : "INTERACT",
    };

    /// <summary>Names only the weapon states that need a persistent HUD callout.</summary>
    public static string WeaponStateLabel(HumanWeaponState state) => state switch
    {
        HumanWeaponState.Reloading => "RELOADING",
        HumanWeaponState.ChangingWeapon => "SWITCHING",
        HumanWeaponState.DryFire => "EMPTY",
        _ => string.Empty,
    };

    /// <summary>Formats the MAG and RESERVE columns of the weapon readout.</summary>
    /// <param name="weapon">The weapon in hand.</param>
    /// <param name="roundsInClip">The magazine count, or <see cref="GunTiming.UnloadedClipSentinel"/> before a clip has loaded.</param>
    /// <param name="reserve">The loose-ammunition count the weapon draws from.</param>
    /// <returns>The MAG and RESERVE strings, either of which is <c>"--"</c> when the column does not apply.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="weapon"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A non-medical weapon whose <see cref="WeaponType.AmmoDisplay"/> is <see cref="AmmoDisplayType.None"/>
    /// counts nothing and blanks both columns. The medipack is a pool of injection charges with no firearm
    /// magazine, but its reserve still belongs under RESERVE even though its native gauge is None. Its
    /// <c>HeldGun</c> never loads a clip, so <paramref name="roundsInClip"/> stays at the unloaded sentinel
    /// and would otherwise render as a literal <c>"-02"</c>.
    /// </remarks>
    public static (string Magazine, string Reserve) AmmoColumns(WeaponType weapon, int roundsInClip, int reserve)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        if (weapon.AmmoDisplay == AmmoDisplayType.None
            && weapon.Category != WeaponCategory.Medipack)
        {
            return ("--", "--");
        }

        bool hasMagazine = weapon.Category != WeaponCategory.Medipack
            && roundsInClip != GunTiming.UnloadedClipSentinel;
        string magazine = hasMagazine ? roundsInClip.ToString("00", CultureInfo.InvariantCulture) : "--";
        return (magazine, reserve.ToString("000", CultureInfo.InvariantCulture));
    }

    /// <summary>Produces a short category tag used beside the weapon silhouette.</summary>
    public static string WeaponClass(WeaponType weapon)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        return weapon.Category switch
        {
            WeaponCategory.Pistol => "SIDEARM",
            WeaponCategory.Gun => "RIFLE",
            WeaponCategory.Shotgun => "SHOTGUN",
            WeaponCategory.Grenade => "GRENADE",
            WeaponCategory.Knife => "MELEE",
            WeaponCategory.Binocular => "OPTICS",
            WeaponCategory.Medipack => "MEDICAL",
            WeaponCategory.ProximityMine => "MINE",
            _ => "WEAPON",
        };
    }
}
