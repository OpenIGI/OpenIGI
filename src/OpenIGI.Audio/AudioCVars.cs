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
    /// <summary>Which named profile the audio settings correspond to.</summary>
    /// <remarks>
    /// <para>
    /// Setting this writes the profile's values over the variables it names, which is how
    /// <c>-vanilla</c> reaches the audio system: the flag emits <c>snd_preset vanilla</c> as a start-up
    /// statement, exactly as it emits <c>gfx_preset vanilla</c> for the renderer.
    /// </para>
    /// <para>
    /// It has to be set before the device opens to take full effect. The voice count and the output rate
    /// are read once, when the device is created; the panning model is read per voice and so does follow
    /// a change immediately.
    /// </para>
    /// </remarks>
    public static readonly EnumCVar<AudioProfile> Preset = CVar.Choice(
        "snd_preset",
        AudioProfile.Enhanced,
        CVarOptions.Archive,
        "Audio profile: vanilla reproduces the original's 32-voice, 22 kHz, flat-pan mix.");

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

    /// <summary>Whether the weather is heard as well as seen.</summary>
    /// <remarks>
    /// A remaster addition with no counterpart in the original, which ships no weather sound at all —
    /// its rain is a band of sprites and nothing more. Off leaves the picture exactly as it was.
    /// </remarks>
    public static readonly BoolCVar Rain = CVar.Bool(
        "snd_rain",
        true,
        CVarOptions.Archive,
        "Hear the weather: rain placed around the listener and reshaped by the building they are in.");

    /// <summary>The gain of the rain bed, over and above the ambience bus.</summary>
    /// <remarks>
    /// Separate from <see cref="AmbienceVolume"/> because they answer different questions: the bus
    /// says how loud the level's beds are against the gunfire, and this says how loud the rain is
    /// against the rest of the beds. A player who wants a quieter storm and the same generators wants
    /// this one.
    /// </remarks>
    public static readonly NumericCVar<float> RainVolume = CVar.Float(
        "snd_rain_volume",
        0.35f,
        0f,
        1f,
        CVarOptions.Archive,
        "Volume of the rain, within the ambience bus.");

    /// <summary>How much of the rain outside a wall is heard through it, as an amplitude.</summary>
    /// <remarks>
    /// <para>
    /// The floor under the whole indoor mix, and the number that decides whether stepping inside
    /// sounds like shelter or like a cellar. It has to carry the interior on its own wherever the roof
    /// is too high to drum — a warehouse, a hangar, anything with a ceiling out of reach — because
    /// there the drumming contributes nothing and this is all that is left.
    /// </para>
    /// <para>
    /// Thirteen decibels down. Genuine transmission loss through a solid wall is rather more than
    /// that, but the buildings in this game are sheds, huts and light industrial shells, and a storm
    /// you can barely hear from inside one reads as a bug rather than as insulation.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> RainWallLevel = CVar.Float(
        "snd_rain_wall",
        0.22f,
        0f,
        1f,
        CVarOptions.Developer | CVarOptions.Hidden,
        "How much rain is heard through a wall, as an amplitude. Lower shuts interiors in.");

    /// <summary>The cutoff the through-the-wall component is heard through, in hertz.</summary>
    /// <remarks>
    /// Rain's own energy peaks between one and two and a half kilohertz, so a cutoff below that takes
    /// the rain out of the rain and leaves a rumble that could be anything. Just under a kilohertz
    /// keeps enough of the peak for it to still be recognisable through the wall.
    /// </remarks>
    public static readonly NumericCVar<float> RainWallCutoffHz = CVar.Float(
        "snd_rain_wall_cutoff",
        900f,
        60f,
        24000f,
        CVarOptions.Developer | CVarOptions.Hidden,
        "The cutoff rain through a wall is heard through, in hertz. Lower is a thicker wall.");

    /// <summary>How loud the drumming of rain on a roof overhead is, as an amplitude.</summary>
    /// <remarks>
    /// Well above the walls: a roof is thin, it is struck directly by the drops, and it is the sound
    /// that makes a shed in a storm unmistakably a shed in a storm.
    /// </remarks>
    public static readonly NumericCVar<float> RainRoofLevel = CVar.Float(
        "snd_rain_roof",
        0.55f,
        0f,
        1f,
        CVarOptions.Developer | CVarOptions.Hidden,
        "How loud rain drumming on a roof overhead is, as an amplitude.");

    /// <summary>Whether the rain mix is reported to the log as it is computed.</summary>
    /// <remarks>
    /// Once a second, not once a frame. What it prints is the whole state the mix is derived from —
    /// the sky exposure, the roof, and every emitter's gain and cutoff — which is the only practical
    /// way to tell "the doorway is not being found" from "the doorway is found and too quiet".
    /// </remarks>
    public static readonly BoolCVar RainDebug = CVar.Bool(
        "snd_rain_debug",
        false,
        CVarOptions.Developer | CVarOptions.Hidden,
        "Report the rain mix — sky exposure, roof and per-direction gain and cutoff — once a second.");

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

    /// <summary>Wires the profile variable to the values it stands for.</summary>
    /// <remarks>
    /// A static constructor rather than a registry, because the audio layer may reference only
    /// <c>OpenIGI.Core</c> and the graphics preset registry lives in the renderer. The profile is not
    /// recomputed when one of its variables is changed by hand: it says what was asked for, and a
    /// player who then moves a single slider is not silently switched to "custom" mid-session.
    /// </remarks>
    static AudioCVars() => Preset.Changed += (_, _) => Apply(Preset.Value);

    /// <summary>Writes a profile's values over the variables it names.</summary>
    /// <param name="profile">The profile to apply. <see cref="AudioProfile.Custom"/> changes nothing.</param>
    public static void Apply(AudioProfile profile)
    {
        switch (profile)
        {
            case AudioProfile.Vanilla:
                // The original's own three numbers: a 32-voice pool (0x4952F0), a 22050 Hz primary
                // buffer, and no positioning -- its sample buffers carry no CTRL3D at all.
                Spatial.Value = false;
                Voices.Value = 32;
                SampleRate.Value = 22050;
                break;

            case AudioProfile.Enhanced:
                Spatial.Value = true;
                Voices.Value = 64;
                SampleRate.Value = 48000;
                break;

            default:
                break;
        }
    }
}
