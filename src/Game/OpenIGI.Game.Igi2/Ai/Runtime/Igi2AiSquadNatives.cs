using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>
/// The squad script's natives, <c>AISquad_SF_*</c> as <c>AISquad_RegisterScriptValues</c> registers them: each works on the squad
/// whose script is running (<c>AISquad_ptCurrentAISquad</c>) and its current event, the last one waiting.
/// </summary>
public sealed class Igi2AiSquadNatives
{
    private readonly Igi2AiWorld _world;

    /// <summary>Initializes a new instance of the <see cref="Igi2AiSquadNatives"/> class.</summary>
    /// <param name="world">The AI module.</param>
    public Igi2AiSquadNatives(Igi2AiWorld world) => _world = world ?? throw new ArgumentNullException(nameof(world));

    private Igi2AiSquad Squad => _world.CurrentSquad ?? throw new InvalidOperationException("No squad script is running.");

    /// <summary>Registers the natives.</summary>
    /// <param name="natives">The namespace.</param>
    public void Register(NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(natives);
        natives.RegisterFunction("AISquad_GetState", GetState);
        natives.RegisterFunction("AISquad_GetEvent", GetEvent);
        natives.RegisterFunction("AISquad_SwitchState", SwitchState);
        natives.RegisterFunction("AISquad_GetSquadType", GetSquadType);
        natives.RegisterFunction("AISquad_HasTarget", HasTarget);
        natives.RegisterFunction("AISquad_ReceiverLookAtEvent", ReceiverLookAtEvent);
        natives.RegisterFunction("AISquad_ReceiverLookInApproxEventDirection", ReceiverLookInApproxEventDirection);
        natives.RegisterFunction("AISquad_TriggerAlarm", TriggerAlarm);
        natives.RegisterFunction("AISquad_ManStationaryGun", ManStationaryGun);
        natives.RegisterFunction("AISquad_MoveToNode", MoveToNode);
        natives.RegisterFunction("AISquad_Patrol", Patrol);
        natives.RegisterFunction("AISquad_ThrowGrenade", ThrowGrenade);
        natives.RegisterFunction("AISquad_GetCurrentPatrolID", GetCurrentPatrolId);
        natives.RegisterFunction("AISquad_GetEventSpecificInt32", GetEventSpecificInt32);
        natives.RegisterFunction("AISquad_GetDistanceToEvent", GetDistanceToEvent);
        natives.RegisterFunction("AISquad_DefaultHandler", DefaultHandler);
        natives.RegisterFunction("AISquad_ReceiverHasTarget", ReceiverHasTarget);
        natives.RegisterFunction("AISquad_PlaySoundAtReceiversPos", PlaySoundAtReceiversPosition);
        natives.RegisterFunction("AISquad_TriggerEvent", TriggerEvent);

        // AISquad_Open binds AISquad_nScriptSquadState and AISquad_nScriptEvent, which nothing in the client writes: nought unless a
        // script sets them.
        natives.RegisterVariable(
            "AISquad_nSquadState", new QvmIntegerVariable(() => _world.SquadScriptState, value => _world.SquadScriptState = value));
        natives.RegisterVariable(
            "AISquad_nEvent", new QvmIntegerVariable(() => _world.SquadScriptEvent, value => _world.SquadScriptEvent = value));
    }

    private static QvmValue Bool(bool value) => QvmValue.FromInt32(value ? 1 : 0);

    private QvmValue GetState(QvmCallContext call) => QvmValue.FromInt32(Squad.State);

    private QvmValue GetEvent(QvmCallContext call) => QvmValue.FromInt32(Squad.CurrentEvent?.Kind ?? -1);

    private QvmValue GetSquadType(QvmCallContext call) => QvmValue.FromInt32(Squad.SquadType);

    private QvmValue HasTarget(QvmCallContext call) => Bool(Squad.Targets.Count > 0);

