using OpenIGI.Audio;
using OpenIGI.Audio.Web;
using OpenIGI.Core.Numerics;
using Xunit;

namespace OpenIGI.Audio.Tests;

/// <summary>
/// The browser's device against a recorded page: the device's model of voices, buffers and streams is
/// what is under test, and the page answers as a browser would.
/// </summary>
public sealed class WebAudioDeviceTests
{
    private static readonly AudioFormat Mono16 = new(22050, 1);

    [Fact]
    public void Opening_asks_the_page_for_the_requested_rate_and_voices()
    {
        FakeWebAudioPage page = new() { SampleRateToOpen = 44100 };
        using WebAudioDevice device = new(AudioDeviceOptions.Default with { VoiceCount = 12, SampleRate = 22050 }, page);

        Assert.True(device.IsAvailable);
        Assert.Equal(12, device.VoiceCount);
        Assert.Equal(44100, device.SampleRate);
        Assert.Contains("open 22050 12", page.Log);
    }

    [Fact]
    public void A_page_without_audio_leaves_a_working_silence()
    {
        FakeWebAudioPage page = new() { SampleRateToOpen = 0 };
        using WebAudioDevice device = new(AudioDeviceOptions.Default with { VoiceCount = 4 }, page);

        Assert.False(device.IsAvailable);
        Assert.Equal(0, device.VoiceCount);

        // Every surface is a no-op rather than a throw when there is no context behind it.
        device.SetCategoryGain(SoundCategory.Music, 0.5f);
        Assert.Equal(0.5f, device.GetCategoryGain(SoundCategory.Music));
        device.Listener = AudioListenerState.Default with { Position = new Vector3d(1, 2, 3) };
        Assert.Equal(new Vector3d(1, 2, 3), device.Listener.Position);
        Assert.Equal(AudioBufferHandle.None, device.CreateBuffer(Mono16, Tone(0.05)));
        Assert.Equal(AudioVoiceHandle.None, device.Play(new AudioBufferHandle(1), VoiceParameters.Default));
        device.SetVoiceParameters(AudioVoiceHandle.None, VoiceParameters.Default);
        device.StopVoice(AudioVoiceHandle.None);
        device.StopAll();
        device.Update();
        device.DestroyBuffer(AudioBufferHandle.None);
        Assert.False(device.IsPlaying(AudioVoiceHandle.None));
        Assert.Equal(0, page.CloseCalls);
    }

    [Fact]
    public void A_tone_uploads_plays_and_stops()
    {
        FakeWebAudioPage page = new();
        using WebAudioDevice device = new(AudioDeviceOptions.Default with { VoiceCount = 8 }, page);

        AudioBufferHandle buffer = device.CreateBuffer(Mono16, Tone(0.05));
        Assert.True(buffer.IsValidHandle);
        Assert.Single(page.BufferBytes);

        AudioVoiceHandle voice = device.Play(buffer, VoiceParameters.Default with { Gain = 0.2f, Looping = true });
        Assert.True(voice.IsValidHandle);
        Assert.True(device.IsPlaying(voice));
        Assert.Equal(1, device.ActiveVoiceCount);

        FakeWebAudioPage.VoiceState state = Assert.Single(page.Voices).Value;
        Assert.Equal(page.BufferBytes.Keys.Single(), state.Buffer);
        Assert.True(state.Looping);
        // The gain is the original's quantised curve, not the raw number.
        Assert.Equal(MixBuses.Quantise(0.2f), state.Gain);

        device.StopVoice(voice);
        Assert.False(device.IsPlaying(voice));
        Assert.Equal(0, device.ActiveVoiceCount);
        Assert.Contains("stop 0", page.Log);

        device.DestroyBuffer(buffer);
        Assert.Empty(page.BufferBytes);
    }

    [Fact]
    public void A_finished_sound_frees_its_voice_on_update()
    {
        FakeWebAudioPage page = new();
        using WebAudioDevice device = new(AudioDeviceOptions.Default with { VoiceCount = 2 }, page);
        AudioBufferHandle buffer = device.CreateBuffer(Mono16, Tone(0.05));

        AudioVoiceHandle voice = device.Play(buffer, VoiceParameters.Default);
        device.Update();
        Assert.True(device.IsPlaying(voice));

        page.FinishVoice(0);
        device.Update();
        Assert.False(device.IsPlaying(voice));
        Assert.Equal(0, device.ActiveVoiceCount);
    }

