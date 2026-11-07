using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// A mod's say in one guard's life, attached through <see cref="SoldierSimulation.GuardBehaviours"/>: told when he decides,
/// asked about what he senses, and allowed to shape how he moves and looks. Every member has a default that changes nothing,
/// so a mod overrides only what it needs, and a guard with none runs exactly as the game runs him. <b>Not retail.</b>
/// </summary>
public interface IGuardBehaviour
{
    /// <summary>Gets a value indicating whether he may play an idle animation now.</summary>
    bool AllowsIdleAnimation => true;

    /// <summary>Gets a value indicating whether his body stays in the world when nobody saw it fall: the game takes it away.</summary>
    bool KeepsBodyOutOfView => false;

    /// <summary>Gets a value indicating whether a round asked of his gun this tick may leave it.</summary>
    bool MayFire => true;

    /// <summary>Gets an offset added to his eye, in world units: a lean.</summary>
    Vector3d EyeOffset => default;

    /// <summary>Gets a turn added to where his eye looks, in radians about the vertical: a glance.</summary>
    float EyeTurn => 0f;

    /// <summary>His tick begins, once the humans he may see have been offered to him.</summary>
    /// <param name="tick">The tick.</param>
    void TickStarted(int tick)
    {
    }

    /// <summary>Asked just before he would go idle, when nothing the script answered is waiting: the place to give him something to do.</summary>
    /// <returns>Whether it gave him something (<see cref="Igi1HumanAi.ApplyActions"/> and the like).</returns>
    bool BeforeIdle() => false;

    /// <summary>His tick ends, just before his body steps.</summary>
    void TickEnded()
    {
    }

    /// <summary>Asked each tick he fights, before the game's own fighting: the place for a fight of the mod's own.</summary>
    /// <param name="enemy">Where he last knew the enemy's feet, in world units.</param>
    /// <returns>Whether it fought for him this tick; if so the game's fighting does not run.</returns>
    bool Fight(Vector3d enemy) => false;

    /// <summary>His fight is over.</summary>
    void CombatEnded()
    {
    }

    /// <summary>Asked before he begins the next command of his patrol.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>Whether he holds where he is this tick instead.</returns>
    bool HoldsPatrolStop(int tick) => false;

    /// <summary>A command of his patrol has finished.</summary>
    /// <param name="tick">The tick.</param>
    void PatrolCommandFinished(int tick)
    {
    }

    /// <summary>His script has answered an event with these actions, and they are applied.</summary>
    /// <param name="cause">The event answered, or <see langword="null"/>.</param>
    /// <param name="actions">The actions.</param>
    void ActionsApplied(AiEvent? cause, IReadOnlyList<AiProducedAction> actions)
    {
    }

    /// <summary>Asked about something he senses, before the game hands it to his script.</summary>
    /// <param name="stimulus">What he senses; the mod may move where he believes it came from.</param>
    /// <returns><see langword="null"/> to let the game go on with it; otherwise what the sensing method returns, and the game does no more with it.</returns>
    bool? Stimulus(ref GuardStimulus stimulus) => null;

    /// <summary>Asked after each sweep of his eyes over a human, whether he counts as seen.</summary>
    /// <param name="sighting">What the sweep found.</param>
    /// <returns>Whether he is seen; the sweep's own answer by default.</returns>
    bool Sighting(in GuardSighting sighting) => sighting.Visible;

    /// <summary>Shapes his body's step this tick: how far he turns and where he moves.</summary>
    /// <param name="shaped">The step the game would take.</param>
    void ShapeStep(ref GuardStep shaped)
    {
    }

    /// <summary>Asked whether he may pass a waypoint of his route now, short of standing on it: a corner cut.</summary>
    /// <param name="waypoint">The waypoint he is heading for.</param>
    /// <param name="remaining">How many waypoints his route has left, this one included.</param>
    /// <returns>Whether he passes it now.</returns>
    bool PassesWaypoint(AiWaypoint waypoint, int remaining) => false;

    /// <summary>Where he steers for a waypoint.</summary>
    /// <param name="waypoint">The waypoint.</param>
    /// <returns>The point he turns towards; the waypoint itself by default.</returns>
    Vector3d SteerToward(AiWaypoint waypoint) => waypoint.Point;
}

/// <summary>One point of a guard's route. <b>Not retail</b> as a public type; the route is the game's.</summary>
/// <param name="Point">Where it is, in world units.</param>
/// <param name="Node">Its graph node, or -1.</param>
/// <param name="Door">Whether it is a door he must pass through on it.</param>
public readonly record struct AiWaypoint(Vector3d Point, int Node, bool Door);

/// <summary>What a guard senses, as <see cref="IGuardBehaviour.Stimulus"/> is asked about it. <b>Not retail.</b></summary>
public enum GuardStimulusKind
{
    /// <summary>A gunshot heard.</summary>
    Gunshot,

    /// <summary>A human's noise heard: a footstep or a landing.</summary>
    Noise,

    /// <summary>A round that missed him.</summary>
    RoundMissed,

    /// <summary>A round that passed close by.</summary>
    RoundPassed,

    /// <summary>Damage taken.</summary>
    Damage,

    /// <summary>An alert from another soldier.</summary>
    Alert,

    /// <summary>A soldier of his own side seen dead.</summary>
    FallenFriend,
}

/// <summary>Something a guard senses. <b>Not retail.</b></summary>
public struct GuardStimulus
{
    /// <summary>Gets or sets what it is.</summary>
    public GuardStimulusKind Kind { get; set; }

    /// <summary>Gets or sets where he takes it to come from, in world units; a mod may move it.</summary>
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets who caused it: a runtime id, or the player's.</summary>
    public int Source { get; set; }

    /// <summary>Gets or sets how far it carries, in world units, where it has a reach.</summary>
    public double Range { get; set; }

    /// <summary>Gets or sets the event the game would post for it, where it posts one.</summary>
    public AiEvent? Event { get; set; }

    /// <summary>Gets or sets a value indicating whether it came from his own side.</summary>
    public bool SameSide { get; set; }

    /// <summary>Gets or sets where the shooter stood, for a round, in world units.</summary>
    public Vector3d? From { get; set; }
}

/// <summary>What a sweep of a guard's eyes found of one human: the facts, for a mod to judge. <b>Not retail.</b></summary>
/// <param name="Candidate">The human: who, where his feet and his mark are, alive, crouching.</param>
/// <param name="Visible">Whether the sweep sees him: in the cone with a clear line.</param>
/// <param name="InCone">Whether he is in the eye's cones.</param>
/// <param name="RayRan">Whether the line-of-sight ray was cast this sweep, rather than its last verdict kept.</param>
/// <param name="Eye">The eye the sweep was made with.</param>
/// <param name="Tick">The tick.</param>
public readonly record struct GuardSighting(AiDetectionCandidate Candidate, bool Visible, bool InCone, bool RayRan, AiEyeSnapshot Eye, int Tick);

/// <summary>A guard's body's step this tick, as <see cref="IGuardBehaviour.ShapeStep"/> may shape it. <b>Not retail.</b></summary>
public struct GuardStep
{
    /// <summary>Gets or sets how far he turns this tick, in radians.</summary>
    public double Turn { get; set; }

    /// <summary>Gets or sets where he moves this tick, in world units across the ground.</summary>
    public Vector3d Move { get; set; }

    /// <summary>Gets the root motion his clip gives this tick, forward, in world units.</summary>
    public double RootMotion { get; init; }

    /// <summary>Gets the root motion his clip gives this tick, sideways, in world units.</summary>
    public double RootLateral { get; init; }
}
