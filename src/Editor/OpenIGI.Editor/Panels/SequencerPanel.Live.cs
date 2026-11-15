using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Cutscenes;
using OpenIGI.Host.Editor;

namespace OpenIGI.Editor.Panels;

/// <summary>
/// The sequencer played live: the scene run on the level itself by the game's own code (<see cref="IEditorCutscenePreview"/>),
/// its vehicles, soldiers and sounds with it, the viewport drawn through its camera. Playing and scrubbing forward run the level
/// on; going back puts it back from memory where it stood and plays on to the moment, at once, so the playhead is followed either way.
/// </summary>
internal sealed partial class SequencerPanel
{
    // Play as soon as the level is ready: asked for while it was being loaded or made ready.
    private bool _wantPlay;

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

            // Begun on a level that holds the mission as it is now: changes from here go into it as they are made.
            WatchDocument();
            _changes.Clear();
            MarkInStep();
            if (_workspace.Document is { } document)
            {
                NoteTimes(document, scene);
            }

            live.Seek(at);
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

    // The playhead dragged: the level follows it either way at once -- on by playing, back by putting it back from memory.
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
        if (!live.IsPreparing)
        {
            live.Seek(_time);
        }
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
            // Not live: what is changed now reaches the level as the mission is saved and loaded for the next scene.
            _changes.Clear();
            return false;
        }

        // What was changed since the last frame goes into the running level; what it cannot take saves the mission and loads it.
        WatchDocument();
        if (!ApplyChanges())
        {
            _wantPlay = false;
            if (LiveInterrupted is { } interrupted)
            {
                interrupted();
            }
            else
            {
                live.Finish();
            }

            Notify?.Invoke("The change could not go into the running level: the mission is saved and the level loaded with it.", false);
            return false;
        }
        live.ShowsCamera = _preview.IsChecked;
        _time = live.Seconds;

        if (_wantPlay && !live.IsPreparing)
        {
            _wantPlay = false;
            live.IsPlaying = true;
        }

        _playing = live.IsPlaying || _wantPlay;
        if (live.IsPreparing)
        {
            Look();
        }

        return true;
    }

    // What the timeline says of how the scene is shown.
    private string LiveStatus() =>
        Live is null ? "Camera only: the shots' path, not the level"
        : !LiveActive ? "Live: Play or scrub to run the scene on the level"
        : Live.IsPreparing ? "Loading the level with the mission's changes…"
        : Live.WasForced ? "Live, made to play: the level's story does not start it from here"
        : "Live: the game's own result";
}
