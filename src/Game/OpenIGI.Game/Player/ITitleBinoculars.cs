namespace OpenIGI.Game.Player;

/// <summary>
/// A player that sights through binoculars of its own, which its own display draws: whether they are at its eyes just now.
/// </summary>
/// <remarks>
/// For a level that has something follow what its player sights -- a designator's lock -- where the player is another game's and
/// brings its own binoculars. A player whose binoculars the host draws says so, and more, through <see cref="ITitlePlayerOptics"/>.
/// </remarks>
public interface ITitleBinoculars
{
    /// <summary>Gets a value indicating whether the binoculars are raised to the player's eyes.</summary>
    bool IsBinocularsRaised { get; }
}
