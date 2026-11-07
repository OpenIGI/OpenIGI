namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>One thing the armoury sells.</summary>
/// <param name="Id">What a player asks for it by: <c>/buy ak47</c>.</param>
/// <param name="Name">What it is shown as.</param>
/// <param name="Weapon">The title's weapon script id it hands over.</param>
/// <param name="Rounds">
/// The rounds of its ammunition it comes with, I.G.I. 2's carry limit for the gun's clip (its shop fills a bought gun's pouch to
/// it), held to the title's own limit where it has a lower one; nought for what has none of its own.
/// </param>
/// <param name="Price">What it costs.</param>
/// <param name="AmmoPrice">What its ammunition costs, filling the pouch back to <paramref name="Rounds"/>; nought when none is sold.</param>
public sealed record ArmouryItem(string Id, string Name, string Weapon, int Rounds, int Price, int AmmoPrice = 0)
{
    /// <summary>Gets a value indicating whether it is a gun's ammunition rather than the gun (<c>/buy ammo ak47</c>).</summary>
    public bool IsAmmo { get; init; }

    /// <summary>Gets a value indicating whether the ammunition is already full, so it is not sold.</summary>
    public bool Full { get; init; }
}

/// <summary>
/// What a round mode with a buy phase sells, and for how much: each title's own weapons, priced so a round's pay buys a
/// rifle and a lost round's a sub-machine gun, as the tactical shooters balance it.
/// </summary>
/// <remarks>
/// Not the original's: I.G.I. had no shop. Ammunition is sold as I.G.I. 2's shop sells it: a row for each gun carried, at that
/// game's price for the gun's clip, filling the pouch back up (I.G.I. 2 fills it to the carry limit), and not sold while full.
/// </remarks>
public static class Armoury
{
    /// <summary>Gets I.G.I.'s armoury, cheapest first within each kind.</summary>
    public static IReadOnlyList<ArmouryItem> Igi1 { get; } =
    [
        new("colt", "Colt Anaconda", "WEAPON_ID_COLT", 64, 300, 40),
        new("uzi", "Uzi", "WEAPON_ID_UZI", 320, 900, 60),
        new("mp5", "MP5", "WEAPON_ID_MP5", 300, 1250, 60),
        new("mp5sd", "MP5 SD", "WEAPON_ID_MP5SD", 300, 1500, 60),
        new("spas", "SPAS-12", "WEAPON_ID_SPAS12", 100, 1700, 20),
        new("ak47", "AK-47", "WEAPON_ID_AK47", 300, 2500, 80),
        new("m16", "M16A2", "WEAPON_ID_M16A2", 300, 2700, 80),
        new("dragunov", "Dragunov", "WEAPON_ID_DRAGUNOV", 20, 4000, 50),
        new("minimi", "Minimi", "WEAPON_ID_MINIMI", 400, 4500, 200),
        new("grenade", "Grenade", "WEAPON_ID_GRENADE", 3, 300),
        new("flashbang", "Flashbang", "WEAPON_ID_FLASHBANG", 3, 200),
        new("medipack", "Medipack", "WEAPON_ID_MEDIPACK", 1, 500),
    ];

    /// <summary>Gets a title's armoury.</summary>
    /// <param name="title">The title's id, as <c>igi1</c>.</param>
    /// <returns>What it sells; empty for a title without one yet.</returns>
    public static IReadOnlyList<ArmouryItem> For(string? title) => title is "igi1" ? Igi1 : [];

    /// <summary>What a player asks for to buy a gun's ammunition: <c>ammo</c> and the gun's id.</summary>
    public const string AmmoWord = "ammo";

    /// <summary>Gets the row that sells a gun's ammunition, as the buy menu lists it beside the gun carried.</summary>
    /// <param name="gun">The gun.</param>
    /// <param name="full">Whether the player's pouch for it is full already.</param>
    /// <returns>The row: asked for as <c>ammo &lt;id&gt;</c>, at the ammunition's price.</returns>
    public static ArmouryItem AmmoRow(ArmouryItem gun, bool full)
    {
        ArgumentNullException.ThrowIfNull(gun);
        return new ArmouryItem($"{AmmoWord} {gun.Id}", $"{gun.Name} ammo", gun.Weapon, gun.Rounds, gun.AmmoPrice) { IsAmmo = true, Full = full };
    }

    /// <summary>
    /// Gets how many rounds of a carried weapon's ammunition the pouch has room for, filled up to a number of rounds or the
    /// type's carry limit, whichever is fewer.
    /// </summary>
    /// <param name="weapons">What the player carries.</param>
    /// <param name="weapon">The title's weapon script id.</param>
    /// <param name="upTo">The rounds it is filled to.</param>
    /// <returns>The room, nought when full; <see langword="null"/> when the weapon is not carried or takes no ammunition.</returns>
    public static int? AmmoRoom(Player.PlayerWeapons weapons, string weapon, int upTo)
    {
        ArgumentNullException.ThrowIfNull(weapons);
        foreach (Player.CarriedWeapon carried in weapons.Carried)
        {
            if (string.Equals(carried.Weapon.ScriptId, weapon, StringComparison.OrdinalIgnoreCase) && carried.Gun.Ammo is { } ammo)
            {
                int limit = weapons.Ammunition.Limit(ammo.AmmoId);
                int target = limit >= 0 ? Math.Min(limit, upTo) : upTo;
                return Math.Max(0, target - weapons.Ammunition.Count(ammo.AmmoId));
            }
        }

        return null;
    }

    /// <summary>Finds an item by what a player asked for: its id, or its number in the list from one.</summary>
    /// <param name="items">The armoury.</param>
    /// <param name="asked">What was asked.</param>
    /// <returns>The item, or <see langword="null"/>.</returns>
    public static ArmouryItem? Find(IReadOnlyList<ArmouryItem> items, string asked)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (int.TryParse(asked, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int number))
        {
            return number >= 1 && number <= items.Count ? items[number - 1] : null;
        }

        return items.FirstOrDefault(i => string.Equals(i.Id, asked, StringComparison.OrdinalIgnoreCase));
    }
}
