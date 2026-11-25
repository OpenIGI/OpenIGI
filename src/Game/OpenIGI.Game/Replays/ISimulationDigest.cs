namespace OpenIGI.Game.Replays;

/// <summary>
/// Part of a running level that folds where it stands into the level's fingerprint (<see cref="SimulationHash"/>): its random
/// streams, its player, its soldiers.
/// </summary>
/// <remarks>
/// <para>
/// The fingerprint is how a replay proves it is still the run it recorded: one is written every second of play, and whoever
/// plays the replay again -- a viewer, the server checking a ranked run -- folds its own and compares. The first second they
/// differ in is the first the simulation went another way, which is where to look for what it read that the replay did not
/// carry.
/// </para>
/// <para>
/// Only simulation state is folded, never what is drawn or heard, and always in the same order: a list in the order it is
/// stepped, never a hash set's. Cheap enough to fold every tick; a title folds what it would take to tell two runs apart, not
/// everything it holds.
/// </para>
/// </remarks>
public interface ISimulationDigest
{
    /// <summary>Folds where this part of the level stands.</summary>
    /// <param name="hash">The level's fingerprint so far.</param>
    void Fold(ref SimulationHash hash);
}
