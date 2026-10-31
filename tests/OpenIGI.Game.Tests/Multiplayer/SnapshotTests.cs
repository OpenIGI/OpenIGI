using OpenIGI.Core.Numerics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Net;
using Xunit;

namespace OpenIGI.Game.Tests.Multiplayer;

/// <summary>Snapshots: written against what the client holds, read back exactly, and blended between ticks.</summary>
public sealed class SnapshotTests
{
    [Fact]
    public void AWholeSnapshotRoundTrips()
    {
        Snapshot snapshot = new(1000, [Human(3, 100_000, -50_000), Human(7, 4096, 8192, health: 128)]);
        Assert.True(RoundTrip(snapshot, baseline: null, out Snapshot? read));
        Assert.Equal(snapshot.Tick, read!.Tick);
        Assert.Equal(snapshot.Humans, read.Humans);
    }

    [Fact]
    public void TheRoundsThatLandedRideEverySnapshotWhole()
    {
        ReplicatedImpact wall = new(ImpactKind.Surface, 2, new Int3(1000, -2000, 300), new Vector3f(0.6f, 0.8f, 0f));
        ReplicatedImpact wound = new(ImpactKind.Blood, 0, new Int3(-5, 7, 9), new Vector3f(0f, 0f, -1f));
        Snapshot before = new(30, [Human(1, 0, 0)]);
        Snapshot after = new(31, [Human(1, 0, 0)], Impacts: [wall, wound]);

        Assert.True(RoundTrip(after, before, out Snapshot? read));
        Assert.Equal(2, read!.Impacts!.Length);
        Assert.Equal(wall.Position, read.Impacts[0].Position);
        Assert.Equal(2, read.Impacts[0].Source);
        Assert.Equal(ImpactKind.Blood, read.Impacts[1].Kind);
        Assert.Equal(-1f, read.Impacts[1].Direction.Z, 2);
        Assert.Equal(0.6f, read.Impacts[0].Direction.X, 2);

        // Events, not state: a snapshot without any carries none, whatever its baseline had.
        Assert.True(RoundTrip(new Snapshot(32, [Human(1, 0, 0)]), after, out Snapshot? next));
        Assert.Null(next!.Impacts);
    }

    [Fact]
    public void WhatAPlayerFeltRidesToItsOwnGame()
    {
        ReplicatedSense flash = new(3, 4, new Vector3f(0.5f, -0.25f, 0.75f));
        ReplicatedSense shake = new(3, 3, A: 204.8f, B: 0.013f, Ticks: 90);
        ReplicatedSense sound = new(2, 7, Text: "elfence_spark1");
        Snapshot before = new(40, [Human(1, 0, 0)]);
        Snapshot after = new(41, [Human(1, 0, 0)], Senses: [flash, shake, sound]);

        Assert.True(RoundTrip(after, before, out Snapshot? read));
        Assert.Equal([flash, shake, sound], read!.Senses!);

        // Events, not state: the next snapshot carries none of them again.
        Assert.True(RoundTrip(new Snapshot(42, [Human(1, 0, 0)]), after, out Snapshot? next));
        Assert.Null(next!.Senses);
    }

    [Fact]
    public void TheDoorsRideWholeAndAgainstABaseline()
    {
        Snapshot whole = new(20, [Human(1, 0, 0)], [0b0000_0101, 0b1000_0000]);
        Assert.True(RoundTrip(whole, baseline: null, out Snapshot? read));
        Assert.Equal(whole.Doors, read!.Doors);

        // Unchanged: taken from the baseline. Changed: sent again.
        Snapshot same = new(21, [Human(1, 0, 0)], [0b0000_0101, 0b1000_0000]);
        Assert.True(RoundTrip(same, whole, out Snapshot? kept));
        Assert.Equal(whole.Doors, kept!.Doors);
        Snapshot opened = new(22, [Human(1, 0, 0)], [0b0000_0111, 0b1000_0000]);
        Assert.True(RoundTrip(opened, whole, out Snapshot? moved));
        Assert.Equal(opened.Doors, moved!.Doors);
    }

    [Fact]
    public void A_climbers_clip_time_and_counters_ride_a_delta()
    {
        Snapshot before = new(10, [Human(1, 0, 0)]);
        ReplicatedHuman climbing = Human(1, 0, 64) with
        {
            Flags = ReplicatedHumanCondition.Alive | ReplicatedHumanCondition.Climbing,
            ClipTime = 1234,
            Shots = 7,
            ActionSequence = 3,
        };
        Snapshot after = new(11, [climbing]);
        Assert.True(RoundTrip(after, before, out Snapshot? read));
        Assert.Equal(climbing, Assert.Single(read!.Humans));
    }
    [Fact]
    public void ADeltaCarriesChangesArrivalsAndDepartures()
    {
        Snapshot before = new(10, [Human(1, 0, 0), Human(2, 500, 500), Human(4, 9000, 9000)]);
        Snapshot after = new(12, [Human(1, 30, 0, yaw: 1234), Human(2, 500, 500), Human(5, -7, 7)]);

        Assert.True(RoundTrip(after, before, out Snapshot? read));
        Assert.Equal(after.Humans, read!.Humans);
    }

