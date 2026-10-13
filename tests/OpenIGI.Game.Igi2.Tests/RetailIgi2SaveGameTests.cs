using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Saves;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Save games (savegame.c, serialize.c): the script a level's state is written as, and read back.</summary>
public sealed class RetailIgi2SaveGameTests
{
    /// <summary>Floats are written as C's <c>%.20G</c> writes them, the exponent in three digits.</summary>
    [Fact]
    public void Floats_are_written_to_twenty_significant_figures()
    {
        Assert.Equal("0.10000000149011611938", Igi2StateWriter.Format(0.1f));
        Assert.Equal("0.5", Igi2StateWriter.Format(0.5f));
        Assert.Equal("1024", Igi2StateWriter.Format(1024f));
        Assert.Equal("9.9999997473787516356E-006", Igi2StateWriter.Format(1e-5f));
        Assert.Equal("-3", Igi2StateWriter.Format(-3.0));
    }

    /// <summary>Raw bytes are written as words most significant byte first in memory order, then the bytes over, and read back.</summary>
    [Fact]
    public void Arrays_round_trip_as_retail_prints_them()
    {
        Igi2StateWriter writer = new();
        writer.Array((ReadOnlySpan<float>)[1f, -2.5f]);
        writer.Array((ReadOnlySpan<byte>)[1, 2, 3, 4, 5, 6]);
        Assert.Equal(", 2, 0, 0x0000803f, 0x000020c0, 1, 2, 0x01020304, 0x05, 0x06", writer.Arguments);
    }

    /// <summary>
    /// Mission 11 run four hundred ticks is saved; its header names the version, the mission and the tick, and every task that keeps
    /// state has a <c>DefineTaskState</c>. Loaded into the mission fresh, the level flow, its timers, containers and variables are as
    /// they were.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_level_saved_and_loaded_is_as_it_was()
    {
        RetailIgi2Mission1 played = RetailIgi2Mission1.Load();
        for (int tick = 0; tick < 400; tick++)
        {
            played.Step();
        }

        Engine.Tasks.GameTask root = played.Tree[0];
        string? script = Igi2SaveGame.Create(root, new Igi2SaveInfo(Igi2SaveInfo.CurrentVersion, 11, played.Run.LevelFlow!.Tick));
        Assert.NotNull(script);
        Assert.StartsWith($"SaveGameInfo(63, 11, {played.Run.LevelFlow.Tick});\r\nBreakScript();\r\n", script, StringComparison.Ordinal);
        Assert.Contains("DefineTaskState(", script, StringComparison.Ordinal);
        Igi2SaveInfo header = Igi2SaveGame.Check(script);
        Assert.True(header.IsValid);
        Assert.Equal(11, header.MissionId);

        RetailIgi2Mission1 fresh = RetailIgi2Mission1.Load();
        Igi2SaveGame.Load(script, fresh.Tree[0], fresh.Run.Natives, fresh.Run.Functions);
        Assert.Equal(played.Run.LevelFlow.Tick, fresh.Run.LevelFlow!.Tick);
        Assert.Equal(played.Run.LevelFlow.TimeOfDayTick, fresh.Run.LevelFlow.TimeOfDayTick);
        Assert.Equal(
            played.Tree.OfType<Igi2ConditionalContainerTask>().Select(static c => (c.Id, c.IsRun)),
            fresh.Tree.OfType<Igi2ConditionalContainerTask>().Select(static c => (c.Id, c.IsRun)));
        Assert.Equal(
            played.Tree.OfType<Igi2EditVariableTask>().Select(static v => (v.Id, v.Value)),
            fresh.Tree.OfType<Igi2EditVariableTask>().Select(static v => (v.Id, v.Value)));
        Assert.Equal(
            played.Tree.OfType<Igi2LevelTimerTask>().Select(static t => (t.Id, t.Tick)),
            fresh.Tree.OfType<Igi2LevelTimerTask>().Select(static t => (t.Id, t.Tick)));
        Assert.Contains(played.Tree.OfType<Igi2ConditionalContainerTask>(), static c => c.IsRun);
    }
}
