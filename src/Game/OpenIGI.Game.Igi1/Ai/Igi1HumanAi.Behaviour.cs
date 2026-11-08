using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Igi1.Ai;

/// <content>
/// A mod's say in the guard (<see cref="IGuardBehaviour"/>), and what a mod may tell him to do. With no behaviour attached
/// none of the calls below happen, and the actions are what the game's own code already does. <b>Not retail.</b>
/// </content>
public sealed partial class Igi1HumanAi
{
    /// <summary>Gets a mod's say in this guard, or <see langword="null"/> while he is the game's alone. <b>Not retail.</b></summary>
    public IGuardBehaviour? Behaviour { get; private set; }

    /// <summary>Gets the route he is walking, waypoint by waypoint, read only. <b>Not retail.</b></summary>
    public IReadOnlyList<AiWaypoint> Waypoints => _route;

    // What attaches a behaviour to him, and to the one who stands for him after a respawn.
    internal Func<Igi1HumanAi, IGuardBehaviour?>? BehaviourFactory
    {
        get => _behaviourFactory;
        set
        {
            _behaviourFactory = value;
            Behaviour = value?.Invoke(this);
        }
    }

    private Func<Igi1HumanAi, IGuardBehaviour?>? _behaviourFactory;

    /// <summary>Writes one of his combat channels for this tick, as his fight does: a stance, a lean, the trigger. <b>Not retail</b> as a public call.</summary>
    /// <param name="channel">The channel.</param>
    /// <param name="value">Its value.</param>
    public void SetCombatChannel(AiCombatChannel channel, float value) => WriteChannel(channel, value);

    /// <summary>Has him walk rather than run, or run rather than walk, from now. <b>Not retail</b> as a public call.</summary>
    /// <param name="walking">Whether he walks.</param>
    public void SetWalking(bool walking) => SetGait(walking);

    /// <summary>Holds his posture where it is this tick, as his fight does between moves. <b>Not retail</b> as a public call.</summary>
    public void KeepPosture() => HoldPosture();

    /// <summary>Has him look at a point until he gets there or is told otherwise, or stop looking with <see langword="null"/>. <b>Not retail</b> as a public call.</summary>
    /// <param name="point">The point, in world units.</param>
    public void LookAt(Vector3d? point) => _lookAtPoint = point;

    /// <summary>Gets whether an action of his script, by its name, is the one running. <b>Not retail</b> as a public call.</summary>
    /// <param name="name">The action's name.</param>
    /// <returns>Whether it runs.</returns>
    public bool IsRunning(string name) => IsActionActive(name);

    /// <summary>Gets the graph node nearest a point, or -1 with no graph. <b>Not retail</b> as a public call.</summary>
    /// <param name="point">The point, in world units.</param>
    /// <returns>The node.</returns>
    public int NodeNearest(Vector3d point) => NearestNode(point);

    /// <summary>Has him fire a burst at a point this tick, as his fight fires at an enemy he can see. <b>Not retail</b> as a public call.</summary>
    /// <param name="point">Where he aims, in world units.</param>
    public void FireBurstAt(Vector3d point)
    {
        if (_weapon is { } weapon)
        {
            RunSharedSubAction(point, weapon(), point);
        }
    }

    /// <summary>Draws from his own stream of chance, so a mod's choices for him replay as the game's do. <b>Not retail</b> as a public call.</summary>
    /// <param name="bound">One past the largest value wanted.</param>
    /// <returns>A value from nought to one less than <paramref name="bound"/>.</returns>
    public int NextRandom(int bound) => NextBounded(bound);

    /// <summary>Gets whether he could see from one point to another, with nothing in between: his own line of sight. <b>Not retail</b> as a public call.</summary>
    /// <param name="from">From, in world units.</param>
    /// <param name="to">To, in world units.</param>
    /// <returns>Whether the line is clear; true when he has no way to tell.</returns>
    public bool CanSeeBetween(Vector3d from, Vector3d to) => _sight is null || _sight(from, to, null);

