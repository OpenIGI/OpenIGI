using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Igi1.Ai;

/// <content>The per-tick behaviours the actions install: the patrol walker, the activate walk, panic, a look at an event, a fall, a stun and scripted fire.</content>
public sealed partial class Igi1HumanAi
{
    private readonly record struct ActivateTask(int TaskId, bool Run, int Node);

    /// <summary>What the soldier does with nothing on its stack.</summary>
    /// <remarks>
    /// A scripted AI with no action writes no channel and stands. A soldier without a script keeps the
    /// built-in fallback's one habit: it walks a route it was sent along.
    /// </remarks>
    private void RunWithoutAction()
    {
        _runningAction = null;
        if (HasScript || _route.Count == 0)
        {
            _scriptedRunGait = false;
            Activity = AiSoldierActivity.Idle;
            return;
        }

        Activity = AiSoldierActivity.Patrolling;
        Advance();
    }

    /// <summary>The combat action's per-tick: the arm, unless its target is gone.</summary>
    private void RunCombat(int tick)
    {
        // The target is dead: leave combat outright rather than keep firing at where it fell. The
        // unregister (0x4894E0) is immediate, and 0x44F410 releases the combat record whose target the
        // scan no longer registers, so this is too. The body is left as it is: the end (0x48D7D0) releases the
        // record and nothing else, and a soldier caught mid-burst runs its fire clip out. Follow, RePosition and
        // LookForTarget hold the tense run (slot 26) before they find the target gone, so a chaser stands up from
        // the run as its fight ends.
        if (!_enemyAlive)
        {
            if (IsPatrolMovementInstalled)
            {
                _tenseRunThisTick = true;
            }

            EndCombat();
            return;
        }

        Activity = AiSoldierActivity.Engaging;
        if (_combatEntryTick)
        {
            _combatEntryTick = false;
            return;
        }

        Fight(tick, _lastSeenFeet);
    }

    // The patrol arm's stance entry still to run: a tick that only installs the stance per-tick.
    private bool _combatEntryTick;

    /// <summary>
    /// The LookAtEvent per-tick (<c>0x48E0A0</c>): sets its deadline on its first tick, turns to the event
    /// through the turn-to-point entry, and completes once it is both aimed inside 2 degrees and past it.
    /// </summary>
    private void RunLookAtEvent(int tick)
    {
        Vector3d watched = _lookAtPoint!.Value;
        if (_lookAtDeadline < 0)
        {
            _lookAtDeadline = tick + _lookAtDurationTicks;
        }

        Activity = AiSoldierActivity.Idle;
        if (TurnTowards(watched, (float)AiGuardCombat.AimToleranceRadians) && tick > _lookAtDeadline)
        {
            _lookAtPoint = null;
        }
    }

    /// <summary>
    /// The Activate per-tick (<c>0x48E7D0</c>): walk to the panel's node, press on arrival, release the
    /// claims. Retail picks the gait from the action's run flag and presses through input channel 4 -- the
    /// port walks its ordinary route and invokes the activation directly; both are recorded stand-ins.
    /// </summary>
    private void RunActivate()
    {
        ActivateTask pressing = _activate!.Value;
        if (!StepTowardsNode(pressing.Node))
        {
            Activity = AiSoldierActivity.Patrolling;
            return;
        }

        bool mountedGun = false;
        if (pressing.TaskId == GunnerId
            && _gunnerCombat is not null
            && _stationaryGun?.Invoke(pressing.TaskId) is { } gun)
        {
            // StationaryGun's player interaction is a temporary input state. Calling its generic
            // BeginUse/CompleteUse pair here would mount a private player sentinel and immediately
            // release it, leaving the scripted gunner visibly beside an empty M2HB. The AI arm owns
            // the real occupant.
            _activateTask?.Invoke(pressing.TaskId);
            mountedGun = _gunnerCombat.MountImmediately(gun);
            if (mountedGun)
            {
                _seatedOnGunThisTick = true;
                SeatOn(gun);
            }
        }
        else
        {
            _activateTask?.Invoke(pressing.TaskId);
        }

        ReleaseActivateClaims();
        Activity = mountedGun ? AiSoldierActivity.Engaging : AiSoldierActivity.Idle;
    }

