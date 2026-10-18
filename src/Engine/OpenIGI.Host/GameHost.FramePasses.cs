using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Time;
using OpenIGI.Frontend;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Host.Infrastructure;
using OpenIGI.Host.Menu;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Rendering.Presets;
using OpenIGI.Rendering;
using OpenIGI.UI.Layout;
using OpenIGI.UI;

namespace OpenIGI.Host;

/// <summary>The frame loop's passes, in the order <see cref="Frames"/> runs them.</summary>
public sealed partial class GameHost
{
    /// <summary>Puts the front end back up for a title that has just been switched to.</summary>
    /// <remarks>
    /// The shell is rebuilt around the new title rather than told about it: its game list, its campaign and which
    /// entry is current are all the title's.
    /// </remarks>
    private void ReopenFrontEndForTitle(FrameLoopState state)
    {
        // The outgoing title's sweep stops with it, and the new title's missions are swept once its
        // first backdrop is up.
        state.MissionWarmer?.Dispose();
        state.MissionWarmer = new MissionWarmer(_gamePath, _missions, _logger, _work);
        _menuFlow.MissionsSwept = false;

        // Closed rather than dropped: it owns a worker that may be part way through building the
        // outgoing game's backdrop, and the replacement below would otherwise race it.
        state.PreviewLoader?.Dispose();
        state.PreviewLoader = new MissionPreviewLoader(
            _gamePath, _missions, _mission.MagicObjects, _baked.ProbeCache, _menuCameras, _previewContext, _logger, _work);

        if (state.ShellHost is not null && state.ShellTypography is not null && state.Saves is not null)
        {
            state.Shell = new Shell(BuildShellContext(state.ShellHost, state.ShellTypography.CreateTheme(), state.Saves));
            state.Shell.OpenOn("campaign");
        }

        // Back to the front end with no level up: the outgoing game's was released and the
        // incoming one has not been asked for.
        _menuFlow.MenuMode = true;
        _menuFlow.PauseMenu = false;
        _menuFlow.ShowcasePending = true;
    }

    /// <summary>Paces the pass, pumps the window, steps the lighting, and lets the console read the keyboard.</summary>
    private void BeginFramePass(FrameLoopState state)
    {
        // The menu has nothing to gain past the display's rate, and a GPU kept at full load turns every
        // small upload between frames into a hitch. Only after a pass that presented, or the pacing
        // would throttle the simulation's ticks as well; never under a capture, whose clock is its own.
        // A level held behind the loading screen while its field bakes is paced harder still: the
        // withheld frames exist to advance the temporal passes, which thirty a second does as well as
        // a hundred, and every core the render thread is not spinning on is one the bake gets.
        if (state.LastPassRendered && !_options.Lockstep && !_options.IsBenchmark)
        {
            if (IsWarmingUp && _gi.FieldBake is not null)
            {
                state.MenuPacer.Pace(30);
            }
            else if (_menuFlow.MenuMode && !RenderCVars.VSync.Value)
            {
                state.MenuPacer.Pace(_window.RefreshRate > 0 ? _window.RefreshRate : 60);
            }
            else
            {
                state.MenuPacer.Reset();
            }
        }
        else
        {
            state.MenuPacer.Reset();
        }

        state.LastPassRendered = false;
        state.PassStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();

        // NVIDIA Reflex, where its runtime runs: its mode, and the frame it paces. The frame begins only
        // once the last one has been presented, so the passes that only tick neither sleep nor count, and
        // it begins BEFORE the input below is read -- sleeping first is how the frame's input comes fresher.
        // Frame generation needs Reflex on and turns it on whatever the setting says.
        _device.SetLowLatency(EffectiveLowLatency(), RenderCVars.FrameLimit.Value);

        // Frame generation interpolates along the motion of play; a menu, a pause, the warm-up hold or a
        // cutscene has none to follow, so it stands down for those and resumes with play. Asked here, at
        // the top of the pass, because loading or unloading it rebuilds the swapchain between frames.
        _device.SetFrameGeneration(
            RenderCVars.FrameGeneration.Value,
            gameplay: !_menuFlow.MenuMode
                && !_menuFlow.PauseMenu
                && _session.Mission is not null
                && !PresentationHoldsLevel
                && _mission.Cutscenes?.IsPlaying != true);
        _device.BeginLatencyFrame();

        _window.PumpEvents();

        // Whatever the detail-map workers finished since the last pass, uploaded a few at a time.
        // The setting is read here rather than captured at construction so that turning
        // r_material_detail up mid-session starts deriving for whatever loads next -- it is zero
        // under vanilla and enhanced, and deriving maps the shader multiplies by nothing is waste.
        _detailMaps.DeriveMissing = RenderCVars.MaterialDetail.Value > 0f;
        _detailMaps.Enabled = _detailMaps.DeriveMissing;
        _detailMaps.PumpDerived();

        // The baked lighting's share of the frame: what its workers finished, and the probe field eased
        // towards the newest pass. Collected here because what comes back becomes GPU textures.
        _baked.Step(_world, _loop.TimeSource.Milliseconds);

        // Whatever the real-time lighting model needs and does not have yet. Every frame rather than
        // at load, because the settings menu can ask for that model at any moment and the structures it
        // reads have to be able to arrive afterwards -- see GameHost.RealtimeGi.cs. A no-op once
        // they exist, which is every frame but the handful after the switch.
        _gi.EnsureStructures();

        // Behind the loading screen the traced world takes every mesh offered in the frame it is
        // offered, so the level is whole in it before anything is lit against it; in play the per-frame
        // budget comes back. Said every frame, so no path out of the load can leave it lifted.
        _device.SetTraceAdmission(IsWarmingUp);

        // This frame's share of a convergence in flight, so a level lights up behind the loading
        // screen or under the entry shot's hold rather than in one stall.
        _gi.StepConvergence();

        // Same handover, same reason: the atlas becomes a GL volume, and the context belongs here.
        // Behind the menu the adoption is deferred to the frame after the picture has been held, so
        // the backdrop crossing over to its real-time lighting happens under the dissolve rather than
        // in front of the player -- the same shape as a backdrop swap.
        if (_gi.TryCollectFieldBake(out DistanceFieldBake.Result baked))
        {
            if (_menuFlow.OverShowcase && _menuBackdrop.Preview is not null)
            {
                _menuFlow.BakedFieldForBackdrop = baked;
            }
            else
            {
                _gi.AdoptDistanceField(baked.Scene, baked.Atlas, fromCache: false);
            }
        }

        // One tick's worth of synthetic time per RENDERED FRAME, from an absolute target rather than
        // an accumulated step so the schedule cannot drift. Per loop pass would be wrong: the loop
        // pumps many times per frame, so the clock would jump a whole tick each pump and the catch-up
        // cap would fire every pass -- measured at 200 ticks for 20 frames, a world running ten times
        // too fast. Deterministic either way, but a capture should show the moment it claims to.
        if (state.LockstepClock is not null)
        {
            state.LockstepClock.Milliseconds = _options.IsBenchmark
                ? BenchmarkSchedule.MillisecondsForFrame(_loop.Clock.FrameCount)
                : (int)(((_loop.Clock.FrameCount + 1L) * 1000L) / GameClock.DefaultTickRate);

            // A capture must not be able to see the mouse. The window grabs the cursor in player mode,
            // so moving the mouse in another application changed what got captured -- which is exactly
            // how the first reference set came out non-reproducible for a reason that had nothing to
            // do with the clock.
            _window.Input.Captured = true;
        }

        // The console reads the frame's input first. If it is open it then marks the frame captured,
        // and every other reader below -- the debug keys, the cameras, the player, the menu -- sees an
        // idle keyboard and mouse. That is why none of them had to learn the console exists.
        int consoleNow = _loop.TimeSource.Milliseconds;
        state.Console.Update(_window.Input, Math.Clamp((consoleNow - state.LastConsoleMilliseconds) / 1000f, 0f, 0.25f));
        state.LastConsoleMilliseconds = consoleNow;
    }

