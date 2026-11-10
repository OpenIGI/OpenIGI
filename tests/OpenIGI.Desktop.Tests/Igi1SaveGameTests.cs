using OpenIGI.Game.Igi1.Saves;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Saves;
using OpenIGI.Host;
using OpenIGI.Rendering.Headless;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// Project I.G.I.'s save games on a real mission, played headless: a level played on and saved, read onto the same mission
/// started afresh or onto the level as it was played on after, is saved again as the same script -- so nothing a save keeps is
/// lost on the way out or back, whatever became of the level it is read onto.
/// </summary>
/// <remarks>Not retail: the original kept no save games. One mission a process.</remarks>
[Collection(AiScenarioSupport.CollectionName)]
public sealed class Igi1SaveGameTests : IDisposable
{
    private readonly AiScenarioSupport _support = new();

    // The mission played: the first, or the one SAVE_MISSION names, so every mission can be put through the same check, one a
    // process.
    private static string Mission => Environment.GetEnvironmentVariable("SAVE_MISSION") is { Length: > 0 } named ? named : "1";

    /// <inheritdoc/>
    public void Dispose() => _support.Dispose();

    /// <summary>Mission 1, ten seconds in: saved, read onto a fresh level, and saved again the same.</summary>
    [RequiresGameDataFact]
    public void A_level_saved_and_read_onto_a_fresh_one_is_saved_the_same()
    {
        (_, string played) = Saved(ticksPlayed: 300, read: null);
        Assert.Contains(SaveScript.DefineTaskState + "(", played, StringComparison.Ordinal);
        Assert.Contains("Igi1Objectives(", played, StringComparison.Ordinal);
        Assert.Contains("Igi1Player(", played, StringComparison.Ordinal);

        (string fresh, string again) = Saved(ticksPlayed: 0, read: played);
        Assert.NotEqual(Lines(fresh), Lines(played));
        if (Environment.GetEnvironmentVariable("SAVE_DUMP") is { Length: > 0 } dump)
        {
            File.WriteAllText(dump + ".a", played);
            File.WriteAllText(dump + ".b", again);
        }

        Assert.Equal(Lines(played), Lines(again));

        static string[] Lines(string script) => script.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// A level saved, then played on -- more of its soldiers dead, its player elsewhere and hurt, time gone by -- and the save read
    /// onto that same level, without the mission being brought up again, is saved again as the same script: a load puts back
    /// everything a save keeps, whatever became of the level since.
    /// </summary>
    [RequiresGameDataFact]
    public void A_save_read_onto_the_level_played_on_puts_it_back()
    {
        string folder = Path.Combine(Path.GetTempPath(), "openigi-inplace-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(Igi1SaveFormat.DirectoryVariable, folder);
        try
        {
            ReadOntoThePlayedLevel();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Igi1SaveFormat.DirectoryVariable, null);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    private void ReadOntoThePlayedLevel()
    {
        using RecordingGraphicsDevice device = new();
        GameHost host = _support.Host(device, "-mission", Mission, "-skipcutscene", "-lockstep", "-nosave", "-frames", "1000000");
        host.Start();
        for (int i = 0; i < 20_000 && (host.Player() is null || host.Soldiers() is null); i++)
        {
            Assert.True(host.Step());
        }

        MissionWorld world = host.Mission();
        Steps(300);
        Wreck(kills: 1);
        Steps(400);

        // A mission that opens on a scene, or with its menus off, is saved once it lets itself be; one that never does within
        // two minutes has nothing here to check.
        for (int i = 0; i < 3600 && !host.SaveGames()!.CanSave; i++)
        {
            Assert.True(host.Step());
        }

        if (!host.SaveGames()!.CanSave)
        {
            return;
        }

        SaveContext context = Igi1SaveFormat.Context(world);
        string saved = SaveScript.Create(world.LevelRoot!, context.Info(1, 0), context)!;
        Assert.True(host.SaveGames()!.Save(0));
        int savesUsed = world.SavesUsed;
        int deadWhenSaved = host.Soldiers()!.Simulation.SoldierViews.Count(static v => v.IsDead);

        // Played on: a gun and a box of rounds taken from where the level laid them, if it laid any; three more dead, long
        // enough for their bodies to go; the player moved on, hurt again and firing.
        OpenIGI.Game.World.GunPickup? gun = world.GunPickups?.Items.FirstOrDefault(static p => p.Task is not null && !p.IsCollected);
        OpenIGI.Game.World.GenericPickup? box = world.GenericPickups?.Items.FirstOrDefault(static p => !p.IsCollected);
        for (int i = 0; i < 120 && gun is { IsCollected: false }; i++)
        {
            host.Player()!.StandAt(gun.Position, 0f);
            Steps(1);
        }

        for (int i = 0; i < 120 && box is { IsCollected: false }; i++)
        {
            host.Player()!.StandAt(box.Position, 0f);
            Steps(1);
        }

        Steps(5);
        Wreck(kills: 3);

        // And the player stood in front of a guard, to be seen, shot at and hunted: minds in a fight, an alarm perhaps raised,
        // the player perhaps dead.
        float before = world.PlayerHealth.RemainingFraction;
        foreach (OpenIGI.Game.Igi1.Player.SoldierView guard in host.Soldiers()!.Simulation.SoldierViews.Where(static v => !v.IsDead).ToArray())
        {
            host.Player()!.StandAt(new OpenIGI.Core.Numerics.Vector3d(guard.Position.X + 12288.0, guard.Position.Y, guard.Position.Z), 0f);
            Steps(150);
            if (world.IsPlayerDead || world.PlayerHealth.RemainingFraction < before)
            {
                break;
            }
        }

        Steps(600);
        if (Environment.GetEnvironmentVariable("SAVE_DUMP") is { Length: > 0 } fight)
        {
            File.WriteAllLines(
                fight + ".fight",
                [$"dead={world.IsPlayerDead} health={world.PlayerHealth.RemainingFraction}", .. host.Soldiers()!.Simulation.BrainPairsForTesting.Select(static p => $"{p.Key.Soldier.Id} {p.Value.DescribeTick()}")]);
        }

        Wreck(kills: 1);
        Steps(60);
        Assert.True(host.Soldiers()!.Simulation.SoldierViews.Count(static v => v.IsDead) > deadWhenSaved);

        // The load the game makes: onto this level, the mission not brought up again.
        Assert.True(host.SaveGames()!.Load(0));
        Assert.False(host.SaveGames()!.LoadRestartsMission);
        Assert.Same(world, host.Mission());
        if (Environment.GetEnvironmentVariable("SAVE_DUMP") is { Length: > 0 } afterLoad)
        {
            File.WriteAllText(afterLoad + ".afterload", $"dead {world.IsPlayerDead} task {world.PlayerTask?.IsDead} failed {world.Objectives?.IsFailed} health {world.PlayerHealth.Damage}");
        }
        Assert.False(world.IsPlayerDead);
        Assert.True(gun is null || !gun.IsCollected, "The gun taken after the save does not lie there again.");
        Assert.True(
            box is null || !box.IsCollected,
            $"The box taken after the save does not lie there again: {box?.Task.Type.Name} {box?.Task.Id} deleted {box?.Task.IsDeleted} parent {box?.Task.Parent?.Type.Name} {box?.Task.Parent?.Id}.");
        Assert.Equal(1, world.GameLoads);
        world.GameLoads = 0;
        world.SavesUsed = savesUsed - 1;
        context = Igi1SaveFormat.Context(world);
        string again = SaveScript.Create(world.LevelRoot!, context.Info(1, 0), context)!;
        if (Environment.GetEnvironmentVariable("SAVE_DUMP") is { Length: > 0 } dump)
        {
            File.WriteAllText(dump + ".saved", saved);
            File.WriteAllText(dump + ".again", again);
        }

        Assert.Equal(Lines(saved), Lines(again));
        Assert.Equal(deadWhenSaved, host.Soldiers()!.Simulation.SoldierViews.Count(static v => v.IsDead));

        // And it plays on from there: the clock runs, the player lives.
        long clock = world.PlayerTicks;
        if (Environment.GetEnvironmentVariable("SAVE_DUMP") is { Length: > 0 } trace)
        {
            List<string> lines = [];
            float damage = -1f;
            for (int i = 0; i < 300; i++)
            {
                Assert.True(host.Step());
                if (world.PlayerHealth.Damage != damage || i % 50 == 0)
                {
                    damage = world.PlayerHealth.Damage;
                    OpenIGI.Core.Numerics.Vector3d at = host.Player()!.PositionUnits;
                    double nearest = host.Soldiers()!.Simulation.SoldierViews.Where(static v => !v.IsDead).Min(v => OpenIGI.Core.Numerics.Vector3d.Distance(v.Position, at));
                    lines.Add($"step {i} tick {world.PlayerTicks} damage {damage} hits {world.PlayerHealth.Hits} dead {world.IsPlayerDead} task {world.PlayerTask?.IsDead} failed {world.Objectives?.IsFailed} mines {host.Player()!.Grenades.Mines.Count} grenades {host.Player()!.Grenades.Grenades.Count} nearest {nearest:F0} guardRounds {host.GuardRoundsFired()} hitsOnPlayer {host.GuardHitsOnPlayer()}");
                }
            }

            File.WriteAllLines(trace + ".trace", lines);
        }
        else
        {
            Steps(300);
        }
        Assert.Equal(deadWhenSaved, host.Soldiers()!.Simulation.SoldierViews.Count(static v => v.IsDead));
        Assert.True(world.PlayerTicks > clock);
        Assert.False(world.IsPlayerDead);
        if (Environment.GetEnvironmentVariable("SAVE_DUMP") is { Length: > 0 } later)
        {
            // A diagnostic beside the check: the same save read onto a level just come up and played as long. Where the two
            // differ, the level played on kept something a save does not hold.
            context = Igi1SaveFormat.Context(world);
            File.WriteAllText(later + ".inplace", SaveScript.Create(world.LevelRoot!, context.Info(1, 0), context)!);
            File.WriteAllLines(
                later + ".minds-inplace",
                host.Soldiers()!.Simulation.BrainPairsForTesting.Select(static p => $"{p.Key.Soldier.Id} {p.Value.DescribeTick()}"));
            File.WriteAllText(later + ".fresh", Played(saved).Later);
        }

        static string[] Lines(string script) => script.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        void Steps(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Assert.True(host.Step());
            }
        }

        void Wreck(int kills)
        {
            for (int i = 0; i < kills; i++)
            {
                host.Soldiers()!.Simulation.KillForTesting(host.Soldiers()!.Simulation.SoldierViews.First(static v => !v.IsDead));
            }

            OpenIGI.Game.Igi1.Player.PlayerCamera rig = host.Player()!;
            rig.StandAt(new OpenIGI.Core.Numerics.Vector3d(rig.PositionUnits.X + 4096.0, rig.PositionUnits.Y, rig.PositionUnits.Z), rig.Yaw + 1f, 0.25f);
            world.PlayerHealth.ApplyDirect(0, 0.2f);
            if (rig.Weapons is { Carried.Count: > 0 } weapons)
            {
                weapons.Carried[0].Gun.State.RoundsInClip = Math.Max(0, weapons.Carried[0].Gun.State.RoundsInClip - 2);
            }

            rig.Grenades.Lay(new OpenIGI.Game.Weapons.PlacedMine(rig.PositionUnits, OpenIGI.Core.Numerics.Matrix3f.Identity, string.Empty));
            Assert.True(rig.Grenades.SpawnWithVelocity(rig.PositionUnits, default, default, fuseTicks: 9000));
        }
    }

    /// <summary>
    /// The quick save key writes the quick slot and the quick load key reads that save onto the level that is up: the player,
    /// moved after the save, stands where it was saved, and the mission counts one game loaded.
    /// </summary>
    [RequiresGameDataFact]
    public void The_quick_keys_save_and_load()
    {
        string folder = Path.Combine(Path.GetTempPath(), "openigi-quick-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(Igi1SaveFormat.DirectoryVariable, folder);
        try
        {
            using RecordingGraphicsDevice device = new();
            OpenIGI.Input.InputState keys = new();
            GameHost host = _support.Host(device, "-mission", Mission, "-skipcutscene", "-lockstep", "-nosave", "-difficulty", "easy", "-frames", "1000000");
            host.Start();
            host.ScriptedViewInput = keys;
            for (int i = 0; i < 20_000 && (host.Player() is null || host.Soldiers() is null); i++)
            {
                Assert.True(host.Step());
            }

            Steps(host, 200);
            OpenIGI.Core.Numerics.Vector3d saved = host.Player()!.PositionUnits;
            Press(host, keys, OpenIGI.Input.KeyCode.F5);
            Assert.Single(Directory.GetFiles(Path.Combine(folder, "saves", "igi1"), "*-5.igisave"));

            host.Player()!.StandAt(new OpenIGI.Core.Numerics.Vector3d(saved.X + 8192.0, saved.Y, saved.Z), 0f);
            Press(host, keys, OpenIGI.Input.KeyCode.F9);
            for (int i = 0; i < 20_000 && host.Mission().GameLoads == 0; i++)
            {
                Assert.True(host.Step());
            }

            Assert.Equal(1, host.Mission().GameLoads);
            Assert.True(OpenIGI.Core.Numerics.Vector3d.Distance(saved, host.Player()!.PositionUnits) < 4096.0);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Igi1SaveFormat.DirectoryVariable, null);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        static void Steps(GameHost host, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Assert.True(host.Step());
            }
        }

        static void Press(GameHost host, OpenIGI.Input.InputState keys, OpenIGI.Input.KeyCode key)
        {
            keys.Keyboard.SetKey(key, down: true);
            Steps(host, 1);
            keys.Keyboard.SetKey(key, down: false);
            keys.Keyboard.BeginFrame();
            Steps(host, 3);
        }
    }

    /// <summary>
    /// A diagnostic, not a check: mission 1 played ten seconds and saved, played ten more and saved again; the first save read
    /// onto a fresh level, played the same ten and saved. Where the two later saves differ is state a save does not yet keep, or
    /// that its mind starting afresh changes. Runs only when <c>SAVE_DUMP</c> names where to write the two.
    /// </summary>
    [RequiresGameDataFact]
    public void Diagnostic_how_far_a_loaded_level_drifts_from_the_one_played_on()
    {
        if (Environment.GetEnvironmentVariable("SAVE_DUMP") is not { Length: > 0 } dump)
        {
            return;
        }

        (string first, string later) = Played(read: null);
        (_, string loadedLater) = Played(read: first);
        File.WriteAllText(dump + ".later", later);
        File.WriteAllText(dump + ".loaded", loadedLater);
    }

    // Mission 1 played ten seconds (or a save of that read onto it), saved, played ten more and saved again.
    private (string First, string Later) Played(string? read)
    {
        using RecordingGraphicsDevice device = new();
        GameHost host = _support.Host(device, "-mission", Mission, "-skipcutscene", "-lockstep", "-frames", "1000000");
        host.Start();
        for (int i = 0; i < 20_000 && (host.Player() is null || host.Soldiers() is null); i++)
        {
            Assert.True(host.Step());
        }

        MissionWorld world = host.Mission();
        if (read is null)
        {
            Run(host, 300);
        }
        else
        {
            Assert.True(SaveScript.Load(read, world.LevelRoot!, Igi1SaveFormat.Context(world)).IsValid);
        }

        SaveContext context = Igi1SaveFormat.Context(world);
        string first = SaveScript.Create(world.LevelRoot!, context.Info(1, 0), context)!;
        Run(host, 300);
        if (Environment.GetEnvironmentVariable("SAVE_DUMP") is { Length: > 0 } dump)
        {
            File.WriteAllLines(
                dump + (read is null ? ".minds-played" : ".minds-loaded"),
                host.Soldiers()!.Simulation.BrainPairsForTesting.Select(static p => $"{p.Key.Soldier.Id} {p.Value.DescribeTick()}"));
        }

        context = Igi1SaveFormat.Context(world);
        return (first, SaveScript.Create(world.LevelRoot!, context.Info(1, 0), context)!);

        static void Run(GameHost host, int ticks)
        {
            long from = host.Soldiers()!.Simulation.TickCount;
            for (int i = 0; i < 200_000 && host.Soldiers()!.Simulation.TickCount < from + ticks; i++)
            {
                Assert.True(host.Step());
            }
        }
    }

    // Mission 1 brought up and played a while, and saved; then a save read onto it, if one is given, and saved again.
    private (string Before, string After) Saved(int ticksPlayed, string? read)
    {
        using RecordingGraphicsDevice device = new();
        GameHost host = _support.Host(device, "-mission", Mission, "-skipcutscene", "-lockstep", "-frames", "1000000");
        host.Start();
        for (int i = 0; i < 20_000 && (host.Player() is null || host.Soldiers() is null); i++)
        {
            Assert.True(host.Step());
        }

        Assert.NotNull(host.Soldiers());
        long from = host.Soldiers()!.Simulation.TickCount;
        for (int i = 0; i < 200_000 && host.Soldiers()!.Simulation.TickCount < from + ticksPlayed; i++)
        {
            Assert.True(host.Step());
        }

        MissionWorld world = host.Mission();
        if (read is null)
        {
            // One soldier dead and fallen, so a save holds a body as well as the living.
            host.Soldiers()!.Simulation.KillForTesting(host.Soldiers()!.Simulation.SoldierViews.First(static v => !v.IsDead));
            for (int i = 0; i < 400; i++)
            {
                Assert.True(host.Step());
            }

            Assert.Contains(host.Soldiers()!.Simulation.SoldierViews, static v => v.IsDead);

            // And the player somewhere else, looking another way, hurt, a round short.
            OpenIGI.Game.Igi1.Player.PlayerCamera rig = host.Player()!;
            rig.StandAt(new OpenIGI.Core.Numerics.Vector3d(rig.PositionUnits.X + 4096.0, rig.PositionUnits.Y, rig.PositionUnits.Z), rig.Yaw + 1f, 0.25f);
            world.PlayerHealth.ApplyDirect(0, 0.5f);
            if (rig.Weapons is { Carried.Count: > 0 } weapons)
            {
                weapons.Carried[0].Gun.State.RoundsInClip = 3;
            }

            // And a mine laid at its feet and a grenade lying beside it, its fuse running.
            rig.Grenades.Lay(new OpenIGI.Game.Weapons.PlacedMine(rig.PositionUnits, OpenIGI.Core.Numerics.Matrix3f.Identity, string.Empty));
            Assert.True(rig.Grenades.SpawnWithVelocity(rig.PositionUnits, default, default, fuseTicks: 9000));
        }

        SaveContext context = Igi1SaveFormat.Context(world);
        string before = SaveScript.Create(world.LevelRoot!, context.Info(1, 0), context)!;
        if (read is null)
        {
            return (before, before);
        }

        List<string> misread = [];
        Assert.True(SaveScript.Load(read, world.LevelRoot!, context, misread).IsValid);
        Assert.Empty(misread);
        context = Igi1SaveFormat.Context(world);
        Assert.Contains(host.Soldiers()!.Simulation.SoldierViews, static v => v.IsDead);
        Assert.Single(host.Player()!.Grenades.Mines);
        Assert.Single(host.Player()!.Grenades.Grenades);
        string after = SaveScript.Create(world.LevelRoot!, context.Info(1, 0), context)!;

        // And the level read back plays on: its soldiers' minds start afresh from where they stood, its dead stay dead.
        for (int i = 0; i < 300; i++)
        {
            Assert.True(host.Step());
        }

        Assert.Contains(host.Soldiers()!.Simulation.SoldierViews, static v => v.IsDead);
        return (before, after);
    }
}
