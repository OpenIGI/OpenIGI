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

    /// <summary>Frames are written as their three angles, as <c>Level_PrintReal32x9Handler</c> writes them, and read back as the frame.</summary>
    [Fact]
    public void Frames_are_written_as_their_angles()
    {
        float[] frame = EngineEuler.Rotation(0f, 0f, 1.5f);
        Igi2StateWriter writer = new();
        writer.Real9(frame);
        Assert.Equal(3, writer.Arguments.Split(", ", StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.EndsWith(Igi2StateWriter.Format(1.5f), writer.Arguments, StringComparison.Ordinal);
    }

    /// <summary>
    /// The player is saved with its weapon and its view as children of its own, and loaded into a fresh mission is where it was,
    /// carries what it carried with the same rounds, looks where it looked and has set off as many alarms.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_player_saved_and_loaded_is_as_it_was()
    {
        RetailIgi2Mission1 played = RetailIgi2Mission1.Load();
        Player.Igi2HumanPlayer before = Attach(played);
        Weapons.Igi2Inventory carried = before.Weapon.Inventory!;
        carried.EnableWeaponType("WEAPON_ID_MP5SD", addAmmo: true);
        carried.Activate(carried.FindWeapon("WEAPON_ID_MP5SD")!);
        before.Position = new Core.Numerics.Vector3d(1000.5, -2000.25, 300.125);
        before.View.Pitch = 0.25f;
        before.TimesAlarmTriggered = 3;
        before.SavesUsed = 2;
        before.FlashBangFlash = 3.25f;

        string script = Igi2SaveGame.Create(played.Tree[0], new Igi2SaveInfo(Igi2SaveInfo.CurrentVersion, 11, 0))!;
        Assert.Contains("\"HumanPlayer\"", script, StringComparison.Ordinal);

        RetailIgi2Mission1 fresh = RetailIgi2Mission1.Load();
        Player.Igi2HumanPlayer after = Attach(fresh);
        Igi2SaveGame.Load(script, fresh.Tree[0], fresh.Run.Natives, fresh.Run.Functions);
        Weapons.Igi2Inventory loaded = after.Weapon.Inventory!;
        Assert.Equal(before.Position, after.Position);
        Assert.Equal(0.25f, after.View.Pitch);
        Assert.Equal(3, after.TimesAlarmTriggered);
        Assert.Equal(2, after.SavesUsed);
        Assert.Equal(3.25f, after.FlashBangFlash);
        Assert.Equal("WEAPON_ID_MP5SD", loaded.Current?.WeaponType.WeaponId);
        Assert.Equal(
            carried.Weapons.Select(static w => (w.Number, w.IsActive, w.Loaded(0), w.Loaded(1))),
            loaded.Weapons.Select(static w => (w.Number, w.IsActive, w.Loaded(0), w.Loaded(1))));
        Assert.Equal(carried.Ammo.Select(static a => (a.Number, a.Count)), loaded.Ammo.Select(static a => (a.Number, a.Count)));
    }

    private static Player.Igi2HumanPlayer Attach(RetailIgi2Mission1 mission)
    {
        Player.Igi2PlayerWeapons weapons = Player.Igi2Gameplay.Arm(
            mission.Services!, mission.Tree[0], mission.Services!.Require<Player.Igi2PlayerTuning>())!;
        (Player.Igi2HumanPlayer player, _, _) = Igi2PlayerFakes.OnFloor(weapons);
        mission.Tree.OfType<Player.Igi2HumanPlayerTask>().Single().Human = player;
        return player;
    }
}
