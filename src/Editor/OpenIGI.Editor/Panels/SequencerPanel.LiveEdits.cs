using OpenIGI.Core.Diagnostics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Cutscenes;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Host.Editor;

namespace OpenIGI.Editor.Panels;

/// <summary>
/// Changes made while a scene plays live go into the running level as they are made (<see cref="IEditorHost.ApplyLiveEdit"/>):
/// each changed task given its new values the way a load of the mission would give them, the scene played again from before
/// the moment the change can first act -- not at all for a change only the camera shows -- so rewinds and play stay what the
/// saved mission plays. Shots added, taken out or reordered go in the same way. A change the level cannot take so (any other task
/// added, removed or moved; one the game does not read again) still saves the mission and loads the level with it.
/// </summary>
internal sealed partial class SequencerPanel
{
    // The camera's parameters that only change what the camera shows, never what happens in the level.
    private static readonly HashSet<string> ViewOnly = new(StringComparer.Ordinal)
    {
        "Position", "Alpha", "Beta", "Gamma", "FOV", "Smooth to next", "Link task ID", "Target task ID",
        "Update link continously", "Update target continously", "Time of day (-1 means use default)", "FILTER", "Noise",
        "Filter color", "Camera shake",
    };

    private readonly List<MissionChange> _changes = [];
    private MissionDocument? _watched;

    // When each cue and animation of the scene started, as last seen: a change's earlier moment is where it can first act.
    private readonly Dictionary<(MissionTask Task, string Parameter), double> _cueSeconds = [];
    private readonly Dictionary<MissionTask, double> _animationStarts = new(ReferenceEqualityComparer.Instance);

    // The scene's shot cameras as last seen, and when each shot started: a shot added or taken out changes the scene from there.
    private readonly Dictionary<MissionTask, double> _shotStarts = new(ReferenceEqualityComparer.Instance);

    // The level load and the edit the running level last held the document at.
    private int _inStepLoad = -1;
    private int _inStepCount = -1;
    private IEditorCommand? _inStepLast;

    /// <summary>Gets whether the running level holds the mission exactly as the editor now has it, saved or not.</summary>
    public bool LevelHoldsDocument =>
        _inStepLoad == _host.LevelLoads
        && _inStepCount == _workspace.History.Done.Count
        && ReferenceEquals(_inStepLast, _workspace.History.Done.Count > 0 ? _workspace.History.Done[^1] : null);

    // The running level holds the document as it is now.
    private void MarkInStep()
    {
        _inStepLoad = _host.LevelLoads;
        _inStepCount = _workspace.History.Done.Count;
        _inStepLast = _workspace.History.Done.Count > 0 ? _workspace.History.Done[^1] : null;
    }

    // Follows the document the workspace has open, noting each change to it.
    private void WatchDocument()
    {
        if (ReferenceEquals(_workspace.Document, _watched))
        {
            return;
        }

        if (_watched is not null)
        {
            _watched.Changed -= _changes.Add;
        }

        _watched = _workspace.Document;
        _changes.Clear();
        _cueSeconds.Clear();
        _animationStarts.Clear();
        _shotStarts.Clear();
        if (_watched is not null)
        {
            _watched.Changed += _changes.Add;
        }
    }

    // Remembers when the scene's cues and animations stand now, for the next change to be measured from.
    private void NoteTimes(MissionDocument document, MissionTask scene)
    {
        _cueSeconds.Clear();
        foreach (CutsceneCue cue in CutsceneModel.Cues(document, scene))
        {
            _cueSeconds[(cue.Task, cue.Parameter)] = cue.Seconds;
        }

        _animationStarts.Clear();
        foreach (CutsceneAnimation animation in CutsceneModel.Animations(document, scene))
        {
            _animationStarts[animation.Task] = animation.Start;
        }

        _shotStarts.Clear();
        foreach (CutsceneShot shot in CutsceneModel.Shots(scene))
        {
            _shotStarts[shot.Camera] = shot.Start;
        }
    }