    /// <summary>
    /// Applies actions as his script's answer to an event at a point (<c>AIAction_MoveToEvent</c>, <c>AIAction_LookAtEvent</c> and the
    /// like read where the event was): a mod's own reason to go somewhere or look at something. <b>Not retail</b> as a public call.
    /// </summary>
    /// <param name="eventPosition">Where the event was, in world units.</param>
    /// <param name="actions">The actions, as <see cref="ApplyActions"/> takes them.</param>
    public void ApplyActionsAt(Vector3d eventPosition, IReadOnlyList<AiProducedAction> actions)
    {
        Vector3d? before = _currentEventPosition;
        _currentEventPosition = eventPosition;
        try
        {
            ApplyActions(actions);
        }
        finally
        {
            _currentEventPosition = before;
        }
    }

    /// <summary>Gets the point he is looking at, or <see langword="null"/> while he looks nowhere in particular. <b>Not retail</b> as a public call.</summary>
    public Vector3d? LookingAt => _lookAtPoint;

    /// <summary>Has him run, or walk, to an event's position, as a script's run flag does. <b>Not retail</b> as a public call.</summary>
    /// <param name="run">Whether he runs.</param>
    public void SetEventGait(bool run) => _scriptedRunGait = run;

    /// <summary>Stands him idle where he is, with no animation asked for: a pause of a mod's own. <b>Not retail</b> as a public call.</summary>
    public void HoldStill()
    {
        Activity = AiSoldierActivity.Idle;
        RequestedAnimation = -1;
    }

    /// <summary>Gets the name of the action he is running, or <see langword="null"/>. <b>Not retail</b> as a public call.</summary>
    public string? RunningActionName => _runningAction;

    /// <summary>Gets the humans offered to his eyes this tick: who he could see, before his sweep. <b>Not retail</b> as a public call.</summary>
    /// <remarks>Offered by reference, each is described as this reads it, so only a mod that asks pays for the reading.</remarks>
    public IReadOnlyList<AiDetectionCandidate> OfferedHumans =>
        _offeredByReference ? [.. _tracked.Values.Select(human => human.Candidate)] : _offeredCandidates;

    // What the tick was offered: the humans themselves, or by reference (then read from what he tracks).
    private IReadOnlyList<AiDetectionCandidate> _offeredCandidates = [];
    private bool _offeredByReference;

    /// <summary>Gets whether he could see from one point to another, looking for a given human, with nothing in between. <b>Not retail</b> as a public call.</summary>
    /// <param name="from">From, in world units.</param>
    /// <param name="to">To, in world units.</param>
    /// <param name="seen">The human looked for, whom his line of sight steps past (his own cover, a gun he mans).</param>
    /// <returns>Whether the line is clear; true when he has no way to tell.</returns>
    public bool CanSeeBetween(Vector3d from, Vector3d to, int seen) => _sight is null || _sight(from, to, seen);

    /// <summary>Gets the nav graph he walks, read only, or <see langword="null"/>. <b>Not retail</b> as a public call.</summary>
    public GraphFile? Graph => _graph;

    /// <summary>Gets which graph nodes his side's soldiers have claimed, or <see langword="null"/>. <b>Not retail</b> as a public call.</summary>
    public AiNodeOccupancy? Occupancy => _occupancy;

    /// <summary>Gets the hit chance the burst his fight last started was given, before any change a mod made to it. <b>Not retail.</b></summary>
    public float BurstHitProbability { get; private set; } = 1f;

    /// <summary>Gets whether a move of the patrol fight's own is under way: lying prone, a one-shot (a grenade, a blow) or a roll. <b>Not retail</b> as a public call.</summary>
    public bool PatrolMoveUnderWay => _patrolProne || _patrolOneShotChild is not null || _patrolRollChild is not null;

    /// <summary>Ends his fight, as the game's own does when it gives up on the enemy. <b>Not retail</b> as a public call.</summary>
    public void EndFight() => EndCombat();

    /// <summary>Clears what his fight asks of his body this tick: no channel written, no trigger, no turn asked for. <b>Not retail</b> as a public call.</summary>
    public void ClearFightCommand()
    {
        _channelWrites.Clear();
        Combat = default;
    }

    /// <summary>Steps him along his route at a fight's tense run, as the game's own fight moves him. <b>Not retail</b> as a public call.</summary>
    public void RunAlongRoute()
    {
        _tenseRunThisTick = true;
        SetGait(walking: false);
        AdvanceInCombat();
    }