    /// <summary>
    /// Switches the squad to an action, <c>AISquad_SF_SwitchStateCB</c>: with a line too, an inquiry may repeat it at once, and the
    /// leader says it; the top action ends handing the coming one the current event, the stack is cleared, and the action is pushed.
    /// </summary>
    private QvmValue SwitchState(QvmCallContext call)
    {
        if (call.ArgumentCount is < 1 or > 2)
        {
            return QvmValue.FromInt32(1);
        }

        Igi2AiSquad squad = Squad;
        IIgi2SoundInstance? sound = null;
        if (call.ArgumentCount == 2)
        {
            int sample = call.GetInt32(1);
            if (call.GetInt32(0) == (int)Igi2AiSquadActionKind.Inquire)
            {
                _world.VoiceLastPlayed[sample] = 0;
            }

            sound = squad.SelectAndPlaySound(sample, squad.FirstMember);
        }

        int next = call.GetInt32(0);
        Igi2AiSquadChange change = new() { NextAction = next, PopMembers = true };
        squad.Pop(change);
        squad.ClearActionStack(popMembers: true);
        if (Create((Igi2AiSquadActionKind)next) is { } action)
        {
            action.SwitchStateSound = sound;
            squad.Push(action, change);
        }

        return QvmValue.FromInt32(1);
    }

    /// <summary>The action of a kind, as the switch makes it; a patrol is only started by <c>AISquad_Patrol</c>.</summary>
    private static Igi2AiSquadAction? Create(Igi2AiSquadActionKind kind) => kind switch
    {
        Igi2AiSquadActionKind.Idle => new Igi2AiSquadIdle(),
        Igi2AiSquadActionKind.HoldArea => new Igi2AiSquadHoldArea(),
        Igi2AiSquadActionKind.Inquire => new Igi2AiSquadInquire(),
        Igi2AiSquadActionKind.SearchArea => new Igi2AiSquadSearchArea(),
        Igi2AiSquadActionKind.Move => new Igi2AiSquadMove(),
        Igi2AiSquadActionKind.Leapfrog => new Igi2AiSquadLeapfrog(),
        Igi2AiSquadActionKind.MoveSearchInDirection => new Igi2AiSquadMoveSearchInDirection(),
        Igi2AiSquadActionKind.LookSearchInDirection => new Igi2AiSquadLookSearchInDirection(),
        Igi2AiSquadActionKind.Danger => new Igi2AiSquadDanger(),
        _ => null,
    };

    /// <summary>Each member who heard the event looks at it, at the rank of <c>AIEVENT_LOOK_AT</c> (46), <c>AISquad_SF_ReceiverLookAtEventCB</c>.</summary>
    private QvmValue ReceiverLookAtEvent(QvmCallContext call)
    {
        Igi2AiSquad squad = Squad;
        if (squad.CurrentEvent is { } current)
        {
            foreach (Igi2HumanAi receiver in current.Receivers.ToArray())
            {
                int delay = squad.OrderDelay();
                receiver.ReceiveSquadOrder(new Igi2AiSquadOrder
                {
                    Kind = Igi2AiSquadOrderKind.LookAtEvent,
                    Delay = delay,
                    Position = current.Position,
                    Timer = receiver.Events.RankOf(46),
                });
            }
        }

        return QvmValue.FromInt32(1);
    }

    /// <summary>
    /// Each member who heard the event looks roughly its way for fifteen seconds over six sectors, at the rank of <c>AIEVENT_LOOK_AT</c>
    /// (<c>AISquad_SF_ReceiverLookInApproxEventDirectionCB</c>).
    /// </summary>
    private QvmValue ReceiverLookInApproxEventDirection(QvmCallContext call)
    {
        Igi2AiSquad squad = Squad;
        if (squad.CurrentEvent is { } current)
        {
            foreach (Igi2HumanAi receiver in current.Receivers.ToArray())
            {
                int delay = squad.OrderDelay();
                receiver.ReceiveSquadOrder(new Igi2AiSquadOrder
                {
                    Kind = Igi2AiSquadOrderKind.LookInApproxDirection,
                    Delay = delay,
                    Position = current.Position,
                    Timer = 450,
                    SectorResolution = 6,
                    Direction = new Vector3f(0f, 0f, receiver.Events.RankOf(46)),
                });
            }
        }

        return QvmValue.FromInt32(1);
    }

