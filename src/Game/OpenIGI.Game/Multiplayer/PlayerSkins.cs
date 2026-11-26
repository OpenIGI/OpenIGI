using OpenIGI.Core.Configuration;
using OpenIGI.Core.Titles;

namespace OpenIGI.Game.Multiplayer;

/// <summary>A look a co-op player can wear: one of the game's own characters.</summary>
/// <param name="Id">What is saved and sent: short, lower case.</param>
/// <param name="Name">What the menu shows.</param>
/// <param name="Model">The character's model, as the levels name it; empty for the level's own player's.</param>
/// <param name="HomeLevel">
/// Project I.G.I.: a level that ships the model, for a mission that doesn't to borrow it from. I.G.I. 2 finds the pack that
/// ships it by the model's name instead, and leaves this nought.
/// </param>
/// <param name="Skeleton">
/// Project I.G.I.: the skeleton the model is skinned to: the player's own body is 0, a soldier 1, an RPG soldier 2, a woman 6.
/// They share the player's bones and differ in proportion, so a look plays the player's clips on its own skeleton's rest pose.
/// I.G.I. 2 finds a body's clips by its bones' shape, which every one of its humans shares, and leaves this nought.
/// </param>
public sealed record PlayerSkin(string Id, string Name, string Model, int HomeLevel = 0, int Skeleton = 0);

/// <summary>The looks one title's co-op players choose between, and the choice this player saved for it.</summary>
public sealed class PlayerLooks
{
    /// <summary>Initializes a new instance of the <see cref="PlayerLooks"/> class.</summary>
    /// <param name="chosen">The setting the choice is saved in.</param>
    /// <param name="all">Every look on offer, in the order the menu cycles them, the one worn when none is chosen first.</param>
    public PlayerLooks(StringCVar chosen, IReadOnlyList<PlayerSkin> all)
    {
        ArgumentNullException.ThrowIfNull(chosen);
        ArgumentNullException.ThrowIfNull(all);
        ArgumentOutOfRangeException.ThrowIfZero(all.Count);
        Chosen = chosen;
        All = all;
    }

    /// <summary>Gets the look worn when none is chosen, or the chosen one can't be had.</summary>
    public PlayerSkin Default => All[0];

    /// <summary>Gets every look on offer, in the order the menu cycles them.</summary>
    public IReadOnlyList<PlayerSkin> All { get; }

    /// <summary>Gets the setting this player's choice is saved in.</summary>
    public StringCVar Chosen { get; }

    /// <summary>Gets the look this player chose, or the default.</summary>
    public PlayerSkin Own => Of(Chosen.Value);

    /// <summary>Finds a look by its id; the default for one not on offer.</summary>
    /// <param name="id">The id.</param>
    /// <returns>The look.</returns>
    public PlayerSkin Of(string? id) =>
        All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Default;
}

/// <summary>The looks co-op players can choose between in each game, and each game's saved choice.</summary>
[CVarContainer]
public static class PlayerSkins
{
    /// <summary>The look worn in Project I.G.I.'s co-op.</summary>
    public static readonly StringCVar ChosenIgi1 = CVar.Text(
        "coop_skin",
        "jones",
        CVarOptions.Archive,
        "The look worn in Project I.G.I.'s co-op: jones, anya, ekk, priboi, harrison, spetsnaz, security, sniper, gunner, rpg.");

    /// <summary>The look worn in I.G.I. 2's co-op.</summary>
    public static readonly StringCVar ChosenIgi2 = CVar.Text(
        "coop_skin_igi2",
        "jones",
        CVarOptions.Archive,
        "The look worn in I.G.I. 2's co-op: jones (the mission's own), jones1-3, igiagent1-3, csf1-3, resg1-3, les1-3, chn1-3, anya, priboi.");

    /// <summary>
    /// Project I.G.I.'s characters, by the model catalog's names. Jones ships with every level; the rest with some, and
    /// <see cref="PlayerSkin.HomeLevel"/> names one that has it.
    /// </summary>
    public static readonly PlayerLooks Igi1 = new(
        ChosenIgi1,
        [
            new("jones", "Jones", "000_01_1", 1, 0),
            new("anya", "Anya", "015_01_1", 1, 6),
            new("ekk", "Ekk", "012_01_1", 12, 6),
            new("priboi", "Jach Priboi", "009_01_1", 6, 1),
            new("harrison", "Harrison", "021_01_1", 4, 1),
            new("spetsnaz", "Spetsnaz", "018_01_1", 9, 1),
            new("security", "Security", "019_01_1", 1, 1),
            new("sniper", "Sniper", "001_01_1", 1, 1),
            new("gunner", "Gunner", "011_02_1", 6, 1),
            new("rpg", "RPG Soldier", "013_01_1", 2, 2),
        ]);

    /// <summary>
    /// I.G.I. 2's characters: first the mission's own Jones, as the campaign dresses him; then the multiplayer game's bodies, each
    /// with its three faces, as its menu offers them -- the first side's (<c>NetManager_atGoodModel</c>: <c>jones</c>,
    /// <c>igiagent</c>, <c>csf</c>) and the second's (<c>NetManager_atBadModel</c>: <c>resg</c>, <c>les</c>, <c>chn</c>), each named
    /// <c>"%s%d_1"</c> by body and face (<c>NetManager_GetTeamModelName</c>); then the campaign's Anya and Priboi. Jones's ship with
    /// every level (the common pack); the rest are found in the multiplayer game's pack, or the location's that ships them, when a
    /// level lacks them.
    /// </summary>
    public static readonly PlayerLooks Igi2 = new(
        ChosenIgi2,
        [
            new("jones", "Jones (the mission's)", string.Empty),
            .. Faces("jones", "Jones"),
            .. Faces("igiagent", "IGI Agent"),
            .. Faces("csf", "CSF Soldier"),
            .. Faces("resg", "Russian Soldier"),
            .. Faces("les", "Libyan Soldier"),
            .. Faces("chn", "Chinese Soldier"),
            new("anya", "Anya", "anya_1"),
            new("priboi", "Priboi", "priboi_1"),
        ]);

    /// <summary>Gets a title's looks.</summary>
    /// <param name="titleId">The title's id.</param>
    /// <returns>I.G.I. 2's for I.G.I. 2, Project I.G.I.'s for anything else.</returns>
    public static PlayerLooks For(string? titleId) => titleId == GameTitle.Igi2.Id ? Igi2 : Igi1;

    // A multiplayer body's three faces, as its menu numbers them.
    private static IEnumerable<PlayerSkin> Faces(string body, string name) =>
        Enumerable.Range(1, 3).Select(face => new PlayerSkin($"{body}{face}", $"{name} {face}", $"{body}{face}_1"));
}
