using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
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

    /// <summary>Spawns the smoke a flashbang leaves.</summary>
    /// <param name="positionInUnits">Where it went off.</param>
    void FlashbangSmoke(Vector3d positionInUnits);

    /// <summary>Spawns an <c>Explosion0</c>'s fire and fragments.</summary>
    /// <param name="positionInUnits">Where.</param>
    /// <param name="fullRangeUnits">Its full range.</param>
    /// <param name="fireballs">How many fireballs.</param>
    /// <param name="fragments">How many fragments.</param>
    void Explosion(Vector3d positionInUnits, float fullRangeUnits, int fireballs, int fragments);

    /// <summary>Spawns a shot-out camera lens's or alarm light's explosion.</summary>
    /// <param name="positionInUnits">Where.</param>
    /// <param name="isCameraLens">Whether it was a camera's lens rather than an alarm light.</param>
    void SpeakerExplosion(Vector3d positionInUnits, bool isCameraLens);

    /// <summary>Shatters a pane into its shards.</summary>
    /// <param name="placement">The pane.</param>
    /// <param name="atUnits">Where it was struck.</param>
    void GlassShatter(WorldPlacement placement, Vector3d atUnits);

}
