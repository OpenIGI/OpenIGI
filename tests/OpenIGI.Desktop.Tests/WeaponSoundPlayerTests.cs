using OpenIGI.Audio;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Sound;
using OpenIGI.Game.Weapons;
using OpenIGI.Host.Audio;
using OpenIGI.TestSupport;
using System.Buffers.Binary;
using Xunit;

namespace OpenIGI.Desktop.Tests;

public sealed class WeaponSoundPlayerTests
{
    [RequiresGameDataFact]
    public void AiAutomaticIsPositionalAndSurvivesItsCadenceGap()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        SoundBank bank = new(fileSystem, null);
        bank.MountDirectory("LOCAL:common/sounds");

        RecordingAudioDevice device = new();
        using SoundSystem sounds = new(device, bank, fileSystem, null);
        WeaponSoundPlayer player = new(sounds);
        WeaponType weapon = new()
        {
            FireSound = "ak47_loop",
            FireLoopEndSound = "ak47_loop_e",
            MaximumRoundsPerBurst = 30,
            RoundsPerMinute = 600,
        };

        Vector3d position = new(10 * SoundSystem.UnitsPerMetre, 0, 0);
        player.Tick(firedThisTick: true, weapon, position);

        RecordingAudioDevice.PlayedVoice started = Assert.Single(device.Played);
        Assert.True(started.Parameters.IsPositional);
        Assert.Equal(10.0, started.Parameters.Position.X, 6);
        Assert.True(started.Parameters.Looping);

        // 600 RPM is a three-tick cadence. Silence between rounds must not stop the loop.
        for (int i = 0; i < GunTiming.TicksPerRound(weapon.RoundsPerMinute); i++)
        {
            player.Tick(firedThisTick: false, weapon, position);
        }

        Assert.Single(device.Played);
        Assert.NotNull(device.Current(started.Voice));

        player.Tick(firedThisTick: false, weapon, position);
        Assert.Equal(2, device.Played.Count);
        Assert.Null(device.Current(started.Voice));
        Assert.True(device.Played[1].Parameters.IsPositional);
    }

    [RequiresGameDataFact]
    public void ResetImmediatelyStopsAnAutomaticLoop()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        SoundBank bank = new(fileSystem, null);
        bank.MountDirectory("LOCAL:common/sounds");

        RecordingAudioDevice device = new();
        using SoundSystem sounds = new(device, bank, fileSystem, null);
        WeaponSoundPlayer player = new(sounds);
        WeaponType weapon = new()
        {
            FireSound = "ak47_loop",
            MaximumRoundsPerBurst = 30,
            RoundsPerMinute = 600,
        };

        player.Tick(firedThisTick: true, weapon);
        RecordingAudioDevice.PlayedVoice loop = Assert.Single(device.Played);
        Assert.NotNull(device.Current(loop.Voice));

        player.Reset();

        Assert.Null(device.Current(loop.Voice));
    }

    [RequiresGameDataFact]
    public void MilUsesTheRetailLoudLoopAndLoopEndSample()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        SoundBank bank = new(fileSystem, null);
        bank.MountDirectory("LOCAL:common/sounds");

        Assert.True(bank.TryGet("mil_loop", out SoundEntry loopSample));
        Assert.True(bank.TryGet("mil_loop_e", out SoundEntry loopEndSample));
        Assert.True(loopSample.Duration > TimeSpan.Zero);
        Assert.True(loopEndSample.Duration > TimeSpan.Zero);
        Assert.True(Peak(IlsfFile.Read(loopSample.Data).DecodePcm().Span) > 1000,
            "The retail MIL loop decodes to a non-silent waveform.");

        RecordingAudioDevice device = new();
        using SoundSystem sounds = new(device, bank, fileSystem, null);
        WeaponSoundPlayer player = new(sounds);
        WeaponType weapon = new()
        {
            ScriptId = "WEAPON_ID_MIL",
            FireSound = "mil_loop",
            FireLoopEndSound = "mil_loop_e",
            MaximumRoundsPerBurst = 100,
            RoundsPerMinute = 600,
        };

        player.Tick(firedThisTick: true, weapon);
        RecordingAudioDevice.PlayedVoice started = Assert.Single(device.Played);
        Assert.NotEqual(AudioVoiceHandle.None, started.Voice);
        Assert.True(started.Parameters.Looping);

        player.Tick(firedThisTick: false, weapon);
        player.Tick(firedThisTick: false, weapon);
        player.Tick(firedThisTick: false, weapon);
        player.Tick(firedThisTick: false, weapon);
        Assert.Equal(2, device.Played.Count);
        Assert.NotEqual(AudioVoiceHandle.None, device.Played[1].Voice);
    }

    [RequiresGameDataFact]
    public void AutomaticWeaponRetriesWhenItsFirstLoopCannotGetAVoice()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        SoundBank bank = new(fileSystem, null);
        bank.MountDirectory("LOCAL:common/sounds");

        RecordingAudioDevice device = new() { VoiceCount = 0 };
        using SoundSystem sounds = new(device, bank, fileSystem, null);
        WeaponSoundPlayer player = new(sounds);
        WeaponType weapon = new()
        {
            ScriptId = "WEAPON_ID_MIL",
            FireSound = "mil_loop",
            FireLoopEndSound = "mil_loop_e",
            MaximumRoundsPerBurst = 100,
            RoundsPerMinute = 600,
        };

        player.Tick(firedThisTick: true, weapon);
        Assert.Empty(device.Played);

        // Once the pool is available, the next round must open the loop instead of being routed to
        // the invalid handle retained by the failed first attempt.
        device.VoiceCount = 1;
        player.Tick(firedThisTick: true, weapon);

        Assert.Single(device.Played);
        Assert.True(device.Played[0].Parameters.Looping);
    }

    private static int Peak(ReadOnlySpan<byte> pcm)
    {
        int peak = 0;
        for (int offset = 0; offset + sizeof(short) <= pcm.Length; offset += sizeof(short))
        {
            peak = Math.Max(peak, Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(pcm[offset..])));
        }

        return peak;
    }
}
