using System.Globalization;
using OpenIGI.Editor.Core.Graphs.Exec;
using OpenIGI.Editor.Core.Rules;
using OpenIGI.Host.Editor;
using OpenIGI.Scripting.Runtime;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor;

/// <summary>
/// The mission's rules over a mission played from the editor: each one waiting, or run and how long into play, read from
/// the game's own variables as it runs.
/// </summary>
/// <remarks>
/// A rule is compiled into a latch (<see cref="ExecLayerCompiler"/>) that counts to one the tick its rule runs, so the rule
/// has run once its latch's <c>nValue</c> is above nought; the watch reads that, as the game exports it, and nothing else.
/// </remarks>
internal sealed class LogicWatch(IEditorHost host, MissionWorkspace workspace)
{
    private const int MostShown = 12;

    // A gap this long between two frames drawn while playing is play started again.
    private const double NewPlayGapSeconds = 1.0;

    private readonly Dictionary<string, double> _ranAt = new(StringComparer.Ordinal);
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private double _lastDrawn = double.NegativeInfinity;
    private double _playSeconds;

    // The time played, counted from the frames drawn while playing; the loading panel's time not counted.
    private void Tick()
    {
        double now = _clock.Elapsed.TotalSeconds;
        double gap = now - _lastDrawn;
        _lastDrawn = now;
        if (gap > NewPlayGapSeconds)
        {
            _ranAt.Clear();
            _playSeconds = 0;
        }
        else if (!host.IsWarmingUp)
        {
            _playSeconds += gap;
        }
    }

    /// <summary>Draws the watch in the window's top right corner; called each frame while a mission is played.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The editor's theme.</param>
    public void Draw(UiPainter painter, EditorTheme theme)
    {
        Tick();
        ExecLayer layer = workspace.Logic.Layout.Actions;
        IReadOnlyList<Rule> rules = RuleBook.Read(layer);

        // Every rule watched, shown or not: the play-test's recording marks the moment each first runs.
        foreach (Rule rule in rules.Where(static r => r.IsEnabled))
        {
            if (!_ranAt.ContainsKey(rule.Id) && Ran(layer, rule))
            {
                _ranAt[rule.Id] = _playSeconds;
                host.MarkPlaytest($"rule: {Title(rule)}");
            }
        }

        if (!EditorCVars.LogicWatch.Value || rules.Count == 0)
        {
            return;
        }

        List<(string Text, bool Ran)> lines = [];
        foreach (Rule rule in rules.Take(MostShown))
        {
            string title = Title(rule);
            if (!rule.IsEnabled)
            {
                lines.Add(($"–  {title}  (off)", false));
                continue;
            }

            lines.Add(_ranAt.TryGetValue(rule.Id, out double at)
                ? (string.Create(CultureInfo.InvariantCulture, $"✓  {title}   at {at:0.0} s"), true)
                : ($"…  {title}", false));
        }

        if (rules.Count > MostShown)
        {
            lines.Add((string.Create(CultureInfo.InvariantCulture, $"and {rules.Count - MostShown} more"), false));
        }

        float pad = theme.Px(10f);
        float width = MathF.Min(theme.Px(420f), lines.Max(l => theme.Font.MeasureWidth(l.Text, theme.TextSize)) + (pad * 2f));
        float height = ((lines.Count + 1) * theme.RowHeight) + pad;
        UiRect box = new(host.Width - width - theme.Px(14f), theme.Px(64f), width, height);
        EditorDraw.Shadow(painter, theme, box);
        EditorDraw.Frame(painter, theme, box, theme.Popup.WithAlpha(215), theme.Border);
        float y = box.Y + (pad * 0.5f);
        EditorDraw.Label(painter, theme, "Rules", new UiRect(box.X + pad, y, width - (pad * 2f), theme.RowHeight), theme.TextMuted, strong: true);
        foreach ((string text, bool ran) in lines)
        {
            y += theme.RowHeight;
            EditorDraw.Label(painter, theme, text, new UiRect(box.X + pad, y, width - (pad * 2f), theme.RowHeight), ran ? theme.Accent : theme.Text);
        }
    }

    private static string Title(Rule rule) => rule.Name.Length > 0 ? rule.Name : rule.When;

    // Whether a rule's latch has counted: the first task its event was compiled to.
    private bool Ran(ExecLayer layer, Rule rule) =>
        layer.Generated.TryGetValue(rule.Id, out List<int>? ids) && ids.Count > 0
        && host.ScriptVariables.TryRead(string.Create(CultureInfo.InvariantCulture, $"EditVariable_{ids[0]}.nValue"), out QvmValue value)
        && value.ToDouble() > 0;
}