    [Fact]
    public void AnUnchangedWorldCostsAlmostNothing()
    {
        ReplicatedHuman[] humans = [.. Enumerable.Range(0, 32).Select(i => Human(i, i * 1000, i * 2000))];
        Snapshot before = new(100, humans);
        Snapshot after = new(101, humans);

        BitWriter whole = new();
        SnapshotCodec.Write(whole, after, null);
        BitWriter delta = new();
        SnapshotCodec.Write(delta, after, before);

        Assert.True(delta.ByteCount < 24, $"an idle 32-player delta took {delta.ByteCount} bytes");
        Assert.True(delta.ByteCount * 5 < whole.ByteCount);
    }

    [Fact]
    public void ASnapshotWhoseBaselineIsGoneIsDropped()
    {
        Snapshot before = new(10, [Human(1, 0, 0)]);
        Snapshot after = new(11, [Human(1, 5, 0)]);
        BitWriter writer = new();
        SnapshotCodec.Write(writer, after, before);
        BitReader reader = new(writer.AsSpan());

        Assert.False(SnapshotCodec.Read(ref reader, _ => null, out _));
    }

    [Fact]
    public void TheBaselineIsTheNewestAcknowledgedSnapshotStillHeld()
    {
        SnapshotHistory history = new(4);
        for (long tick = 1; tick <= 6; tick++)
        {
            history.Add(new Snapshot(tick, []));
        }

        Assert.Null(history.Baseline);
        history.Acknowledge(5);
        history.Acknowledge(4);
        Assert.Equal(5, history.Baseline!.Tick);
        history.Acknowledge(1);
        Assert.Equal(5, history.Baseline!.Tick);

        for (long tick = 7; tick <= 12; tick++)
        {
            history.Add(new Snapshot(tick, []));
        }

        Assert.Null(history.Baseline);
    }

    [Fact]
    public void ABodyIsDrawnBetweenTheTwoSnapshotsAroundTheMoment()
    {
        SnapshotInterpolator buffer = new();
        buffer.Add(new Snapshot(20, [Human(1, 1000, 0, yaw: 65000)]));
        buffer.Add(new Snapshot(10, [Human(1, 0, 0, yaw: 64000)]));

        Assert.True(buffer.Sample(15, 1, out ReplicatedHuman mid));
        Assert.Equal(500, mid.Position.X);
        Assert.Equal(64500, mid.Yaw);
    }

    [Fact]
    public void AHeadingIsBlendedTheShortWayRound()
    {
        SnapshotInterpolator buffer = new();
        buffer.Add(new Snapshot(0, [Human(1, 0, 0, yaw: 65500)]));
        buffer.Add(new Snapshot(2, [Human(1, 0, 0, yaw: 100)]));

        Assert.True(buffer.Sample(1, 1, out ReplicatedHuman mid));
        Assert.Equal(unchecked((ushort)(65500 + 68)), mid.Yaw);
    }

    [Fact]
    public void PastTheNewestSnapshotABodyCarriesOnForAWhileAndThenHolds()
    {
        SnapshotInterpolator buffer = new() { MaxExtrapolationTicks = 4 };
        buffer.Add(new Snapshot(10, [Human(1, 0, 0) with { Velocity = new Vector3f(100, 0, 0) }]));

        Assert.True(buffer.Sample(12, 1, out ReplicatedHuman soon));
        Assert.Equal(200, soon.Position.X);
        Assert.True(buffer.Sample(50, 1, out ReplicatedHuman late));
        Assert.Equal(400, late.Position.X);
    }

    [Fact]
    public void ARewindFindsAShotsTargetWhereTheShooterSawIt()
    {
        // The server's own snapshots, one a tick, a target running along X; the shooter drew it 3.5 ticks behind.
        SnapshotInterpolator history = new(64);
        for (long tick = 0; tick < 40; tick++)
        {
            history.Add(new Snapshot(tick, [Human(9, (int)(tick * 1024), 0)]));
        }

        Assert.True(history.Sample(30 - 3.5, 9, out ReplicatedHuman seen));
        Assert.Equal((int)(26.5 * 1024), seen.Position.X);
        Assert.False(history.Sample(10, 99, out _));
    }

    private static bool RoundTrip(Snapshot snapshot, Snapshot? baseline, out Snapshot? read)
    {
        BitWriter writer = new();
        SnapshotCodec.Write(writer, snapshot, baseline);
        BitReader reader = new(writer.AsSpan());
        return SnapshotCodec.Read(ref reader, tick => baseline is { } b && b.Tick == tick ? b : null, out read);
    }

    private static ReplicatedHuman Human(int id, int x, int y, ushort yaw = 0, byte health = 255) =>
        new(id, new Int3(x, y, 200), new Vector3f(1.5f, 0, 0), yaw, 300, 4, 1, 12, health, 1, ReplicatedHumanCondition.Alive | ReplicatedHumanCondition.OnGround);
}
