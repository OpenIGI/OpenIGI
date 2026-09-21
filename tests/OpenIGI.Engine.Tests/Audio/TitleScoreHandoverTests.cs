using OpenIGI.Audio;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Vfs;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Engine.Tests.Audio;

/// <summary>
/// What has to happen to the bank and to the score when the session stops running one game and starts
/// running the other.
/// </summary>
/// <remarks>
/// <para>
/// A title switch re-points the <c>LOCAL</c> device at the other installation and rebuilds the resource
/// registry, the task loop and every presenter — but the audio device belongs to the process and
/// survives, and so, unless something says otherwise, does everything hanging off it. Three properties
/// then conspire to keep the outgoing game's menu theme playing under the incoming game's menu:
/// </para>
/// <list type="number">
/// <item>
/// The bank keeps the <b>first</b> mount of a name, faithfully to the original, so re-mounting the new
/// game's <c>menusystem/sound/sounds.res</c> cannot retire <c>menu_music</c>.
/// </item>
/// <item>
/// The score treats a repeat of the playing track as a no-op, so simply asking for <c>menu_music</c>
/// again is declined.
/// </item>
/// <item>
/// A voice already streaming holds the handle it opened, so it goes on sounding whatever the bank now
/// says.
/// </item>
/// </list>
/// <para>
/// And the two themes are not interchangeable: I.G.I. 1 ships <c>menu_music.wav</c>, resident IMA
/// ADPCM, and I.G.I. 2 ships <c>menu_music.mp3</c>, a 320 kbps MPEG-1 Layer III stream. Hearing the
/// wrong one is not a subtlety.
/// </para>
/// <para>Skipped unless both <c>IGI_GAME_PATH</c> and <c>IGI2_GAME_PATH</c> are set.</para>
/// </remarks>
public sealed class TitleScoreHandoverTests
{
    private const string MenuSounds = "LOCAL:menusystem/sound/sounds.res";

    [RequiresTitlesDataFact("igi1", "igi2")]
    public void TheTwoGamesShipTheirMenuThemeInDifferentFormatsUnderTheSameName()
    {
        SoundBank first = Mount(GameDataSource.Igi1);
        SoundBank second = Mount(GameDataSource.Igi2);

        Assert.True(first.TryGet(MusicPlayer.MenuMusic, out SoundEntry one));
        Assert.True(second.TryGet(MusicPlayer.MenuMusic, out SoundEntry two));

        Assert.Equal(SoundCodec.Ilsf, one.Codec);
        Assert.False(one.IsStreamed, "The first game's theme is resident ADPCM inside its archive.");

        Assert.Equal(SoundCodec.Mpeg, two.Codec);
        Assert.True(two.IsStreamed, "The sequel's theme is decoded as it plays.");

        // An archived MPEG track has no file to reopen, so it must keep its packed bytes -- eight
        // megabytes held rather than the thirty-six expanding it would cost.
        Assert.False(two.Data.IsEmpty, "An archived stream must keep the bytes it decodes from.");

        Assert.True(
            (two.Duration - one.Duration).Duration() > TimeSpan.FromSeconds(30),
            $"Expected two distinct themes; the first runs {one.Duration} and the sequel's {two.Duration}.");
    }

    [RequiresTitlesDataFact("igi1", "igi2")]
    public void ReMountingWithoutClearingLeavesTheOutgoingGamesMenuSoundsInTheBank()
    {
        // Exactly what a title switch does short of the teardown: re-point LOCAL and mount again.
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameDataSource.Igi1.RequireRoot()));

        SoundBank bank = new(fileSystem, null);
        bank.MountArchive(MenuSounds);
        Assert.True(bank.TryGet(MusicPlayer.MenuMusic, out SoundEntry first));
        Assert.Equal(SoundCodec.Ilsf, first.Codec);

        fileSystem.RegisterDevice(new LocalFileDevice(GameDataSource.Igi2.RequireRoot()));
        bank.MountArchive(MenuSounds);

        Assert.True(bank.TryGet(MusicPlayer.MenuMusic, out SoundEntry stale));
        Assert.Equal(SoundCodec.Ilsf, stale.Codec);
        Assert.Same(first, stale);
    }

    [RequiresTitlesDataFact("igi1", "igi2")]
    public void ClearingBeforeTheMountIsWhatMakesTheIncomingGamesThemeResolve()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameDataSource.Igi1.RequireRoot()));

        SoundBank bank = new(fileSystem, null);
        bank.MountArchive(MenuSounds);

        fileSystem.RegisterDevice(new LocalFileDevice(GameDataSource.Igi2.RequireRoot()));
        bank.Clear();
        bank.MountArchive(MenuSounds);

        Assert.True(bank.TryGet(MusicPlayer.MenuMusic, out SoundEntry fresh));
        Assert.Equal(SoundCodec.Mpeg, fresh.Codec);
        Assert.EndsWith(".mp3", fresh.Origin, StringComparison.OrdinalIgnoreCase);

        // Every other menu sound moves with it. Both games file them under the same names AND at the
        // same paths, so a bank that retired only the music would go on clicking and beeping in the
        // outgoing game's voice with nothing in the path to show for it. The recordings differ, which
        // is what makes that visible here.
        Assert.True(bank.TryGet("menu_high", out SoundEntry highlight));
        Assert.True(Mount(GameDataSource.Igi1).TryGet("menu_high", out SoundEntry outgoing));
        Assert.Equal(highlight.Origin, outgoing.Origin);
        Assert.NotEqual(outgoing.Data.Length, highlight.Data.Length);
    }

    [RequiresTitlesDataFact("igi1", "igi2")]
    public void StoppingIsWhatRetiresTheOutgoingGamesThemeRatherThanTheNewBank()
    {
        // The whole bug, in the order the host hits it. Without the Stop, the score believes menu_music
        // is already playing, declines to start the sequel's, and the first game's voice keeps sounding.
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameDataSource.Igi1.RequireRoot()));

        SoundBank bank = new(fileSystem, null);
        bank.MountArchive(MenuSounds);

        RecordingAudioDevice device = new();
        MusicPlayer music = new(new SoundSystem(device, bank, fileSystem, null), null);

        Assert.True(music.Play(MusicPlayer.MenuMusic));
        Assert.Single(device.Played);
        Assert.False(device.Played[0].IsStream, "The first game's theme is resident.");

        // The bank is rebuilt for the other game and the score is NOT told.
        fileSystem.RegisterDevice(new LocalFileDevice(GameDataSource.Igi2.RequireRoot()));
        bank.Clear();
        bank.MountArchive(MenuSounds);

        Assert.True(music.Play(MusicPlayer.MenuMusic));
        Assert.Single(device.Played);
        Assert.Equal(1, device.ActiveVoiceCount);

        // Told, as ResetSoundsForTitle tells it. Now the sequel's theme starts, and as a stream.
        music.Stop();
        Assert.Equal(0, device.ActiveVoiceCount);

        Assert.True(music.Play(MusicPlayer.MenuMusic));
        Assert.Equal(2, device.Played.Count);
        Assert.True(device.Played[1].IsStream, "The sequel's theme is decoded as it plays.");
        Assert.Equal(1, device.ActiveVoiceCount);
        Assert.Equal(MusicPlayer.MenuMusic, music.CurrentTrack);
    }

    private static SoundBank Mount(GameDataSource source)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(source.RequireRoot()));

        SoundBank bank = new(fileSystem, null);
        Assert.True(bank.MountArchive(MenuSounds) > 0, $"{source.Title.DisplayName} ships menu sounds.");
        return bank;
    }
}
