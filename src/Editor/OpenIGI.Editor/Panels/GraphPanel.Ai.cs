using System.Globalization;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Editor.Core.Graphs;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Qvm;

namespace OpenIGI.Editor.Panels;

/// <content>
/// The mission's AI scripts, under the logic in the list: one entry a script (named after the soldier it drives), one graph
/// an event under it. Edits are undoable commands; saving the mission compiles each edited script into the project.
/// </content>
internal sealed partial class GraphPanel
{
    private const string LocalDevice = "LOCAL:";

    private readonly Dictionary<string, AiScript> _aiScripts = new(StringComparer.OrdinalIgnoreCase);
    private string? _aiListedFor;
    private AiScript? _aiOpen;
    private EventScope? _aiScope;
    private EventCanvasSource? _ai;

    // Reads the mission's ai folder when the mission changes: each script decompiled and split into its events.
    private void LoadAiScripts()
    {
        string? directory = _workspace.Mission?.MissionDirectory;
        if (directory == _aiListedFor)
        {
            return;
        }

        _aiListedFor = directory;
        _aiScripts.Clear();
        CloseAi();
        if (directory is null)
        {
            return;
        }

        IEnumerable<string> paths;
        try
        {
            paths = [.. _host.Files.EnumerateFiles(LocalDevice + directory.TrimEnd('/', '\\') + "/ai", "*.qvm").Select(static p => p.Replace('\\', '/'))];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (string path in paths.Where(static p => !p.EndsWith("/settings.qvm", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!_host.TryReadFile(path, out ReadOnlyMemory<byte> image) || !QvmProgram.IsQvm(image.Span))
            {
                continue;
            }

            try
            {
                QscScript script = QscDecompiler.Decompile(image);
                EventGraph graph = EventGraph.Read(script, _host.Vocabulary);
                GraphLayout layout = ReadAiLayout(path[LocalDevice.Length..], script);
                AiScript ai = new(path, NameOf(path), graph, EventGraphView.Scopes(graph), layout);
                layout.Changed += () =>
                {
                    ai.IsLayoutDirty = true;
                    _stale = true;
                };
                _aiScripts[path] = ai;
            }
            catch (Exception error) when (error is QvmFormatException or QscDecompileException or ArgumentException)
            {
                _host.Logger.Warning("Editor", $"{path} is not an AI script the graph can read: {error.Message}");
            }
        }
    }

    /// <summary>The key a saved AI layout keeps the hash of its script's text under.</summary>
    private const string AiScriptHashKey = "script";

    // An AI script's saved layout, kept only when it was saved for this very script: one changed since (in the Script panel,
    // by a mod) reads afresh rather than putting old places on different nodes.
    private GraphLayout ReadAiLayout(string gamePath, QscScript script)
    {
        try
        {
            if (_workspace.Project.ReadGraph(gamePath) is { } bytes)
            {
                GraphLayout saved = GraphLayout.Read(bytes);
                if (saved.TextHashes.TryGetValue(AiScriptHashKey, out ulong hash) && hash == GraphLayout.HashOf(QscPrinter.Print(script)))
                {
                    return saved;
                }
            }
        }
        catch (Exception error) when (error is IOException or System.Text.Json.JsonException or FormatException)
        {
            _host.Logger.Warning("Editor", $"The saved layout of {gamePath} could not be read: {error.Message}");
        }

        return new GraphLayout();
    }

    // The list's AI section: a heading, a row a script, a row an event.
    private void AppendAiRows()
    {
        LoadAiScripts();
        if (_aiScripts.Count == 0)
        {
            return;
        }

        GraphRow heading = new("ai") { Title = "AI Scripts" };
        foreach (AiScript script in _aiScripts.Values)
        {
            GraphRow file = new("ai:" + script.Path) { Title = script.Name, Parent = heading, AiPath = script.Path };
            bool scriptMatches = _query.Length == 0 || script.Name.Contains(_query, StringComparison.OrdinalIgnoreCase);
            foreach (EventScope scope in script.Scopes.Where(s => scriptMatches || Matches(script, s)))
            {
                file.Children.Add(new GraphRow($"ai:{script.Path}|{scope.Key}") { Title = scope.Title, Parent = file, AiPath = script.Path, AiScope = scope });
            }

            if (file.Children.Count > 0)
            {
                heading.Children.Add(file);
            }
        }

        if (heading.Children.Count > 0)
        {
            _roots.Add(heading);
        }
    }

    // An event matches the find text by its name, or by any statement or value in it: a native's name, an event, a literal.
    private bool Matches(AiScript script, EventScope scope) =>
        scope.Title.Contains(_query, StringComparison.OrdinalIgnoreCase)
        || scope.Members.Any(key => script.Graph.Nodes[key] switch
        {
            EventStatementNode statement => statement.Text.Contains(_query, StringComparison.OrdinalIgnoreCase),
            EventValueNode value => value.Text.Contains(_query, StringComparison.OrdinalIgnoreCase),
            _ => false,
        });

    // A script named for what it drives: the task its id names ("Guard 23"), else its file name.
    private string NameOf(string path)
    {
        string file = Path.GetFileNameWithoutExtension(path);
        if (int.TryParse(file, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
            && _workspace.Document?.EnumerateTasks().FirstOrDefault(t => t.Id == id) is { } task)
        {
            return $"{TaskPresentation.Name(task)} · {file}.qvm";
        }

        return file + ".qvm";
    }

    // An AI graph by name: "<script> <event>", matched against "Guard · 23.qvm On See Enemy"; the first whose name holds it.
    private bool OpenAiNamed(string name)
    {
        LoadAiScripts();
        foreach (AiScript script in _aiScripts.Values)
        {
            foreach (EventScope scope in script.Scopes)
            {
                if ($"{script.Name} {scope.Title}".Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    OpenAi(script.Path, scope);
                    return true;
                }
            }
        }

        return false;
    }

    private void OpenAi(string path, EventScope scope)
    {
        if (!_aiScripts.TryGetValue(path, out AiScript? script) || (ReferenceEquals(_aiOpen, script) && _aiScope == scope))
        {
            return;
        }

        Arrive(new GraphPlace(null, path, scope.Key));
        _aiOpen = script;
        _aiScope = scope;
        _stale = true;
        _frameWanted = true;
        _list.Invalidate();
    }

    private void CloseAi()
    {
        _aiOpen = null;
        _aiScope = null;
        _ai = null;
    }

    // The open AI graph, built again when it changes: returns whether one is open.
    private bool RefreshAi()
    {
        if (_aiOpen is not { } script || _aiScope is not { } scope)
        {
            _ai = null;
            return false;
        }

        // An edit splits the script again; the open event is found again by its key.
        scope = script.Scopes.FirstOrDefault(s => s.Key == scope.Key) ?? script.Scopes[0];
        _aiScope = scope;
        EventGraphView.Place(script.Graph, scope, script.Layout);
        _ai = new EventCanvasSource(script.Graph, scope, script.Layout, Theme);
        _canvas.Source = _ai;
        return true;
    }


    private string AiBreadcrumb() => _aiOpen is { } script && _aiScope is { } scope ? $"AI Scripts  ›  {script.Name}  ›  {scope.Title}" : string.Empty;

    // One AI script: where it is, what it is called, its graph, its events and where their nodes sit.
    private sealed class AiScript(string path, string name, EventGraph graph, IReadOnlyList<EventScope> scopes, GraphLayout layout)
    {
        public string Path { get; } = path;

        public string Name { get; } = name;

        public EventGraph Graph { get; } = graph;

        public IReadOnlyList<EventScope> Scopes { get; set; } = scopes;

        public GraphLayout Layout { get; } = layout;

        /// <summary>Gets or sets a value indicating whether its nodes were moved or commented since its layout was saved.</summary>
        public bool IsLayoutDirty { get; set; }

        /// <summary>Gets or sets a value indicating whether it has edits the project does not have yet.</summary>
        public bool IsDirty { get; set; }
    }
}
