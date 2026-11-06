using OpenIGI.Core.Graphics;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Host.Editor;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Runtime;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>
/// A <c>Condition</c> edited as what it says rather than as script text: groups of tests joined by "all of" or "any of", each
/// test a task's field, negated or compared, with what each reads in the running level beside it.
/// </summary>
/// <remarks>
/// <para>
/// Tasks are picked from the fields the level's scripts can read (the same names a condition uses, <c>CutScene_1200.isRun</c>),
/// so a condition built here names only things that exist. A condition the builder cannot take apart, or one the person
/// would rather type, is edited as highlighted code instead; it is never rewritten until it is edited.
/// </para>
/// </remarks>
internal sealed class ConditionEditor : EditorElement
{
    private readonly IEditorHost _host;
    private readonly Func<string> _get;
    private readonly Action<string> _set;
    private readonly Action<string> _goTo;
    private readonly Action _relayout;
    private readonly Button _modeButton;
    private readonly CodeEditor _code;
    private readonly List<Row> _rows = [];
    private ConditionGroup _root = new();
    private string _shownText = string.Empty;
    private string? _error;
    private bool _codeMode;

    /// <summary>Initializes a new instance of the <see cref="ConditionEditor"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="host">The running game, for the fields its scripts can read and their values now.</param>
    /// <param name="get">Reads the condition's text.</param>
    /// <param name="set">Writes it.</param>
    /// <param name="goTo">Selects a task by the name scripts use for it.</param>
    /// <param name="relayout">Asks for the row to be laid out again, after the condition gained or lost lines.</param>
    public ConditionEditor(EditorTheme theme, IEditorHost host, Func<string> get, Action<string> set, Action<string> goTo, Action relayout)
        : base(theme)
    {
        _host = host;
        _get = get;
        _set = set;
        _goTo = goTo;
        _relayout = relayout;
        _modeButton = new Button(theme, "Code") { Style = ButtonStyle.Flat, IsToggle = true, Tooltip = "Edit the condition as script code" };
        _modeButton.Clicked += () =>
        {
            _codeMode = !_codeMode || _error is not null;
            Rebuild();
            _relayout();
        };
        _code = new CodeEditor(theme) { Highlight = line => QscHighlighter.Line(theme, line) };
        _code.TextChanged += CommitCode;
        AddChild(_modeButton);
        AddChild(_code);
        Load(announce: false);
    }

    /// <summary>Gets how many grid rows the editor wants: one for its bar, and one per line it shows.</summary>
    public int Lines => 1 + (_codeMode ? Math.Clamp(_code.LineCount + 1, 3, 10) : Math.Max(1, _rows.Sum(static r => r.Lines.Count)));

    /// <summary>Pulls the condition in again when it changed from outside (an undo), leaving it alone while being edited.</summary>
    public void Refresh() => Load(announce: true);