    /// <summary>Runs the shell over its own input, or lets the game's binds have the keys when it is not up.</summary>
    private void UpdateShell(FrameLoopState state)
    {
        if (!(_menuFlow.MenuMode && state.Shell is not null))
        {
            DrainPressQueue(state, executeBinds: true);
        }

        // The cinematic shell reads the frame's input after the console and, like it, marks the frame
        // captured so the free camera and the game beneath see an idle keyboard and pad. When the console
        // is open it owns the frame and the shell only advances its animation.
        if (_menuFlow.MenuMode && state.Shell is not null)
        {
            int shellNow = _loop.TimeSource.Milliseconds;
            float shellElapsed = Math.Clamp((shellNow - state.LastShellMilliseconds) / 1000f, 0f, 0.1f);
            state.LastShellMilliseconds = shellNow;

            var shellFrame = new LayoutContext(_window.Width, _window.Height);
            state.Shell.Update(shellFrame, _window.Input, shellElapsed);
            DrainPressQueue(state, executeBinds: false);
            if (!state.Console.IsOpen)
            {
                _window.Input.Captured = true;
            }

            // Something wants the picture to change — a new backdrop has finished building, or the
            // showcase camera has held a place long enough to move to the next one. Either way the
            // frame on screen is held first, so the two can be blended rather than cut between.
            // None of it applies to the pause menu, whose picture is a level that is not going anywhere.
            // Not while a load is departing: the held frame is the sequence's picture from here, and
            // a backdrop swapped under a departure would be a different place from the one the card
            // is over.
            bool changeWanted = !_menuFlow.PauseMenu
                && !_loadSequence.IsActive
                && (state.PreviewLoader is { HasResult: true }
                    || _showcaseCamera is { IsCutDue: true }
                    || _menuFlow.BakedFieldForBackdrop is not null);
            if (changeWanted && !_menuFlow.HoldFrameWanted && !_menuFlow.HeldFrameReady)
            {
                _menuFlow.HoldFrameWanted = true;
            }

            // The held picture stays up until the incoming level has converged its lighting, so what
            // the dissolve reveals is the settled backdrop rather than one still lighting up. A field
            // that is still baking is not waited for -- that is seconds, and it crosses over under its
            // own hold when it lands.
            if (_menuFlow.DissolveRevealPending && !_gi.IsConverging)
            {
                _menuFlow.DissolveRevealPending = false;
                _menuFlow.Dissolve.Retarget(0f);
            }

            _menuFlow.Dissolve.Update(shellElapsed);
        }
    }

    /// <summary>Steps the shot that carries the player out of the menu and into their own eyes.</summary>
    private void UpdateEntryShot(FrameLoopState state)
    {
        // The mission-entry shot. It runs on real elapsed time, like the menu's orbit and the
        // interface's own animation, because it is presentation: it must look the same whatever rate
        // the display is running at, and it must not be tied to the simulation's fixed tick.
        if (_entry.IsRunning)
        {
            int entryNow = _loop.TimeSource.Milliseconds;
            float entryElapsed = Math.Clamp((entryNow - state.LastEntryMilliseconds) / 1000f, 0f, 0.1f);
            state.LastEntryMilliseconds = entryNow;

            // The orbit keeps turning under the start of the shot so the rise can leave at the speed
            // the menu was already moving at, rather than from a standstill.
            CameraPose liveOrbit = _showcaseCamera is { } liveShowcase
                ? CameraPose.FromCamera(liveShowcase.Update(entryElapsed))
                : _entry.Pose;

            _entry.Update(
                entryElapsed,
                liveOrbit,
                _mission.Player is { } standing ? CameraPose.FromCamera(standing.Camera) : null);

            // The hand-over, taken as the travel begins: the fastest, most smeared moment of the shot,
            // where the one frame the promotion still costs is least visible. What it used to cost --
            // several hundred milliseconds -- has moved onto the preview worker; see AdoptPreview.
            if (_entry.WantsPromotion)
            {
                if (_menuBackdrop.Preview is { } entering && PromoteBackdrop(entering))
                {
                    _entry.NotifyPromoted();
                }
                else
                {
                    // Nothing to promote. Rather than hold forever, let the shot finish and land on
                    // whatever the view already is.
                    _entry.NotifyPromoted();
                    _logger.Warning("Menu", "The mission-entry shot had no backdrop left to promote.");
                }
            }

            // The beat holds until the picture under it is final -- the lighting built and converged,
            // the temporal passes settled -- which is what the warm-up decides. The camera keeps
            // drifting while it waits, so a cold first-time bake reads as a longer shot rather than a
            // frozen one.
            if (_entry.Phase == MissionEntryPhase.Hold && !IsWarmingUp && !_loadSequence.IsActive)
            {
                _entry.NotifyReady();
            }

            // Nothing takes input for the length of the shot -- not the shell, not the player, not
            // the free camera. The shell still ANIMATES, so its fade plays out; it just hears nothing.
            if (_entry.IsCameraActive && !state.Console.IsOpen)
            {
                _window.Input.Captured = true;
            }

            // The character standing at the spawn, visible for the dive and taken away once the
            // camera is close enough that its head fills the frame. Placed and re-skinned every frame
            // it is shown rather than merely switched on: the body is uploaded as a rest mesh and has
            // no pose until something puts one in it.
            if (_soldiers is { } bodies)
            {
                if (_entry.ShowPlayerBody && _mission.Player is { } standingBody)
                {
                    // Minus the yaw, for the reason the third-person follow states at length: a
                    // heading is a gamma and turns the other way. This shot has always faced the
                    // character the wrong way about; it went unnoticed because the dive ends inside
                    // their head and the level's own spawn heading is often near zero, where the
                    // mirror shows nothing.
                    bodies.StandPlayerBodyAt(standingBody.Body.Position, -standingBody.Body.Yaw);
                }
                else
                {
                    bodies.Simulation.HidePlayerBody();
                }
            }

            // The shot's own smear, from its own movement; handed back to the player's setting after.
            ApplyEntryMotionBlur();

            if (_entry.Phase == MissionEntryPhase.Done)
            {
                _entry.Reset();
                _mission.Soldiers?.HidePlayerBody();
                ApplyGameplayMotionBlur();
                ReportWatchedFramePassesIn(1);

                // The eye is drawn between its last two ticks, and it has just been somewhere else
                // entirely; without dropping the pair the first gameplay frame interpolates across
                // the whole descent.
                _logger.Information("Menu", "The mission-entry shot has handed over.");
            }
        }
    }

