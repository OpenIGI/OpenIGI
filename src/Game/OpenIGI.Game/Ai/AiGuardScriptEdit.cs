using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Qvm;

namespace OpenIGI.Game.Ai;

/// <summary>
/// Changes a guard's existing script to new orders, touching only the calls that say them: what else the script does --
/// its own handling of combat, of events these orders do not name, its comments of code -- stays as it was written.
/// </summary>
/// <remarks>
/// <para>
/// Every shipped guard script is the games' event switch, but none is laid out exactly as <see cref="AiGuardScript.Build"/>
/// writes one (not one of Project I.G.I.'s 868), so a script written afresh would lose what each does of its own. Here
/// the script is taken apart (<see cref="QscDecompiler"/>, which gives every retail script back byte for byte), the branch an
/// order belongs to found by its event test, and in that branch alone the order's call replaced, put in after the default
/// handler, or taken out.
/// </para>
/// <para>
/// What is written is then read back (<see cref="AiGuardScript.Read"/>): unless it gives exactly the orders asked for, the
/// change is refused rather than written, so a script laid out some other way is never half changed.
/// </para>
/// </remarks>
public static class AiGuardScriptEdit
{
    private const string EventTest = "AIFunction_GetCurrentEventType";
    private const string DefaultHandler = "AIFunction_DefaultHandler";
    private const string Patrol = "AIAction_Patrol";

    /// <summary>Changes a script to new orders.</summary>
    /// <param name="image">The script as it is.</param>
    /// <param name="after">The orders it should give.</param>
    /// <param name="squads">Whether a patrol put in is flagged as I.G.I. 2's squads' are (see <see cref="AiGuardScript.Build"/>).</param>
    /// <returns>The changed script, or null when it cannot be changed so and read back the same.</returns>
    public static byte[]? Apply(ReadOnlyMemory<byte> image, AiGuardOrders after, bool squads)
    {
        ArgumentNullException.ThrowIfNull(after);
        QscScript script = QscDecompiler.Decompile(image);
        AiGuardOrders before = AiGuardScript.Read(QvmProgram.Read(image));

        List<QscStatement> statements = [.. script.Statements];
        bool changed = false;
        foreach ((string native, Func<AiGuardOrders, QscExpression?> value) in Setters)
        {
            QscExpression? was = value(before);
            QscExpression? now = value(after);
            if (Same(was, now))
            {
                continue;
            }

            if (!Rewrite(statements, "AIEVENT_CREATE", branch => SetCall(branch, native, now is null ? null : [now], afterHandler: true)))
            {
                return null;
            }

            changed = true;
        }

        foreach ((string kind, int? was, int? now, int start) in (ReadOnlySpan<(string, int?, int?, int)>)
            [("AIEVENT_IDLE", before.PatrolPathId, after.PatrolPathId, after.PatrolStart), ("AIEVENT_ALARMON", before.AlarmPatrolPathId, after.AlarmPatrolPathId, after.AlarmPatrolStart)])
        {
            if (was == now)
            {
                continue;
            }

            QscExpression[]? arguments = now is { } path
                ? [new QscIntLiteral(path), new QscIntLiteral(start), squads ? new QscIntLiteral(1) : new QscSymbol("AIACTIONFLAG_NONE")]
                : null;
            if (!Rewrite(statements, kind, branch => SetCall(branch, Patrol, arguments, afterHandler: false, keepTail: 1)))
            {
                return null;
            }

            changed = true;
        }

        if (!changed)
        {
            return image.ToArray();
        }

        byte[] written = QscCompiler.Compile(new QscScript(statements, script.Version)
        {
            RequestedStackSizeBytes = script.RequestedStackSizeBytes,
            RequestedMaxLocals = script.RequestedMaxLocals,
            FileInfo = script.FileInfo,
        });

        // Read back as the game will read it: only exactly the orders asked for is a change made.
        AiGuardOrders read = AiGuardScript.Read(QvmProgram.Read(written));
        return read with { Tunings = after.Tunings } == after ? written : null;
    }

