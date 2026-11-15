using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Hosting;
using OpenIGI.Host.Editor;

namespace OpenIGI.Host;

/// <summary>
/// The editor's cutscene preview: a scene played on the level itself by the game's own tick. A moment ahead is played on to; a
/// moment behind is reached by putting the level back from memory where it stood a little before (a checkpoint every second
/// of the scene, <see cref="LevelCheckpoint"/>) and playing on to it, so every moment shown is the one play reaches -- the
/// checkpoints hold the whole level, field for field, which a save never did. Ending the preview, or turning to another
/// scene, puts back the level as it stood before any was played. Nothing is loaded again but for a mission saved with changes.
/// </summary>
public sealed partial class GameHost
{
    private CutscenePreviewer? _cutscenePreview;

    /// <summary>Gets the preview, made the first time the editor asks, or null where the game has none.</summary>
    private CutscenePreviewer? CutscenePreview =>
        _hosted.CutscenePreview is null ? null : _cutscenePreview ??= new CutscenePreviewer(this);

    /// <summary>Gets whether the preview runs the level in real time, which the editor's hold then lets through.</summary>
    private bool CutscenePreviewRuns => _cutscenePreview is { Scene: not null, IsPlaying: true };

    /// <summary>Gets whether the preview holds the level still: one is shown at a moment, or its level is being loaded afresh.</summary>
    private bool CutscenePreviewHoldsLevel => _cutscenePreview is { } preview && (preview.IsPreparing || preview is { Scene: not null, IsPlaying: false });

    /// <summary>One tick of the level as play runs it: its task tree, then the mission's tick.</summary>
    private void StepLevelTick()
    {
        if (_session.LevelRoot is { } level)
        {
            bool paused = level.IsPaused;
            level.IsPaused = false;
            level.Receive(TaskMessage.Update);
            level.IsPaused = paused;
        }

        _hosted.Tick.Run(true);
    }

    /// <summary>Loads the mission that is up afresh, keeping the camera where it is, as the editor's own reload does.</summary>
    private void ReloadLevelInPlace()
    {
        if (_session.Mission is not { } mission)
        {
            return;
        }

        (Core.Numerics.Vector3d Position, float Yaw, float Pitch)? pose = _camera is { } kept ? (kept.Position, kept.Yaw, kept.Pitch) : null;
        CancelMissionWarmUp();
        _editorMotion.Clear();
        StartMission(mission.Id);
        if (pose is { } at)
        {
            _camera?.MoveTo(at.Position, at.Yaw, at.Pitch);
        }
    }

    /// <summary>What the editor drives a preview through.</summary>
    /// <param name="host">The host.</param>
    private sealed class CutscenePreviewer(GameHost host) : IEditorCutscenePreview
    {
        // The game's ticks a second: a scene's clock counts them.
        private const int Frequency = 30;

        // How far ahead the level is run looking for a scene to start of itself, in ticks, before it is made to play.
        private const int LongestLeadIn = 60 * Frequency;

        // How often, in ticks of the scene, the level is held as it plays: a seek back plays on at most this far.
        private const int CheckpointEvery = Frequency;

        // What the checkpoints of one scene may hold of their own before every other one is let go.
        private const long CheckpointBudgetBytes = 512L * 1024 * 1024;

        // For each scene, how many ticks from the level's start it starts in of itself; -1 for one that has to be made to play.
        private readonly Dictionary<int, int> _leadIns = [];

        // The scene's checkpoints, by its clock.
        private readonly SortedList<int, LevelCheckpoint> _checkpoints = [];

        // The level the preview plays on, and whether it has run it since it was last as the editor had it.
        private LevelTask? _level;
        private bool _ran;

        // The level as the editor had it before any scene played: what ending the preview, or another scene, starts from.
        private LevelCheckpoint? _pristine;

        // The level being loaded afresh (the one it replaces), and the moment to play the scene up to once it is in.
        private LevelTask? _replaced;
        private bool _reloading;
        private int? _target;

        public int? Scene { get; private set; }

        public bool IsPlaying
        {
            get;
            set
            {
                field = value && Scene is not null && !IsPreparing && !IsFinished;
                _ran |= field;
            }
        }

        public bool ShowsCamera { get; set; } = true;

        public bool IsPreparing => _reloading;

        public double Seconds => _target is { } target ? target / (double)Frequency
            : Scene is { } id && Game is { } game ? Math.Max(0, game.SceneTicks(id)) / (double)Frequency : 0;

        public bool IsFinished => !_reloading && Scene is { } id && Game?.IsFinished(id) == true;

