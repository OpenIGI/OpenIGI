using System.Globalization;

namespace OpenIGI.Game.Config;

/// <summary>
/// One player's saved settings and progress.
/// </summary>
/// <remarks>
/// <para>
/// The original keeps these in a fixed array of sixteen 1380-byte records
/// (<c>Config_GetActiveProfile</c>, <c>0x406220</c>, is
/// <c>base + 1380 * activeIndex</c>), and <em>every</em> option is per-profile — graphics, sound and
/// input included. Two players on one machine do not share a resolution.
/// </para>
/// <para>
/// The field layout below is the record's, for anyone checking against the binary. It is modelled as
/// properties rather than a byte blob because nothing outside the save file depends on the packing,
/// and <c>config.qvm</c> — which is the save file — is keyed by name, not by offset.
/// </para>
/// </remarks>
public sealed class PlayerProfile
{
    /// <summary>The number of key bindings a profile carries.</summary>
    /// <remarks><c>Config_ResetToDefaults</c> (<c>0x405300</c>) writes 27 and copies them from <c>0x536410</c>.</remarks>
    public const int BindingCount = 27;

    /// <summary>The longest name the original stores, including the terminator.</summary>
    /// <remarks>The name field at <c>+1344</c> is 32 bytes and is filled by a bounded string read.</remarks>
    public const int NameCapacity = 32;

    /// <summary>The name the profile was created with.</summary>
    /// <remarks>Field <c>+1344</c>. Compared case-insensitively when a new profile is created.</remarks>
    public string Name { get; set; } = string.Empty;

    /// <summary>The difficulty, as one of the <c>GD_*</c> constants.</summary>
    /// <remarks>Field <c>+0</c>, and the second argument of <c>Config_CreateNewPlayerProfile</c>.</remarks>
    public int Difficulty { get; set; } = 1;

    /// <summary>The mission the player has reached.</summary>
    /// <remarks><c>GOActiveMission</c> in the save file; read by <c>Config_PlayerGetActiveMission</c>.</remarks>
    public int ActiveMission { get; set; }

    /// <summary>Whether the mouse's vertical axis is inverted.</summary>
    /// <remarks>
    /// The original stores the <em>negation</em> at <c>+1336</c>: the setter writes
    /// <c>field = (arg == 0)</c> and the getter returns <c>field == 0</c>. Both negate, so the value a
    /// script sees round-trips; only the stored sense is inverted, and nothing outside those two
    /// functions reads the field.
    /// </remarks>
    public bool InvertMouse { get; set; }

    /// <summary>The mouse sensitivity, 0 to 1.</summary>
    /// <remarks>Defaults to <c>0.4f</c> — <c>0x3ECCCCCD</c> in <c>Config_ResetToDefaults</c>.</remarks>
    public float MouseSensitivity { get; set; } = 0.4f;

    /// <summary>Whether blood is drawn.</summary>
    /// <remarks><c>GOIsBlood</c>. The German build forces this off; see <see cref="GameConfig.IsGermany"/>.</remarks>
    public bool BloodEnabled { get; set; } = true;

    /// <summary>The display mode index the profile last used.</summary>
    /// <remarks><c>GOGfxDisp</c>. Reconciled against the modes the hardware actually offers on load.</remarks>
    public int DisplayMode { get; set; }

    /// <summary>The render device's GUID, as the original writes it.</summary>
    /// <remarks>
    /// <c>GOGfxDevice</c>. A retail save holds
    /// <c>{00000000-0000-0000-0000-000000000000}</c> for the default device.
    /// </remarks>
    public string DeviceGuid { get; set; } = DefaultDeviceGuid;

    /// <summary>The device GUID meaning "whichever device is first".</summary>
    public const string DefaultDeviceGuid = "{00000000-0000-0000-0000-000000000000}";

