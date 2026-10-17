using System.Globalization;
using System.Text;
using OpenIGI.Audio;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Audio;
using OpenIGI.Game.World;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Host.Audio;

/// <summary>
/// The weather, heard: six taps on one rain recording, placed around the listener's head and mixed
/// from what <see cref="SkyExposureProbe"/> measures about the building they are standing in.
/// </summary>
/// <remarks>
/// <para>
/// <b>The idea.</b> Rain is not a sound source, it is a field — it arrives from every direction at
/// once, and what a building does to it is not to turn it down but to reshape it. Standing in a yard
/// you are inside the field. Step into a shed and the field is replaced by three quite different
/// sounds arriving from three different places: the drumming of the drops on the roof over your head,
/// the dull broadband push of the same rain through the walls, and — if a door is open — the rain
/// itself, unfiltered, from exactly where the door is. Cross-fading a stereo loop against a quieter
/// stereo loop reproduces none of that, which is why an interior in most games sounds like an
/// exterior with the fader down.
/// </para>
/// <para>
/// <b>What is built instead.</b> One emitter on each of the six world axes, three metres out, moving
/// with the listener. The probe reports how open each of those six directions is; that number decides
/// both how loud that emitter is and how bright, and the three components above fall out of it
/// without being special-cased:
/// </para>
/// <list type="bullet">
/// <item><description>
/// the part of a direction that is open carries the recording untouched, at an amplitude equal to the
/// square root of how open it is — because open-ness is a fraction of the field's <i>power</i>, and
/// amplitude is the square root of power. A doorway subtends a large fraction of the sphere when you
/// are standing in it and a tiny one from the back of the room, so a door gets louder as you approach
/// it with no distance term written anywhere;
/// </description></item>
/// <item><description>
/// the part that is closed still carries the same recording, thirteen decibels down and through a
/// nine-hundred hertz roof — a shed or a light industrial shell rather than a bunker, and loud enough
/// that stepping inside reads as shelter rather than as a cellar;
/// </description></item>
/// <item><description>
/// the upward emitter carries, in addition, the drumming — louder than the walls and brighter,
/// scaled by how closely the roof stands over the ear.
/// </description></item>
/// </list>
/// <para>
/// The three are summed as powers, not amplitudes, because they are uncorrelated; the emitter's
/// cutoff is their power-weighted mean in log frequency, so an emitter that is mostly doorway sounds
/// like a doorway and one that is mostly wall sounds like a wall. Nothing in here tests "am I
/// indoors": there is no such flag, and an awning, a doorway, a courtyard and a cellar are all just
/// different numbers coming out of the same six.
/// </para>
/// <para>
/// <b>Why six emitters and not a stereo bed.</b> Because the request the whole design answers is
/// "one ear at the open door and the other not", and only a real position can do that. The emitters
/// are placed in world space around the ear, so turning the head swings the door across the stereo
/// image exactly as it should, and under <c>snd_3d</c> the backend's own panning — head-related, on a
/// device that offers it — does the work rather than a hand-written pan.
/// </para>
/// </remarks>
internal sealed class RainAmbience : IDisposable
{
    /// <summary>How far from the ear the emitters stand, in metres.</summary>
    /// <remarks>
    /// Close, because these are directions rather than objects: the loudness is already decided by
    /// the mix and must not be decided a second time by distance. Standing inside
    /// <see cref="EmitterMinimumDistanceMetres"/> is what keeps the device's linear fade at unity.
    /// </remarks>
    private const float EmitterRadiusMetres = 3f;

    /// <summary>The distance within which the device applies no fade.</summary>
    private const float EmitterMinimumDistanceMetres = 4f;

    /// <summary>The distance at which the device would silence the emitter, never reached.</summary>
    private const float EmitterMaximumDistanceMetres = 60f;

    /// <summary>The cutoff the drumming is heard through, in hertz.</summary>
    private const float PatterCutoffHz = 3500f;

    /// <summary>How long the whole bed takes to fade in or out when the weather starts or stops.</summary>
    private const float MasterFadeSeconds = 2.5f;

