using OpenIGI.Game.Igi1.Player;
using OpenIGI.Core.Diagnostics;
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

    /// <summary>Writes a line of the session's diagnostics, where a versus player was put and how.</summary>
    /// <param name="line">The line.</param>
    public void NoteSession(string line) => host.Logger.Information("NetDiag", line);

    /// <summary>Hands a player one weapon, with its ammunition filled to a count: what it bought in a match.</summary>
    /// <param name="rig">The player's rig.</param>
    /// <param name="scriptId">The weapon's script id.</param>
    /// <param name="rounds">The rounds its ammunition is filled to.</param>
    /// <returns><see langword="true"/> when the level knows the weapon and the player could take it.</returns>
    public bool Give(PlayerCamera rig, string scriptId, int rounds)
    {
        ArgumentNullException.ThrowIfNull(rig);
        return rig.Weapons is { } weapons && Weapons is not null && Ammunition is not null && Hand(rig, weapons, scriptId, rounds);
    }

    private bool Hand(PlayerCamera rig, OpenIGI.Game.Player.PlayerWeapons weapons, string scriptId, int rounds)
    {
        if (!Weapons.TryGetByScriptId(scriptId, out WeaponType weapon))
        {
            return false;
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

        return true;
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
