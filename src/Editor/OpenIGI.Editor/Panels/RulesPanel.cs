using System.Globalization;
using OpenIGI.Core.Collections;
using OpenIGI.Editor.Core.Graphs;
using OpenIGI.Editor.Core.Graphs.Exec;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Editor.Core.Rules;
using OpenIGI.Host.Editor;
using OpenIGI.Scripting.Vocabulary;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>
/// The mission's logic as rules: each one a sentence that says when it runs, what else must hold, and what it then does,
/// step by step. Everything is picked from lists made of the mission's own objects and what the game's tasks can do; a
/// line can always be typed as the game's own expression instead.
/// </summary>
/// <remarks>
/// A rule is the mission's action layer read as a list (<see cref="RuleBook"/>), so the same logic shows in the graph as a
/// chain of nodes, and is compiled into the game's own tasks when the mission is saved. Every edit is one undoable step.
/// </remarks>
internal sealed partial class RulesPanel : EditorElement
{
    private readonly IEditorHost _host;
    private readonly MissionWorkspace _workspace;
    private readonly Action<string, bool> _notify;
    private readonly TreeView<RuleRow> _list;
    private readonly Selection<RuleRow> _selection = new();
    private readonly Dictionary<string, RuleRow> _rows = new(StringComparer.Ordinal);
    private readonly List<RuleRow> _roots = [];
    private readonly List<(Button Button, Func<bool> Enabled)> _bar = [];
    private IReadOnlyList<Rule> _rules = [];
    private RuleText? _text;
    private bool _stale = true;

    /// <summary>Initializes a new instance of the <see cref="RulesPanel"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="host">The editor host.</param>
    /// <param name="workspace">The open mission.</param>
    /// <param name="notify">Shows a toast: the message, and whether it is an error.</param>
    public RulesPanel(EditorTheme theme, IEditorHost host, MissionWorkspace workspace, Action<string, bool> notify)
        : base(theme)
    {
        _host = host;
        _workspace = workspace;
        _notify = notify;
        _list = new TreeView<RuleRow>(
            theme,
            new TreeSource<RuleRow>
            {
                Roots = () => _roots,
                Children = static row => row.Children,
                HasChildren = static row => row.Children.Count > 0,
                Parent = static row => row.Parent,
                Label = static row => row.Label,
                Icon = static row => row.Icon,
                Detail = static row => row.Detail,
                Dimmed = static row => row.Dimmed,
                Tint = row => row.Kind == RuleRowKind.Rule ? theme.Accent : null,
            },
            _selection)
        {
            AllowDrag = false,
            EmptyText = "No rules yet. New Rule makes one: when something happens, do these things.",
        };
        _list.ItemActivated += Edit;
        _list.DeleteRequested += rows => Delete(rows.Count > 0 ? rows[0] : null);
        AddChild(_list);

        Add("New Rule", KitIcon.Plus, "A new rule: when something happens, do these things, once", NewRule, () => _workspace.Document is not null, ButtonStyle.Primary);
        Add("If", KitIcon.None, "Something else that must hold for the rule to run", AddCondition, HasRule);
        Add("Do", KitIcon.None, "Make something happen: show a message, complete an objective, open a door…", () => AddStep(StepKind.Drive), HasRule);
        Add("Wait", KitIcon.None, "Wait some seconds before the steps after it", () => AddStep(StepKind.Wait), HasRule);
        Add("If / Else", KitIcon.None, "Do one list of steps or another, by something read when the rule gets here", () => AddStep(StepKind.Branch), HasRule);
        Add("Call", KitIcon.None, "Call one of the game's script functions", () => AddStep(StepKind.Call), HasRule);
        Add(string.Empty, KitIcon.ArrowUp, "Move it earlier", () => Move(-1), () => Selected is { Kind: RuleRowKind.Rule or RuleRowKind.Step });
        Add(string.Empty, KitIcon.ArrowDown, "Move it later", () => Move(1), () => Selected is { Kind: RuleRowKind.Rule or RuleRowKind.Step });
        Add("On / Off", KitIcon.None, "Switch the rule off without losing it, or on again", ToggleEnabled, HasRule);
        Add("Delete", KitIcon.Close, "Delete the selected rule or line (Delete)", () => Delete(Selected), () => Selected is { Kind: not RuleRowKind.When and not RuleRowKind.Otherwise });

        workspace.Changed += () => _stale = true;
        workspace.Logic.Changed += () => _stale = true;
    }