    /// <summary>
    /// Sends a member to raise the alarm, <c>AISquad_SF_TriggerAlarmCB</c>: with an alarm control not already ringing and its
    /// trigger, the member nearest the trigger is moved out of the lead (the original swaps the first other member to the front),
    /// and, if within the run distance and not already activating, ordered to activate the trigger, calling out when not alone.
    /// </summary>
    private QvmValue TriggerAlarm(QvmCallContext call)
    {
        Igi2AiSquad squad = Squad;
        int controlId = squad.Task.AlarmControlId;
        int triggerId = squad.Task.AlarmTriggerId;
        if (controlId == -1 || triggerId == -1 || (uint)controlId >= 0x1000u)
        {
            return QvmValue.FromInt32(1);
        }

        if (_world.TaskById(controlId) is not Igi2AlarmControlTask { IsAlarm: false } || _world.TaskPosition(triggerId) is not { } trigger)
        {
            return QvmValue.FromInt32(1);
        }

        List<Igi2HumanAi> team = squad.Teams[0].Members;
        Igi2HumanAi? closest = null;
        float best = 0f;
        foreach (Igi2HumanAi member in team)
        {
            float distance = Igi2AiMath.Distance(member.Soldier.Position, trigger);
            if (closest is null || distance < best)
            {
                best = distance;
                closest = member;
            }
        }

        int other = team.FindIndex(m => !ReferenceEquals(m, closest));
        if (other >= 0)
        {
            squad.Teams[0].MakeLeader(other);
        }

        if (closest is not null && ((Fpu)squad.Task.MaxRunDistance * 4096.0).Value > best && !closest.IsTop(Igi2AiContent.ActivateInit))
        {
            closest.ReceiveSquadOrder(new Igi2AiSquadOrder { Kind = Igi2AiSquadOrderKind.Activate, Node = triggerId });
            if (team.Count > 1 && closest.Say(closest.Soldier, "gv_22_03", queueable: false) is { } sound && !squad.AddSoundToPlayingList(sound, closest))
            {
                sound.Delete();
            }
        }

        return QvmValue.FromInt32(1);
    }

    /// <summary>
    /// Sends a member to man the squad's mounted gun, <c>AISquad_SF_ManStationaryGunCB</c>: of the members not already on it whose
    /// target is in front of the gun, the nearest, if within the run distance and not activating, is ordered to it.
    /// </summary>
    private QvmValue ManStationaryGun(QvmCallContext call)
    {
        Igi2AiSquad squad = Squad;
        int gunId = squad.Task.StationaryGunId;
        if ((uint)gunId >= 0x1000u || _world.StationaryGun(gunId) is not { IsManned: false } gun)
        {
            return QvmValue.FromInt32(1);
        }

        Igi2HumanAi? closest = null;
        float best = 0f;
        foreach (Igi2HumanAi member in squad.Teams[0].Members)
        {
            if (member.IsTop(Igi2AiContent.CombatStationaryGunInit) || member.GetTarget(visibleOnly: true) is not { } target)
            {
                continue;
            }

            Vector3d at = target.DetectablePosition;
            Vector3d g = gun.Position;
            Vector3f forward = gun.Forward;
            if (((((Fpu)forward.Z * (at.Z - g.Z)) + ((Fpu)forward.Y * (at.Y - g.Y))) + ((Fpu)forward.X * (at.X - g.X))).Value < 0.0)
            {
                continue;
            }

            float distance = Igi2AiMath.Distance(member.Soldier.Position, g);
            if (closest is null || distance < best)
            {
                best = distance;
                closest = member;
            }
        }

        if (closest is not null && ((Fpu)squad.Task.MaxRunDistance * 4096.0).Value > best && !closest.IsTop(Igi2AiContent.ActivateInit))
        {
            closest.ReceiveSquadOrder(new Igi2AiSquadOrder { Kind = Igi2AiSquadOrderKind.Activate, Node = gunId });
        }

        return QvmValue.FromInt32(1);
    }

