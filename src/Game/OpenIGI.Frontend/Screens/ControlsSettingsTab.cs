using OpenIGI.Core.Configuration;
using OpenIGI.UI.Kit;
using System.Globalization;
using OpenIGI.Frontend.Settings;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Input;
using OpenIGI.UI.Layout;
using OpenIGI.UI;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The settings page's Controls category: the mouse, a touch screen's gyroscope where there is one, then every key binding
/// of the active profile, which it rebinds in place and persists as retail input symbols.
/// </summary>
internal static class ControlsSettingsTab
{
    /// <summary>A touch screen's rows, shown once the screen has been touched.</summary>
    private static readonly SettingRow[] TouchSettings =
    [
        new SettingRow(
            "touch_look_sensitivity",
            "Look Sensitivity",
            "How far the view turns for a drag across the screen. At 1 it is the game's own rate; higher turns further "
                + "for the same drag."),
        new SettingRow(
            "touch_gyro",
            "Gyroscope Aim",
            "Turning the phone turns the view, alongside the fingers. Scope aims with it only through a sight or the "
                + "binoculars, where a small correction is hardest to make with a thumb. On an iPhone, the next tap "
                + "asks leave to read the motion sensors.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["off"] = "Off",
                ["scope"] = "Scope",
                ["always"] = "Always",
            }),
        new SettingRow(
            "touch_gyro_sensitivity",
            "Gyroscope Sensitivity",
            "How far the view turns for a turn of the phone. At 1 it turns exactly as far as the phone did; higher "
                + "needs less wrist."),
        new SettingRow(
            "touch_gyro_invert",
            "Invert Gyroscope",
            "Tilting the top of the phone away looks down instead of up."),
    ];

    // How a co-op player's voice goes out, and how loud the others' are. The key held to talk is a row of its own, below.
    private static readonly SettingRow[] VoiceSettings =
    [
        new SettingRow(
            "coop_voice",
            "Voice Chat",
            "How your voice reaches your teammates in co-op: only while you hold the push-to-talk key, whenever you speak, or "
                + "not at all. You still hear them unless their volume is at nought.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["0"] = "Off",
                ["1"] = "Push to Talk",
                ["2"] = "Open Mic",
            }),
        new SettingRow(
            "coop_voice_volume",
            "Voice Volume",
            "How loud your teammates' voices are. 100 is as they spoke; up to 200 lifts a quiet microphone.",
            Step: 10f),
    ];

    // The keys kept as a setting holding a bind name rather than as a bind: each one held or pressed only in a match.
    private static readonly (string Variable, string Caption, string Group)[] SettingKeys =
    [
        ("mp_team_key", "Team menu", Multiplayer),
        ("mp_controls_key", "Show controls", Multiplayer),
        ("coop_voice_key", "Push to talk", Voice),
    ];

    // How the movement keys behave, as the player likes them.
    private static readonly SettingRow[] MovementSettings =
    [
        new SettingRow(
            "in_crouch_toggle",
            "Toggle Crouch",
            "Press crouch once to crouch and again to stand, rather than holding it down."),
    ];

    /// <summary>Builds the category's groups.</summary>
    /// <param name="context">The shared shell context.</param>
    /// <param name="rowBuilder">What the touch screen's rows are built with.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="resetBindings">What puts the bindings back to their defaults, for R; null without an active profile.</param>
    public static List<SettingsGroup> Build(ShellContext context, SettingsRowBuilder rowBuilder, UiTheme theme, out Action? resetBindings)
    {
        resetBindings = null;
        PlayerProfile? profile = context.Config.Active;
        if (profile is null)
        {
            return [];
        }

        // The mouse comes first: it is the setting a player reaches for on the way into their first mission,
        // and unlike a binding it has no default anyone is happy with.
        var sensitivity = new Slider(theme, "Mouse sensitivity", 0f, 1f, profile.MouseSensitivity, 0.05f)
        {
            // What a player is actually choosing is how far the mouse travels for a full turn, so that is
            // what the readout says — in centimetres at a stated DPI.
            Format = v => string.Create(
                CultureInfo.InvariantCulture,
                $"{MouseLookSettings.FromSensitivity(v, invertPitch: false).TurnDistanceCentimetres():0} cm/360°"),

            // A typed number is that distance too.
            FromTyped = centimetres => MouseLookSettings.SensitivityForTurnDistance(centimetres),
            Tooltip = "How far the mouse turns the view.\n\nThe readout is how much desk a full turn takes on "
                + "an 800 DPI mouse — halve it for 1600 DPI. Lower is slower and more precise.",
        };
        sensitivity.ValueChanged += value => profile.MouseSensitivity = value;

        var invert = new Toggle(theme, "Invert mouse", profile.InvertMouse)
        {
            Tooltip = "Pushing the mouse forward looks down instead of up.",
        };
        invert.ValueChanged += value => profile.InvertMouse = value;

        var grouped = new List<(string Group, KeyBindingRow Row)>();
        var bindingRows = new List<KeyBindingRow>(profile.Bindings.Count);
        var bindingIndices = new List<int>(profile.Bindings.Count);
        PlayerProfile visibleBindings = new();
        for (int i = 0; i < profile.Bindings.Count; i++)
        {
            int index = i;
            InputBinding binding = profile.Bindings[index];
            string? label = context.Host.InputBindingLabel(binding.Action);
            if (label is null) { continue; }
            label = Humanize(label);
            int visibleIndex = bindingRows.Count;
            visibleBindings.Bindings.Add(binding);
            bindingIndices.Add(index);
            KeyCode current = InputBindingKeyNames.TryParseKeyboardKey(binding.Key, out KeyCode parsed) ? parsed : KeyCode.None;
            var row = new KeyBindingRow(theme, label, current, InputBindingKeyNames.DisplayName(binding))
            {
                Tooltip = $"Press this row, then a key, mouse button or wheel scroll, to rebind {label}.",
            };

            row.BindingChanged += key => { visibleBindings.RebindKeyboard(visibleIndex, key); Rebound(); };
            row.MouseBindingChanged += button => { visibleBindings.RebindMouseButton(visibleIndex, button); Rebound(); };
            row.MouseWheelBindingChanged += direction => { visibleBindings.RebindMouseWheel(visibleIndex, direction); Rebound(); };
            bindingRows.Add(row);
            grouped.Add((GroupOf(binding.Action), row));

            void Rebound()
            {
                for (int rowIndex = 0; rowIndex < bindingRows.Count; rowIndex++)
                {
                    profile.Bindings[bindingIndices[rowIndex]] = visibleBindings.Bindings[rowIndex];
                    RefreshBindingRow(bindingRows[rowIndex], visibleBindings.Bindings[rowIndex]);
                }
            }
        }

        // The keys that run a command rather than one of the profile's actions: the third-person view, and a co-op room's
        // mark and chat. A keyboard key each -- a command has no mouse button to answer to.
        var commandRows = new List<(KeyBindingRow Row, string Statement)>();
        foreach (CommandKey command in context.Host.CommandKeys)
        {
            string caption = Humanize(command.Label);
            var row = new KeyBindingRow(theme, caption, command.Key, CommandKeyName(command.Key))
            {
                Tooltip = $"Press this row, then a key, to rebind {caption}.",
            };
            string statement = command.Statement;
            row.BindingChanged += key => { context.Host.BindCommandKey(statement, key); RefreshCommandRows(); };
            row.MouseBindingChanged += _ => RefreshCommandRows();
            row.MouseWheelBindingChanged += _ => RefreshCommandRows();
            commandRows.Add((row, statement));
            grouped.Add((command.Statement.StartsWith(MultiplayerCommandPrefix, StringComparison.Ordinal) ? Multiplayer : "Interface", row));
        }

        // The keys held in a setting by their bind name: rebinding one writes the name, which the game reads as it plays.
        var settingKeyRows = new List<(KeyBindingRow Row, ICVar Cvar)>();
        foreach ((string variable, string caption, string group) in SettingKeys)
        {
            if (!context.Cvars.TryGet(variable, out ICVar? cvar) || cvar is null)
            {
                continue;
            }

            KeyCode current = BindKeyNames.TryParse(cvar.ValueText, out KeyCode parsed) ? parsed : KeyCode.None;
            var row = new KeyBindingRow(theme, caption, current, CommandKeyName(current))
            {
                Tooltip = $"Press this row, then a key, to rebind {caption}.",
            };
            row.BindingChanged += key =>
            {
                if (BindKeyNames.NameOf(key) is { Length: > 0 } name)
                {
                    cvar.TrySetText(name, out _);
                }

                RefreshSettingKeys();
            };

            // A key is all these can hold: a mouse button or the wheel puts the row back on the key it still is.
            row.MouseBindingChanged += _ => RefreshSettingKeys();
            row.MouseWheelBindingChanged += _ => RefreshSettingKeys();
            settingKeyRows.Add((row, cvar));
            grouped.Add((group, row));
        }

        void RefreshSettingKeys()
        {
            foreach ((KeyBindingRow row, ICVar cvar) in settingKeyRows)
            {
                KeyCode key = BindKeyNames.TryParse(cvar.ValueText, out KeyCode parsed) ? parsed : KeyCode.None;
                row.SetBinding(key, CommandKeyName(key));
            }
        }

        // Every row again from the binds themselves: a key taken from one command shows as taken, and a mouse button, which
        // a command cannot have, shows the key it still is on.
        void RefreshCommandRows()
        {
            IReadOnlyList<CommandKey> now = context.Host.CommandKeys;
            foreach ((KeyBindingRow row, string statement) in commandRows)
            {
                KeyCode key = now.FirstOrDefault(command => command.Statement == statement).Key;
                row.SetBinding(key, CommandKeyName(key));
            }
        }

        void Reset()
        {
            context.Host.ResetInputBindings(profile);
            for (int i = 0; i < bindingRows.Count; i++)
            {
                visibleBindings.Bindings[i] = profile.Bindings[bindingIndices[i]];
                RefreshBindingRow(bindingRows[i], visibleBindings.Bindings[i]);
            }

            RefreshCommandRows();
            foreach ((_, ICVar cvar) in settingKeyRows)
            {
                cvar.TrySetText(cvar.DefaultText, out _);
            }

            RefreshSettingKeys();
        }

        resetBindings = Reset;

        List<SettingsGroup> groups =
        [
            BuildBand(context, theme, grouped, Reset),
            SettingsGroup.Of("Mouse", [sensitivity, invert]),
        ];
        if (context.Host.TouchScreen)
        {
            List<Control> touchRows = rowBuilder.SettingRows(theme, TouchSettings);

            // The layout is arranged over the mission itself, so the way to its editor is offered only from a mission's pause.
            var layoutButton = new Button(theme, "CUSTOMISE CONTROLS")
            {
                Accent = ButtonAccent.Ghost,
                TextSize = 16f,
                Padding = new Thickness(18f, 10f),
                HorizontalAlignment = HorizontalAlignment.Left,
                IsEnabled = context.InGame,
                Tooltip = context.InGame
                    ? "Move, resize, fade, hide or add the touch controls and the display's readouts, over three saved layouts."
                    : "Start a mission and pause it to arrange the touch controls and the display's readouts.",
            };
            layoutButton.Activated += _ => context.Host.EditTouchLayout();
            touchRows.Insert(0, layoutButton);
            groups.Add(SettingsGroup.Of("Touch", touchRows));
        }

        foreach (string name in BindingGroups)
        {
            List<Control> rows = [.. grouped.Where(entry => entry.Group == name).Select(static entry => (Control)entry.Row)];

            // How the movement keys behave leads their panel, rather than standing in a second panel of the same name.
            if (name == "Movement")
            {
                rows.InsertRange(0, rowBuilder.SettingRows(theme, MovementSettings));
            }

            if (rows.Count > 0)
            {
                groups.Add(SettingsGroup.Of(name, rows));
            }
        }

        // Co-op voice last, beside the other multiplayer keys: how it goes out, how loud the others are, and the key held to talk.
        List<Control> voice = rowBuilder.SettingRows(theme, VoiceSettings);
        voice.AddRange(grouped.Where(static entry => entry.Group == Voice).Select(static entry => (Control)entry.Row));
        if (voice.Count > 0)
        {
            groups.Add(SettingsGroup.Of(Voice, voice));
        }

        return groups;
    }

    // The bindings' groups, in the order the page shows them.
    private static readonly string[] BindingGroups = ["Movement", "Combat", WeaponSlots, "Equipment", "Interface", Multiplayer];

    // The keys that take a weapon of each kind in hand.
    private const string WeaponSlots = "Weapon slots";

    // The keys that do nothing outside a multiplayer or co-op session, together at the foot, so a player who only
    // plays the campaign can pass them by and one setting up for a match finds them in one place.
    private const string Multiplayer = "Multiplayer";

    // Co-op voice chat's settings and its key, a panel of their own after the multiplayer keys.
    private const string Voice = "Voice";

    // The console commands a co-op room answers to, and nothing else does.
    private const string MultiplayerCommandPrefix = "coop_";

    // Across the top: a box that narrows the rows to those whose action or key has what is typed, how a row is rebound,
    // and what puts every binding back. R does the last from the keyboard; the button is there for the pointer.
    private static SettingsGroup BuildBand(ShellContext context, UiTheme theme, List<(string Group, KeyBindingRow Row)> rows, Action reset)
    {
        bool compact = ShellSizes.IsCompact(theme);
        var search = new TextBox(theme)
        {
            Placeholder = "Search actions or keys",
            MaxLength = 32,
            Width = compact ? float.NaN : 420f,
            Height = 44f,
            VerticalAlignment = VerticalAlignment.Center,
            LeadingGlyph = KitIcons.Glyph(KitIcon.Search),
            Tooltip = "Show only the actions whose name or key has this in it.",
        };
        search.TextChanged += text =>
        {
            string wanted = text.Trim();
            foreach ((string _, KeyBindingRow row) in rows)
            {
                bool shown = wanted.Length == 0
                    || row.Action.Contains(wanted, StringComparison.OrdinalIgnoreCase)
                    || KeyBindingRow.FriendlyName(row.BoundKeyName).Contains(wanted, StringComparison.OrdinalIgnoreCase);
                row.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            }
        };

        var resetButton = new Button(theme, "RESET BINDINGS")
        {
            Accent = ButtonAccent.Ghost,
            LeadingGlyph = KitIcons.Glyph(KitIcon.History),
            GlyphSize = 18f,
            TextSize = 17f,
            Tracking = 0.12f,
            Padding = new Thickness(20f, 0f),
            Height = 44f,
            VerticalAlignment = VerticalAlignment.Center,
            Tooltip = "Restore the standard WASD keyboard and mouse assignments.",
        };
        resetButton.Activated += _ => reset();

        UiElement band;
        if (compact)
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8f };
            stack.AddChild(search);
            resetButton.HorizontalAlignment = HorizontalAlignment.Left;
            stack.AddChild(resetButton);
            band = stack;
        }
        else
        {
            var row = new Grid { ColumnSpacing = 12f };
            row.RowDefinitions.Add(GridLength.Auto);
            row.ColumnDefinitions.Add(GridLength.Auto);
            row.ColumnDefinitions.Add(GridLength.Star);
            row.ColumnDefinitions.Add(GridLength.Auto);
            row.Add(search, 0, 0);
            row.Add(new Label(theme.Body, context.Host.TouchScreen ? "Select a row, then press the new key." : "Select a row, then press the new key. Esc cancels.", 15f, theme.TextSecondary) { VerticalAlignment = VerticalAlignment.Center }, 0, 1);
            row.Add(resetButton, 0, 2);
            band = row;
        }

        return new SettingsGroup("Bindings", [search, resetButton]) { Banner = band };
    }

    // Which group an action's row stands in.
    private static string GroupOf(string action)
    {
        // Dropping a weapon falls to the weapon test below: I.G.I. 2 drops in a single-player game too, and files the key with
        // its combat ones.
        if (action is "Scoreboard" or "BuyMenu")
        {
            return Multiplayer;
        }

        if (action.StartsWith("Move", StringComparison.OrdinalIgnoreCase)
            || action.StartsWith("Peek", StringComparison.OrdinalIgnoreCase)
            || action.StartsWith("Lean", StringComparison.OrdinalIgnoreCase)
            || action is "Jump" or "Crouch" or "ToggleWalkRun" or "Prone" or "Sprint")
        {
            return "Movement";
        }

        // The nine slot keys, apart from the weapon handling, so neither panel runs long.
        if (action.StartsWith("WeaponCategory", StringComparison.Ordinal))
        {
            return WeaponSlots;
        }

        if (action.Contains("Fire", StringComparison.OrdinalIgnoreCase)
            || action.Contains("Weapon", StringComparison.OrdinalIgnoreCase)
            || action.Contains("Reload", StringComparison.OrdinalIgnoreCase)
            || action.Contains("Grenade", StringComparison.OrdinalIgnoreCase)
            || action.Contains("Knife", StringComparison.OrdinalIgnoreCase)
            || action is "Underbarrel" or "Mount")
        {
            return "Combat";
        }

        if (action.StartsWith("Zoom", StringComparison.OrdinalIgnoreCase)
            || action is "Activate" or "Flashlight" or "Binoculars" or "MapComputer" or "ComputerObjectives" or "ComputerLog"
                or "NightVision" or "Thermal" or "Medkit")
        {
            return "Equipment";
        }

        return "Interface";
    }

    // An action's stored name as a person would say it: MoveLeft is Move left. A name that already has its spaces --
    // a title's own label -- is left as it is.
    private static string Humanize(string label)
    {
        if (label.Contains(' ', StringComparison.Ordinal))
        {
            return label;
        }

        string known = label switch
        {
            "MoveUp" => "Move forward",
            "MoveDown" => "Move back",
            "ToggleWalkRun" => "Walk / run",
            "PrevWeapon" => "Previous weapon",
            "Underbarrel" => "Underbarrel launcher",
            "Mount" => "Mount weapon",
            "ComputerObjectives" => "Computer: objectives",
            "ComputerLog" => "Computer: log",
            "ThirdPersonView" => "Third-person view",
            "CoopMark" => "Mark for team",
            "CoopChat" => "Chat",
            "CoopTeamChat" => "Team chat",
            _ => string.Empty,
        };
        if (known.Length > 0)
        {
            return known;
        }

        var spaced = new System.Text.StringBuilder(label.Length + 4);
        for (int i = 0; i < label.Length; i++)
        {
            char c = label[i];
            bool boundary = i > 0 && ((char.IsUpper(c) && char.IsLower(label[i - 1])) || (char.IsAsciiDigit(c) && char.IsLetter(label[i - 1])));
            if (boundary)
            {
                spaced.Append(' ');
            }

            spaced.Append(i > 0 && char.IsUpper(c) ? char.ToLowerInvariant(c) : c);
        }

        return spaced.ToString();
    }

    private static string CommandKeyName(KeyCode key) => key == KeyCode.None ? "Unbound" : key.ToString();

    private static void RefreshBindingRow(KeyBindingRow row, InputBinding binding)
    {
        KeyCode key = InputBindingKeyNames.TryParseKeyboardKey(binding.Key, out KeyCode parsed)
            ? parsed
            : KeyCode.None;
        row.SetBinding(key, InputBindingKeyNames.DisplayName(binding));
    }
}