    /// <summary>The RunPanicking per-tick: run to the chosen node, and end on arrival.</summary>
    private void RunPanicking()
    {
        if (StepTowardsNode(_panicDestination))
        {
            _panicDestination = -1;
            Activity = AiSoldierActivity.Idle;
            return;
        }

        Activity = AiSoldierActivity.Panicking;
    }

    /// <summary>
    /// Runs one tick of the current patrol command — the human walker <c>0x48E920</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is the walker's: the crouch flag and the gait are applied first and unconditionally,
    /// because both outlive the command that set them, and only then does the switch run. A command that
    /// reports itself finished advances the cursor and clears the deadline; a kind the walker has no arm
    /// for returns <em>without</em> advancing, which is how a
    /// <see cref="AiPatrolCommandKind.SetSpeed"/> wedges a human patrol — reproduced rather than
    /// smoothed over.
    /// </para>
    /// </remarks>
    private void RunPatrolCommand(int tick)
    {
        AiPatrolRoute route = Route!;

        // 0x48E920 sets the gait from the cursor's flag before it looks at the command, so a RunTo changes it from
        // the tick after: the flag is the one the last movement command left, or a new cursor's walk.
        SetGait(route.IsWalking);
        if (route.Current is not { } command)
        {
            Activity = AiSoldierActivity.Idle;
            return;
        }

        // The crouch flag is written to the kneel channel at the top of every tick, before the command
        // runs: a Crouch command kneels from the tick after it, and the first tick of a walk still kneels.
        if (route.IsCrouching)
        {
            WriteChannel(AiCombatChannel.Kneel, 1f);
        }

        bool finished = route.Begin(tick);

        switch (command.Kind)
        {
            case AiPatrolCommandKind.Animation:
                // 0x48E920's stand action: the clip goes to the body through channel 3 once, which puts it in the
                // play-animation state (35) until the clip ends, and the command is done when the body is back in
                // its stand state -- its kneel, under a Crouch.
                Activity = AiSoldierActivity.Idle;
                RequestedAnimation = -1;
                if (route.AnimationRequested)
                {
                    WriteChannel(AiCombatChannel.PlayAnimation, command.Operand);
                    finished = false;
                }
                else
                {
                    finished = AiCombatChannels.IsInStance(
                        _type, route.IsCrouching ? AiStanceChannel.Kneeling : AiStanceChannel.Standing, CombatState);
                }

                break;

            case AiPatrolCommandKind.Delay:
                Activity = AiSoldierActivity.Idle;
                RequestedAnimation = -1;
                break;

            case AiPatrolCommandKind.WalkTo:
            case AiPatrolCommandKind.RunTo:
                RequestedAnimation = -1;
                finished = StepTowardsNode(command.Operand);
                break;

            case AiPatrolCommandKind.LookAtNode:
                Activity = AiSoldierActivity.Idle;
                RequestedAnimation = -1;
                finished = FaceNode(command.Operand);
                break;

            case AiPatrolCommandKind.Crouch:
                // Already applied by Begin; it is the one kind that never waits.
                break;

            case AiPatrolCommandKind.Quit:
                Activity = AiSoldierActivity.Idle;
                RequestedAnimation = -1;
                Route = null;
                return;

            default:
                // No arm in the human walker: the cursor does not advance. SetSpeed is the only kind that
                // reaches here, and the patrol stays on it.
                Activity = AiSoldierActivity.Idle;
                return;
        }

        if (finished)
        {
            _animationFinished = false;
            route.Advance();
        }
    }

    private void BeginFallFlat(double seconds)
    {
        if (_graph?.NodeAt(CurrentNode) is { } node
            && (node.Criteria & GraphNodeCriteria.Stair) != 0)
        {
            return;
        }

        // 0x44D2C0 skips the action when the current event's subject (its +44 task) stands nearer than the AI's own
        // lie-down distance (+15800, the row's "Combat liedown min distance"), origin to origin. A record that names
        // no subject skips the test, as the null-handle arm does.
        if (_currentEventSubject != 0
            && ResolveCurrentEventSubjectOrigin() is { } subjectOrigin
            && Vector3d.Distance(subjectOrigin, OriginPosition) < (CombatParameters?.LieDownMinimumDistanceInUnits ?? 0.0))
        {
            return;
        }

        _fallFlatDurationTicks = Math.Max(0, (int)(seconds * 30.0));
        _fallFlatDeadline = -2;
        Activity = AiSoldierActivity.FallenFlat;
    }

