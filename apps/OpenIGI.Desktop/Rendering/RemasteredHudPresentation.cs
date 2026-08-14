using OpenIGI.Core.Graphics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Desktop;

/// <summary>Pure presentation decisions shared by the remaster HUD and its focused tests.</summary>
public static class RemasteredHudPresentation
{
    /// <summary>Whether the dedicated HUD setting selects the remaster presentation.</summary>
    public static bool UsesRemasteredHud(bool remasteredHudEnabled) => remasteredHudEnabled;

    /// <summary>Whether binoculars use the enhanced optics interface paired with the tactical computer.</summary>
    public static bool UsesRemasteredOptics(bool tacticalComputerEnabled) => tacticalComputerEnabled;

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
        > 0.55f => Bgra32.FromRgb(91, 210, 141),
        > 0.28f => Bgra32.FromRgb(240, 176, 84),
        _ => Bgra32.FromRgb(235, 92, 82),
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
        "a_gun.spr" => "USE GUN",
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
