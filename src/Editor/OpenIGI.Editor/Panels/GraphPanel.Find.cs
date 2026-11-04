using System.Globalization;
using OpenIGI.Editor.Core.Graphs;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Kit;

namespace OpenIGI.Editor.Panels;

/// <summary>Find in Graph: every task of the mission's graphs, searched by name, type, id or what it says.</summary>
internal sealed partial class GraphPanel
{
    // Ctrl+F: a palette of every task the mission's graphs hold, each opening its own graph with it selected.
    private void OpenFind()
    {
        if (Root is not { } root || Graph is not { } graph)
        {
            return;
        }

        IReadOnlyList<GraphScope> scopes = _workspace.Logic.Scopes;
        List<PaletteEntry> entries = [];
        foreach (TaskGraphNode node in graph.TaskNodes)
        {
            if (GraphScopes.HomeScope(scopes, node.Key) is not { } home)
            {
                continue;
            }

            // What it says, when it says something: a message's line, a condition's text.
            IReadOnlyList<TaskNodeDetail> details = TaskNodeDetails.For(node, _host.MissionText);
            string says = details.Count > 0 ? details[0].Value : string.Empty;
            string id = node.Task.Id >= 0 ? $" #{node.Task.Id.ToString(CultureInfo.InvariantCulture)}" : string.Empty;
            string key = node.Key.Value;
            entries.Add(new PaletteEntry(MissionCanvasSource.Title(node), () => Reveal(home, key))
            {
                Detail = says.Length > 0 ? $"{node.Task.TypeName}{id} · {home.Title} · {says}" : $"{node.Task.TypeName}{id} · {home.Title}",
                Icon = KitIcon.Graph,
            });
        }

        CommandPalette.Open(root, [.. entries.OrderBy(static e => e.Title, StringComparer.OrdinalIgnoreCase)], "Find in graph");
    }

    // Opens a graph with one of its nodes selected and in view.
    private void Reveal(GraphScope scope, string node)
    {
        CloseAi();
        _stale = true;
        Open(scope.Key);
        if (_rows.GetValueOrDefault(scope.Key) is { } row)
        {
            _listSelection.Set(row);
            _list.Reveal(row);
        }

        Refresh();
        _frameWanted = false;
        _canvas.Select([node]);
        _canvas.Frame([node]);
        LeadWorkspaceSelection();
    }
}
