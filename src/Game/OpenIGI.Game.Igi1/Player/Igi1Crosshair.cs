using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>
/// Project I.G.I.'s player as the remastered crosshair reads him: the weapon in hand's crosshair, the spread its next round
/// takes, whether an enemy is under it, and what his rounds have done. <b>Not retail</b>: the original draws one cross, for every
/// gun alike.
/// </summary>
public static class Igi1Crosshair
{
    // Guns fired from the hip at a run of rounds: the submachine guns.
    private static readonly HashSet<string> SubmachineGuns = new(StringComparer.OrdinalIgnoreCase)
    {
        "WEAPON_ID_UZI", "WEAPON_ID_UZIX2", "WEAPON_ID_MP5", "WEAPON_ID_MP5SD", "WEAPON_ID_MAC10",
    };

    // The belt-fed and the mounted.
    private static readonly HashSet<string> MachineGuns = new(StringComparer.OrdinalIgnoreCase)
    {
        "WEAPON_ID_MINIMI", "WEAPON_ID_M2HB",
    };

    // The rocket launchers.
    private static readonly HashSet<string> Launchers = new(StringComparer.OrdinalIgnoreCase)
    {
        "WEAPON_ID_RPG18", "WEAPON_ID_RPG7", "WEAPON_ID_RPG_IR",
    };

    /// <summary>The crosshair a weapon is aimed with.</summary>
    /// <param name="weapon">The weapon.</param>
    /// <returns>Its crosshair.</returns>
    public static CrosshairShape ShapeOf(WeaponType weapon)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        return weapon.Category switch
        {
            WeaponCategory.Pistol => CrosshairShape.Pistol,
            WeaponCategory.Shotgun => CrosshairShape.Shotgun,
            WeaponCategory.Knife => CrosshairShape.Melee,
            WeaponCategory.Grenade => CrosshairShape.Grenade,
            WeaponCategory.Gun when ScopedWeapons.IsScoped(weapon.ScriptId) => CrosshairShape.Sniper,
            WeaponCategory.Gun when Launchers.Contains(weapon.ScriptId) => CrosshairShape.Launcher,
            WeaponCategory.Gun when MachineGuns.Contains(weapon.ScriptId) => CrosshairShape.MachineGun,
            WeaponCategory.Gun when SubmachineGuns.Contains(weapon.ScriptId) => CrosshairShape.Smg,
            WeaponCategory.Gun => CrosshairShape.Rifle,
            _ => CrosshairShape.None,
        };
    }

    /// <summary>What the crosshair shows of the player this frame.</summary>
    /// <param name="player">The player.</param>
    /// <param name="soldiers">The mission's soldiers, or <see langword="null"/> for none.</param>
    /// <returns>The feedback.</returns>
    /// <remarks>
    /// The spread is the cone the next round strays in (<see cref="GunFiring.ConeAngle"/>): a gun's least until its burst runs
    /// on, then its most. The view is the eye's, its forward the middle column, as the round leaves along it.
    /// </remarks>
    public static PlayerAimFeedback Feedback(PlayerCamera player, SoldierSimulation? soldiers)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (player.Weapons is not { InHand: { } held } weapons)
        {
            return PlayerAimFeedback.Of(CrosshairShape.None, 0f, false, 0, player.Shots);
        }

        CrosshairShape shape = ShapeOf(held.Weapon);
        float spread = shape is CrosshairShape.None or CrosshairShape.Melee or CrosshairShape.Grenade
            ? 0f
            : GunFiring.ConeAngle(held.Weapon, held.Gun.State.RoundsThisBurst);
        bool onEnemy = false;
        if (soldiers is not null && shape != CrosshairShape.None && player.View is { } view)
        {
            Matrix3f m = view.EyeOrientation;
            onEnemy = soldiers.EnemyUnderAim(view.Eye, new Vector3f(m.M12, m.M22, m.M32));
        }

        return PlayerAimFeedback.Of(shape, spread, onEnemy, weapons.ShotSequence, player.Shots);
    }
}
