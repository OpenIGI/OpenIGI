using OpenIGI.Core.Numerics;
using OpenIGI.Desktop.Audio;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Weapons;
using OpenIGI.TestSupport;
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
}
