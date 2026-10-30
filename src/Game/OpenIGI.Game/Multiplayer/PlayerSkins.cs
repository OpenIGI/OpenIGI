using OpenIGI.Core.Configuration;

namespace OpenIGI.Game.Multiplayer;

/// <summary>A look a co-op player can wear: one of the game's own characters.</summary>
/// <param name="Id">What is saved and sent: short, lower case.</param>
/// <param name="Name">What the menu shows.</param>
/// <param name="Model">The character's model, as the levels name it.</param>
/// <param name="HomeLevel">A level that ships the model, for a mission that doesn't to borrow it from.</param>
/// <param name="Skeleton">
/// The skeleton the model is skinned to: the player's own body is 0, a soldier 1, an RPG soldier 2, a woman 6. They share
/// the player's bones and differ in proportion, so a look plays the player's clips on its own skeleton's rest pose.
/// </param>
public sealed record PlayerSkin(string Id, string Name, string Model, int HomeLevel, int Skeleton);

/// <summary>The looks a co-op player can choose between, and the choice itself.</summary>
/// <remarks>
/// The original's characters, by the model catalog's names. Jones ships with every level; the rest with some, and
/// <see cref="PlayerSkin.HomeLevel"/> names one that has it.
/// </remarks>
[CVarContainer]
public static class PlayerSkins
{
    /// <summary>The look worn when none is chosen, or the chosen one can't be had.</summary>
    public static readonly PlayerSkin Default = new("jones", "Jones", "000_01_1", 1, 0);

    /// <summary>Every look on offer, in the order the menu cycles them.</summary>
    public static readonly IReadOnlyList<PlayerSkin> All =
    [
        Default,
        new("anya", "Anya", "015_01_1", 1, 6),
        new("ekk", "Ekk", "012_01_1", 12, 6),
        new("priboi", "Jach Priboi", "009_01_1", 6, 1),
        new("harrison", "Harrison", "021_01_1", 4, 1),
        new("spetsnaz", "Spetsnaz", "018_01_1", 9, 1),
        new("security", "Security", "019_01_1", 1, 1),
        new("sniper", "Sniper", "001_01_1", 1, 1),
        new("gunner", "Gunner", "011_02_1", 6, 1),
        new("rpg", "RPG Soldier", "013_01_1", 2, 2),
    ];

    /// <summary>The look this player wears in co-op.</summary>
    public static readonly StringCVar Chosen = CVar.Text(
        "coop_skin",
        Default.Id,
        CVarOptions.Archive,
        "The look worn in co-op: " + string.Join(", ", All.Select(static s => s.Id)) + ".");

    /// <summary>Finds a look by its id; the default for one not on offer.</summary>
    /// <param name="id">The id.</param>
    /// <returns>The look.</returns>
    public static PlayerSkin Of(string? id) =>
        All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Default;
}
