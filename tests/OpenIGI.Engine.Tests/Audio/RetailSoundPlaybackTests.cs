using OpenIGI.Audio;
using OpenIGI.Audio.OpenAL;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Vfs;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Engine.Tests.Audio;

/// <summary>
/// Plays real sounds out of a real installation through the real backend — the whole chain from the
/// game's own bytes to a sounding voice.
/// </summary>
/// <remarks>
/// Skipped when <c>IGI_GAME_PATH</c> is unset, and the parts that need hardware check for it first, so
/// this passes on a continuous-integration runner with no sound card.
/// </remarks>
public sealed class RetailSoundPlaybackTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly OpenAlAudioDevice _device = new(AudioDeviceOptions.Default with { VoiceCount = 16 });

    public RetailSoundPlaybackTests(ITestOutputHelper output) => _output = output;

    public void Dispose() => _device.Dispose();

    [RequiresGameDataFact]
    public void AResidentEffectPlaysFromTheBank()
    {
        using SoundSystem sounds = Build(out _);
        if (!_device.IsAvailable)
        {
            _output.WriteLine("No audio device; the bank half still ran.");
            return;
        }

        AudioVoiceHandle voice = sounds.Play("glock_shot_1", VoiceParameters.Default);

        Assert.True(voice.IsValidHandle, "A shipped sound should have found a voice.");
        Assert.True(sounds.IsPlaying(voice));

        sounds.Stop(voice);
    }

    [RequiresGameDataFact]
    public void AStreamedSoundPlaysWithoutBeingHeldInMemory()
    {
        using SoundSystem sounds = Build(out SoundBank bank);
        if (!_device.IsAvailable)
        {
            return;
        }

        // Three megabytes packed, about thirteen expanded. The bank holds a path and nothing else, and
        // the device is fed a buffer at a time -- so this playing at all is the streaming path working.
        Assert.True(bank.TryGet("game_music", out SoundEntry music));
        Assert.True(music.Data.IsEmpty, "A streamed sound must not be resident.");

        AudioVoiceHandle voice = sounds.Play(
            "game_music",
            VoiceParameters.Default with { Category = SoundCategory.Music, Looping = true });

        Assert.True(voice.IsValidHandle);
        Assert.True(sounds.IsPlaying(voice));

        // Pump it the way the frame loop does. It has to still be sounding afterwards: a stream that is
        // not refilled runs dry and stops, which is the failure this catches.
        for (int i = 0; i < 8; i++)
        {
            sounds.Update();
        }

        Assert.True(sounds.IsPlaying(voice), "The music stopped while being pumped.");
        sounds.Stop(voice);
    }

    [RequiresGameDataFact]
    public void TheSameSoundTwiceUploadsOneBuffer()
    {
        // The point of the cache: a firefight plays one sound hundreds of times and must not re-decode
        // and re-upload it each time.
        RecordingAudioDevice recorder = new();
        using SoundSystem sounds = Build(out _, recorder);

        for (int i = 0; i < 5; i++)
        {
            sounds.Play("glock_shot_1", VoiceParameters.Default);
        }

        Assert.Single(recorder.CreatedBuffers);
        Assert.Equal(5, recorder.Played.Count);
    }

    [RequiresGameDataFact]
    public void APositionalSoundCarriesItsPlaceInMetres()
    {
        RecordingAudioDevice recorder = new();
        using SoundSystem sounds = Build(out _, recorder);

        // Engine units in, metres out: the render world divides by 4096 on load and the device works in
        // the same units the renderer does.
        sounds.PlayAt("glock_shot_1", new Vector3d(4096 * 10, 0, 0), 2f, 40f);

        VoiceParameters played = Assert.Single(recorder.Played).Parameters;
        Assert.True(played.IsPositional);
        Assert.Equal(10.0, played.Position.X, 6);
        Assert.Equal(2f, played.MinimumDistance);
        Assert.Equal(40f, played.MaximumDistance);
    }

    [RequiresGameDataFact]
    public void AnUnknownNameIsIgnoredRatherThanFatal()
    {
        using SoundSystem sounds = Build(out _);
        Assert.Equal(AudioVoiceHandle.None, sounds.Play("there_is_no_such_sound", VoiceParameters.Default));
    }

    private SoundSystem Build(out SoundBank bank, IAudioDevice? device = null)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        fileSystem.SetAlias(VirtualFileSystem.MissionAlias, "LOCAL:missions/location0/level1/");

        bank = new SoundBank(fileSystem, null);
        bank.MountDirectory("LOCAL:common/sounds");
        bank.MountDirectory("MISSION:sounds");

        return new SoundSystem(device ?? _device, bank, fileSystem, null);
    }
}