    /// <summary>Which entry of <see cref="SkyExposureProbe.Directions"/> points at the sky.</summary>
    private const int UpAxis = 4;

    /// <summary>How importantly the pool treats these voices when it is full.</summary>
    /// <remarks>
    /// Above the crowd of distant gunfire and below anything the player did. A continuous bed that
    /// vanishes mid-firefight is far more noticeable than one more rifle crack that never sounds, so
    /// the bed must not be the thing that gets stolen.
    /// </remarks>
    private const int VoicePriority = 180;

    private readonly SoundSystem _sounds;
    private readonly IOpenIgiLogger _logger;
    private readonly SkyExposureProbe _probe = new();
    private readonly AudioVoiceHandle[] _voices;
    private readonly FilteredLoopSource?[] _taps;
    private readonly float[] _gains;

    private short[] _pcm = [];
    private int _sampleRate;
    private float _masterGain;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="RainAmbience"/> class.</summary>
    /// <param name="sounds">The sound system whose device the taps stream on.</param>
    /// <param name="logger">Where to report what was loaded and what could not be.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sounds"/> is <see langword="null"/>.</exception>
    public RainAmbience(SoundSystem sounds, IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(sounds);

        _sounds = sounds;
        _logger = logger ?? NullLogger.Instance;
        _voices = new AudioVoiceHandle[SkyExposureProbe.Directions.Length];
        _taps = new FilteredLoopSource?[SkyExposureProbe.Directions.Length];
        _gains = new float[SkyExposureProbe.Directions.Length];
    }

    /// <summary>Gets a value indicating whether a rain recording was found and is ready to play.</summary>
    public bool IsLoaded => _pcm.Length > 0;

    /// <summary>Gets a value indicating whether any tap is currently sounding.</summary>
    public bool IsSounding => _masterGain > 0f;

    /// <summary>Gets how much of the sky reached the listener at the last update, zero to one.</summary>
    public float Exposure => _probe.Exposure;

    /// <summary>Gets how closely a roof stood over the listener at the last update, zero to one.</summary>
    public float RoofNearness => _probe.RoofNearness;

    /// <summary>Loads the rain recording that ships beside the game.</summary>
    /// <param name="path">The WAV to read.</param>
    /// <returns><see langword="true"/> when the file was found and is PCM this can loop.</returns>
    /// <remarks>
    /// Mono is required, not preferred: only a mono source is placed in three dimensions, and a stereo
    /// buffer would play flat on the listener's head — which is the one thing this whole class exists
    /// to avoid. The width comes back from the six emitters, each reading a different stretch of the
    /// same recording.
    /// </remarks>
    public bool TryLoad(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!File.Exists(path))
        {
            _logger.Information("Audio", $"No rain recording at '{path}'; the weather stays silent.");
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (IOException error)
        {
            _logger.Warning("Audio", $"Rain recording would not read: {error.Message}");
            return false;
        }

        if (!WavPcm.TryRead(bytes, out AudioFormat format, out byte[] pcm))
        {
            _logger.Warning("Audio", $"'{path}' is not an uncompressed PCM WAV; the weather stays silent.");
            return false;
        }

        if (format.Channels != 1 || format.BitsPerSample != 16)
        {
            _logger.Warning(
                "Audio",
                $"The rain recording must be mono 16-bit to be placed in 3D; '{path}' is "
                + $"{format.Channels} ch, {format.BitsPerSample}-bit.");
            return false;
        }

        _pcm = new short[pcm.Length / 2];
        Buffer.BlockCopy(pcm, 0, _pcm, 0, _pcm.Length * 2);
        _sampleRate = format.SampleRate;

        _logger.Information(
            "Audio",
            $"Rain recording: {_pcm.Length / (float)format.SampleRate:0.0} s, {format.SampleRate} Hz mono.");
        return true;
    }

