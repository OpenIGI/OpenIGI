using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Scoring;
using OpenIGI.Input;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    // How many frames a lockstep run leaves the rank screen up before it goes on by itself: nobody is there to press a key.
    private const int LockstepScoreFrames = 150;

    // How many seconds the rank screen takes to fade in.
    private const float ScoreFadeSeconds = 0.35f;

    // The frames the rank screen has been up, and the real time it came up at.
    private int _scoreFrames;
    private int _scoreShownAt;

    // Whether a finger was down on the last frame, so a tap is told from a touch held on from play.
    private bool _scoreTouchWasDown;

    /// <summary>
    /// The rank screen's half of the frame. A co-op client puts up the score its server tells. While a score holds the level it
    /// takes the frame's input: where this end may go on -- a single player, a co-op host -- Space, the original's key
    /// (<c>MissionScoreScreen_RunHandler</c>), or Return, Escape, a click, a tap or the pad's confirm goes on; a co-op squad's
    /// countdown goes on by itself when it runs out, on the server (a client waits for the server to take it on).
    /// </summary>
    /// <param name="state">The frame loop's state.</param>
    /// <returns>Whether a score holds the level, and so owns the frame's keys.</returns>
    private bool UpdateScoreScreen(FrameLoopState state)
    {
        if (TickObserver is Multiplayer.ISessionMissionScore scored && scored.TakeMissionScore() is { } squad)
        {
            _outcome.HearSquadScore(squad);
        }

        if (_session.Score is not { } hold)
        {
            _scoreFrames = 0;
            return false;
        }

        if (_scoreFrames++ == 0)
        {
            _scoreShownAt = _loop.TimeSource.Milliseconds;
        }

        bool goOn = hold.CanContinue && _scoreFrames > 1 && !state.Console.IsOpen && ContinuePressed(_window.Input);
        goOn |= hold.DeadlineMilliseconds is { } deadline && _loop.TimeSource.Milliseconds >= deadline && TickObserver is not Multiplayer.ISessionFollower;
        goOn |= hold.DeadlineMilliseconds is null && _options.Lockstep && _scoreFrames >= LockstepScoreFrames;

        // Nothing under it sees the keys: the level stands still, and the key that goes on must not also fire or pause.
        _window.Input.Captured = true;
        if (goOn)
        {
            _scoreFrames = 0;
            _logger.Information("Mission", "The rank screen was taken down; going on.");
            _outcome.ContinueFromScore(state);
        }

        return true;
    }

    // Space, Return, Escape, a click, a fresh tap or the pad's confirm.
    private bool ContinuePressed(Input.InputState input)
    {
        bool touched = input.Touch.AnyDown && !_scoreTouchWasDown;
        _scoreTouchWasDown = input.Touch.AnyDown;
        return input.Keyboard.WasPressed(KeyCode.Space)
            || input.Keyboard.WasPressed(KeyCode.Return)
            || input.Keyboard.WasPressed(KeyCode.NumpadEnter)
            || input.Keyboard.WasPressed(KeyCode.Escape)
            || input.Mouse.WasPressed(MouseButton.Left)
            || input.Gamepad.WasPressed(GamepadButton.South)
            || input.Gamepad.WasPressed(GamepadButton.Start)
            || touched;
    }

    /// <summary>Draws the rank screen over the level while it is up.</summary>
    /// <param name="state">The frame loop's state, whose sprite batch it draws into.</param>
    private void DrawScoreScreen(FrameLoopState state)
    {
        if (ShownScoreCard() is not { } card || _remasteredHud is not { IsReady: true } hud)
        {
            return;
        }

        float fade = _scoreFrames == 0 ? 1f : Math.Clamp((_loop.TimeSource.Milliseconds - _scoreShownAt) / (ScoreFadeSeconds * 1000f), 0f, 1f);
        _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
        state.Batch.Begin();
        hud.DrawMissionScore(state.Batch, card, ScorePrompt(), fade, SafeWindowWidth, SafeWindowHeight);
        state.Batch.End();
    }

    // What the screen's foot says: the key that goes on where this end may, and a co-op squad's countdown.
    private Presentation.MissionScorePrompt ScorePrompt()
    {
        if (_session.Score is not { } hold)
        {
            return PreviewScorePrompt();
        }

        int? secondsLeft = hold.DeadlineMilliseconds is { } deadline
            ? (int)Math.Max(0L, (deadline - _loop.TimeSource.Milliseconds + 999L) / 1000L)
            : null;
        return new Presentation.MissionScorePrompt(hold.CanContinue ? ContinueKey : null, secondsLeft, WaitsForHost: !hold.CanContinue);
    }

    // The key the screen's cap names.
    private const string ContinueKey = "SPACE";

    /// <summary>
    /// Gets the words a mission's score is told in: the menus' text, which holds I.G.I. 2's own keys in its language; a key it
    /// lacks -- all of them, for Project I.G.I. -- is told in English.
    /// </summary>
    internal MissionScoreWords ScoreWords =>
        _menuStrings is { } table ? new MissionScoreWords(key => table.Contains(key) ? table.Get(key) : null) : MissionScoreWords.InEnglish;

    /// <summary>The card of the best the active profile has done in a mission of the running game, for the menu.</summary>
    /// <param name="missionId">The mission.</param>
    /// <returns>The card, or <see langword="null"/> where none is kept.</returns>
    private MissionScoreCard? BestScore(int missionId) =>
        MissionScoring.CardFor(_configuration.Active?.MissionRecordOf(_titleRuntime.Title.Id, missionId), ScoreWords);

    // The card up now: the mission's own, or the overlay preview's sample.
    private MissionScoreCard? ShownScoreCard() => _session.Score?.Card ?? PreviewScoreCard();
}
