using OpenIGI.Game.Ai;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Qvm;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Editor.Tests;

/// <summary>A guard's own script changed to new orders where they are said, and nowhere else.</summary>
public sealed class GuardScriptEditTests(ITestOutputHelper output)
{
    /// <summary>
    /// Over every shipped guard script: the same orders give the same bytes; new sight, a toggled invulnerability and the
    /// alarm taken away read back exactly as asked; and every line of the script that does not say those orders is still
    /// there, in order.
    /// </summary>
    [RequiresTitleDataFact("igi1")]
    public void Retail_scripts_change_only_where_their_orders_are_said()
    {
        int scripts = 0, changed = 0, refused = 0;
        string[] said = ["AIFunction_SetViewLength", "AIFunction_SetViewGamma", "AIFunction_SetInvulnerability", "AIFunction_SetAlarmControlID"];
        foreach (string path in GameDataSource.Igi1.EnumerateFiles("*.qvm").Where(static p => p.Replace('\\', '/').Contains("/ai/", StringComparison.OrdinalIgnoreCase)))
        {
            byte[] bytes = File.ReadAllBytes(path);
            AiGuardOrders orders;
            try
            {
                orders = AiGuardScript.Read(QvmProgram.Read(bytes));
            }
            catch (QvmFormatException)
            {
                continue;
            }

            scripts++;
            Assert.Equal(bytes, AiGuardScriptEdit.Apply(bytes, orders, squads: false));

            AiGuardOrders wanted = orders with { ViewLength = 40.5f, ViewGamma = 75f, IsInvulnerable = orders.IsInvulnerable == true ? null : true, AlarmControlId = null };
            if (AiGuardScriptEdit.Apply(bytes, wanted, squads: false) is not { } written)
            {
                refused++;
                continue;
            }

            changed++;
            Assert.Equal(wanted, AiGuardScript.Read(QvmProgram.Read(written)) with { Tunings = wanted.Tunings });
            Assert.Equal(Others(bytes, said), Others(written, said));
        }

        output.WriteLine($"{scripts} scripts: {changed} changed, {refused} refused.");
        Assert.True(scripts > 800);
        Assert.True(changed > scripts * 0.9, $"Only {changed} of {scripts} could be changed.");
    }

    /// <summary>A patrol given to a guard that stands, and taken away again, is said on its idle event alone.</summary>
    [Fact]
    public void A_patrol_is_put_in_and_taken_out_on_the_idle_event()
    {
        byte[] guard = QscCompiler.Compile(QscParser.ParseScript("""
            if (AIFunction_GetCurrentEventType() == AIEVENT_CREATE)
            {
            	AIFunction_DefaultHandler();
            }
            else if (AIFunction_GetCurrentEventType() == AIEVENT_IDLE)
            {
            }
            else
            {
            	AIFunction_DefaultHandler();
            }
            """, QvmProgram.IgiVersion));

        byte[] patrolling = AiGuardScriptEdit.Apply(guard, AiGuardOrders.None with { PatrolPathId = 2400 }, squads: false)!;
        Assert.Contains("AIAction_Patrol(2400, 0, AIACTIONFLAG_NONE);", QscPrinter.Print(QscDecompiler.Decompile(patrolling)), StringComparison.Ordinal);
        Assert.Equal(2400, AiGuardScript.Read(QvmProgram.Read(patrolling)).PatrolPathId);

        byte[] standing = AiGuardScriptEdit.Apply(patrolling, AiGuardOrders.None, squads: false)!;
        Assert.Equal(guard, standing);
    }

    // The script's lines, those saying the given natives left out.
    private static string[] Others(byte[] script, string[] natives) =>
        [.. QscPrinter.Print(QscDecompiler.Decompile(script)).ReplaceLineEndings("\n").Split('\n')
            .Where(line => !natives.Any(n => line.Contains(n, StringComparison.Ordinal)))];
}