    /// <summary>Moves the squad to a node, <c>AISquad_SF_MoveToNodeCB</c>: false with no leader or no such node.</summary>
    private QvmValue MoveToNode(QvmCallContext call)
    {
        Igi2AiSquad squad = Squad;
        if (call.ArgumentCount != 1 || squad.FirstMember is not { Graph: { } graph } leader)
        {
            return Bool(false);
        }

        int id = call.GetInt32(0);
        if (graph.NodeAt(id) is not { } node)
        {
            return Bool(false);
        }

        if (leader.CurrentNode != id)
        {
            squad.ClearActionStack(popMembers: true);
            squad.Push(new Igi2AiSquadMove(), new Igi2AiSquadChange { NextAction = (int)Igi2AiSquadActionKind.Move, PopMembers = true, Position = node.Position });
        }

        return Bool(true);
    }

    /// <summary>Starts the squad on a patrol, <c>AISquad_SF_PatrolCB</c>: false with no leader.</summary>
    private QvmValue Patrol(QvmCallContext call)
    {
        Igi2AiSquad squad = Squad;
        if (call.ArgumentCount != 1 || squad.FirstMember is null)
        {
            return Bool(false);
        }

        int path = call.GetInt32(0);
        squad.ClearActionStack(popMembers: true);
        squad.Push(new Igi2AiSquadPatrol(), new Igi2AiSquadChange { NextAction = (int)Igi2AiSquadActionKind.Patrol, PopMembers = true, PatrolPathId = path });
        return Bool(true);
    }

    /// <summary>
    /// Has up to two members carrying grenades throw at targets within fifty metres, <c>AISquad_SF_ThrowGrenadeCB</c>: false with no
    /// leader.
    /// </summary>
    private QvmValue ThrowGrenade(QvmCallContext call)
    {
        Igi2AiSquad squad = Squad;
        if (squad.FirstMember is null)
        {
            return Bool(false);
        }

        int thrown = 0;
        for (int t = 0; t < squad.TeamCount && thrown < 2; t++)
        {
            foreach (Igi2HumanAi member in squad.Teams[t].Members.ToArray())
            {
                if (thrown >= 2)
                {
                    break;
                }

                if (!member.HasGrenade)
                {
                    continue;
                }

                Vector3d target = member.GetTargetPosition(out _);
                if (target == default)
                {
                    continue;
                }

                Vector3d from = member.Soldier.Position;
                double dx = target.X - from.X, dy = target.Y - from.Y, dz = target.Z - from.Z;
                if (Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) < 204800.0)
                {
                    member.ReceiveSquadOrder(new Igi2AiSquadOrder { Kind = Igi2AiSquadOrderKind.ThrowGrenade });
                    thrown++;
                }
            }
        }

