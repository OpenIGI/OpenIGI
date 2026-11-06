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

    /// <summary>Takes every weapon and round a player carries away: one who died in a match keeps nothing it had.</summary>
    /// <param name="rig">The player's rig.</param>
    public static void Disarm(PlayerCamera rig)
    {
        ArgumentNullException.ThrowIfNull(rig);
        rig.Weapons?.Disarm();
    }

    /// <summary>Reads what a player carries: each weapon's script id, and the rounds of its ammunition in the pouch.</summary>
    /// <param name="rig">The player's rig.</param>
    /// <returns>Its kit.</returns>
    public IReadOnlyList<(string Weapon, int Rounds)> Kit(PlayerCamera rig)
    {
        ArgumentNullException.ThrowIfNull(rig);
        if (rig.Weapons is not { } weapons)
        {
            return [];
        }

        return
        [
            .. weapons.Carried.Select(c => (c.Weapon.ScriptId,
                Ammunition is not null && Ammunition.TryGetByCalibre(c.Weapon.CalibreId, out AmmoType ammo) ? weapons.Ammunition.Count(ammo.AmmoId) : 0)),
        ];
    }

    /// <summary>
    /// Puts the server's kit on a client's own player: from nothing when the server's player was given only it (a respawn after
    /// a death), so both start the same fresh guns; otherwise the weapons it lacks added and each pouch set to the server's count,
    /// the guns it holds kept with their magazines.
    /// </summary>
    /// <param name="rig">The client's own player.</param>
    /// <param name="kit">The server's kit.</param>
    /// <param name="fresh">Whether the server's player was armed from nothing.</param>
    public void Arm(PlayerCamera rig, IReadOnlyList<(string Weapon, int Rounds)> kit, bool fresh)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(kit);
        if (rig.Weapons is not { } weapons || Weapons is null || Ammunition is null)
        {
            return;
        }

        if (fresh)
        {
            weapons.Disarm();
        }

        foreach ((string weapon, int rounds) in kit)
        {
            if (Hand(rig, weapons, weapon, 0) && Weapons.TryGetByScriptId(weapon, out WeaponType type)
                && Ammunition.TryGetByCalibre(type.CalibreId, out AmmoType ammo))
            {
                weapons.Ammunition.Add(ammo.AmmoId, rounds - weapons.Ammunition.Count(ammo.AmmoId));
            }
        }
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
