using OpenIGI.UI.Editor.Actions;

namespace OpenIGI.Editor;

/// <summary>The cutscene camera's keys: piloting one with the viewport, and keying the view, from anywhere in the editor.</summary>
internal sealed partial class EditorSession
{
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
        actions.Add(new EditorAction("sequencer.key-from-view", "Key Camera from View", _sequencer.KeyFromView)
        {
            Category = "Cutscene",
            Shortcut = KeyChord.Parse("K"),
            CanExecute = () => _sequencer.CanPilot,
        });
    }
}
