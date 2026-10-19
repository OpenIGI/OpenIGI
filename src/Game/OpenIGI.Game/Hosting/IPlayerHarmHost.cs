using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Hosting;

/// <summary>What the player's damage and death ask of whatever plays and shows them.</summary>
public interface IPlayerHarmHost
{
    /// <summary>Gets the log.</summary>
    IOpenIgiLogger Logger { get; }

    /// <summary>Gets the game's loop, whose clock the player's grunts are gated on.</summary>
    GameLoop Game { get; }

    /// <summary>Plays a sound definition.</summary>
    /// <param name="name">The definition.</param>
    /// <param name="positionInUnits">Where, or <see langword="null"/> for the listener's own.</param>
    /// <param name="priority">Its priority.</param>
    void PlayDefinition(string name, Vector3d? positionInUnits = null, int priority = 0);

    /// <summary>Hands the view to the camera that pulls back over the fallen body.</summary>
    /// <param name="position">The body, in world units.</param>
    /// <param name="facing">The way it was thrown.</param>
    /// <param name="walls">What pulls the camera in.</param>
    void BeginDeathCamera(Vector3d position, float facing, WorldRayCast walls);
}
