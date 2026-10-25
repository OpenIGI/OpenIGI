using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Igi1.Ai;

/// <content>The action stack: the script's dispatch, installing what it produced, and each action's start, run and end functions.</content>
public sealed partial class Igi1HumanAi
{
    /// <summary>
    /// Ends the actions the alarm is meant to have installed — <c>AIFunction_RemoveAlarmActions</c>
    /// (<c>0x44DBC0</c>), which <c>guard.qsc</c> calls on <c>ALARMOFF</c>.
    /// </summary>
    /// <remarks>
    /// The original ends every action whose priority equals the word at <c>HumanAI+2108</c>. That word is
    /// the ninth entry of the priority order — an event <em>type</em> — compared against a priority
    /// <em>position</em>, so it ends what the event at that position installed, not the alarm run. With the
    /// archetypes' order, the ninth entry is GUNSHOT (16) and position 16 is EXPLOSION; an alarm run
    /// installed from COMBAT carries on after the alarm is switched off. Reproduced as it is.
    /// </remarks>
    public void RemoveAlarmActions()
    {
        IReadOnlyList<AiEvent> order = Events.PriorityOrder;
        int priority = order.Count > AlarmActionsOrderIndex ? (int)order[AlarmActionsOrderIndex] : 0;
        foreach (AiPendingAction action in _pendingActions.Actions.ToArray())
        {
            if (action.Priority == priority)
            {
                FinishAction(action);
            }
        }
    }

    private const string ActionPatrol = "AIAction_Patrol";
    private const string ActionCombat = "AIAction_Combat";
    private const string ActionActivate = "AIAction_Activate";

    // HumanAI+2108 - 2076: which entry of the priority order 0x44DBC0 reads.
    private const int AlarmActionsOrderIndex = 8;

    /// <summary>Installs whatever the archetype script asked for on the last dispatch.</summary>
    /// <param name="actions">The produced actions, in script order.</param>
    /// <remarks>
    /// <para>
    /// The original hands one produced action to <c>0x450E70</c> and several to <c>0x450F10</c>, and both do
    /// the same three things. First <c>0x450EE0</c> ends every action above the first pushable one on the
    /// stack. Then each produced action goes on at the <b>head</b> and its start function runs.
    /// </para>
    /// <para>
    /// The produced list is built in script order (each <c>AIAction_*</c> appends at the tail through
    /// <c>0x450E10</c> -> <c>0x4AF8F0</c>), and <c>0x450F10</c> walks it from the TAIL back, adding each to the
    /// head (<c>0x4AF910</c>). So the FIRST action the script named ends up on top and runs first: a guard's
    /// COMBAT branch, <c>Activate(alarm trigger)</c> then <c>Combat</c>, runs to the alarm and fights after.
    /// Installing in script order had the fight on top, and a guard that saw you never went for the alarm.
    /// </para>
    /// <para>
    /// Every action takes the flag word of the first one walked -- the LAST the script named. A start that
    /// finishes its own action (SetCombat posts COMBAT; a refused Activate never begins) leaves nothing on the
    /// stack.
    /// </para>
    /// </remarks>
    public void ApplyActions(IReadOnlyList<AiProducedAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);

        // A copy: the script host reuses its produced-action list for the next soldier it runs.
        LastActions = [.. actions];
        if (actions.Count == 0)
        {
            return;
        }

