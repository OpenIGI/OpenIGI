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
}
