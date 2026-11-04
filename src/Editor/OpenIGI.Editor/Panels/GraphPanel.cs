using OpenIGI.Core.Collections;
using OpenIGI.Editor.Core.Graphs;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Host.Editor;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>
/// The mission's logic as small node graphs, one for each container of logic, listed down the side: a graph's tasks, the
/// conditions they read and the fields they export, wired together, with what they wire to elsewhere as reference nodes.
/// Every edit is a document command, so it undoes like any other.
/// </summary>
internal sealed partial class GraphPanel : EditorElement
{
    private readonly IEditorHost _host;
    private readonly MissionWorkspace _workspace;
    private readonly Func<IReadOnlyList<MissionIssue>> _issues;
    private readonly Action<string, bool> _notify;
    private readonly NodeCanvas _canvas;
    private readonly TreeView<GraphRow> _list;
    private readonly Selection<GraphRow> _listSelection = new();
    private readonly TextField _find;
    private readonly Button _allPins;
    private readonly Button _frameAll;
    private readonly Button _backButton;
    private readonly Button _forwardButton;
    private string _query = string.Empty;
    private readonly Button _comment;
    private readonly Button _collapse;
    private readonly List<GraphRow> _roots = [];
    private readonly Dictionary<string, GraphRow> _rows = new(StringComparer.Ordinal);
    private MissionCanvasSource? _source;
    private IReadOnlyList<GraphScope>? _listedScopes;
    private string? _scopeKey;
    private bool _stale = true;
    private bool _syncingSelection;
    private bool _frameWanted;

