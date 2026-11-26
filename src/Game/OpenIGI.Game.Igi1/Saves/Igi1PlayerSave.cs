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
        (Vector3d Feet, float Yaw, float Pitch) stance = ExchangeStance(exchange, rig, world.PlayerLife);
        world.PlayerTicks = exchange.Int((int)world.PlayerTicks);
        world.SavesUsed = exchange.Int(world.SavesUsed);
        world.GameLoads = exchange.Int(world.GameLoads);
        world.MenusEnabled = exchange.Bool(world.MenusEnabled);
        ExchangeArms(exchange, rig, stance, world.WeaponTypes, world.AmmoTypes);
    });

    /// <summary>
    /// The player as it stands in another game's level, for that level's save: where it stands and looks, its life, and what it
    /// carries, throws and has laid. The level's own save keeps the rest -- its clock, its saves, its menus.
    /// </summary>
    /// <param name="rig">The player's rig.</param>
    /// <param name="life">Its life.</param>
    /// <param name="weapons">The game's weapons, which what it carried is made of again.</param>
    /// <param name="ammunition">The game's ammunition.</param>
    /// <returns>The section.</returns>
    public static ISaveSection GuestSection(PlayerCamera rig, Igi1PlayerLife life, WeaponTypeRegistry weapons, AmmoTypeRegistry ammunition) =>
        new ExchangedSection("Igi1GuestPlayer", exchange =>
            ExchangeArms(exchange, rig, ExchangeStance(exchange, rig, life), weapons, ammunition));

    // Where the player stands and looks, its health, whether it is dead, and how long its cries are held.
    private static (Vector3d Feet, float Yaw, float Pitch) ExchangeStance(StateExchange exchange, PlayerCamera? rig, Igi1PlayerLife life)
    {
        Vector3d feet = exchange.Position(rig?.PositionUnits ?? default);
        float yaw = exchange.Real(rig?.Yaw ?? 0f);
        float pitch = exchange.Real(rig?.Pitch ?? 0f);

        if (life.Health is IExchangedState health)
        {
            health.Exchange(exchange);
        }
        else
        {
            exchange.Real(0f);
            exchange.Int(0);
        }

        life.IsDead = exchange.Bool(life.IsDead);
        life.HitQuietUntil = exchange.Int(life.HitQuietUntil);
        return (feet, yaw, pitch);
    }

    // What the player carries, with the rounds in each clip and its pool; the weapon in hand; its grenades and mines; and, read
    // back, the rig stood where it was saved and armed again.
    private static void ExchangeArms(
        StateExchange exchange,
        PlayerCamera? rig,
        (Vector3d Feet, float Yaw, float Pitch) stance,
        WeaponTypeRegistry? weaponTypes,
        AmmoTypeRegistry? ammoTypes)
    {
        (Vector3d feet, float yaw, float pitch) = stance;
        PlayerWeapons? weapons = rig?.Weapons;
        (int Weapon, int Rounds)[] carried = exchange.Run<(int Weapon, int Rounds)>(
            [.. (weapons?.Carried ?? []).Select(static c => (c.Weapon.WeaponId, c.Gun.State.RoundsInClip))],
            static (e, gun) => (e.Int(gun.Weapon), e.Int(gun.Rounds)));
        (int Ammo, int Count)[] pool = exchange.Run<(int Ammo, int Count)>(
            [.. (weapons?.Ammunition.Contents ?? new Dictionary<int, int>()).OrderBy(static p => p.Key).Select(static p => (p.Key, p.Value))],
            static (e, ammo) => (e.Int(ammo.Ammo), e.Int(ammo.Count)));
        // The weapon in its hands, or the one it is reaching for.
        int inHand = exchange.Int(weapons?.WantedWeaponId ?? -1);

        // What it has thrown and laid: its grenades with their fuses running, and its mines. A rocket or a launcher's round in
        // flight is over in a moment and is not kept.
        if (rig is not null)
        {
            rig.Grenades.Exchange(exchange);
        }
        else
        {
            exchange.Int(0);
            exchange.Int(0);
        }

        if (!exchange.IsReading || rig is null)
        {
            return;
        }

        rig.StandAfresh(feet, yaw, pitch);
        if (weapons is null || weaponTypes is not { } types)
        {
            return;
        }

        // Out of death first, when the level the save is read onto had killed it: only a living hand is armed.
        rig.ReviveWeapons();
        weapons.Disarm();
        foreach ((int weaponId, int rounds) in carried)
        {
            if (!types.TryGet(weaponId, out WeaponType type))
            {
                continue;
            }

            AmmoType? ammo = ammoTypes is { } ammunition && ammunition.TryGetByCalibre(type.CalibreId, out AmmoType found) ? found : null;
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
    }
}
