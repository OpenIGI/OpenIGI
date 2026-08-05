using OpenIGI.Audio;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Vfs;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Engine.Tests.Audio;

/// <summary>
/// Drives the score against a recording device, so what it asked for can be asserted without anything
/// being audible.
/// </summary>
/// <remarks>Skipped when <c>IGI_GAME_PATH</c> is unset: the tracks come from a real installation.</remarks>
public sealed class MusicPlayerTests
{
    [RequiresGameDataFact]
    public void ATrackPlaysLoopingOnTheMusicBus()
    {
        MusicPlayer music = Build(out RecordingAudioDevice device);

        Assert.True(music.Play(MusicPlayer.GameMusic));
        Assert.Equal(MusicPlayer.GameMusic, music.CurrentTrack);

        VoiceParameters played = Assert.Single(device.Played).Parameters;
        Assert.Equal(SoundCategory.Music, played.Category);
        Assert.True(played.Looping);
        Assert.False(played.IsPositional);

        // Above every effect: a full voice pool must never decide the score is the thing to drop.
        Assert.Equal(int.MaxValue, played.Priority);
    }

    [RequiresGameDataFact]
    public void ReRequestingThePlayingTrackDoesNotRestartIt()
    {
        // The mission state asks for its music whenever it is entered. Restarting on each ask would
        // stutter the score back to its opening bar.
        MusicPlayer music = Build(out RecordingAudioDevice device);

        music.Play(MusicPlayer.GameMusic);
        music.Play(MusicPlayer.GameMusic);
        music.Play(MusicPlayer.GameMusic);

        Assert.Single(device.Played);
    }

    [RequiresGameDataFact]
    public void ChangingTrackWithNoFadeStopsTheOldOneOutright()
    {
        MusicPlayer music = Build(out RecordingAudioDevice device);
        music.FadeSeconds = 0f;

        music.Play(MusicPlayer.GameMusic);
        AudioVoiceHandle first = device.Played[0].Voice;

        music.Play(MusicPlayer.MenuMusic);

        // The original hard-cuts, and that is what the vanilla profile has to do.
        Assert.False(device.IsPlaying(first));
        Assert.Equal(2, device.Played.Count);
        Assert.Equal(1, device.ActiveVoiceCount);
    }

    [RequiresGameDataFact]
    public void AFadeCrossesTheTwoTracksOverAndThenRetiresTheOldOne()
    {
        MusicPlayer music = Build(out RecordingAudioDevice device);
        music.FadeSeconds = 1f;

        music.Play(MusicPlayer.GameMusic);
        music.Update(1f);
        music.Play(MusicPlayer.MenuMusic);

        // Both sound during the cross.
        Assert.Equal(2, device.ActiveVoiceCount);

        // The new track starts silent and climbs.
        AudioVoiceHandle incoming = device.Played[1].Voice;
        Assert.Equal(0f, device.Current(incoming)!.Value.Gain);

        music.Update(0.5f);
        Assert.InRange(device.Current(incoming)!.Value.Gain, 0.4f, 0.6f);

        music.Update(0.6f);
        Assert.Equal(1, device.ActiveVoiceCount);
        Assert.Equal(1f, device.Current(incoming)!.Value.Gain);
    }

    [RequiresGameDataFact]
    public void DisablingStopsTheScoreAndEnablingBringsItBack()
    {
        MusicPlayer music = Build(out RecordingAudioDevice device);

        music.Play(MusicPlayer.GameMusic);
        Assert.Equal(1, device.ActiveVoiceCount);

        music.Enabled = false;
        Assert.Equal(0, device.ActiveVoiceCount);
        Assert.False(music.IsPlaying);

        // The original has nowhere to remember a position, so the track restarts from its beginning.
        music.Enabled = true;
        Assert.Equal(1, device.ActiveVoiceCount);
        Assert.Equal(MusicPlayer.GameMusic, music.CurrentTrack);
    }

    [RequiresGameDataFact]
    public void NothingPlaysWhileMusicIsDisabled()
    {
        MusicPlayer music = Build(out RecordingAudioDevice device);
        music.Enabled = false;

        Assert.False(music.Play(MusicPlayer.GameMusic));
        Assert.Empty(device.Played);
    }

    [RequiresGameDataFact]
    public void TheVolumeReachesTheSoundingVoice()
    {
        MusicPlayer music = Build(out RecordingAudioDevice device);
        music.FadeSeconds = 0f;
        music.Play(MusicPlayer.GameMusic);

        AudioVoiceHandle voice = device.Played[0].Voice;
        Assert.Equal(1f, device.Current(voice)!.Value.Gain);

        // Game_SetMusicVolume, which is what the options menu's slider previews through.
        music.Volume = 0.25f;
        Assert.Equal(0.25f, device.Current(voice)!.Value.Gain);
    }

    [RequiresGameDataFact]
    public void AnUnknownTrackLeavesNothingPlaying()
    {
        MusicPlayer music = Build(out RecordingAudioDevice device);

        Assert.False(music.Play("no_such_track"));
        Assert.Equal(string.Empty, music.CurrentTrack);
        Assert.Empty(device.Played);
    }

    private static MusicPlayer Build(out RecordingAudioDevice device)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        fileSystem.SetAlias(VirtualFileSystem.MissionAlias, "LOCAL:missions/location0/level1/");

        SoundBank bank = new(fileSystem, null);
        bank.MountDirectory("MISSION:sounds");
        bank.MountArchive("LOCAL:menusystem/sound/sounds.res");

        device = new RecordingAudioDevice();
        return new MusicPlayer(new SoundSystem(device, bank, fileSystem, null), null);
    }
}