    /// <summary>Measures the listener's surroundings and mixes the six emitters to match.</summary>
    /// <param name="earMetres">Where the listener's ear is, in world metres.</param>
    /// <param name="shelter">The level's roof raster.</param>
    /// <param name="cast">The collision the probe's rays are tested against, or <see langword="null"/>.</param>
    /// <param name="unitsPerMetre">What <paramref name="cast"/> measures in.</param>
    /// <param name="intensity">How hard it is raining, zero for not at all.</param>
    /// <param name="volume">The player's own rain volume, zero to one.</param>
    /// <param name="deltaSeconds">How long since the last update.</param>
    /// <exception cref="ArgumentNullException"><paramref name="shelter"/> is <see langword="null"/>.</exception>
    public void Update(
        Vector3d earMetres,
        ShelterHeightField shelter,
        WorldRayCast? cast,
        double unitsPerMetre,
        float intensity,
        float volume,
        float deltaSeconds)
    {
        ArgumentNullException.ThrowIfNull(shelter);

        if (!IsLoaded || !_sounds.Device.IsAvailable)
        {
            return;
        }

        // The fade is the only thing that runs while the weather is off, so that stopping the rain is
        // a rain shower ending rather than a switch being thrown.
        float wanted = Math.Clamp(volume, 0f, 1f) * Math.Clamp(intensity, 0f, 1f);
        float step = deltaSeconds <= 0f ? 1f : 1f - MathF.Exp(-deltaSeconds / MasterFadeSeconds);
        _masterGain += (wanted - _masterGain) * step;

        if (_masterGain <= 0.001f && wanted <= 0f)
        {
            // Faded out. Stop once and then do nothing at all: a dry level must not pay for a probe,
            // and every mission but three of the fourteen is dry.
            if (_masterGain > 0f || _voices[0].IsValidHandle)
            {
                Stop();
            }

            _masterGain = 0f;
            return;
        }

        _probe.Update(earMetres, shelter, cast, unitsPerMetre, deltaSeconds);

        ReadOnlySpan<Vector3f> axes = SkyExposureProbe.Directions;
        ReadOnlySpan<float> open = _probe.DirectionalExposure;
        float roof = _probe.RoofNearness;

        // Read once a frame rather than per emitter, so a console tweak lands on all six together.
        float wallLevel = AudioCVars.RainWallLevel.Value;
        float wallCutoff = AudioCVars.RainWallCutoffHz.Value;
        float patterLevel = AudioCVars.RainRoofLevel.Value;

        for (int axis = 0; axis < axes.Length; axis++)
        {
            float direct = Math.Clamp(open[axis], 0f, 1f);
            float closed = 1f - direct;

            // Three uncorrelated arrivals, summed as powers because that is how uncorrelated signals
            // sum. Only the sky-facing emitter carries the drumming, and only to the extent that the
            // sky above it is actually blocked by the roof doing the drumming.
            float wallPower = wallLevel * wallLevel * closed;
            float patterPower = axis == UpAxis
                ? patterLevel * roof * patterLevel * roof * closed
                : 0f;
            float power = direct + wallPower + patterPower;

            _gains[axis] = _masterGain * MathF.Sqrt(power);
            float cutoff = MeanCutoff(direct, wallPower, patterPower, power, wallCutoff);

            Vector3d position = earMetres
                + (new Vector3d(axes[axis].X, axes[axis].Y, axes[axis].Z) * EmitterRadiusMetres);

            EnsureVoice(axis, cutoff, position);
            if (_taps[axis] is { } tap)
            {
                tap.CutoffHz = cutoff;
            }

            Place(axis, position);
        }
    }

    /// <summary>Silences every emitter and gives its voice back to the pool.</summary>
    /// <remarks>
    /// Called on mission teardown and whenever the weather has finished fading out. The device
    /// disposes each tap as it retires the voice, so nothing is left holding the recording.
    /// </remarks>
    public void Stop()
    {
        for (int axis = 0; axis < _voices.Length; axis++)
        {
            if (_voices[axis].IsValidHandle)
            {
                _sounds.Stop(_voices[axis]);
            }

            _voices[axis] = AudioVoiceHandle.None;
            _taps[axis] = null;
            _gains[axis] = 0f;
        }

        _masterGain = 0f;
        _probe.Reset();
    }