    /// <summary>Walks the title and mission cycles the command line asked for.</summary>
    private void RunSessionCycles(FrameLoopState state)
    {
        // -titlecycle changes GAME on a fixed cadence, taking exactly the path the menu's selector
        // takes. Latched on the frame count for the same reason -missioncycle is, and placed beside
        // it because it is the same kind of flag: the switch is the largest teardown the session
        // performs and a person clicking a dropdown was the only way to reach it.
        if (_options.TitleCycleFrames > 0)
        {
            long titleLeg = _loop.Clock.FrameCount / _options.TitleCycleFrames;
            if (titleLeg != _titleCycleLeg)
            {
                _titleCycleLeg = titleLeg;
                RequestNextInstalledTitle();
            }
        }

        // -missioncycle leaves the level and re-enters it on a fixed cadence, taking exactly the two
        // paths the pause menu's Abandon and the campaign's Play take. It is here so it runs against
        // a fully built frame rather than during start-up, and it is latched on the frame count so
        // both legs happen once each rather than every frame past the mark.
        if (_options.MissionCycleFrames > 0 && state.Shell is not null)
        {
            long leg = _loop.Clock.FrameCount / _options.MissionCycleFrames;
            if (leg != _missionCycleLeg)
            {
                _missionCycleLeg = leg;
                if (_session.Mission is { } cycling)
                {
                    _logger.Information("Mission", $"-missioncycle: abandoning {cycling.Id}.");
                    _missionCycleMission = cycling.Id;
                    ReleasePlayedMission();
                    state.Shell.ClosePauseMenu();
                    state.Shell.OpenOn("campaign");
                    _menuFlow.MenuMode = true;
                    _menuFlow.PauseMenu = false;
                    _menuFlow.ShowcasePending = true;
                }
                else if (_missionCycleMission > 0)
                {
                    _logger.Information("Mission", $"-missioncycle: playing {_missionCycleMission} again.");
                    state.Shell.ClosePauseMenu();
                    _menuFlow.MenuMode = false;
                    _menuFlow.PauseMenu = false;
                    ApplyGameplayMotionBlur();
                    StartMission(_missionCycleMission);
                }
            }
        }
    }

    /// <summary>Lets Escape skip a cutscene or bring the pause menu up over the level.</summary>
    private void HandleEscape(FrameLoopState state)
    {
        // Escape brings the shell up over the level as a pause menu. It goes AFTER the shell's own
        // update, so the frame that opens the menu is not also the frame the menu reads a back press
        // on -- and after it the shell has marked the frame captured, which is what stops the Escape
        // that closed the menu from opening it again on the same pass.
        //
        // It also goes BEFORE the handheld computer below, which owns Escape while it is open: closing
        // the computer clears the flag, so a check placed after it would see a closed computer and a
        // still-pressed Escape and pause the game in the same breath.
        if (!_menuFlow.MenuMode
            && !state.Console.IsOpen
            && _mission.Cutscenes?.IsPlaying == true
            && _window.Input.Keyboard.WasPressed(Input.KeyCode.Escape))
        {
            _mission.Cutscenes.RequestBreak();
            _cutsceneSounds.StopAll();
            _mission.Objectives?.ClearCutsceneMessages();
        }

        if (!_menuFlow.MenuMode
            && !state.Console.IsOpen
            && !_computerOpen
            && !_session.FailedMenu
            && _mission.Cutscenes?.IsPlaying != true
            && TitlePlayer is not { IsCutscenePlaying: true } and not { IsMenuEnabled: false }
            && state.Shell is not null
            && _session.Mission is not null
            && _window.Input.Keyboard.WasPressed(Input.KeyCode.Escape))
        {
            _menuFlow.MenuMode = true;
            _menuFlow.PauseMenu = true;
            state.Shell.OpenPauseMenu();

            // The pointer belongs to the menu now. The look is what holds the cursor captive, so the
            // player is told to let go of it rather than the lock being fought over each frame.
            _mission.Player?.HoldStill();
            _window.CursorLocked = false;

            // The eye is drawn between its last two ticks and neither of them moves again until the
            // menu closes, so the pair is dropped: without this the frozen picture rocked back and
            // forth between two positions a tick apart as the blend swept on.
            _logger.Information("Menu", "Paused.");
        }
    }

    /// <summary>Fires the console statements <c>-runat</c> scheduled for this frame.</summary>
    private void RunDeferredCommands(FrameLoopState state)
    {
        // -runat fires a console statement once on a named frame, after the world exists. Latched
        // by index so a statement runs exactly once however long the capture goes on.
        if (_options.DeferredCommands.Count > 0)
        {
            for (int i = 0; i < _options.DeferredCommands.Count; i++)
            {
                (int frame, string statement) = _options.DeferredCommands[i];
                if (_deferredCommandsFired.Contains(i) || _loop.Clock.FrameCount < frame)
                {
                    continue;
                }

                _deferredCommandsFired.Add(i);
                _logger.Information("Console", $"-runat {frame}: {statement}");
                state.Console.System.Execute(statement, echo: true);
            }
        }
    }

