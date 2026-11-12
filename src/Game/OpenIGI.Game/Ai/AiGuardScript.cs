using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Ai;

/// <summary>
/// A guard's own script, <c>MISSION:AI/&lt;mind id&gt;.qvm</c>, in either game: its orders read off it (<see cref="Read"/>), and a
/// script written from them (<see cref="Build"/>). Both games' guard scripts are the same event switch over the same natives.
/// </summary>
/// <remarks>
/// <para>
/// A guard's script is one event switch, run each time its mind is told something: on <c>AIEVENT_CREATE</c> it sets the
/// guard's alarm, gunner and view; on <c>AIEVENT_IDLE</c> it sends it on its patrol (<c>AIAction_Patrol</c>), and on
/// <c>AIEVENT_ALARMON</c> on another, where it has one; anything else goes to <c>AIFunction_DefaultHandler</c>. The script is
/// run here for those three events in a namespace of the game's own
/// constants (<see cref="AiScriptApi"/>), with every native it calls recorded rather than done, and the level's own variables
/// read as nought -- a quiet level -- so what it would have the guard do from the start is what it says.
/// </para>
/// <para>
/// A script that cannot run so -- one that faults, or waits on the level to say something first -- gives no orders, and the
/// guard does as its kind does.
/// </para>
/// </remarks>
public static class AiGuardScript
{
    /// <summary>Reads a guard's orders off its script.</summary>
    /// <param name="program">The script.</param>
    /// <returns>The orders; <see cref="AiGuardOrders.None"/> where it gives none.</returns>
    public static AiGuardOrders Read(QvmProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        Recorder recorder = new();
        NativeRegistry natives = recorder.Namespace(program);
        foreach (AiEvent told in (AiEvent[])[AiEvent.Create, AiEvent.Idle, AiEvent.AlarmOn])
        {
            recorder.Event = (int)told;
            if (new QvmInterpreter(program, natives).Run() != QvmError.None)
            {
                return AiGuardOrders.None;
            }
        }

        return recorder.Orders;
    }

    /// <summary>Writes a guard's script from its orders, as the game's own guards' are laid out.</summary>
    /// <param name="orders">The guard's orders.</param>
    /// <param name="squads">
    /// Whether the guard runs in a squad, as I.G.I. 2's do: its patrol goes idle and each event is passed on to the squad
    /// (<c>AIAction_Idle</c>, <c>AIFunction_PassEventOnToSquad</c>), its patrol flagged 1. A Project I.G.I. guard has no squad;
    /// its patrol is flagged <c>AIACTIONFLAG_NONE</c>, as the game's own guards' scripts flag theirs.
    /// </param>
    /// <returns>The compiled script.</returns>
    /// <remarks>
    /// <para>
    /// The shape is the games' own (a patrolling soldier's, I.G.I. 2's <c>missions/location1/level1/ai/502.qvm</c>): on
    /// <c>AIEVENT_CREATE</c> the default handler, then the orders that set the guard up; on <c>AIEVENT_IDLE</c> the patrol; on
    /// <c>AIEVENT_ALARMON</c> the alarm's patrol, where it has one; anything else to the default handler.
    /// </para>
    /// <para>A guard with no patrol is idle as its type is, through the default handler.</para>
    /// </remarks>
    public static byte[] Build(AiGuardOrders orders, bool squads)
    {
        ArgumentNullException.ThrowIfNull(orders);

        List<QscStatement> create = [Statement("AIFunction_DefaultHandler")];
        if (orders.ViewLength is { } length)
        {
            create.Add(Statement("AIFunction_SetViewLength", new QscRealLiteral(length)));
        }

        if (orders.ViewGamma is { } gamma)
        {
            create.Add(Statement("AIFunction_SetViewGamma", new QscRealLiteral(gamma)));
        }

        if (orders.AlarmControlId is { } control)
        {
            create.Add(Statement("AIFunction_SetAlarmControlID", new QscIntLiteral(control)));
        }

        if (orders.AlarmTriggerId is { } alarmTrigger)
        {
            create.Add(Statement("AIFunction_SetAlarmTriggerID", new QscIntLiteral(alarmTrigger)));
        }

        if (orders.GunnerId is { } gunner)
        {
            create.Add(Statement("AIFunction_SetGunnerID", new QscIntLiteral(gunner)));
        }

        if (orders.AlarmAccess is { } access)
        {
            create.Add(Statement(
                "AIFunction_SetAlarmAccess",
                new QscSymbol(access == AiAlarmAccess.AfterCombat ? "AIALARMACCESS_AFTERCOMBAT" : "AIALARMACCESS_BEFORECOMBAT")));
        }

        if (orders.IsInvulnerable is { } invulnerable)
        {
            create.Add(Statement("AIFunction_SetInvulnerability", new QscIntLiteral(invulnerable ? 1 : 0)));
        }

        foreach ((string native, float value) in orders.Tunings)
        {
            create.Add(Statement(native, new QscRealLiteral(value)));
        }

        // Built inside out: the last arm is the default handler, and each event test wraps what follows it as its else,
        // which is the if / else-if chain the games' own scripts compile to.
        List<QscStatement> rest = [Statement("AIFunction_DefaultHandler")];
        if (orders.AlarmPatrolPathId is { } alarmPath)
        {
            List<QscStatement> alarm =
                [Statement("AIAction_Patrol", new QscIntLiteral(alarmPath), new QscIntLiteral(orders.AlarmPatrolStart), Flags(squads))];
            if (squads)
            {
                alarm.Add(Statement("AIFunction_PassEventOnToSquad"));
            }

            rest = [EventIs("AIEVENT_ALARMON", alarm, rest)];
        }

        if (orders.PatrolPathId is { } path)
        {
            List<QscStatement> idle =
                [Statement("AIAction_Patrol", new QscIntLiteral(path), new QscIntLiteral(orders.PatrolStart), Flags(squads))];
            if (squads)
            {
                idle.Add(Statement("AIAction_Idle", new QscIntLiteral(1)));
                idle.Add(Statement("AIFunction_PassEventOnToSquad"));
            }

            rest = [EventIs("AIEVENT_IDLE", idle, rest)];
        }

        // One writer for every script the engine makes: the syntax tree the decompiler, the parser and the config writer
        // share, compiled as the games' own compiler laid its scripts out.
        return QscCompiler.Compile(new QscScript([EventIs("AIEVENT_CREATE", create, rest)], QvmProgram.IgiVersion));
    }