    /// <summary>Initializes a new instance of the <see cref="GraphPanel"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="host">The editor host.</param>
    /// <param name="workspace">The open mission.</param>
    /// <param name="issues">The mission's current problems, to badge nodes with.</param>
    /// <param name="notify">Shows a toast: the message, and whether it is an error.</param>
    public GraphPanel(EditorTheme theme, IEditorHost host, MissionWorkspace workspace, Func<IReadOnlyList<MissionIssue>> issues, Action<string, bool> notify)
        : base(theme)
    {
        _host = host;
        _workspace = workspace;
        _issues = issues;
        _notify = notify;
        _canvas = new NodeCanvas(theme, new EmptySource());
        _list = new TreeView<GraphRow>(
            theme,
            new TreeSource<GraphRow>
            {
                Roots = () => _roots,
                Children = static row => row.Children,
                HasChildren = static row => row.Children.Count > 0,
                Parent = static row => row.Parent,
                Label = static row => row.Title,
                Icon = static row => row.AiScope is not null ? KitIcon.Play : row.AiPath is not null ? KitIcon.Person : row.Key == "ai" ? KitIcon.Script : row.Scope is { IsGated: true } ? KitIcon.Graph : KitIcon.Hierarchy,
                Detail = static row => (row.Scope?.Members.Count ?? row.AiScope?.Members.Count)?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Tint = row => (row.AiScope is not null ? row.AiScope == _aiScope : _aiScope is null && row.Scope?.Key == _scopeKey) ? theme.Accent : null,
            },
            _listSelection)
        { AllowDrag = false };
        _find = new TextField(theme) { Placeholder = "Find graphs: StatusMessage, AreaActivate…", SelectAllOnFocus = false };
        _find.TextChanged += text =>
        {
            _query = text.Trim();
            _listedScopes = null;
            _stale = true;
        };
        _allPins = new Button(theme, "Show All Pins") { IsToggle = true, Style = ButtonStyle.Flat, Tooltip = "Draw every output, not only the ones something reads, to wire a new one" };
        _allPins.Clicked += () =>
        {
            _allPins.IsChecked = !_allPins.IsChecked;
            _stale = true;
        };
        _frameAll = new Button(theme, "Frame All", KitIcon.Search) { Style = ButtonStyle.Flat, Tooltip = "Fit the whole graph in view (Home)" };
        _backButton = new Button(theme, string.Empty, KitIcon.ArrowLeft) { Style = ButtonStyle.Flat, Tooltip = "Back to the graph before (Alt+Left, mouse back)" };
        _forwardButton = new Button(theme, string.Empty, KitIcon.ArrowRight) { Style = ButtonStyle.Flat, Tooltip = "Forward again (Alt+Right, mouse forward)" };
        _backButton.Clicked += GoBack;
        _forwardButton.Clicked += GoForward;
        _comment = new Button(theme, "Comment") { Style = ButtonStyle.Flat, Tooltip = "Put a comment round the selected nodes" };
        _collapse = new Button(theme, "Collapse to Macro") { Style = ButtonStyle.Flat, Tooltip = "Make the selected action nodes a project macro, one node in their place (double-click it to expand)" };
        _collapse.Clicked += CollapseToMacro;
        _frameAll.Clicked += () => _canvas.Frame();
        _comment.Clicked += CommentSelection;
        foreach (UiElement child in (UiElement[])[_find, _list, _canvas, _backButton, _forwardButton, _allPins, _frameAll, _comment, _collapse])
        {
            AddChild(child);
        }

        _listSelection.Changed += () =>
        {
            if (_listSelection.Primary is { AiPath: { } path, AiScope: { } aiScope })
            {
                OpenAi(path, aiScope);
            }
            else if (_listSelection.Primary?.Scope is { } scope)
            {
                CloseAi();
                _stale = true;
                Open(scope.Key);
            }
        };
        workspace.Changed += () => _stale = true;
        workspace.Saving += SaveAiScripts;
        workspace.BeforeSave += CompileActions;
        workspace.Logic.Changed += () => _stale = true;
        workspace.Selection.Changed += FollowWorkspaceSelection;
        _canvas.SelectionChanged += LeadWorkspaceSelection;
        _canvas.MoveRequested += Move;
        _canvas.FrameMoveRequested += MoveFrame;
        _canvas.LinkRequested += Link;
        _canvas.UnlinkRequested += Unlink;
        _canvas.LinkDroppedOnEmpty += OpenSearchFromPin;
        _canvas.DeleteRequested += Delete;
        _canvas.CommentRequested += CommentSelection;
        _canvas.ValueClicked += ValueClicked;
        _canvas.FindRequested += OpenFind;
        _canvas.CopyRequested += Copy;
        _canvas.PasteRequested += Paste;
        _canvas.DuplicateRequested += Duplicate;
        _canvas.BackRequested += GoBack;
        _canvas.ForwardRequested += GoForward;
        _canvas.FrameResizeRequested += ResizeFrame;
        _canvas.FrameContextRequested += OpenFrameMenu;
        _canvas.RerouteAddRequested += AddReroute;
        _canvas.RerouteMoveRequested += MoveReroute;
        _canvas.RerouteRemoveRequested += RemoveReroute;
        _canvas.DoubleClicked += (node, at) =>
        {
            if (node is null)
            {
                OpenAddTask(at);
            }
            else
            {
                if (_ai is not null)
                {
                    AiEditValue(node, at);
                }
                else if (!ExpandMacro(node))
                {
                    OpenHomeOf(node);
                }
            }
        };
        _canvas.ContextRequested += (_, at) => OpenAddTask(at);
    }

    private float Bar => Theme.ControlHeight + Theme.Px(10f);

    private float ListWidth => Theme.Px(230f);

    private MissionGraph? Graph => _workspace.Logic.Graph;

    /// <summary>Selects the first node of the open graph whose title holds a text, ignoring case.</summary>
    /// <param name="title">The text.</param>
    /// <returns>Whether one was found.</returns>
    public bool SelectNamed(string title)
    {
        Refresh();
        if (_canvas.Source.Nodes.FirstOrDefault(n => n.Title.Contains(title, StringComparison.OrdinalIgnoreCase)) is not { } node)
        {
            return false;
        }

        _canvas.Select([node.Id]);
        _canvas.Frame([node.Id]);
        LeadWorkspaceSelection();
        return true;
    }