        _pendingActions.PopToPushable(action => EndAction(action.Name));
        bool pushable = (FlagsOf(actions[^1]) & AiActionFlagWord.Pushable) != 0;
        for (int index = actions.Count - 1; index >= 0; index--)
        {
            AiProducedAction produced = actions[index];
            // 0x450F10 draws the 6..11-tick countdown before each start. Only an action with no run
            // function waits on it, but every AI subsystem shares this random stream.
            int countdown = (int)((_random.NextDouble() + 1.0) * 0.2 * 30.0);
            AiPendingAction? node = null;
            node = new AiPendingAction(produced.Slot, countdown, () => RunAction(node!), pushable, produced.Name);
            _pendingActions.Push(node);
            StartAction(produced);
            if (!IsActionActive(produced.Name))
            {
                FinishAction(node);
            }
        }
    }

    // The flag word is every AIAction_* native's last argument (0x44D0E0, 0x44D160, 0x44D6E0, ...).
    private static AiActionFlagWord FlagsOf(AiProducedAction action) =>
        AiScriptApi.Natives.FirstOrDefault(native => native.Name == action.Name) is { } native
            ? (AiActionFlagWord)action.Argument(native.ArgumentCount - 1)
            : AiActionFlagWord.None;

    /// <summary>Runs an action's start function — the <c>+20</c> each <c>AIAction_*</c> wrapper installs.</summary>
    private void StartAction(AiProducedAction action)
    {
        switch (action.Name)
        {
            case ActionPatrol:
                // The first argument is a patrol PATH id: a PatrolPath task whose child tasks are the
                // commands. Resolving it is the whole point of the registry.
                BeginPatrol(action.Argument(0));
                break;

            case "AIAction_RunToNode":
                _scriptedRunGait = true;
                BeginGoTo(action.Argument(0));
                break;

            case "AIAction_WalkToNode":
                _scriptedRunGait = false;
                BeginGoTo(action.Argument(0));
                break;

            case ActionCombat:
                EnterCombat();
                break;

            case "AIAction_SetCombat":
                PostCombatEvent();
                break;

            case "AIAction_FallFlat":
                BeginFallFlat(action.Real(0));
                break;

            case "AIAction_FireAtTask":
                BeginFireAtTask(action);
                break;

            case "AIAction_FireAtNode":
                BeginFireAtNode(action);
                break;

            case "AIAction_Dead":
                Kill();
                break;

            case "AIAction_Idle":
                Activity = AiSoldierActivity.Idle;
                break;

            case ActionActivate:
                // 0x44D420: arg 0 = the task to press, arg 1 = the gait (run when set). The start
                // (0x48E620) claims the task -- and, when it is this AI's own alarm trigger, the
                // alarm control too -- or SKIPS the action outright when somebody already has.
                BeginActivate(action.Argument(0), run: action.Argument(1) != 0);
                break;

            case "AIAction_LookAtEvent":
                BeginLookAtEvent(action);
                break;

            case "AIAction_MoveToEvent":
                BeginMoveToEvent();
                break;

            case "AIAction_RunPanicking":
                BeginRunPanicking(action.Real(0));
                break;

            case "AIAction_PlayAnimation":
                BeginPlayAnimation(action);
                break;

            case "AIAction_PlaySound":
                PlayScriptSound(action);
                break;

            case "AIAction_Stunned":
                BeginStunned(action.Real(0));
                break;

            case "AIAction_KickGrenade":
                BeginKickGrenade();
                break;

            default:
                break;
        }
    }

    /// <summary>Runs one tick of an action the stack chose — its <c>+24</c> — and ends it when it is done.</summary>
    private void RunAction(AiPendingAction action)
    {
        _runningAction = action.Name;
        int tick = _lastTick;
        switch (action.Name)
        {
            case ActionCombat:
                RunCombat(tick);
                break;

            case ActionPatrol:
                RunPatrolCommand(tick);
                break;

            case ActionActivate:
                // 0x48E7D0 sets the gait from its run argument every tick.
                SetGait(walking: _activate is not { Run: true });
                RunActivate();
                break;

            case "AIAction_FallFlat":
                RunFallFlat(tick);
                break;

            case "AIAction_Stunned":
                RunStunned(tick);
                break;

            case "AIAction_FireAtNode":
            case "AIAction_FireAtTask":
                Activity = AiSoldierActivity.FiringAtTask;
                RunScriptedFire();
                break;

            case "AIAction_RunPanicking":
                RunPanicking();
                break;

            case "AIAction_LookAtEvent":
                RunLookAtEvent(tick);
                break;

            case "AIAction_WalkToNode":
            case "AIAction_RunToNode":
            case "AIAction_MoveToEvent":
                // 0x48EBF0 / 0x48EC50 / 0x48E020 set their gait every tick.
                SetGait(walking: !_scriptedRunGait);
                if (StepTowardsNode(_goToNode))
                {
                    _goToNode = -1;
                    Activity = AiSoldierActivity.Idle;
                }

                break;

            case "AIAction_Idle":
                // 0x48E520 -> 0x4513F0 only brings the view back to level, at the pitch clamp; it never
                // ends, so an idle AI stays idle until something is pushed over it.
                Activity = AiSoldierActivity.Idle;
                _pitchInput = Math.Clamp(-ViewPitch, -_steering.PitchClamp, _steering.PitchClamp) / MathF.PI;
                break;

            case "AIAction_PlayAnimation":
                // 0x48DEC0 holds the clip on animation channel 3 until it has played; the body is the
                // clip's, whatever fight it was pushed over.
                Activity = AiSoldierActivity.Idle;
                break;

            default:
                // KickGrenade's runner (0x48E250) does nothing.
                break;
        }

        if (!IsActionActive(action.Name))
        {
            FinishAction(action);
        }
    }

    private void RunScript()
    {
        if (_host is null || _script is null)
        {
            // No script attached: the built-in fallback decides, which is what shipped before guard.qsc ran.
            // A pending COMBAT event counts as the stimulus too -- it is what a SetCombat synthesizes
            // (0x48E560), and the engine's default answer to a dispatched COMBAT is AIAction_Combat.
            // A soldier thrown flat finishes the fall first, exactly as the scripted path defers it.
            // TakingDamage is deliberately not a fallback combat-install trigger: the patrol combat arm
            // consumes its drained count for evasion, and re-installing combat for every hit interrupts
            // persistent prone behavior.
            if (TargetSlot.IsVisible
                || Events.PendingCount(AiEvent.Combat) > 0
                || Events.PendingCount(AiEvent.Gunshot) > 0
                || Events.PendingCount(AiEvent.GunshotMiss) > 0
                || Events.PendingCount(AiEvent.Alert) > 0)
            {
                if (!_combatInstalled)
                {
                    ApplyActions([new AiProducedAction(
                        ActionCombat, [(int)AiActionFlagWord.Pushable], PriorityPositionOf(AiEvent.Combat))]);
                }
                else
                {
                    EnterCombat();
                }
            }

            Drain();
            return;
        }

        Events.Pump(_pendingActions.PriorityCap, Dispatch);
    }

    /// <summary>Runs the script for one event type at a priority position, and installs what it produced.</summary>
    private bool Dispatch(AiEvent type, int slot)
    {
        // What sub_44D850 reads mid-dispatch: the current event's own position, off the queue head.
        AiEventRecord? current = Events.HeadOf(type);
        _currentEventPosition = current?.Position;
        _currentEventSource = current?.Source ?? 0;
        _currentEventSubject = current?.Subject ?? 0;

        // AIFunction_GetEventDistance (0x44E930) measures the CURRENT queue record from the AI at
        // dispatch time. Keeping the value written when an event was posted let a later visibility
        // sweep or alert overwrite a queued gunshot's distance before guard.qvm saw it.
        if (_currentEventPosition is { } eventPosition)
        {
            LastEventDistance = Vector3d.Distance(Position, eventPosition);
        }

        Dispatched?.Invoke(type, slot);
        bool handled = _host!.Run(_script!, this, type, slot, _archetypeScript);
        LastDispatchedEvent = type;
        if (handled)
        {
            ApplyActions(_host.Produced);
        }

        _currentEventPosition = null;
        _currentEventSource = 0;
        _currentEventSubject = 0;
        return handled;
    }

    private bool IsActionActive(string name) => name switch
    {
        ActionPatrol => Route is not null,
        ActionCombat => _combatInstalled,
        "AIAction_Dead" => Activity == AiSoldierActivity.Dead,
        "AIAction_FallFlat" => _fallFlatDeadline != -1,
        ActionActivate => _activate is not null,
        "AIAction_WalkToNode" or "AIAction_RunToNode" or "AIAction_MoveToEvent" => _goToNode >= 0,
        "AIAction_FireAtNode" or "AIAction_FireAtTask" => _scriptedFire is not null,
        "AIAction_PlayAnimation" => _scriptedAnimation,
        "AIAction_LookAtEvent" => _lookAtPoint is not null,
        "AIAction_Stunned" => _stunnedDurationTicks >= 0,
        "AIAction_KickGrenade" => _hasKickableGrenade,
        "AIAction_RunPanicking" => _panicDestination >= 0,
        "AIAction_Idle" => true,

        // SetCombat posts COMBAT and ends in its start (0x48E560); PlaySound in its first run (0x48DF80).
        _ => false,
    };

    /// <summary>Takes an action off the stack and runs its end function — <c>0x450FF0</c>.</summary>
    private void FinishAction(AiPendingAction action)
    {
        _pendingActions.Remove(action);
        EndAction(action.Name);
    }

    /// <summary>An action's end function — the <c>+32</c> <c>0x450FF0</c> runs, however the action ended.</summary>
    private void EndAction(string name)
    {
        switch (name)
        {
            case ActionPatrol:
                Route = null;
                RequestedAnimation = -1;
                break;

            case ActionCombat:
                EndCombat();
                break;

            case ActionActivate:
                // 0x48E870 releases the two claim handles.
                ReleaseActivateClaims();
                break;

            case "AIAction_FallFlat":
                _fallFlatDeadline = -1;
                break;

            case "AIAction_WalkToNode":
            case "AIAction_RunToNode":
            case "AIAction_MoveToEvent":
                _goToNode = -1;
                _scriptedRunGait = false;
                break;

            case "AIAction_FireAtNode":
            case "AIAction_FireAtTask":
                EndScriptedFire();
                break;

            case "AIAction_PlayAnimation":
                if (_scriptedAnimation)
                {
                    RequestedAnimation = -1;
                    _scriptedAnimation = false;
                }

                break;

            case "AIAction_LookAtEvent":
                _lookAtPoint = null;
                break;

            case "AIAction_Stunned":
                if (_stunnedDurationTicks >= 0)
                {
                    RequestedAnimation = -1;
                }

                _stunnedDurationTicks = -1;
                _stunnedDeadline = -1;
                break;

            case "AIAction_RunPanicking":
                _panicDestination = -1;
                break;

            default:
                break;
        }
    }

    private void Drain()
    {
        // One event type dispatches per tick and every queue is then emptied, so an event nothing acted on is
        // discarded rather than deferred.
        Events.Pump(AiEventQueues.UnboundedPriorityCap, (_, _) => true);
    }

    private int PriorityPositionOf(AiEvent type)
    {
        IReadOnlyList<AiEvent> order = Events.PriorityOrder;
        for (int i = 0; i < order.Count; i++)
        {
            if (order[i] == type)
            {
                return i;
            }
        }

        return AiEventQueues.UnboundedPriorityCap;
    }
}
