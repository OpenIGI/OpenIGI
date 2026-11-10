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
/// started afresh, is saved again as the same script -- so nothing a save keeps is lost on the way out or back.
/// </summary>
/// <remarks>Not retail: the original kept no save games. One mission a process.</remarks>
[Collection(AiScenarioSupport.CollectionName)]
public sealed class Igi1SaveGameTests : IDisposable
{
    private readonly AiScenarioSupport _support = new();

    /// <inheritdoc/>
    public void Dispose() => _support.Dispose();

    /// <summary>Mission 1, ten seconds in: saved, read onto a fresh level, and saved again the same.</summary>
    [RequiresGameDataFact]
    public void A_level_saved_and_read_onto_a_fresh_one_is_saved_the_same()
    {
        (_, string played) = Saved(ticksPlayed: 300, read: null);
        Assert.Contains(SaveScript.DefineTaskState + "(", played, StringComparison.Ordinal);
        Assert.Contains("Igi1Objectives(", played, StringComparison.Ordinal);

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

    // Mission 1 brought up and played a while, and saved; then a save read onto it, if one is given, and saved again.
    private (string Before, string After) Saved(int ticksPlayed, string? read)
    {
        using RecordingGraphicsDevice device = new();
        GameHost host = _support.Host(device, "-mission", "1", "-skipcutscene", "-lockstep", "-frames", "1000000");
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
