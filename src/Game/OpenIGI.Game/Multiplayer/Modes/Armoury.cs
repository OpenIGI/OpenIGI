namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>One thing the armoury sells.</summary>
/// <param name="Id">What a player asks for it by: <c>/buy ak47</c>.</param>
/// <param name="Name">What it is shown as.</param>
/// <param name="Weapon">The title's weapon script id it hands over.</param>
/// <param name="Rounds">The rounds of its ammunition it comes with; nought for what has none of its own.</param>
/// <param name="Price">What it costs.</param>
public sealed record ArmouryItem(string Id, string Name, string Weapon, int Rounds, int Price);

/// <summary>
/// What a round mode with a buy phase sells, and for how much: each title's own weapons, priced so a round's pay buys a
/// rifle and a lost round's a sub-machine gun, as the tactical shooters balance it.
/// </summary>
/// <remarks>Not the original's: I.G.I. had no shop; I.G.I. 2's priced only its own multiplayer kit.</remarks>
public static class Armoury
{
    /// <summary>Gets I.G.I.'s armoury, cheapest first within each kind.</summary>
    public static IReadOnlyList<ArmouryItem> Igi1 { get; } =
    [
        new("colt", "Colt Anaconda", "WEAPON_ID_COLT", 30, 300),
        new("uzi", "Uzi", "WEAPON_ID_UZI", 160, 900),
        new("mp5", "MP5", "WEAPON_ID_MP5", 150, 1250),
        new("mp5sd", "MP5 SD", "WEAPON_ID_MP5SD", 150, 1500),
        new("spas", "SPAS-12", "WEAPON_ID_SPAS12", 40, 1700),
        new("ak47", "AK-47", "WEAPON_ID_AK47", 150, 2500),
        new("m16", "M16A2", "WEAPON_ID_M16A2", 150, 2700),
        new("dragunov", "Dragunov", "WEAPON_ID_DRAGUNOV", 40, 4000),
        new("minimi", "Minimi", "WEAPON_ID_MINIMI", 200, 4500),
        new("grenade", "Grenade", "WEAPON_ID_GRENADE", 2, 300),
        new("flashbang", "Flashbang", "WEAPON_ID_FLASHBANG", 2, 200),
        new("medipack", "Medipack", "WEAPON_ID_MEDIPACK", 1, 500),
    ];

    /// <summary>Gets a title's armoury.</summary>
    /// <param name="title">The title's id, as <c>igi1</c>.</param>
    /// <returns>What it sells; empty for a title without one yet.</returns>
    public static IReadOnlyList<ArmouryItem> For(string? title) => title is "igi1" ? Igi1 : [];

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
