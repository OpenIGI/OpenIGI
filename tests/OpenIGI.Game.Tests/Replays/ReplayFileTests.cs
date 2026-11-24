using System.IO.Compression;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Computer;
using OpenIGI.Game.Player;
using OpenIGI.Game.Replays;
using OpenIGI.Game.Scoring;
using Xunit;

namespace OpenIGI.Game.Tests.Replays;

/// <summary>
/// A replay written and read back is the same replay to the bit, and anything that is not one -- cut short, mangled, someone
/// else's format, a body that inflates without end -- is refused as such, never half-read.
/// </summary>
public sealed class ReplayFileTests
{
    [Fact]
    public void A_replay_reads_back_as_it_was_written()
    {
        ReplayFile written = Sample(seed: 1, ticks: 3000);
        ReplayFile read = ReplayFile.Read(written.ToBytes());

        Assert.Equal(written.Header.GameVersion, read.Header.GameVersion);
        Assert.Equal(written.Header.Title, read.Header.Title);
        Assert.Equal(written.Header.Mission, read.Header.Mission);
        Assert.Equal(written.Header.Difficulty, read.Header.Difficulty);
        Assert.Equal(written.Header.Ruleset, read.Header.Ruleset);
        Assert.Equal(written.Header.Settings, read.Header.Settings);
        Assert.Equal(written.Header.ContentFingerprint, read.Header.ContentFingerprint);
        Assert.Equal(written.Header.Player, read.Header.Player);
        Assert.Equal(written.Header.PlayerName, read.Header.PlayerName);
        Assert.Equal(written.Header.RecordedAt, read.Header.RecordedAt);
        Assert.Equal(written.Result, read.Result);

        Assert.Equal(written.Tape.Length, read.Tape.Length);
        for (int tick = 0; tick < written.Tape.Length; tick++)
        {
            Assert.Equal(written.Tape.BreaksAt(tick), read.Tape.BreaksAt(tick));
            Assert.True(SameInput(written.Tape.InputAt(tick), read.Tape.InputAt(tick)), $"tick {tick} reads back differently");
        }

        Assert.Equal(written.Tape.Commands, read.Tape.Commands);
        Assert.Equal(written.Tape.Marks, read.Tape.Marks);
        Assert.Equal(written.Tape.Digests, read.Tape.Digests);
    }

    [Fact]
    public void A_minute_of_play_is_small()
    {
        // Thirty ticks a second, the mouse moving on every one: the size the server's storage is planned by.
        byte[] bytes = Sample(seed: 2, ticks: 60 * 30).ToBytes();
        Assert.True(bytes.Length < 64 * 1024, $"A minute of play took {bytes.Length} bytes.");
    }

    [Fact]
    public void A_replay_cut_short_is_refused()
    {
        ReplayFile written = Sample(seed: 3, ticks: 500);
        byte[] bytes = written.ToBytes();
        foreach (int length in new[] { 0, 3, 5, 6, 7, 41, bytes.Length / 3, bytes.Length / 2, bytes.Length - 16 })
        {
            Assert.Throws<InvalidDataException>(() => ReplayFile.Read(bytes.AsSpan(0, length)));
        }

        // A cut that takes nothing the body needed -- the final block's padding -- leaves the very same replay, as its hash says.
        for (int cut = 1; cut <= 4; cut++)
        {
            try
            {
                ReplayFile read = ReplayFile.Read(bytes.AsSpan(0, bytes.Length - cut));
                Assert.Equal(written.Tape.Length, read.Tape.Length);
                Assert.Equal(written.Tape.Digests, read.Tape.Digests);
            }
            catch (InvalidDataException)
            {
                // Refused, as a cut into the body must be.
            }
        }
    }

    [Fact]
    public void Another_format_is_refused()
    {
        byte[] bytes = Sample(seed: 4, ticks: 10).ToBytes();
        byte[] otherMagic = (byte[])bytes.Clone();
        otherMagic[0] = (byte)'X';
        Assert.Throws<InvalidDataException>(() => ReplayFile.Read(otherMagic));

        byte[] otherVersion = (byte[])bytes.Clone();
        otherVersion[4] = 99;
        Assert.Throws<InvalidDataException>(() => ReplayFile.Read(otherVersion));
    }

    [Fact]
    public void A_mangled_replay_is_refused_or_read_but_never_breaks_the_reader()
    {
        byte[] bytes = Sample(seed: 5, ticks: 300).ToBytes();
        Random random = new(6);
        for (int i = 0; i < 2000; i++)
        {
            byte[] mangled = (byte[])bytes.Clone();
            int flips = 1 + random.Next(4);
            for (int f = 0; f < flips; f++)
            {
                mangled[6 + random.Next(mangled.Length - 6)] ^= (byte)(1 << random.Next(8));
            }

            try
            {
                _ = ReplayFile.Read(mangled);
            }
            catch (InvalidDataException)
            {
                // Refused, as it should be.
            }
        }
    }

