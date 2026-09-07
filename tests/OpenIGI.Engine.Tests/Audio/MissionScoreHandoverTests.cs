using OpenIGI.Audio;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Vfs;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Engine.Tests.Audio;

/// <summary>
/// What has to happen to the bank and to the score when one mission gives way to another.
/// </summary>
/// <remarks>
/// <para>
/// Every level ships its own <c>sounds/game_music.wav</c> — fourteen different tracks under one name —
/// so <c>game_music</c> means a different file in each. Two properties of the machinery conspire against
/// that if a load does not tear the bank down:
/// </para>
/// <list type="number">
/// <item>
/// The bank keeps the <b>first</b> mount of a name, faithfully: the original's loader reports
/// <c>"SOUND - File %s already loaded!"</c> rather than replacing. Re-mounting therefore cannot update
/// <c>game_music</c>.
/// </item>
/// <item>
/// A mounted entry stores the path it was <b>found at</b>, not the <c>MISSION:</c> alias it was found
/// through, so re-pointing the alias does not redirect it either.
/// </item>
/// </list>
/// <para>
/// And the score treats a repeat of the playing track as a no-op, so it will decline to start a track it
/// believes is already sounding — correct within a mission, wrong across one.
/// </para>
/// <para>Skipped when <c>IGI_GAME_PATH</c> is unset: the tracks come from a real installation.</para>
/// </remarks>
public sealed class MissionScoreHandoverTests
{
    private const string FirstMission = "LOCAL:missions/location0/level1/";
    private const string SecondMission = "LOCAL:missions/location0/level5/";
    private const string MissionSounds = "MISSION:sounds";
    private const string MenuSounds = "LOCAL:menusystem/sound/sounds.res";

    [RequiresGameDataFact]
    public void ReMountingWithoutClearingLeavesThePreviousLevelsScoreInTheBank()
    {
        VirtualFileSystem fileSystem = FileSystem();
        SoundBank bank = new(fileSystem, null);

        fileSystem.SetAlias(VirtualFileSystem.MissionAlias, FirstMission);
        bank.MountDirectory(MissionSounds);
        Assert.True(bank.TryGet(MusicPlayer.GameMusic, out SoundEntry first));
        Assert.Contains("level1/", first.Origin, StringComparison.OrdinalIgnoreCase);

        // Exactly what a mission load does short of the teardown: re-point MISSION: and mount again.
        fileSystem.SetAlias(VirtualFileSystem.MissionAlias, SecondMission);
        bank.MountDirectory(MissionSounds);

        Assert.True(bank.TryGet(MusicPlayer.GameMusic, out SoundEntry stale));
        Assert.Contains("level1/", stale.Origin, StringComparison.OrdinalIgnoreCase);
    }

    [RequiresGameDataFact]
    public void ClearingBeforeTheMountIsWhatMakesTheNewLevelsScoreResolve()
    {
        VirtualFileSystem fileSystem = FileSystem();
        SoundBank bank = new(fileSystem, null);

        fileSystem.SetAlias(VirtualFileSystem.MissionAlias, FirstMission);
        bank.MountDirectory(MissionSounds);
        Assert.True(bank.TryGet(MusicPlayer.GameMusic, out SoundEntry first));

        fileSystem.SetAlias(VirtualFileSystem.MissionAlias, SecondMission);
        bank.Clear();
        bank.MountDirectory(MissionSounds);

        Assert.True(bank.TryGet(MusicPlayer.GameMusic, out SoundEntry fresh));
        Assert.Contains("level5/", fresh.Origin, StringComparison.OrdinalIgnoreCase);

        // The two really are different recordings, so hearing the wrong one is audible rather than
        // theoretical -- level 1's track runs about three times as long as level 5's.
        Assert.True(
            first.Duration - fresh.Duration > TimeSpan.FromSeconds(30),
            $"Expected two distinct scores; level 1 runs {first.Duration} and level 5 {fresh.Duration}.");
    }

    [RequiresGameDataFact]
    public void StoppingLetsTheSameTrackNameStartAgainFromANewBank()
    {
        // The score is asked for game_music on entering every mission. Without being told the bank it
        // was playing out of has gone, it would answer the second ask with "already playing" and leave
        // the previous level's stream sounding.
        VirtualFileSystem fileSystem = FileSystem();
        fileSystem.SetAlias(VirtualFileSystem.MissionAlias, FirstMission);

        SoundBank bank = new(fileSystem, null);
        bank.MountDirectory(MissionSounds);

        RecordingAudioDevice device = new();
        MusicPlayer music = new(new SoundSystem(device, bank, fileSystem, null), null);

        Assert.True(music.Play(MusicPlayer.GameMusic));
        Assert.Single(device.Played);

        // Re-asking within the mission is still a no-op; that is what keeps the score from stuttering.
        Assert.True(music.Play(MusicPlayer.GameMusic));
        Assert.Single(device.Played);

        // The mission changes: the bank is emptied and rebuilt, and the score is told.
        music.Stop();
        fileSystem.SetAlias(VirtualFileSystem.MissionAlias, SecondMission);
        bank.Clear();
        bank.MountDirectory(MissionSounds);

        Assert.True(music.Play(MusicPlayer.GameMusic));
        Assert.Equal(2, device.Played.Count);
        Assert.Equal(1, device.ActiveVoiceCount);
        Assert.Equal(MusicPlayer.GameMusic, music.CurrentTrack);
    }

    [RequiresGameDataFact]
    public void StoppingRetiresTheOutgoingStreamRatherThanLeavingItSounding()
    {
        VirtualFileSystem fileSystem = FileSystem();
        fileSystem.SetAlias(VirtualFileSystem.MissionAlias, FirstMission);

        SoundBank bank = new(fileSystem, null);
        bank.MountDirectory(MissionSounds);
        bank.MountArchive(MenuSounds);

        RecordingAudioDevice device = new();
        MusicPlayer music = new(new SoundSystem(device, bank, fileSystem, null), null)
        {
            // Even mid-crossfade: a level handover must not carry a fading track into the next level.
            FadeSeconds = 2f,
        };

        music.Play(MusicPlayer.GameMusic);
        music.Play(MusicPlayer.MenuMusic);
        Assert.Equal(2, device.ActiveVoiceCount);

        music.Stop();
        Assert.Equal(0, device.ActiveVoiceCount);
        Assert.False(music.IsPlaying);
        Assert.Equal(string.Empty, music.CurrentTrack);
    }

    private static VirtualFileSystem FileSystem()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        return fileSystem;
    }
}
