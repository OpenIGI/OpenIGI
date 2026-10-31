using OpenIGI.Audio;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Host;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Parity.Tests;

/// <summary>Co-op voice between two real games: what one says into its microphone is played by the other.</summary>
[Collection(ParityGroup.Name)]
public sealed class Igi1CoopVoiceTests
{
    [RequiresGameDataFact]
    public void Each_player_on_an_open_mic_is_heard_by_the_other()
    {
        var hostAudio = new ListeningAudio();
        var joinerAudio = new ListeningAudio();
        using var pair = new Igi1CoopPair(hostAudio, joinerAudio);
        Assert.True(pair.RunUntil(() => pair.Joined, 6000) >= 0, "the joiner never got in");
        pair.RunUntil(() => false, 30);

        // Open mic rather than the key: a lockstep run keeps the window's own keys from the game.
        CoopVoiceCVars.Mode.Value = 2;
        int frames = pair.RunUntil(() => hostAudio.Device.Heard > 0.05 && joinerAudio.Device.Heard > 0.05, 300);

        Assert.True(frames >= 0, $"host heard {hostAudio.Device.Heard:0.000} (gain {hostAudio.Device.LastGain:0.00}), joiner heard {joinerAudio.Device.Heard:0.000} (gain {joinerAudio.Device.LastGain:0.00})");
    }

    [RequiresGameDataFact]
    public void A_voice_the_device_let_go_of_is_played_again()
    {
        var hostAudio = new ListeningAudio();
        using var pair = new Igi1CoopPair(hostAudio, new ListeningAudio());
        Assert.True(pair.RunUntil(() => pair.Joined, 6000) >= 0, "the joiner never got in");
        CoopVoiceCVars.Mode.Value = 2;
        Assert.True(pair.RunUntil(() => hostAudio.Device.Streams > 0, 300) >= 0, "the host never played the joiner");

        hostAudio.Device.StopAll();
        hostAudio.Device.Heard = 0;
        Assert.True(pair.RunUntil(() => hostAudio.Device.Heard > 0.05, 300) >= 0, "the voice stayed silent once let go");
    }

    [RequiresGameDataFact]
    public void The_microphone_is_closed_once_nobody_talks()
    {
        var joinerAudio = new ListeningAudio();
        using var pair = new Igi1CoopPair(new ListeningAudio(), joinerAudio);
        Assert.True(pair.RunUntil(() => pair.Joined, 6000) >= 0, "the joiner never got in");
        CoopVoiceCVars.Mode.Value = 2;
        Assert.True(pair.RunUntil(() => joinerAudio.Device.Microphone is { Open: true }, 300) >= 0, "the microphone never opened");

        // Push to talk with the key up: closed after the tail, so nothing says it still listens.
        CoopVoiceCVars.Mode.Value = 1;
        Assert.True(pair.RunUntil(() => joinerAudio.Device.Microphone is { Open: false }, 150) >= 0, "the microphone stayed open");
    }

