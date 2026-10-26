using OpenIGI.Engine.Tasks;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    // The map computer the original draws, its device model, and the tactical interface that can stand in for it.
    private IMapComputerPresenter? _mapComputer;
    private MapComputerDeviceRenderer? _mapComputerDevice;
    private ITacticalComputerPresenter? _tacticalComputer;
    private readonly MapComputerCamera _computerFlight = new();

    private IComputerHighlightCatalogue? _computerHighlights;
    private bool _computerOpen;
    private bool _computerCamFired;

    /// <summary>Gets the tactical interface when the preset asks for it and it built its faces.</summary>
    /// <remarks>
    /// Read per frame rather than latched at mission bring-up, so <c>r_computer_tactical</c> can be
    /// thrown from the console between one frame and the next — which is the only practical way to
    /// compare the redesign against the original it replaces. Both renderers are therefore built for
    /// every mission; each costs an archive or a cached atlas, and neither is worth a restart.
    /// </remarks>
    private ITacticalComputerPresenter? TacticalComputer =>
        HudCVars.TacticalComputer.Value && _tacticalComputer is { IsReady: true } ready ? ready : null;

    // The map gate of the level last asked about; rebuilt when another level comes up.
    private MapComputerGate? _mapGate;

    /// <summary>Whether the level lets the player read the map computer's map right now (<see cref="MapComputerGate"/>).</summary>
    private bool MapComputerAvailable()
    {
        if (_session.LevelRoot is not { } root)
        {
            return true;
        }

        if (_mapGate is null || !ReferenceEquals(_mapGate.LevelRoot, root))
        {
            _mapGate = new MapComputerGate(root, _loop.Variables);
        }

        return _mapGate.MapAvailable;
    }

    /// <summary>The map computer, its highlights, the tactical computer and the comms cards.</summary>
    private void MissionComputer(MissionBuild b)
    {
        LoadedMission loaded = b.Loaded;
        LevelTask levelRoot = b.LevelRoot;
        MissionStringResolver missionText = b.MissionText!;

        _computerHighlights = _presentation.Presenters.Create<ComputerHighlightRequest, IComputerHighlightCatalogue>(new ComputerHighlightRequest(levelRoot, _loop.Variables));
        _mapComputer = _presentation.Presenters.Create<MapComputerPresentationRequest, IMapComputerPresenter>(
            new MapComputerPresentationRequest(_device, _resources, _logger, missionText.Get, _hosted));
        if (_mapComputer is not null && !_mapComputer.Load(_fileSystem))
        {
            _mapComputer.Dispose();
            _mapComputer = null;
        }

        _mapComputerDevice = new MapComputerDeviceRenderer(_device, _resources, _logger);
        if (!_mapComputerDevice.Load(loaded))
        {
            _mapComputerDevice.Dispose();
            _mapComputerDevice = null;
        }

        // Both presentations are built, and r_computer_tactical decides which is drawn — see
        // GameHost.TacticalComputer. The retail pair above needs the installation's own archives; this
        // one needs only the shipped faces, so a mission whose computer.res is missing still has a map.
        _tacticalComputer = _presentation.Presenters.Create<MapComputerPresentationRequest, ITacticalComputerPresenter>(
            new MapComputerPresentationRequest(_device, _resources, _logger, missionText.Get));
        if (_tacticalComputer is not null && !_tacticalComputer.Load())
        {
            _tacticalComputer.Dispose();
            _tacticalComputer = null;
        }

        // The comms cards. Built here rather than beside the HUD below, because the messages belong to the
        // mission rather than to the loadout: a level whose player carries nothing still sends them.
        _messages?.Dispose();
        _messages = new StatusMessageRenderer(_device, _logger);
        if (!_messages.Load())
        {
            _messages.Dispose();
            _messages = null;
        }

        // The walls. Built here because two consumers want it: the player's collision, and the
        // soldiers' ground probe - a guard's declared Position is only where it starts, and without
        // the settle the retail integrator performs, floor-authored spawns bury the body to the waist.
        //
        // A promoted backdrop arrives with all three already built on a worker: the scene walk, the
        // world renderer and the collision index are exactly what AdoptPreview put up to draw the menu
        // over, and they are the same level.
        EndStage("objectives, highlights and the map computer");
    }

    /// <summary>
    /// Gets a value indicating whether a title's player standing in the mission uses the mission's computer -- the remastered
    /// one -- rather than its own: there is no mission player of the mission's own, and the mission has objectives to show.
    /// </summary>
    /// <remarks>
    /// Another game's player in a Project I.G.I. level carries its own game's computer, which knows nothing of the level's
    /// objectives; the mission's own computer, laid over the level the player stands in, does.
    /// </remarks>
    private bool TitleUsesMissionComputer =>
        HostedPlayer is null && TitlePlayer is not null && TacticalComputer is not null && ComputerObjectives is not null;

    /// <summary>
    /// Gets what the computer lists: the mission's own objectives where it has them -- Project I.G.I.'s, whoever plays them --
    /// else the title's player's own computer's, in a level of the title's own.
    /// </summary>
    private IComputerObjectives? ComputerObjectives =>
        (IComputerObjectives?)_mission.Objectives ?? (TitlePlayer as ITitlePlayerComputer)?.ComputerObjectives;

    /// <summary>Whether the computer offers its map: the mission's own gate where it has one, else the title's player's computer's.</summary>
    private bool ComputerMapOffered() =>
        _mission.Objectives is null && TitlePlayer is ITitlePlayerComputer titleComputer
            ? titleComputer.ComputerMapAvailable
            : MapComputerAvailable();

    /// <summary>The player the computer serves, where it stands in engine units and the eye it looks through; or none.</summary>
    /// <returns>The mission's own player, else a title's player using the mission's computer.</returns>
    private ComputerSubjectView? ComputerSubject() =>
        HostedPlayer is { } hosted ? new ComputerSubjectView(hosted.Body.Position, hosted.Camera)
        : TitleUsesMissionComputer && _playerMode && TitlePlayer is { } title ? new ComputerSubjectView(title.Position, TitleCamera(title))
        : null;

    /// <summary>Gets where the player the computer serves stands, in engine units, or none.</summary>
    private Vector3d? ComputerBodyUnits => ComputerSubject()?.BodyUnits;

    /// <summary>Whether the computer's key went down this frame, by the mission player's own bindings or the profile's.</summary>
    private bool ComputerKeyPressed()
    {
        if (HostedPlayer is { } hosted)
        {
            hosted.InputProfile = _configuration.Active;
            return hosted.WasActionPressed(_window.Input, PlayerAction.MapComputer);
        }

        return PlayerProfileInputBindings.WasPressed(_configuration.Active, _window.Input, PlayerInputBindings.ConfigName(PlayerAction.MapComputer));
    }

    /// <summary>The player the computer serves: where it stands, in engine units, and the eye the computer climbs from.</summary>
    /// <param name="BodyUnits">Where it stands.</param>
    /// <param name="Camera">Its eye.</param>
    private readonly record struct ComputerSubjectView(Vector3d BodyUnits, Camera3D Camera);

    /// <summary>Reads the flashlight switch and the handheld computer, which own the keys while they are in use.</summary>
    private void UpdatePlayerDevices(FrameLoopState state)
    {
        // The remaster's flashlight, before the map computer's key: it is read only while the player's
        // own view is on screen, which the computer opening this frame has not yet changed.
        UpdateFlashlightSwitch(_menuFlow.MenuMode, state.Console.IsOpen);

        // IGI1's handheld is a game mode, entered by the retail C binding. It owns the pointer and
        // freezes player/camera input while the world continues its fixed-tick simulation.
        if (!_menuFlow.MenuMode && !state.Console.IsOpen && ComputerSubject() is { } subject
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

            bool toggleComputer = ComputerKeyPressed() || automaticOpen;
            bool closeComputer = _computerOpen && TacticalComputer is not null && _window.Input.Keyboard.WasPressed(Input.KeyCode.Escape);

            // The computer always opens; whether its map is there to read is the level's to say.
            bool mapAvailable = ComputerMapOffered();

            if (TacticalComputer is { } tacticalComputer)
            {
                // Real elapsed time, on its own clock. The transition is presentation: it must take
                // the same wall-clock second whatever the frame rate, and it must keep running while
                // the simulation is paused behind it.
                int computerNow = _loop.TimeSource.Milliseconds;
                float computerElapsed = Math.Clamp((computerNow - state.LastComputerMilliseconds) / 1000f, 0f, 0.1f);
                state.LastComputerMilliseconds = computerNow;

                CameraPose eye = CameraPose.FromCamera(subject.Camera);

                // The field of view gameplay is being drawn at, which is where the climb leaves from.
                // Taken at the default rather than from the live sight: a player who opens the map
                // down a scope is the one case it differs, and inheriting a scoped field of view
                // there would start the shot already halfway zoomed in.
                float gameplayFieldOfView = VerticalFieldOfView(
                    (float)_window.Width / Math.Max(1, _window.Height),
                    HumanView.DefaultTanX);

                // Without the device there is no map to climb to: the pages come up over the player's own view.
                tacticalComputer.MapAvailable = mapAvailable;
                CameraPose vantage = mapAvailable ? tacticalComputer.Vantage() : eye;
                float vantageFieldOfView = mapAvailable ? tacticalComputer.FieldOfView : gameplayFieldOfView;

                if (!_computerOpen && toggleComputer)
                {
                    _computerOpen = true;
                    tacticalComputer.Open(subject.BodyUnits);
                    HostedPlayer?.SetMapComputerOpen(true);
                    _computerFlight.BeginOpen(
                        eye,
                        gameplayFieldOfView,
                        vantage,
                        vantageFieldOfView);

                    // The interface draws its own pointer, so the platform's would be a second,
                    // unrelated cursor over it.
                    _window.CursorVisible = false;
                    _window.CursorLocked = false;
                }
                else if (_computerOpen && (toggleComputer || closeComputer) && _computerFlight.CanClose)
                {
                    _computerFlight.BeginClose(
                        vantage,
                        vantageFieldOfView,
                        eye,
                        gameplayFieldOfView);
                }

                if (_computerOpen)
                {
                    _computerFlight.Update(
                        computerElapsed,
                        eye,
                        vantage,
                        vantageFieldOfView);

                    // Only once the display has settled. A drag or a page change that landed during
                    // the climb would move a map the player cannot read yet.
                    if (_computerFlight.IsInteractive)
                    {
                        tacticalComputer.Update(
                            _window.Input,
                            computerElapsed,
                            subject.BodyUnits,
                            _window.Width,
                            _window.Height);
                    }

                    // The computer keeps the view — and the player's input — until the fall has
                    // finished landing, so the last frame of the shot is the first frame of play.
                    if (_computerFlight.Phase == MapComputerPhase.Done)
                    {
                        _computerOpen = false;
                        _computerFlight.Reset();
                        HostedPlayer?.SetMapComputerOpen(false);
                    }

                    _window.Input.Captured = true;
                }
            }
            else if (_mapComputer is { IsReady: true } retailComputer && HostedPlayer is not null)
            {
                // The original's own computer is the mission's: its tick opens it from the weapon's idle state and closes it
                // on the key, and it reads the mouse the frames gather here. Escape does not close it.
                retailComputer.MapAvailable = mapAvailable;
                GatherComputerInput();

                // -computercam holds the map key down for a few frames, so the capture opens it as the player would.
                if (automaticOpen)
                {
                    _computerCamKeyFrames = 4;
                }

                PressComputerCamKey();
                if (_computerOpen)
                {
                    _window.Input.Captured = true;
                }
            }
        }
    }

    // The frames -computercam still holds the map key down.
    private int _computerCamKeyFrames;
    private Input.InputState? _computerCamInput;

    private void PressComputerCamKey()
    {
        if (_computerCamKeyFrames <= 0)
        {
            if (_computerCamInput is not null && ReferenceEquals(ScriptedViewInput, _computerCamInput))
            {
                ScriptedViewInput = null;
            }

            _computerCamInput = null;
            return;
        }

        _computerCamKeyFrames--;
        string? name = PlayerProfileInputBindings.BoundKeyName(_configuration.Active, PlayerInputBindings.ConfigName(PlayerAction.MapComputer));
        if (!InputBindingKeyNames.TryParseKeyboardKey(name, out Input.KeyCode key))
        {
            return;
        }

        _computerCamInput ??= new Input.InputState();
        _computerCamInput.Keyboard.BeginFrame();
        _computerCamInput.Keyboard.SetKey(key, _computerCamKeyFrames > 0);
        ScriptedViewInput = _computerCamInput;
    }

    // The mouse between two ticks, as the original's poll leaves it for the computer: the counts added up, a button down if it
    // was held or went down at any frame, the wheel's notches queued one a tick.
    private float _computerCountsX;
    private float _computerCountsY;
    private int _computerButtons;
    private int _computerWheel;

    private void GatherComputerInput()
    {
        Input.MouseState mouse = _window.Input.Mouse;
        _computerCountsX += mouse.DeltaX;
        _computerCountsY += mouse.DeltaY;
        if (mouse.IsDown(Input.MouseButton.Left) || mouse.WasPressed(Input.MouseButton.Left))
        {
            _computerButtons |= 0x1;
        }

        if (mouse.IsDown(Input.MouseButton.Right) || mouse.WasPressed(Input.MouseButton.Right))
        {
            _computerButtons |= 0x2;
        }

        _computerWheel += Math.Sign(mouse.WheelDelta);
    }

    /// <summary>Hands the computer the mouse since the last tick and starts gathering the next.</summary>
    private OpenIGI.Game.Computer.ComputerPointerInput TakeComputerInput()
    {
        int buttons = _computerButtons;
        if (_computerWheel > 0)
        {
            buttons |= 0x100;
            _computerWheel--;
        }
        else if (_computerWheel < 0)
        {
            buttons |= 0x200;
            _computerWheel++;
        }

        OpenIGI.Game.Computer.ComputerPointerInput taken = new(_computerCountsX, _computerCountsY, buttons);
        _computerCountsX = 0f;
        _computerCountsY = 0f;
        _computerButtons = 0;
        return taken;
    }

    /// <summary>The mission's computer has opened or closed: the view, the HUD and the platform's pointer follow it.</summary>
    private void MissionComputerChanged(bool open)
    {
        _computerOpen = open;

        // The computer draws its own pointer; the platform's would be a second one over it.
        _window.CursorVisible = false;
        _window.CursorLocked = false;
        _computerWheel = 0;
    }

    /// <summary>Gets a value indicating whether the title's own computer is in use rather than the tactical one.</summary>
    private bool UsesMissionComputer => TacticalComputer is null && _mapComputer is { IsReady: true } && HostedPlayer is not null;

    /// <summary>Gets the display the computer lays its screens across: the back buffer at the interface's magnification.</summary>
    private (int Width, int Height) ComputerDisplaySize
    {
        get
        {
            int scale = Math.Max(1, UiScale.Resolve(_options.UiScale, _window.Width, _window.Height));
            return (_window.Width / scale, _window.Height / scale);
        }
    }
}
