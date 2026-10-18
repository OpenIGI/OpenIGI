using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Host.Audio;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

/// <content>A title's own player: stood up, fed its input once a tick, and looked through.</content>
/// <remarks>
/// <para>
/// Present only for a title whose composition root supplies an <see cref="ITitleGameplay"/>. Everything here is
/// behind <see cref="TitlePlayer"/>, which stays <see langword="null"/> for a title that walks the host's own rig,
/// so that rig's path is the one it always was.
/// </para>
/// <para>
/// The host's rig and a title's player are never both standing: when the title stands its own, the host's is let go
/// and every system that reads the host's rig sees no player.
/// </para>
/// </remarks>
public sealed partial class GameHost
{
    private const double TitleUnitsPerMetre = 4096.0;

    private ITitleGameplay? _gameplay;
    // The title's own player, with what it can do; null when the host's rig is the player.
    private TitlePlayerFeatures? _titleFeatures;

    private ITitlePlayer? TitlePlayer => _titleFeatures?.Player;
    private readonly OpenIGI.Presentation.Cameras.TitlePlayerPresentation _titlePresentation = new();
    private float _titlePresentationBlend = 1f;
    private ITitleViewModelPresenter? _titleArms;

    // The same presenter, when it also draws what the player has lying in its level.
    private ITitleWorldModelPresenter? TitleWorldModelPresenter => _titleArms as ITitleWorldModelPresenter;
    private ITitleHudPresenter? _titleHud;
    private float _titleMouseX;
    private float _titleMouseY;
    private PlayerAction _titlePressed;
    private ulong _titleExtraPressed;
    private float _titleWheel;
    private TitleRawKeys _titleScriptRaw;
    private PlayerAction _titleScriptHeld;
    private double _titleTickMilliseconds;
    private double _titleTickMaximumMilliseconds;
    private int _titleMeasuredTicks;
    private bool _titleLastLiveLook;
    private bool _titleLastCameraMatches;

    /// <summary>Stands the title's own player where the level places it, when the title brings one.</summary>
    /// <param name="loaded">The mission.</param>
    /// <param name="levelRoot">The level's tree.</param>
    /// <param name="scene">The placements the scene walk produced.</param>
    private void StandTitlePlayer(LoadedMission loaded, LevelTask levelRoot, WorldScene scene)
    {
        RestoreTitleInputBindings();
        _titleFeatures = null;
        _titleHud?.Dispose();
        _titleHud = null;
        _titleArms?.Dispose();
        _titleArms = null;
        if (_gameplay is null)
        {
            return;
        }

        ITitlePlayer? player = _gameplay.CreatePlayer(new TitlePlayerRequest(
            new WorldBuildRequest(loaded, levelRoot, _resources, _mission.MagicObjects, _logger, Services: _title.Services),
            new WorldBuildResult(scene, 0, 0, _ground, _mission.Spins),
            _title.Services,
            (AiDifficulty)Math.Clamp(_mission.PlayerDifficulty, 0, 2),
            _audioOut.Sounds is { } sounds && _audioOut.Bank is { } bank ? new TitleAudio(sounds, bank) : null,
            _loop.Natives));
        if (player is null)
        {
            _logger.Warning("Player", "The level places no player for the title to stand.");
            return;
        }

        _titleFeatures = new TitlePlayerFeatures(player);
        _titleHud = _presentation.Presenters.Create<HudPresentationRequest, ITitleHudPresenter>(new HudPresentationRequest(_device, _logger));
        _titleHud?.Load(_resources.FileSystem);
        _titleTickMilliseconds = 0;
        _titleTickMaximumMilliseconds = 0;
        _titleMeasuredTicks = 0;
        _titleLastLiveLook = false;
        _titleLastCameraMatches = false;
        _titlePresentation.Reset(_titleFeatures);
        _mission.Player = null;
        _world?.Motions = player.Motions;
        _world?.Destruction = player.Destruction;

        // What the title's player holds in front of its eyes, drawn from the title's own packs.
        if (_titleFeatures.FirstPerson is not null)
        {
            _titleArms = _presentation.Presenters.Create<ViewModelPresentationRequest, ITitleViewModelPresenter>(ViewModelRequest());
            if (_titleArms is not null && !_titleArms.Load(loaded))
            {
                _titleArms.Dispose();
                _titleArms = null;
            }

            // Lit as the world's unbaked objects are: the arms and the weapon carry no lightmap.
            _titleArms?.Shading = _presentation.ReadObjectShading(levelRoot) ?? ObjectShading.Default;
        }
        _titleMouseX = 0f;
        _titleMouseY = 0f;
        _titlePressed = default;
        _titleExtraPressed = 0;
        _playerMode = !_options.StartFlying;

        Vector3d eye = player.EyePosition;
        _logger.Information(
            "Player",
            FormattableString.Invariant($"The title's player stands at {player.Position}; eye at -campos ")
                + FormattableString.Invariant(
                    $"{eye.X / TitleUnitsPerMetre:F3},{eye.Y / TitleUnitsPerMetre:F3},{eye.Z / TitleUnitsPerMetre:F3}."));
    }