    private enum StepKind
    {
        Drive,
        Wait,
        Branch,
        Call,
    }

    private ExecLayer Actions => _workspace.Logic.Layout.Actions;

    private RuleRow? Selected => _selection.Primary;

    private float Bar => Theme.ControlHeight + Theme.Px(10f);

    /// <summary>Selects the rule with a name, or else the first whose title holds it, ignoring case.</summary>
    /// <param name="name">The name.</param>
    /// <returns>Whether one was found.</returns>
    public bool SelectNamed(string name)
    {
        Refresh();
        RuleRow? row = _roots.FirstOrDefault(r => r.Label.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? _roots.FirstOrDefault(r => r.Label.Contains(name, StringComparison.OrdinalIgnoreCase));
        _selection.Set(row);
        return row is not null;
    }

    /// <summary>
    /// Makes a rule from one line, for a launch script or the console: what starts it, then its steps, each after the word
    /// <c>then</c> (<c>Terminal_10.isHacked then wait 2 then call Game_DisableMusic() then do StatusMessage_40.Send</c>).
    /// </summary>
    /// <param name="line">The line.</param>
    /// <returns>Whether every part of it was understood.</returns>
    public bool AddFromLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        string[] parts = [.. line.Split(" then ", StringSplitOptions.None).Select(static p => p.Trim())];
        if (parts.Length == 0 || parts[0].Length == 0 || _workspace.Document is null)
        {
            return false;
        }

        List<string> taken = [RuleBook.NewId(Actions)];
        List<RuleStep> steps = [];
        foreach (string part in parts.Skip(1))
        {
            string id = RuleBook.NewId(Actions, taken);
            taken.Add(id);
            (string verb, string rest) = part.IndexOf(' ') is > 0 and int space ? (part[..space], part[(space + 1)..].Trim()) : (part, string.Empty);
            int dot = rest.IndexOf('.');
            int underscore = dot > 0 ? rest.LastIndexOf('_', dot) : -1;
            RuleStep? step = verb.ToUpperInvariant() switch
            {
                "WAIT" when double.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) => new WaitStep(id, seconds),
                "CALL" when rest.Length > 0 => new CallStep(id, rest),
                // Only a task the mission has, of the type written: a wrong id would drive whatever else has it.
                "DO" when underscore > 0 && _workspace.Logic.Graph?.Nodes.GetValueOrDefault(new GraphNodeKey("t" + rest[(underscore + 1)..dot])) is TaskGraphNode target
                    && target.Task.TypeName == rest[..underscore] && target.Task.FindParameter(rest[(dot + 1)..]) is not null
                    => new DriveStep(id, target.Key, rest[(dot + 1)..]),
                _ => null,
            };
            if (step is null)
            {
                return false;
            }

            steps.Add(step);
        }

        Commit("New Rule", new Rule(taken[0], parts[0], steps));
        Select("rule:" + taken[0]);
        return true;
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        Refresh();
        float x = contentRect.X + Theme.Px(6f);
        float y = contentRect.Y + Theme.Px(5f);
        foreach ((Button button, Func<bool> enabled) in _bar)
        {
            float width = button.Text.Length == 0
                ? Theme.ControlHeight
                : Theme.Font.MeasureWidth(button.Text, Theme.TextSize) + Theme.Px(button.Icon == KitIcon.None ? 24f : 44f);
            button.IsEnabled = enabled();
            button.Place(context, new UiRect(x, y, width, Theme.ControlHeight));
            x += width + Theme.Px(4f);
        }

