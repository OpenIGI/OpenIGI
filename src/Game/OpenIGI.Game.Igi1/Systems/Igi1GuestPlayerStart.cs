using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Systems;

/// <summary>Who Project I.G.I.'s player is in another game's level, and what it stands in.</summary>
/// <param name="Task">The level's own player task, which the player stands for and whose spawn it takes.</param>
/// <param name="Solids">The level's placed objects, as the game's player walks into and stands on them.</param>
/// <param name="Difficulty">The difficulty its own hits are scaled by.</param>
/// <param name="Surroundings">What it is hurt and dies in: the level's sounds, its corpse and its death camera.</param>
public sealed record Igi1GuestPlayerStart(HumanPlayerTask Task, ObjectCollisionIndex Solids, AiDifficulty Difficulty, IIgi1PlayerSurroundings Surroundings)
{
    /// <summary>Gets the level's sources of bodies its rounds meet: the level's own humans, and any other game's it hosts.</summary>
    public IReadOnlyList<IShootableBodies> Bodies { get; init; } = [];

    /// <summary>Gets the ladders it climbs.</summary>
    public IReadOnlyList<LadderPlacement> Ladders { get; init; } = [];

    /// <summary>Gets what it carries.</summary>
    public PlayerLoadout Loadout { get; init; } = PlayerLoadout.Of([], []);

    /// <summary>Gets the level's query group its humans answer lines in, which the player stands in.</summary>
    public uint HumanGroup { get; init; } = 1;

    /// <summary>Gets the level's material row for a human, which a line going into the player enters.</summary>
    public int HumanMaterial { get; init; }
}
