using System.Globalization;
using OpenIGI.Frontend.Settings;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Input;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The settings page's Controls category: the mouse, a touch screen's gyroscope where there is one, then every key binding
/// of the active profile, which it rebinds in place and persists as retail input symbols.
/// </summary>
internal static class ControlsSettingsTab
{
    /// <summary>A touch screen's rows, shown once the screen has been touched.</summary>
    private static readonly GraphicsSetting[] TouchSettings =
    [
        new GraphicsSetting(
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
        new GraphicsSetting(
            "touch_gyro_sensitivity",
            "Gyroscope Sensitivity",
            "How far the view turns for a turn of the phone. At 1 it turns exactly as far as the phone did; higher "
                + "needs less wrist."),
        new GraphicsSetting(
            "touch_gyro_invert",
            "Invert Gyroscope",
            "Tilting the top of the phone away looks down instead of up."),
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
            Tooltip = "How far the mouse turns the view.\n\nThe readout is how much desk a full turn takes on "
                + "an 800 DPI mouse — halve it for 1600 DPI. Lower is slower and more precise.",
        };
        sensitivity.ValueChanged += value => profile.MouseSensitivity = value;

        var invert = new Toggle(theme, "Invert mouse", profile.InvertMouse)
        {
            Tooltip = "Pushing the mouse forward looks down instead of up.",
        };
        invert.ValueChanged += value => profile.InvertMouse = value;

        var bindingRows = new List<KeyBindingRow>(profile.Bindings.Count);
        var bindingIndices = new List<int>(profile.Bindings.Count);
        PlayerProfile visibleBindings = new();
        for (int i = 0; i < profile.Bindings.Count; i++)
        {
            int index = i;
            InputBinding binding = profile.Bindings[index];
            string? label = context.Host.InputBindingLabel(binding.Action);
            if (label is null) { continue; }
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

            void Rebound()
            {
                for (int rowIndex = 0; rowIndex < bindingRows.Count; rowIndex++)
                {
                    profile.Bindings[bindingIndices[rowIndex]] = visibleBindings.Bindings[rowIndex];
                    RefreshBindingRow(bindingRows[rowIndex], visibleBindings.Bindings[rowIndex]);
                }
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
        }

        resetBindings = Reset;

        // R does the same from the keyboard; the button is there for the pointer, and says what R would.
        var resetButton = new Button(theme, "RESET BINDINGS TO DEFAULTS")
        {
            Accent = ButtonAccent.Ghost,
            TextSize = 16f,
            Padding = new Thickness(18f, 10f),
            HorizontalAlignment = HorizontalAlignment.Left,
            Tooltip = "Restore the standard WASD keyboard and mouse assignments.",
        };
        resetButton.Activated += _ => Reset();

        List<SettingsGroup> groups = [SettingsGroup.Of("Mouse", [sensitivity, invert, resetButton])];
        if (context.Host.TouchScreen)
        {
            groups.Add(SettingsGroup.Of("Touch", rowBuilder.SettingRows(theme, TouchSettings)));
        }

        groups.Add(SettingsGroup.Of("Key bindings", [.. bindingRows]));
        return groups;
    }

    private static void RefreshBindingRow(KeyBindingRow row, InputBinding binding)
    {
        KeyCode key = InputBindingKeyNames.TryParseKeyboardKey(binding.Key, out KeyCode parsed)
            ? parsed
            : KeyCode.None;
        row.SetBinding(key, InputBindingKeyNames.DisplayName(binding));
    }
}
