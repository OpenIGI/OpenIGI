using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Mission 1's two timers (<c>LevelTimer_RunHandler</c>, <c>igi2 0x564580</c>): started by script, counting while they run.</summary>
public sealed class RetailIgi2LevelTimerTests
{
    /// <summary>The hacking sound's timer, <c>LevelTimer_2700</c>: on while <c>EditVariable_271</c> is one.</summary>
    private const int Hack = 2700;

    /// <summary>The guards' timer, <c>LevelTimer_2701</c>: on once the variable passes one, and from then on by itself.</summary>
    private const int Guards = 2701;

    /// <summary>
    /// Authored not to run, a timer stands at nought until its script turns it on; it then counts a tick a tick, stops when the
    /// script turns it off, and keeps its count. One whose script reads its own <c>isRun</c> keeps itself running.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_timer_counts_while_its_script_runs_it()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Igi2LevelTimerTask hack = m.Tree.OfType<Igi2LevelTimerTask>().Single(static t => t.Id == Hack);
        Igi2LevelTimerTask guards = m.Tree.OfType<Igi2LevelTimerTask>().Single(static t => t.Id == Guards);
        Assert.Equal(("EditVariable_271.nValue == 1", false), (hack.OnScript, hack.InitialRun));
        Assert.Equal("EditVariable_271.nValue > 1 || this.isRun", guards.OnScript);
        Igi2EditVariableTask variable = m.Tree.OfType<Igi2EditVariableTask>().Single(static v => v.Id == 271);

        Tick(m, hack, guards, 3);
        Assert.Equal((false, 0, false, 0), (hack.IsRun, hack.Tick, guards.IsRun, guards.Tick));

        variable.Value = 1;
        Tick(m, hack, guards, 5);
        Assert.Equal((true, 5, false, 0), (hack.IsRun, hack.Tick, guards.IsRun, guards.Tick));
        Assert.Equal(5.0, m.Run.Evaluate(Igi2LevelExpressions.Create($"LevelTimer_{Hack}.nTick", "test", m.Run.Natives)));

        variable.Value = 2;
        Tick(m, hack, guards, 4);
        Assert.Equal((false, 5, true, 4), (hack.IsRun, hack.Tick, guards.IsRun, guards.Tick));

        variable.Value = 0;
        Tick(m, hack, guards, 2);
        Assert.Equal((true, 6), (guards.IsRun, guards.Tick));
    }

    // Each timer is run on its own, without the rest of the level, so nothing else moves the variable it reads.
    private static void Tick(RetailIgi2Mission1 m, Igi2LevelTimerTask hack, Igi2LevelTimerTask guards, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            hack.Run(m.Run);
            guards.Run(m.Run);
        }
    }
}
