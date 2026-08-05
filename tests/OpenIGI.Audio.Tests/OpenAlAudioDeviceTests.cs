using OpenIGI.Audio;
using OpenIGI.Audio.OpenAL;
using OpenIGI.Core.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Audio.Tests;

/// <summary>
/// Exercises the real backend. Every test here has to pass on a machine with no sound card, because
/// continuous integration is one — so they assert the contract holds whether or not audio opened.
/// </summary>
public sealed class OpenAlAudioDeviceTests
{
    private readonly ITestOutputHelper _output;

    public OpenAlAudioDeviceTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ADeviceAlwaysConstructsAndDisposes()
    {
        using OpenAlAudioDevice device = new(AudioDeviceOptions.Default);
        _output.WriteLine($"available={device.IsAvailable} voices={device.VoiceCount}");

        // The point of the test: no throw either way. A missing OpenAL must not stop the game booting.
        Assert.True(device.VoiceCount >= 0);
    }

    [Fact]
    public void EverySurfaceIsSafeWhenNoHardwareOpened()
    {
        using OpenAlAudioDevice device = new(AudioDeviceOptions.Default with { VoiceCount = 4 });

        device.SetCategoryGain(SoundCategory.Music, 0.5f);
        Assert.Equal(0.5f, device.GetCategoryGain(SoundCategory.Music));

        device.Listener = AudioListenerState.Default with { Position = new Vector3d(1, 2, 3) };
        Assert.Equal(new Vector3d(1, 2, 3), device.Listener.Position);

        // These all have to be no-ops rather than throws when there is no device behind them.
        device.SetVoiceParameters(AudioVoiceHandle.None, VoiceParameters.Default);
        device.StopVoice(AudioVoiceHandle.None);
        device.StopAll();
        device.Update();
        device.DestroyBuffer(AudioBufferHandle.None);
        Assert.False(device.IsPlaying(AudioVoiceHandle.None));
    }

    [Fact]
    public void AToneUploadsAndPlaysWhenHardwareIsPresent()
    {
        using OpenAlAudioDevice device = new(AudioDeviceOptions.Default with { VoiceCount = 8 });
        if (!device.IsAvailable)
        {
            _output.WriteLine("No audio device; skipping the audible half.");
            return;
        }

        AudioFormat format = new(22050, 1);
        AudioBufferHandle buffer = device.CreateBuffer(format, Tone(format, seconds: 0.05));
        Assert.True(buffer.IsValidHandle);

        AudioVoiceHandle voice = device.Play(buffer, VoiceParameters.Default with { Gain = 0.2f });
        Assert.True(voice.IsValidHandle);
        Assert.True(device.IsPlaying(voice));
        Assert.Equal(1, device.ActiveVoiceCount);

        device.StopVoice(voice);
        Assert.False(device.IsPlaying(voice));
        Assert.Equal(0, device.ActiveVoiceCount);

        device.DestroyBuffer(buffer);
    }

    [Fact]
    public void AStaleHandleIsIgnoredRatherThanAppliedToWhateverTookItsPlace()
    {
        using OpenAlAudioDevice device = new(AudioDeviceOptions.Default with { VoiceCount = 1 });
        if (!device.IsAvailable)
        {
            return;
        }

        AudioFormat format = new(22050, 1);
        AudioBufferHandle buffer = device.CreateBuffer(format, Tone(format, seconds: 0.05));

        AudioVoiceHandle first = device.Play(buffer, VoiceParameters.Default with { Priority = 0 });
        Assert.True(first.IsValidHandle);

        // The pool holds one voice, so a more important sound takes it.
        AudioVoiceHandle second = device.Play(buffer, VoiceParameters.Default with { Priority = 10 });
        Assert.True(second.IsValidHandle);
        Assert.NotEqual(first, second);

        // The first caller still holds its handle. Stopping through it must not silence the sound that
        // stole its slot -- this is the whole reason a handle carries a generation.
        Assert.False(device.IsPlaying(first));
        device.StopVoice(first);
        Assert.True(device.IsPlaying(second));

        device.DestroyBuffer(buffer);
    }

