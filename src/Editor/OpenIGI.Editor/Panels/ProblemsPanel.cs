using System.Globalization;
using OpenIGI.Core.Collections;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>A row of the problems list: a check and how many it found, or one problem under it.</summary>
/// <param name="Check">The check, for a group row.</param>
/// <param name="Issue">The problem, for a problem row.</param>
internal sealed record ProblemRow(string Check, MissionIssue? Issue);

/// <summary>
/// The mission's analysis: every problem the checks find, grouped by check, kept current as the mission is edited. The
/// selected problem is explained beside the list with a button for each fix, one to fix every problem like it, and one to
/// go to what it is about; double-clicking a problem goes there too.
/// </summary>
internal sealed class ProblemsPanel : EditorElement
{
    private readonly MissionWorkspace _workspace;
    private readonly IMissionFacts _facts;
    private readonly TreeView<ProblemRow> _list;
    private readonly Selection<ProblemRow> _selection = new();
    private readonly Button _errors;
    private readonly Button _warnings;
    private readonly Button _analyze;
    private readonly Button _goTo;
    private readonly Button _fixAll;
    private readonly List<Button> _fixes = [];
    private IReadOnlyList<MissionIssue> _issues = [];
    private IReadOnlyList<ProblemRow> _groups = [];
    private Dictionary<string, IReadOnlyList<ProblemRow>> _byCheck = [];
    private MissionIssue? _shown;
    private bool _stale = true;

    public ProblemsPanel(EditorTheme theme, MissionWorkspace workspace, IMissionFacts facts)
        : base(theme)
    {
        _workspace = workspace;
        _facts = facts;
        _list = new TreeView<ProblemRow>(
            theme,
            new TreeSource<ProblemRow>
            {
                Roots = () => _groups,
                Children = row => row.Issue is null ? _byCheck.GetValueOrDefault(row.Check, []) : [],
                HasChildren = static row => row.Issue is null,
                Parent = row => row.Issue is null ? null : _groups.FirstOrDefault(g => g.Check == row.Check),
                Label = static row => row.Issue?.Message ?? row.Check,
                Icon = static row => row.Issue is null ? KitIcon.Search : row.Issue.Severity == IssueSeverity.Error ? KitIcon.Close : KitIcon.Search,
                Detail = row => row.Issue is null
                    ? string.Create(CultureInfo.InvariantCulture, $"{_byCheck.GetValueOrDefault(row.Check, []).Count}")
                    : row.Issue.Task is { } task ? TaskPresentation.Name(task) : null,
                Tint = row => row.Issue is null ? null : row.Issue.Severity == IssueSeverity.Error ? theme.Error : theme.Warning,
            },
            _selection)
        { AllowDrag = false };
        _list.ItemActivated += row => GoToIssue(row.Issue);
        _selection.Changed += Rebuild;

        _errors = Toggle("Errors");
        _warnings = Toggle("Warnings");
        _analyze = new Button(theme, "Analyze Again", KitIcon.History) { Style = ButtonStyle.Flat, Tooltip = "Run every check again, the ones that read the running level too" };
        _analyze.Clicked += () => _stale = true;
        _goTo = new Button(theme, "Go To", KitIcon.ArrowRight) { Tooltip = "Select what the problem is about and frame it" };
        _goTo.Clicked += () => GoToIssue(_shown);
        _fixAll = new Button(theme, "Fix All Like This") { Tooltip = "Apply the first fix to every problem of this check, as one step" };
        _fixAll.Clicked += FixAll;
        workspace.Changed += () => _stale = true;
        foreach (UiElement child in (UiElement[])[_list, _errors, _warnings, _analyze, _goTo, _fixAll])
        {
            AddChild(child);
        }

        Button Toggle(string text)
        {
            Button button = new(theme, text) { IsToggle = true, IsChecked = true, Style = ButtonStyle.Flat };
            button.Clicked += () =>
            {
                button.IsChecked = !button.IsChecked;
                Group();
            };
            return button;
        }
    }