    /// <summary>Reads the flashlight switch and the handheld computer, which own the keys while they are in use.</summary>
    private void UpdatePlayerDevices(FrameLoopState state)
    {
        // The remaster's flashlight, before the map computer's key: it is read only while the player's
        // own view is on screen, which the computer opening this frame has not yet changed.
        UpdateFlashlightSwitch(_menuFlow.MenuMode, state.Console.IsOpen);

        // IGI1's handheld is a game mode, entered by the retail C binding. It owns the pointer and
        // freezes player/camera input while the world continues its fixed-tick simulation.
        if (!_menuFlow.MenuMode && !state.Console.IsOpen && _mission.Player is not null
            && (TacticalComputer is not null || _mapComputer is { IsReady: true }))
        {
            // -computercam presses C on a named frame, so the transition can be captured from a
            // moment somebody can ask for again. Latched, not held: it must open the computer once
            // and then leave the key alone, or every frame past it would toggle.
            bool automaticOpen = _options.ComputerCamFrame > 0
                && !_computerCamFired
                && _loop.Clock.FrameCount >= _options.ComputerCamFrame;
            if (automaticOpen)
            {
                _computerCamFired = true;
            }

            _mission.Player.InputProfile = _configuration.Active;
            bool toggleComputer = _mission.Player.WasActionPressed(_window.Input, PlayerAction.MapComputer) || automaticOpen;
            bool closeComputer = _computerOpen && _window.Input.Keyboard.WasPressed(Input.KeyCode.Escape);

            if (TacticalComputer is { } tacticalComputer)
            {
                // Real elapsed time, on its own clock. The transition is presentation: it must take
                // the same wall-clock second whatever the frame rate, and it must keep running while
                // the simulation is paused behind it.
                int computerNow = _loop.TimeSource.Milliseconds;
                float computerElapsed = Math.Clamp((computerNow - state.LastComputerMilliseconds) / 1000f, 0f, 0.1f);
                state.LastComputerMilliseconds = computerNow;

                CameraPose eye = CameraPose.FromCamera(_mission.Player.Camera);

                // The field of view gameplay is being drawn at, which is where the climb leaves from.
                // Taken at the default rather than from the live sight: a player who opens the map
                // down a scope is the one case it differs, and inheriting a scoped field of view
                // there would start the shot already halfway zoomed in.
                float gameplayFieldOfView = VerticalFieldOfView(
                    (float)_window.Width / Math.Max(1, _window.Height),
                    HumanView.DefaultTanX);

                if (!_computerOpen && toggleComputer)
                {
                    _computerOpen = true;
                    tacticalComputer.Open(_mission.Player.Body.Position);
                    _mission.Player.SetMapComputerOpen(true);
                    _computerFlight.BeginOpen(
                        eye,
                        gameplayFieldOfView,
                        tacticalComputer.Vantage(),
                        tacticalComputer.FieldOfView);

                    // The interface draws its own pointer, so the platform's would be a second,
                    // unrelated cursor over it.
                    _window.CursorVisible = false;
                    _window.CursorLocked = false;
                }
                else if (_computerOpen && (toggleComputer || closeComputer) && _computerFlight.CanClose)
                {
                    _computerFlight.BeginClose(
                        tacticalComputer.Vantage(),
                        tacticalComputer.FieldOfView,
                        eye,
                        gameplayFieldOfView);
                }

                if (_computerOpen)
                {
                    _computerFlight.Update(
                        computerElapsed,
                        eye,
                        tacticalComputer.Vantage(),
                        tacticalComputer.FieldOfView);

                    // Only once the display has settled. A drag or a page change that landed during
                    // the climb would move a map the player cannot read yet.
                    if (_computerFlight.IsInteractive)
                    {
                        tacticalComputer.Update(
                            _window.Input,
                            computerElapsed,
                            _mission.Player.Body.Position,
                            _window.Width,
                            _window.Height);
                    }

                    // The computer keeps the view — and the player's input — until the fall has
                    // finished landing, so the last frame of the shot is the first frame of play.
                    if (_computerFlight.Phase == MapComputerPhase.Done)
                    {
                        _computerOpen = false;
                        _computerFlight.Reset();
                        _mission.Player.SetMapComputerOpen(false);
                    }

                    _window.Input.Captured = true;
                }
            }
            else if (_mapComputer is { IsReady: true } retailComputer)
            {
                if (toggleComputer || closeComputer)
                {
                    _computerOpen = toggleComputer ? !_computerOpen : false;
                    if (_computerOpen)
                    {
                        retailComputer.Open(_mission.Player.Body.Position);
                    }
                    _mission.Player.SetMapComputerOpen(_computerOpen);
                    // Retail draws COMPUTER:pointer.spr. Leaving the platform cursor visible put a
                    // second, unrelated pointer over it.
                    _window.CursorVisible = false;
                    _window.CursorLocked = false;
                }

                if (_computerOpen)
                {
                    retailComputer.Update(_window.Input, _window.Width, _window.Height);
                    _window.Input.Captured = true;
                }
            }
        }
    }

