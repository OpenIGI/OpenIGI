using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Igi1.Ai;

/// <content>
/// The behaviours mods attach to the guard (<see cref="IGuardBehaviour"/>), and the operations <see cref="GuardControl"/> hands
/// them. With none attached nothing below is called, and none of the operations changes what the game's own code does.
/// <b>Not retail.</b>
/// </content>
public sealed partial class Igi1HumanAi
{
    private GuardBehaviourSet? _behaviours;
    private GuardControl? _control;

    // What the tick was offered: the humans themselves, or by reference (then read from what he tracks, on the first ask).
    private IReadOnlyList<AiDetectionCandidate> _offeredCandidates = [];
    private bool _offeredByReference;
    private readonly List<AiDetectionCandidate> _offeredRead = [];
    private bool _offeredReadThisTick;

    /// <summary>Gets the behaviours attached to him, or <see langword="null"/> while he is the game's alone.</summary>
    internal GuardBehaviourSet? Behaviours => _behaviours is { Any: true } attached ? attached : null;

    /// <summary>Gets the first behaviour a mod has attached to him that is a <typeparamref name="T"/>. <b>Not retail.</b></summary>
    /// <typeparam name="T">What is looked for: a mod's own behaviour type, or an interface of its own.</typeparam>
    /// <returns>It, or <see langword="null"/>.</returns>
    public T? BehaviourOf<T>()
        where T : class => _behaviours?.Find<T>();

    /// <summary>Gets the hit chance the burst his fight last started was given, before any change a behaviour made to it. <b>Not retail.</b></summary>
    public float BurstHitProbability { get; private set; } = 1f;

    /// <summary>Attaches what a factory makes for him, under the factory; it may decline him.</summary>
    /// <param name="factory">The factory.</param>
    /// <param name="logger">Where a factory or a behaviour that throws is reported.</param>
    internal void Attach(Func<GuardControl, IGuardBehaviour?> factory, IOpenIgiLogger logger)
    {
        _control ??= new GuardControl(this);
        if (!Contained.Run(logger, "Ai.Behaviour", "A guard behaviour's factory", (factory, Control: _control), static f => f.factory(f.Control), out IGuardBehaviour? made)
            || made is not { } behaviour)
        {
            return;
        }

        bool sensed = _behaviours?.HasSenses == true;
        (_behaviours ??= new GuardBehaviourSet(logger)).Add(factory, behaviour);

        // The first say in what he sees takes up the sweep where it stands, so a human he sees now stays seen until the
        // sweep itself says otherwise.
        if (!sensed && _behaviours.HasSenses)
        {
            foreach (Igi1AiTrackedHuman human in _tracked.Values)
            {
                human.SweepVisible = human.Slot.IsVisible;
            }
        }
    }

    /// <summary>Lets go of what a factory attached to him.</summary>
    /// <param name="factory">The factory.</param>
    internal void Detach(Func<GuardControl, IGuardBehaviour?> factory) => _behaviours?.Remove(factory);

    /// <summary>Gets the route he is walking, waypoint by waypoint.</summary>
    internal IReadOnlyList<AiWaypoint> Waypoints => _route;

    /// <summary>Gets or sets the point he looks at until he gets there or is told otherwise, or <see langword="null"/> for none.</summary>
    internal Vector3d? LookingAt
    {
        get => _lookAtPoint;
        set => _lookAtPoint = value;
    }

    /// <summary>Gets the name of the action he is running, or <see langword="null"/>.</summary>
    internal string? RunningActionName => _runningAction;

    /// <summary>Gets the nav graph he walks, or <see langword="null"/>.</summary>
    internal GraphFile? Graph => _graph;

    /// <summary>Gets which graph nodes his side's soldiers have claimed, or <see langword="null"/>.</summary>
    internal AiNodeOccupancy? Occupancy => _occupancy;

    /// <summary>Gets whether a move of the patrol fight's own is under way: lying prone, a one-shot (a grenade, a blow) or a roll.</summary>
    internal bool PatrolMoveUnderWay => _patrolProne || _patrolOneShotChild is not null || _patrolRollChild is not null;

    /// <summary>Gets the humans offered to his eyes this tick, read once a tick on the first ask; the list is refilled each tick.</summary>
    internal IReadOnlyList<AiDetectionCandidate> OfferedHumans
    {
        get
        {
            if (!_offeredByReference)
            {
                return _offeredCandidates;
            }

            if (!_offeredReadThisTick)
            {
                _offeredRead.Clear();
                foreach (Igi1AiTrackedHuman human in _tracked.Values)
                {
                    _offeredRead.Add(human.Candidate);
                }

                _offeredReadThisTick = true;
            }

            return _offeredRead;
        }
    }

    // A new tick's offer: what was read of the last is forgotten.
    private void TakeOffer(bool byReference, IReadOnlyList<AiDetectionCandidate> candidates)
    {
        (_offeredByReference, _offeredCandidates) = (byReference, candidates);
        _offeredReadThisTick = false;
    }