    [Fact]
    public void A_stale_handle_is_ignored_rather_than_applied_to_whatever_took_its_place()
    {
        FakeWebAudioPage page = new();
        using WebAudioDevice device = new(AudioDeviceOptions.Default with { VoiceCount = 1 }, page);
        AudioBufferHandle buffer = device.CreateBuffer(Mono16, Tone(0.05));

        AudioVoiceHandle first = device.Play(buffer, VoiceParameters.Default with { Priority = 0 });
        AudioVoiceHandle second = device.Play(buffer, VoiceParameters.Default with { Priority = 10 });
        Assert.True(second.IsValidHandle);
        Assert.NotEqual(first, second);

        Assert.False(device.IsPlaying(first));
        device.StopVoice(first);
        Assert.True(device.IsPlaying(second));
    }

    [Fact]
    public void A_voice_is_refused_rather_than_stolen_from_something_more_important()
    {
        FakeWebAudioPage page = new();
        using WebAudioDevice device = new(AudioDeviceOptions.Default with { VoiceCount = 1 }, page);
        AudioBufferHandle buffer = device.CreateBuffer(Mono16, Tone(0.05));

        AudioVoiceHandle important = device.Play(buffer, VoiceParameters.Default with { Priority = 10 });
        Assert.Equal(AudioVoiceHandle.None, device.Play(buffer, VoiceParameters.Default with { Priority = 1 }));
        Assert.True(device.IsPlaying(important));
    }

    [Fact]
    public void Destroying_a_buffer_stops_the_voices_sounding_it()
    {
        FakeWebAudioPage page = new();
        using WebAudioDevice device = new(AudioDeviceOptions.Default with { VoiceCount = 4 }, page);
        AudioBufferHandle buffer = device.CreateBuffer(Mono16, Tone(0.05));
        AudioBufferHandle other = device.CreateBuffer(Mono16, Tone(0.05));

        AudioVoiceHandle voice = device.Play(buffer, VoiceParameters.Default);
        AudioVoiceHandle kept = device.Play(other, VoiceParameters.Default);

        device.DestroyBuffer(buffer);
        Assert.False(device.IsPlaying(voice));
        Assert.True(device.IsPlaying(kept));
    }

    [Fact]
    public void A_stream_is_primed_pumped_and_freed_when_it_drains()
    {
        FakeWebAudioPage page = new();
        using WebAudioDevice device = new(AudioDeviceOptions.Default with { VoiceCount = 1 }, page);

        // Six buffers' worth, so the priming leaves two to pump.
        CountingSource source = new(frames: VoiceSlot.StreamFramesPerBuffer * 6, channels: 2);
        AudioVoiceHandle voice = device.PlayStream(source, VoiceParameters.Default with { Category = SoundCategory.Music });
        Assert.True(voice.IsValidHandle);

        FakeWebAudioPage.VoiceState state = page.Voices[0];
        Assert.Equal((22050, 2), state.Stream);
        Assert.Equal(VoiceSlot.StreamBufferCount, state.Chunks);
        Assert.All(state.ChunkBytes, bytes => Assert.Equal(VoiceSlot.StreamFramesPerBuffer * 2 * sizeof(short), bytes));

        // Nothing has finished, so a pump adds nothing.
        device.Update();
        Assert.Equal(VoiceSlot.StreamBufferCount, state.Chunks);

        // Two chunks finish: two more are queued, which is the end of the source.
        page.FinishChunk(0);
        page.FinishChunk(0);
        device.Update();
        Assert.Equal(VoiceSlot.StreamBufferCount, state.Chunks);
        Assert.Equal(6, state.ChunkBytes.Count);
        Assert.False(source.IsDisposed);

        // The queue plays out, and only then is the voice freed and the source disposed.
        for (int i = 0; i < VoiceSlot.StreamBufferCount; i++)
        {
            page.FinishChunk(0);
        }

        device.Update();
        Assert.False(device.IsPlaying(voice));
        Assert.True(source.IsDisposed);
    }