    // The origin (Human+32) of the human the current event names, as 0x44D2C0 reads it off the record's +44 task.
    private Vector3d? ResolveCurrentEventSubjectOrigin()
    {
        // The player is -1 in records and candidates alike; by the time an event is dispatched Tick has offered
        // every human's current body.
        if (_tracked.TryGetValue(_currentEventSubject, out Igi1AiTrackedHuman? human))
        {
            AiDetectionCandidate candidate = human.Candidate;
            return candidate.Origin ?? new Vector3d(
                candidate.Feet.X,
                candidate.Feet.Y,
                candidate.Feet.Z + (candidate.Crouching ? HumanOriginCrouchedInUnits : HumanOriginStandingInUnits));
        }

        if (_locateTask is not null && _locateTask(_currentEventSubject, out Vector3d position))
        {
            return position;
        }

        return null;
    }

    private void RunFallFlat(int tick)
    {
        if (_fallFlatDeadline < 0)
        {
            _fallFlatDeadline = tick + _fallFlatDurationTicks;
        }

        _channelWrites.Clear();
        WriteChannel(AiCombatChannel.LieDown, 1f);
        Combat = new AiCombatCommand();

        if (tick <= _fallFlatDeadline)
        {
            return;
        }

        _fallFlatDeadline = -1;
        Activity = AiSoldierActivity.Idle;
    }

    private void BeginStunned(double seconds)
    {
        _stunnedDurationTicks = Math.Max(0, (int)(seconds * 30.0));
        _stunnedDeadline = -1;
        RequestedAnimation = 38;
        Activity = AiSoldierActivity.Stunned;
    }

    private void RunStunned(int tick)
    {
        if (_stunnedDeadline < 0)
        {
            _stunnedDeadline = tick + _stunnedDurationTicks;
        }

        _channelWrites.Clear();
        Combat = new AiCombatCommand();
        if (tick <= _stunnedDeadline)
        {
            WriteChannel(AiCombatChannel.PlayAnimation, 38.0f);
            return;
        }

        _stunnedDeadline = -1;
        _stunnedDurationTicks = -1;
        RequestedAnimation = -1;
        Activity = AiSoldierActivity.Idle;
    }

    private void BeginFireAtTask(AiProducedAction action)
    {
        int taskId = action.Argument(0);
        BeginScriptedFire(
            () => _locateTask is not null && _locateTask(taskId, out Vector3d position) ? position : null,
            action);
    }

    private void BeginFireAtNode(AiProducedAction action)
    {
        int nodeId = action.Argument(0);
        BeginScriptedFire(() => _graph?.NodeAt(nodeId)?.Position, action);
    }

    private void BeginScriptedFire(Func<Vector3d?> locateTarget, AiProducedAction action)
    {
        if (locateTarget() is null || CombatParameters is null)
        {
            return;
        }

        // 0x44D580 / 0x44D630: the time and the wait are seconds the script names, made ticks at once.
        _scriptedFire = new Igi1ScriptedFire
        {
            LocateTarget = locateTarget,
            Bursts = new AiGuardCombat(CombatParameters, _random.NextDouble) { DrawsStandDelay = false },
            RemainingTicks = (int)(action.Real(1) * 30.0f),
            BurstDelayTicks = (int)(action.Real(2) * 30.0f),
            ShowReload = action.Argument(3) != 0,
        };
        Body.Reset();
        Activity = AiSoldierActivity.FiringAtTask;
    }

