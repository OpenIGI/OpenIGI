using OpenIGI.Game.Ai;
using OpenIGI.Scripting.Qsc;
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

        List<QscStatement> create = [Statement("AIFunction_DefaultHandler")];
        if (orders.ViewLength is { } length)
        {
            create.Add(Statement("AIFunction_SetViewLength", new QscRealLiteral(length)));
        }

        if (orders.ViewGamma is { } gamma)
        {
            create.Add(Statement("AIFunction_SetViewGamma", new QscRealLiteral(gamma)));
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

        // Built inside out: the last arm is the default handler, and each event test wraps what follows it as
        // its else, which is the if / else-if chain retail's scripts compile to.
        List<QscStatement> rest = [Statement("AIFunction_DefaultHandler")];
        if (orders.AlarmPatrolPathId is { } alarmPath)
        {
            rest = [EventIs(
                "AIEVENT_ALARMON",
                [
                    Statement("AIAction_Patrol", new QscIntLiteral(alarmPath), new QscIntLiteral(orders.AlarmPatrolStart), new QscIntLiteral(1)),
                    Statement("AIFunction_PassEventOnToSquad"),
                ],
                rest)];
        }

        if (orders.PatrolPathId is { } path)
        {
            rest = [EventIs(
                "AIEVENT_IDLE",
                [
                    Statement("AIAction_Patrol", new QscIntLiteral(path), new QscIntLiteral(orders.PatrolStart), new QscIntLiteral(1)),
                    Statement("AIAction_Idle", new QscIntLiteral(1)),
                    Statement("AIFunction_PassEventOnToSquad"),
                ],
                rest)];
        }

        QscScript script = new([EventIs("AIEVENT_CREATE", create, rest)], QvmProgram.IgiVersion);
        return QscCompiler.Compile(script);
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
}