    [Fact]
    public void AVoiceIsRefusedRatherThanStolenFromSomethingMoreImportant()
    {
        using OpenAlAudioDevice device = new(AudioDeviceOptions.Default with { VoiceCount = 1 });
        if (!device.IsAvailable)
        {
            return;
        }

        AudioFormat format = new(22050, 1);
        AudioBufferHandle buffer = device.CreateBuffer(format, Tone(format, seconds: 0.05));

        AudioVoiceHandle important = device.Play(buffer, VoiceParameters.Default with { Priority = 10 });
        Assert.True(important.IsValidHandle);

        Assert.Equal(AudioVoiceHandle.None, device.Play(buffer, VoiceParameters.Default with { Priority = 1 }));
        Assert.True(device.IsPlaying(important));

        device.DestroyBuffer(buffer);
    }

    [Fact]
    public void AStreamIsDisposedEvenWhenItCannotBePlayed()
    {
        using OpenAlAudioDevice device = new(AudioDeviceOptions.Default with { VoiceCount = 1 });

        // Whether or not hardware opened, the device owns the source it is handed. Leaking one leaks a
        // file handle every time the music starts.
        CountingSource source = new();
        AudioVoiceHandle voice = device.PlayStream(source, VoiceParameters.Default);

        if (!voice.IsValidHandle)
        {
            Assert.True(source.IsDisposed);
            return;
        }

        device.StopVoice(voice);
        Assert.True(source.IsDisposed);
    }

    [Fact]
    public void StoppingEverythingLeavesNoActiveVoices()
    {
        using OpenAlAudioDevice device = new(AudioDeviceOptions.Default with { VoiceCount = 4 });
        if (!device.IsAvailable)
        {
            return;
        }

        AudioFormat format = new(22050, 1);
        AudioBufferHandle buffer = device.CreateBuffer(format, Tone(format, seconds: 0.05));

        for (int i = 0; i < 4; i++)
        {
            device.Play(buffer, VoiceParameters.Default);
        }

        Assert.Equal(4, device.ActiveVoiceCount);
        device.StopAll();
        Assert.Equal(0, device.ActiveVoiceCount);

        device.DestroyBuffer(buffer);
    }

    [Fact]
    public void AnUnacceptableFormatIsRejected()
    {
        using OpenAlAudioDevice device = new(AudioDeviceOptions.Default);
        Assert.Throws<ArgumentException>(() => device.CreateBuffer(new AudioFormat(0, 1), new byte[16]));
        Assert.Throws<ArgumentException>(() => device.CreateBuffer(new AudioFormat(22050, 5), new byte[16]));
    }

    private static byte[] Tone(AudioFormat format, double seconds)
    {
        int frames = (int)(format.SampleRate * seconds);
        byte[] pcm = new byte[frames * format.BlockAlign];

        for (int i = 0; i < frames; i++)
        {
            short sample = (short)(Math.Sin(2 * Math.PI * 440 * i / format.SampleRate) * 8000);
            for (int channel = 0; channel < format.Channels; channel++)
            {
                int offset = ((i * format.Channels) + channel) * sizeof(short);
                pcm[offset] = (byte)(sample & 0xFF);
                pcm[offset + 1] = (byte)((sample >> 8) & 0xFF);
            }
        }

        return pcm;
    }

    private sealed class CountingSource : ISampleSource
    {
        public bool IsDisposed { get; private set; }

        public AudioFormat Format => new(22050, 1);

        public bool CanRestart => true;

        public int Read(Span<short> destination)
        {
            destination.Clear();
            return destination.Length;
        }

        public void Restart()
        {
        }

        public void Dispose() => IsDisposed = true;
    }
}
