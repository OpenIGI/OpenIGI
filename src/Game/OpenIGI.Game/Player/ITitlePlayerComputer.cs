using OpenIGI.Game.World;

namespace OpenIGI.Game.Player;

/// <summary>A title's player that carries its own game's computer, whose objectives the remastered computer can show in its stead.</summary>
/// <remarks>
/// In a level of the title's own, the remastered computer lists what this gives; standing in another game's mission, it lists
/// that mission's own objectives instead.
/// </remarks>
public interface ITitlePlayerComputer
{
    /// <summary>Gets the objectives and transmissions the player's own computer holds, or null where it holds none.</summary>
    IComputerObjectives? ComputerObjectives { get; }

    /// <summary>Gets a value indicating whether the player's computer offers its map now.</summary>
    bool ComputerMapAvailable { get; }
}
