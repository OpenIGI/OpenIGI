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
        if (_session.LevelRoot is not { } level)
        {
            return "No level is running.";
        }

        if (!ReferenceEquals(level, _liveEditsLevel))
        {
            _liveEdits.Clear();
            _liveEditsLevel = level;
        }

        GameTask? task = level;
        foreach ((int index, string type) in path)
        {
            task = task?.Children.ElementAtOrDefault(index);
            if (task is null || !string.Equals(task.Type.Name, type, StringComparison.Ordinal))
            {
                return "The running level is not the mission as the editor has it.";
            }
        }

        if (task is null || ReferenceEquals(task, level))
        {
            return "No task is named.";
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

    // The journal laid on the level again: after a checkpoint is put back, each changed task is given its values as they are now.
    private void ReapplyLiveEdits()
    {
        if (!ReferenceEquals(_session.LevelRoot, _liveEditsLevel))
        {
            _liveEdits.Clear();
            return;
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
