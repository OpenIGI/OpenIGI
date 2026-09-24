using OpenIGI.Audio;
using Xunit;

namespace OpenIGI.Audio.Tests;

/// <summary>The pool every backend allocates, steals and refills through.</summary>
public sealed class VoicePoolTests
{
    [Fact]
    public void Voices_are_handed_out_in_slot_order_until_none_is_free()
    {
        VoicePool<VoiceSlot> pool = Pool(3, out _);

        Assert.Equal(0, pool.Acquire(0)!.Index);
        Assert.Equal(1, pool.Acquire(0)!.Index);
        Assert.Equal(2, pool.Acquire(0)!.Index);
        Assert.Null(pool.Acquire(0));
        Assert.Equal(3, pool.ActiveCount);
    }

    [Fact]
    public void A_full_pool_steals_the_least_important_voice_and_silences_it_first()
    {
        VoicePool<VoiceSlot> pool = Pool(2, out List<int> silenced);
        VoiceSlot loud = pool.Acquire(0)!;
        loud.Parameters = VoiceParameters.Default with { Priority = 5 };
        VoiceSlot quiet = pool.Acquire(0)!;
        quiet.Parameters = VoiceParameters.Default with { Priority = 1 };
        AudioVoiceHandle quietHandle = quiet.Handle;

        VoiceSlot stolen = pool.Acquire(3)!;

        Assert.Same(quiet, stolen);
        Assert.Equal([quiet.Index], silenced);
        Assert.True(stolen.IsActive);
        Assert.NotEqual(quietHandle, stolen.Handle);
        Assert.False(pool.TryResolve(quietHandle, out _));
        Assert.True(pool.TryResolve(loud.Handle, out _));
    }

    [Fact]
    public void An_equally_important_voice_is_not_stolen()
    {
        VoicePool<VoiceSlot> pool = Pool(1, out List<int> silenced);
        VoiceSlot voice = pool.Acquire(0)!;
        voice.Parameters = VoiceParameters.Default with { Priority = 4 };

        Assert.Null(pool.Acquire(4));
        Assert.Empty(silenced);
        Assert.True(voice.IsActive);
    }

    [Fact]
    public void A_handle_names_one_generation_of_one_slot()
    {
        VoicePool<VoiceSlot> pool = Pool(2, out _);
        VoiceSlot voice = pool.Acquire(0)!;
        AudioVoiceHandle handle = voice.Handle;

        Assert.True(pool.TryResolve(handle, out VoiceSlot resolved));
        Assert.Same(voice, resolved);
        Assert.False(pool.TryResolve(AudioVoiceHandle.None, out _));
        Assert.False(pool.TryResolve(new AudioVoiceHandle(99), out _));

        pool.Retire(voice);
        Assert.False(pool.TryResolve(handle, out _));

        VoiceSlot reused = pool.Acquire(0)!;
        Assert.Same(voice, reused);
        Assert.NotEqual(handle, reused.Handle);
        Assert.True(pool.TryResolve(reused.Handle, out _));
    }

    [Fact]
    public void Retiring_disposes_the_stream_and_clears_the_slot()
    {
        VoicePool<VoiceSlot> pool = Pool(1, out List<int> silenced);
        VoiceSlot voice = pool.Acquire(0)!;
        DisposableSource source = new();
        voice.Stream = source;
        voice.Buffer = new AudioBufferHandle(7);

        pool.RetireAll();

        Assert.True(source.IsDisposed);
        Assert.Null(voice.Stream);
        Assert.False(voice.IsStreaming);
        Assert.Equal(AudioBufferHandle.None, voice.Buffer);
        Assert.False(voice.IsActive);
        Assert.Equal([0], silenced);
        Assert.Equal(0, pool.ActiveCount);
    }

    [Fact]
    public void A_stream_buffer_is_one_buffers_worth_of_interleaved_samples()
    {
        VoicePool<VoiceSlot> pool = Pool(1, out _);
        VoiceSlot voice = pool.Acquire(0)!;
        voice.Stream = new DisposableSource(channels: 2, samples: VoiceSlot.StreamFramesPerBuffer * 2 * 3);

        Assert.Equal(VoiceSlot.StreamFramesPerBuffer * 2, pool.FillStreamBuffer(voice).Length);
        Assert.Equal(VoiceSlot.StreamFramesPerBuffer * 2, pool.FillStreamBuffer(voice).Length);
        Assert.Equal(VoiceSlot.StreamFramesPerBuffer * 2, pool.FillStreamBuffer(voice).Length);
        Assert.True(pool.FillStreamBuffer(voice).IsEmpty);
        Assert.True(voice.StreamDrained);
    }

    [Fact]
    public void A_looping_stream_is_rewound_at_most_once_per_fill()
    {
        VoicePool<VoiceSlot> pool = Pool(1, out _);
        VoiceSlot voice = pool.Acquire(0)!;
        voice.Parameters = VoiceParameters.Default with { Looping = true };

        // Half a buffer per pass: a fill crosses the loop point once and stops short rather than spinning.
        DisposableSource source = new(channels: 1, samples: VoiceSlot.StreamFramesPerBuffer / 2);
        voice.Stream = source;

        Assert.Equal(VoiceSlot.StreamFramesPerBuffer, pool.FillStreamBuffer(voice).Length);
        Assert.Equal(1, source.Restarts);
        Assert.False(voice.StreamDrained);

        // A source that answers nothing even after a rewind is finished, however it is looped.
        voice.Stream = new DisposableSource(channels: 1, samples: 0);
        Assert.True(pool.FillStreamBuffer(voice).IsEmpty);
        Assert.True(voice.StreamDrained);
    }

    [Fact]
    public void An_empty_pool_has_no_voices_and_refuses_every_request()
    {
        VoicePool<VoiceSlot> pool = VoicePool.Empty<VoiceSlot>();

        Assert.Equal(0, pool.Count);
        Assert.Null(pool.Acquire(100));
        Assert.False(pool.TryResolve(new AudioVoiceHandle(1), out _));
        pool.RetireAll();
    }

    private static VoicePool<VoiceSlot> Pool(int count, out List<int> silenced)
    {
        List<int> log = [];
        silenced = log;
        return new VoicePool<VoiceSlot>(count, index => new VoiceSlot(index), voice => log.Add(voice.Index));
    }

    private sealed class DisposableSource : ISampleSource
    {
        private readonly int _samples;
        private int _position;

        public DisposableSource(int channels = 1, int samples = 0)
        {
            Format = new AudioFormat(22050, channels);
            _samples = samples;
        }

        public bool IsDisposed { get; private set; }

        public int Restarts { get; private set; }

        public AudioFormat Format { get; }

        public bool CanRestart => true;

        public int Read(Span<short> destination)
        {
            int count = Math.Min(destination.Length, _samples - _position);
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