    /// <summary>Gathers a frame's input for the title's player: the mouse adds up and presses are kept until a tick.</summary>
    private void GatherTitleInput()
    {
        _titleMouseX += _window.Input.Mouse.DeltaX;
        _titleMouseY += _window.Input.Mouse.DeltaY;
        _titleWheel += _window.Input.Mouse.WheelDelta;
        if (_titleFeatures?.Look is { } look)
        {
            if (RenderCVars.SmoothPresentation.Value && !_options.Lockstep)
            {
                PlayerProfile? profile = _configuration.Active;
                look.GatherLook(_window.Input.Mouse.DeltaX, _window.Input.Mouse.DeltaY,
                    profile?.MouseSensitivity ?? MouseLookSettings.DefaultSensitivity, profile?.InvertMouse ?? false);
            }
            else { look.ResetLook(); }
        }
        foreach ((PlayerInputSlot _, PlayerAction action) in PlayerInputBindings.All)
        {
            if (PlayerProfileInputBindings.WasPressed(_configuration.Active, _window.Input, PlayerInputBindings.ConfigName(action)))
            {
                _titlePressed |= action;
            }
        }
        if (_gameplay is { } gameplay)
        {
            _titleExtraPressed |= TitleInputBindings.Sample(_configuration.Active, _window.Input, gameplay.InputBindings, pressed: true);
        }
    }

    /// <summary>Runs the title's player for one tick, with the input gathered since the last.</summary>
    /// <returns><see langword="true"/> when the title has its own player, whether or not it ran this tick.</returns>
    private bool TickTitlePlayer()
    {
        if (TitlePlayer is not { } player)
        {
            return false;
        }

        if (_mission.PlayerTicked || !_playerMode || _mission.Cutscenes?.IsPlaying == true)
        {
            return true;
        }

        _mission.PlayerTicked = true;
        PlayerAction held = default;
        foreach ((PlayerInputSlot _, PlayerAction action) in PlayerInputBindings.All)
        {
            if (PlayerProfileInputBindings.IsDown(_configuration.Active, _window.Input, PlayerInputBindings.ConfigName(action)))
            {
                held |= action;
            }
        }

        held |= _titleScriptHeld;

        PlayerProfile? profile = _configuration.Active;
        PlayerAction replaced = _gameplay is { } titleGameplay ? TitleInputBindings.ReplacedActions(titleGameplay.InputBindings) : 0;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        player.Tick(new TitlePlayerInput(
            _titleMouseX,
            _titleMouseY,
            held & ~replaced,
            _titlePressed & ~replaced,
            profile?.MouseSensitivity ?? MouseLookSettings.DefaultSensitivity,
            profile?.InvertMouse ?? false,
            TitleBreakKey(),
            UnfilteredMouse: RenderCVars.SmoothPresentation.Value && !_options.Lockstep,
            TitleHeld: _gameplay is { } gameplay ? TitleInputBindings.Sample(profile, _window.Input, gameplay.InputBindings, pressed: false) : 0,
            TitlePressed: _titleExtraPressed,
            Raw: SampleRawKeys()));
        _titlePresentation.CaptureTick(_titleFeatures!);
        double milliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        _titleTickMilliseconds += milliseconds;
        _titleTickMaximumMilliseconds = Math.Max(_titleTickMaximumMilliseconds, milliseconds);
        _titleMeasuredTicks++;
        _titleMouseX = 0f;
        _titleMouseY = 0f;
        _titleWheel = 0f;
        _titlePressed = default;
        _titleExtraPressed = 0;
        return true;
    }

