using System.Buffers.Binary;
using OpenIGI.Audio;
using OpenIGI.Formats.Sound;
using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>
/// A sound's waveform with a play button: decoded once, played through the game's own audio device, a playhead
/// following it. Click the waveform to play from there.
/// </summary>
internal sealed class SoundPreview : EditorElement
{
    private readonly IAudioDevice _device;
    private readonly Button _play;
    private (float Min, float Max)[] _peaks = [];
    private byte[] _pcm = [];
    private AudioFormat _format;
    private AudioBufferHandle? _buffer;
    private AudioVoiceHandle? _voice;
    private double _duration;
    private double _started;
    private double _clock;
    private double _from;

    public SoundPreview(EditorTheme theme, IAudioDevice device)
        : base(theme)
    {
        _device = device;
        _play = new Button(theme, "Play", KitIcon.Play);
        _play.Clicked += () =>
        {
            if (IsPlaying)
            {
                Stop();
            }
            else
            {
                PlayFrom(0.0);
            }
        };
        AddChild(_play);
    }

    /// <summary>Gets a description of the sound shown, or empty.</summary>
    public string Facts { get; private set; } = string.Empty;

    private bool IsPlaying => _voice is { } voice && _device.IsPlaying(voice);

    /// <summary>Shows a sound file, or nothing; any playing preview stops.</summary>
    /// <param name="data">An ILSF sound, or empty.</param>
    /// <returns>Whether the bytes were a sound this preview reads.</returns>
    public bool Show(ReadOnlyMemory<byte> data)
    {
        Release();
        _peaks = [];
        Facts = string.Empty;
        if (data.IsEmpty || !IlsfFile.IsIlsf(data.Span))
        {
            return false;
        }

        IlsfFile sound = IlsfFile.Read(data);
        _pcm = sound.DecodePcm().ToArray();
        _format = new AudioFormat(sound.SampleRate, sound.Channels, 16);
        _duration = sound.Duration.TotalSeconds;
        Facts = FormattableString.Invariant(
            $"{sound.Duration.TotalSeconds:0.00} s  •  {sound.SampleRate} Hz  •  {(sound.Channels == 1 ? "mono" : $"{sound.Channels} channels")}  •  {(sound.IsAdpcm ? "ADPCM" : "PCM")}");
        BuildPeaks(512);
        return true;
    }

    /// <summary>Stops playback and frees the sound's buffer.</summary>
    public void Release()
    {
        Stop();
        if (_buffer is { } buffer)
        {
            _device.DestroyBuffer(buffer);
            _buffer = null;
        }
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        _play.Measure(context, new UiVec2(float.PositiveInfinity, Theme.ControlHeight));
        _play.Arrange(new UiRect(contentRect.X + Theme.Px(8f), contentRect.Y + Theme.Px(8f), _play.DesiredSize.X, Theme.ControlHeight));
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        _clock += elapsedSeconds;
        _play.Text = IsPlaying ? "Stop" : "Play";
        _play.Icon = IsPlaying ? KitIcon.Stop : KitIcon.Play;
        _play.Visibility = _peaks.Length > 0 ? Visibility.Visible : Visibility.Hidden;
    }

    /// <inheritdoc/>
    protected override bool OnPointerDown(in PointerEvent e)
    {
        UiRect wave = WaveRect;
        if (e.Button != MouseButton.Left || _peaks.Length == 0 || !wave.Contains(e.Position))
        {
            return false;
        }

        PlayFrom(Math.Clamp((e.Position.X - wave.X) / wave.Width, 0f, 1f) * _duration);
        return true;
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        painter.FillRect(Bounds, Theme.Input);
        if (_peaks.Length == 0)
        {
            return;
        }

        UiRect wave = WaveRect;
        float middle = wave.Center.Y;
        float half = wave.Height * 0.5f;
        float step = wave.Width / _peaks.Length;
        for (int i = 0; i < _peaks.Length; i++)
        {
            (float min, float max) = _peaks[i];
            float x = wave.X + (i * step);
            painter.FillRect(new UiRect(x, middle - (max * half), MathF.Max(1f, step), MathF.Max(1f, (max - min) * half)), Theme.Accent.WithAlpha(200));
        }

        painter.FillRect(new UiRect(wave.X, middle, wave.Width, Theme.Hairline), Theme.Border);
        if (IsPlaying && _duration > 0)
        {
            double at = Math.Min(_duration, _from + (_clock - _started));
            float x = wave.X + (float)(at / _duration * wave.Width);
            painter.FillRect(new UiRect(x, wave.Y, Theme.Px(2f), wave.Height), Theme.Text);
        }
    }

    private UiRect WaveRect => UiRect.FromEdges(Bounds.X + Theme.Px(8f), Bounds.Y + Theme.ControlHeight + Theme.Px(16f), Bounds.Right - Theme.Px(8f), Bounds.Bottom - Theme.Px(8f));

    private void BuildPeaks(int columns)
    {
        int frameBytes = _format.BlockAlign;
        int frames = frameBytes > 0 ? _pcm.Length / frameBytes : 0;
        if (frames == 0)
        {
            return;
        }

        columns = Math.Min(columns, frames);
        _peaks = new (float, float)[columns];
        for (int c = 0; c < columns; c++)
        {
            int first = (int)((long)c * frames / columns);
            int last = (int)((long)(c + 1) * frames / columns);
            float min = 0f;
            float max = 0f;
            for (int f = first; f < last; f++)
            {
                float sample = BinaryPrimitives.ReadInt16LittleEndian(_pcm.AsSpan(f * frameBytes, 2)) / 32768f;
                min = MathF.Min(min, sample);
                max = MathF.Max(max, sample);
            }

            _peaks[c] = (min, max);
        }
    }

    private void PlayFrom(double seconds)
    {
        Stop();
        int frameBytes = _format.BlockAlign;
        int start = Math.Clamp((int)(seconds * _format.SampleRate) * frameBytes, 0, Math.Max(0, _pcm.Length - frameBytes));
        if (_buffer is { } previous)
        {
            _device.DestroyBuffer(previous);
        }

        _buffer = _device.CreateBuffer(_format, _pcm.AsSpan(start));
        _voice = _device.Play(_buffer.Value, VoiceParameters.Default with { Category = SoundCategory.Effects });
        _from = seconds;
        _started = _clock;
    }

    private void Stop()
    {
        if (_voice is { } voice)
        {
            _device.StopVoice(voice);
            _voice = null;
        }
    }
}