    // if (AIFunction_GetCurrentEventType() == kind) { then } else { otherwise }
    private static QscIf EventIs(string kind, List<QscStatement> then, List<QscStatement> otherwise) =>
        new(
            new QscBinary(QscBinaryOperator.Equal, new QscCall("AIFunction_GetCurrentEventType", []), new QscSymbol(kind)),
            then,
            otherwise);

    // A call whose value is dropped.
    private static QscExpressionStatement Statement(string native, params QscExpression[] arguments) =>
        new(new QscCall(native, arguments));

    // A patrol's flags: I.G.I. 2's soldiers' 1, Project I.G.I.'s guards' AIACTIONFLAG_NONE.
    private static QscExpression Flags(bool squads) => squads ? new QscIntLiteral(1) : new QscSymbol("AIACTIONFLAG_NONE");

    // The natives a script calls, kept as orders rather than done.
    private sealed class Recorder
    {
        public int Event { get; set; }

        public AiGuardOrders Orders { get; private set; } = AiGuardOrders.None;

        public NativeRegistry Namespace(QvmProgram program)
        {
            NativeRegistry natives = new();
            foreach (string? name in program.SymbolNames)
            {
                if (name is null || natives.Contains(name))
                {
                    continue;
                }

                if (AiScriptApi.TryGetConstant(name, out int constant))
                {
                    natives.RegisterConstant(name, constant);
                }
                else if (name is "TRUE")
                {
                    natives.RegisterConstant(name, 1);
                }
                else if (name is "FALSE")
                {
                    natives.RegisterConstant(name, 0);
                }
                else if (name.Contains('.', StringComparison.Ordinal))
                {
                    // One of the level's own variables -- a container running, a door open, a timer's tick -- read as nought.
                    natives.RegisterVariable(name, new QvmIntegerVariable(static () => 0, static _ => { }));
                }
                else if (name.StartsWith("AI", StringComparison.Ordinal) && !name.StartsWith("AIEVENT_", StringComparison.Ordinal))
                {
                    AiNativeReturn returns = AiScriptApi.TryGetNative(name, out AiNative native) ? native.Return : AiNativeReturn.Void;
                    natives.RegisterFunction(
                        name,
                        call => Record(name, call),
                        returnType: returns == AiNativeReturn.Real ? QvmValueKind.Float : QvmValueKind.Integer);
                }
                else
                {
                    natives.RegisterConstant(name, 0);
                }
            }

            return natives;
        }

        private QvmValue Record(string name, QvmCallContext call)
        {
            switch (name)
            {
                case "AIFunction_GetCurrentEventType":
                    return QvmValue.FromInt32(Event);
                case "AIAction_Patrol" when Event == (int)AiEvent.AlarmOn:
                    Orders = Orders with { AlarmPatrolPathId = call.GetInt32(0), AlarmPatrolStart = call.GetInt32(1) };
                    break;
                case "AIAction_Patrol" when Event == (int)AiEvent.Idle:
                    Orders = Orders with { PatrolPathId = call.GetInt32(0), PatrolStart = call.GetInt32(1) };
                    break;
                case "AIFunction_SetAlarmControlID":
                    Orders = Orders with { AlarmControlId = call.GetInt32(0) };
                    break;
                case "AIFunction_SetAlarmTriggerID":
                    Orders = Orders with { AlarmTriggerId = call.GetInt32(0) };
                    break;
                case "AIFunction_SetAlarmAccess":
                    Orders = Orders with { AlarmAccess = (AiAlarmAccess)call.GetInt32(0) };
                    break;
                case "AIFunction_SetGunnerID":
                    Orders = Orders with { GunnerId = call.GetInt32(0) };
                    break;
                case "AIFunction_SetViewLength":
                    Orders = Orders with { ViewLength = call.GetSingle(0) };
                    break;
                case "AIFunction_SetViewGamma":
                    Orders = Orders with { ViewGamma = call.GetSingle(0) };
                    break;
                case "AIFunction_SetInvulnerability":
                    Orders = Orders with { IsInvulnerable = call.GetInt32(0) != 0 };
                    break;
            }

            return QvmValue.Zero;
        }
    }
}
