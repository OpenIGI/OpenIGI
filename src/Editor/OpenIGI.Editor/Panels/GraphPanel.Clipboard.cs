using System.Globalization;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Graphs;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Scripting.Qsc;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>
/// Copy, paste and duplicate on a mission graph: the tasks go through the editor's own clipboard text, so what is copied
/// here pastes in the Outliner too, and come back as new tasks with free ids whose conditions still read each other.
/// </summary>
internal sealed partial class GraphPanel
{
    // Where the copied tasks stood, by their old ids, so a paste lays them out as they were.
    private readonly Dictionary<int, GraphPoint> _copiedAt = [];

    private void Copy(IReadOnlyCollection<string> ids)
    {
        if (_ai is not null)
        {
            _notify("Copy and paste work on mission graphs; an AI script's nodes are edited in place.", false);
            return;
        }

        if (_workspace.Document is not { } document || Copied(ids) is not { Length: > 0 } tops)
        {
            return;
        }

        _copiedAt.Clear();
        foreach (MissionTask task in tops.SelectMany(static t => t.SelfAndSubtree()).Where(static t => t.Id >= 0))
        {
            if (_workspace.Logic.Layout.Positions.TryGetValue(KeyOf(task.Id), out GraphPoint at))
            {
                _copiedAt[task.Id] = at;
            }
        }

        _host.ClipboardText = document.WriteTasks(tops);
        _notify(tops.Length == 1 ? $"Copied {tops[0]}." : $"Copied {tops.Length} tasks.", false);
    }

    private void Paste(UiVec2 at)
    {
        if (_ai is not null || _workspace.Document is not { } document)
        {
            return;
        }

        PastedTasks pasted;
        IReadOnlyDictionary<int, int> original;
        try
        {
            pasted = document.ReadTasksAsNew(_host.ClipboardText ?? string.Empty, out original);
        }
        catch (QscParseException)
        {
            _notify("The clipboard does not hold tasks.", true);
            return;
        }

        if (pasted.Tasks.Count == 0)
        {
            _notify(pasted.Skipped.Count > 0 ? $"This mission has no {string.Join(", ", pasted.Skipped.Distinct())} to paste." : "The clipboard does not hold tasks.", true);
            return;
        }

        // Laid out as they were copied, the group's top-left corner at the point; tasks never placed stack beside it.
        GraphPoint[] known = [.. original.Values.Where(_copiedAt.ContainsKey).Select(o => _copiedAt[o])];
        double minX = known.Length > 0 ? known.Min(static p => p.X) : 0, minY = known.Length > 0 ? known.Min(static p => p.Y) : 0;
        int stacked = 0;
        foreach (MissionTask task in pasted.Tasks.SelectMany(static t => t.SelfAndSubtree()).Where(static t => t.Id >= 0))
        {
            GraphPoint place = original.TryGetValue(task.Id, out int old) && _copiedAt.TryGetValue(old, out GraphPoint was)
                ? new GraphPoint(at.X + was.X - minX, at.Y + was.Y - minY)
                : new GraphPoint(at.X + (40 * stacked), at.Y + (40 * stacked++));
            _workspace.Logic.Layout.Positions[KeyOf(task.Id)] = place;
        }

        // Into the open graph's container, as a new task goes, so it is drawn where it was put.
        MissionTask? holder = _source?.Scope.Container is { } container && Graph?.Nodes.GetValueOrDefault(container) is TaskGraphNode node
            ? node.Task
            : document.RootTasks.FirstOrDefault();
        TaskLocation location = new(holder, holder?.Children.Count ?? document.Statements.Count);
        using (_workspace.History.BeginTransaction(pasted.Tasks.Count == 1 ? $"Paste {pasted.Tasks[0]}" : $"Paste {pasted.Tasks.Count} tasks"))
        {
            foreach (MissionTask task in pasted.Tasks)
            {
                _workspace.Execute(new InsertTaskCommand(document, task, location));
                location = location with { Index = location.Index + 1 };
            }
        }

        _workspace.Selection.Set(pasted.Tasks);
        if (pasted.Skipped.Count > 0)
        {
            _notify($"Pasted {pasted.Tasks.Count}; left out what this mission does not declare ({string.Join(", ", pasted.Skipped.Distinct())}).", false);
        }
    }

    // Ctrl+D: a copy of the selection just beside it.
    private void Duplicate(IReadOnlyCollection<string> ids)
    {
        if (_ai is not null || Copied(ids) is not { Length: > 0 })
        {
            return;
        }

        Copy(ids);
        if (_copiedAt.Count == 0)
        {
            return;
        }

        float offset = 40f;
        Paste(new UiVec2((float)_copiedAt.Values.Min(static p => p.X) + offset, (float)_copiedAt.Values.Min(static p => p.Y) + offset));
    }

    // The selected tasks, without any that sits inside another selected one: those go with it.
    private MissionTask[]? Copied(IReadOnlyCollection<string> ids)
    {
        if (Graph is not { } graph)
        {
            return null;
        }

        List<MissionTask> tasks = [.. ids.Select(id => graph.Nodes.GetValueOrDefault(new GraphNodeKey(id))).OfType<TaskGraphNode>().Select(static n => n.Task)];
        return [.. tasks.Where(task => !tasks.Any(other => !ReferenceEquals(other, task) && Within(task, other)))];
    }

    private static bool Within(MissionTask task, MissionTask ancestor)
    {
        for (MissionTask? parent = task.Parent; parent is not null; parent = parent.Parent)
        {
            if (ReferenceEquals(parent, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static GraphNodeKey KeyOf(int id) => new(string.Create(CultureInfo.InvariantCulture, $"t{id}"));
}
