using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Hosting;
using OpenIGI.Host.Editor;

namespace OpenIGI.Host;

/// <summary>
/// The editor's cutscene preview: a scene played on the level itself by the game's own tick. A moment ahead is played on to;
/// a moment behind is reached by loading the level afresh and playing the scene again up to it, so every moment shown is the
/// one play reaches -- nothing is put back from a copy, which would leave behind whatever the copy does not hold.
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

        // For each scene, how many ticks from the level's start it starts in of itself; -1 for one that has to be made to play.
        private readonly Dictionary<int, int> _leadIns = [];

        // The level the preview plays on, and whether it has run it: a level run is loaded afresh before it is edited again.
        private LevelTask? _level;
        private bool _ran;

        // The level being loaded afresh (the one it replaces), and the moment to play the scene up to once it is in.
        private LevelTask? _replaced;
        private bool _reloading;
        private int? _target;
        private Action? _restoreSkip;

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

        /// <summary>Gets whether the level is being loaded afresh to play the scene up to a moment behind the one shown.</summary>
        public bool IsPreparing => _reloading;

        public double Seconds => _target is { } target ? target / (double)Frequency
            : Scene is { } id && Game is { } game ? Math.Max(0, game.SceneTicks(id)) / (double)Frequency : 0;

        public bool IsFinished => !_reloading && Scene is { } id && Game?.IsFinished(id) == true;

        public bool WasForced => Scene is { } id && _leadIns.TryGetValue(id, out int lead) && lead < 0;

        public float ViewportHeight =>
            Scene is not null && !_reloading && host._mission.CutsceneFrameShown is { } frame ? Math.Clamp(frame.ViewportHeightFactor, 0f, 1f) : 1f;

        private IMissionCutscenePreview? Game => host._hosted.CutscenePreview;

        public bool Begin(int sceneId)
        {
            Finish();
            if (Game is not { } game || game.SceneTicks(sceneId) < 0)
            {
                return false;
            }

            Scene = sceneId;
            if (host.PresentationHoldsLevel && !_reloading)
            {
                // The level is still coming up: the scene starts on it once it is in.
                _reloading = true;
                _replaced = null;
                _target = 0;
                return true;
            }

            if (_reloading)
            {
                // The level the last preview ran is being loaded afresh already: the scene starts on it.
                _target = 0;
                return true;
            }

            if (_ran)
            {
                // The level has been run since it was loaded: the scene starts on one loaded afresh.
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

            // A moment behind: the level loaded afresh and the scene played again up to it.
            if (game.SceneTicks(id) > target)
            {
                Reload(target);
                return;
            }

            PlayTo(id, target);
        }

        public void Finish()
        {
            IsPlaying = false;
            Game?.Force(null);
            if (_ran && !_reloading)
            {
                // Whatever the scene did stays in the level that played it: the editor goes on with one loaded afresh.
                Reload(null);
            }
            else if (!_ran)
            {
                _restoreSkip?.Invoke();
            }

            _restoreSkip = null;
            Scene = null;
            _target = null;
            host._cutsceneSounds.StopAll();
        }

        /// <summary>Each frame, before the level's hold is decided: a level loaded afresh taken up, and the scene stopped at its end.</summary>
        public void AfterFrame()
        {
            if (_reloading)
            {
                if (host._session.LevelRoot is { } fresh && !ReferenceEquals(fresh, _replaced) && !host.PresentationHoldsLevel)
                {
                    _reloading = false;
                    _replaced = null;
                    _ran = false;
                    _level = fresh;
                    int? target = _target;
                    _target = null;
                    if (Scene is not null)
                    {
                        Start(target ?? 0);
                    }
                }

                return;
            }

            if (Scene is null)
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
                return;
            }

            if (IsFinished)
            {
                IsPlaying = false;
            }
        }

        // The scene started on the level as it stands, never run: by the level's own ticks when its story starts it, made to play
        // otherwise; then played up to a moment.
        private void Start(int target)
        {
            if (Scene is not { } id || Game is not { } game)
            {
                return;
            }

            _level = host._session.LevelRoot;
            _restoreSkip = game.AllowScenes();
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
                    // Waiting on something only play brings about: made to play from a level loaded afresh.
                    _leadIns[id] = -1;
                    Reload(target);
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

            PlayTo(id, target);
        }

        // The level run on until the scene's clock reaches a moment, or it ends.
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
            }

            // What the run up to the moment set sounding is not heard all at once: the scene's sounds start again as it plays on.
            host._cutsceneSounds.StopAll();
        }

        // The mission loaded afresh; the scene, if one is previewed, played up to a moment once it is in.
        private void Reload(int? target)
        {
            IsPlaying = false;
            Game?.Force(null);
            _replaced = host._session.LevelRoot;
            _reloading = true;
            _target = target;
            host.ReloadLevelInPlace();
        }
    }
}