    /// <summary>One tick of FireAtNode / FireAtTask — <c>0x48ECC0</c> / <c>0x48EE00</c>.</summary>
    /// <remarks>
    /// The time runs down first and ends the action. An empty clip reloads through the input slot and fires nothing
    /// that tick, or refills at once and sets the next burst up afresh. A burst is set up (<c>0x48B9C0</c>, at full
    /// accuracy) the first tick there is none, then -- once the wait armed after the last burst has run out, and with
    /// no turning while it does -- fired through combat's own fire child (<c>0x48D8E0</c>); its end arms the wait again.
    /// </remarks>
    private void RunScriptedFire()
    {
        Igi1ScriptedFire firing = _scriptedFire!;
        _channelWrites.Clear();
        Combat = default;
        if (firing.RemainingTicks-- < 0 || _weapon is null)
        {
            EndScriptedFire();
            return;
        }

        AiCombatWeaponState weapon = _weapon();
        if (weapon.AmmoInClip <= 0)
        {
            if (firing.ShowReload)
            {
                Combat = new AiCombatCommand { PressReload = true };
                WriteRawInputSlot(8, 1f);
                return;
            }

            // Weapon_UseAmmo (0x4F2350): the clip refills now, and the burst is set up again.
            Combat = new AiCombatCommand { ReloadWeaponDirectly = true };
            firing.BurstSetUp = false;
            return;
        }

        if (firing.LocateTarget() is not { } target)
        {
            EndScriptedFire();
            return;
        }

        AiCombatSense sense = new(
            HasDetectionSlot: true,
            InCone: true,
            Visible: true,
            TargetPosition: target,
            DistanceToTargetInUnits: Vector3d.Distance(Position, target),
            LocomotionState: AiCombatChannels.StateOf(_type, AiStanceChannel.Standing),
            AmmoInClip: weapon.AmmoInClip,
            Category: weapon.Category,
            ClipSize: weapon.ClipSize,
            BurstSize: weapon.BurstSize,
            AimersOnTarget: 0);

        if (!firing.BurstSetUp)
        {
            AiCombatBurst burst = firing.Bursts.StartScriptedBurst(sense, Vision.Primary.Length);
            HitProbability = burst.HitProbability;
            firing.BurstSetUp = true;
        }

        if (firing.Wait >= 0)
        {
            firing.Wait--;
            return;
        }

        AiCombatCommand command = firing.Bursts.FireScriptedBurst(sense, out bool done);
        Combat = command;
        if (command.FirePose)
        {
            WriteChannel(AiCombatChannel.Fire, 1f);
        }

        if (command.TurnTo is { } point)
        {
            firing.Bursts.ReportAimed(TurnTowards(point, (float)AiGuardCombat.AimToleranceRadians));
        }

        if (done)
        {
            firing.BurstSetUp = false;
            firing.Wait = firing.BurstDelayTicks;
        }
    }

    private void EndScriptedFire()
    {
        _scriptedFire = null;
        Combat = new AiCombatCommand();
        _channelWrites.Clear();
        if (Activity == AiSoldierActivity.FiringAtTask)
        {
            Activity = AiSoldierActivity.Idle;
        }
    }

    private void BeginRunPanicking(double distanceMetres)
    {
        if (_graph is null || _currentEventPosition is not { } eventPosition)
        {
            return;
        }

        int? destination = AiRunPanicking.FindDestination(
            _graph, Position, eventPosition, distanceMetres);
        if (destination is null || !GoTo(destination.Value))
        {
            return;
        }

        _panicDestination = destination.Value;
        Activity = AiSoldierActivity.Panicking;
    }

    private void BeginActivate(int taskId, bool run)
    {
        if (_claims is null || _locateTask is null)
        {
            return;
        }

        // Already claimed by somebody -- or, for this AI's own alarm trigger, the CONTROL claimed --
        // means the action never starts (0x48E620's early exits).
        if (_claims.Contains(taskId))
        {
            return;
        }

        bool isMyAlarmTrigger = taskId == AlarmTriggerId && AlarmTriggerId != -1;
        if (!_locateTask(taskId, out Vector3d position))
        {
            // "Level QTask ID #%d not defined (AI Activate)" -- the original warns and gives up.
            return;
        }

        int node = NearestNode(position);
        if (node < 0)
        {
            return;
        }

        _claims.Claim(taskId);
        _activateClaimA = taskId;
        if (isMyAlarmTrigger && AlarmControlId != -1)
        {
            _claims.Claim(AlarmControlId);
            _activateClaimB = AlarmControlId;
        }

        GoTo(node);
        _activate = new ActivateTask(taskId, run, node);
        _activateTaskId = taskId;
        Activity = AiSoldierActivity.Patrolling;
    }

    /// <summary>The start shared by WalkToNode, RunToNode and MoveToEvent: name the node to walk to.</summary>
    /// <param name="node">The destination, or a negative id when there is none, which leaves nothing to run.</param>
    private void BeginGoTo(int node)
    {
        _goToNode = node >= 0 && _graph?.NodeAt(node) is not null ? node : -1;
        if (_goToNode >= 0)
        {
            Activity = AiSoldierActivity.Patrolling;
        }
    }