    /// <summary>The changes made since the last frame given to the running level.</summary>
    /// <returns>Whether the level took them all; false when it has to be loaded again with them.</returns>
    private bool ApplyChanges()
    {
        if (_changes.Count == 0 || _workspace.Document is not { } document || _scene is not { } scene)
        {
            return true;
        }

        MissionChange[] changes = [.. _changes];
        _changes.Clear();
        MissionChange[] structural = [.. changes.Where(static c => c.Kind != MissionChangeKind.Parameter)];
        if (structural.Length > 0 && !ApplyShotChanges(document, scene, structural))
        {
            return false;
        }

        foreach (IGrouping<MissionTask, MissionChange> task in changes.Where(static c => c.Kind == MissionChangeKind.Parameter)
            .GroupBy(static c => c.Task, ReferenceEqualityComparer.Instance as IEqualityComparer<MissionTask>))
        {
            if (!document.Contains(task.Key))
            {
                return false;
            }

            double? from = ActsFrom(document, scene, task.Key, [.. task.Select(static c => c.Parameter ?? string.Empty)]);
            if (_host.ApplyLiveEdit(document.LevelPath(task.Key), document.CompileTaskValues(task.Key), from) is { } refused)
            {
                _host.Logger.Information("Editor", $"The running level could not take the change to {task.Key.TypeName} {task.Key.Label}: {refused}");
                return false;
            }
        }

        NoteTimes(document, scene);
        MarkInStep();
        return true;
    }

    // Shots added, taken out or moved within the scene: the scene's children given to the running level as they are now -- each shot's
    // camera made afresh from its values, the rest kept -- from the earliest shot changed on. Anything else added, removed or moved
    // (or a shot nested deeper, or with tasks under it) is not taken this way.
    private bool ApplyShotChanges(MissionDocument document, MissionTask scene, MissionChange[] structural)
    {
        static bool Shot(MissionTask task) => task.TypeName == CutsceneModel.CameraType && task.Children.Count == 0;
        foreach (MissionChange change in structural)
        {
            bool ours = change.Kind == MissionChangeKind.Removed ? _shotStarts.ContainsKey(change.Task) : ReferenceEquals(change.Task.Parent, scene);
            if (change.Kind == MissionChangeKind.Identity || !Shot(change.Task) || !ours)
            {
                return false;
            }
        }

        if (scene.SelfAndSubtree().Any(t => t.TypeName == CutsceneModel.CameraType && !ReferenceEquals(t.Parent, scene)))
        {
            return false;
        }

        IReadOnlyList<CutsceneShot> now = CutsceneModel.Shots(scene);
        double from = double.MaxValue;
        foreach (MissionChange change in structural)
        {
            if (_shotStarts.TryGetValue(change.Task, out double was))
            {
                from = Math.Min(from, was);
            }

            if (now.FirstOrDefault(s => ReferenceEquals(s.Camera, change.Task)) is { } shot)
            {
                from = Math.Min(from, shot.Start);
            }
        }

        List<(string, ReadOnlyMemory<byte>?)> children = [.. scene.Children.Select(child =>
            (child.TypeName, child.TypeName == CutsceneModel.CameraType ? (ReadOnlyMemory<byte>?)document.CompileTaskValues(child) : null))];
        if (_host.ApplyLiveChildren(document.LevelPath(scene), children, from == double.MaxValue ? 0 : from) is { } refused)
        {
            _host.Logger.Information("Editor", $"The running level could not take the scene's new shots: {refused}");
            return false;
        }

        return true;
    }

    // When in the scene a change can first act: never, for what only the camera shows; a shot's length from the shot on; a cue or    // an animation from the earlier of where it was and where it is; the scene's own settings from its start; anything else from
    // the level's start.
    private double? ActsFrom(MissionDocument document, MissionTask scene, MissionTask task, string[] parameters)
    {
        if (task.TypeName == CutsceneModel.CameraType && scene.SelfAndSubtree().Contains(task))
        {
            if (parameters.All(ViewOnly.Contains))
            {
                return double.PositiveInfinity;
            }

            if (parameters.All(static p => ViewOnly.Contains(p) || p == CutsceneModel.DurationParameter)
                && CutsceneModel.Shots(scene).FirstOrDefault(s => ReferenceEquals(s.Camera, task)) is { } shot)
            {
                return shot.Start;
            }

            return 0;
        }

        if (ReferenceEquals(task, scene))
        {
            return 0;
        }

        double? earliest = null;
        foreach (string parameter in parameters)
        {
            double? was = _cueSeconds.TryGetValue((task, parameter), out double before) ? before : null;
            double? now = CutsceneModel.Cues(document, scene).FirstOrDefault(c => ReferenceEquals(c.Task, task) && c.Parameter == parameter)?.Seconds;
            if (was is null && now is null)
            {
                earliest = null;
                break;
            }

            double at = Math.Min(was ?? double.MaxValue, now ?? double.MaxValue);
            earliest = Math.Min(earliest ?? double.MaxValue, at);
        }

        if (earliest is null && _animationStarts.TryGetValue(task, out double started))
        {
            double now = CutsceneModel.Animations(document, scene).FirstOrDefault(a => ReferenceEquals(a.Task, task))?.Start ?? started;
            return Math.Min(started, now);
        }

        return earliest;
    }
}
