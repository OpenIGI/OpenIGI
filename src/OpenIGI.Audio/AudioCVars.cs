using OpenIGI.Core.Configuration;

namespace OpenIGI.Audio;

/// <summary>
/// The audio system's console variables.
/// </summary>
/// <remarks>
/// <para>
/// Declared next to the audio seam, the way the renderer declares its own. The console, the settings
/// file and the options menu all reach these through <see cref="CVarRegistry.Default"/>, so there is one
/// value and three views onto it. The <c>snd_</c> prefix is the one the settings screen already builds
/// its Audio page from — until this file existed, that page rendered empty.
/// </para>
/// <para>
/// The volumes here are the <em>output</em> side. They are distinct from the player profile's own sound
/// settings, which the original stores per profile and which remain the value the game's own menus
/// edit; the profile feeds these, not the other way round.
/// </para>
/// </remarks>
[CVarContainer]
public static class AudioCVars
{
    /// <summary>Whether the audio backend is opened at all.</summary>
    public static readonly BoolCVar Enabled = CVar.Bool(
        "snd_enabled",
        true,
        CVarOptions.Archive,
        "Open an audio device at startup. Off runs the game silently.");

    /// <summary>The master gain applied over every bus.</summary>
    public static readonly NumericCVar<float> MasterVolume = CVar.Float(
        "snd_volume",
        1f,
        0f,
        1f,
        CVarOptions.Archive,
        "Master volume.");

    /// <summary>The gain of the sound-effects bus.</summary>
    public static readonly NumericCVar<float> EffectsVolume = CVar.Float(
        "snd_effects_volume",
        1f,
        0f,
        1f,
        CVarOptions.Archive,
        "Volume of weapons, footsteps, doors and impacts.");

    /// <summary>The gain of the music bus.</summary>
    public static readonly NumericCVar<float> MusicVolume = CVar.Float(
        "snd_music_volume",
        0.7f,
        0f,
        1f,
        CVarOptions.Archive,
        "Volume of the mission score and the menu theme.");

    /// <summary>The gain of the speech bus.</summary>
    public static readonly NumericCVar<float> SpeechVolume = CVar.Float(
        "snd_speech_volume",
        1f,
        0f,
        1f,
        CVarOptions.Archive,
        "Volume of voice lines.");

    /// <summary>The gain of the ambience bus.</summary>
    public static readonly NumericCVar<float> AmbienceVolume = CVar.Float(
        "snd_ambience_volume",
        1f,
        0f,
        1f,
        CVarOptions.Archive,
        "Volume of the level's continuous background beds.");

    /// <summary>How many voices may sound at once.</summary>
    /// <remarks>
    /// The original's pool is 32 (<c>0x4952F0</c>), and that is what the vanilla profile uses. Raising
    /// it is the single cheapest audible improvement in a firefight, where the original simply drops
    /// whatever asks for a voice once the pool is full.
    /// </remarks>
    public static readonly NumericCVar<int> Voices = CVar.Int(
        "snd_voices",
        64,
        8,
        255,
        CVarOptions.Archive,
        "How many sounds may play at once. The original's limit is 32.");

    /// <summary>Whether the left and right output channels are swapped.</summary>
    /// <remarks>Mirrors the original's reverse-stereo profile option, global <c>0x543914</c>.</remarks>
    public static readonly BoolCVar ReverseStereo = CVar.Bool(
        "snd_reverse_stereo",
        false,
        CVarOptions.Archive,
        "Swap the left and right output channels.");

    /// <summary>Whether sounds are positioned in three dimensions rather than panned as the original did.</summary>
    /// <remarks>
    /// Off is the original's behaviour, which is not DirectSound3D at all: a linear distance fade and a
    /// lateral pan computed by hand in <c>0x496E60</c>, with no elevation, no distance cue beyond the
    /// fade, and no head model. On hands position to the backend and lets it do the work properly.
    /// </remarks>
    public static readonly BoolCVar Spatial = CVar.Bool(
        "snd_3d",
        true,
        CVarOptions.Archive,
        "Position sounds in 3D. Off reproduces the original's flat pan.");

    /// <summary>The output sample rate requested from the backend.</summary>
    /// <remarks>
    /// The original's primary buffer is 22050 Hz and everything is resampled into it. The shipped sounds
    /// are not all at that rate — some are 44100 — so the original throws away detail it shipped.
    /// </remarks>
    public static readonly NumericCVar<int> SampleRate = CVar.Int(
        "snd_rate",
        48000,
        11025,
        192000,
        CVarOptions.Archive,
        "Output sample rate. The original mixes at 22050.");
}
