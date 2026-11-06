using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi1.World;

/// <content>A player put back in a versus game, handed again what it starts with.</content>
public sealed partial class Igi1PlayerSetup
{
    /// <summary>
    /// Hands a player put back what a player starts with: the mission's own weapons and the map's, their ammunition filled
    /// again to what each declares (never taken down, so what it picked up and still carries stays).
    /// </summary>
    /// <param name="rig">The player's rig.</param>
    /// <param name="extra">The map's weapons, each with the rounds its ammunition is filled to.</param>
    public void Restock(PlayerCamera rig, IReadOnlyList<(string Weapon, int Rounds)> extra)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(extra);
        if (rig.Weapons is not { } weapons || Weapons is null || Ammunition is null)
        {
            return;
        }

        if (_bringUp is { } b && PlayerLoadout.TryRead(b.LevelRoot, out PlayerLoadout? loadout) && loadout is not null)
        {
            foreach (LoadoutWeapon declared in loadout.Weapons)
            {
                Hand(rig, weapons, declared.WeaponId, 0);
            }

            foreach (LoadoutAmmo declared in loadout.Ammunition)
            {
                if (Ammunition.Types.FirstOrDefault(a => string.Equals(a.ScriptId, declared.AmmoId, StringComparison.Ordinal)) is { } pool)
                {
                    FillTo(weapons, pool.AmmoId, declared.Count);
                }
            }
        }

        foreach ((string weapon, int rounds) in extra)
        {
            Hand(rig, weapons, weapon, rounds);
        }
    }

    private void Hand(PlayerCamera rig, OpenIGI.Game.Player.PlayerWeapons weapons, string scriptId, int rounds)
    {
        if (!Weapons.TryGetByScriptId(scriptId, out WeaponType weapon))
        {
            return;
        }

        bool hasAmmo = Ammunition.TryGetByCalibre(weapon.CalibreId, out AmmoType ammo);
        if (!weapons.HasWeapon(weapon.WeaponId))
        {
            rig.Carry(weapon, hasAmmo ? ammo : null);
        }

        if (hasAmmo && rounds > 0)
        {
            FillTo(weapons, ammo.AmmoId, rounds);
        }
    }

    private static void FillTo(OpenIGI.Game.Player.PlayerWeapons weapons, int ammoId, int rounds)
    {
        int missing = rounds - weapons.Ammunition.Count(ammoId);
        if (missing > 0)
        {
            weapons.Ammunition.Add(ammoId, missing);
        }
    }
}
