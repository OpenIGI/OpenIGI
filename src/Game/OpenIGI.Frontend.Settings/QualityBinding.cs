using OpenIGI.Core.Configuration;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Settings;

/// <summary>
/// Builds the control for a <see cref="QualityGroup"/>: one dropdown that writes every variable under it,
/// and reads them back to say which step they currently are.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart to <see cref="CVarBinding"/>, which binds one control to one variable. Here one control
/// stands over several, so the two directions are not symmetrical: choosing a step writes them all, while
/// reading back has to <em>recognise</em> a step from what the variables happen to say — and sometimes none
/// of them fits, because a variable has been set from the console on its own. That is what the extra
/// <c>Custom</c> step is for. It is shown so the row can tell the truth, and disabled so it cannot be
/// chosen: there is nothing for "make it custom" to write.
/// </para>
/// <para>
/// A group whose variables are not all registered builds nothing rather than a row that half works.
/// </para>
/// </remarks>
public static class QualityBinding
{
    /// <summary>The step shown when the variables match none of the group's own.</summary>
    public const string CustomOption = "Custom";

    /// <summary>Builds a dropdown bound to a quality group, or <see langword="null"/> when it cannot be.</summary>
    /// <param name="theme">The theme the control draws from.</param>
    /// <param name="cvars">The registry the group's variables live in.</param>
    /// <param name="group">The group to bind.</param>
    /// <param name="lifetime">
    /// Receives a handle that unhooks the dropdown from its variables when disposed. Without one the
    /// variables -- which live for the process -- hold the dropdown, and the page it sits on, for as long.
    /// </param>
    /// <returns>The bound dropdown, or <see langword="null"/> when a variable the group names is missing.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static Dropdown? CreateControl(
        UiTheme theme, CVarRegistry cvars, QualityGroup group, ICollection<IDisposable>? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(cvars);
        return CreateControl(theme, Lookup(cvars), group, lifetime);
    }

    /// <summary>Builds a dropdown over a quality group's variables as some view of them has them.</summary>
    /// <param name="theme">The theme the control draws from.</param>
    /// <param name="find">
    /// Finds a variable by name, or <see langword="null"/>: the registry's, or a <see cref="SettingsDraft"/>'s
    /// staged stand-ins, so the row writes only to the draft.
    /// </param>
    /// <param name="group">The group to bind.</param>
    /// <param name="lifetime">Receives the handle that unhooks the dropdown; see the registry overload.</param>
    /// <returns>The bound dropdown, or <see langword="null"/> when a variable the group names is missing.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static Dropdown? CreateControl(
        UiTheme theme, Func<string, ICVar?> find, QualityGroup group, ICollection<IDisposable>? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(find);
        ArgumentNullException.ThrowIfNull(group);

        var managed = new List<ICVar>();
        foreach (string name in group.Variables)
        {
            if (find(name) is not { } cvar)
            {
                return null;
            }

            managed.Add(cvar);
        }

        if (managed.Count == 0 || group.Levels.Count == 0)
        {
            return null;
        }

        string[] options = new string[group.Levels.Count + 1];
        for (int i = 0; i < group.Levels.Count; i++)
        {
            options[i] = group.Levels[i].Name;
        }

        int customIndex = group.Levels.Count;
        options[customIndex] = CustomOption;

        var gate = new BindingGate();
        var dropdown = new Dropdown(theme, group.Caption, options, Detect(find, group) ?? customIndex)
        {
            OptionEnabled = index => index != customIndex,
        };

        dropdown.SelectionChanged += index =>
        {
            if (gate.Busy || index < 0 || index >= group.Levels.Count)
            {
                return;
            }

            // Inside the gate, so the variables' own change events do not each re-read the group and fight
            // the write half way through it.
            gate.Run(() =>
            {
                foreach ((string variable, string value) in group.Levels[index].Values)
                {
                    find(variable)?.TrySetText(value, out _);
                }
            });
        };

        // Every variable is watched, because the console can move them one at a time and this row has to
        // notice. A change that lands the set on one of the steps shows that step; anything else shows
        // Custom, which is the honest answer.
        EventHandler<CVarChangedEventArgs> changed =
            (_, _) => gate.Run(() => dropdown.SelectedIndex = Detect(find, group) ?? customIndex);
        foreach (ICVar cvar in managed)
        {
            cvar.Changed += changed;
        }

        lifetime?.Add(new Unhook(() =>
        {
            foreach (ICVar cvar in managed)
            {
                cvar.Changed -= changed;
            }
        }));

        return dropdown;
    }

    /// <summary>Works out which of a group's steps the current values are, if any.</summary>
    /// <param name="cvars">The registry to read.</param>
    /// <param name="group">The group to recognise.</param>
    /// <returns>The step's index, or <see langword="null"/> when the values match none of them.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static int? Detect(CVarRegistry cvars, QualityGroup group)
    {
        ArgumentNullException.ThrowIfNull(cvars);
        return Detect(Lookup(cvars), group);
    }

    /// <summary>Works out which of a group's steps some view of its variables is at, if any.</summary>
    /// <param name="find">Finds a variable by name, or <see langword="null"/>.</param>
    /// <param name="group">The group to recognise.</param>
    /// <returns>The step's index, or <see langword="null"/> when the values match none of them.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static int? Detect(Func<string, ICVar?> find, QualityGroup group)
    {
        ArgumentNullException.ThrowIfNull(find);
        ArgumentNullException.ThrowIfNull(group);

        for (int index = 0; index < group.Levels.Count; index++)
        {
            bool matches = true;
            foreach ((string variable, string value) in group.Levels[index].Values)
            {
                // MatchesText rather than a comparison against ValueText, for the reason
                // GraphicsPresetRegistry.Detect records: a step declaring 0.70 reads back as 0.7 and the
                // row would report Custom for ever after.
                if (find(variable) is not { } cvar || !cvar.MatchesText(value))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return index;
            }
        }

        return null;
    }

    private static Func<string, ICVar?> Lookup(CVarRegistry cvars) =>
        name => cvars.TryGet(name, out ICVar? cvar) ? cvar : null;

    // Runs its action once, on the first dispose.
    private sealed class Unhook(Action action) : IDisposable
    {
        private Action? _action = action;

        public void Dispose()
        {
            _action?.Invoke();
            _action = null;
        }
    }
}