    /// <summary>Raised when the user asks to go to a problem's task.</summary>
    public event Action<MissionTask>? GoTo;

    /// <summary>Gets the current problems, all of them whatever the filters show.</summary>
    public IReadOnlyList<MissionIssue> Issues
    {
        get
        {
            Refresh();
            return _issues;
        }
    }

    /// <summary>Gets the number of errors.</summary>
    public int ErrorCount => Issues.Count(static i => i.Severity == IssueSeverity.Error);

    private float Bar => Theme.ControlHeight + Theme.Px(10f);

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        Refresh();
        float pad = Theme.Px(6f);
        float y = contentRect.Y + Theme.Px(5f);
        float x = contentRect.Right - pad;
        foreach (Button button in (Button[])[_analyze, _warnings, _errors])
        {
            float width = Theme.Font.MeasureWidth(button.Text, Theme.TextSize) + Theme.Px(button == _analyze ? 44f : 24f);
            x -= width;
            button.Place(context, new UiRect(x, y, width, Theme.ControlHeight));
            x -= Theme.Px(4f);
        }

        // The list on the left, the selected problem's explanation and fixes on the right.
        float split = contentRect.X + MathF.Round(contentRect.Width * 0.58f);
        _list.Place(context, UiRect.FromEdges(contentRect.X, contentRect.Y + Bar, split, contentRect.Bottom));
        float detailX = split + Theme.Px(12f);
        float buttonsY = contentRect.Y + Bar + (Theme.RowHeight * 4f) + Theme.Px(16f);
        float buttonWidth = MathF.Max(Theme.Px(120f), contentRect.Right - detailX - Theme.Px(12f));
        for (int i = 0; i < _fixes.Count; i++)
        {
            _fixes[i].Place(context, new UiRect(detailX, buttonsY + (i * (Theme.ControlHeight + Theme.Px(4f))), MathF.Min(buttonWidth, Theme.Px(260f)), Theme.ControlHeight));
        }

