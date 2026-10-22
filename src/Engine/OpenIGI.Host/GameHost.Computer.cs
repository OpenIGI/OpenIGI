using OpenIGI.Engine.Tasks;
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
            new MapComputerPresentationRequest(_device, _resources, _logger, missionText.Get));
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

    /// <summary>Reads the flashlight switch and the handheld computer, which own the keys while they are in use.</summary>
    private void UpdatePlayerDevices(FrameLoopState state)
    {
        // The remaster's flashlight, before the map computer's key: it is read only while the player's
        // own view is on screen, which the computer opening this frame has not yet changed.
        UpdateFlashlightSwitch(_menuFlow.MenuMode, state.Console.IsOpen);

        // IGI1's handheld is a game mode, entered by the retail C binding. It owns the pointer and
        // freezes player/camera input while the world continues its fixed-tick simulation.
        if (!_menuFlow.MenuMode && !state.Console.IsOpen && HostedPlayer is not null
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

            HostedPlayer.InputProfile = _configuration.Active;
            bool toggleComputer = HostedPlayer.WasActionPressed(_window.Input, PlayerAction.MapComputer) || automaticOpen;
            bool closeComputer = _computerOpen && _window.Input.Keyboard.WasPressed(Input.KeyCode.Escape);

            // The computer always opens; whether its map is there to read is the level's to say.
            bool mapAvailable = MapComputerAvailable();

            if (TacticalComputer is { } tacticalComputer)
            {
                // Real elapsed time, on its own clock. The transition is presentation: it must take
                // the same wall-clock second whatever the frame rate, and it must keep running while
                // the simulation is paused behind it.
                int computerNow = _loop.TimeSource.Milliseconds;
                float computerElapsed = Math.Clamp((computerNow - state.LastComputerMilliseconds) / 1000f, 0f, 0.1f);
                state.LastComputerMilliseconds = computerNow;

                CameraPose eye = CameraPose.FromCamera(HostedPlayer.Camera);

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
                    tacticalComputer.Open(HostedPlayer.Body.Position);
                    HostedPlayer.SetMapComputerOpen(true);
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
                            HostedPlayer.Body.Position,
                            _window.Width,
                            _window.Height);
                    }

                    // The computer keeps the view — and the player's input — until the fall has
                    // finished landing, so the last frame of the shot is the first frame of play.
                    if (_computerFlight.Phase == MapComputerPhase.Done)
                    {
                        _computerOpen = false;
                        _computerFlight.Reset();
                        HostedPlayer.SetMapComputerOpen(false);
                    }

                    _window.Input.Captured = true;
                }
            }
            else if (_mapComputer is { IsReady: true } retailComputer)
            {
                retailComputer.MapAvailable = mapAvailable;
                if (toggleComputer || closeComputer)
                {
                    _computerOpen = toggleComputer ? !_computerOpen : false;
                    if (_computerOpen)
                    {
                        retailComputer.Open(HostedPlayer.Body.Position);
                    }
                    HostedPlayer.SetMapComputerOpen(_computerOpen);
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
}
