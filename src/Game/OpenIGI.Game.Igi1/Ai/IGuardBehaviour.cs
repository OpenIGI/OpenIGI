using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// A mod's say in one guard, attached through <see cref="SoldierSimulation.AttachGuardBehaviour"/>. It says nothing itself: a
/// behaviour implements whichever of <see cref="IGuardLife"/>, <see cref="IGuardSenses"/>, <see cref="IGuardMovement"/> and
/// <see cref="IGuardCombat"/> it needs, every member of which has a default that changes nothing. A guard with no behaviour runs
/// exactly as the game runs him, and one with several hears from each in the order they were attached. <b>Not retail.</b>
/// </summary>
/// <remarks>A behaviour that throws is logged, and the guard goes on as though it had said nothing.</remarks>
public interface IGuardBehaviour;

/// <summary>A behaviour's say in a guard's life: his tick, his idle, his patrol, his body. <b>Not retail.</b></summary>
public interface IGuardLife : IGuardBehaviour
{
    /// <summary>Gets a value indicating whether he may play an idle animation now; he plays one only if every behaviour lets him.</summary>
    bool AllowsIdleAnimation => true;

    /// <summary>Gets a value indicating whether his body stays when nobody saw it fall, which the game takes away; it stays if any behaviour keeps it.</summary>
    bool KeepsUnseenBody => false;

    /// <summary>His tick begins, once the humans he may see have been offered to him.</summary>
    /// <param name="tick">The tick.</param>
    void TickStarted(int tick)
    {
    }

    /// <summary>
    /// He is about to go idle, when nothing his script answered is waiting: the place to give him something to do
    /// (<see cref="GuardControl.ApplyActionsAt"/>). Given something, he does not go idle.
    /// </summary>
    void BeforeIdle()
    {
    }

    /// <summary>His tick ends, just before his body steps.</summary>
    void TickEnded()
    {
    }

    /// <summary>His script has answered an event with these actions, and they are applied.</summary>
    /// <param name="cause">The event answered, or <see langword="null"/>.</param>
    /// <param name="actions">The actions.</param>
    void ActionsApplied(AiEvent? cause, IReadOnlyList<AiProducedAction> actions)
    {
    }
}

/// <summary>A behaviour's say in what a guard senses and sees. <b>Not retail.</b></summary>
public interface IGuardSenses : IGuardBehaviour
{
    /// <summary>Gets an offset added to his eye, in world units: a lean. Every behaviour's are added.</summary>
    Vector3d EyeOffset => default;

    /// <summary>Gets a turn added to where his eye looks, in radians about the vertical: a glance. Every behaviour's are added.</summary>
    float EyeTurn => 0f;

    /// <summary>Asked about something he senses, before the game hands it to his script; each behaviour in turn, until one answers.</summary>
    /// <param name="stimulus">What he senses; a behaviour may move where he believes it came from, and the next is asked of that.</param>
    /// <returns><see langword="null"/> to let the next behaviour, then the game, go on with it; otherwise what the sensing method returns, and nothing more is done with it.</returns>
    bool? Stimulus(ref GuardStimulus stimulus) => null;

    /// <summary>Asked after each sweep of his eyes over a human, whether he counts as seen; each behaviour is handed the last one's verdict.</summary>
    /// <param name="sighting">What the sweep found, its <see cref="GuardSighting.Visible"/> the verdict so far.</param>
    /// <returns>Whether he is seen.</returns>
    bool Sighting(in GuardSighting sighting) => sighting.Visible;
}

/// <summary>A behaviour's say in how a guard moves: his step, his route and his patrol. <b>Not retail.</b></summary>
public interface IGuardMovement : IGuardBehaviour
{
    /// <summary>Shapes his body's step this tick, as the behaviours before it left it.</summary>
    /// <param name="shaped">The step the game would take, as shaped so far.</param>
    void ShapeStep(ref GuardStep shaped)
    {
    }

    /// <summary>Asked whether he may pass a waypoint of his route now, short of standing on it: a corner cut. He passes it if any behaviour lets him.</summary>
    /// <param name="waypoint">The waypoint he is heading for.</param>
    /// <param name="remaining">How many waypoints his route has left, this one included.</param>
    /// <returns>Whether he passes it now.</returns>
    bool PassesWaypoint(AiWaypoint waypoint, int remaining) => false;

    /// <summary>Where he steers for a waypoint; each behaviour is handed the point the last one chose.</summary>
    /// <param name="waypoint">The waypoint, its <see cref="AiWaypoint.Point"/> the point chosen so far.</param>
    /// <returns>The point he turns towards; the waypoint's by default.</returns>
    Vector3d SteerToward(AiWaypoint waypoint) => waypoint.Point;

    /// <summary>Asked before he begins the next command of his patrol; he holds if any behaviour holds him.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>Whether he holds where he is this tick instead.</returns>
    bool HoldsPatrolStop(int tick) => false;

    /// <summary>A command of his patrol has finished.</summary>
    /// <param name="tick">The tick.</param>
    void PatrolCommandFinished(int tick)
    {
    }
}

/// <summary>A behaviour's say in how a guard fights. <b>Not retail.</b></summary>
public interface IGuardCombat : IGuardBehaviour
{
    /// <summary>Gets a value indicating whether a round asked of his gun this tick may leave it. Every behaviour is asked, and it leaves only if all let it.</summary>
    bool MayFire => true;

    /// <summary>Asked each tick he fights, before the game's own fighting: the place for a fight of the behaviour's own. The first to fight for him has the tick.</summary>
    /// <param name="enemy">Where he last knew the enemy's feet, in world units.</param>
    /// <returns>Whether it fought for him this tick; if so no other behaviour is asked and the game's fighting does not run.</returns>
    bool Fight(Vector3d enemy) => false;

    /// <summary>His fight is over.</summary>
    void CombatEnded()
    {
    }
}

/// <summary>
/// One item of a guard's movement queue -- <c>0x452080</c>'s record: the point walked to (<c>+8</c>), whether a door stands on
/// the hop (<c>+32</c>), and the node that becomes the current one when it is reached (<c>+36</c>). <b>Not retail</b> as a public
/// type; the route is the game's.
/// </summary>
/// <param name="Point">The point walked to, in world units (<c>+8</c>).</param>
/// <param name="Node">The node that becomes the current one when it is reached, or -1 (<c>+36</c>).</param>
/// <param name="Door">Whether a door stands on the hop (<c>+32</c>).</param>
public readonly record struct AiWaypoint(Vector3d Point, int Node, bool Door);

/// <summary>What a guard senses, as <see cref="IGuardSenses.Stimulus"/> is asked about it. <b>Not retail.</b></summary>
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

/// <summary>A guard's body's step this tick, as <see cref="IGuardMovement.ShapeStep"/> may shape it. <b>Not retail.</b></summary>
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
