using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Scripting.Runtime;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The natives scripts reach the level's objects through, as humanplayer.c, areaactivateqtask.c and levelflow.c answer them.</summary>
public sealed class Igi2LevelNativesTests
{
    /// <summary>With no player the player counts as dead.</summary>
    [Fact]
    public void No_player_is_a_dead_player()
    {
        (Igi2LevelNatives level, NativeRegistry natives, Igi2ScriptFunctions functions) = Setup();
        level.Bind(null, static _ => null, null);
        Assert.Equal(1, Igi2LineScript.Evaluate("HumanPlayer_isDead(0)", natives, functions));
    }

    /// <summary>An object counts as in an area when the area's volume holds its position; anything not an area holds nothing.</summary>
    [Fact]
    public void An_object_is_in_an_area_its_volume_holds()
    {
        (Igi2LevelNatives level, NativeRegistry natives, Igi2ScriptFunctions functions) = Setup();
        Igi2AreaActivateTask area = new() { Position = default, Dimensions = new Vector3f(10 * 4096f, 10 * 4096f, 10 * 4096f) };
        area.Prepare();
        Interaction.Igi2MineFieldTask near = new() { Position = new Vector3d(4096, 0, 0) };
        Interaction.Igi2MineFieldTask far = new() { Position = new Vector3d(4096 * 100, 0, 0) };
        level.Bind(null, id => id switch { 1 => area, 2 => near, 3 => far, _ => null }, null);

        Assert.Equal(1, Igi2LineScript.Evaluate("AreaActivate_IdInArea(1, 2)", natives, functions));
        Assert.Equal(0, Igi2LineScript.Evaluate("AreaActivate_IdInArea(1, 3)", natives, functions));
        Assert.Equal(0, Igi2LineScript.Evaluate("AreaActivate_IdInArea(2, 1)", natives, functions));
    }

    /// <summary>The time of day is the hour times the ticks of an hour, thirty a second.</summary>
    [Fact]
    public void The_time_of_day_is_set_in_ticks()
    {
        (Igi2LevelNatives level, NativeRegistry natives, Igi2ScriptFunctions functions) = Setup();
        Igi2LevelFlowTask flow = new();
        level.Bind(null, static _ => null, flow);
        Igi2LineScript.Evaluate("LevelFlow_SetTimeOfDay(6.5)", natives, functions);
        Assert.Equal(702000, flow.TimeOfDayTick);
    }

    /// <summary>
    /// The designator's held ticks count from the first hit for the task its dot rests on, and nought for another; with no dot, a
    /// task that does not exist matches the empty dot, as the original compares.
    /// </summary>
    [Fact]
    public void The_designators_held_ticks_are_for_its_task()
    {
        (Igi2LevelNatives level, NativeRegistry natives, Igi2ScriptFunctions functions) = Setup();
        object target = new();
        level.Bind(null, id => id == 7 ? target : null, null);
        level.LaserDesignator.HitTask = target;
        level.LaserDesignator.TickFirstHit = -40;
        Assert.Equal(40, Igi2LineScript.Evaluate("LaserDesignator_GetTicksHeldOnTask(7)", natives, functions));
        Assert.Equal(0, Igi2LineScript.Evaluate("LaserDesignator_GetTicksHeldOnTask(8)", natives, functions));
    }

    private static (Igi2LevelNatives Level, NativeRegistry Natives, Igi2ScriptFunctions Functions) Setup()
    {
        NativeRegistry natives = new();
        Igi2ScriptFunctions functions = new();
        Igi2LevelNatives level = new();
        level.Register(natives, functions);
        return (level, natives, functions);
    }
}