    [Fact]
    public void A_body_that_inflates_without_end_is_refused()
    {
        using MemoryStream file = new();
        file.Write("OIGR"u8);
        file.Write([(byte)(ReplayFile.FormatVersion & 0xff), (byte)(ReplayFile.FormatVersion >> 8)]);
        using (DeflateStream deflate = new(file, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            byte[] zeros = new byte[1024 * 1024];
            for (int i = 0; i < (ReplayFile.MaxInflatedBytes / zeros.Length) + 2; i++)
            {
                deflate.Write(zeros);
            }
        }

        Assert.Throws<InvalidDataException>(() => ReplayFile.Read(file.ToArray()));
    }

    [Fact]
    public void A_file_past_the_largest_is_refused_unread()
    {
        Assert.Throws<InvalidDataException>(() => ReplayFile.Read(new byte[ReplayFile.MaxFileBytes + 1]));
    }

    [Fact]
    public void A_tape_finds_its_fingerprints_by_tick()
    {
        InputTape tape = new();
        for (int tick = 0; tick < 300; tick++)
        {
            tape.Ran(tick);
            if (tick % 30 == 29)
            {
                tape.SetDigest(tick, (ulong)tick * 7919UL);
            }
        }

        Assert.Equal(29UL * 7919UL, tape.DigestAt(29));
        Assert.Equal(299UL * 7919UL, tape.DigestAt(299));
        Assert.Null(tape.DigestAt(30));

        // Out of order: kept as it stood.
        tape.SetDigest(10, 1);
        Assert.Null(tape.DigestAt(10));
    }

    // A replay of every kind of tick: the mouse moving, keys coming and going, the cutscene broken, the crosshair over the
    // shoulder, the picture between ticks, the wheel's picks and the computer up.
    internal static ReplayFile Sample(int seed, int ticks)
    {
        Random random = new(seed);
        InputTape tape = new();
        PlayerAction[] actions = [PlayerAction.MoveUp, PlayerAction.Fire, PlayerAction.Crouch, PlayerAction.MoveLeft, PlayerAction.Reload];
        PlayerAction held = default;
        for (int tick = 0; tick < ticks; tick++)
        {
            tape.Ran(tick);
            tape.SetBreak(tick, tick < 40 && tick % 3 == 0);
            if (tick % 97 == 13)
            {
                continue;
            }

            if (random.Next(20) == 0)
            {
                held ^= actions[random.Next(actions.Length)];
            }

            bool computer = tick % 400 > 350;
            TitlePlayerInput input = new(
                (float)((random.NextDouble() - 0.5) * 12),
                (float)((random.NextDouble() - 0.5) * 6),
                held,
                random.Next(30) == 0 ? actions[random.Next(actions.Length)] : default,
                0.4f,
                InvertMouse: false,
                BreakCutscene: tick < 40,
                UnfilteredMouse: tick > 1000,
                TitleHeld: tick % 200 < 5 ? 0x4UL : 0UL,
                TitlePressed: 0,
                Raw: tick % 50 == 0 ? TitleRawKeys.Enter : TitleRawKeys.None,
                ViewBlend: tick % 2 == 0 ? (float)random.NextDouble() : null,
                Aim: tick % 300 < 100 ? new TitleAim(new Vector3d(1024.5, -2048.25, 300.125), new Vector3f(0.6f, 0.8f, 0f)) : null,
                WheelPick: tick % 333 == 0 ? 7 : null,
                Computer: computer
                    ? new TitleComputerInput(true, new ComputerPointerInput(3.5f, -1f, 1, tick % 400 == 399) { PageKey = ComputerPageKind.Objectives }, held, 1280, 720)
                    : new TitleComputerInput(false, default, held, 1280, 720));
            tape.SetInput(tick, input);
            if (tick % 30 == 29)
            {
                tape.SetDigest(tick, (ulong)random.NextInt64());
            }
        }

        tape.Command(0, "god 0");
        tape.Mark(120, "Guard 412 sees you");
        ReplayHeader header = new(
            "0.7.0",
            "igi1",
            3,
            1,
            Ruleset.Classic.Id,
            [("g_grenade_cook", "manual"), ("g_igi2_movement", "retail")],
            new string('a', 64),
            "2b01639bb3650b1c",
            "Jones",
            1_700_000_000_000L);
        ReplayResult result = new(
            ReplayOutcome.Completed,
            ticks - 1,
            new MissionScore((uint)ticks, 2, 87, 140, 61) { Difficulty = 1, MissionId = 3, Kills = 19, TimesAlarmTriggered = 1 },
            string.Empty);
        return new ReplayFile(header, result, tape);
    }

    private static bool SameInput(TitlePlayerInput? a, TitlePlayerInput? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        TitlePlayerInput x = a.Value;
        TitlePlayerInput y = b.Value;
        return BitConverter.SingleToInt32Bits(x.MouseX) == BitConverter.SingleToInt32Bits(y.MouseX)
            && BitConverter.SingleToInt32Bits(x.MouseY) == BitConverter.SingleToInt32Bits(y.MouseY)
            && BitConverter.SingleToInt32Bits(x.MouseSensitivity) == BitConverter.SingleToInt32Bits(y.MouseSensitivity)
            && x with { MouseX = 0, MouseY = 0, MouseSensitivity = 0 } == y with { MouseX = 0, MouseY = 0, MouseSensitivity = 0 };
    }
}