    [Fact]
    public void A_looping_stream_rewinds_and_never_drains()
    {
        FakeWebAudioPage page = new();
        using WebAudioDevice device = new(AudioDeviceOptions.Default with { VoiceCount = 1 }, page);

        CountingSource source = new(frames: VoiceSlot.StreamFramesPerBuffer, channels: 1);
        AudioVoiceHandle voice = device.PlayStream(source, VoiceParameters.Default with { Looping = true });

        for (int round = 0; round < 5; round++)
        {
            page.FinishChunk(0);
            device.Update();
            Assert.True(device.IsPlaying(voice));
        }

        Assert.Equal(VoiceSlot.StreamBufferCount + 5, page.Voices[0].ChunkBytes.Count);
        Assert.True(source.Restarts >= 5);
    }

    [Fact]
    public void A_stream_is_disposed_even_when_it_cannot_be_played()
    {
        FakeWebAudioPage silent = new() { SampleRateToOpen = 0 };
        using (WebAudioDevice device = new(AudioDeviceOptions.Default, silent))
        {
            CountingSource source = new(frames: 10, channels: 1);
            Assert.Equal(AudioVoiceHandle.None, device.PlayStream(source, VoiceParameters.Default));
            Assert.True(source.IsDisposed);
        }

        FakeWebAudioPage page = new();
        using (WebAudioDevice device = new(AudioDeviceOptions.Default with { VoiceCount = 1 }, page))
        {
            CountingSource empty = new(frames: 0, channels: 1);
            Assert.Equal(AudioVoiceHandle.None, device.PlayStream(empty, VoiceParameters.Default));
            Assert.True(empty.IsDisposed);
            Assert.Equal(0, device.ActiveVoiceCount);
        }
    }

    [Fact]
    public void Disposing_closes_the_page_and_the_streams()
    {
        FakeWebAudioPage page = new();
        CountingSource source = new(frames: 100000, channels: 1);
        using (WebAudioDevice device = new(AudioDeviceOptions.Default with { VoiceCount = 2 }, page))
        {
            device.PlayStream(source, VoiceParameters.Default);
        }

        Assert.True(source.IsDisposed);
        Assert.Equal(1, page.CloseCalls);
    }

    [Fact]
    public void A_bus_gain_moves_the_page_bus_not_the_voices()
    {
        FakeWebAudioPage page = new();
        using WebAudioDevice device = new(AudioDeviceOptions.Default with { VoiceCount = 2 }, page);
        AudioBufferHandle buffer = device.CreateBuffer(Mono16, Tone(0.05));
        device.Play(buffer, VoiceParameters.Default with { Category = SoundCategory.Effects, Gain = 1f });
        int sets = page.Log.Count(line => line.StartsWith("set ", StringComparison.Ordinal));

        device.SetCategoryGain(SoundCategory.Effects, 0.25f);

        Assert.Equal(0.25f, page.BusGains[(int)SoundCategory.Effects]);
        Assert.Equal(0.25f, device.GetCategoryGain(SoundCategory.Effects));
        Assert.Equal(sets, page.Log.Count(line => line.StartsWith("set ", StringComparison.Ordinal)));
        // The voice's own level stays the quantised gain alone; the bus is the page's.
        Assert.Equal(MixBuses.Quantise(1f), page.Voices[0].Gain);
    }

    [Fact]
    public void A_placed_voice_hands_its_geometry_to_the_panner_when_spatial()
    {
        FakeWebAudioPage page = new();
        using WebAudioDevice device = new(AudioDeviceOptions.Default with { VoiceCount = 1, Spatial = true }, page);
        AudioBufferHandle buffer = device.CreateBuffer(Mono16, Tone(0.05));

        device.Play(buffer, VoiceParameters.Default.At(new Vector3d(10, 20, 30), 2f, 40f) with { Pitch = 1.5f });

        FakeWebAudioPage.VoiceState state = page.Voices[0];
        Assert.True(state.Positional);
        Assert.Equal((10.0, 20.0, 30.0), state.Position);
        Assert.Equal((2f, 40f), state.Distances);
        Assert.Equal(1.5f, state.Pitch);
    }