    [RequiresGameDataFact]
    public void Nobody_is_shown_talking_before_anyone_has_spoken()
    {
        using var pair = new Igi1CoopPair(new ListeningAudio(), new ListeningAudio());
        Assert.True(pair.RunUntil(() => pair.Joined, 6000) >= 0, "the joiner never got in");
        pair.RunUntil(() => false, 30);

        // Push to talk, the key up: neither game says anyone is talking -- its own player least of all.
        System.Reflection.MethodInfo talking = typeof(GameHost).GetMethod("Talking", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        Assert.Empty((IEnumerable<int>)talking.Invoke(pair.HostGame, [])!);
        Assert.Empty((IEnumerable<int>)talking.Invoke(pair.JoinerGame, [])!);
    }

    // An audio device that makes no sound but plays every stream as the real one would, and keeps how loud they were.
    private sealed class ListeningAudio : IAudioDeviceFactory
    {
        public ListeningDevice Device { get; } = new();

        public IAudioDevice Open(AudioDeviceOptions options, IOpenIgiLogger logger) => Device;
    }

    private sealed class ListeningDevice : IAudioDevice
    {
        private readonly NullAudioDevice _null = new();
        private readonly Dictionary<AudioVoiceHandle, (ISampleSource Source, float Gain)> _streams = [];
        private readonly short[] _buffer = new short[1600];

        public double Heard { get; set; }

        public int Streams => _streams.Count;

        public float LastGain { get; private set; } = -1f;

        public bool IsAvailable => true;

        public int VoiceCount => _null.VoiceCount;

        public int ActiveVoiceCount => _null.ActiveVoiceCount;

        public AudioListenerState Listener { get => _null.Listener; set => _null.Listener = value; }

        public void SetCategoryGain(SoundCategory category, float gain) => _null.SetCategoryGain(category, gain);

        public float GetCategoryGain(SoundCategory category) => _null.GetCategoryGain(category);

        public AudioBufferHandle CreateBuffer(AudioFormat format, ReadOnlySpan<byte> pcm) => _null.CreateBuffer(format, pcm);

        public void DestroyBuffer(AudioBufferHandle buffer) => _null.DestroyBuffer(buffer);

        public AudioVoiceHandle Play(AudioBufferHandle buffer, in VoiceParameters parameters) => _null.Play(buffer, parameters);

        public AudioVoiceHandle PlayStream(ISampleSource source, in VoiceParameters parameters)
        {
            AudioVoiceHandle handle = _null.PlayStream(source, parameters);
            if (source is LiveVoice)
            {
                _streams[handle] = (source, parameters.Gain);
            }

            return handle;
        }

        public bool IsPlaying(AudioVoiceHandle voice) => _streams.ContainsKey(voice) || _null.IsPlaying(voice);

        public void SetVoiceParameters(AudioVoiceHandle voice, in VoiceParameters parameters)
        {
            if (_streams.TryGetValue(voice, out var stream))
            {
                _streams[voice] = stream with { Gain = parameters.Gain };
                LastGain = parameters.Gain;
            }

            _null.SetVoiceParameters(voice, parameters);
        }

        public void StopVoice(AudioVoiceHandle voice)
        {
            _streams.Remove(voice);
            _null.StopVoice(voice);
        }

        public void StopAll()
        {
            _streams.Clear();
            _null.StopAll();
        }

        // A frame's worth from each stream, heard at the gain it is played at.
        public void Update()
        {
            foreach ((ISampleSource source, float gain) in _streams.Values)
            {
                int read = source.Read(_buffer);
                double sum = 0;
                for (int i = 0; i < read; i++)
                {
                    sum += (double)_buffer[i] * _buffer[i];
                }

                Heard = Math.Max(Heard, Math.Sqrt(sum / Math.Max(1, read)) / short.MaxValue * gain);
            }

            _null.Update();
        }

        public ToneMicrophone? Microphone { get; private set; }

        public IMicrophone? OpenMicrophone(int sampleRate) => Microphone = new ToneMicrophone(sampleRate);

        public void Dispose() => _null.Dispose();
    }

    // A microphone hearing a steady tone: a frame's worth of it, then nothing more until the next frame asks.
    private sealed class ToneMicrophone(int sampleRate) : IMicrophone
    {
        public bool Open { get; private set; } = true;

        private long _at;
        private bool _given;

        public int SampleRate => sampleRate;

        public int Read(Span<short> destination)
        {
            _given = !_given;
            if (!_given)
            {
                return 0;
            }

            int count = Math.Min(destination.Length, sampleRate / 30);
            for (int i = 0; i < count; i++, _at++)
            {
                destination[i] = (short)(Math.Sin(_at * 2 * Math.PI * 440 / sampleRate) * 12000);
            }

            return count;
        }

        public void Dispose() => Open = false;
    }
}
