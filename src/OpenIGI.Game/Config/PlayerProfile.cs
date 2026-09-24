using System.Globalization;
using OpenIGI.Input;

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

    /// <summary>The bindings a fresh profile starts with.</summary>
    /// <remarks>
    /// The port keeps the retail action set but uses a conflict-free modern movement layout. They must
    /// live in the model rather than the Controls screen: a profile created before that page is opened
    /// still has to serialize to a complete configuration and provide usable input to the game.
    /// </remarks>
    public static IReadOnlyList<InputBinding> DefaultBindings { get; } =
    [
        new("MoveLeft", "INPUTPORT_DEVICE_KEYBOARD", "KEY_A"),
        new("MoveRight", "INPUTPORT_DEVICE_KEYBOARD", "KEY_D"),
        new("MoveUp", "INPUTPORT_DEVICE_KEYBOARD", "KEY_W"),
        new("MoveDown", "INPUTPORT_DEVICE_KEYBOARD", "KEY_S"),
        new("Fire", "INPUTPORT_DEVICE_MOUSE", "MOUSE_BUTTON_1"),
        new("Reload", "INPUTPORT_DEVICE_KEYBOARD", "KEY_RETURN"),
        new("Activate", "INPUTPORT_DEVICE_KEYBOARD", "KEY_E"),
        new("Crouch", "INPUTPORT_DEVICE_KEYBOARD", "KEY_RIGHT_CTRL"),
        new("Jump", "INPUTPORT_DEVICE_KEYBOARD", "KEY_SPACE"),
        new("NextWeapon", "INPUTPORT_DEVICE_MOUSE", "MOUSE_BUTTON_2"),
        new("PrevWeapon", "INPUTPORT_DEVICE_KEYBOARD", "KEY_O"),
        new("AlternateFire", "INPUTPORT_DEVICE_KEYBOARD", "KEY_BACKSPACE"),
        new("Peek", "INPUTPORT_DEVICE_KEYBOARD", "KEY_NUMPAD1"),
        new("Binoculars", "INPUTPORT_DEVICE_KEYBOARD", "KEY_B"),
        new("MapComputer", "INPUTPORT_DEVICE_KEYBOARD", "KEY_C"),
        new("ToggleWalkRun", "INPUTPORT_DEVICE_KEYBOARD", "KEY_LEFT_SHIFT"),
        new("ZoomIn", "INPUTPORT_DEVICE_KEYBOARD", "KEY_PAGEUP"),
        new("ZoomOut", "INPUTPORT_DEVICE_KEYBOARD", "KEY_PAGEDOWN"),
        new("WeaponCategory1", "INPUTPORT_DEVICE_KEYBOARD", "KEY_1"),
        new("WeaponCategory2", "INPUTPORT_DEVICE_KEYBOARD", "KEY_2"),
        new("WeaponCategory3", "INPUTPORT_DEVICE_KEYBOARD", "KEY_3"),
        new("WeaponCategory4", "INPUTPORT_DEVICE_KEYBOARD", "KEY_4"),
        new("WeaponCategory5", "INPUTPORT_DEVICE_KEYBOARD", "KEY_5"),
        new("WeaponCategory6", "INPUTPORT_DEVICE_KEYBOARD", "KEY_6"),
        new("WeaponCategory7", "INPUTPORT_DEVICE_KEYBOARD", "KEY_7"),
        new("WeaponCategory8", "INPUTPORT_DEVICE_KEYBOARD", "KEY_8"),
        new("WeaponCategory9", "INPUTPORT_DEVICE_KEYBOARD", "KEY_9"),
    ];

    /// <summary>The bindings the original's own fresh profile starts with.</summary>
    /// <remarks>
    /// The 27 rows <c>Config_ResetToDefaults</c> (<c>0x405300</c>) copies from <c>0x536410</c>, read from
    /// the binary: DirectInput codes <c>0xCB</c>, <c>0xCD</c>, <c>0xC8</c> and <c>0xD0</c> are the arrow
    /// keys, jump is Numpad 0, activate the right Shift, walk/run W. The retail <c>config.qvm</c> an
    /// installation ships lists exactly these. Kept to recognise a profile nobody ever rebound, which is
    /// what <see cref="HasRetailDefaultControls"/> is for; nothing is given them any more.
    /// </remarks>
    public static IReadOnlyList<InputBinding> RetailDefaultBindings { get; } =
    [
        new("MoveLeft", "INPUTPORT_DEVICE_KEYBOARD", "KEY_LEFT"),
        new("MoveRight", "INPUTPORT_DEVICE_KEYBOARD", "KEY_RIGHT"),
        new("MoveUp", "INPUTPORT_DEVICE_KEYBOARD", "KEY_UP"),
        new("MoveDown", "INPUTPORT_DEVICE_KEYBOARD", "KEY_DOWN"),
        new("Fire", "INPUTPORT_DEVICE_MOUSE", "MOUSE_BUTTON_1"),
        new("Reload", "INPUTPORT_DEVICE_KEYBOARD", "KEY_RETURN"),
        new("Activate", "INPUTPORT_DEVICE_KEYBOARD", "KEY_RIGHT_SHIFT"),
        new("Crouch", "INPUTPORT_DEVICE_KEYBOARD", "KEY_RIGHT_CTRL"),
        new("Jump", "INPUTPORT_DEVICE_KEYBOARD", "KEY_NUMPAD0"),
        new("NextWeapon", "INPUTPORT_DEVICE_MOUSE", "MOUSE_BUTTON_2"),
        new("PrevWeapon", "INPUTPORT_DEVICE_KEYBOARD", "KEY_O"),
        new("AlternateFire", "INPUTPORT_DEVICE_KEYBOARD", "KEY_BACKSPACE"),
        new("Peek", "INPUTPORT_DEVICE_KEYBOARD", "KEY_NUMPAD1"),
        new("Binoculars", "INPUTPORT_DEVICE_KEYBOARD", "KEY_SPACE"),
        new("MapComputer", "INPUTPORT_DEVICE_KEYBOARD", "KEY_C"),
        new("ToggleWalkRun", "INPUTPORT_DEVICE_KEYBOARD", "KEY_W"),
        new("ZoomIn", "INPUTPORT_DEVICE_KEYBOARD", "KEY_PAGEUP"),
        new("ZoomOut", "INPUTPORT_DEVICE_KEYBOARD", "KEY_PAGEDOWN"),
        new("WeaponCategory1", "INPUTPORT_DEVICE_KEYBOARD", "KEY_1"),
        new("WeaponCategory2", "INPUTPORT_DEVICE_KEYBOARD", "KEY_2"),
        new("WeaponCategory3", "INPUTPORT_DEVICE_KEYBOARD", "KEY_3"),
        new("WeaponCategory4", "INPUTPORT_DEVICE_KEYBOARD", "KEY_4"),
        new("WeaponCategory5", "INPUTPORT_DEVICE_KEYBOARD", "KEY_5"),
        new("WeaponCategory6", "INPUTPORT_DEVICE_KEYBOARD", "KEY_6"),
        new("WeaponCategory7", "INPUTPORT_DEVICE_KEYBOARD", "KEY_7"),
        new("WeaponCategory8", "INPUTPORT_DEVICE_KEYBOARD", "KEY_8"),
        new("WeaponCategory9", "INPUTPORT_DEVICE_KEYBOARD", "KEY_9"),
    ];

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
    public int ActiveMission { get; set; } = 1;

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

    /// <summary>Creates a profile with the retail input defaults.</summary>
    public static PlayerProfile CreateDefault()
    {
        PlayerProfile profile = new();
        profile.Bindings.AddRange(DefaultBindings);
        return profile;
    }

    /// <summary>Restores every supported action to its current default assignment.</summary>
    /// <remarks>Unknown extension actions are preserved because this model cannot define their defaults.</remarks>
    public void ResetBindingsToDefaults()
    {
        foreach (InputBinding binding in DefaultBindings)
        {
            int index = Bindings.FindIndex(candidate =>
                string.Equals(candidate.Action, binding.Action, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                Bindings[index] = binding;
            }
            else
            {
                Bindings.Add(binding);
            }
        }
    }

    /// <summary>Whether every original action is still on the key the original gave it.</summary>
    /// <returns><see langword="true"/> for a profile nobody rebound; extra actions are not counted.</returns>
    public bool HasRetailDefaultControls() =>
        RetailDefaultBindings.All(retail => Bindings.Exists(binding =>
            string.Equals(binding.Action, retail.Action, StringComparison.OrdinalIgnoreCase)
            && string.Equals(binding.Device, retail.Device, StringComparison.OrdinalIgnoreCase)
            && string.Equals(binding.Key, retail.Key, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Moves a profile onto the modern controls: the WASD layout, and the mouse not inverted.</summary>
    public void AdoptModernControls()
    {
        ResetBindingsToDefaults();
        InvertMouse = false;
    }

    /// <summary>Restores retail bindings missing from an older OpenIGI profile.</summary>
    /// <remarks>
    /// Early shell builds could serialize an incomplete binding list. A retail profile always carries one
    /// row for every supported action, so missing actions are repaired while existing and extension rows
    /// are left exactly as the player saved them.
    /// </remarks>
    public void RestoreMissingDefaultBindings()
    {
        var actions = new HashSet<string>(Bindings.Select(binding => binding.Action), StringComparer.OrdinalIgnoreCase);
        foreach (InputBinding binding in DefaultBindings)
        {
            if (actions.Add(binding.Action))
            {
                Bindings.Add(binding);
            }
        }
    }

    /// <summary>Assigns a keyboard key to one action, swapping any conflicting assignment.</summary>
    /// <param name="index">The binding row to change.</param>
    /// <param name="key">The new key.</param>
    /// <returns>The row that received the old assignment, or <c>-1</c> when there was no conflict.</returns>
    /// <remarks>
    /// A control must not silently drive two actions. Swapping preserves a complete, usable binding set
    /// and also lets a keyboard assignment trade places with one of the retail mouse-button defaults.
    /// </remarks>
    public int RebindKeyboard(int index, KeyCode key)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Bindings.Count);

        InputBinding previous = Bindings[index];
        int conflict = -1;
        for (int i = 0; i < Bindings.Count; i++)
        {
            InputBinding candidate = Bindings[i];
            if (i != index
                && string.Equals(candidate.Device, InputBindingKeyNames.KeyboardDevice, StringComparison.OrdinalIgnoreCase)
                && InputBindingKeyNames.TryParseKeyboardKey(candidate.Key, out KeyCode candidateKey)
                && candidateKey == key)
            {
                conflict = i;
                break;
            }
        }

        Bindings[index] = previous with
        {
            Device = InputBindingKeyNames.KeyboardDevice,
            Key = InputBindingKeyNames.ToRetailKeyboardKey(key),
        };

        if (conflict >= 0)
        {
            Bindings[conflict] = Bindings[conflict] with
            {
                Device = previous.Device,
                Key = previous.Key,
            };
        }

        return conflict;
    }

    /// <summary>Assigns a mouse button to one action, swapping any conflicting assignment.</summary>
    /// <param name="index">The binding row to change.</param>
    /// <param name="button">The new mouse button.</param>
    /// <returns>The row that received the old assignment, or <c>-1</c> when there was no conflict.</returns>
    public int RebindMouseButton(int index, MouseButton button)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Bindings.Count);

        InputBinding previous = Bindings[index];
        int conflict = -1;
        for (int i = 0; i < Bindings.Count; i++)
        {
            InputBinding candidate = Bindings[i];
            if (i != index
                && string.Equals(candidate.Device, InputBindingKeyNames.MouseDevice, StringComparison.OrdinalIgnoreCase)
                && InputBindingKeyNames.TryParseMouseButton(candidate.Key, out MouseButton candidateButton)
                && candidateButton == button)
            {
                conflict = i;
                break;
            }
        }

        Bindings[index] = previous with
        {
            Device = InputBindingKeyNames.MouseDevice,
            Key = InputBindingKeyNames.ToRetailMouseButton(button),
        };

        if (conflict >= 0)
        {
            Bindings[conflict] = Bindings[conflict] with
            {
                Device = previous.Device,
                Key = previous.Key,
            };
        }

        return conflict;
    }

    /// <summary>Assigns a mouse-wheel direction to one action, swapping any conflicting assignment.</summary>
    /// <param name="index">The binding row to change.</param>
    /// <param name="direction">Positive scrolls up, negative scrolls down.</param>
    /// <returns>The row that received the old assignment, or <c>-1</c> when there was no conflict.</returns>
    /// <remarks>Up and down are distinct controls, like separate buttons: only the same direction conflicts.</remarks>
    public int RebindMouseWheel(int index, int direction)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Bindings.Count);

        direction = direction > 0 ? 1 : -1;
        InputBinding previous = Bindings[index];
        int conflict = -1;
        for (int i = 0; i < Bindings.Count; i++)
        {
            InputBinding candidate = Bindings[i];
            if (i != index
                && string.Equals(candidate.Device, InputBindingKeyNames.MouseDevice, StringComparison.OrdinalIgnoreCase)
                && InputBindingKeyNames.TryParseMouseWheel(candidate.Key, out int candidateDirection)
                && candidateDirection == direction)
            {
                conflict = i;
                break;
            }
        }

        Bindings[index] = previous with
        {
            Device = InputBindingKeyNames.MouseDevice,
            Key = InputBindingKeyNames.ToRetailMouseWheel(direction),
        };

        if (conflict >= 0)
        {
            Bindings[conflict] = Bindings[conflict] with
            {
                Device = previous.Device,
                Key = previous.Key,
            };
        }

        return conflict;
    }

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

