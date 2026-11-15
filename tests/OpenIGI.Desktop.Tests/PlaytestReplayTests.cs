using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Host;
using OpenIGI.Rendering.Headless;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// A play-test recorded and played back: the level loaded afresh and given the recorded input a tick puts the player and every
/// soldier where they were, tick for tick, whatever the window gives meanwhile; and a moment gone back to is the one played.
/// </summary>
[Collection(AiScenarioSupport.CollectionName)]
public sealed class PlaytestReplayTests : IDisposable
{
    private const int Length = 420;
    private const int Midway = 150;

    private readonly AiScenarioSupport _support = new();

    /// <inheritdoc/>
    public void Dispose() => _support.Dispose();

    /// <summary>
    /// Mission 1 played for 14 s -- forward, then firing (which the guards hear) -- and played back with the window holding
    /// nothing: at the same ticks the player and the soldiers stand exactly where they stood; a seek back reaches the same
    /// moment again.
    /// </summary>
    [RequiresGameDataFact]
    public void A_recorded_play_test_plays_back_as_it_was_played()
    {
        using RecordingGraphicsDevice device = new();
        GameHost host = _support.Host(device, "-mission", "1", "-lockstep", "-skipcutscene", "-frames", "1000000");
        host.Start();
        RunUntil(host, () => host.TitlePlayerForTesting is not null && host.TickOfLevel > 10);

        Assert.StartsWith("Recording", host.PlaytestCommand("record"), StringComparison.Ordinal);
        Dictionary<int, (Vector3d Player, Vector3d[] Soldiers)> played = [];
        RunUntil(host, () =>
        {
            host.TitleHeldForTesting = host.TickOfLevel switch
            {
                >= 200 and < 260 => PlayerAction.Fire,
                >= 30 and < 300 => PlayerAction.MoveUp,
                _ => default,
            };
            Note(host, played);
            return host.TickOfLevel >= Length;
        });

        // Moved and heard: something happened worth replaying.
        Assert.True(Vector3d.Distance(played[Midway].Player, played[30].Player) > 1.0, "the player walked");

        host.TitleHeldForTesting = default;
        Assert.StartsWith("Playing back", host.PlaytestCommand("replay"), StringComparison.Ordinal);
        Dictionary<int, (Vector3d Player, Vector3d[] Soldiers)> replayed = [];
        RunUntil(host, () =>
        {
            Note(host, replayed);
            return host.TickOfLevel >= Length;
        });

        foreach (int tick in (int[])[30, Midway, 250, Length - 1])
        {
            Assert.Equal(played[tick].Player, replayed[tick].Player);
            Assert.Equal(played[tick].Soldiers, replayed[tick].Soldiers);
        }

        // Back to a moment: the level loaded afresh and run up to it.
        host.PlaytestCommand(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"seek {Midway / 30.0}"));
        Dictionary<int, (Vector3d Player, Vector3d[] Soldiers)> again = [];
        RunUntil(host, () =>
        {
            Note(host, again);
            return host.TickOfLevel >= Midway + 1;
        });

        Assert.Equal(played[Midway].Player, again[Midway].Player);
        Assert.Equal(played[Midway].Soldiers, again[Midway].Soldiers);
    }

    // What stands where at the tick the level has run to, once a tick.
    private static void Note(GameHost host, Dictionary<int, (Vector3d Player, Vector3d[] Soldiers)> into)
    {
        if (host.TitlePlayerForTesting is not { } player || into.ContainsKey(host.TickOfLevel))
        {
            return;
        }

        List<SoldierSnapshot> soldiers = [];
        host.SoldiersForTesting?.Snapshot(soldiers);
        into[host.TickOfLevel] = (player.Position, [.. soldiers.Select(static s => s.Position)]);
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