    /// <summary>Opens the mission graph with a name, or else the first whose name holds it, ignoring case.</summary>
    /// <param name="name">The name.</param>
    /// <returns>Whether a graph was found.</returns>
    public bool OpenNamed(string name)
    {
        IReadOnlyList<GraphScope> scopes = _workspace.Logic.Scopes;
        GraphScope? found = scopes.FirstOrDefault(s => s.Title.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? scopes.FirstOrDefault(s => s.Title.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (found is null)
        {
            return OpenAiNamed(name);
        }

        CloseAi();
        _stale = true;
        Open(found.Key);
        _listSelection.Set(_rows.GetValueOrDefault(found.Key));
        return true;
    }

    /// <summary>Opens one of the mission's graphs.</summary>
    /// <param name="key">The scope's key.</param>
    public void Open(string key)
    {
        if (key == _scopeKey)
        {
            return;
        }

        Arrive(new GraphPlace(key, null, null));
        _scopeKey = key;
        _stale = true;
        _frameWanted = true;
        _list.Invalidate();
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        Refresh();
        _find.Place(context, new UiRect(contentRect.X + Theme.Px(6f), contentRect.Y + Theme.Px(5f), ListWidth - Theme.Px(12f), Theme.ControlHeight));
        _list.Place(context, UiRect.FromEdges(contentRect.X, contentRect.Y + Bar, contentRect.X + ListWidth, contentRect.Bottom - Theme.RowHeight));
        float left = contentRect.X + ListWidth + 1f;
        float pad = Theme.Px(6f);
        float x = left + pad;
        float y = contentRect.Y + Theme.Px(5f);
        _backButton.IsEnabled = _back.Count > 0;
        _forwardButton.IsEnabled = _forward.Count > 0;
        foreach (Button button in (Button[])[_backButton, _forwardButton, _frameAll, _comment, _collapse, _allPins])
        {
            float width = button.Text.Length == 0 ? Theme.ControlHeight : Theme.Font.MeasureWidth(button.Text, Theme.TextSize) + Theme.Px(button == _frameAll ? 44f : 24f);
            button.Place(context, new UiRect(x, y, width, Theme.ControlHeight));
            x += width + Theme.Px(4f);
        }

        float right = contentRect.Right;
        _canvas.Place(context, UiRect.FromEdges(left, contentRect.Y + Bar, right, contentRect.Bottom - Theme.RowHeight));
        RebuildDetails();
        if (_frameWanted && _canvas.Bounds.Width > 0f && _canvas.Source is { Nodes.Count: > 0 } source)
        {
            _frameWanted = false;
            _canvas.Frame();

            // Opened where its text reads: if the whole graph only fits further out, its top-left corner instead.
            if (_canvas.Zoom < 0.9f * Theme.Scale)
            {
                float minX = source.Nodes.Min(static n => n.Position.X);
                float minY = source.Nodes.Min(static n => n.Position.Y);
                float spanX = _canvas.Bounds.Width / Theme.Scale;
                float spanY = _canvas.Bounds.Height / Theme.Scale;
                _canvas.Frame([.. source.Nodes
                    .Where(n => n.Position.X < minX + spanX - NodeCanvas.NodeWidth && n.Position.Y < minY + spanY - 120f)
                    .Select(static n => n.Id)]);
            }
        }
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        painter.FillRect(Bounds, Theme.Panel);
        painter.FillRect(new UiRect(Bounds.X + ListWidth, Bounds.Y, 1f, Bounds.Height), Theme.Border);
        UiRect strip = UiRect.FromEdges(Bounds.X, Bounds.Bottom - Theme.RowHeight, Bounds.Right, Bounds.Bottom);
        painter.FillRect(strip, Theme.Chrome);
        EditorDraw.Label(painter, Theme, Explain(), strip with { X = strip.X + Theme.Px(8f), Width = strip.Width - Theme.Px(16f) }, Theme.TextMuted);
        if (_ai is not null)
        {
            float x = _allPins.Bounds.Right + Theme.Px(16f);
            EditorDraw.Label(painter, Theme, AiBreadcrumb(), new UiRect(x, _allPins.Bounds.Y, Bounds.Right - x - Theme.Px(8f), Theme.ControlHeight), Theme.Text);
        }
        else if (_source?.Scope is { } scope)
        {
            float x = _allPins.Bounds.Right + Theme.Px(16f);
            EditorDraw.Label(painter, Theme, Breadcrumb(scope), new UiRect(x, _allPins.Bounds.Y, Bounds.Right - x - Theme.Px(8f), Theme.ControlHeight), Theme.Text);
        }

        if (_workspace.Document is null)
        {
            EditorDraw.Label(painter, Theme, "Open a mission to see its logic.", new UiRect(Bounds.X + ListWidth + Theme.Px(16f), Bounds.Y + Bar + Theme.Px(16f), Bounds.Width, Theme.RowHeight), Theme.TextDisabled);
        }
    }

    // The source is built again whenever the document, the layout, the problems or the open graph change.
    private void Refresh()
    {
        if (!_stale)
        {
            return;
        }

        _stale = false;
        if (Graph is not { } graph)
        {
            _source = null;
            _canvas.Source = new EmptySource();
            _roots.Clear();
            _rows.Clear();
            _listedScopes = null;
            _list.Invalidate();
            return;
        }

        IReadOnlyList<GraphScope> scopes = _workspace.Logic.Scopes;
        if (!ReferenceEquals(scopes, _listedScopes))
        {
            _listedScopes = scopes;
            BuildList(scopes);
            AppendAiRows();
        }

        if (RefreshAi())
        {
            return;
        }

        GraphScope? scope = scopes.FirstOrDefault(s => s.Key == _scopeKey) ?? (scopes.Count > 0 ? scopes[0] : null);
        if (scope is null)
        {
            _source = null;
            _canvas.Source = new EmptySource();
            return;
        }

        if (scope.Key != _scopeKey)
        {
            _scopeKey = scope.Key;
            _frameWanted = true;
        }

        GraphLayout layout = _workspace.Logic.Layout;
        GraphAutoLayout.Place(graph, layout, scope);
        Dictionary<MissionTask, MissionIssue> problems = [];
        foreach (MissionIssue issue in _issues())
        {
            if (issue.Task is { } task)
            {
                problems.TryAdd(task, issue);
            }
        }

        _source = new MissionCanvasSource(graph, layout, scope, key => GraphScopes.HomeScope(scopes, key)?.Title, scopes[0].Key, _host.Vocabulary, _host.MissionText, _allPins.IsChecked, Theme, problems);
        _canvas.Source = _source;
    }

    // The list: each scope under the one its container sits in. Rows are kept by key, so the list's selection survives a rebuild.
    private void BuildList(IReadOnlyList<GraphScope> scopes)
    {
        Dictionary<string, GraphRow> rows = new(StringComparer.Ordinal);
        _roots.Clear();
        // While finding, the graphs that match stand flat at the top, so none hides in a folded parent.
        foreach (GraphScope scope in scopes.Where(s => _query.Length == 0 || Matches(s)))
        {
            GraphRow row = _rows.TryGetValue(scope.Key, out GraphRow? kept) ? kept : new GraphRow(scope.Key);
            row.Scope = scope;
            row.Title = scope.Title;
            row.Children.Clear();
            row.Parent = _query.Length == 0 && scope.Parent is { } parent && rows.TryGetValue(parent, out GraphRow? above) ? above : null;
            (row.Parent?.Children ?? _roots).Add(row);
            rows[scope.Key] = row;
        }

        _rows.Clear();
        foreach ((string key, GraphRow row) in rows)
        {
            _rows[key] = row;
        }

        _list.Invalidate();
    }

    // A graph matches the find text by its name, or by any task it draws (type, name, label) or expression it holds.
    private bool Matches(GraphScope scope)
    {
        if (scope.Title.Contains(_query, StringComparison.OrdinalIgnoreCase) || Graph is not { } graph)
        {
            return scope.Title.Contains(_query, StringComparison.OrdinalIgnoreCase);
        }

        return scope.Members.Concat(scope.References).Any(key => graph.Nodes.GetValueOrDefault(key) switch
        {
            TaskGraphNode task => task.Task.TypeName.Contains(_query, StringComparison.OrdinalIgnoreCase)
                || GraphScopes.Title(task).Contains(_query, StringComparison.OrdinalIgnoreCase),
            ExpressionGraphNode expression => expression.Text.Contains(_query, StringComparison.OrdinalIgnoreCase),
            _ => false,
        });
    }

    private string Breadcrumb(GraphScope scope)
    {
        List<string> parts = [];
        for (GraphRow? row = _rows.GetValueOrDefault(scope.Key); row is not null; row = row.Parent)
        {
            parts.Insert(0, row.Title);
        }

        return string.Join("  ›  ", parts);
    }

    // A reference node's own logic is another graph: double-clicking it goes there.
    private void OpenHomeOf(string id)
    {
        if (_source?.Scope is { } scope && scope.References.Contains(new GraphNodeKey(id))
            && GraphScopes.HomeScope(_workspace.Logic.Scopes, new GraphNodeKey(id)) is { } home)
        {
            Open(home.Key);
            _listSelection.Set(_rows.GetValueOrDefault(home.Key));
            _list.Reveal(_rows[home.Key]);
        }
    }

    // The line under the canvas: what the selected node is and does, from the vocabulary.
    private string Explain()
    {
        if (_canvas.Selected.Count != 1 || _canvas.Source.Nodes.Count == 0)
        {
            return "Pick a graph on the left. Drag from a pin to wire it; drop on empty canvas to search. Double-click a reference to open its graph.";
        }

        string id = _canvas.Selected.First();
        CanvasNode? node = _canvas.Source.Nodes.FirstOrDefault(n => n.Id == id);
        return node switch
        {
            null => string.Empty,
            { Problem: { } problem } => problem,
            { Tooltip: { } tip } => $"{node.Title}: {tip}",
            _ => node.Subtitle is { } subtitle ? $"{node.Title} ({subtitle})" : node.Title,
        };
    }

    // A task picked elsewhere opens the graph its logic is in, then is selected there.
    private void FollowWorkspaceSelection()
    {
        if (_syncingSelection || Graph is not { } graph)
        {
            return;
        }

        GraphNodeKey[] keys = [.. _workspace.Selection.Items.Select(t => graph.TryGetNode(t, out TaskGraphNode? node) ? node.Key : (GraphNodeKey?)null).OfType<GraphNodeKey>()];
        if (keys.Length == 1 && _source?.Scope.Members.Contains(keys[0]) != true
            && GraphScopes.HomeScope(_workspace.Logic.Scopes, keys[0]) is { } home)
        {
            Open(home.Key);
            Refresh();
        }

        _syncingSelection = true;
        _canvas.Select(keys.Select(static k => k.Value));
        _syncingSelection = false;
    }

    private void LeadWorkspaceSelection()
    {
        if (_syncingSelection || Graph is not { } graph)
        {
            return;
        }

        _syncingSelection = true;
        _workspace.Selection.Set([.. _canvas.Selected
            .Select(id => graph.Nodes.GetValueOrDefault(new GraphNodeKey(id)))
            .OfType<TaskGraphNode>()
            .Select(static n => n.Task)]);
        _syncingSelection = false;
    }

    // One entry in the graph list.
    private sealed class GraphRow(string key)
    {
        public string Key { get; } = key;

        public string Title { get; set; } = string.Empty;

        public GraphScope? Scope { get; set; }

        public string? AiPath { get; init; }

        public EventScope? AiScope { get; init; }

        public GraphRow? Parent { get; set; }

        public List<GraphRow> Children { get; } = [];
    }

    private sealed class EmptySource : INodeCanvasSource
    {
        public IReadOnlyList<CanvasNode> Nodes => [];

        public IReadOnlyList<CanvasWire> Wires => [];

        public IReadOnlyList<CanvasFrame> Frames => [];
    }
}
