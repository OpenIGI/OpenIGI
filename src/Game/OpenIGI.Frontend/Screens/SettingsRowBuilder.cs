using OpenIGI.Core.Configuration;
using OpenIGI.Frontend.Settings;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// What every settings category builds its rows with: how a row finds its variable, where the variables a
/// category binds are kept for R to reset, and what ties the rows to the variables they show.
/// </summary>
/// <remarks>
/// One per build of the settings page. The page points <see cref="Find"/> and <see cref="TabCvars"/> at the
/// category it is about to build, so a staged category's rows bind through the draft and a live one's
/// straight to its variables.
/// </remarks>
internal sealed class SettingsRowBuilder
{
    private readonly ShellContext _context;
    private readonly List<IDisposable> _bindings;
    private readonly Dictionary<Control, ICVar> _boundCvars;

    /// <summary>Initializes a new instance of the <see cref="SettingsRowBuilder"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    /// <param name="bindings">What ties the page's rows to the variables they show; disposed on every rebuild.</param>
    /// <param name="boundCvars">The variable each one-variable row shows, which the help line reads.</param>
    public SettingsRowBuilder(ShellContext context, List<IDisposable> bindings, Dictionary<Control, ICVar> boundCvars)
    {
        _context = context;
        _bindings = bindings;
        _boundCvars = boundCvars;
    }

    /// <summary>Gets the shared shell context.</summary>
    public ShellContext Context => _context;

    /// <summary>Gets what ties the page's rows to the variables they show.</summary>
    public List<IDisposable> Bindings => _bindings;

    /// <summary>Gets the variable each one-variable row shows.</summary>
    public Dictionary<Control, ICVar> BoundCvars => _boundCvars;

    /// <summary>Gets or sets how the rows being built find their variables: through the draft on a staged category.</summary>
    public Func<string, ICVar?> Find { get; set; } = static _ => null;

    /// <summary>Gets or sets the variables the category being built binds, which R puts back to their defaults.</summary>
    public List<ICVar> TabCvars { get; set; } = [];

    /// <summary>Why the first of these variables that cannot take effect here cannot, as a tooltip lead.</summary>
    /// <param name="names">The variables a row drives.</param>
    /// <returns>"Unavailable: reason." and a blank line, or empty when every one of them works here.</returns>
    public string Unavailable(IReadOnlyList<string> names)
    {
        foreach (string name in names)
        {
            string reason = Context.Host.SettingUnavailable(name);
            if (reason.Length > 0)
            {
                return $"Unavailable: {reason}.\n\n";
            }
        }

        return string.Empty;
    }

    /// <summary>Whether any of these variables only takes effect on the next level load.</summary>
    public bool NamesRequiringRestart(IReadOnlyList<string> names)
    {
        foreach (string name in names)
        {
            if (Context.Cvars.TryGet(name, out ICVar? cvar)
                && cvar is not null
                && cvar.Flags.HasFlag(CVarOptions.RequiresRestart))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A row per setting, captioned and explained in the page's own words.</summary>
    /// <remarks>
    /// Deliberately not <see cref="CvarGroupBuilder.CvarRows"/>, which captions from the variable name and explains from the
    /// variable's own help — text written for the console. The variable name still ends the tooltip, as the
    /// handle for going further from the console.
    /// </remarks>
    public List<Control> SettingRows(UiTheme theme, IReadOnlyList<GraphicsSetting> settings)
    {
        var rows = new List<Control>();
        foreach (GraphicsSetting setting in settings)
        {
            if (Find(setting.Variable) is not { } cvar || !CVarBinding.CanBind(cvar))
            {
                continue;
            }

            if (CVarBinding.CreateControl(theme, cvar, setting.Caption, _bindings, setting.Choices) is not { } control)
            {
                continue;
            }

            DescribeRow(control, cvar, setting);
            rows.Add(control);
            TabCvars.Add(cvar);
        }

        return rows;
    }

    /// <summary>Greys a row out where its setting cannot take effect, and gives it its help.</summary>
    public void DescribeRow(Control control, ICVar cvar, GraphicsSetting setting)
    {
        string restart = cvar.Flags.HasFlag(CVarOptions.RequiresRestart)
            ? "   [takes effect on the next level]"
            : string.Empty;

        string unavailable = Unavailable([setting.Variable]);
        control.IsEnabled = unavailable.Length == 0;
        control.Tooltip = $"{unavailable}{setting.Help}{restart}\n\n{setting.Variable}";
    }

    /// <summary>Runs an action whenever a variable changes, for as long as this build of the page is shown.</summary>
    public void WhileShown(string variable, Action action)
    {
        if (!Context.Cvars.TryGet(variable, out ICVar? cvar) || cvar is null)
        {
            return;
        }

        EventHandler<CVarChangedEventArgs> changed = (_, _) => action();
        cvar.Changed += changed;
        _bindings.Add(new Unsubscribe(() => cvar.Changed -= changed));
    }

    /// <summary>Counts these variables among the category's, for R to put back.</summary>
    public void TrackForReset(IReadOnlyList<string> names)
    {
        foreach (string name in names)
        {
            if (Find(name) is { } cvar)
            {
                TabCvars.Add(cvar);
            }
        }
    }
}