/// <summary>Converts the retail config's input-constant names to the port's input names and back.</summary>
/// <remarks>
/// The original config stores symbols such as <c>KEY_RIGHT_CTRL</c> and <c>MOUSE_BUTTON_1</c>, while
/// the input layer deliberately uses descriptive enum names such as <see cref="KeyCode.RightControl"/>.
/// Translating at the config boundary keeps the file byte-compatible without leaking its spelling into UI.
/// </remarks>
public static class InputBindingKeyNames
{
    /// <summary>The original symbol for the keyboard input device.</summary>
    public const string KeyboardDevice = "INPUTPORT_DEVICE_KEYBOARD";

    /// <summary>The original symbol for the mouse input device.</summary>
    public const string MouseDevice = "INPUTPORT_DEVICE_MOUSE";

    /// <summary>Tries to turn a stored mouse-button name into the input enum.</summary>
    public static bool TryParseMouseButton(string? storedName, out MouseButton button)
    {
        button = default;
        if (string.IsNullOrWhiteSpace(storedName)
            || !storedName.StartsWith("MOUSE_BUTTON_", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(storedName["MOUSE_BUTTON_".Length..], out int number)
            || number < 1
            || number > MouseState.ButtonCount)
        {
            return false;
        }

        button = (MouseButton)(number - 1);
        return true;
    }

    /// <summary>Returns the original config symbol for a mouse button.</summary>
    public static string ToRetailMouseButton(MouseButton button)
    {
        int index = (int)button;
        ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(button));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, MouseState.ButtonCount, nameof(button));
        return $"MOUSE_BUTTON_{index + 1}";
    }

    /// <summary>The stored name for an upward mouse-wheel notch.</summary>
    /// <remarks>
    /// The port's own spelling: retail never bound the wheel, so there is no original symbol to
    /// keep. It travels through the same <c>GOInRemap</c> write/read path as <c>MOUSE_BUTTON_n</c>,
    /// and older builds read it back as an inert string they preserve verbatim.
    /// </remarks>
    public const string MouseWheelUpKey = "MOUSE_WHEEL_UP";

    /// <summary>The stored name for a downward mouse-wheel notch.</summary>
    public const string MouseWheelDownKey = "MOUSE_WHEEL_DOWN";

    /// <summary>Tries to turn a stored mouse-wheel name into a scroll direction.</summary>
    /// <param name="storedName">The persisted key name.</param>
    /// <param name="direction">Receives +1 for up, -1 for down.</param>
    /// <returns><see langword="true"/> when the name is a wheel binding.</returns>
    public static bool TryParseMouseWheel(string? storedName, out int direction)
    {
        if (string.Equals(storedName, MouseWheelUpKey, StringComparison.OrdinalIgnoreCase))
        {
            direction = 1;
            return true;
        }

        if (string.Equals(storedName, MouseWheelDownKey, StringComparison.OrdinalIgnoreCase))
        {
            direction = -1;
            return true;
        }

        direction = 0;
        return false;
    }

    /// <summary>Returns the stored name for a mouse-wheel direction.</summary>
    /// <param name="direction">Positive scrolls up, negative scrolls down.</param>
    public static string ToRetailMouseWheel(int direction) =>
        direction > 0 ? MouseWheelUpKey : MouseWheelDownKey;

    /// <summary>Tries to turn a stored key name into a keyboard scan-code enum.</summary>
    public static bool TryParseKeyboardKey(string? storedName, out KeyCode key)
    {
        key = KeyCode.None;
        if (string.IsNullOrWhiteSpace(storedName))
        {
            return false;
        }

        // Compatibility with the early shell, which wrote KeyCode.ToString() directly.
        if (Enum.TryParse(storedName, ignoreCase: true, out key))
        {
            return true;
        }

        key = storedName.ToUpperInvariant() switch
        {
            "KEY_LEFT" => KeyCode.Left,
            "KEY_RIGHT" => KeyCode.Right,
            "KEY_UP" => KeyCode.Up,
            "KEY_DOWN" => KeyCode.Down,
            "KEY_RETURN" => KeyCode.Return,
            "KEY_BACKSPACE" => KeyCode.Backspace,
            "KEY_SPACE" => KeyCode.Space,
            "KEY_PAGEUP" => KeyCode.PageUp,
            "KEY_PAGEDOWN" => KeyCode.PageDown,
            "KEY_LEFT_SHIFT" => KeyCode.LeftShift,
            "KEY_RIGHT_SHIFT" => KeyCode.RightShift,
            "KEY_LEFT_CTRL" => KeyCode.LeftControl,
            "KEY_RIGHT_CTRL" => KeyCode.RightControl,
            "KEY_LEFT_ALT" => KeyCode.LeftAlt,
            "KEY_RIGHT_ALT" => KeyCode.RightAlt,
            "KEY_NUMPAD0" => KeyCode.Numpad0,
            "KEY_NUMPAD1" => KeyCode.Numpad1,
            "KEY_NUMPAD2" => KeyCode.Numpad2,
            "KEY_NUMPAD3" => KeyCode.Numpad3,
            "KEY_NUMPAD4" => KeyCode.Numpad4,
            "KEY_NUMPAD5" => KeyCode.Numpad5,
            "KEY_NUMPAD6" => KeyCode.Numpad6,
            "KEY_NUMPAD7" => KeyCode.Numpad7,
            "KEY_NUMPAD8" => KeyCode.Numpad8,
            "KEY_NUMPAD9" => KeyCode.Numpad9,
            "KEY_0" => KeyCode.D0,
            "KEY_1" => KeyCode.D1,
            "KEY_2" => KeyCode.D2,
            "KEY_3" => KeyCode.D3,
            "KEY_4" => KeyCode.D4,
            "KEY_5" => KeyCode.D5,
            "KEY_6" => KeyCode.D6,
            "KEY_7" => KeyCode.D7,
            "KEY_8" => KeyCode.D8,
            "KEY_9" => KeyCode.D9,
            _ => KeyFromSimpleRetailName(storedName),
        };
        return key != KeyCode.None;
    }

    /// <summary>Returns the original config symbol for a keyboard key.</summary>
    public static string ToRetailKeyboardKey(KeyCode key) => key switch
    {
        KeyCode.Left => "KEY_LEFT",
        KeyCode.Right => "KEY_RIGHT",
        KeyCode.Up => "KEY_UP",
        KeyCode.Down => "KEY_DOWN",
        KeyCode.Return => "KEY_RETURN",
        KeyCode.Backspace => "KEY_BACKSPACE",
        KeyCode.Space => "KEY_SPACE",
        KeyCode.PageUp => "KEY_PAGEUP",
        KeyCode.PageDown => "KEY_PAGEDOWN",
        KeyCode.LeftShift => "KEY_LEFT_SHIFT",
        KeyCode.RightShift => "KEY_RIGHT_SHIFT",
        KeyCode.LeftControl => "KEY_LEFT_CTRL",
        KeyCode.RightControl => "KEY_RIGHT_CTRL",
        KeyCode.LeftAlt => "KEY_LEFT_ALT",
        KeyCode.RightAlt => "KEY_RIGHT_ALT",
        KeyCode.Numpad0 => "KEY_NUMPAD0",
        KeyCode.Numpad1 => "KEY_NUMPAD1",
        KeyCode.Numpad2 => "KEY_NUMPAD2",
        KeyCode.Numpad3 => "KEY_NUMPAD3",
        KeyCode.Numpad4 => "KEY_NUMPAD4",
        KeyCode.Numpad5 => "KEY_NUMPAD5",
        KeyCode.Numpad6 => "KEY_NUMPAD6",
        KeyCode.Numpad7 => "KEY_NUMPAD7",
        KeyCode.Numpad8 => "KEY_NUMPAD8",
        KeyCode.Numpad9 => "KEY_NUMPAD9",
        KeyCode.D0 => "KEY_0",
        KeyCode.D1 => "KEY_1",
        KeyCode.D2 => "KEY_2",
        KeyCode.D3 => "KEY_3",
        KeyCode.D4 => "KEY_4",
        KeyCode.D5 => "KEY_5",
        KeyCode.D6 => "KEY_6",
        KeyCode.D7 => "KEY_7",
        KeyCode.D8 => "KEY_8",
        KeyCode.D9 => "KEY_9",
        _ => $"KEY_{ToScreamingSnake(key.ToString())}",
    };

    /// <summary>Returns a player-facing label for a stored binding without losing unrecognised values.</summary>
    public static string DisplayName(InputBinding binding)
    {
        if (string.Equals(binding.Device, MouseDevice, StringComparison.OrdinalIgnoreCase)
            && TryParseMouseWheel(binding.Key, out int wheel))
        {
            return wheel > 0 ? "Mouse Wheel Up" : "Mouse Wheel Down";
        }

        if (string.Equals(binding.Device, MouseDevice, StringComparison.OrdinalIgnoreCase)
            && binding.Key.StartsWith("MOUSE_BUTTON_", StringComparison.OrdinalIgnoreCase))
        {
            return $"Mouse Button {binding.Key["MOUSE_BUTTON_".Length..]}";
        }

        return TryParseKeyboardKey(binding.Key, out KeyCode key) ? key.ToString() : binding.Key;
    }

    private static KeyCode KeyFromSimpleRetailName(string storedName)
    {
        const string prefix = "KEY_";
        if (!storedName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return KeyCode.None;
        }

        string compact = storedName[prefix.Length..].Replace("_", string.Empty, StringComparison.Ordinal);
        return Enum.TryParse(compact, ignoreCase: true, out KeyCode parsed) ? parsed : KeyCode.None;
    }

    private static string ToScreamingSnake(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length + 4);
        for (int i = 0; i < value.Length; i++)
        {
            char current = value[i];
            if (i > 0 && char.IsUpper(current) && !char.IsDigit(value[i - 1]))
            {
                builder.Append('_');
            }

            builder.Append(char.ToUpperInvariant(current));
        }

        return builder.ToString();
    }
}

