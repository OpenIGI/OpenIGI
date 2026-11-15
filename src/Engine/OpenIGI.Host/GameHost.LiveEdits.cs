using OpenIGI.Engine.Tasks;

namespace OpenIGI.Host;

/// <summary>
/// An editor's changes made in the level that is running, without loading it again: a task given new parameters as a load
/// would give them (<see cref="TaskScriptCommands.Rebind"/>), the game reading it again (a scene's shots, an animation's keys), and
/// the change kept in a journal that is laid on again after every checkpoint the level is put back to -- so going back never
/// brings an old value back, and the level always runs the mission as the editor now has it.
/// </summary>
/// <remarks>Not retail: the original has no editor.</remarks>
public sealed partial class GameHost
{
    // Each task changed since the level was loaded, and the compiled call that gives it its values now.
    private readonly Dictionary<GameTask, ReadOnlyMemory<byte>> _liveEdits = new(ReferenceEqualityComparer.Instance);

    // Each task whose children were changed since the level was loaded, and the children it has now, in order.
    private readonly Dictionary<GameTask, IReadOnlyList<GameTask>> _liveChildren = new(ReferenceEqualityComparer.Instance);
    private LevelTask? _liveEditsLevel;

    /// <summary>Gives a task of the level that is running new parameters.</summary>
    /// <param name="path">Where it is: each step a child index under the level (then under that child) and the type found there.</param>
    /// <param name="call">A compiled script whose one statement is the task's <c>Task_New</c>, without its children.</param>
    /// <param name="fromSceneSeconds">
    /// The earliest moment of the scene being previewed the change can act at: what the preview held from then on is let go and
    /// played again. Positive infinity for a change only the camera shows; null when it can act from the level's start.
    /// </param>
    /// <returns>Null when it was given; otherwise why not, and the level has to be loaded again to take it.</returns>
    internal string? ApplyLiveEdit(IReadOnlyList<(int Index, string Type)> path, ReadOnlyMemory<byte> call, double? fromSceneSeconds)
    {
        if (Find(path, out string? missing) is not { } task)
        {
            return missing;
        }

        if (_loop.TaskCommands.Rebind(task, call) is { } refused)
        {
            return refused;
        }

        _hosted.CutscenePreview?.Reread(task);
        _liveEdits[task] = call;
        _cutscenePreview?.Edited(fromSceneSeconds);
        return null;
    }

    /// <summary>
    /// Gives a task of the running level the children the editor now has under it: those it keeps matched in order to the ones
    /// it has of their types, new ones (a shot added) made from their compiled calls, those gone let go -- and keeps that order
    /// through every rewind.
    /// </summary>
    /// <param name="path">Where the task is, as <see cref="ApplyLiveEdit"/> takes it.</param>
    /// <param name="children">Its children in order: each one's type, and for one to make anew its compiled <c>Task_New</c>.</param>
    /// <param name="fromSceneSeconds">When in the previewed scene the change can first act, as <see cref="ApplyLiveEdit"/> takes it.</param>
    /// <returns>Null when the level took it; otherwise why not.</returns>
    internal string? ApplyLiveChildren(IReadOnlyList<(int Index, string Type)> path, IReadOnlyList<(string Type, ReadOnlyMemory<byte>? Values)> children, double? fromSceneSeconds)
    {
        if (Find(path, out string? missing) is not { } parent)
        {
            return missing;
        }

        // Those it keeps, matched in order to the ones it has of each type; then the new ones made.
        Dictionary<string, Queue<GameTask>> kept = new(StringComparer.Ordinal);
        foreach (GameTask child in parent.Children)
        {
            if (!kept.TryGetValue(child.Type.Name, out Queue<GameTask>? same))
            {
                kept[child.Type.Name] = same = new Queue<GameTask>();
            }

            same.Enqueue(child);
        }

        List<GameTask> order = [];
        foreach ((string type, ReadOnlyMemory<byte>? values) in children)
        {
            if (values is { } call)
            {
                if (_loop.TaskCommands.NewUnder(parent, call, out GameTask? made) is { } refused)
                {
                    return refused;
                }

                order.Add(made!);
            }
            else if (kept.TryGetValue(type, out Queue<GameTask>? same) && same.TryDequeue(out GameTask? existing))
            {
                order.Add(existing);
            }
            else
            {
                return "The running level is not the mission as the editor has it.";
            }
        }

        _loop.Tasks.SetChildren(parent, order);
        _liveChildren[parent] = order;
        _hosted.CutscenePreview?.Reread(parent);
        _cutscenePreview?.Edited(fromSceneSeconds);
        return null;
    }

    // The task a path names, under the level that is running; null, with why, where it names none.
    private GameTask? Find(IReadOnlyList<(int Index, string Type)> path, out string? why)
    {
        why = null;
        if (_session.LevelRoot is not { } level)
        {
            why = "No level is running.";
            return null;
        }

        if (!ReferenceEquals(level, _liveEditsLevel))
        {
            _liveEdits.Clear();
            _liveChildren.Clear();
            _liveEditsLevel = level;
        }

        GameTask? task = level;
        foreach ((int index, string type) in path)
        {
            task = task?.Children.ElementAtOrDefault(index);
            if (task is null || !string.Equals(task.Type.Name, type, StringComparison.Ordinal))
            {
                why = "The running level is not the mission as the editor has it.";
                return null;
            }
        }

        if (task is null || ReferenceEquals(task, level))
        {
            why = "No task is named.";
            return null;
        }

        return task;
    }

    // The journal laid on the level again: after a checkpoint is put back, each changed task is given its children and its values
    // as they are now.
    private void ReapplyLiveEdits()
    {
        if (!ReferenceEquals(_session.LevelRoot, _liveEditsLevel))
        {
            _liveEdits.Clear();
            _liveChildren.Clear();
            return;
        }

        foreach ((GameTask parent, IReadOnlyList<GameTask> order) in _liveChildren)
        {
            _loop.Tasks.SetChildren(parent, order);
            _hosted.CutscenePreview?.Reread(parent);
        }

        foreach ((GameTask task, ReadOnlyMemory<byte> call) in _liveEdits)
        {
            if (_loop.TaskCommands.Rebind(task, call) is null)
            {
                _hosted.CutscenePreview?.Reread(task);
            }
        }
    }
}
