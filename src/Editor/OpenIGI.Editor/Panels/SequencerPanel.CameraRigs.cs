using System.Globalization;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Cutscenes;
using OpenIGI.Editor.Core.Missions;

namespace OpenIGI.Editor.Panels;

/// <content>
/// Camera rigs -- rails, cranes and dolly tracks, as film crews have them: a rail clicked out on the ground in the viewport, a
/// crane or an orbit round the selected object -- each baked into plain keys the game reads, its settings kept so it can be changed
/// and its keys made again (<see cref="CutsceneRigs"/>).
/// </content>
internal sealed partial class SequencerPanel
{
    private const double RailHeight = 2;

    private List<Vector3d>? _railDraft;
    private MissionTask? _railSubject;

    /// <summary>Gets or sets the open mission's rigs, which the session makes.</summary>
    public CutsceneRigStore? Rigs { get; set; }

    /// <summary>Gets the rail being laid in the viewport, in world metres on the ground; null when none is.</summary>
    public IReadOnlyList<Vector3d>? RailDraft => _railDraft;

    /// <summary>Gets what the viewport's hint says while a rail is laid.</summary>
    public string RailHint => _railDraft is { } draft
        ? string.Create(CultureInfo.InvariantCulture, $"Rail: click the ground for each point ({draft.Count} so far){(_railSubject is { } s ? $", watching {TaskPresentation.Name(s)}" : string.Empty)}  ·  Enter makes it  ·  Backspace takes the last  ·  Esc stops")
        : string.Empty;

    /// <summary>Starts laying a rail in the viewport, watching the selected object when there is one.</summary>
    public void StartRail()
    {
        if (_scene is null)
        {
            return;
        }

        _railDraft = [];
        _railSubject = Subject();
        Notify?.Invoke("Click the ground in the viewport for the rail's points, then press Enter.", false);
    }

    /// <summary>Adds a point to the rail being laid.</summary>
    /// <param name="ground">Where the ground was clicked, in world metres.</param>
    public void AddRailPoint(Vector3d ground) => _railDraft?.Add(ground);

    /// <summary>Takes the rail's last point away.</summary>
    public void UndoRailPoint()
    {
        if (_railDraft is { Count: > 0 } draft)
        {
            draft.RemoveAt(draft.Count - 1);
        }
    }

    /// <summary>Stops laying a rail: made into keys, or dropped.</summary>
    /// <param name="make">Whether to make it.</param>
    public void EndRail(bool make)
    {
        if (_railDraft is not { } draft || _scene is not { } scene)
        {
            _railDraft = null;
            return;
        }

        _railDraft = null;
        if (!make)
        {
            return;
        }

        if (draft.Count < 2)
        {
            Notify?.Invoke("A rail needs two points at least.", true);
            return;
        }

        AddRig(scene, new CutsceneRig(CutsceneRigs.NewName(scene, CutsceneRigKind.Rail), CutsceneRigKind.Rail, scene.Id, _railSubject?.Id ?? -1, [.. draft], 8, 0, RailHeight, 0, 0, string.Empty));
    }

    /// <summary>Adds a crane or an orbit round the selected object, from the side the viewport's camera is on.</summary>
    /// <param name="kind">The kind.</param>
    public void AddSwing(CutsceneRigKind kind)
    {
        if (_scene is not { } scene || Subject() is not { } subject || TaskPresentation.PositionMetres(subject) is not { } centre)
        {
            return;
        }

        Vector3d from = _host.Camera?.Position ?? centre + new Vector3d(30, 0, 10);
        Vector3d away = from - centre;
        double reach = Math.Clamp(Math.Sqrt((away.X * away.X) + (away.Y * away.Y)), 8, 60);
        double heading = Math.Atan2(away.Y, away.X) * 180 / Math.PI;
        CutsceneRig rig = kind == CutsceneRigKind.Crane
            ? new(CutsceneRigs.NewName(scene, kind), kind, scene.Id, subject.Id, [], 8, reach, reach * 0.8, 90, heading, string.Empty)
            : new(CutsceneRigs.NewName(scene, kind), kind, scene.Id, subject.Id, [], 12, reach, reach * 0.35, 360, heading, string.Empty);
        AddRig(scene, rig);
    }

    /// <summary>Makes a rig's keys again from changed settings, in their place: one step with the settings kept.</summary>
    /// <param name="scene">The rig's scene.</param>
    /// <param name="rig">The rig's new settings.</param>
    public void Remake(MissionTask scene, CutsceneRig rig)
    {
        if (_workspace.Document is not { } document || Rigs?.Find(rig.SceneId, rig.Name) is not { } kept
            || CutsceneRigs.Place(document, scene, rig, SubjectAt(document, rig.SubjectId), null) is not { } placed)
        {
            return;
        }

        _workspace.Execute(new CompositeCommand("Change " + rig.Name, [placed.Step, Rigs.Change(kept, placed.Rig)]));
    }

    /// <summary>Lets a rig's keys go as plain keys: its settings forgotten, its keys kept as they are.</summary>
    /// <param name="rig">The rig.</param>
    public void Unrig(CutsceneRig rig)
    {
        if (Rigs is { } rigs)
        {
            _workspace.Execute(new CompositeCommand("Plain Keys from " + rig.Name, [rigs.Change(rig, null)]));
        }
    }

    // A new rig's keys after the key at the playhead (after the whole rig that key belongs to), its settings kept with them.
    private void AddRig(MissionTask scene, CutsceneRig rig)
    {
        if (_workspace.Document is not { } document || Rigs is not { } rigs)
        {
            return;
        }

        IReadOnlyList<CutsceneShot> shots = CutsceneModel.Shots(scene);
        MissionTask? after = shots.LastOrDefault(s => s.Start <= _time)?.Camera;
        if (after is { Label.Length: > 0 } && rigs.Of(scene, after) is { } holding)
        {
            after = CutsceneRigs.KeysOf(scene, holding.Name)[^1];
        }

        if (CutsceneRigs.Place(document, scene, rig, SubjectAt(document, rig.SubjectId), after) is not { } placed)
        {
            Notify?.Invoke("That rig makes no keys here.", true);
            return;
        }

        _workspace.Execute(new CompositeCommand("Add " + rig.Name, [placed.Step, rigs.Change(null, placed.Rig)]));
        if (CutsceneRigs.KeysOf(scene, rig.Name) is [var first, ..])
        {
            _workspace.Selection.Set(first);
        }

        Notify?.Invoke(string.Create(CultureInfo.InvariantCulture, $"Made {rig.Name}: its keys are plain keys the game reads; select one to change the rig in the Inspector."), false);
    }

    private static Vector3d? SubjectAt(MissionDocument document, int id) =>
        id >= 0 && document.EnumerateTasks().FirstOrDefault(t => t.Id == id) is { } subject ? TaskPresentation.PositionMetres(subject) : null;
}