        public bool WasForced => Scene is { } id && _leadIns.TryGetValue(id, out int lead) && lead < 0;

        public float ViewportHeight =>
            Scene is not null && !_reloading && host._mission.CutsceneFrameShown is { } frame ? Math.Clamp(frame.ViewportHeightFactor, 0f, 1f) : 1f;

        private IMissionCutscenePreview? Game => host._hosted.CutscenePreview;

        // The level as the editor had it, while it is still the one up.
        private LevelCheckpoint? Pristine => _pristine is { } held && ReferenceEquals(held.Level, host._session.LevelRoot) ? held : null;

        public bool Begin(int sceneId)
        {
            Finish();
            if (Game is not { } game || game.SceneTicks(sceneId) < 0)
            {
                return false;
            }

            Scene = sceneId;
            if (_reloading || host.PresentationHoldsLevel)
            {
                // The level is still coming up: the scene starts on it once it is in.
                _reloading = true;
                _target = 0;
                return true;
            }

            if (_ran)
            {
                // The level has been run and cannot be put back: the scene starts on one loaded afresh.
                Reload(0);
                return true;
            }

            Start(0);
            return true;
        }

        public void Seek(double seconds)
        {
            if (Scene is not { } id || Game is not { } game)
            {
                return;
            }

            int target = Math.Max(0, (int)Math.Round(seconds * Frequency));
            if (_reloading)
            {
                _target = target;
                return;
            }

            if (game.SceneTicks(id) > target)
            {
                // A moment behind: the level put back where the scene last stood at or before it, and played on.
                if (!Rewind(target))
                {
                    return;
                }
            }

            PlayTo(id, target);
        }

        public void Finish() => Finish(reload: false);

        public void Finish(bool reload)
        {
            IsPlaying = false;
            Game?.Force(null);
            if (reload && !_reloading)
            {
                // The mission was saved with changes: the level loaded afresh holds them, which no checkpoint does.
                Reload(null);
            }
            else if (!_reloading && Pristine is { } pristine && (_ran || Scene is not null))
            {
                // Whatever the scene did is put back, the scenes' skip as the level had it too: the editor goes on with the level
                // as it had it.
                host.RestoreLevel(pristine);
                _ran = false;
            }
            else if (_ran && !_reloading)
            {
                Reload(null);
            }

            _checkpoints.Clear();
            Scene = null;
            _target = null;
            host._cutsceneSounds.StopAll();
        }

        /// <summary>Each frame, before the level's hold is decided: a level loaded afresh taken up, the scene held each second it plays, and stopped at its end.</summary>
        public void AfterFrame()
        {
            if (_reloading)
            {
                if (host._session.LevelRoot is { } fresh && !ReferenceEquals(fresh, _replaced) && !host.PresentationHoldsLevel)
                {
                    _reloading = false;
                    _replaced = null;
                    _ran = false;
                    _pristine = null;
                    _checkpoints.Clear();
                    int? target = _target;
                    _target = null;
                    if (Scene is not null)
                    {
                        Start(target ?? 0);
                    }
                }

                return;
            }

            if (Scene is not { } id)
            {
                return;
            }

            // A level loaded since by anything else (Play, the editor's own reload) is not the one previewed.
            if (!ReferenceEquals(_level, host._session.LevelRoot))
            {
                IsPlaying = false;
                Game?.Force(null);
                Scene = null;
                _ran = false;
                _pristine = null;
                _checkpoints.Clear();
                return;
            }

            if (IsPlaying)
            {
                Hold(id);
            }

            if (IsFinished)
            {
                IsPlaying = false;
            }
        }

        // The scene started on the level as the editor has it: by the level's own ticks when its story starts it, made to play
        // otherwise; then played up to a moment. The level is held first, to be put back when the preview ends.
        private void Start(int target)
        {
            if (Scene is not { } id || Game is not { } game)
            {
                return;
            }

            _level = host._session.LevelRoot;
            if (Pristine is null && host.CanCheckpointLevel)
            {
                _pristine = host.CaptureLevel(0);
            }

            _ = game.AllowScenes();
            if (!_leadIns.TryGetValue(id, out int lead))
            {
                // Found once a scene: the level's ticks until its story starts it, the scenes before it moving what they move.
                lead = 0;
                while (lead < LongestLeadIn && !game.IsPlaying(id))
                {
                    host.StepLevelTick();
                    _ran = true;
                    lead++;
                }

                if (!game.IsPlaying(id))
                {
                    // Waiting on something only play brings about: made to play, from the level as the editor had it.
                    _leadIns[id] = -1;
                    if (Pristine is { } pristine)
                    {
                        host.RestoreLevel(pristine);
                        _ran = false;
                        Start(target);
                    }
                    else
                    {
                        Reload(target);
                    }

                    return;
                }

                _leadIns[id] = lead;
            }
            else if (lead < 0)
            {
                game.Force(id);
                for (int i = 0; i < 3 && !game.IsPlaying(id); i++)
                {
                    host.StepLevelTick();
                    _ran = true;
                }
            }
            else
            {
                for (int i = 0; i < lead; i++)
                {
                    host.StepLevelTick();
                    _ran = true;
                }
            }

            _checkpoints.Clear();
            Hold(id);
            PlayTo(id, target);
        }

