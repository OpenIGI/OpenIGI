using OpenIGI.Game.Igi2.Flow;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Mission timers, as missiontimer.c runs and draws them.</summary>
public sealed class Igi2MissionTimerTests
{
    /// <summary>
    /// A timer waits for its start expression, then counts its seconds' ticks down, held while its pause expression holds, shows
    /// minutes and seconds, and once at nought finishes on the next tick.
    /// </summary>
    [Fact]
    public void A_timer_counts_down_once_started()
    {
        Igi2MissionTimerTask timer = new() { StartExpression = "1", PauseExpression = "0", TimeInSeconds = 61.5f };
        timer.Prepare();
        Assert.Equal(1845, timer.TicksLeft);
        Assert.Equal("01 : 01", timer.DisplayText);

        Igi2LevelRun level = Level();
        timer.Run(level);
        Assert.Equal((Igi2MissionTimerStatus.Running, 1845), (timer.Status, timer.TicksLeft));
        for (int tick = 0; tick < 1845; tick++)
        {
            timer.Run(level);
        }

        Assert.Equal((Igi2MissionTimerStatus.Running, 0), (timer.Status, timer.TicksLeft));
        timer.Run(level);
        Assert.Equal(Igi2MissionTimerStatus.Finished, timer.Status);
        Assert.False(timer.IsShown);
    }

    /// <summary>A timer whose pause expression holds does not count.</summary>
    [Fact]
    public void A_paused_timer_holds()
    {
        Igi2MissionTimerTask timer = new() { StartExpression = "1", PauseExpression = "1", TimeInSeconds = 1f };
        timer.Prepare();
        Igi2LevelRun level = Level();
        for (int tick = 0; tick < 10; tick++)
        {
            timer.Run(level);
        }

        Assert.Equal((Igi2MissionTimerStatus.Running, 30), (timer.Status, timer.TicksLeft));
    }

    private static Igi2LevelRun Level()
    {
        Engine.Tasks.TaskTypeRegistry types = new();
        Engine.Tasks.TaskTree tree = new(types);
        Engine.Tasks.GameTask root = tree.New(null, types.Register<Engine.Tasks.ContainerTask>("Container"));
        return new(root, new OpenIGI.Scripting.Runtime.NativeRegistry(), new Scripting.Igi2ScriptFunctions());
    }
}
