using System.Diagnostics;
using System.Globalization;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Host;
using OpenIGI.Rendering.Headless;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// A level held in memory and put back: mission 1, its intro running (the truck driven in, the guards about), recorded at a
/// moment, played on, put back, and played on again -- the second run is the first, every object of the level exactly as it was.
/// </summary>
[Collection(AiScenarioSupport.CollectionName)]
public sealed class LevelCheckpointTests(ITestOutputHelper output) : IDisposable
{
    private const int Run = 300;

    private readonly AiScenarioSupport _support = new();

    /// <inheritdoc/>
    public void Dispose() => _support.Dispose();

    [RequiresGameDataFact]
    public void A_level_put_back_plays_on_exactly_as_it_did()
    {
        using RecordingGraphicsDevice device = new();
        GameHost host = _support.Host(device, "-mission", "1", "-lockstep", "-frames", "1000000");
        host.Start();
        RunUntil(host, () => host.TitlePlayerForTesting is not null && host.TickOfLevel > 90);

        Stopwatch watch = Stopwatch.StartNew();
        LevelCheckpoint start = host.CaptureLevel(0)!;
        double captureMs = watch.Elapsed.TotalMilliseconds;
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"capture: {start.State.Objects} objects, {start.State.Bytes / 1048576.0:0.0} MB, {captureMs:0} ms"));
        foreach ((string type, int count, long bytes) in start.State.Census().Take(40))
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {bytes / 1024.0,10:0} KB {count,7} {type}"));
        }

        (List<Vector3d[]> first, LevelCheckpoint end) = Play(host, start);

        watch.Restart();
        Assert.True(host.RestoreLevel(start));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"restore: {watch.Elapsed.TotalMilliseconds:0} ms"));
        (List<Vector3d[]> second, LevelCheckpoint again) = Play(host, start);

        IReadOnlyList<string> differences = end.State.Differences(again.State);
        foreach (string difference in differences)
        {
            output.WriteLine(difference);
        }

        Assert.Equal(first.Count, second.Count);
        for (int tick = 0; tick < first.Count; tick++)
        {
            Assert.True(first[tick].SequenceEqual(second[tick]), $"positions differ at tick {tick} after the restore");
        }

        Assert.Empty(differences);

        // The same checkpoint again: it was never written into, so a third run is the first again too.
        Assert.True(host.RestoreLevel(start));
        (_, LevelCheckpoint third) = Play(host, start);
        Assert.Empty(end.State.Differences(third.State));

        // A later capture, its types compiled and the level's unchanged data shared with the one before.
        watch.Restart();
        LevelCheckpoint later = host.CaptureLevel(0, third)!;
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"later capture: {watch.Elapsed.TotalMilliseconds:0} ms, {later.State.OwnBytes / 1048576.0:0.00} MB of its own"));
    }

    // The level played on from a checkpoint for a while: where the player and the soldiers stand each tick, and the level at the end.
    private static (List<Vector3d[]> Positions, LevelCheckpoint End) Play(GameHost host, LevelCheckpoint from)
    {
        List<Vector3d[]> positions = [];
        List<SoldierSnapshot> soldiers = [];
        int begun = host.TickOfLevel;
        int noted = -1;
        RunUntil(host, () =>
        {
            int tick = host.TickOfLevel - begun;
            if (tick != noted)
            {
                noted = tick;
                soldiers.Clear();
                host.SoldiersForTesting?.Snapshot(soldiers);
                positions.Add([host.TitlePlayerForTesting!.Position, .. soldiers.Select(static s => s.Position)]);
            }

            return tick >= Run;
        });

        return (positions, host.CaptureLevel(Run, from)!);
    }

    private static void RunUntil(GameHost host, Func<bool> done)
    {
        for (int i = 0; i < 200_000 && !done(); i++)
        {
            Assert.True(host.Step());
        }

        Assert.True(done());
    }
}