        return Bool(true);
    }

    private QvmValue GetCurrentPatrolId(QvmCallContext call) =>
        QvmValue.FromInt32(Squad.Actions.Count > 0 && Squad.Actions[^1] is Igi2AiSquadPatrol patrol ? patrol.PatrolPathId : -1);

    private QvmValue GetEventSpecificInt32(QvmCallContext call) => QvmValue.FromInt32(Squad.CurrentEvent?.SpecificInt ?? -1);

    /// <summary>
    /// How far, in metres, the nearest member who heard the event is from it, <c>AISquad_SF_GetDistanceToEvent</c>: -1 with no
    /// event or no one who heard it.
    /// </summary>
    private QvmValue GetDistanceToEvent(QvmCallContext call)
    {
        if (Squad.CurrentEvent is not { Receivers.Count: > 0 } current)
        {
            return QvmValue.FromInt32(-1);
        }

        float nearest = float.MaxValue;
        foreach (Igi2HumanAi receiver in current.Receivers)
        {
            float distance = Igi2AiMath.Distance(receiver.Soldier.Position, current.Position);
            if (distance < nearest)
            {
                nearest = distance;
            }
        }

        return QvmValue.FromInt32(Fpu.ToInt32((Fpu)nearest * 0.000244140625));
    }

    /// <summary>Runs the default squad script, <c>AISquad_SF_DefaultHandlerCB</c>.</summary>
    private QvmValue DefaultHandler(QvmCallContext call)
    {
        if (_world.SquadDefault is { } script)
        {
            _world.Execute(script);
        }

        return QvmValue.FromInt32(1);
    }

    /// <summary>
    /// Whether every member who heard the event has a target or is identifying one, <c>AISquad_SF_ReceiverHasTarget</c>: a squad on
    /// a vehicle counts any target listed, the others only one in sight; false with no one who heard it.
    /// </summary>
    private QvmValue ReceiverHasTarget(QvmCallContext call)
    {
        Igi2AiSquad squad = Squad;
        if (squad.CurrentEvent is not { Receivers.Count: > 0 } current)
        {
            return Bool(false);
        }

        foreach (Igi2HumanAi receiver in current.Receivers)
        {
            if (receiver.GetTarget(visibleOnly: squad.SquadType != 2) is null && !receiver.IsIdentifyingTarget())
            {
                return Bool(false);
            }
        }

        return Bool(true);
    }

    /// <summary>
    /// The member who heard the event nearest it says a line, <c>AISquad_SF_SelectAndPlaySoundAtReceiversPos</c> (the line held to
    /// nought to 19); the result is the reverse of the others', nought when it ran.
    /// </summary>
    private QvmValue PlaySoundAtReceiversPosition(QvmCallContext call)
    {
        if (call.ArgumentCount != 1)
        {
            return QvmValue.FromInt32(1);
        }

        Igi2AiSquad squad = Squad;
        if (squad.CurrentEvent is { } current && ClosestReceiver(current) is { } speaker)
        {
            int sample = Math.Clamp(call.GetInt32(0), 0, 19);
            squad.SelectAndPlaySound(sample, speaker);
        }

        return QvmValue.FromInt32(0);
    }

    /// <summary>The member who heard an event nearest it, <c>AISquad_GetClosestEventReceiver</c>.</summary>
    private static Igi2HumanAi? ClosestReceiver(Igi2AiSquadEvent current)
    {
        Igi2HumanAi? closest = null;
        float best = 0f;
        foreach (Igi2HumanAi receiver in current.Receivers)
        {
            float distance = Igi2AiMath.Distance(receiver.Soldier.Position, current.Position);
            if (closest is null || distance < best)
            {
                best = distance;
                closest = receiver;
            }
        }

        return closest;
    }

    /// <summary>
    /// Raises an event from the first member who heard the current one, <c>AISquad_SF_TriggerEvent</c>: only
    /// <c>AIEVENT_CALL_FOR_HELP</c> (41), heard within the radius given in metres by anyone (flags 1314); the result is nought when it
    /// ran.
    /// </summary>
    private QvmValue TriggerEvent(QvmCallContext call)
    {
        if (call.ArgumentCount != 2)
        {
            return QvmValue.FromInt32(1);
        }

        int kind = call.GetInt32(0);
        float radius = call.GetSingle(1);
        if (kind == 41 && Squad.CurrentEvent is { Receivers.Count: > 0 } current && current.Receivers[0].Soldier is { } caller)
        {
            _world.SendEvent(new Igi2AiEvent(41, caller.Team, 1314, ((Fpu)radius * 4096.0).F, caller.Position, default, caller, null));
        }

        return QvmValue.FromInt32(0);
    }
}