    /// <summary>The gamma correction, 0 to 1.</summary>
    /// <remarks>
    /// <c>GOGfxGamma</c>. The in-game slider writes <c>this.vValue + 0.5</c>, so the stored range is
    /// offset from what the control shows.
    /// </remarks>
    public float Gamma { get; set; }

    /// <summary>The graphics performance flags.</summary>
    /// <remarks>
    /// <c>GOGfxPerformance</c>. A bitmask, not a level — see
    /// <see cref="GraphicsPerformance"/> for the level table and the nearest-match rule that maps
    /// between them.
    /// </remarks>
    public int PerformanceFlags { get; set; }

    /// <summary>Whether transparency effects are drawn.</summary>
    public bool Transparency { get; set; } = true;

    /// <summary>The language, as one of the six language constants.</summary>
    /// <remarks><c>GOGameLang</c>.</remarks>
    public int Language { get; set; }

    /// <summary>Whether sound effects are enabled.</summary>
    public bool SoundEffects { get; set; } = true;

    /// <summary>Whether music is enabled.</summary>
    public bool Music { get; set; } = true;

    /// <summary>Whether speech is enabled.</summary>
    public bool Speech { get; set; } = true;

    /// <summary>Whether the stereo image is reversed.</summary>
    public bool ReverseStereo { get; set; }

    /// <summary>The sound-effects volume, 0 to 1.</summary>
    public float SoundEffectsVolume { get; set; } = 1.0f;

    /// <summary>The music volume, 0 to 1.</summary>
    public float MusicVolume { get; set; } = 1.0f;

    /// <summary>The speech volume, 0 to 1.</summary>
    public float SpeechVolume { get; set; } = 1.0f;

    /// <summary>The key bindings, in the order the save file lists them.</summary>
    /// <remarks>
    /// Each is an action name, an input device and a key — the three arguments of <c>GOInRemap</c>.
    /// The order is the file's; nothing indexes them positionally.
    /// </remarks>
    public List<InputBinding> Bindings { get; } = [];

    /// <summary>Makes an independent copy.</summary>
    /// <returns>The copy.</returns>
    /// <remarks>
    /// <c>Config_CreateNewPlayerProfile</c> (<c>0x4050A0</c>) copies the <em>active</em> profile's
    /// record into the new slot before overwriting the name and difficulty, so a new player inherits
    /// whoever was playing last's settings and key bindings rather than the defaults.
    /// </remarks>
    public PlayerProfile Clone()
    {
        PlayerProfile copy = new()
        {
            Name = Name,
            Difficulty = Difficulty,
            ActiveMission = ActiveMission,
            InvertMouse = InvertMouse,
            MouseSensitivity = MouseSensitivity,
            BloodEnabled = BloodEnabled,
            DisplayMode = DisplayMode,
            DeviceGuid = DeviceGuid,
            Gamma = Gamma,
            PerformanceFlags = PerformanceFlags,
            Transparency = Transparency,
            Language = Language,
            SoundEffects = SoundEffects,
            Music = Music,
            Speech = Speech,
            ReverseStereo = ReverseStereo,
            SoundEffectsVolume = SoundEffectsVolume,
            MusicVolume = MusicVolume,
            SpeechVolume = SpeechVolume,
        };

        copy.Bindings.AddRange(Bindings);
        return copy;
    }

    /// <inheritdoc/>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Name} (difficulty {Difficulty}, mission {ActiveMission})");
}

/// <summary>One key binding.</summary>
/// <param name="Action">The action name, such as <c>MoveLeft</c>.</param>
/// <param name="Device">The input device constant, such as <c>INPUTPORT_DEVICE_KEYBOARD</c>.</param>
/// <param name="Key">The key or button constant, such as <c>KEY_LEFT</c>.</param>
/// <remarks>
/// These are stored by <em>name</em> in the save file rather than by value, which is what makes a
/// saved binding survive a change to the engine's key numbering.
/// </remarks>
public readonly record struct InputBinding(string Action, string Device, string Key);
