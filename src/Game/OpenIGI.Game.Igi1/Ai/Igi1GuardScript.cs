using OpenIGI.Game.Ai;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>Reads a Project I.G.I. guard's orders off its own script, <c>MISSION:AI/&lt;mind id&gt;.qsc</c>.</summary>
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
public static class Igi1GuardScript
{
    /// <summary>Reads a guard's orders off its script.</summary>
    /// <param name="program">The script.</param>
    /// <returns>The orders; <see cref="Igi1GuardOrders.None"/> where it gives none.</returns>
    public static Igi1GuardOrders Read(QvmProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        Recorder recorder = new();
        NativeRegistry natives = recorder.Namespace(program);
        foreach (AiEvent told in (AiEvent[])[AiEvent.Create, AiEvent.Idle, AiEvent.AlarmOn])
        {
            recorder.Event = (int)told;
            if (new QvmInterpreter(program, natives).Run() != QvmError.None)
            {
                return Igi1GuardOrders.None;
            }
        }

        return recorder.Orders;
    }

    // The natives a script calls, kept as orders rather than done.
    private sealed class Recorder
    {
        public int Event { get; set; }

        public Igi1GuardOrders Orders { get; private set; } = Igi1GuardOrders.None;

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