        _list.Place(context, UiRect.FromEdges(contentRect.X, contentRect.Y + Bar, contentRect.Right, contentRect.Bottom - Theme.RowHeight));
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        painter.FillRect(Bounds, Theme.Panel);
        UiRect strip = UiRect.FromEdges(Bounds.X, Bounds.Bottom - Theme.RowHeight, Bounds.Right, Bounds.Bottom);
        painter.FillRect(strip, Theme.Chrome);
        EditorDraw.Label(painter, Theme, Explain(), strip with { X = strip.X + Theme.Px(8f), Width = strip.Width - Theme.Px(16f) }, Theme.TextMuted);
    }

    // The line under the list: what the selected row is, and how it is written for the game.
    private string Explain()
    {
        if (_workspace.Document is null)
        {
            return "Open a mission to see its rules.";
        }

        return Selected switch
        {
            null => "A rule runs once, the first tick its When holds. Double-click a line to change it; the buttons add lines to the selected rule.",
            { Kind: RuleRowKind.Rule, Rule.IsFreeform: true } => "This chain was wired in the graph in a way a list cannot say; change it in the Logic graph.",
            { Kind: RuleRowKind.Rule } => "Double-click to rename. It is compiled into the game's own tasks when the mission is saved.",
            { Kind: RuleRowKind.When, Rule: { } rule } => $"As the game reads it: {Shown(rule.When)}",
            { Kind: RuleRowKind.Condition, Rule: { } rule, Index: int index } when index < rule.Conditions.Count => $"As the game reads it: {Shown(rule.Conditions[index])}",
            { Kind: RuleRowKind.Step, Step: IfStep block } => $"As the game reads it: {Shown(block.Condition)}",
            { Kind: RuleRowKind.Step, Step: DriveStep drive } => $"Makes '{drive.Pin}' hold from this point of the rule on.",
            { Kind: RuleRowKind.Step, Step: WaitStep } => "Counted in game time: 30 ticks a second.",
            { Kind: RuleRowKind.Otherwise } => "The steps done when the If above does not hold. Select it and add steps.",
            _ => string.Empty,
        };

        static string Shown(string expression) => expression.Length == 0 ? "(nothing yet)" : expression;
    }

    private void Refresh()
    {
        if (!_stale)
        {
            return;
        }

        _stale = false;
        _roots.Clear();
        if (_workspace.Document is not { } document || _workspace.Logic.Graph is not { } graph)
        {
            _rules = [];
            _text = null;
            _list.Invalidate();
            return;
        }

        _rules = RuleBook.Read(Actions);
        _text = new RuleText(document, graph, _host.Vocabulary, _host.MissionText);
        HashSet<string> live = new(StringComparer.Ordinal);
        foreach (Rule rule in _rules)
        {
            bool isNew = !_rows.ContainsKey("rule:" + rule.Id);
            RuleRow row = Row(live, "rule:" + rule.Id, RuleRowKind.Rule, rule, null, _text.Title(rule), KitIcon.Play);
            row.Detail = rule.IsFreeform ? "made in the graph" : rule.IsEnabled ? null : "off";
            row.Dimmed = !rule.IsEnabled;
            _roots.Add(row);
            Child(live, row, $"when:{rule.Id}", RuleRowKind.When, rule, $"WHEN   {_text.Condition(rule.When)}", KitIcon.ChevronRight);
            for (int i = 0; i < rule.Conditions.Count; i++)
            {
                RuleRow condition = Child(live, row, string.Create(CultureInfo.InvariantCulture, $"if:{rule.Id}:{i}"), RuleRowKind.Condition, rule, $"IF   {_text.Condition(rule.Conditions[i])}", KitIcon.Check);
                condition.Index = i;
            }

            Steps(live, row, rule, rule.Steps);
            if (isNew)
            {
                _list.SetExpanded(row, expanded: true);
            }
        }

        foreach (string gone in _rows.Keys.Where(k => !live.Contains(k)).ToArray())
        {
            _rows.Remove(gone);
        }

        _selection.Prune(row => live.Contains(row.Key));
        _list.Invalidate();
    }

    private void Steps(HashSet<string> live, RuleRow parent, Rule rule, IReadOnlyList<RuleStep> steps)
    {
        foreach (RuleStep step in steps)
        {
            bool isNew = !_rows.ContainsKey("step:" + step.Id);
            string words = step is IfStep condition ? $"IF   {_text!.Condition(condition.Condition)}" : $"THEN   {_text!.Step(step)}";
            RuleRow row = Child(live, parent, "step:" + step.Id, RuleRowKind.Step, rule, words, step is WaitStep ? KitIcon.Pause : step is IfStep ? KitIcon.Check : KitIcon.ArrowRight);
            row.Step = step;
            if (step is IfStep block)
            {
                Steps(live, row, rule, block.Then);
                RuleRow otherwise = Child(live, row, "else:" + step.Id, RuleRowKind.Otherwise, rule, "OTHERWISE", KitIcon.ChevronRight);
                otherwise.Step = step;
                Steps(live, otherwise, rule, block.Otherwise);
                if (isNew)
                {
                    _list.SetExpanded(row, expanded: true);
                    _list.SetExpanded(otherwise, expanded: true);
                }
            }
        }
    }

    private RuleRow Child(HashSet<string> live, RuleRow parent, string key, RuleRowKind kind, Rule rule, string label, KitIcon icon)
    {
        RuleRow row = Row(live, key, kind, rule, parent, label, icon);
        row.Dimmed = parent.Dimmed;
        parent.Children.Add(row);
        return row;
    }

    // Rows are kept by key, so the selection and what is unfolded survive a rebuild.
    private RuleRow Row(HashSet<string> live, string key, RuleRowKind kind, Rule rule, RuleRow? parent, string label, KitIcon icon)
    {
        if (!_rows.TryGetValue(key, out RuleRow? row))
        {
            _rows[key] = row = new RuleRow(key);
        }

        live.Add(key);
        row.Kind = kind;
        row.Rule = rule;
        row.Parent = parent;
        row.Label = label;
        row.Icon = icon;
        row.Step = null;
        row.Detail = null;
        row.Index = null;
        row.Children.Clear();
        return row;
    }

    private void Add(string text, KitIcon icon, string tooltip, Action clicked, Func<bool> enabled, ButtonStyle style = ButtonStyle.Flat)
    {
        Button button = new(Theme, text, icon) { Style = style, Tooltip = tooltip };
        button.Clicked += clicked;
        AddChild(button);
        _bar.Add((button, enabled));
    }

    private bool HasRule() => Selected is { Rule.IsFreeform: false };

    // The rule as the layer has it now: a row's own copy may be a step behind after an edit made this frame.
    private Rule? Current(RuleRow? row) =>
        row?.Rule is { } rule ? RuleBook.Read(Actions).FirstOrDefault(r => r.Id == rule.Id) : null;

    private void Commit(string name, Rule rule) =>
        _workspace.Execute(new SetActionsCommand(name, _workspace.Logic.Layout, ExecLayerState.Of(Actions), RuleBook.With(Actions, rule)));

    private void Select(string key)
    {
        _stale = true;
        Refresh();
        if (_rows.TryGetValue(key, out RuleRow? row))
        {
            _selection.Set(row);
            _list.Reveal(row);
        }
    }

    private void NewRule()
    {
        string id = RuleBook.NewId(Actions);
        Commit("New Rule", new Rule(id, string.Empty, []) { Name = string.Create(CultureInfo.InvariantCulture, $"Rule {_rules.Count + 1}") });
        Select("rule:" + id);
        if (_rows.TryGetValue("when:" + id, out RuleRow? when))
        {
            Edit(when);
        }
    }

    private void ToggleEnabled()
    {
        if (Current(Selected) is { IsFreeform: false } rule)
        {
            Commit(rule.IsEnabled ? "Switch Rule Off" : "Switch Rule On", rule with { IsEnabled = !rule.IsEnabled });
        }
    }

    private void AddCondition()
    {
        if (Current(Selected) is not { IsFreeform: false } rule)
        {
            return;
        }

        PickCondition("What else must hold", string.Empty, text => Commit("Add Condition", rule with { Conditions = [.. rule.Conditions, text] }));
    }

    private void AddStep(StepKind kind)
    {
        if (Selected is not { } row || Current(row) is not { IsFreeform: false } rule)
        {
            return;
        }

        StepPlace place = row switch
        {
            { Kind: RuleRowKind.Otherwise, Step: { } block } => new StepPlace(block.Id, Inside: true, Otherwise: true),
            { Kind: RuleRowKind.Step, Step: IfStep block } => new StepPlace(block.Id, Inside: true),
            { Kind: RuleRowKind.Step, Step: { } step } => new StepPlace(step.Id),
            _ => new StepPlace(null),
        };
        string id = RuleBook.NewId(Actions);
        void Put(RuleStep step)
        {
            Commit("Add Step", RuleEdits.Insert(rule, step, place));
            Select("step:" + id);
        }

        switch (kind)
        {
            case StepKind.Wait:
                PickSeconds(1.0, seconds => Put(new WaitStep(id, seconds)));
                break;

            case StepKind.Branch:
                PickCondition("If what holds", string.Empty, text => Put(new IfStep(id, text, [], [])));
                break;

            case StepKind.Call:
                PickCall(string.Empty, text => Put(new CallStep(id, text)));
                break;

            default:
                PickAction((target, pin) => Put(new DriveStep(id, target, pin)));
                break;
        }
    }

    // A double click on a line: its value picked or typed again.
    private void Edit(RuleRow row)
    {
        if (Current(row) is not { } rule)
        {
            return;
        }

        if (rule.IsFreeform)
        {
            _notify("This chain was wired in the graph; change it in the Logic graph.", false);
            return;
        }

        switch (row)
        {
            case { Kind: RuleRowKind.Rule }:
                _list.BeginRename(row, rule.Name, name => Commit("Rename Rule", rule with { Name = name.Trim() }));
                break;

            case { Kind: RuleRowKind.When }:
                PickCondition("When what holds", rule.When, text => Commit("Change When", rule with { When = text }));
                break;

            case { Kind: RuleRowKind.Condition, Index: int index } when index < rule.Conditions.Count:
                PickCondition("What else must hold", rule.Conditions[index], text =>
                    Commit("Change Condition", rule with { Conditions = [.. rule.Conditions.Select((c, i) => i == index ? text : c)] }));
                break;

            case { Kind: RuleRowKind.Step, Step: WaitStep wait }:
                PickSeconds(wait.Seconds, seconds => Commit("Change Wait", RuleEdits.Replace(rule, wait.Id, s => ((WaitStep)s) with { Seconds = seconds })));
                break;

            case { Kind: RuleRowKind.Step, Step: IfStep block }:
                PickCondition("If what holds", block.Condition, text => Commit("Change If", RuleEdits.Replace(rule, block.Id, s => ((IfStep)s) with { Condition = text })));
                break;

            case { Kind: RuleRowKind.Step, Step: CallStep call }:
                PickCall(call.Call, text => Commit("Change Call", RuleEdits.Replace(rule, call.Id, s => ((CallStep)s) with { Call = text })));
                break;

            case { Kind: RuleRowKind.Step, Step: DriveStep drive }:
                PickAction((target, pin) => Commit("Change Step", RuleEdits.Replace(rule, drive.Id, s => ((DriveStep)s) with { Target = target, Pin = pin })));
                break;
        }
    }

    private void Delete(RuleRow? row)
    {
        if (row is null || Current(row) is not { } rule)
        {
            return;
        }

        switch (row)
        {
            case { Kind: RuleRowKind.Rule }:
                _workspace.Execute(new SetActionsCommand("Delete Rule", _workspace.Logic.Layout, ExecLayerState.Of(Actions), RuleBook.Without(Actions, rule.Id)));
                break;

            case { Kind: RuleRowKind.Condition, Index: int index } when !rule.IsFreeform:
                Commit("Delete Condition", rule with { Conditions = [.. rule.Conditions.Where((_, i) => i != index)] });
                break;

            case { Kind: RuleRowKind.Step, Step: { } step } when !rule.IsFreeform:
                Commit("Delete Step", RuleEdits.Remove(rule, step.Id));
                break;
        }
    }

    private void Move(int by)
    {
        if (Selected is not { } row || Current(row) is not { } rule)
        {
            return;
        }

        if (row.Kind == RuleRowKind.Rule)
        {
            int at = _rules.Select(static r => r.Id).ToList().IndexOf(rule.Id);
            _workspace.Execute(new SetActionsCommand("Move Rule", _workspace.Logic.Layout, ExecLayerState.Of(Actions), RuleBook.Moved(Actions, rule.Id, at + by)));
        }
        else if (row is { Kind: RuleRowKind.Step, Step: { } step } && !rule.IsFreeform)
        {
            Commit("Move Step", RuleEdits.Move(rule, step.Id, by));
        }
    }
}