        float below = buttonsY + (_fixes.Count * (Theme.ControlHeight + Theme.Px(4f))) + Theme.Px(6f);
        _fixAll.Place(context, _shown is { Fixes.Count: > 0 } && _byCheck.GetValueOrDefault(_shown.Check, []).Count > 1
            ? new UiRect(detailX, below, MathF.Min(buttonWidth, Theme.Px(260f)), Theme.ControlHeight)
            : default);
        _goTo.Place(context, _shown?.Task is not null
            ? new UiRect(detailX, below + (_fixAll.Bounds.Height > 0 ? Theme.ControlHeight + Theme.Px(4f) : 0f), Theme.Px(120f), Theme.ControlHeight)
            : default);
    }

    /// <inheritdoc/>
    protected override void OnDrawOverlay(UiPainter painter)
    {
        float pad = Theme.Px(8f);
        int errors = _issues.Count(static i => i.Severity == IssueSeverity.Error);
        string summary = _workspace.Document is null
            ? "No mission open"
            : _issues.Count == 0
                ? "No problems found"
                : string.Create(CultureInfo.InvariantCulture, $"{errors} error(s), {_issues.Count - errors} warning(s)");
        EditorDraw.Label(painter, Theme, summary, new UiRect(Bounds.X + pad, Bounds.Y + Theme.Px(5f), Theme.Px(260f), Theme.ControlHeight), _issues.Count == 0 ? Theme.TextDisabled : Theme.Text, strong: _issues.Count > 0);

        float split = Bounds.X + MathF.Round(Bounds.Width * 0.58f);
        painter.FillRect(new UiRect(split, Bounds.Y + Bar, Theme.Hairline, Bounds.Height - Bar), Theme.Border);
        float x = split + Theme.Px(12f), width = Bounds.Right - x - Theme.Px(12f);
        float y = Bounds.Y + Bar + Theme.Px(6f);
        if (_shown is not { } issue)
        {
            EditorDraw.Label(painter, Theme, _issues.Count == 0 ? string.Empty : "Select a problem to see what it means and how to fix it.", new UiRect(x, y, width, Theme.RowHeight), Theme.TextDisabled);
            return;
        }

        EditorDraw.Label(painter, Theme, issue.Severity == IssueSeverity.Error ? "Error" : "Warning", new UiRect(x, y, width, Theme.RowHeight), issue.Severity == IssueSeverity.Error ? Theme.Error : Theme.Warning, strong: true);
        y += Theme.RowHeight;
        EditorDraw.Label(painter, Theme, issue.Message, new UiRect(x, y, width, Theme.RowHeight), Theme.Text);
        y += Theme.RowHeight;
        if (issue.Task is { } task)
        {
            EditorDraw.Label(painter, Theme, $"{TaskPresentation.Name(task)}  •  {task.TypeName}", new UiRect(x, y, width, Theme.RowHeight), Theme.TextMuted);
        }

        y += Theme.RowHeight;
        string explanation = MissionValidator.Checks.FirstOrDefault(c => c.Name == issue.Check)?.Explanation ?? string.Empty;
        EditorDraw.Label(painter, Theme, explanation, new UiRect(x, y, width, Theme.RowHeight), Theme.TextDisabled);
        if (issue.Fixes.Count == 0)
        {
            EditorDraw.Label(painter, Theme, "No automatic fix: go to it and change it by hand.", new UiRect(x, y + Theme.RowHeight + Theme.Px(10f), width, Theme.RowHeight), Theme.TextDisabled);
        }
    }

    private void Refresh()
    {
        if (!_stale)
        {
            return;
        }

        _stale = false;
        _issues = _workspace.Document is { } document ? MissionValidator.Validate(document, _facts) : [];
        Group();
    }

    // The problems the filters let through, by check, every group open.
    private void Group()
    {
        IEnumerable<MissionIssue> shown = _issues.Where(i => i.Severity == IssueSeverity.Error ? _errors.IsChecked : _warnings.IsChecked);
        _byCheck = shown.GroupBy(static i => i.Check).ToDictionary(
            static g => g.Key,
            static g => (IReadOnlyList<ProblemRow>)[.. g.Select(i => new ProblemRow(g.Key, i))]);
        _groups = [.. _byCheck.Keys.Select(static check => new ProblemRow(check, null))];
        foreach (ProblemRow group in _groups)
        {
            _list.SetExpanded(group, expanded: true);
        }

        _selection.Prune(row => row.Issue is not null && _issues.Contains(row.Issue));
        _list.Invalidate();
        Rebuild();
    }

    // The fix buttons for the selected problem.
    private void Rebuild()
    {
        _shown = _selection.Primary?.Issue;
        foreach (Button old in _fixes)
        {
            RemoveChild(old);
        }

        _fixes.Clear();
        foreach (IssueFix fix in _shown?.Fixes ?? [])
        {
            Button button = new(Theme, fix.Label) { Style = _fixes.Count == 0 ? ButtonStyle.Primary : ButtonStyle.Standard };
            button.Clicked += () => Apply(fix);
            AddChild(button);
            _fixes.Add(button);
        }
    }

    private void Apply(IssueFix fix)
    {
        if (fix.Make() is { } step)
        {
            _workspace.Execute(step);
            _stale = true;
        }
    }

    // The first fix of every problem the selected one's check found, as one step.
    private void FixAll()
    {
        if (_shown is not { } issue)
        {
            return;
        }

        IEditorCommand[] steps = [.. _byCheck.GetValueOrDefault(issue.Check, [])
            .Select(static row => row.Issue is { Fixes.Count: > 0 } found ? found.Fixes[0].Make() : null)
            .OfType<IEditorCommand>()];
        if (steps.Length > 0)
        {
            _workspace.Execute(new CompositeCommand($"Fix {steps.Length} {issue.Check} Problem(s)", steps));
            _stale = true;
        }
    }

    private void GoToIssue(MissionIssue? issue)
    {
        if (issue?.Task is { } task)
        {
            _workspace.Selection.Set(task);
            GoTo?.Invoke(task);
        }
    }
}
