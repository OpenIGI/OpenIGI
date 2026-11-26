namespace OpenIGI.Frontend.Settings;

/// <summary>What the Audio page offers: the volumes first, then how the sound is placed, then the device's own settings.</summary>
/// <remarks>
/// Written out rather than gathered from the <c>snd_</c> variables by name, which put the output sample rate beside the
/// music volume under captions made from variable names. A new audio variable has to be given a row here or a line in
/// <see cref="ConsoleOnly"/>; <c>SettingsMenuCoverageTests</c> fails until it is.
/// </remarks>
public static class AudioMenu
{
    /// <summary>Gets the page's panels, in the order they are shown.</summary>
    public static IReadOnlyList<SettingsPanel> Panels { get; } =
    [
        new SettingsPanel(
            "Volume",
            [],
            [
                new SettingRow("snd_volume", "Master Volume", "How loud the whole game is.", Step: 0.05f),
                new SettingRow("snd_effects_volume", "Effects", "Weapons, footsteps, doors and impacts.", Step: 0.05f),
                new SettingRow("snd_music_volume", "Music", "The mission score and the menu theme.", Step: 0.05f),
                new SettingRow("snd_speech_volume", "Speech", "Radio messages and every other voice line.", Step: 0.05f),
                new SettingRow("snd_ambience_volume", "Ambience", "The level's background sound: wind, generators, birds.", Step: 0.05f),
                new SettingRow(
                    "snd_rain_volume",
                    "Rain",
                    "How loud the rain is against the rest of the level's background sound.",
                    Step: 0.05f) { ShownWhen = new("snd_rain") },
            ]),

        new SettingsPanel(
            "Sound",
            [],
            [
                new SettingRow(
                    "snd_3d",
                    "3D Audio",
                    "Places each sound around you -- in front, behind, above -- and lets it fade with distance as it would. Off "
                        + "pans it left and right only, as the original does."),
                new SettingRow(
                    "snd_rain",
                    "Rain Sounds",
                    "Lets you hear the weather: rain all around you outside, drumming on the roof and muffled through the walls "
                        + "inside. The original's rain makes no sound."),
                new SettingRow("snd_reverse_stereo", "Swap Left and Right", "Swaps the left and right speakers, for headphones worn the other way round."),
                new SettingRow(
                    "snd_preset",
                    "Audio Style",
                    "Original reproduces the original's mix -- 32 sounds at once, a lower rate, left-right panning only. Enhanced "
                        + "is the remaster's. Custom is what it becomes as soon as you change one of those yourself.",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["custom"] = "Custom",
                        ["vanilla"] = "Original",
                        ["enhanced"] = "Enhanced",
                    }) { Readings = ["custom"], Advanced = true },
                new SettingRow(
                    "snd_voices",
                    "Sounds at Once",
                    "How many sounds may play at the same time. The original stops at 32, which a firefight runs out of; more "
                        + "keeps every shot and shell heard.",
                    Step: 8f) { Advanced = true },
                new SettingRow(
                    "snd_rate",
                    "Output Quality",
                    "The rate the sound is mixed at, in samples a second. 48000 suits nearly every device; the original mixes "
                        + "at 22050. Takes effect the next time the game starts.",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["22050"] = "22,050 Hz (original)",
                        ["44100"] = "44,100 Hz",
                        ["48000"] = "48,000 Hz",
                        ["96000"] = "96,000 Hz",
                    }) { Advanced = true },
                new SettingRow(
                    "snd_enabled",
                    "Sound",
                    "Off runs the game silent and opens no sound device at all. Takes effect the next time the game starts.") { Advanced = true },
            ]),
    ];

    /// <summary>Gets every row on the page, in the order they are shown.</summary>
    public static IReadOnlyList<SettingRow> Rows { get; } = [.. Panels.SelectMany(panel => panel.Rows)];

    /// <summary>Gets the audio variables the page deliberately does not show, each with the reason.</summary>
    /// <remarks>Developer and hidden variables are never offered, so only the visible ones left out need a line here.</remarks>
    public static IReadOnlyList<string> ConsoleOnly { get; } = [];
}