    // Reads the condition in when its text changed; announce asks for a new layout when its height did.
    private void Load(bool announce)
    {
        string text = _get();
        if (text == _shownText)
        {
            return;
        }

        _shownText = text;
        bool parsed = ConditionModel.TryParse(text, out ConditionGroup root, out _error);
        _root = root;
        _codeMode = _codeMode || !parsed;
        if (_code.Text != text && !_code.IsFocusWithin)
        {
            _code.Text = text;
        }

        int before = Lines;
        Rebuild();
        if (Lines != before && announce)
        {
            _relayout();
        }
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        float line = Theme.RowHeight + Theme.Px(4f);
        float bar = Theme.ControlHeight;
        _modeButton.Place(context, new UiRect(contentRect.Right - Theme.Px(52f), contentRect.Y, Theme.Px(52f), bar));
        UiRect body = UiRect.FromEdges(contentRect.X, contentRect.Y + line, contentRect.Right, contentRect.Bottom);
        _code.Place(context, _codeMode ? body : default);
        if (_codeMode)
        {
            return;
        }

        float y = body.Y;
        float gap = Theme.Px(4f);
        foreach (Row row in _rows)
        {
            row.Top = y;
            row.Remove?.Place(context, new UiRect(body.Right - Theme.Px(20f), y, Theme.Px(20f), Theme.ControlHeight));
            for (int l = 0; l < row.Lines.Count; l++)
            {
                // Fixed widths first; what is left is shared out by weight, so a narrow inspector squeezes names, not buttons.
                List<(EditorElement Widget, float Width)> items = row.Lines[l];
                float left = body.X + (row.Depth * Theme.Px(12f)) + (l > 0 ? Theme.Px(38f) : 0f);
                float right = body.Right - Theme.Px(38f);
                float fixedWidth = items.Where(static i => i.Width > 0).Sum(i => Theme.Px(i.Width)) + (gap * Math.Max(0, items.Count - 1));
                float weights = items.Where(static i => i.Width < 0).Sum(static i => -i.Width);
                float share = MathF.Max(0f, right - left - fixedWidth);
                float x = left;
                foreach ((EditorElement widget, float width) in items)
                {
                    float w = width > 0 ? Theme.Px(width) : share * (-width / MathF.Max(weights, 1e-3f));
                    widget.Place(context, new UiRect(x, y, MathF.Max(0f, w), Theme.ControlHeight));
                    x += w + gap;
                }

                y += line;
            }
        }
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        // The bar: what the whole condition comes to in the running level, or why the text is not a condition.
        UiRect bar = new(Bounds.X, Bounds.Y, Bounds.Width - Theme.Px(56f), Theme.ControlHeight);
        if (_error is not null)
        {
            EditorDraw.Label(painter, Theme, $"Not a condition: {_error}", bar, Theme.Error);
        }
        else if (_root.Items.Count == 0)
        {
            EditorDraw.Label(painter, Theme, "No condition: always runs", bar, Theme.TextMuted);
        }
        else
        {
            bool? now = Evaluate(_root);
            Dot(painter, new UiVec2(bar.X + Theme.Px(6f), bar.Center.Y), now);
            EditorDraw.Label(painter, Theme, now switch { true => "True now", false => "False now", _ => "Not known until the level runs" }, bar with { X = bar.X + Theme.Px(16f) }, Theme.TextMuted);
        }

        if (_codeMode)
        {
            return;
        }

        foreach (Row row in _rows)
        {
            if (row.Node is ConditionTerm or ConditionCode)
            {
                Dot(painter, new UiVec2(Bounds.Right - Theme.Px(30f), row.Top + (Theme.ControlHeight * 0.5f)), Evaluate(row.Node));
            }
        }
    }

    private void Dot(UiPainter painter, UiVec2 at, bool? state)
    {
        Bgra32 colour = state switch { true => Bgra32.FromRgb(90, 200, 120), false => Bgra32.FromRgb(200, 90, 80), _ => Theme.TextDisabled };
        painter.FillDot(at, Theme.Px(3.5f), colour);
    }

    // The widgets for every row of the builder, made again whenever the condition's shape changes.
    private void Rebuild()
    {
        foreach (Row row in _rows)
        {
            foreach ((EditorElement widget, _) in row.Lines.SelectMany(static l => l))
            {
                RemoveChild(widget);
            }

            if (row.Remove is { } remove)
            {
                RemoveChild(remove);
            }
        }

        _rows.Clear();
        _modeButton.IsChecked = _codeMode;
        if (!_codeMode)
        {
            AddGroup(_root, null, 0);
        }
    }

    private void AddGroup(ConditionGroup group, ConditionGroup? parent, int depth)
    {
        Row header = new(group, depth);
        Button join = new(Theme, group.Any ? "Any of these" : "All of these") { Tooltip = "Click to switch between every test below holding, and any one of them" };
        join.Clicked += () => Change(() => group.Any = !group.Any);
        if (parent is not null)
        {
            header.Add(Not(group), 36f);
        }

        header.Add(join, -1f);
        Button test = new(Theme, string.Empty, KitIcon.Plus) { Style = ButtonStyle.Flat, Tooltip = "Add a test of a task's field" };
        test.Clicked += () => PickSubject(subject => Change(() => group.Items.Add(new ConditionTerm { Subject = subject, Field = FieldsOf(subject).FirstOrDefault() ?? "isRun" }), shape: true));
        Button nested = new(Theme, "( )") { Style = ButtonStyle.Flat, Tooltip = "Add a group of tests joined their own way" };
        nested.Clicked += () => Change(() => group.Items.Add(new ConditionGroup { Any = !group.Any }), shape: true);
        header.Add(test, 24f);
        header.Add(nested, 30f);
        if (parent is not null)
        {
            header.Remove = RemoveButton(() => parent.Items.Remove(group));
        }

        Keep(header);
        foreach (ConditionNode item in group.Items)
        {
            switch (item)
            {
                case ConditionGroup inner:
                    AddGroup(inner, group, depth + 1);
                    break;
                case ConditionTerm term:
                    AddTerm(term, group, depth + 1);
                    break;
                case ConditionCode code:
                    AddCode(code, group, depth + 1);
                    break;
            }
        }
    }

