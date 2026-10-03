using OpenIGI.Audio;
using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Engine.Audio;

/// <summary>
/// Plays the score: one looping streamed track at a time, with the state the game switches between.
/// </summary>
/// <remarks>
/// <para>
/// <b>Music is the game's own data, not CD audio.</b> The original does call <c>mciSendCommandA</c> and
/// does create <c>CLSID_DirectMusic</c> (<c>0x498480</c>), and the milestone plan assumed a mission's
/// score would therefore have to be replaced track by track. It does not: every mission ships
/// <c>sounds/game_music.wav</c> — a streamed ADPCM track, two and a half minutes of stereo for mission
/// one — and the menu system ships <c>menu_music</c> in its own archive. Both are ordinary ILSF sounds,
/// so both play through the same path everything else does.
/// </para>
/// <para>
/// The two state names are the original's: <c>game_music</c> (<c>0x415B30</c>) and <c>menu_music</c>
/// (<c>0x418520</c>). <c>Game_EnableMusic</c>, <c>Game_DisableMusic</c> and <c>Game_SetMusicVolume</c>
/// are the script's handles on it.
/// </para>
/// <para>
/// Crossfading is an addition. The original hard-cuts between states, so <see cref="FadeSeconds"/> is
/// zero under the vanilla profile and the cut is preserved exactly.
/// </para>
/// </remarks>
public sealed class MusicPlayer
{
    /// <summary>The track a mission plays.</summary>
    public const string GameMusic = "game_music";

    /// <summary>The track the menus play.</summary>
    public const string MenuMusic = "menu_music";

    private const string LogCategory = "Audio";

    private readonly SoundSystem _sounds;
    private readonly IOpenIgiLogger _logger;

    private AudioVoiceHandle _current;
    private AudioVoiceHandle _fading;
    private string _currentTrack = string.Empty;

    // What SHOULD be playing, which outlives what is. Stop() clears the current track, so without this
    // a disable/enable round trip would forget which state the game was in and come back silent.
    private string _desiredTrack = string.Empty;
    private float _currentGain;
    private float _fadingGain;
    private bool _enabled = true;
    private float _volume = 1f;

    /// <summary>Initializes a new instance of the <see cref="MusicPlayer"/> class.</summary>
    /// <param name="sounds">Where tracks are found and played.</param>
    /// <param name="logger">Where to report a track that is not there.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sounds"/> is <see langword="null"/>.</exception>
    public MusicPlayer(SoundSystem sounds, IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(sounds);
        _sounds = sounds;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets or sets how long a change of track takes to cross over. Zero is the original's hard cut.</summary>
    public float FadeSeconds { get; set; }

    /// <summary>Gets the track currently playing, or an empty string when nothing is.</summary>
    public string CurrentTrack => _currentTrack;

    /// <summary>Gets a value indicating whether a track is sounding.</summary>
    public bool IsPlaying => _current.IsValidHandle && _sounds.IsPlaying(_current);

    /// <summary>Gets or sets whether music plays at all.</summary>
    /// <remarks>
    /// <c>Game_EnableMusic</c> and <c>Game_DisableMusic</c>. Turning it off stops whatever is playing;
    /// turning it back on restarts the state's track from its beginning, which is what the original does
    /// — it has nowhere to remember a position.
    /// </remarks>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            if (!value)
            {
                Stop();
            }
            else
            {
                Play(_desiredTrack);
            }
        }
    }

    /// <summary>Gets or sets the music volume, zero to one.</summary>
    /// <remarks>
    /// <c>Game_SetMusicVolume</c>. Distinct from the music bus, which carries the player's own setting:
    /// this is the score's level within it, and the menu's volume slider previews through it.
    /// </remarks>
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_current.IsValidHandle)
            {
                _sounds.SetVoiceParameters(_current, Parameters(_currentGain * _volume));
            }
        }
    }

    /// <summary>Starts a track, or leaves it alone when it is already the one playing.</summary>
    /// <param name="track">The track name, such as <see cref="GameMusic"/>.</param>
    /// <returns><see langword="true"/> when a track is playing afterwards.</returns>
    public bool Play(string track)
    {
        if (string.IsNullOrEmpty(track))
        {
            return false;
        }

        _desiredTrack = track;
        if (!_enabled)
        {
            return false;
        }

        // Re-requesting the playing track is a no-op rather than a restart. The mission state asks for
        // its music every time it is entered, and restarting on each ask would stutter the score.
        if (string.Equals(_currentTrack, track, StringComparison.OrdinalIgnoreCase) && IsPlaying)
        {
            return true;
        }

        BeginFadeOut();

        AudioVoiceHandle voice = _sounds.Play(track, Parameters(FadeSeconds > 0f ? 0f : _volume));
        if (!voice.IsValidHandle)
        {
            _logger.Debug(LogCategory, $"No music track '{track}'.");
            _currentTrack = string.Empty;
            return false;
        }

        _current = voice;
        _currentTrack = track;
        _currentGain = FadeSeconds > 0f ? 0f : 1f;
        return true;
    }

    /// <summary>Stops the music.</summary>
    public void Stop()
    {
        if (_current.IsValidHandle)
        {
            _sounds.Stop(_current);
        }

        if (_fading.IsValidHandle)
        {
            _sounds.Stop(_fading);
        }

        _current = AudioVoiceHandle.None;
        _fading = AudioVoiceHandle.None;
        _currentTrack = string.Empty;
        _currentGain = 0f;
        _fadingGain = 0f;
    }

    /// <summary>Advances the crossfade.</summary>
    /// <param name="deltaSeconds">Wall-clock seconds since the last call.</param>
    /// <remarks>
    /// Driven from wall clock rather than from ticks on purpose: a fade is presentation, and tying it to
    /// the simulation would make it stutter whenever the loop catches up on ticks.
    /// </remarks>
    public void Update(float deltaSeconds)
    {
        if (FadeSeconds <= 0f || deltaSeconds <= 0f)
        {
            return;
        }

        float step = deltaSeconds / FadeSeconds;

        if (_current.IsValidHandle && _currentGain < 1f)
        {
            _currentGain = Math.Min(1f, _currentGain + step);
            _sounds.SetVoiceParameters(_current, Parameters(_currentGain * _volume));
        }

        if (_fading.IsValidHandle)
        {
            _fadingGain -= step;
            if (_fadingGain <= 0f)
            {
                _sounds.Stop(_fading);
                _fading = AudioVoiceHandle.None;
                _fadingGain = 0f;
            }
            else
            {
                _sounds.SetVoiceParameters(_fading, Parameters(_fadingGain * _volume));
            }
        }
    }

    private void BeginFadeOut()
    {
        // Only one track may be fading out. A third change before the second finished stops the oldest
        // outright rather than letting fades pile up on a pool that has other work to do.
        if (_fading.IsValidHandle)
        {
            _sounds.Stop(_fading);
        }

        if (FadeSeconds > 0f && _current.IsValidHandle)
        {
            _fading = _current;
            _fadingGain = _currentGain;
        }
        else if (_current.IsValidHandle)
        {
            _sounds.Stop(_current);
            _fading = AudioVoiceHandle.None;
        }

        _current = AudioVoiceHandle.None;
        _currentGain = 0f;
    }

    private static VoiceParameters Parameters(float gain) => VoiceParameters.Default with
    {
        Category = SoundCategory.Music,
        Gain = Math.Clamp(gain, 0f, 1f),
        Looping = true,

        // Above every effect: the pool must never decide the score is the thing to drop.
        Priority = int.MaxValue,
    };
}