    /// <summary>The fixed keys and buttons a title's own screens read, held now, and the wheel turned since the last tick.</summary>
    private TitleRawKeys SampleRawKeys()
    {
        OpenIGI.Input.KeyboardState keys = _window.Input.Keyboard;
        OpenIGI.Input.MouseState mouse = _window.Input.Mouse;
        TitleRawKeys raw = _titleScriptRaw;
        if (mouse.IsDown(OpenIGI.Input.MouseButton.Left)) { raw |= TitleRawKeys.MouseLeft; }
        if (mouse.IsDown(OpenIGI.Input.MouseButton.Right)) { raw |= TitleRawKeys.MouseRight; }
        if (_titleWheel > 0f) { raw |= TitleRawKeys.WheelUp; }
        if (_titleWheel < 0f) { raw |= TitleRawKeys.WheelDown; }
        if (keys.IsDown(OpenIGI.Input.KeyCode.Up)) { raw |= TitleRawKeys.Up; }
        if (keys.IsDown(OpenIGI.Input.KeyCode.Down)) { raw |= TitleRawKeys.Down; }
        if (keys.IsDown(OpenIGI.Input.KeyCode.Left)) { raw |= TitleRawKeys.Left; }
        if (keys.IsDown(OpenIGI.Input.KeyCode.Right)) { raw |= TitleRawKeys.Right; }
        if (keys.IsDown(OpenIGI.Input.KeyCode.Return)) { raw |= TitleRawKeys.Enter; }
        if (keys.IsDown(OpenIGI.Input.KeyCode.Home)) { raw |= TitleRawKeys.Home; }
        if (keys.IsDown(OpenIGI.Input.KeyCode.LeftControl) || keys.IsDown(OpenIGI.Input.KeyCode.RightControl)) { raw |= TitleRawKeys.Control; }
        return raw;
    }

    private void RestoreTitleInputBindings()
    {
        if (_gameplay is not { } gameplay) { return; }
        foreach (PlayerProfile profile in _configuration.Profiles) { TitleInputBindings.RestoreMissing(profile, gameplay.InputBindings); }
    }

    /// <summary>Applies pending mouse motion to this rendered frame while gameplay remains on its tick.</summary>
    /// <param name="smooth">Whether Smooth Motion is enabled for this frame.</param>
    private void PresentTitleLook(bool smooth)
    {
        PlayerProfile? profile = _configuration.Active;
        bool available = smooth && _playerMode && !_computerOpen && !_entry.IsCameraActive
            && !_session.FailedMenu;
        if (!available && _titleFeatures?.Look is { } stopped) { stopped.ResetLook(); }
        _titlePresentation.SetLookPreview(available && _titleFeatures?.Look is { CanPreviewLook: true } look
            ? look.PreviewLook(_titleMouseX, _titleMouseY, profile?.MouseSensitivity ?? MouseLookSettings.DefaultSensitivity,
                profile?.InvertMouse ?? false)
            : null);
    }

