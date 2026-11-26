using OpenIGI.Editor.Panels;
using OpenIGI.Host.Editor;
using OpenIGI.UI.Editor.Actions;

namespace OpenIGI.Editor;

/// <summary>The cutscene camera's keys: drawn and handled in the viewport, piloted, keyed, railed and rigged, from anywhere in the editor.</summary>
internal sealed partial class EditorSession
{
    // The viewport and the Inspector given the sequencer's camera: its rig drawn, its keys moved, piloted, railed and rigged.
    private void WireCutsceneCamera(IEditorHost host, InspectorPanel inspector)
    {
        _viewport.CameraPath = () => _dock.IsInFront("sequencer") && host.CutscenePreview is not { Scene: not null, ShowsCamera: true } ? _sequencer.Path() : null;
        _viewport.Letterbox = () => host.CutscenePreview is { Scene: not null, ShowsCamera: true, IsPreparing: false } live ? live.ViewportHeight : _sequencer.PilotLetterbox;
        _viewport.PilotBanner = () => _sequencer.PilotBanner;
        _viewport.CameraKeyPose = key => _dock.IsInFront("sequencer") ? _sequencer.KeyPose(key) : null;
        _viewport.MoveCameraKey = _sequencer.MoveKey;
        _viewport.CameraKeyPicked = _sequencer.PickKey;
        _viewport.RailDraft = () => _sequencer.RailDraft;
        _viewport.RailHint = () => _sequencer.RailHint;
        _viewport.RailPoint = _sequencer.AddRailPoint;
        _viewport.RailBack = _sequencer.UndoRailPoint;
        _viewport.RailEnd = _sequencer.EndRail;
        CutsceneRigStore rigs = new(_workspace, host);
        _sequencer.Rigs = rigs;
        inspector.Rigs = rigs;
        inspector.RemakeRig = _sequencer.Remake;
        inspector.Unrig = _sequencer.Unrig;
        _viewport.PilotWheel = _sequencer.PilotLens;
    }

    private void RegisterCameraActions(ActionTable actions)
    {
        actions.Add(new EditorAction("sequencer.pilot", "Pilot Camera Key", () =>
        {
            _dock.Show("sequencer");
            _sequencer.TogglePilot();
        })
        {
            Category = "Cutscene",
            Shortcut = KeyChord.Parse("Shift+P"),
            IsChecked = () => _sequencer.Piloting,
            CanExecute = () => _sequencer.Piloting || _sequencer.CanPilot,
        });
        actions.Add(new EditorAction("sequencer.pilot-stop", "Stop Piloting", _sequencer.StopPilot)
        {
            Category = "Cutscene",
            Shortcut = KeyChord.Parse("Esc"),
            CanExecute = () => _sequencer.Piloting,
        });
        actions.Add(new EditorAction("sequencer.rail-make", "Make the Rail", () => _sequencer.EndRail(true))
        {
            Category = "Cutscene",
            Shortcut = KeyChord.Parse("Return"),
            CanExecute = () => _sequencer.RailDraft is not null,
        });
        actions.Add(new EditorAction("sequencer.rail-stop", "Stop Laying the Rail", () => _sequencer.EndRail(false))
        {
            Category = "Cutscene",
            Shortcut = KeyChord.Parse("Esc"),
            CanExecute = () => _sequencer.RailDraft is not null,
        });
        actions.Add(new EditorAction("sequencer.rail-back", "Take the Rail's Last Point", _sequencer.UndoRailPoint)
        {
            Category = "Cutscene",
            Shortcut = KeyChord.Parse("Backspace"),
            CanExecute = () => _sequencer.RailDraft is not null,
        });
        actions.Add(new EditorAction("sequencer.pilot-previous", "Pilot the Key Before", () => _sequencer.PilotNext(-1))
        {
            Category = "Cutscene",
            Shortcut = KeyChord.Parse("LeftBracket"),
            CanExecute = () => _sequencer.Piloting,
        });
        actions.Add(new EditorAction("sequencer.pilot-next", "Pilot the Key After", () => _sequencer.PilotNext(1))
        {
            Category = "Cutscene",
            Shortcut = KeyChord.Parse("RightBracket"),
            CanExecute = () => _sequencer.Piloting,
        });
        actions.Add(new EditorAction("sequencer.add-key", "Add Camera Key at the Playhead", _sequencer.AddKeyAtPlayhead)
        {
            Category = "Cutscene",
            Shortcut = KeyChord.Parse("Shift+K"),
            CanExecute = () => _sequencer.CanPilot,
        });
        actions.Add(new EditorAction("sequencer.key-from-view", "Key Camera from View", _sequencer.KeyFromView)
        {
            Category = "Cutscene",
            Shortcut = KeyChord.Parse("K"),
            CanExecute = () => _sequencer.CanPilot,
        });
    }
}
