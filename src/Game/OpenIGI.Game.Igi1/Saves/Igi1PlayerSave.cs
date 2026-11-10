using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.Saves;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi1.Saves;

/// <summary>
/// What a save game keeps of Project I.G.I.'s player. Not retail: the original kept no save games; this keeps what I.G.I. 2's
/// keeps of its own (<c>HumanPlayer</c>, <c>WeaponHandler</c>) -- where the player stands and looks, its health, what it carries
/// and the rounds in each, and the mission's counts -- and lets what it was in the middle of start afresh: it stands still, its
/// weapon drawn anew.
/// </summary>
internal static class Igi1PlayerSave
{
    /// <summary>The statement a save keeps the player in.</summary>
    /// <param name="world">The played mission.</param>
    /// <returns>The section.</returns>
    public static ISaveSection Section(MissionWorld world) => new ExchangedSection("Igi1Player", exchange =>
    {
        PlayerCamera? rig = world.Player;
        Vector3d feet = exchange.Position(rig?.PositionUnits ?? default);
        float yaw = exchange.Real(rig?.Yaw ?? 0f);
        float pitch = exchange.Real(rig?.Pitch ?? 0f);

        if (world.PlayerHealth is IExchangedState health)
        {
            health.Exchange(exchange);
        }
        else
        {
            exchange.Real(0f);
            exchange.Int(0);
        }

        world.IsPlayerDead = exchange.Bool(world.IsPlayerDead);
        world.PlayerHitQuietUntil = exchange.Int(world.PlayerHitQuietUntil);
        world.PlayerTicks = exchange.Int((int)world.PlayerTicks);
        world.SavesUsed = exchange.Int(world.SavesUsed);
        world.GameLoads = exchange.Int(world.GameLoads);
        world.MenusEnabled = exchange.Bool(world.MenusEnabled);

        PlayerWeapons? weapons = rig?.Weapons;
        (int Weapon, int Rounds)[] carried = exchange.Run<(int Weapon, int Rounds)>(
            [.. (weapons?.Carried ?? []).Select(static c => (c.Weapon.WeaponId, c.Gun.State.RoundsInClip))],
            static (e, gun) => (e.Int(gun.Weapon), e.Int(gun.Rounds)));
        (int Ammo, int Count)[] pool = exchange.Run<(int Ammo, int Count)>(
            [.. (weapons?.Ammunition.Contents ?? new Dictionary<int, int>()).OrderBy(static p => p.Key).Select(static p => (p.Key, p.Value))],
            static (e, ammo) => (e.Int(ammo.Ammo), e.Int(ammo.Count)));
        // The weapon in its hands, or the one it is reaching for.
        int inHand = exchange.Int(weapons?.WantedWeaponId ?? -1);
        if (!exchange.IsReading || rig is null)
        {
            return;
        }

        rig.StandAt(feet, yaw, pitch);
        if (weapons is null || world.WeaponTypes is not { } types)
        {
            return;
        }

        weapons.Disarm();
        foreach ((int weaponId, int rounds) in carried)
        {
            if (!types.TryGet(weaponId, out WeaponType type))
            {
                continue;
            }

            AmmoType? ammo = world.AmmoTypes is { } ammunition && ammunition.TryGetByCalibre(type.CalibreId, out AmmoType found) ? found : null;
            if (rig.Carry(type, ammo) && weapons.Carried.Count > 0 && weapons.Carried[^1].Gun is { } gun && gun.Weapon.WeaponId == weaponId)
            {
                gun.State.RoundsInClip = rounds;
            }
        }

        foreach ((int ammoId, int count) in pool)
        {
            weapons.Ammunition.Add(ammoId, count - weapons.Ammunition.Count(ammoId));
        }

        if (inHand >= 0)
        {
            weapons.RequestWeapon(inHand);
        }
    });
}