    /// <summary>Arms, cycles and swaps the level behind the menu.</summary>
    private void UpdateMenuBackdrop(FrameLoopState state)
    {
        // The showcase's first backdrop, asked for a couple of menu frames in so the shell is already on
        // screen. It is built on a worker like every other one; until it arrives the menu sits over its
        // dark backdrop rather than over a blank blue clear.
        if (_menuFlow.OverShowcase && _menuFlow.ShowcasePending && _loop.Clock.FrameCount >= 2)
        {
            _menuFlow.ShowcasePending = false;

            // -entrycam's mission stands in for the default showcase, so the backdrop it presses Play
            // over is built through exactly the path a player's selection would have built it through.
            int firstShowcase = _options.EntryCamMission > 0 ? _options.EntryCamMission : ShowcaseMissionId;
            _menuFlow.CurrentShowcaseMissionId = firstShowcase;
            RequestBackdrop(state.PreviewLoader, firstShowcase);
        }

        // The backdrop is made playable here, on the frames where nothing is happening but a menu.
        // A couple of milliseconds is invisible at the uncapped rate the menu runs at, and the work
        // is finished long before anybody has finished reading a briefing.
        if (_menuFlow.OverShowcase && _arming is { IsArmed: false })
        {
            _arming.Step(IdleArmingBudgetMilliseconds);

            // The level is resident: the decoded pictures nobody took are dropped, and the staging
            // the uploads went through is given back rather than held for the next level.
            if (_arming.IsArmed)
            {
                _memory.ReleaseLoadLeftovers(_world, _detailMaps);
            }
        }

        // -entrycam presses Play the moment the backdrop it was pointed at is armed, which is the
        // state a player's press would find it in after a second of reading. Waiting for the arming
        // rather than only for the backdrop is what keeps the measurement honest.
        if (state.EntryCamPending && _menuFlow.OverShowcase && _menuBackdrop.Preview is { } entryBackdrop
            && entryBackdrop.MissionId == _options.EntryCamMission
            && _arming is { IsArmed: true })
        {
            // -entrytime: the time chosen first, as the dropdown chooses it, and Play a second later
            // over the backdrop that choice re-lit.
            if (_options.EntryTime != MissionTimeOfDay.Default && state.EntryTimeChosenAt < 0)
            {
                state.EntryTimeChosenAt = _loop.Clock.FrameCount;
                state.ShellHostForEntryCam?.SetMissionConditions(_conditions with { TimeOfDay = _options.EntryTime });
            }

            bool settled = state.EntryTimeChosenAt < 0
                || _loop.Clock.FrameCount - state.EntryTimeChosenAt >= EntryTimeSettleFrames;

            if (settled)
            {
                state.EntryCamPending = false;
                _logger.Information("Menu", $"-entrycam is pressing Play on mission {entryBackdrop.MissionId}.");
                state.ShellHostForEntryCam?.StartMission(
                    entryBackdrop.MissionId,
                    _configuration.Active?.Difficulty ?? (int)AiDifficulty.Normal);
            }
        }

        // -backdropcycle walks the campaign's rows on a fixed cadence, taking the same path a player
        // moving down the mission list takes: it asks the preview loader for the next mission and
        // lets the ordinary handover below adopt it. Latched on the frame count so each row is asked
        // for once, and only while the menu is actually up over a backdrop.
        if (_options.BackdropCycleFrames > 0 && _menuFlow.OverShowcase && state.PreviewLoader is not null)
        {
            long row = _loop.Clock.FrameCount / _options.BackdropCycleFrames;
            if (row != _menuFlow.BackdropCycleRow)
            {
                _menuFlow.BackdropCycleRow = row;
                int[] order = [.. _missions.InCampaignOrder().Select(m => m.Id)];
                if (order.Length > 0)
                {
                    int next = order[(int)(row % order.Length)];
                    if (next != _menuFlow.CurrentShowcaseMissionId)
                    {
                        _logger.Information("Menu", $"-backdropcycle: selecting mission {next}.");
                        _menuFlow.PendingPreviewMissionId = null;
                        _menuFlow.CurrentShowcaseMissionId = next;
                        RequestBackdrop(state.PreviewLoader, next);
                    }
                }
            }
        }

        // -presetcycle applies the next graphics preset on a fixed cadence, the way the settings page does.
        if (_options.PresetCycleFrames > 0)
        {
            long step = _loop.Clock.FrameCount / _options.PresetCycleFrames;
            if (step != _menuFlow.PresetCycleStep)
            {
                _menuFlow.PresetCycleStep = step;
                GraphicsPreset[] presets = [GraphicsPreset.Ultra, GraphicsPreset.Vanilla, GraphicsPreset.High, GraphicsPreset.Enhanced];
                GraphicsPreset preset = presets[(int)(step % presets.Length)];
                if (GraphicsPresetRegistry.Default.IsAllowed(preset))
                {
                    _logger.Information("Menu", $"-presetcycle: applying {preset}.");
                    GraphicsPresetRegistry.Default.Apply(preset);
                }
            }
        }

        // The build request, once the campaign selection has held still for a moment. The debounce is
        // only there to stop a run down the list queueing fourteen builds; it costs the render thread
        // nothing either way, because the build happens on a worker.
        if (_menuFlow.MenuMode
            && !_menuFlow.PauseMenu
            && _menuFlow.PendingPreviewMissionId is { } previewMissionId
            && _loop.TimeSource.Milliseconds - _menuFlow.PreviewRequestedAtMs > PreviewSettleMs)
        {
            _menuFlow.PendingPreviewMissionId = null;
            RequestBackdrop(state.PreviewLoader, previewMissionId);
        }

        // A backdrop whose mission's files were still arriving, asked for again until they are there.
        if (_menuFlow.OverShowcase && _backdropAwaitingFiles is { } awaitingFiles)
        {
            RequestBackdrop(state.PreviewLoader, awaitingFiles);
        }

        // The handover. Everything a backdrop is made of was built off the render thread; what is left
        // is the part that cannot be — a GL texture belongs to the context that made it — so the
        // renderers are constructed here, on the frame after the outgoing picture was held. Same shape
        // as the probe and distance-field bakes above.
        if (_menuFlow.OverShowcase && _menuFlow.HeldFrameReady)
        {
            bool changed = false;
            if (state.PreviewLoader is { } loader
                && loader.TryCollect(out MissionPreview? readyPreview) && readyPreview is not null)
            {
                _menuFlow.CurrentShowcaseMissionId = readyPreview.MissionId;
                AdoptPreview(readyPreview);
                changed = true;

                // Under the held frame, where the pause is not seen: the outgoing level and the
                // incoming one's decoding have just been through the heap.
                _memory.TrimAfterLoad();

                // A soak reports where memory stands every tenth swap, so growth shows as a trend.
                if (_options.BackdropCycleFrames > 0 && ++_menuFlow.BackdropSwaps % 10 == 0)
                {
                    _memory.Report($"After {_menuFlow.BackdropSwaps} backdrop swap(s)");
                }

                // Once the first backdrop is up, every other mission's files are read through in the
                // background, so whichever the player settles on next builds from memory.
                if (!_menuFlow.MissionsSwept)
                {
                    _menuFlow.MissionsSwept = true;
                    state.MissionWarmer?.WarmAll(_missions.InCampaignOrder().Select(mission => mission.Id));
                }
            }
            else if (_showcaseCamera is { IsCutDue: true } showcase)
            {
                // No new level, just the next place in this one. The camera does not take the cut
                // itself, so that it happens here — after the outgoing frame has been held.
                showcase.CommitCut();
                changed = true;
            }
            else if (_menuFlow.BakedFieldForBackdrop is { } landedField)
            {
                // The backdrop's field, baked behind the menu, adopted under the held frame so the
                // level's cross-over to its real-time lighting is dissolved to rather than watched.
                _menuFlow.BakedFieldForBackdrop = null;
                _gi.AdoptDistanceField(landedField.Scene, landedField.Atlas, fromCache: false);
                changed = true;
            }

            _menuFlow.HeldFrameReady = false;

            // The held frame is now the whole picture; it dissolves off to reveal what is behind it,
            // once the lighting behind it has converged.
            if (changed)
            {
                _menuFlow.Dissolve.SnapTo(_menuFlow.HeldFrameIsPicture ? 1f : 0f);
                _menuFlow.DissolveRevealPending = _menuFlow.HeldFrameIsPicture;
            }
        }
    }

    /// <summary>Reads the developer keys: captures, overrides, the debug view and the view toggle.</summary>
    private void HandleDeveloperKeys(FrameLoopState state)
    {
        // The camera is advanced on every pass of the loop, not only on the passes that render. The
        // pump clears the mouse's motion accumulator, and the loop pumps several times per rendered
        // frame — so consuming it only on a render threw most of the mouse movement away and made the
        // view lurch. It also runs on real elapsed time: a fixed step tied flying speed to the frame
        // rate, which is not what a person moving a mouse expects.
        // F12 toggles the debug overlay: the player's position and angle, and a crosshair readout of
        // whatever is under it — an object (with its task), a soldier, or nothing.
        if (_window.Input.Keyboard.WasPressed(Input.KeyCode.F12))
        {
            state.ShowDebug = !state.ShowDebug;
        }

        // F11 re-bakes the lighting from scratch, live. It throws away every cached field and the one
        // on screen, then starts tracing again from the same inputs this level loaded with, so the
        // light resolves back in from nothing over the next seconds -- the way to see a change to the
        // bake without reloading the level, and to watch it generate. Ignored where there is nothing to
        // bake (GI off, or the level's scene or cast not yet set up).
        // F8 puts the overrides folder in and out of the frame. Watched as a VARIABLE rather than as
        // a key press, so `r_overrides 0` from the console does the same thing as the key -- the bind
        // sets the variable and this notices, which is one path rather than two that must agree.
        //
        // Nothing happens with an empty folder: the flag is meaningful only against something to
        // compare with, and reloading a level's textures to show the same assets again would be a
        // long hitch for no picture.
        // Any layer with files counts, switched on or not: with every layer off there is still
        // something the switch is the difference between once one comes back on.
        if (_overridesApplied != RenderCVars.OverridesEnabled.Value && _overrides.HasContent)
        {
            _overridesApplied = RenderCVars.OverridesEnabled.Value;
            _overrides.Bypassed = !_overridesApplied;

            int swapped = ApplyOverrideChange(out _);

            _logger.Information(
                "Overrides",
                _overridesApplied
                    ? $"Overrides on (F8); {swapped} resource(s) swapped in."
                    : $"Overrides off (F8); {swapped} resource(s) back to the game's own.");
        }

        if (_window.Input.Keyboard.WasPressed(Input.KeyCode.F11)
            && RenderCVars.GlobalIllumination.Value
            && _scene is not null
            && _baked.CanRebake)
        {
            _baked.Rebake(_scene, _loop.TimeSource.Milliseconds);
        }

        // F1 swaps between flying and walking. The player takes the camera's position when it is
        // switched on, so the swap happens where you were looking.
        if (_window.Input.Keyboard.WasPressed(Input.KeyCode.F1) && TitlePlayer is not null)
        {
            ToggleTitlePlayer();
        }
        else if (_window.Input.Keyboard.WasPressed(Input.KeyCode.F1) && _mission.Player is not null && _camera is not null)
        {
            _playerMode = !_playerMode;
            if (_playerMode)
            {
                if (!_mission.Player.Place(_ground, _camera.Position, _camera.Yaw))
                {
                    _playerMode = false;
                    _logger.Warning("Player", "Nothing to stand on here; staying with the free camera.");
                }
                else
                {
                    // Place() drops the player's presentation pair because this is a teleport.
                }
            }
            else
            {
                _camera.MoveTo(_mission.Player.Camera.Position, _mission.Player.Body.Yaw, _mission.Player.Body.Pitch);
            }

            _logger.Information("Player", _playerMode ? "Walking." : "Flying.");
        }
    }

