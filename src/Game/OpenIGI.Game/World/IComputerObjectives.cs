namespace OpenIGI.Game.World;

/// <summary>What a mission's computer lists, whichever game's mission it is: its objective rows and the transmissions it logged.</summary>
/// <remarks>
/// Project I.G.I.'s objective runtime is one; a title's player carrying its own game's computer gives its own through
/// <see cref="Player.ITitlePlayerComputer"/>. The remastered computer draws from this alone.
/// </remarks>
public interface IComputerObjectives
{
    /// <summary>Gets the objective rows, in order.</summary>
    IReadOnlyList<MissionObjective> Objectives { get; }

    /// <summary>Gets the transmissions logged, oldest first.</summary>
    IReadOnlyList<MissionComputerLogEntry> Log { get; }
}