    // The orders set on creation, each its native and its one argument, or null when it is not given.
    private static readonly (string Native, Func<AiGuardOrders, QscExpression?> Value)[] Setters =
    [
        ("AIFunction_SetViewLength", static o => o.ViewLength is { } v ? new QscRealLiteral(v) : null),
        ("AIFunction_SetViewGamma", static o => o.ViewGamma is { } v ? new QscRealLiteral(v) : null),
        ("AIFunction_SetAlarmControlID", static o => o.AlarmControlId is { } v ? new QscIntLiteral(v) : null),
        ("AIFunction_SetAlarmTriggerID", static o => o.AlarmTriggerId is { } v ? new QscIntLiteral(v) : null),
        ("AIFunction_SetGunnerID", static o => o.GunnerId is { } v ? new QscIntLiteral(v) : null),
        ("AIFunction_SetAlarmAccess", static o => o.AlarmAccess is { } v
            ? new QscSymbol(v == AiAlarmAccess.AfterCombat ? "AIALARMACCESS_AFTERCOMBAT" : "AIALARMACCESS_BEFORECOMBAT")
            : null),
        ("AIFunction_SetInvulnerability", static o => o.IsInvulnerable is { } v ? new QscIntLiteral(v ? 1 : 0) : null),
    ];

    private static bool Same(QscExpression? a, QscExpression? b) => (a, b) switch
    {
        (null, null) => true,
        (QscRealLiteral x, QscRealLiteral y) => x.Value.Equals(y.Value),
        (QscIntLiteral x, QscIntLiteral y) => x.Value == y.Value,
        (QscSymbol x, QscSymbol y) => x.Name == y.Name,
        _ => false,
    };

    // The branch of the event switch that tests for an event, rewritten in place; false when the script has none.
    private static bool Rewrite(List<QscStatement> statements, string kind, Func<List<QscStatement>, List<QscStatement>> change)
    {
        for (int i = 0; i < statements.Count; i++)
        {
            if (statements[i] is not QscIf branch)
            {
                continue;
            }

            if (Tests(branch.Condition, kind))
            {
                statements[i] = new QscIf(branch.Condition, change([.. branch.Then]), branch.Else);
                return true;
            }

            // The rest of the switch hangs off each test's else.
            if (branch.Else is { } rest)
            {
                List<QscStatement> otherwise = [.. rest];
                if (Rewrite(otherwise, kind, change))
                {
                    statements[i] = new QscIf(branch.Condition, branch.Then, otherwise);
                    return true;
                }
            }
        }

        return false;
    }

    // AIFunction_GetCurrentEventType() == kind, either way round.
    private static bool Tests(QscExpression condition, string kind) =>
        condition is QscBinary { Operator: QscBinaryOperator.Equal } test
        && ((test.Left is QscCall { Name: EventTest } && test.Right is QscSymbol right && right.Name == kind)
            || (test.Right is QscCall { Name: EventTest } && test.Left is QscSymbol left && left.Name == kind));

    // A branch with its call to a native set to new arguments: replaced where it stands, put in after the default handler
    // (or at the branch's start) where it has none, or taken out for none. keepTail keeps that many of the old call's last
    // arguments (a patrol's flags) as the script wrote them.
    private static List<QscStatement> SetCall(List<QscStatement> branch, string native, QscExpression[]? arguments, bool afterHandler, int keepTail = 0)
    {
        int at = branch.FindIndex(s => s is QscExpressionStatement { Expression: QscCall call } && call.Name == native);
        if (arguments is null)
        {
            branch.RemoveAll(s => s is QscExpressionStatement { Expression: QscCall call } && call.Name == native);
            return branch;
        }

        if (at >= 0)
        {
            QscCall old = (QscCall)((QscExpressionStatement)branch[at]).Expression;
            if (keepTail > 0 && old.Arguments.Count == arguments.Length)
            {
                arguments = [.. arguments.Take(arguments.Length - keepTail), .. old.Arguments.Skip(arguments.Length - keepTail)];
            }

            branch[at] = new QscExpressionStatement(new QscCall(native, arguments));
            return branch;
        }

        int handler = branch.FindIndex(static s => s is QscExpressionStatement { Expression: QscCall { Name: DefaultHandler } });
        branch.Insert(afterHandler && handler >= 0 ? handler + 1 : 0, new QscExpressionStatement(new QscCall(native, arguments)));
        return branch;
    }
}