        /// <summary>
        /// An editor's change came into the level: the checkpoints from the moment it can act are let go, and the moment shown is
        /// played again from before it, so it shows the change.
        /// </summary>
        /// <param name="fromSceneSeconds">When the change can first act in the scene: infinity for the camera only, null for any time.</param>
        public void Edited(double? fromSceneSeconds)
        {
            if (Scene is not { } id || _reloading || Game is not { } game)
            {
                return;
            }

            if (fromSceneSeconds is not { } from || !double.IsPositiveInfinity(from))
            {
                int first = fromSceneSeconds is { } at ? Math.Max(0, (int)Math.Floor(at * Frequency)) : 0;
                for (int i = _checkpoints.Count - 1; i >= 0 && _checkpoints.Keys[i] >= first; i--)
                {
                    _checkpoints.RemoveAt(i);
                }
            }

            bool playing = IsPlaying;
            int now = Math.Max(0, game.SceneTicks(id));
            if (Rewind(now, before: true))
            {
                PlayTo(id, now);
            }

            IsPlaying = playing;
        }

        // The level put back at the scene's last checkpoint at or before a moment (strictly before it, when asked), to be played on
        // to it (true); or, with none, as the editor had it and the scene started again up to the moment (false: nothing to play).
        private bool Rewind(int target, bool before = false)
        {
            LevelCheckpoint? at = null;
            for (int i = _checkpoints.Count - 1; i >= 0; i--)
            {
                if (before ? _checkpoints.Keys[i] < target : _checkpoints.Keys[i] <= target)
                {
                    at = _checkpoints.Values[i];
                    break;
                }
            }

            if (at is not null && host.RestoreLevel(at))
            {
                return true;
            }

            if (Pristine is { } pristine && host.RestoreLevel(pristine))
            {
                _ran = false;
                Start(target);
                return false;
            }

            Reload(target);
            return false;
        }

        // The level run on until the scene's clock reaches a moment, or it ends, held each second on the way.
        private void PlayTo(int id, int target)
        {
            if (Game is not { } game)
            {
                return;
            }

            int guard = target + Frequency;
            while (game.SceneTicks(id) < target && game.IsPlaying(id) && guard-- > 0)
            {
                host.StepLevelTick();
                _ran = true;
                Hold(id);
            }

            // What the run up to the moment set sounding is not heard all at once: the scene's sounds start again as it plays on.
            host._cutsceneSounds.StopAll();
        }

        // A checkpoint where the scene's clock is on a second it has none for; the oldest half let go past the budget.
        private void Hold(int id)
        {
            if (Game is not { } game || !game.IsPlaying(id) || !host.CanCheckpointLevel)
            {
                return;
            }

            int ticks = game.SceneTicks(id);
            int key = ticks / CheckpointEvery * CheckpointEvery;
            if (ticks != key || _checkpoints.ContainsKey(key))
            {
                return;
            }

            LevelCheckpoint? previous = _checkpoints.Count > 0 ? _checkpoints.Values[^1] : Pristine;
            if (host.CaptureLevel(key, previous) is not { } held)
            {
                return;
            }

            _checkpoints[key] = held;
            if (_checkpoints.Values.Sum(static c => c.State.OwnBytes) > CheckpointBudgetBytes)
            {
                // Every other one let go: seeks back then play on two seconds at most rather than one.
                for (int i = _checkpoints.Count - 2; i > 0; i -= 2)
                {
                    _checkpoints.RemoveAt(i);
                }
            }
        }

        // The mission loaded afresh; the scene, if one is previewed, played up to a moment once it is in.
        private void Reload(int? target)
        {
            IsPlaying = false;
            Game?.Force(null);
            _replaced = host._session.LevelRoot;
            _reloading = true;
            _target = target;
            _pristine = null;
            _checkpoints.Clear();
            host.ReloadLevelInPlace();
        }
    }
}
