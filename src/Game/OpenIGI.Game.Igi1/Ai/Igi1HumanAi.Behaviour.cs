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