    [Fact]
    public void A_placed_voice_is_panned_flat_under_vanilla_and_follows_the_listener()
    {
        FakeWebAudioPage page = new();
        using WebAudioDevice device = new(AudioDeviceOptions.Vanilla with { VoiceCount = 1 }, page);
        AudioBufferHandle buffer = device.CreateBuffer(Mono16, Tone(0.05));

        // Ten metres to the listener's right, with a range whose sum is twenty: the original's pan is
        // local X over (min + max), so half right.
        VoiceParameters parameters = VoiceParameters.Default.At(new Vector3d(10, 0, 0), 5f, 15f);
        device.Play(buffer, parameters);

        FakeWebAudioPage.VoiceState state = page.Voices[0];
        Assert.False(state.Positional);
        Assert.Equal(0.5f, state.Pan, precision: 3);

        // Move the listener onto the source: the image centres without the voice being re-aimed by the game.
        device.Listener = AudioListenerState.Default with { Position = new Vector3d(10, 0, 0) };
        Assert.Equal(0f, state.Pan, precision: 3);
        Assert.Equal((10.0, 0.0, 0.0), (page.Listener.X, page.Listener.Y, page.Listener.Z));
    }

    [Fact]
    public void Reverse_stereo_mirrors_the_flat_pan()
    {
        FakeWebAudioPage page = new();
        using WebAudioDevice device = new(AudioDeviceOptions.Vanilla with { VoiceCount = 1, ReverseStereo = true }, page);
        AudioBufferHandle buffer = device.CreateBuffer(Mono16, Tone(0.05));

        device.Play(buffer, VoiceParameters.Default with { Pan = 0.75f });

        Assert.Equal(-0.75f, page.Voices[0].Pan, precision: 3);
    }

    [Fact]
    public void Stopping_everything_leaves_no_active_voices()
    {
        FakeWebAudioPage page = new();
        using WebAudioDevice device = new(AudioDeviceOptions.Default with { VoiceCount = 4 }, page);
        AudioBufferHandle buffer = device.CreateBuffer(Mono16, Tone(0.05));

        for (int i = 0; i < 4; i++)
        {
            device.Play(buffer, VoiceParameters.Default);
        }

        Assert.Equal(4, device.ActiveVoiceCount);
        device.StopAll();
        Assert.Equal(0, device.ActiveVoiceCount);
        Assert.Equal(4, page.Log.Count(line => line.StartsWith("stop ", StringComparison.Ordinal)));
    }

    [Fact]
    public void An_unacceptable_format_is_rejected()
    {
        using WebAudioDevice device = new(AudioDeviceOptions.Default, new FakeWebAudioPage());
        Assert.Throws<ArgumentException>(() => device.CreateBuffer(new AudioFormat(0, 1), new byte[16]));
        Assert.Throws<ArgumentException>(() => device.CreateBuffer(new AudioFormat(22050, 5), new byte[16]));
    }

    [Fact]
    public void The_factory_opens_a_device_over_its_page()
    {
        FakeWebAudioPage page = new();
        WebAudioDeviceFactory factory = new(page);

        using IAudioDevice first = factory.Open(AudioDeviceOptions.Default with { VoiceCount = 3 }, Core.Diagnostics.NullLogger.Instance);
        using IAudioDevice second = factory.Open(AudioDeviceOptions.Default with { VoiceCount = 5 }, Core.Diagnostics.NullLogger.Instance);

        Assert.IsType<WebAudioDevice>(first);
        Assert.Equal(3, first.VoiceCount);
        Assert.Equal(5, second.VoiceCount);
        Assert.Equal(2, page.OpenCalls);
    }

    private static byte[] Tone(double seconds)
    {
        int frames = (int)(Mono16.SampleRate * seconds);
        byte[] pcm = new byte[frames * Mono16.BlockAlign];
        for (int i = 0; i < frames; i++)
        {
            short sample = (short)(Math.Sin(2 * Math.PI * 440 * i / Mono16.SampleRate) * 8000);
            pcm[i * 2] = (byte)(sample & 0xFF);
            pcm[(i * 2) + 1] = (byte)((sample >> 8) & 0xFF);
        }

        return pcm;
    }

    /// <summary>A source of a fixed number of frames that counts its rewinds.</summary>
    private sealed class CountingSource : ISampleSource
    {
        private readonly int _samples;
        private int _position;

        public CountingSource(int frames, int channels)
        {
            Format = new AudioFormat(22050, channels);
            _samples = frames * channels;
        }

        public bool IsDisposed { get; private set; }

        public int Restarts { get; private set; }

        public AudioFormat Format { get; }

        public bool CanRestart => true;

        public int Read(Span<short> destination)
        {
            int count = Math.Min(destination.Length, _samples - _position);
            destination[..count].Clear();
            _position += count;
            return count;
        }

        public void Restart()
        {
            Restarts++;
            _position = 0;
        }

        public void Dispose() => IsDisposed = true;
    }
}
