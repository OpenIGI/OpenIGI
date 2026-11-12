using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Hosting;

/// <summary>What the level's blasts ask of whatever shows and plays them: their sounds, their fire and smoke, and the voices they cut.</summary>
public interface IBlastEffectsHost
{
    /// <summary>Gets the log.</summary>
    IOpenIgiLogger Logger { get; }

    /// <summary>Gets the game's loop, whose variables an authored blast's expression writes.</summary>
    GameLoop Game { get; }

    /// <summary>Plays a sound definition where something happened.</summary>
    /// <param name="name">The definition.</param>
    /// <param name="positionInUnits">Where.</param>
    /// <param name="priority">Its priority.</param>
    void PlayDefinition(string name, Vector3d positionInUnits, int priority);

    /// <summary>Shatters a pane into its shards.</summary>
    /// <param name="placement">The pane.</param>
    /// <param name="atUnits">Where it was struck.</param>
    void GlassShatter(WorldPlacement placement, Vector3d atUnits);

    /// <summary>
    /// Shows search and destroy's bomb going off, over and above the rocket's fire it is drawn with: the remastered blast's
    /// shockwave, fireball, light and shake, and the props and walls near it shattered until the level is put back.
    /// </summary>
    /// <param name="atUnits">Where it went off, in world units.</param>
    /// <param name="radiusUnits">How far it kills, in world units.</param>
    /// <param name="debrisWorld">What the pieces it throws land on and bounce off: the queries a body falls through.</param>
    /// <returns>
    /// Whether the remastered blast is shown; <see langword="false"/> where it is off (the vanilla look) or nothing draws,
    /// and the bomb is the rocket's fire alone.
    /// </returns>
    /// <remarks>Presentation alone: what the bomb kills and breaks for good is the mode's and the blast's, as before.</remarks>
    bool BombBlast(Vector3d atUnits, float radiusUnits, Physics.RagdollWorld? debrisWorld) => false;
}
