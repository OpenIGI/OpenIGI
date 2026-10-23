using OpenIGI.Game.Ai;
using OpenIGI.Scripting.Qvm;

namespace OpenIGI.Game.Igi2.Scripting;

/// <summary>Writes a soldier's script, <c>MISSION:AI/&lt;mind id&gt;.qvm</c>, from its orders, as I.G.I. 2's own soldiers' are laid out.</summary>
/// <remarks>
/// <para>
/// The shape is retail's (a patrolling soldier's, <c>missions/location1/level1/ai/502.qvm</c>): on <c>AIEVENT_CREATE</c> the
/// default handler, then the orders that set the soldier up; on <c>AIEVENT_IDLE</c> the patrol, <c>AIAction_Idle</c> and the
/// event passed on to the squad; on <c>AIEVENT_ALARMON</c> the alarm's patrol, where it has one, and the event passed on;
/// anything else to the default handler, which passes it on too
/// (<c>common/ai/default.qvm</c>).
/// </para>
/// <para>
/// A soldier with no patrol is idle as its type is, through the default handler; one with no orders at all is the generic
/// script (<see cref="Igi2GenericScripts"/>).
/// </para>
/// </remarks>
public static class Igi2SoldierScript
{
    /// <summary>Writes the script.</summary>
    /// <param name="orders">The soldier's orders.</param>
    /// <returns>The compiled script.</returns>
    public static byte[] Build(Igi2SoldierOrders orders)
    {
        ArgumentNullException.ThrowIfNull(orders);
        QvmWriter writer = new();
        QvmCodeWriter code = writer.Code;
        QvmLabel notCreate = code.CreateLabel();
        QvmLabel notIdle = code.CreateLabel();
        QvmLabel notAlarm = code.CreateLabel();
        QvmLabel end = code.CreateLabel();

        EventIs(writer, "AIEVENT_CREATE", notCreate);
        Statement(writer, QvmExpression.Call("AIFunction_DefaultHandler"));
        if (orders.ViewLength is { } length)
        {
            Statement(writer, QvmExpression.Call("AIFunction_SetViewLength", QvmExpression.Real(length)));
        }

        if (orders.ViewGamma is { } gamma)
        {
            Statement(writer, QvmExpression.Call("AIFunction_SetViewGamma", QvmExpression.Real(gamma)));
        }

        if (orders.GunnerId is { } gunner)
        {
            Statement(writer, QvmExpression.Call("AIFunction_SetGunnerID", QvmExpression.Int(gunner)));
        }

        if (orders.AlarmAccess is { } access)
        {
            Statement(writer, QvmExpression.Call(
                "AIFunction_SetAlarmAccess",
                QvmExpression.Symbol(access == AiAlarmAccess.AfterCombat ? "AIALARMACCESS_AFTERCOMBAT" : "AIALARMACCESS_BEFORECOMBAT")));
        }

        if (orders.IsInvulnerable is { } invulnerable)
        {
            Statement(writer, QvmExpression.Call("AIFunction_SetInvulnerability", QvmExpression.Int(invulnerable ? 1 : 0)));
        }

        code.Branch(QvmOpcode.Bra, end);
        code.MarkLabel(notCreate);

        if (orders.PatrolPathId is { } path)
        {
            EventIs(writer, "AIEVENT_IDLE", notIdle);
            Statement(writer, QvmExpression.Call("AIAction_Patrol", QvmExpression.Int(path), QvmExpression.Int(orders.PatrolStart), QvmExpression.Int(1)));
            Statement(writer, QvmExpression.Call("AIAction_Idle", QvmExpression.Int(1)));
            Statement(writer, QvmExpression.Call("AIFunction_PassEventOnToSquad"));
            code.Branch(QvmOpcode.Bra, end);
            code.MarkLabel(notIdle);
        }

        if (orders.AlarmPatrolPathId is { } alarmPath)
        {
            EventIs(writer, "AIEVENT_ALARMON", notAlarm);
            Statement(writer, QvmExpression.Call(
                "AIAction_Patrol", QvmExpression.Int(alarmPath), QvmExpression.Int(orders.AlarmPatrolStart), QvmExpression.Int(1)));
            Statement(writer, QvmExpression.Call("AIFunction_PassEventOnToSquad"));
            code.Branch(QvmOpcode.Bra, end);
            code.MarkLabel(notAlarm);
        }

        Statement(writer, QvmExpression.Call("AIFunction_DefaultHandler"));
        code.MarkLabel(end);
        code.Emit(QvmOpcode.Brk);
        return writer.Build();
    }

    // if (AIFunction_GetCurrentEventType() == kind) ... else goto otherwise.
    private static void EventIs(QvmWriter writer, string kind, QvmLabel otherwise)
    {
        QvmExpression.Call("AIFunction_GetCurrentEventType").EmitTo(writer);
        writer.PushSymbol(kind);
        writer.Code.Emit(QvmOpcode.Eq);
        writer.Code.Branch(QvmOpcode.Bf, otherwise);
    }

    // A call whose value is dropped.
    private static void Statement(QvmWriter writer, QvmExpression call)
    {
        call.EmitTo(writer);
        writer.Code.Emit(QvmOpcode.Pop);
    }
}
