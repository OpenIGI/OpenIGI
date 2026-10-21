using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons.Ballistics;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Input;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>What the player carries: weapons handed to the weapon machine, singly or all at once.</summary>
internal static class PlayerArsenal
{
    /// <summary>Adds a weapon to what the player carries.</summary>
    /// <param name="weapons">The weapon machine, or <see langword="null"/> before the player has one.</param>
    /// <param name="weapon">The weapon's roster entry.</param>
    /// <param name="ammo">Its ammunition record, or <see langword="null"/> when it names no calibre.</param>
    /// <returns><see langword="false"/> when the player has no weapon machine yet.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="weapon"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Carrying is not holding. Which of them ends up in hand is the weapon cycle's decision, taken on
    /// the first tick — see <see cref="PlayerCamera.UseWeaponCycle"/>.
    /// </remarks>
    internal static bool Carry(PlayerWeapons? weapons, WeaponType weapon, AmmoType? ammo)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        if (weapons is null)
        {
            return false;
        }

        weapons.Carry(weapon, ammo);
        return true;
    }

    /// <summary>Carries every weapon in the roster that is not carried already — the all-weapons cheat.</summary>
    /// <param name="weapons">The weapon machine, or <see langword="null"/> before the player has one.</param>
    /// <param name="roster">The weapons loaded for the current mission.</param>
    /// <param name="ammunition">The ammunition table each weapon's calibre is resolved against.</param>
    /// <returns>How many weapons were newly carried; zero once everything is.</returns>
    /// <remarks>
    /// Safe to call every tick. A weapon already carried keeps its magazine; a new one joins the end of
    /// the switch cycle so the cycle keys can reach it.
    /// </remarks>
    internal static int EnsureAllWeapons(
        PlayerWeapons? weapons,
        IReadOnlyList<WeaponType> roster,
        AmmoTypeRegistry ammunition)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(ammunition);

        if (weapons is null)
        {
            return 0;
        }

        int added = 0;
        foreach (WeaponType weapon in roster)
        {
            if (weapons.HasWeapon(weapon.WeaponId))
            {
                continue;
            }

            bool hasAmmo = ammunition.TryGetByCalibre(weapon.CalibreId, out AmmoType ammo);
            weapons.Carry(weapon, hasAmmo ? ammo : null);
            weapons.EnsureCycleContains(weapon.WeaponId);
            added++;
        }

        return added;
    }
}