    /// <summary>Advances the day and the lamps on real elapsed time.</summary>
    private void AdvanceWorldClocks(FrameLoopState state)
    {
        // The day/night cycle is advanced on every pass, on real elapsed time, so holding [ or ] sweeps
        // the sun smoothly whatever the frame rate -- the same reasoning the camera below is advanced by.
        // The keyboard's own capture flag keeps the console's typing out of it.
        //
        // The automatic cycle, on top of the scrub, runs only while a mission is actually being played:
        // Menu mode covers the front end and the pause menu alike, and neither is time the player is
        // spending in the level. The cycle banks the paused seconds nowhere, so coming back does not
        // release a jump in the sun.
        {
            int dayNightNow = _loop.TimeSource.Milliseconds;
            float dayNightElapsed = Math.Clamp((dayNightNow - _dayNightMilliseconds) / 1000f, 0f, 0.1f);
            _dayNightMilliseconds = dayNightNow;
            bool clockRuns = RenderCVars.DayNightCycle.Value && !_menuFlow.MenuMode && _session.Mission is not null;
            _dayNight.Update(
                _window.Input.Keyboard,
                dayNightElapsed,
                clockRuns ? RenderCVars.DayLength.Value * 60f : 0f);
        }

        // The level's lamps, on every pass and on real elapsed time, for exactly the reasons the sweep
        // above is: a fitting takes a couple of hundred milliseconds to go out and about a second to
        // come back, and a fade advanced once per rendered frame would be a different fade on every
        // machine. Cheap enough to be unconditional -- a level nobody has touched walks an array of
        // steady lamps and returns. The daylight's photocells ride on the same pass, reading the sun
        // the sweep above has just moved.
        UpdateLamps(_loop.TimeSource.Milliseconds, audible: !_menuFlow.MenuMode && _session.Mission is not null);
    }

    /// <summary>Steers the free camera and the sun gizmo.</summary>
    private void UpdateFlyingView(FrameLoopState state)
    {
        // G shows the sun gizmo, but only while flying: it takes the cursor to drag its rings, which the
        // walking player needs for looking. Leaving the free camera puts it away.
        if (_window.Input.Keyboard.WasPressed(Input.KeyCode.G) && !_playerMode)
        {
            _sunGizmo.Toggle();
        }

        if (_playerMode)
        {
            _sunGizmo.Hide();
        }

        if (_camera is not null)
        {
            int nowMilliseconds = _loop.TimeSource.Milliseconds;
            float elapsed = Math.Clamp((nowMilliseconds - state.LastCameraMilliseconds) / 1000f, 0f, 0.1f);
            state.LastCameraMilliseconds = nowMilliseconds;

            float rawX = _window.Input.Mouse.DeltaX;
            float rawY = _window.Input.Mouse.DeltaY;
            bool right = _window.Input.Mouse.IsDown(Input.MouseButton.Right);

            // Resolved per frame rather than cached, so the options menu's sensitivity slider takes
            // effect on the next frame instead of the next launch. It is two multiplies and a Pow.
            MouseLookSettings look = MouseLookSettings.FromProfile(_configuration.Active);
            if (_mission.Player is not null)
            {
                _mission.Player.LookSettings = look;
            }

            if (Viewpoint is { } lookViewpoint)
            {
                lookViewpoint.LookSettings = look;
            }

            if (state.Console.WantsPointer)
            {
                // The developer console is a pointer-driven tool: its panels are dragged and
                // resized. This branch comes first and is repeated every pass for the same reason
                // the pause menu's is -- the lock below would otherwise be re-asserted a frame
                // later and the cursor would snap back to the centre under the reader's hand.
                // HoldStill matters just as much here: taking the cursor back on close warps the
                // pointer, and without re-arming the settling frame that warp is read as aim.
                // The system cursor stays hidden: the console draws its own, as the shell does,
                // so the pointer looks the same everywhere and is drawn over the panels rather
                // than by the compositor on top of them.
                _mission.Player?.HoldStill();
                _window.CursorLocked = false;
            }
            else if (MenuOwnsPointer(_menuFlow.PauseMenu, _session.FailedMenu))
            {
                // A paused view does not turn, and the cursor has to be free to reach the menu. The
                // release is repeated every pass rather than done once on the way in, so nothing can
                // quietly take the pointer back while the menu is up; it also re-arms the look's
                // settling frame, so the warp that re-capturing the cursor produces on resume is not
                // read as a movement the player made.
                //
                // The mission-failed screen belongs here for the same reason. It arrives while the
                // player is dead and the death camera is orbiting, and EVERY branch below this one
                // takes the pointer: the orbit, and then -- because _playerMode outlives the player --
                // the look, whose Look() sets IsCapturingMouse and locks. The screen released the
                // cursor each frame and the branch below re-took it, which is the pointer that
                // flashed and would not move. Standing every camera branch down is what actually
                // settles it; excluding them one at a time only moves the fight further down.
                _mission.Player?.HoldStill();
                _window.CursorLocked = false;
            }
            else if (Viewpoint?.IsDeathViewActive == true)
            {
                // Dead: the mouse orbits the death camera around the body rather than turning a head that
                // is no longer taking input -- HumanCamera_DeathCameraControlInput. The cursor stays locked
                // so the orbit keeps turning however far the mouse is moved.
                Viewpoint?.ControlDeathView(rawX, rawY);
                _window.CursorLocked = !state.CaptureMode;

                // The look is not driving the view this frame, so it must not consider itself
                // capturing: whenever the orbit hands control back, the first frame of look is a
                // settling one and the re-lock's re-centring is absorbed rather than aimed with.
                _mission.Player?.ReleaseLook();
            }
            else if (_mission.Player is { IsOnLadder: true }
                or { IsInWorldInteraction: true, IsMountedStationaryGun: false })
            {
                // A mounted gun is not among these: its look turns the barrel, so it falls through
                // to the ordinary first-person look below.
                //
                // On a ladder the mouse orbits the third-person interaction camera around the climbing
                // body, as the original's mode-3 camera does -- HumanCamera_ThirdPersonControlInput -- so
                // you can look around while you climb rather than being locked behind the character.
                Viewpoint?.ControlInteractionView(rawX, rawY);
                _window.CursorLocked = !state.CaptureMode;
                _mission.Player?.ReleaseLook();
            }
            else if (_computerOpen)
            {
                // The computer takes the pointer outright. Re-arming the look here is what stops
                // closing it from throwing the view: dropping the lock restores the desktop pointer,
                // and taking it back re-centres the backend's virtual one.
                _window.CursorLocked = false;
                _mission.Player?.ReleaseLook();
            }
            else if (_mission.Cutscenes?.IsPlaying == true)
            {
                _mission.Player?.HoldStill();
                _window.CursorLocked = false;
            }
            else if (_playerMode && TitlePlayer is not null)
            {
                // A title's own player takes its look on the tick, as the original does; the frame only
                // gathers what the mouse and the keys did since the last one.
                GatherTitleInput();
                _window.CursorLocked = !state.CaptureMode;
            }
            else if (_playerMode && _mission.Player is not null)
            {
                // Looking is per frame so the view is as smooth as the display allows; the body itself
                // moves on the fixed tick below, so it steps identically whatever the frame rate.
                _mission.Player.Look(_window.Input, elapsed);
                _window.CursorLocked = !state.CaptureMode && _mission.Player.IsCapturingMouse;
            }
            else if (_sunGizmo.Active)
            {
                // The sun gizmo owns the cursor while it is up: free it so its rings can be dragged, and
                // leave the camera where it is so a drag turns the sun rather than the view. The gizmo is
                // a free-camera tool, so this only ever runs while flying.
                _window.CursorLocked = false;
            }
            else
            {
                _camera.Update(ViewInput, elapsed);

                // The cursor is locked away while a mouse button is held, the way a scene editor's is,
                // so the view keeps turning however far the mouse is dragged. It comes back on release.
                _window.CursorLocked = !state.CaptureMode && _camera.IsCapturingMouse;
            }

            if (_options.LogCamera && (right || rawX != 0f || rawY != 0f))
            {
                _logger.Information(
                    "Camera",
                    $"rmb={(right ? 1 : 0)} raw=({rawX:0.##},{rawY:0.##}) pos=({_camera.Position.X:0.##},"
                        + $"{_camera.Position.Y:0.##},{_camera.Position.Z:0.##}) fwd={_camera.Camera.Orientation.Row3} "
                        + $"right={_camera.Camera.Orientation.Row1} dt={elapsed:0.####}");
            }
        }
    }

