using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Systems;

/// <summary>One of Project I.G.I.'s soldiers, as another game's level places it.</summary>
/// <param name="Model">The body it wears, one of the game's soldier models.</param>
/// <param name="AiType">Its kind, an <c>AITYPE_*</c> name of the game's AI settings.</param>
/// <param name="GraphId">The graph it walks, by its <c>AIGraph</c>'s id in the level.</param>
/// <param name="Position">Where it stands, in world units.</param>
/// <param name="Gamma">The way it faces, in radians.</param>
/// <param name="Team">Its team.</param>
public sealed record Igi1GuestSoldier(string Model, string AiType, int GraphId, Vector3d Position, float Gamma, int Team)
{
    /// <summary>Gets the weapons it carries, <c>WEAPON_ID_*</c> names, the first in its hand.</summary>
    public IReadOnlyList<string> Weapons { get; init; } = [];

    /// <summary>Gets what its script tells it.</summary>
    public AiGuardOrders Orders { get; init; } = AiGuardOrders.None;

    /// <summary>Gets the id its mind is given where it is free, or a negative one for any.</summary>
    public int PreferredId { get; init; } = -1;
}

/// <summary>What another game's level gives Project I.G.I.'s soldiers to stand, see, move and fight in.</summary>
/// <param name="World">The level's collision, which they move, look and shoot through.</param>
/// <param name="Player">The level's player, whom they look out for and fight.</param>
/// <param name="Random">The level's stream, which they draw from.</param>
/// <param name="Difficulty">The difficulty they fight at.</param>
public sealed record Igi1GuestLevelStart(IHostCollisionWorld World, IAiPlayer Player, IgiRandom Random, AiDifficulty Difficulty)
{
    /// <summary>Gets the level's graphs, by their task ids, read as the game reads a graph file.</summary>
    public IReadOnlyDictionary<int, GraphFile> Graphs { get; init; } = new Dictionary<int, GraphFile>();

    /// <summary>Gets the game's material row for a row of the level's, or <see langword="null"/> where it has none.</summary>
    public Func<int, int?> MaterialOf { get; init; } = static _ => null;

    /// <summary>Gets the level's query group its humans answer lines in, which the soldiers stand in.</summary>
    public uint HumanGroup { get; init; } = 1;

    /// <summary>Gets the level's material row for a human, which a line going into a soldier enters.</summary>
    public int HumanMaterial { get; init; }

    /// <summary>Gets what the level's rounds' and blasts' damage is multiplied by as it strikes a soldier.</summary>
    public float DamageFromHost { get; init; } = 1f;

    /// <summary>Gets what the soldiers' rounds' damage is multiplied by as it strikes what the level holds.</summary>
    public float DamageToHost { get; init; } = 1f;

    /// <summary>Gets what the soldiers' sounds play through, or <see langword="null"/> for silence.</summary>
    public ITitleAudio? Audio { get; init; }

    /// <summary>Gets where the soldiers report.</summary>
    public IOpenIgiLogger Logger { get; init; } = NullLogger.Instance;
}