    /// <summary>Whether the title's cutscene break key is down this tick.</summary>
    /// <remarks>
    /// Escape, read as the key's state, as retail's <c>LevelFlow_GetBreakCutSceneKey</c> reads it. With cinematics off it is
    /// held, the same path the host's own intro skip takes: the level's scripts end every cutscene the way the key does.
    /// <c>OPENIGI_BREAK_CUTSCENE=1</c> holds it too, for a lockstep run.
    /// </remarks>
    private bool TitleBreakKey() =>
        !_options.PlayCutscenes
        || _window.Input.Keyboard.IsDown(OpenIGI.Input.KeyCode.Escape)
        || string.Equals(Environment.GetEnvironmentVariable("OPENIGI_BREAK_CUTSCENE"), "1", StringComparison.Ordinal);

    /// <summary>Registers the console's hold on the title player's controls, for a capture to drive it without keys.</summary>
    /// <param name="console">The console to register with.</param>
    /// <remarks>
    /// <c>-runat 60 "title_hold Fire"</c> holds fire from frame 60 until a later <c>title_hold none</c>; several actions
    /// may be named at once. Only a title's own player reads it; the host's rig never does.
    /// </remarks>
    private void RegisterTitlePlayerCommands(ConsoleSystem console)
    {
        console.Commands.Add("title_ai", "Reports title guards' AI startup, script, graph and animation state.", context =>
        {
            if (_titleFeatures?.Diagnostics is not { } diagnostics)
            {
                context.Print("The active title player exposes no actor diagnostics.");
                return;
            }
            foreach (string line in diagnostics.DescribeActors()) { context.Print(line); }
        });

        console.Commands.Add(
            "title_presentation",
            "Reports the active build, title camera smoothing gates and measured title tick cost.",
            context =>
            {
                context.Print($"Game directory: {AppContext.BaseDirectory}; process: {Environment.ProcessPath}");
                if (TitlePlayer is not { } player)
                {
                    context.Print("No title player is active; the host player or fly camera supplies the view.");
                    return;
                }

                string gates = $"r_smooth={RenderCVars.SmoothPresentation.Value}, lockstep={_options.Lockstep}, "
                    + $"walking={_playerMode}, computer={_computerOpen}, entry={_entry.IsCameraActive}, "
                    + $"hostCutscene={_mission.Cutscenes?.IsPlaying == true}, titleCutscene={player.IsCutscenePlaying}, "
                    + $"canPreview={_titleFeatures?.Look is { CanPreviewLook: true }}, "
                    + $"lastGameplayLiveLook={_titleLastLiveLook}, lastGameplayCameraMatches={_titleLastCameraMatches}";
                context.Print(gates);
                context.Print(FormattableString.Invariant(
                    $"blend={_titlePresentationBlend:F4}, pendingMouse=({_titleMouseX:F3},{_titleMouseY:F3}), ticks={_loop.Clock.TickCount}, renders={_loop.Clock.FrameCount}, titleTickMs mean={_titleTickMilliseconds / Math.Max(1, _titleMeasuredTicks):F3} max={_titleTickMaximumMilliseconds:F3} samples={_titleMeasuredTicks}"));
                _pacing.Report("title_presentation");
            });

        console.Commands.Add(
            "title_hold",
            "Holds the title player's controls as if their keys were down: action names (Fire, Reload, MoveUp, ...), the fixed keys a title's screens read (Up, Enter, MouseLeft, WheelUp, ...) or none. Not in the original.",
            context =>
            {
                PlayerAction held = default;
                TitleRawKeys raw = TitleRawKeys.None;
                for (int i = 0; i < context.Count; i++)
                {
                    if (string.Equals(context[i], "none", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (Enum.TryParse(context[i], ignoreCase: true, out PlayerAction action))
                    {
                        held |= action;
                    }
                    else if (Enum.TryParse(context[i], ignoreCase: true, out TitleRawKeys key))
                    {
                        raw |= key;
                    }
                    else
                    {
                        context.Warn($"'{context[i]}' is neither a player action nor a fixed key.");
                        return;
                    }
                }

                _titleScriptHeld = held;
                _titleScriptRaw = raw;
                context.Print($"Title player holds {held}, keys {raw}.");
            },
            "<action ...|none>");
    }

    /// <summary>Draws what the title's player holds, after the world and over it, as the host's own rig draws its arms.</summary>
    /// <param name="eye">The camera the frame is drawn from.</param>
    /// <remarks>
    /// Only while walking with nothing else holding the view: not in the free camera, a cutscene, the computer or a
    /// camera entry. Depth is cleared first so a wall the eyes stand against cannot cut through the hands, and the
    /// geometry is flagged first-person so the lighting neither meters nor bounces off it.
    /// </remarks>
    private void DrawTitleFirstPerson(in Camera3D eye)
    {
        if (_titleFeatures?.FirstPerson is not { } hands || _titleArms is not { } arms
            || !_playerMode || _computerOpen || _entry.IsCameraActive || _mission.Cutscenes?.IsPlaying == true
            || _titlePresentation.FirstPerson(_titlePresentationBlend) is not { } held)
        {
            return;
        }

        ViewModelDepth.Begin(_device);
        _device.SetFirstPersonGeometry(true);
        arms.Draw(held, eye, ViewModelProjection.Create(held.ObjectFieldOfView, FarPlane));
        _device.SetFirstPersonGeometry(false);
    }

    /// <summary>Draws the models the title's player has lying in its level, with the world's projection and depth.</summary>
    /// <param name="eye">The camera the frame is drawn from.</param>
    /// <param name="projection">The world's projection.</param>
    private void DrawTitleWorldModels(in Camera3D eye, in Matrix4f projection)
    {
        if (_titleFeatures?.WorldModels is { } owned && TitleWorldModelPresenter is { } models)
        {
            models.DrawWorld(owned.WorldModels, eye, projection);
            models.DrawWorldSprites(owned.WorldSprites, eye, projection);
        }
    }

    /// <summary>Draws the marks the title's player has lying on the ground, over the lit picture.</summary>
    /// <param name="eye">The camera the frame is drawn from.</param>
    /// <param name="projection">The world's projection.</param>
    /// <remarks>They blend, so they follow the lit world as the water does; drawn into the G-buffer they would be opaque squares.</remarks>
    private void DrawTitleGroundDecals(in Camera3D eye, in Matrix4f projection)
    {
        if (_titleFeatures?.WorldModels is { } owned && TitleWorldModelPresenter is { } models)
        {
            models.DrawGroundDecals(owned.GroundDecals, eye, projection);
        }
    }

    /// <summary>The camera the title's player looks through, in metres.</summary>
    private static Camera3D TitleCamera(ITitlePlayer player)
    {
        Vector3d eye = player.EyePosition;
        return new Camera3D(
            new Vector3d(eye.X / TitleUnitsPerMetre, eye.Y / TitleUnitsPerMetre, eye.Z / TitleUnitsPerMetre),
            player.ViewOrientation);
    }

    /// <summary>F1 swaps cameras; returning places the title's player beneath the free camera as IGI1 does.</summary>
    private void ToggleTitlePlayer()
    {
        if (TitlePlayer is not { } player || _camera is null)
        {
            return;
        }

        _playerMode = !_playerMode;
        if (!_playerMode)
        {
            Camera3D eye = TitleCamera(player);
            Vector3f forward = eye.Orientation.Row3;
            float yaw = MathF.Atan2(forward.X, forward.Y);
            float pitch = MathF.Asin(Math.Clamp(forward.Z, -1f, 1f));
            _camera.MoveTo(eye.Position, yaw, pitch);
        }
        else if (_titleFeatures?.Placement is { } placement)
        {
            if (!placement.TryPlace(_camera.Position * TitleUnitsPerMetre, _camera.Yaw))
            {
                _playerMode = false;
                _logger.Warning("Player", "Cannot stand here; staying with the free camera.");
            }
            else
            {
                _titlePresentation.Reset(_titleFeatures);
                _titlePresentationBlend = 1f;
            }
        }

        _titleMouseX = 0f;
        _titleMouseY = 0f;
        _titlePressed = default;
        _titleExtraPressed = 0;
        _titleFeatures?.Look?.ResetLook();

        _logger.Information("Player", _playerMode ? "Walking." : "Flying.");
    }
}
