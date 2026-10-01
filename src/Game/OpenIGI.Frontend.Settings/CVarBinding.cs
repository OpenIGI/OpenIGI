using System.Globalization;
using OpenIGI.Core.Configuration;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Settings;

/// <summary>
/// Builds a bound control for a console variable — a toggle, a slider or a dropdown, wired to the CVar
/// both ways so the widget writes it live and an external change writes the widget back.
/// </summary>
/// <remarks>
/// <para>
/// This is the heart of the settings screen: a row is a CVar rendered as the control its type calls for.
/// A boolean is a toggle, an enum is a dropdown over its choices, and a numeric with both bounds is a
/// slider; an unbounded number or free text has no gamepad-friendly control and returns none, so the
/// screen shows it read-only. Writing the widget calls <see cref="ICVar.TrySetText"/>, whose
/// <see cref="ICVar.Changed"/> event the game already listens to, so a change previews immediately.
/// </para>
/// <para>
/// The two directions are guarded against feeding back into each other: while the binding pushes a value
/// one way it ignores the echo coming the other, so a preset that rewrites a dozen CVars updates their
/// widgets without each update bouncing back through <c>TrySetText</c>.
/// </para>
/// </remarks>
public static class CVarBinding
{
    private static readonly string[] KnownPrefixes = ["r", "snd", "ui", "g"];

    /// <summary>Determines whether a control can be built for a CVar.</summary>
    /// <param name="cvar">The variable.</param>
    /// <returns><see langword="true"/> when it is a boolean, an enum, or a bounded number.</returns>
    public static bool CanBind(ICVar cvar)
    {
        ArgumentNullException.ThrowIfNull(cvar);
        return cvar.ValueType == typeof(bool)
            || cvar.ValueType.IsEnum
            || TryParseBounds(cvar.RangeText, out _, out _);
    }

    /// <summary>Builds a control bound to a CVar, or <see langword="null"/> when its type has no control.</summary>
    /// <param name="theme">The theme the control draws from.</param>
    /// <param name="cvar">The variable to bind.</param>
    /// <param name="label">A caption, or <see langword="null"/> to derive one from the CVar name.</param>
    /// <param name="lifetime">
    /// Receives a handle that unhooks the control from the CVar when disposed. Without one the CVar holds
    /// the control for as long as the CVar lives, which is only right for a screen that lives as long —
    /// a screen rebuilt on every resize, or one that comes and goes, should pass one and dispose it.
    /// </param>
    /// <returns>The bound control, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static Control? CreateControl(
        UiTheme theme,
        ICVar cvar,
        string? label = null,
        ICollection<IDisposable>? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(cvar);

        string caption = label ?? Humanize(cvar.Name);
        Binding binding;
        if (cvar.ValueType == typeof(bool))
        {
            binding = BindBool(theme, caption, cvar);
        }
        else if (cvar.ValueType.IsEnum)
        {
            binding = BindEnum(theme, caption, cvar);
        }
        else if (TryParseBounds(cvar.RangeText, out float min, out float max))
        {
            binding = BindNumeric(theme, caption, cvar, min, max);
        }
        else
        {
            return null;
        }