    private void ReleaseActivateClaims()
    {
        if (_activateClaimA != -1)
        {
            _claims?.Release(_activateClaimA);
            _activateClaimA = -1;
        }

        if (_activateClaimB != -1)
        {
            _claims?.Release(_activateClaimB);
            _activateClaimB = -1;
        }

        _activate = null;
        _activateTaskId = -1;
    }

    /// <summary>SetCombat's start, <c>0x48E560</c>: post COMBAT to this AI's own queue, about the current event.</summary>
    private void PostCombatEvent()
    {
        // 0x48E560 does NOT install a combat arm. It synthesizes an AIEVENT_COMBAT record
        // carrying the triggering event's position, posts it to this AI's OWN queue
        // (sub_450D50) and ends itself at once (sub_450FF0). The arm arrives on the next
        // tick, when that COMBAT event dispatches and the script's own COMBAT branch runs --
        // which is also where guard.qvm keeps its run-to-the-alarm logic, and what places
        // AIAction_Combat on the action stack at COMBAT's own priority position so nothing
        // ranked below it (further gunshots, footsteps) can interrupt the engagement.
        // Treating SetCombat as a direct combat install skipped all three: every heard
        // event re-entered combat, wiped the hunt route, and the guard never went anywhere.
        Events.Post(
            new AiEventRecord(
                AiEvent.Combat,
                -1,
                AiEventFilter.None,
                0.0f,
                _currentEventPosition ?? TargetMark,
                _currentEventSource,
                _currentEventSubject),
            owner: RuntimeId,
            worldTests: null);
    }

    /// <summary>LookAtEvent's start, <c>0x44D870</c>: take the event's position and the hold.</summary>
    /// <param name="action">The produced action, whose real 0 is the hold in seconds.</param>
    private void BeginLookAtEvent(AiProducedAction action)
    {
        // 0x44D870: copy the event's position, arg 0 = the hold in SECONDS (a real), then
        // the behaviour turns to it (0x48E0A0, instant turn to 2 degrees) and holds until
        // aimed AND expired. This is a guard "noticing" a shot it heard.
        if (_currentEventPosition is { } noticed)
        {
            _lookAtPoint = noticed;
            _lookAtDurationTicks = (int)(action.Real(0) * 30.0f);
            _lookAtDeadline = -1;
        }
    }

    /// <summary>MoveToEvent's start, <c>0x44D7D0</c>: walk to the node nearest the event.</summary>
    private void BeginMoveToEvent()
    {
        // 0x44D7D0 -> 0x48DFE0: the nearest-node query (sub_5096F0) against the EVENT's
        // position, then walk there -- a guard investigating the noise.
        if (_currentEventPosition is { } toInvestigate)
        {
            BeginGoTo(NearestNode(toInvestigate));
        }
    }

    /// <summary>PlayAnimation's start: request the scripted one-shot clip.</summary>
    /// <param name="action">The produced action, whose argument 0 is the clip.</param>
    private void BeginPlayAnimation(AiProducedAction action)
    {
        // The scripted one-shot clip: the id the script chose (usually what
        // GetAnimationToPlay returned). Cleared when the renderer reports the clip done.
        RequestedAnimation = action.Argument(0);
        _animationFinished = false;
        _scriptedAnimation = true;
    }

    /// <summary>PlaySound's one run, <c>0x48DF80</c>: hand the sound to the owning human's audio.</summary>
    /// <param name="action">The produced action: the definition name and the attach flag.</param>
    private void PlayScriptSound(AiProducedAction action)
    {
        // 0x48DF80 plays once and completes the action in the same run. When the flag is
        // set the sound is attached to Human+20; otherwise it is created globally and its
        // position is set to HumanAI+32.
        string sound = action.Text(0);
        if (!string.IsNullOrEmpty(sound))
        {
            _scriptSound?.Invoke(sound, action.Argument(1) != 0);
        }
    }

    /// <summary>KickGrenade's start: with a grenade handle, settle on the nearest node and do nothing more.</summary>
    private void BeginKickGrenade()
    {
        // The wrapper refuses to install without a grenade event handle. With one, IGI1
        // records its nearest node and retains a no-op runner (0x48E250); preserve that
        // dormant behavior rather than inventing a kick animation or impulse.
        if (_hasKickableGrenade)
        {
            _route.Clear();
            DestinationNode = NearestNode(Position);
        }
    }
}