    private void AddTerm(ConditionTerm term, ConditionGroup parent, int depth)
    {
        Row row = new(term, depth);
        Button subject = new(Theme, term.Subject) { Tooltip = "The task; click to pick another. Right of it, the field it is tested on." };
        subject.Clicked += () => PickSubject(picked => Change(() =>
        {
            term.Subject = picked;
            if (!FieldsOf(picked).Contains(term.Field))
            {
                term.Field = FieldsOf(picked).FirstOrDefault() ?? term.Field;
            }
        }));

        string[] fields = [.. FieldsOf(term.Subject).Append(term.Field).Distinct(StringComparer.Ordinal)];
        Dropdown field = new(Theme) { Options = fields, SelectedIndex = Array.IndexOf(fields, term.Field) };
        field.SelectionChanged += index => Change(() => term.Field = fields[index]);

        string[] comparisons = ["is true", .. ConditionModel.ComparisonOperators.Select(ConditionModel.Text)];
        Dropdown compare = new(Theme)
        {
            Options = comparisons,
            SelectedIndex = term.Compare is { } op ? 1 + ConditionModel.ComparisonOperators.ToList().IndexOf(op) : 0,
        };
        compare.SelectionChanged += index => Change(
            () =>
            {
                term.Compare = index == 0 ? null : ConditionModel.ComparisonOperators[index - 1];
                term.Value = index == 0 ? null : term.Value ?? new QscIntLiteral(0);
            },
            shape: false);

        Button go = new(Theme, string.Empty, KitIcon.ArrowRight) { Style = ButtonStyle.Flat, Tooltip = $"Select {term.Subject}" };
        go.Clicked += () => _goTo(term.Subject);
        row.Add(Not(term), 36f);
        row.Add(subject, -1.4f);
        row.Add(go, 20f);
        row.Add(field, -1f);

        // The comparison on a line of its own, so it is never squeezed out.
        row.NewLine();
        row.Add(compare, 64f);
        if (term.Compare is not null)
        {
            TextField value = new(Theme) { Text = term.Value is { } v ? QscPrinter.Print(v) : "0", Monospace = true, Validate = Parses };
            value.Committed += text => Change(() => term.Value = QscParser.ParseExpression(text));
            row.Add(value, -1f);
        }
        row.Remove = RemoveButton(() => parent.Items.Remove(term));
        Keep(row);
    }

    private void AddCode(ConditionCode code, ConditionGroup parent, int depth)
    {
        Row row = new(code, depth);
        TextField text = new(Theme) { Text = QscPrinter.Print(code.Expression), Monospace = true, Validate = Parses, Tooltip = "An expression the builder keeps as written" };
        text.Committed += value => Change(() => code.Expression = QscParser.ParseExpression(value));
        row.Add(Not(code), 36f);
        row.Add(text, -1f);
        row.Remove = RemoveButton(() => parent.Items.Remove(code));
        Keep(row);
    }

    private Button Not(ConditionNode node)
    {
        Button not = new(Theme, "not") { IsToggle = true, IsChecked = node.Negated, Tooltip = "Holds when this does not" };
        not.Clicked += () => Change(() => node.Negated = !node.Negated);
        return not;
    }

    private Button RemoveButton(Action remove)
    {
        Button button = new(Theme, string.Empty, KitIcon.Close) { Style = ButtonStyle.Flat, Tooltip = "Remove" };
        button.Clicked += () => Change(remove, shape: true);
        return button;
    }

    private void Keep(Row row)
    {
        foreach ((EditorElement widget, _) in row.Lines.SelectMany(static l => l))
        {
            AddChild(widget);
        }

        if (row.Remove is { } remove)
        {
            AddChild(remove);
        }

        _rows.Add(row);
    }