    /// <summary>Draws what goes over the world: the shell, the warm-up, the title card and the console.</summary>
    private void DrawPresentation(FrameLoopState state)
    {
        if (_options.IsBenchmark && _loop.Clock.FrameCount == BenchmarkSchedule.WarmUpFrames)
        {
            state.FramePerformance.Reset();
            _benchmarkAllocStart = GC.GetTotalAllocatedBytes();
            _benchmarkGcStart = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        }
        if (!_options.IsBenchmark || _loop.Clock.FrameCount <= BenchmarkSchedule.MeasurementEndFrame)
        {
            state.FramePerformance.NoteFrame();
        }

        if (_scene is not null && _world is not null && _camera is not null)
        {
            RenderWorld(state, state.Console);
        }

        // The cinematic shell draws over the resolved showcase scene, in 2D on the back buffer, before
        // the console (which goes over even the shell).
        // The shell keeps being DRAWN through the mission-entry shot even though the menu is over, so
        // that it can fade rather than vanish. It hears nothing while it does.
        if ((_menuFlow.MenuMode || _entry.IsRunning) && state.Shell is not null)
        {
            // Exactly one layer may draw a pointer. The console is drawn after this and over it,
            // so while it is up it owns the cursor; otherwise the reader sees two arrows.
            state.Shell.Stack.PointerSuppressed = state.Console.IsOpen;

            _device.BeginStageTiming(RenderStage.UserInterface);
            _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
            var shellPainter = UiPainter.Create(state.Batch, new LayoutContext(_window.Width, _window.Height));
            state.Batch.Begin();

            // The going-in sequence's picture: the level exactly as it is drawn, before any of the
            // interface goes over it. Taken here for the same reason the dissolve's hold is.
            CaptureLoadPictureIfWanted();

            // None of the backdrop machinery applies to the pause menu: its backdrop is the mission,
            // already drawn on this very frame, and there is no swap for it to dissolve between.
            if (!_menuFlow.PauseMenu)
            {
                // Before the showcase level has loaded there is no scene to draw, so fill a dark backdrop
                // rather than leaving the frame's blue clear behind the menu.
                if (_scene is null)
                {
                    shellPainter.FillRect(new UiRect(0f, 0f, _window.Width, _window.Height), Bgra32.FromRgb(12, 14, 18));

                    // Nothing on screen to dissolve from, so the hold is a no-op and the first backdrop is
                    // simply revealed. Without this the swap would wait forever on a hold that never came.
                    if (_menuFlow.HoldFrameWanted)
                    {
                        _menuFlow.CompleteHold(picture: false);
                    }
                }
                else
                {
                    // The outgoing level, caught here: after the 3D pass, before any of the menu is drawn
                    // over it, so what is held is the picture alone.
                    if (_menuFlow.HoldFrameWanted)
                    {
                        _menuFlow.HoldFrame(_window.Width, _window.Height);
                        _menuFlow.CompleteHold(picture: true);
                    }

                    // The dissolve, over the resolved scene and UNDER the shell: the two levels blend into
                    // each other while the menu stays lit and responsive on top of both.
                    _menuFlow.DrawDissolve(shellPainter, _window.Width, _window.Height);
                }
            }

            // The whole interface behind one opacity. It has to be pushed HERE rather than left to
            // each screen's own transition, because a screen deliberately draws its header and footer
            // outside that fade and the stack draws the pointer outside every screen — so only a push
            // around the whole draw takes all of it, chrome and cursor included.
            float shellOpacity = MathF.Min(
                _entry.IsRunning ? _entry.ShellOpacity : 1f,
                _loadSequence.ShellOpacity);
            if (shellOpacity > 0.001f)
            {
                // The frame so far — the level, the dissolve over it — blurred into the levels the
                // shell's backdrop and every glass panel sample. The blur closes the 2D pass, so the
                // batch is flushed around it and the shell draws on a painter that knows the levels.
                state.Batch.End();
                shellPainter = UiPainter.Create(state.Batch, shellPainter.Context, _menuBackdrop.Glass(_device.BlurFrameForBackdrop(), SafeWindowWidth, SafeWindowHeight));
                state.Batch.Begin();

                shellPainter.PushOpacity(shellOpacity);
                state.Shell.DrawBackdrop(shellPainter);
                state.Shell.Draw(shellPainter);
                shellPainter.PopOpacity();
            }

            // The shot's own framing goes over the interface at full strength: the bars and the title
            // belong to the cinematic, not to the menu, and they must not fade out with it.
            if (_entry.IsRunning)
            {
                DrawEntryFraming(shellPainter);
            }

            state.Batch.End();
            _device.EndStageTiming();
        }

        // The level, withheld while it settles. Over the HUD and the menus because it stands in for
        // the whole picture, and under the console because the console has to be usable while a level
        // is warming -- that is exactly when somebody is looking into why it is.
        DrawMissionWarmUp(state.Batch);

        // The mission's caption, standing in the level under the shot: over the defocus, so it is the
        // one sharp thing while the lighting settles, and under the state.Console.
        DrawWorldTitle(state.Batch);

        // The console goes over everything, including the menus, so it is the last thing drawn.
        // WantsDraw rather than IsOpen: the watch and performance panels can be pinned to stay up
        // with the console shut. It is false during a capture run whatever the reader pinned, so a
        // pinned overlay can never leak into a reference screenshot and break the vanilla gate.
        if (state.Console.WantsDraw)
        {
            _device.BeginStageTiming(RenderStage.UserInterface);
            _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
            state.Batch.Begin();
            state.Console.Draw(state.Batch, _window.Width, _window.Height, UiScale.Resolve(_options.UiScale, _window.Width, _window.Height));
            state.Batch.End();
            _device.EndStageTiming();
        }
    }