    /// <summary>Gets whether he could see from one point to another with nothing in between: his own line of sight.</summary>
    /// <param name="from">From, in world units.</param>
    /// <param name="to">To, in world units.</param>
    /// <param name="seen">The human looked for, whom the line steps past (his own cover, a gun he mans), or <see langword="null"/>.</param>
    /// <returns>Whether the line is clear; true when he has no way to tell.</returns>
    internal bool CanSeeBetween(Vector3d from, Vector3d to, int? seen = null) => _sight is null || _sight(from, to, seen);

    /// <summary>Fires a burst at a point this tick, as his fight fires at an enemy he can see; nothing without a weapon.</summary>
    /// <param name="point">Where he aims, in world units.</param>
    internal void FireBurstAt(Vector3d point)
    {
        if (_weapon is { } weapon)
        {
            RunSharedSubAction(point, weapon(), point);
        }
    }

    /// <summary>Runs his own fire at his target this tick: aim, bursts and reloads, as his fight fires; nothing without a weapon.</summary>
    /// <param name="enemy">Where the enemy is, in world units.</param>
    internal void FireAtEnemy(Vector3d enemy)
    {
        if (_weapon is { } weapon)
        {
            RunSharedSubAction(enemy, weapon());
        }
    }

    /// <summary>
    /// Applies actions as his script's answer to an event at a point (<c>AIAction_MoveToEvent</c>, <c>AIAction_LookAtEvent</c>
    /// and the like read where the event was). The event's position is his for the actions alone: what it was before is put
    /// back, whatever they do.
    /// </summary>
    /// <param name="eventPosition">Where the event was, in world units.</param>
    /// <param name="actions">The actions, as <see cref="ApplyActions"/> takes them.</param>
    internal void ApplyActionsAt(Vector3d eventPosition, IReadOnlyList<AiProducedAction> actions)
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

    /// <summary>Has him run, or walk, to an event's position from now, as a script's run flag does.</summary>
    /// <param name="run">Whether he runs.</param>
    internal void SetEventGait(bool run) => _scriptedRunGait = run;

    /// <summary>Stands him idle where he is with no animation asked for, until something gives him another.</summary>
    internal void HoldStill()
    {
        Activity = AiSoldierActivity.Idle;
        RequestedAnimation = -1;
    }

    /// <summary>Clears what his fight asked of his body this tick, so none of it reaches his body: no channel, no trigger, no turn.</summary>
    internal void ClearFightCommand()
    {
        _channelWrites.Clear();
        Combat = default;
    }

    /// <summary>Steps him along his route at a fight's tense run this tick, as the game's own fight moves him.</summary>
    internal void RunAlongRoute()
    {
        _tenseRunThisTick = true;
        SetGait(walking: false);
        AdvanceInCombat();
    }

    /// <summary>Stops him where he is: his route and his destination dropped, and with them any move of his fight's own along them.</summary>
    internal void StopRoute()
    {
        _route.Clear();
        DestinationNode = -1;
        _movingInCombat = false;
    }

    /// <summary>Pulls his trigger this tick at his target, as a soldier on the move fires: the command is the trigger alone, his aim on the target.</summary>
    /// <param name="hitProbability">The chance each round hits.</param>
    internal void PullTrigger(float hitProbability)
    {
        Combat = new AiCombatCommand { PullTrigger = true };
        Target?.Aim(this, AiAimMode.Aiming);
        HitProbability = hitProbability;
    }

    /// <summary>Sets the chance each round now asked of his gun hits.</summary>
    /// <param name="value">The chance.</param>
    internal void SetHitProbability(float value) => HitProbability = value;

    /// <summary>
    /// Drops the burst under way, and the moves of the game's own fight he had latched (a reposition, a look for the target), so
    /// the next tick's fight starts from him standing ready.
    /// </summary>
    internal void CancelBurst()
    {
        _combat?.CancelChild();
        _repositionLatched = false;
        _lookForTargetLatched = false;
    }

    /// <summary>
    /// Has a patrol throw his grenade, as his fight's own one-shot does: the throw is the tick's only command, and the game's
    /// fight runs it while it lasts (<see cref="PatrolMoveUnderWay"/>).
    /// </summary>
    /// <returns>Whether he will: false for a soldier with no patrol fight or no grenade.</returns>
    internal bool ThrowGrenade()
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

    /// <summary>Drops a grenade throw asked for that has not finished; any other one-shot under way is left alone.</summary>
    internal void ForgetGrenadeThrow()
    {
        if (_patrolOneShotChild == AiPatrolAction.ThrowGrenade)
        {
            _patrolOneShotChild = null;
        }
    }

    // Asks the behaviours about a stimulus; null to go on as the game does.
    private bool? Sensed(GuardStimulusKind kind, ref Vector3d position, int source, double range = 0.0, AiEvent? type = null, bool sameSide = false, Vector3d? from = null)
    {
        if (_behaviours is not { HasSenses: true } behaviours)
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
        bool? answer = behaviours.Stimulus(ref stimulus);
        position = stimulus.Position;
        return answer;
    }
}