    /// <summary>Describes the current mix, for the console.</summary>
    /// <returns>One line naming the exposure, the roof and every emitter's gain and cutoff.</returns>
    public string Describe()
    {
        StringBuilder text = new();
        text.Append(CultureInfo.InvariantCulture, $"exposure {_probe.Exposure:0.00}");
        text.Append(CultureInfo.InvariantCulture, $" roof {_probe.RoofNearness:0.00}");
        text.Append(CultureInfo.InvariantCulture, $" master {_masterGain:0.00}");

        ReadOnlySpan<string> names = ["+X", "-X", "+Y", "-Y", "up", "down"];
        for (int axis = 0; axis < _gains.Length; axis++)
        {
            float cutoff = _taps[axis]?.CutoffHz ?? 0f;
            text.Append(CultureInfo.InvariantCulture, $" | {names[axis]} {_gains[axis]:0.00}@{cutoff:0} Hz");
        }

        return text.ToString();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    /// <summary>The power-weighted mean of the three components' cutoffs, in log frequency.</summary>
    /// <remarks>
    /// Logarithmic because frequency is heard that way: the midpoint between four hundred hertz and
    /// twenty-four kilohertz is two and a half kilohertz, not twelve. An arithmetic mean would make
    /// any emitter with a trace of open sky in it sound wide open.
    /// </remarks>
    private static float MeanCutoff(
        float direct,
        float wallPower,
        float patterPower,
        float power,
        float wallCutoffHz)
    {
        if (power <= 0f)
        {
            return wallCutoffHz;
        }

        float weighted = (direct * MathF.Log(FilteredLoopSource.OpenCutoffHz))
            + (wallPower * MathF.Log(wallCutoffHz))
            + (patterPower * MathF.Log(PatterCutoffHz));

        return MathF.Exp(weighted / power);
    }

    /// <summary>Starts an emitter's voice if it has none, or has lost the one it had.</summary>
    /// <remarks>
    /// A voice can go away underneath us — the pool can steal it, and a device that was reopened has
    /// none of the old ones — so this is checked every frame rather than only at the start. The tap is
    /// created with its cutoff already set, because a tap that starts open and closes over its first
    /// fifth of a second is audible as a swoop every time one is replaced.
    /// </remarks>
    private void EnsureVoice(int axis, float cutoff, Vector3d positionMetres)
    {
        if (_voices[axis].IsValidHandle && _sounds.IsPlaying(_voices[axis]))
        {
            return;
        }

        // Each emitter reads a different stretch of the same recording. Six copies of one waveform
        // would sum coherently and collapse the field to a point; six different stretches of rain sum
        // the way six directions of real rain do.
        int start = (int)((long)_pcm.Length * axis / _voices.Length);
        FilteredLoopSource tap = new(_pcm, _sampleRate, start, cutoff);

        AudioVoiceHandle voice = _sounds.Device.PlayStream(tap, Parameters(axis, positionMetres));
        if (!voice.IsValidHandle)
        {
            // PlayStream disposed the tap on its way out; try again next frame rather than spinning.
            _taps[axis] = null;
            return;
        }

        _voices[axis] = voice;
        _taps[axis] = tap;
    }

    /// <summary>Moves an emitter and re-levels it.</summary>
    private void Place(int axis, Vector3d positionMetres)
    {
        if (_voices[axis].IsValidHandle)
        {
            _sounds.SetVoiceParameters(_voices[axis], Parameters(axis, positionMetres));
        }
    }

    /// <summary>The voice parameters for one emitter.</summary>
    private VoiceParameters Parameters(int axis, Vector3d positionMetres) => new()
    {
        Category = SoundCategory.Ambience,
        Gain = Math.Clamp(_gains[axis], 0f, 1f),
        Pitch = 1f,
        Looping = true,
        Priority = VoicePriority,
        IsPositional = true,
        Position = positionMetres,
        MinimumDistance = EmitterMinimumDistanceMetres,
        MaximumDistance = EmitterMaximumDistanceMetres,
    };
}
