using OpenIGI.Game.Igi2.Interaction;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Terminals, as terminal.c runs them.</summary>
public sealed class Igi2TerminalTests
{
    /// <summary>
    /// A finished hack is made on the next tick, which alone is hacked this tick; the terminal stays hacked, and in use while it was
    /// hacked at in the last two ticks.
    /// </summary>
    [Fact]
    public void A_hack_is_made_the_tick_after_it_finishes()
    {
        Igi2TerminalTask terminal = new();
        Flow.Igi2LevelRun level = Level();
        Assert.True(terminal.Terminal.IsActive);
        Assert.Equal(4f, terminal.HackTime);
        Assert.Equal("tba_hackcomp", terminal.ActivateSound);

        terminal.Activate(0);
        terminal.Finish();
        Assert.False(terminal.Terminal.IsHacked);
        terminal.Run(level);
        Assert.Equal((true, true, true), (terminal.Terminal.IsHacked, terminal.Terminal.IsHackedThisTick, terminal.Terminal.IsInUse));
        terminal.Run(level);
        Assert.Equal((true, false), (terminal.Terminal.IsHacked, terminal.Terminal.IsHackedThisTick));
    }

    /// <summary>An exploded terminal can no longer be hacked.</summary>
    [Fact]
    public void An_exploded_terminal_is_inactive()
    {
        Igi2TerminalTask terminal = new();
        terminal.Explosive.Reset();
        terminal.Explosive.WeaponHit(1000f);
        for (int tick = 0; tick < 100; tick++)
        {
            terminal.Run(Level());
        }

        Assert.True(terminal.Explosive.IsExploded);
        Assert.False(terminal.Terminal.IsActive);
    }

    private static Flow.Igi2LevelRun Level()
    {
        Engine.Tasks.TaskTypeRegistry types = new();
        Engine.Tasks.TaskTree tree = new(types);
        Engine.Tasks.GameTask root = tree.New(null, types.Register<Engine.Tasks.ContainerTask>("Container"));
        return new(root, new OpenIGI.Scripting.Runtime.NativeRegistry(), new Scripting.Igi2ScriptFunctions());
    }
}