/// <summary>Resolves a profile's persisted controls against the live input snapshot.</summary>
public static class PlayerProfileInputBindings
{
    /// <summary>
    /// The smallest wheel motion that counts as a notch, in scroll units. Discrete mice report
    /// whole notches; precision trackpads report fractions, including feather-touch noise from
    /// resting fingers. Below this floor motion is ignored rather than fired as input.
    /// </summary>
    /// <remarks>
    /// A wheel notch is a momentary edge with no held state, so a bound action reads
    /// <see langword="true"/> on every frame that scrolls past the floor in its direction — one
    /// frame per scrolled frame, which is what makes scroll-to-switch-weapons track the gesture.
    /// The same edge semantics apply to sustained actions: scrolling continuously holds them,
    /// while a single notch nudges them for exactly one frame.
    /// </remarks>
    public const float WheelNoiseFloorNotches = 0.1f;

    /// <summary>Determines whether an action is currently held.</summary>
    public static bool IsDown(PlayerProfile? profile, InputState input, string action) =>
        Resolve(profile, input, action, pressed: false);

    /// <summary>Determines whether an action was pressed this frame.</summary>
    public static bool WasPressed(PlayerProfile? profile, InputState input, string action) =>
        Resolve(profile, input, action, pressed: true);

    /// <summary>Names the control an action is bound to, for an interface that has to show it.</summary>
    /// <param name="profile">The player's profile, or <see langword="null"/> for the shipped defaults.</param>
    /// <param name="action">The action's config name, as <c>PlayerInputBindings.ConfigName</c> spells it.</param>
    /// <returns>The first bound control's display name, or <see langword="null"/> when nothing is bound.</returns>
    /// <exception cref="ArgumentException"><paramref name="action"/> is empty.</exception>
    /// <remarks>
    /// The first binding wins, which is the same one <see cref="IsDown"/> would find first: a prompt has
    /// room for one key, and showing a key the player has rebound away from would be worse than showing
    /// none. Unbinding an action leaves the caller to decide what to print instead.
    /// </remarks>
    public static string? BoundKeyName(PlayerProfile? profile, string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        IReadOnlyList<InputBinding> bindings = profile?.Bindings ?? PlayerProfile.DefaultBindings;
        foreach (InputBinding binding in bindings)
        {
            if (string.Equals(binding.Action, action, StringComparison.OrdinalIgnoreCase))
            {
                return InputBindingKeyNames.DisplayName(binding);
            }
        }

        return null;
    }

