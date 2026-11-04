using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Actions;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>The mission's object tree: search, select, reorder and reparent by dragging, delete, frame.</summary>
internal sealed class OutlinerPanel : EditorElement
{
    private readonly MissionWorkspace _workspace;
    private readonly TextField _search;
    private readonly TreeView<MissionTask> _tree;

    public OutlinerPanel(EditorTheme theme, MissionWorkspace workspace, Func<MissionTask, bool> isHidden, Action<MissionTask> toggleHidden)
        : base(theme)
    {
        _workspace = workspace;
        _search = new TextField(theme) { Placeholder = "Search objects", SelectAllOnFocus = false };
        _search.TextChanged += text => _tree!.Filter = text;
        _tree = new TreeView<MissionTask>(
            theme,
            new TreeSource<MissionTask>
            {
                Roots = () => _workspace.Document?.RootTasks ?? [],
                Children = static task => task.Children,
                Parent = static task => task.Parent,
                Label = TaskPresentation.Name,
                Icon = TaskPresentation.Icon,
                IconTint = static task => EditorIcons.KindColour(TaskPresentation.Icon(task)),
                Detail = static task => task.Label.Length > 0 ? task.TypeName : null,
                Dimmed = isHidden,
                RowButton = (task, hovered) => isHidden(task) ? "visibility_off" : hovered ? "visibility" : null,
                RowButtonClicked = toggleHidden,
            },
            workspace.Selection);
        _tree.DropRequested += Drop;
        _tree.DeleteRequested += Delete;
        _tree.ItemActivated += task => Activated?.Invoke(task);
        _tree.ContextRequested += OpenMenu;
        workspace.Changed += _tree.Invalidate;
        AddChild(_search);
        AddChild(_tree);
    }

    /// <summary>Raised when the user asks to frame a task in the viewport.</summary>
    public event Action<MissionTask>? Activated;

    /// <summary>Gets or sets what the Create menu offers, given the parent a new object would go under (or none).</summary>
    public Func<MissionTask?, IEnumerable<MenuItem>>? CreateMenu { get; set; }

    /// <summary>Edits a task's label in place, in its row.</summary>
    /// <param name="task">The task.</param>
    /// <param name="commit">Given the new label.</param>
    public void BeginRename(MissionTask task, Action<string> commit) => _tree.BeginRename(task, task.Label, commit);

    /// <summary>Scrolls a task into view, expanding its ancestors.</summary>
    /// <param name="task">The task.</param>
    public void Reveal(MissionTask task) => _tree.Reveal(task);

    /// <summary>Deletes tasks as one undo step.</summary>
    /// <param name="tasks">The tasks.</param>
    public void Delete(IReadOnlyList<MissionTask> tasks)
    {
        if (_workspace.Document is not { } document || tasks.Count == 0)
        {
            return;
        }

        // Only what is still in the mission; and a task whose ancestor is also going is taken with it, as deleting it
        // separately would fail on the undo.
        MissionTask[] going = [.. tasks.Where(document.Contains)];
        MissionTask[] tops = [.. going.Where(task => !going.Any(other => !ReferenceEquals(other, task) && IsAncestor(other, task)))];
        if (tops.Length == 0)
        {
            return;
        }

        using (_workspace.History.BeginTransaction(tops.Length == 1 ? $"Delete {TaskPresentation.Name(tops[0])}" : $"Delete {tops.Length} objects"))
        {
            foreach (MissionTask task in tops)
            {
                _workspace.Execute(new RemoveTaskCommand(document, task));
            }
        }
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        float pad = Theme.Px(6f);
        UiRect search = new(contentRect.X + pad, contentRect.Y + pad, contentRect.Width - (pad * 2f), Theme.ControlHeight);
        _search.Place(context, search);
        UiRect tree = UiRect.FromEdges(contentRect.X, search.Bottom + pad, contentRect.Right, contentRect.Bottom);
        _tree.Place(context, tree);
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter) => painter.FillRect(Bounds, Theme.Panel);

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds) =>
        _tree.EmptyText = _workspace.Document is null
            ? _workspace.LoadError is { } error ? $"Could not read the mission: {error}" : "No mission open"
            : _search.Text.Length > 0 ? "No objects match" : "This mission has no objects";

    private static bool IsAncestor(MissionTask ancestor, MissionTask task)
    {
        for (MissionTask? walk = task.Parent; walk is not null; walk = walk.Parent)
        {
            if (ReferenceEquals(walk, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private void Drop(IReadOnlyList<MissionTask> tasks, MissionTask target, TreeDropPosition position)
    {
        if (_workspace.Document is not { } document)
        {
            return;
        }

        using (_workspace.History.BeginTransaction(tasks.Count == 1 ? $"Move {TaskPresentation.Name(tasks[0])}" : $"Move {tasks.Count} objects"))
        {
            // Each move is measured after the previous one, so inserting in order keeps the dragged group's order.
            int offset = 0;
            foreach (MissionTask task in tasks)
            {
                TaskLocation location = position == TreeDropPosition.Inside
                    ? new TaskLocation(target, target.Children.Count)
                    : AtSibling(document, task, target, position == TreeDropPosition.After, offset++);
                _workspace.Execute(new MoveTaskCommand(document, task, location));
            }
        }

        _tree.SetExpanded(target, position == TreeDropPosition.Inside || _tree.IsExpanded(target));
    }

    private static TaskLocation AtSibling(MissionDocument document, MissionTask moving, MissionTask target, bool after, int offset)
    {
        // Measured with the moving task already taken out, which shifts the target up by one when it came first.
        TaskLocation at = document.Locate(target);
        TaskLocation from = document.Locate(moving);
        int index = at.Index + (after ? 1 : 0) + offset;
        if (ReferenceEquals(from.Parent, at.Parent) && from.Index < at.Index)
        {
            index--;
        }

        return at with { Index = index };
    }

    private void OpenMenu(MissionTask? task, UiVec2 at)
    {
        if (Root is not { } root)
        {
            return;
        }

        ActionTable actions = root.Actions;
        if (task is null)
        {
            MenuPopup.OpenAt(root, at,
            [
                MenuItem.Submenu("Create", CreateMenu?.Invoke(null) ?? []),
                MenuItem.Separator,
                MenuItem.For(actions["edit.paste"]),
                MenuItem.For(actions["edit.show-all"]),
            ]);
            return;
        }

        MenuPopup.OpenAt(root, at,
        [
            MenuItem.For(actions["edit.cut"]),
            MenuItem.For(actions["edit.copy"]),
            MenuItem.For(actions["edit.paste"]),
            MenuItem.For(actions["edit.paste-child"]),
            MenuItem.Separator,
            MenuItem.For(actions["edit.rename"]),
            MenuItem.For(actions["edit.duplicate"]),
            MenuItem.For(actions["edit.delete"]),
            MenuItem.Separator,
            MenuItem.For(actions["edit.hide"]),
            MenuItem.For(actions["edit.isolate"]),
            MenuItem.For(actions["view.frame"]),
            MenuItem.Separator,
            MenuItem.Submenu("Create Child", CreateMenu?.Invoke(task) ?? []),
            MenuItem.Command("Expand All", () => SetSubtreeExpanded(task, expanded: true)),
            MenuItem.Command("Collapse All", () => SetSubtreeExpanded(task, expanded: false)),
        ]);
    }

    private void SetSubtreeExpanded(MissionTask task, bool expanded)
    {
        foreach (MissionTask each in task.SelfAndSubtree())
        {
            _tree.SetExpanded(each, expanded);
        }
    }
}
