using OpenIGI.Core.Numerics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Replays;
using Xunit;

namespace OpenIGI.Game.Tests.Replays;

/// <summary>
/// A run's ghost -- the runner's body tick by tick and what happened along the way -- is written beside its inputs and read back
/// as it was; a still runner costs next to nothing; a file of the first format, before replays carried ghosts, is read still; and
/// a ghost that is not one is refused.
/// </summary>
public sealed class GhostTrackTests
{
    [Fact]
    public void A_ghost_reads_back_as_it_was_written()
    {
        ReplayFile written = ReplayFileTests.Sample(seed: 3, ticks: 1800) with { Ghost = Running(1800, seed: 3) };
        ReplayFile read = ReplayFile.Read(written.ToBytes());

        GhostTrack ghost = Assert.IsType<GhostTrack>(read.Ghost);
        Assert.Equal(written.Ghost!.Length, ghost.Length);
        for (int tick = 0; tick < ghost.Length; tick++)
        {
            Assert.Equal(written.Ghost.At(tick), ghost.At(tick));
        }

        Assert.Equal(written.Ghost.Events, ghost.Events);
        Assert.Equal([(2, 400), (0, 950)], ghost.Splits());
    }

    [Fact]
    public void A_ghost_standing_still_costs_next_to_nothing()
    {
        GhostTrack still = new();
        ReplicatedHuman standing = new(0, new Int3(1000, 2000, 300), default, 1234, 0, 3, 1, 2, 255, 0, ReplicatedHumanCondition.Alive | ReplicatedHumanCondition.OnGround);
        for (int tick = 0; tick < 18000; tick++)
        {
            still.Set(tick, standing);
        }

        using MemoryStream memory = new();
        using (BinaryWriter writer = new(memory, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            GhostTrackCodec.Write(writer, still);
        }

        // Ten minutes: a bit a tick, before the file is deflated.
        Assert.True(memory.Length < 18000 / 8 + 64, $"Ten minutes standing took {memory.Length} bytes.");
    }

    [Fact]
    public void A_minute_of_a_ghost_running_is_small()
    {
        // Moving, turning and its speed changing on every tick, as I.G.I. 2's runner's does: inputs and ghost together, deflated.
        int bare = ReplayFileTests.Sample(seed: 6, ticks: 1800).ToBytes().Length;
        int with = (ReplayFileTests.Sample(seed: 6, ticks: 1800) with { Ghost = Running(1800, seed: 6) }).ToBytes().Length;
        Assert.True(with - bare < 48 * 1024, $"A minute's ghost took {with - bare} bytes.");
    }

    [Fact]
    public void Ticks_skipped_hold_the_body_as_it_was()
    {
        GhostTrack track = new();
        ReplicatedHuman first = new(0, new Int3(1, 2, 3), default, 0, 0, 0, 0, 0, 255, 0, ReplicatedHumanCondition.Alive);
        track.Set(0, first);
        track.Set(5, first with { Position = new Int3(9, 9, 9) });
        Assert.Equal(6, track.Length);
        Assert.Equal(first, track.At(4));
        Assert.Null(track.At(6));
    }

    [Fact]
    public void A_replay_of_the_first_format_reads_with_no_ghost()
    {
        byte[] bytes = ReplayFileTests.Sample(seed: 4, ticks: 300).ToBytes();
        byte[] first = FirstFormat(bytes);
        ReplayFile read = ReplayFile.Read(first);
        Assert.Null(read.Ghost);
        Assert.Equal(300, read.Tape.Length);
    }

    [Fact]
    public void A_ghost_of_no_kind_this_build_knows_is_refused()
    {
        byte[] bytes = ReplayFileTests.Sample(seed: 5, ticks: 300).ToBytes();
        byte[] body = Inflated(bytes);
        body[^1] = 7;
        Assert.Throws<InvalidDataException>(() => ReplayFile.Read(Rewrapped(body, ReplayFile.FormatVersion)));
    }

    // A runner going about a level: walking and turning, ducking now and then, firing in bursts; two objectives done and a kill.
    private static GhostTrack Running(int ticks, int seed)
    {
        Random random = new(seed);
        GhostTrack track = new();
        Int3 at = new(100000, -50000, 2000);
        for (int tick = 0; tick < ticks; tick++)
        {
            at = new Int3(at.X + random.Next(-40, 41), at.Y + random.Next(-40, 41), at.Z + random.Next(-2, 3));
            track.Set(tick, new ReplicatedHuman(
                0,
                at,
                new Vector3f(random.NextSingle(), random.NextSingle(), 0f),
                (ushort)(tick * 37),
                (ushort)(tick % 90 < 10 ? 300 : 0),
                (ushort)(tick % 300 < 200 ? 4 : 7),
                (ushort)(tick % 60 is >= 40 and < 48 ? 2 : 1),
                3,
                (byte)Math.Max(1, 255 - (tick / 10)),
                0,
                ReplicatedHumanCondition.Alive | (tick % 60 is >= 40 and < 48 ? ReplicatedHumanCondition.Firing : 0),
                (byte)(tick / 60)));
        }

        track.Event(120, GhostEventKind.Alarm, 1);
        track.Event(400, GhostEventKind.Objective, 2);
        track.Event(512, GhostEventKind.Kill, 1);
        track.Event(950, GhostEventKind.Objective, 0);
        track.Event(960, GhostEventKind.Objective, 2);
        track.Event(ticks - 1, GhostEventKind.End, 1);
        return track;
    }

    // The same replay as the first format wrote it: no byte for a ghost after the inputs.
    private static byte[] FirstFormat(byte[] bytes)
    {
        byte[] body = Inflated(bytes);
        Assert.Equal(0, body[^1]);
        return Rewrapped(body[..^1], 1);
    }

    private static byte[] Inflated(byte[] bytes)
    {
        using MemoryStream source = new(bytes, 42, bytes.Length - 42);
        using System.IO.Compression.DeflateStream inflate = new(source, System.IO.Compression.CompressionMode.Decompress);
        using MemoryStream body = new();
        inflate.CopyTo(body);
        return body.ToArray();
    }

    private static byte[] Rewrapped(byte[] body, ushort version)
    {
        using MemoryStream file = new();
        file.Write("OIGR"u8);
        file.Write([(byte)(version & 0xff), (byte)(version >> 8)]);
        file.Write(BitConverter.GetBytes(body.Length));
        file.Write(System.Security.Cryptography.SHA256.HashData(body));
        using (System.IO.Compression.DeflateStream deflate = new(file, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(body);
        }

        return file.ToArray();
    }
}
