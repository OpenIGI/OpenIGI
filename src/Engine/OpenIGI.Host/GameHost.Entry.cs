using System.Diagnostics;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Animation;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    // The two animation sets every mission uses: skeleton 0's, which the player's body walks on, and
    // skeleton 5's, the first-person rig the held weapon hangs off. They live under common/ rather than
    // under any level, so they are the same bytes for every mission of one game -- 1.5 MB and 0.9 MB of
    // IFF re-read and re-parsed on the render thread every single time a mission started. Read once per
    // TITLE: they come from the game's own installation, so switching games has to forget them.
    private BoneAnimationSet? _playerBodyAnimations;
    private BoneAnimationSet? _viewRigAnimations;
    private bool _sharedAnimationsRead;

    // The read, in flight on a worker. Started at start-up and waited for at the first mission, which is
    // seconds later and usually long finished.
    private Task<(BoneAnimationSet? Body, BoneAnimationSet? Rig)>? _sharedAnimationsTask;

    /// <summary>Starts reading the two shared animation sets on a worker.</summary>
    /// <remarks>
    /// Two and a half megabytes of IFF, parsed into quaternion tracks. It reads files and allocates and
    /// touches nothing else in the session, so there is no reason for it to be between the player and
    /// the first frame — and at start-up the render thread is otherwise waiting on the driver to build a
    /// device, which is exactly the moment a worker is free.
    /// </remarks>
    private void BeginReadingSharedAnimations() =>
        _sharedAnimationsTask ??= _work.Run(() =>
            (ReadAnimationSet("000.iff", "the player cannot walk"),
             ReadAnimationSet("005.iff", "no weapon can be held")));

    /// <summary>Makes the two shared animation sets available, waiting for the read if it is still going.</summary>
    /// <remarks>
    /// Failure is not fatal and is not retried: without skeleton 0 the player can stand and fall but not
    /// walk, and without skeleton 5 nothing can be held — both of which the callers already report. A
    /// second attempt would only produce the same warning once per mission.
    /// </remarks>
    private void ReadSharedAnimations()
    {
        if (_sharedAnimationsRead)
        {
            return;
        }

        _sharedAnimationsRead = true;
        BeginReadingSharedAnimations();
        (_playerBodyAnimations, _viewRigAnimations) = _sharedAnimationsTask!.GetAwaiter().GetResult();
    }

    /// <summary>Drops the shared sets so the next mission reads the running title's own.</summary>
    /// <remarks>
    /// Called when the game being played changes, which is the only thing that can invalidate them. A
    /// read still in flight is abandoned rather than waited on: it reads files and touches nothing else,
    /// so the worst it can do is finish into a task nobody holds.
    /// </remarks>
    private void ForgetSharedAnimations()
    {
        _sharedAnimationsRead = false;
        _sharedAnimationsTask = null;
        _playerBodyAnimations = null;
        _viewRigAnimations = null;
    }

    private BoneAnimationSet? ReadAnimationSet(string name, string consequence)
    {
        string path = Path.Combine(_gamePath, "common", "anims", name);
        try
        {
            if (File.Exists(path))
            {
                return BoneAnimationSet.Read(File.ReadAllBytes(path));
            }

            _logger.Warning("Player", $"No animation set at {path}; {consequence}.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            _logger.Warning("Player", $"Could not read {path}: {ex.Message}; {consequence}.");
        }

        return null;
    }

    // The worst single loop pass, in milliseconds, since the watch was armed. The point of promoting the
    // menu's backdrop rather than loading the mission again is that pressing Play costs the frame
    // nothing, and "nothing" is a number somebody has to be able to read back. Armed when Play is
    // pressed and reported when the entry finishes.
    private double _worstPassMilliseconds;
    private bool _watchingFramePasses;
    private int _reportWatchAtFrame = -1;
    private string _watchSubject = string.Empty;

    /// <summary>How long a single loop pass has to take before it is worth naming in the log.</summary>
    /// <remarks>
    /// Well above a 60 Hz frame's 16.7 ms, because the interesting number is not "this frame was a
    /// little slow" but "the picture visibly stopped". Anything over this is a hitch a player would see.
    /// </remarks>
    private const double HitchMilliseconds = 40.0;

    /// <summary>How long a frame with nothing but a menu on it will spend making the backdrop playable.</summary>
    /// <remarks>
    /// Small enough to be invisible — the menu renders uncapped, so this is a fraction of the headroom a
    /// frame already has — and large enough that a level's residency is finished within a second or two
    /// of the backdrop appearing, which is far less time than it takes to read a briefing.
    /// </remarks>
    private const double IdleArmingBudgetMilliseconds = 2.0;

    /// <summary>Starts watching for the worst loop pass, forgetting whatever the last watch saw.</summary>
    /// <param name="what">What the watch is covering, for the log line it eventually writes.</param>
    private void BeginWatchingFramePasses(string what)
    {
        _worstPassMilliseconds = 0.0;
        _watchingFramePasses = true;
        _reportWatchAtFrame = -1;
        _watchSubject = what;
    }

    /// <summary>Asks for the watch to report once a few more frames have been drawn.</summary>
    /// <param name="frames">How many further frames to cover before reporting.</param>
    /// <remarks>
    /// The expensive pass is the one that is still running when the work finishes, and a pass only knows
    /// its own cost once it ends — so reporting inline would always miss the very frame the measurement
    /// is about. Waiting a few frames also catches anything the first drawn frame of a new mission pays
    /// for, such as a shader compiled on first use.
    /// </remarks>
    private void ReportWatchedFramePassesIn(int frames) =>
        _reportWatchAtFrame = _loop.Clock.FrameCount + Math.Max(1, frames);

    /// <summary>Writes the watch's result out once the frame it was deferred to arrives.</summary>
    private void FlushWatchedFramePasses()
    {
        if (!_watchingFramePasses || _reportWatchAtFrame < 0 || _loop.Clock.FrameCount < _reportWatchAtFrame)
        {
            return;
        }

        ReportWatchedFramePasses(_watchSubject);
        _reportWatchAtFrame = -1;
    }

    /// <summary>Records how long one pass of the frame loop took, while a watch is running.</summary>
    /// <param name="startedAt">The <see cref="Stopwatch"/> timestamp the pass began at.</param>
    private void NoteFramePass(long startedAt)
    {
        if (!_watchingFramePasses)
        {
            return;
        }

        double milliseconds =
            (Stopwatch.GetTimestamp() - startedAt) * 1000.0 / Stopwatch.Frequency;
        if (milliseconds > _worstPassMilliseconds)
        {
            _worstPassMilliseconds = milliseconds;
        }

        // Keep this measurement side-effect free. Logging every hitch here is especially harmful while
        // the scheduler is catching up after an animation: the synchronous file logger turns each hitch
        // into more work, which creates more catch-up ticks and makes the slowdown self-amplifying. The
        // final worst-frame summary and the named slow stages retain the useful diagnostic information.
    }

    // Where the time inside a single mission hand-over went. Only collected while a watch is running, so
    // it costs an ordinary frame a branch and nothing else.
    private long _stageStartedAt;

    /// <summary>Marks the start of a named stage of bringing a mission up.</summary>
    private void BeginStage() => _stageStartedAt = Stopwatch.GetTimestamp();

    // OPENIGI_STAGE_TIMES=1: every load stage's time, whatever is being watched -- where a headless capture's load goes.
    private static readonly bool LogStageTimes = Environment.GetEnvironmentVariable("OPENIGI_STAGE_TIMES") == "1";

    /// <summary>Reports how long the stage since <see cref="BeginStage"/> took, and starts the next.</summary>
    /// <param name="what">The stage that just finished.</param>
    private void EndStage(string what)
    {
        if (_watchingFramePasses || LogStageTimes)
        {
            double milliseconds =
                (Stopwatch.GetTimestamp() - _stageStartedAt) * 1000.0 / Stopwatch.Frequency;
            _logger.Information(
                "Mission",
                FormattableString.Invariant($"  {what}: {milliseconds:F1} ms."));
        }

        // One more share of the loading bar, and a frame to show it on: the load is the one stretch
        // where nothing else presents. See GameHost.MissionWarmUp.cs.
        ReportLoadStage();

        _stageStartedAt = Stopwatch.GetTimestamp();
    }

    /// <summary>Reports the stage since <see cref="BeginStage"/> only when it was slow enough to matter.</summary>
    /// <param name="what">The stage that just finished.</param>
    /// <remarks>
    /// For stages that run every frame, where naming every one of them would bury the one that mattered.
    /// </remarks>
    private void EndStageIfSlow(string what)
    {
        double milliseconds =
            (Stopwatch.GetTimestamp() - _stageStartedAt) * 1000.0 / Stopwatch.Frequency;
        if (_watchingFramePasses && milliseconds > HitchMilliseconds)
        {
            _logger.Information(
                "Mission",
                FormattableString.Invariant($"  {what}: {milliseconds:F1} ms."));
        }
    }

    /// <summary>Stops the watch and reports the worst pass it saw.</summary>
    /// <param name="what">What the watch was covering, for the log line.</param>
    private void ReportWatchedFramePasses(string what)
    {
        if (!_watchingFramePasses)
        {
            return;
        }

        _watchingFramePasses = false;
        _logger.Information(
            "Mission",
            FormattableString.Invariant(
                $"{what}: the worst single frame cost {_worstPassMilliseconds:F1} ms."));
    }

    /// <summary>Gives the motion blur back to the player's setting once the entry shot hands over.</summary>
    /// <remarks>
    /// Called twice on the way into a mission -- once when the menu leaves and again when the entry shot
    /// hands over -- which is harmless: clearing an override that is not there does nothing.
    /// </remarks>
    private static void ApplyGameplayMotionBlur() => PostCVars.MotionBlur.ClearOverride();

    /// <summary>Applies the shot's own smear, from how far it is moving.</summary>
    /// <remarks>
    /// Overridden rather than written: the variable is archived and preset-managed, so writing it would read
    /// the preset as Custom for the length of the shot and could be saved as the player's own setting.
    /// </remarks>
    private void ApplyEntryMotionBlur() =>
        PostCVars.MotionBlur.Override(Math.Max(PostCVars.MotionBlur.StoredValue, _entry.MotionBlur));

    /// <summary>How much of the frame's height each cinematic bar takes when fully closed.</summary>
    /// <remarks>
    /// Eleven per cent a side turns a 16:9 frame into roughly 2.39:1, which is the ratio a film camera
    /// would have shot this in. Derived from the viewport every frame rather than fixed in pixels, so a
    /// resize partway through the shot costs nothing.
    /// </remarks>
    private const float LetterboxFraction = 0.11f;

    // The shell's own faces, so the shot's title is set in the same type the menu is. Borrowed, not
    // owned: the composition root builds and disposes them.
    private UiFont? _shellBodyFont;
    private UI.Kit.IgiTypography? _shellType;

    /// <summary>Draws the shot's own chrome: the cinematic bars.</summary>
    /// <param name="painter">The shell's painter, drawing on the back buffer over everything else.</param>
    private void DrawEntryFraming(UiPainter painter)
    {
        float width = _window.Width;
        float height = _window.Height;

        float bars = height * LetterboxFraction * Math.Clamp(_entry.LetterboxAmount, 0f, 1f);
        if (bars > 0.5f)
        {
            var black = Bgra32.FromRgb(0, 0, 0);
            painter.FillRect(new UiRect(0f, 0f, width, bars), black);
            painter.FillRect(new UiRect(0f, height - bars, width, bars), black);
        }
    }
}