    // A change to the condition: written at once, and the rows made again when its shape changed.
    private void Change(Action change, bool shape = true)
    {
        change();
        _shownText = ConditionModel.Print(_root);
        _set(_shownText);
        if (shape)
        {
            int before = Lines;
            Rebuild();
            if (Lines != before)
            {
                _relayout();
            }
        }
    }

    private void CommitCode()
    {
        string text = _code.Text;
        bool parsed = ConditionModel.TryParse(text, out ConditionGroup root, out _error);
        _code.ErrorLine = parsed ? null : 0;
        if (parsed)
        {
            _root = root;
            _shownText = text;
            _set(text);
        }
    }

    // A task picked from those whose fields the level's scripts can read, searched by name.
    private void PickSubject(Action<string> picked)
    {
        if (Root is not { } root)
        {
            return;
        }

        PaletteEntry[] entries = [.. Subjects().Prepend("this").Select(s => new PaletteEntry(s, () => picked(s)) { Detail = string.Join(", ", FieldsOf(s).Take(4)) })];
        CommandPalette.Open(root, entries, "Pick a task by name: CutScene, AlarmControl, HumanSoldier…");
    }

    private IEnumerable<string> Subjects() =>
        _host.ScriptVariables.BoundNames.Where(static n => n.Contains('.'))
            .Select(static n => n[..n.LastIndexOf('.')]).Distinct(StringComparer.Ordinal).Order(StringComparer.OrdinalIgnoreCase);

    private IEnumerable<string> FieldsOf(string subject) =>
        subject == "this"
            ? ["isRun"]
            : _host.ScriptVariables.BoundNames.Where(n => n.Length > subject.Length + 1 && n.StartsWith(subject + ".", StringComparison.Ordinal))
                .Select(n => n[(subject.Length + 1)..]).Distinct(StringComparer.Ordinal);

    private static bool Parses(string text)
    {
        try
        {
            QscParser.ParseExpression(text);
            return true;
        }
        catch (QscParseException)
        {
            return false;
        }
    }

    // What a part of the condition comes to in the running level, or null where a value cannot be read.
    private bool? Evaluate(ConditionNode node)
    {
        bool? result = node switch
        {
            ConditionGroup group => group.Any
                ? group.Items.Select(Evaluate).Aggregate((bool?)false, static (a, b) => a == true || b == true ? true : a is null || b is null ? null : false)
                : group.Items.Select(Evaluate).Aggregate((bool?)true, static (a, b) => a == false || b == false ? false : a is null || b is null ? null : true),
            ConditionTerm term => Test(term),
            _ => null,
        };

        return node.Negated && result is { } held ? !held : result;
    }

    private bool? Test(ConditionTerm term)
    {
        if (!_host.ScriptVariables.TryRead(term.Symbol, out QvmValue read) || !read.IsNumber)
        {
            return null;
        }

        double now = read.Kind == QvmValueKind.Integer ? read.Integer : read.Number;
        if (term.Compare is not { } compare)
        {
            return now != 0.0;
        }

        double? against = term.Value switch
        {
            QscIntLiteral i => i.Value,
            QscRealLiteral r => r.Value,
            _ => null,
        };

        return against is not { } value ? null : compare switch
        {
            QscBinaryOperator.Equal => now == value,
            QscBinaryOperator.NotEqual => now != value,
            QscBinaryOperator.Less => now < value,
            QscBinaryOperator.LessOrEqual => now <= value,
            QscBinaryOperator.Greater => now > value,
            _ => now >= value,
        };
    }

    // One line of the builder: its part of the condition, how deep it sits, and the widgets across it.
    private sealed class Row(ConditionNode node, int depth)
    {
        public ConditionNode Node { get; } = node;

        public int Depth { get; } = depth;

        // Each line's widgets with their widths: positive in pixels, negative a weight of what is left.
        public List<List<(EditorElement Widget, float Width)>> Lines { get; } = [[]];

        public Button? Remove { get; set; }

        public float Top { get; set; }

        public void Add(EditorElement widget, float width) => Lines[^1].Add((widget, width));

        public void NewLine() => Lines.Add([]);
    }
}
