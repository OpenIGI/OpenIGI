using OpenIGI.Core.Collections;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>What is wrong with the open mission, kept current as it is edited; double-click an entry to go to it.</summary>
internal sealed class ProblemsPanel : EditorElement
{
    private readonly MissionWorkspace _workspace;
    private readonly TreeView<MissionIssue> _list;
    private IReadOnlyList<MissionIssue> _issues = [];
    private bool _stale = true;

    public ProblemsPanel(EditorTheme theme, MissionWorkspace workspace)
        : base(theme)
    {
        _workspace = workspace;
        _list = new TreeView<MissionIssue>(
            theme,
            new TreeSource<MissionIssue>
            {
                Roots = () => _issues,
                Children = static _ => [],
                HasChildren = static _ => false,
                Parent = static _ => null,
                Label = static issue => issue.Message,
                Icon = static issue => issue.Severity == IssueSeverity.Error ? KitIcon.Close : KitIcon.Search,
                Detail = static issue => issue.Task is { } task ? TaskPresentation.Name(task) : null,
                Tint = issue => issue.Severity == IssueSeverity.Error ? theme.Error : theme.Warning,
            },
            new Selection<MissionIssue>())
        { AllowDrag = false };
        _list.ItemActivated += issue =>
        {
            if (issue.Task is { } task)
            {
                _workspace.Selection.Set(task);
                GoTo?.Invoke(task);
            }
        };
        workspace.Changed += () => _stale = true;
        AddChild(_list);
    }

    /// <summary>Raised when the user asks to go to a problem's task.</summary>
    public event Action<MissionTask>? GoTo;

    /// <summary>Gets the current problems.</summary>
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

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        Refresh();
        _list.Place(context, contentRect);
    }

    /// <inheritdoc/>
    protected override void OnDrawOverlay(UiPainter painter)
    {
        if (_issues.Count == 0)
        {
            string text = _workspace.Document is null ? "No mission open" : "No problems found";
            EditorDraw.Label(painter, Theme, text, new UiRect(Bounds.X + Theme.Padding, Bounds.Y + Theme.Px(10f), Bounds.Width, Theme.RowHeight), Theme.TextDisabled);
        }
    }

    private void Refresh()
    {
        if (!_stale)
        {
            return;
        }

        _stale = false;
        _issues = _workspace.Document is { } document ? MissionValidator.Validate(document) : [];
        _list.Invalidate();
    }
}
