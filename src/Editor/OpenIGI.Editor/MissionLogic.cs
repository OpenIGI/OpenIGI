using OpenIGI.Core.Diagnostics;
using OpenIGI.Editor.Core.Graphs;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Editor.Core.Projects;
using OpenIGI.Host.Editor;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Qsc;

namespace OpenIGI.Editor;

/// <summary>
/// The open mission's logic graph: built from the document whenever it changes, laid out from the project's sidecar, and
/// saved beside the mission.
/// </summary>
/// <remarks>
/// The graph is a view; this only keeps it current and keeps its layout. Edits made in the graph panel are document commands,
/// so the document changing is the one signal that the graph must be built again.
/// </remarks>
internal sealed class MissionLogic(IEditorHost host)
{
    private MissionDocument? _document;
    private MissionGraph? _graph;
    private TaskCatalogue? _catalogue;
    private IReadOnlyList<GraphScope> _scopes = [];

    /// <summary>Raised when the graph or its layout changes.</summary>
    public event Action? Changed;

    /// <summary>Gets the layout: positions, comments, frames and action nodes.</summary>
    public GraphLayout Layout { get; private set; } = new();

    /// <summary>Gets which game's expression rules the mission's text follows.</summary>
    public LevelExpressionDialect Dialect => Equals(host.Title, global::OpenIGI.Core.Titles.GameTitle.Igi2)
        ? LevelExpressionDialect.Igi2
        : LevelExpressionDialect.Igi1;

    /// <summary>Gets the graph split into small graphs, one for each container of logic, in mission order.</summary>
    public IReadOnlyList<GraphScope> Scopes => Graph is null ? [] : _scopes;

    /// <summary>Gets the task types as nodes, for the palette.</summary>
    public TaskCatalogue? Catalogue => _catalogue;

    /// <summary>Gets the graph as the document stands, building it if the document changed since.</summary>
    public MissionGraph? Graph
    {
        get
        {
            if (_graph is null && _document is { } document)
            {
                _catalogue ??= TaskCatalogue.Build(document.Declarations.Values, host.Vocabulary);
                _graph = MissionGraph.Build(document, _catalogue, host.Vocabulary, Dialect);
                _scopes = GraphScopes.Build(_graph, Layout.Positions.ContainsKey);
            }

            return _graph;
        }
    }

    /// <summary>Opens a mission's graph, with the layout its project kept for it.</summary>
    /// <param name="project">The project.</param>
    /// <param name="gamePath">The mission's objects script.</param>
    /// <param name="document">The mission.</param>
    public void Load(EditorProject project, string gamePath, MissionDocument document)
    {
        _document = document;
        _graph = null;
        _catalogue = null;
        Layout = Read(project, gamePath, host);
        Layout.Changed += () => Changed?.Invoke();
        if (Graph is { } graph)
        {
            Layout.Forget(graph, CurrentText(graph));
        }

        Changed?.Invoke();
    }

    /// <summary>Drops the graph: no mission is open.</summary>
    public void Clear()
    {
        _document = null;
        _graph = null;
        _catalogue = null;
        Layout = new GraphLayout();
        Changed?.Invoke();
    }

    /// <summary>Marks the graph stale after an edit; it is built again when next asked for.</summary>
    public void Invalidate()
    {
        _graph = null;
        _catalogue = null;
        Changed?.Invoke();
    }

    /// <summary>Saves the layout beside the mission, remembering each expression's text so a later outside edit is noticed.</summary>
    /// <param name="project">The project.</param>
    /// <param name="gamePath">The mission's objects script.</param>
    public void Save(EditorProject project, string gamePath)
    {
        if (Graph is not { } graph)
        {
            return;
        }

        Layout.TextHashes.Clear();
        foreach ((string owner, string text) in CurrentText(graph))
        {
            Layout.TextHashes[owner] = GraphLayout.HashOf(text);
        }

        project.WriteGraph(gamePath, Layout.Write());
    }

    private static GraphLayout Read(EditorProject project, string gamePath, IEditorHost host)
    {
        if (project.ReadGraph(gamePath) is not { } bytes)
        {
            return new GraphLayout();
        }

        try
        {
            return GraphLayout.Read(bytes);
        }
        catch (Exception error) when (error is InvalidDataException or System.Text.Json.JsonException or KeyNotFoundException)
        {
            host.Logger.Warning("Editor", $"The graph layout for {gamePath} could not be read, so it is laid out afresh: {error.Message}");
            return new GraphLayout();
        }
    }

    // Each expression parameter's text now, by "task/parameter" as the layout keys it.
    private static Dictionary<string, string> CurrentText(MissionGraph graph)
    {
        Dictionary<string, string> text = new(StringComparer.Ordinal);
        foreach (((GraphNodeKey task, string parameter), GraphPinRef _) in graph.ExpressionRoots)
        {
            if (graph.Nodes[task] is TaskGraphNode node && node.Task.FindParameter(parameter) is { Arguments: [QscStringLiteral literal] })
            {
                text[$"{task.Value}/{parameter}"] = literal.Value;
            }
        }

        return text;
    }
}