    private static bool Resolve(PlayerProfile? profile, InputState input, string action, bool pressed)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        IReadOnlyList<InputBinding> bindings = profile?.Bindings ?? PlayerProfile.DefaultBindings;
        foreach (InputBinding binding in bindings)
        {
            if (!string.Equals(binding.Action, action, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(binding.Device, InputBindingKeyNames.KeyboardDevice, StringComparison.OrdinalIgnoreCase)
                && InputBindingKeyNames.TryParseKeyboardKey(binding.Key, out KeyCode key)
                && (pressed ? input.Keyboard.WasPressed(key) : input.Keyboard.IsDown(key)))
            {
                return true;
            }

            if (string.Equals(binding.Device, InputBindingKeyNames.MouseDevice, StringComparison.OrdinalIgnoreCase)
                && InputBindingKeyNames.TryParseMouseButton(binding.Key, out MouseButton button)
                && (pressed ? input.Mouse.WasPressed(button) : input.Mouse.IsDown(button)))
            {
                return true;
            }

            // A wheel notch is a momentary edge with no held state, so the pressed and held reads
            // are the same test: did this frame's scroll move this direction.
            if (string.Equals(binding.Device, InputBindingKeyNames.MouseDevice, StringComparison.OrdinalIgnoreCase)
                && InputBindingKeyNames.TryParseMouseWheel(binding.Key, out int wheel)
                && IsWheelNotch(input.Mouse.WheelDelta, wheel))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWheelNotch(float delta, int direction) =>
        MathF.Abs(delta) >= WheelNoiseFloorNotches
        && ((direction > 0 && delta > 0f) || (direction < 0 && delta < 0f));
}