        lifetime?.Add(binding);
        return binding.Control;
    }

    /// <summary>Turns a CVar name into a caption: drops a known prefix and title-cases the rest.</summary>
    /// <param name="name">The CVar name, e.g. <c>snd_music_volume</c>.</param>
    /// <returns>A caption, e.g. <c>Music Volume</c>.</returns>
    public static string Humanize(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Equals("r_computer_tactical", StringComparison.OrdinalIgnoreCase))
        {
            return "Tactical Computer";
        }

        string[] parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
        int start = parts.Length > 1 && Array.Exists(KnownPrefixes, p => p == parts[0]) ? 1 : 0;

        var words = new List<string>(parts.Length);
        for (int i = start; i < parts.Length; i++)
        {
            string word = parts[i];
            words.Add(word.Equals("hud", StringComparison.OrdinalIgnoreCase)
                ? "HUD"
                : word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..]);
        }

        return words.Count > 0 ? string.Join(' ', words) : name;
    }

    private static Binding BindBool(UiTheme theme, string caption, ICVar cvar)
    {
        var gate = new BindingGate();
        var toggle = new Toggle(theme, caption, cvar.ValueText == "1");
        Action<bool> valueChanged = value =>
        {
            if (!gate.Busy)
            {
                cvar.TrySetText(value ? "1" : "0", out _);
            }
        };
        EventHandler<CVarChangedEventArgs> cvarChanged = (_, _) =>
            gate.Run(() => toggle.Value = cvar.ValueText == "1");
        toggle.ValueChanged += valueChanged;
        cvar.Changed += cvarChanged;
        return new Binding(toggle, () =>
        {
            toggle.ValueChanged -= valueChanged;
            cvar.Changed -= cvarChanged;
        });
    }

    private static Binding BindEnum(UiTheme theme, string caption, ICVar cvar)
    {
        // The enum's RangeText is its choices joined by " | ", already in the lower-case form ValueText
        // uses, so it is the reliable option list to bind through the ICVar seam.
        string[] options = cvar.RangeText.Split(" | ", StringSplitOptions.RemoveEmptyEntries);
        int current = Math.Max(0, Array.IndexOf(options, cvar.ValueText));
        var gate = new BindingGate();
        var dropdown = new Dropdown(theme, caption, options, current);
        Action<int> selectionChanged = index =>
        {
            if (!gate.Busy && index >= 0 && index < options.Length)
            {
                cvar.TrySetText(options[index], out _);
            }
        };
        EventHandler<CVarChangedEventArgs> cvarChanged = (_, _) => gate.Run(() =>
        {
            int i = Array.IndexOf(options, cvar.ValueText);
            if (i >= 0)
            {
                dropdown.SelectedIndex = i;
            }
        });
        dropdown.SelectionChanged += selectionChanged;
        cvar.Changed += cvarChanged;
        return new Binding(dropdown, () =>
        {
            dropdown.SelectionChanged -= selectionChanged;
            cvar.Changed -= cvarChanged;
        });
    }

    private static Binding BindNumeric(UiTheme theme, string caption, ICVar cvar, float min, float max)
    {
        bool isInteger = cvar.ValueType == typeof(int) || cvar.ValueType == typeof(long)
            || cvar.ValueType == typeof(short) || cvar.ValueType == typeof(byte);
        float step = isInteger ? 1f : MathF.Max(0.01f, (max - min) / 20f);
        float value = ParseFloat(cvar.ValueText, min);
        var gate = new BindingGate();
        var slider = new Slider(theme, caption, min, max, value, step)
        {
            Format = v => isInteger
                ? ((long)MathF.Round(v)).ToString(CultureInfo.InvariantCulture)
                : v.ToString("0.##", CultureInfo.InvariantCulture),
        };
        Action<float> valueChanged = v =>
        {
            if (!gate.Busy)
            {
                cvar.TrySetText(slider.Format(v), out _);
            }
        };
        EventHandler<CVarChangedEventArgs> cvarChanged = (_, _) =>
            gate.Run(() => slider.Value = ParseFloat(cvar.ValueText, slider.Value));
        slider.ValueChanged += valueChanged;
        cvar.Changed += cvarChanged;
        return new Binding(slider, () =>
        {
            slider.ValueChanged -= valueChanged;
            cvar.Changed -= cvarChanged;
        });
    }

    private static bool TryParseBounds(string rangeText, out float min, out float max)
    {
        min = 0f;
        max = 0f;
        if (string.IsNullOrEmpty(rangeText))
        {
            return false;
        }

        int separator = rangeText.IndexOf(" .. ", StringComparison.Ordinal);
        if (separator < 0)
        {
            return false; // "1 or more" / "1 or less" / a boolean's "0 | 1" — no two-sided range
        }

        string low = rangeText[..separator];
        string high = rangeText[(separator + 4)..];
        return float.TryParse(low, NumberStyles.Float, CultureInfo.InvariantCulture, out min)
            && float.TryParse(high, NumberStyles.Float, CultureInfo.InvariantCulture, out max)
            && max > min;
    }

    private static float ParseFloat(string text, float fallback) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;

    // A bound control and the two handlers that tie it to its CVar. Disposing unhooks both, once.
    private sealed class Binding(Control control, Action unhook) : IDisposable
    {
        private Action? _unhook = unhook;

        public Control Control { get; } = control;

        public void Dispose()
        {
            _unhook?.Invoke();
            _unhook = null;
        }
    }
}
