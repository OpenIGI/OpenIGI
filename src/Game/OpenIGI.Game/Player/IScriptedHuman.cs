using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Player;

/// <summary>
/// A human as a level's script handles it by its id, whichever game's it is: what the script's calls do to a human of another
/// game standing in the level for one of its own (<see cref="IHostedHuman.StandIn"/>).
/// </summary>
public interface IScriptedHuman
{
    /// <summary>Puts the human at a spot (<c>Human_PopPosition</c>): its feet there, off whatever it stood on.</summary>
    /// <param name="position">Where its feet go, in world units.</param>
    void PopPosition(Vector3d position);

    /// <summary>
    /// Holds the human where something else carries it -- the level's own body on a mounted gun, a seat that moves: its feet
    /// exactly there, on nothing. It is let go by putting it somewhere (<see cref="PopPosition"/>).
    /// </summary>
    /// <param name="position">Where its feet are held, in world units.</param>
    void HoldAt(Vector3d position);

    /// <summary>Takes every weapon out of the human's hands and off it (<c>HumanPlayer_DisableAllWeapons</c>).</summary>
    void DisableAllWeapons();
}