    /// <summary>Stops him where he is: his route and his destination dropped. <b>Not retail</b> as a public call.</summary>
    public void StopRoute()
    {
        _route.Clear();
        DestinationNode = -1;
        _movingInCombat = false;
    }

    /// <summary>Turns him toward a point by his own turn rate. <b>Not retail</b> as a public call.</summary>
    /// <param name="point">The point, in world units.</param>
    /// <param name="tolerance">How near, in radians, counts as facing it.</param>
    /// <param name="level">Whether he turns about the vertical alone, his view kept level.</param>
    /// <returns>Whether he faces it within the tolerance.</returns>
    public bool TurnToward(Vector3d point, float tolerance, bool level = false) => TurnTowards(point, tolerance, level);

    /// <summary>Gets whether a point lies in the narrow cone a soldier on the move fires within. <b>Not retail</b> as a public call.</summary>
    /// <param name="point">The point, in world units.</param>
    /// <returns>Whether it does.</returns>
    public bool FacesForMovingFire(Vector3d point) => FacesForFollowFire(point);

    /// <summary>Gets the point a soldier aims at on a human whose feet stand at a point. <b>Not retail</b> as a public call.</summary>
    /// <param name="feet">The human's feet, in world units.</param>
    /// <param name="crouching">Whether the human crouches.</param>
    /// <returns>The aim point.</returns>
    public static Vector3d AimPointOn(Vector3d feet, bool crouching) => AimPointOf(feet, crouching);

    /// <summary>Pulls his trigger this tick at his target with the hit chance given, as a soldier on the move fires. <b>Not retail</b> as a public call.</summary>
    /// <param name="hitProbability">The chance each round hits.</param>
    public void PullTrigger(float hitProbability)
    {
        Combat = new AiCombatCommand { PullTrigger = true };
        Target?.Aim(this, AiAimMode.Aiming);
        HitProbability = hitProbability;
    }

    /// <summary>Runs his own fire at his target this tick: aim, bursts and reloads, as his fight fires. <b>Not retail</b> as a public call.</summary>
    /// <param name="enemy">Where the enemy is, in world units.</param>
    public void FireAtEnemy(Vector3d enemy)
    {
        if (_weapon is { } weapon)
        {
            RunSharedSubAction(enemy, weapon());
        }
    }

    /// <summary>Sets the chance each round now asked of his gun hits. <b>Not retail</b> as a public call.</summary>
    /// <param name="value">The chance.</param>
    public void SetHitProbability(float value) => HitProbability = value;

    /// <summary>Drops the burst under way, and whatever move of the game's own fight he had latched, so he stands ready. <b>Not retail</b> as a public call.</summary>
    public void CancelBurst()
    {
        _combat?.CancelChild();
        _repositionLatched = false;
        _lookForTargetLatched = false;
    }

    /// <summary>Has a patrol throw his grenade, as his fight's own one-shot does: the game's fight runs it while it lasts (<see cref="PatrolMoveUnderWay"/>). <b>Not retail</b> as a public call.</summary>
    /// <returns>Whether he will: false for a soldier with no patrol fight or no grenade.</returns>
    public bool ThrowGrenade()
    {
        if (_patrol is null || !HasGrenade)
        {
            return false;
        }

        _channelWrites.Clear();
        _patrolOneShotChild = AiPatrolAction.ThrowGrenade;
        WriteChannel(AiCombatChannel.ThrowGrenade, 1f);
        return true;
    }

    /// <summary>Drops a grenade throw asked for that has not finished. <b>Not retail</b> as a public call.</summary>
    public void ForgetGrenadeThrow()
    {
        if (_patrolOneShotChild == AiPatrolAction.ThrowGrenade)
        {
            _patrolOneShotChild = null;
        }
    }

    // Asks the behaviour about a stimulus; null to go on as the game does.
    private bool? Sensed(GuardStimulusKind kind, ref Vector3d position, int source, double range = 0.0, AiEvent? type = null, bool sameSide = false, Vector3d? from = null)
    {
        if (Behaviour is not { } behaviour)
        {
            return null;
        }

        GuardStimulus stimulus = new()
        {
            Kind = kind,
            Position = position,
            Source = source,
            Range = range,
            Event = type,
            SameSide = sameSide,
            From = from,
        };
        bool? answer = behaviour.Stimulus(ref stimulus);
        position = stimulus.Position;
        return answer;
    }
}