    /// <summary>Writes this frame's captures, completes a renderer switch, and ends a timed run.</summary>
    private void FinishFrame(FrameLoopState state)
    {
        // A flight writes one numbered frame per rendered frame -- stem.NNNN.ext beside the given
        // screenshot path -- so the sequence shows the temporal filters resolving under motion. Written
        // here, after everything 2D is on the back buffer, exactly where the single still is captured.
        // -entrycam writes its frames the same way, so the shot can be flipped through afterwards.
        // A camera move is the one thing a single still cannot show, and the entry is the one camera
        // move nobody can trigger at a repeatable moment by hand.
        if ((_options.HasFlight || (_options.EntryCamMission > 0 && _entry.IsRunning))
            && _options.ScreenshotPath is { } flightPath
            && _scene is not null)
        {
            // Not while a flight still waits out its -flightdelay: those frames are the opening still, the
            // same picture hundreds of times over, eight megabytes each at 1080p -- a capture that held the
            // view for six hundred frames before moving filled the disk with them.
            if (!_options.HasFlight || state.FlightFrame >= _options.FlightDelay)
            {
                Bgra32[] frame = new Bgra32[_window.Width * _window.Height];
                _device.CaptureFrame(_window.Width, _window.Height, frame);

                string directory = Path.GetDirectoryName(flightPath) ?? string.Empty;
                string stem = Path.GetFileNameWithoutExtension(flightPath);
                string extension = Path.GetExtension(flightPath);
                string numbered = Path.Combine(directory, FormattableString.Invariant($"{stem}.{state.FlightFrame:0000}{extension}"));
                BitmapWriter.Write(numbered, _window.Width, _window.Height, frame);
            }

            state.FlightFrame++;
        }

        // Authored-scene capture: write numbered stills of the requested scene every 90 logic ticks
        // (3 s) up to the still budget. Test-only; driven by environment variables.
        // A chained intro resets its scene-local clock. Do not carry the previous scene's next
        // capture threshold into the next scene and silently miss its early/short shots.
        if (state.CaptureCutscene && _mission.Cutscenes?.ActiveScene is { } observedScene
            && !ReferenceEquals(state.PreviousCapturedScene, observedScene))
        {
            state.PreviousCapturedScene = observedScene;
            state.CutsceneCaptureTick = state.CutsceneCaptureStartTick;
        }
        if (state.CaptureCutscene
            && state.CutsceneCaptureCount < state.CutsceneCaptureLimit
            && _mission.Cutscenes?.ActiveScene is { } activeCutscene
            && (state.CutsceneCaptureScene <= 0 || activeCutscene.Id == state.CutsceneCaptureScene)
            && activeCutscene.Ticks >= state.CutsceneCaptureTick)
        {
            Directory.CreateDirectory(state.CutsceneCaptureDirectory!);
            Bgra32[] capture = new Bgra32[_window.Width * _window.Height];
            _device.CaptureFrame(_window.Width, _window.Height, capture);
            int missionId = _session.Mission?.Id ?? 0;
            string path = Path.Combine(
                state.CutsceneCaptureDirectory!,
                state.CutsceneCaptureEvery > 0
                    ? FormattableString.Invariant($"mission-{missionId:00}-scene-{activeCutscene.Id}-tick-{activeCutscene.Ticks:000000}.bmp")
                    : FormattableString.Invariant($"mission-{missionId:00}-scene-{activeCutscene.Id}-tick-{activeCutscene.Ticks:000000}-frame-{_loop.Clock.FrameCount:000000}.bmp"));
            BitmapWriter.Write(path, _window.Width, _window.Height, capture);
            _logger.Information("CutsceneCapture", $"Wrote {path}.");
            state.CutsceneCaptureTick += state.CutsceneCaptureEvery;
            state.CutsceneCaptureCount++;
        }

        AdvanceRendererSwitch();

        if (_options.ExitAfterFrames > 0 && _loop.Clock.FrameCount >= _options.ExitAfterFrames)
        {
            // A flight has already written its frames one by one; only a single-still capture writes here.
            if (!_options.HasFlight && _options.ScreenshotPath is { } screenshotPath)
            {
                Bgra32[] capture = new Bgra32[_window.Width * _window.Height];
                _device.CaptureFrame(_window.Width, _window.Height, capture);
                BitmapWriter.Write(screenshotPath, _window.Width, _window.Height, capture);
                _logger.Information("Desktop", $"Wrote {_window.Width}x{_window.Height} capture to {screenshotPath}.");
            }
            else if (_options.HasFlight)
            {
                _logger.Information(
                    "Desktop",
                    $"Wrote {state.FlightFrame} flight frame(s) at {_window.Width}x{_window.Height} beside {_options.ScreenshotPath}.");
            }

            if (_options.LightingDumpPath is { } lightingDumpPath)
            {
                WriteLightingDump(lightingDumpPath);
            }

            _mission.Soldiers?.LogAiSummary();
            state.Running = false;
        }
    }
}
