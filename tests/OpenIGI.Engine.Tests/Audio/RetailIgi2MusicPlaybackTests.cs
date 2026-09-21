using OpenIGI.Audio;
using OpenIGI.Audio.OpenAL;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Vfs;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Engine.Tests.Audio;

/// <summary>
/// Plays I.G.I. 2's menu theme out of a real installation through the real backend — the whole chain
/// from the archived MPEG bytes to a sounding voice.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <c>RetailSoundPlaybackTests</c> for the sequel, and the only path in either game
/// that is neither resident nor reopened from a file: the track is inside an archive, so the bank keeps
/// its packed bytes and the decoder reads them out of memory a buffer at a time.
/// </para>
/// <para>
/// Skipped when <c>IGI2_GAME_PATH</c> is unset, and the part that needs hardware checks for it first,
/// so this passes on a runner with no sound card.
/// </para>
/// </remarks>
[Collection(OpenAlDeviceFixture.Name)]
public sealed class RetailIgi2MusicPlaybackTests : IDisposable
{
    private const string MenuSounds = "LOCAL:menusystem/sound/sounds.res";

    private readonly ITestOutputHelper _output;
    private readonly OpenAlAudioDevice _device = new(AudioDeviceOptions.Default with { VoiceCount = 16 });

    /// <summary>Initializes a new instance of the <see cref="RetailIgi2MusicPlaybackTests"/> class.</summary>
    /// <param name="output">The xunit output sink.</param>
    public RetailIgi2MusicPlaybackTests(ITestOutputHelper output) => _output = output;

    /// <inheritdoc/>
    public void Dispose() => _device.Dispose();

    [RequiresTitleDataFact("igi2")]
    public void TheMenuThemePlaysAndKeepsPlayingWhilePumped()
    {
        VirtualFileSystem fileSystem = FileSystem();
        SoundBank bank = new(fileSystem, null);
        Assert.True(bank.MountArchive(MenuSounds) > 0);

        using SoundSystem sounds = new(_device, bank, fileSystem, null);
        if (!_device.IsAvailable)
        {
            _output.WriteLine("No audio device; the bank half still ran.");
            return;
        }

        MusicPlayer music = new(sounds, null);
        Assert.True(music.Play(MusicPlayer.MenuMusic), "The sequel's menu theme did not start.");
        Assert.True(music.IsPlaying);

        // Pump it the way the frame loop does. It has to still be sounding afterwards: a stream that is
        // not refilled runs dry and stops, which is the failure this catches -- and for an MPEG track
        // the refill is a decode, so this is the decoder running under the device's own thread.
        for (int i = 0; i < 16; i++)
        {
            sounds.Update();
        }

        Assert.True(music.IsPlaying, "The theme stopped while being pumped.");
        music.Stop();
        Assert.False(music.IsPlaying);
    }

    [RequiresTitleDataFact("igi2")]
    public void TheThemeIsStreamedRatherThanUploadedAsOneBuffer()
    {
        // Thirty-six megabytes expanded. A buffer upload would mean the whole track decoded before the
        // menu drew its first frame, which is exactly what the streaming path exists to avoid.
        RecordingAudioDevice recorder = new();
        VirtualFileSystem fileSystem = FileSystem();
        SoundBank bank = new(fileSystem, null);
        bank.MountArchive(MenuSounds);

        using SoundSystem sounds = new(recorder, bank, fileSystem, null);
        AudioVoiceHandle voice = sounds.Play(
            MusicPlayer.MenuMusic,
            VoiceParameters.Default with { Category = SoundCategory.Music, Looping = true });

        Assert.True(voice.IsValidHandle);
        Assert.Empty(recorder.CreatedBuffers);
        Assert.True(Assert.Single(recorder.Played).IsStream);

        // And the source is released with the voice rather than leaked, which for a decoder holding an
        // eight-megabyte stream is not a rounding error.
        Assert.Equal(1, recorder.LiveStreamCount);
        sounds.Stop(voice);
        Assert.Equal(0, recorder.LiveStreamCount);
    }

    [RequiresTitleDataFact("igi2")]
    public void TheMenuClicksComeFromTheSequelsOwnArchive()
    {
        // The music is what was noticed, but every menu sound had the same problem: the two games ship
        // different recordings under the same names at the same paths.
        VirtualFileSystem fileSystem = FileSystem();
        SoundBank bank = new(fileSystem, null);
        bank.MountArchive(MenuSounds);

        foreach (string name in new[] { "menu_high", "menu_sel", "menu_ok", "menu_cancel" })
        {
            Assert.True(bank.TryGet(name, out SoundEntry entry), $"The sequel ships {name}.");
            Assert.Equal(SoundCodec.Ilsf, entry.Codec);
            Assert.False(entry.IsStreamed);
        }
    }

    private static VirtualFileSystem FileSystem()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameDataSource.Igi2.RequireRoot()));
        return fileSystem;
    }
}
