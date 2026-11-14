using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Cutscenes;
using OpenIGI.Host.Editor;

namespace OpenIGI.Editor.Panels;

/// <summary>
/// The sequencer played live: the scene run on the level itself by the game's own code (<see cref="IEditorCutscenePreview"/>),
/// its vehicles, soldiers and sounds with it, the viewport drawn through its camera. Playing and scrubbing forward run the level
/// on; going back loads it afresh and plays up to the moment, which takes a moment, so a drag behind the live moment shows the
/// camera's path until it is let go.
/// </summary>
internal sealed partial class SequencerPanel
{
    // Play as soon as the level is ready: asked for while it was being loaded or made ready.
    private bool _wantPlay;

    // The last step in the history when the live scene began: an edit since makes the level the scene plays on out of date.
    private IEditorCommand? _lastEditAtBegin;
    private int _editsAtBegin = -1;

    // A scrub behind the live moment: the camera's path is shown until it is let go, then the level is played up to it.
    private bool _draggingBack;

    /// <summary>
    /// Gets or sets what makes the level match the mission before it plays live: saved and loaded afresh when it has changes
    /// not yet saved, then the action given.
    /// </summary>
    public Action<Action>? PrepareLive { get; set; }

    /// <summary>Gets or sets what ends a live scene the mission was changed under: saved, and the level loaded afresh with it.</summary>
    public Action? LiveInterrupted { get; set; }

    private IEditorCutscenePreview? Live => _live.IsChecked ? _host.CutscenePreview : null;

    private bool LiveActive => Live is { Scene: { } id } && _scene is { } scene && scene.Id == id;

    /// <summary>Turns Live on or off, as its button does.</summary>
    /// <param name="on">Whether scenes play live.</param>
    public void SetLive(bool on)
    {
        if (_live.IsChecked == on || (on && _host.CutscenePreview is null))
        {
            return;
        }

        _live.IsChecked = on;
        if (!on)
        {
            _host.CutscenePreview?.Finish();
            _wantPlay = false;
            _draggingBack = false;
        }
    }

    /// <summary>Gets whether the scene is shown live, as the game plays it.</summary>
    public bool IsLive => LiveActive;

    // The scene begun live at a moment, the level saved and loaded afresh first when the mission has changed.
    private void StartLive(double at, bool play)
    {
        if (Live is not { } live || _scene is not { Id: >= 0 } scene)
        {
            return;
        }

        _wantPlay = play;
        void Begin()
        {
            if (!live.Begin(scene.Id))
            {
                Notify?.Invoke("This scene cannot be played live on the level.", true);
                return;
            }

            live.Seek(at);
            _editsAtBegin = _workspace.History.Done.Count;
            _lastEditAtBegin = _workspace.History.Done.Count > 0 ? _workspace.History.Done[^1] : null;
        }

        if (PrepareLive is { } prepare)
        {
            prepare(Begin);
        }
        else
        {
            Begin();
        }
    }

    private void EndLive()
    {
        if (Live is { Scene: not null } live)
        {
            live.Finish();
        }

        _wantPlay = false;
        _draggingBack = false;
        _editsAtBegin = -1;
    }

    private void LiveTogglePlay()
    {
        if (_scene is null)
        {
            return;
        }

        double length = CutsceneModel.Length(_scene);
        if (!LiveActive)
        {
            StartLive(_time >= length - 1e-3 ? 0 : _time, play: true);
            return;
        }

        IEditorCutscenePreview live = Live!;
        if (live.IsPlaying || _wantPlay)
        {
            live.IsPlaying = false;
            _wantPlay = false;
            return;
        }

        if (live.IsFinished || _time >= length - 1e-3)
        {
            live.Seek(0);
        }

        _wantPlay = true;
    }

    // The playhead dragged: forward runs the level on at once; back shows the camera's path until let go.
    private void ScrubTo(double seconds)
    {
        _time = Math.Max(0, seconds);
        if (Live is not null && !LiveActive && _scene is { Id: >= 0 })
        {
            StartLive(_time, play: false);
            return;
        }

        if (!LiveActive)
        {
            Look();
            return;
        }

        IEditorCutscenePreview live = Live!;
        live.IsPlaying = false;
        _wantPlay = false;
        if (!live.IsPreparing && _time >= live.Seconds - 1e-6)
        {
            _draggingBack = false;
            live.Seek(_time);
            return;
        }

        _draggingBack = true;
        Look();
    }

    // The drag let go: a moment behind the live one is played up to now.
    private void EndScrub()
    {
        if (_draggingBack && LiveActive)
        {
            Live!.Seek(_time);
        }

        _draggingBack = false;
    }

    /// <summary>Each frame: the playhead and the play button follow the live scene.</summary>
    /// <returns>Whether the scene is live, and so the frame is done.</returns>
    private bool UpdateLive()
    {
        if (Live is not { } live)
        {
            return false;
        }

        if (live.Scene is { } shown && _scene?.Id != shown)
        {
            // Another scene chosen: the live one ends, and its level is loaded afresh.
            EndLive();
            return false;
        }

        if (!LiveActive)
        {
            return false;
        }

        IEditorCommand? last = _workspace.History.Done.Count > 0 ? _workspace.History.Done[^1] : null;
        if (_workspace.History.Done.Count != _editsAtBegin || !ReferenceEquals(last, _lastEditAtBegin))
        {
            // The level the scene plays on is not the mission any more: saved, and loaded afresh with the change in it.
            _wantPlay = false;
            _draggingBack = false;
            _editsAtBegin = -1;
            if (LiveInterrupted is { } interrupted)
            {
                interrupted();
            }
            else
            {
                live.Finish();
            }

            Notify?.Invoke("The live scene stopped as the mission changed: it is saved, and Play shows the scene again with the change.", false);
            return false;
        }

        live.ShowsCamera = _preview.IsChecked && !_draggingBack;
        if (!_draggingBack)
        {
            _time = live.Seconds;
        }

        if (_wantPlay && !live.IsPreparing)
        {
            _wantPlay = false;
            live.IsPlaying = true;
        }

        _playing = live.IsPlaying || _wantPlay;
        if (live.IsPreparing || _draggingBack)
        {
            Look();
        }

        return true;
    }

    // What the timeline says of how the scene is shown.
    private string LiveStatus() =>
        Live is null ? "Camera only: the shots' path, not the level"
        : !LiveActive ? "Live: Play or scrub to run the scene on the level"
        : Live.IsPreparing ? "Loading the level to play it again up to here…"
        : _draggingBack ? "Let go to play the level up to here"
        : Live.WasForced ? "Live, made to play: the level's story does not start it from here"
        : "Live: the game's own result";
}
