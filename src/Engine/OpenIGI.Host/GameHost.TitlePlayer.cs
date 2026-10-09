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
/// behind <see cref="_titlePlayer"/>, which stays <see langword="null"/> for a title that walks the host's own rig,
/// so that rig's path is the one it always was.
/// </para>
/// <para>
/// The host's rig and a title's player are never both standing: when the title stands its own, the host's is let go
/// and every system that reads the host's rig sees no player.
/// </para>
/// </remarks>
internal sealed partial class GameHost
{
    private const double TitleUnitsPerMetre = 4096.0;

    private ITitleGameplay? _gameplay;
    private ITitlePlayer? _titlePlayer;
    private ITitleViewModelPresenter? _titleArms;
    private float _titleMouseX;
    private float _titleMouseY;
    private PlayerAction _titlePressed;
    private PlayerAction _titleScriptHeld;

    /// <summary>Stands the title's own player where the level places it, when the title brings one.</summary>
    /// <param name="loaded">The mission.</param>
    /// <param name="levelRoot">The level's tree.</param>
    /// <param name="scene">The placements the scene walk produced.</param>
    private void StandTitlePlayer(LoadedMission loaded, LevelTask levelRoot, WorldScene scene)
    {
        _titlePlayer = null;
        _titleArms?.Dispose();
        _titleArms = null;
        if (_gameplay is null)
        {
            return;
        }

        ITitlePlayer? player = _gameplay.CreatePlayer(new TitlePlayerRequest(
            new WorldBuildRequest(loaded, levelRoot, _resources, _magic, _logger),
            new WorldBuildResult(scene, 0, 0, _ground, _spins),
            _title.Services,
            (AiDifficulty)Math.Clamp(_playerDifficulty, 0, 2),
            _sounds is { } sounds && _soundBank is { } bank ? new TitleAudio(sounds, bank) : null,
            _loop.Natives));
        if (player is null)
        {
            _logger.Warning("Player", "The level places no player for the title to stand.");
            return;
        }

        _titlePlayer = player;
        _player = null;
        _world?.Motions = player.Motions;
        _world?.Destruction = player.Destruction;

        // What the title's player holds in front of its eyes, drawn from the title's own packs.
        if (player is ITitleFirstPerson)
        {
            _titleArms = _presentation.CreateTitleViewModel(ViewModelRequest());
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
        foreach ((PlayerInputSlot _, PlayerAction action) in PlayerInputBindings.All)
        {
            if (PlayerProfileInputBindings.WasPressed(_configuration.Active, _window.Input, PlayerInputBindings.ConfigName(action)))
            {
                _titlePressed |= action;
            }
        }
    }

    /// <summary>Runs the title's player for one tick, with the input gathered since the last.</summary>
    /// <returns><see langword="true"/> when the title has its own player, whether or not it ran this tick.</returns>
    private bool TickTitlePlayer()
    {
        if (_titlePlayer is not { } player)
        {
            return false;
        }

        if (_playerTicked || !_playerMode || _cutscenes?.IsPlaying == true)
        {
            return true;
        }

        _playerTicked = true;
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
        player.Tick(new TitlePlayerInput(
            _titleMouseX,
            _titleMouseY,
            held,
            _titlePressed,
            profile?.MouseSensitivity ?? MouseLookSettings.DefaultSensitivity,
            profile?.InvertMouse ?? false,
            TitleBreakKey()));
        _titleMouseX = 0f;
        _titleMouseY = 0f;
        _titlePressed = default;
        return true;
    }

    /// <summary>Whether the title's cutscene break key is down this tick.</summary>
    /// <remarks>
    /// Escape, read as the key's state, as retail's <c>LevelFlow_GetBreakCutSceneKey</c> reads it. With cinematics off it is
    /// held, the same path the host's own intro skip takes: the level's scripts end every cutscene the way the key does.
    /// <c>OPENIGI_BREAK_CUTSCENE=1</c> holds it too, for a lockstep run.
    /// </remarks>
    private bool TitleBreakKey() =>
        !_options.PlayCutscenes
        || _window.Input.Keyboard.IsDown(Input.KeyCode.Escape)
        || string.Equals(Environment.GetEnvironmentVariable("OPENIGI_BREAK_CUTSCENE"), "1", StringComparison.Ordinal);

    /// <summary>Registers the console's hold on the title player's controls, for a capture to drive it without keys.</summary>
    /// <param name="console">The console to register with.</param>
    /// <remarks>
    /// <c>-runat 60 "title_hold Fire"</c> holds fire from frame 60 until a later <c>title_hold none</c>; several actions
    /// may be named at once. Only a title's own player reads it; the host's rig never does.
    /// </remarks>
    private void RegisterTitlePlayerCommands(ConsoleSystem console)
    {
        console.Commands.Add(
            "title_hold",
            "Holds the title player's controls as if their keys were down: action names (Fire, Reload, MoveUp, ...) or none. Not in the original.",
            context =>
            {
                PlayerAction held = default;
                for (int i = 0; i < context.Count; i++)
                {
                    if (string.Equals(context[i], "none", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!Enum.TryParse(context[i], ignoreCase: true, out PlayerAction action))
                    {
                        context.Warn($"'{context[i]}' is not a player action.");
                        return;
                    }

                    held |= action;
                }

                _titleScriptHeld = held;
                context.Print($"Title player holds {held}.");
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
        if (_titlePlayer is not ITitleFirstPerson hands || _titleArms is not { } arms
            || !_playerMode || _computerOpen || _entry.IsCameraActive || _cutscenes?.IsPlaying == true
            || hands.FirstPerson is not { } held)
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
        if (_titlePlayer is ITitleWorldModels owned && _titleArms is ITitleWorldModelPresenter models)
        {
            models.DrawWorld(owned.WorldModels, eye, projection);
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

    /// <summary>F1 between the title's player and the free camera: the free camera leaves from where the eyes are.</summary>
    private void ToggleTitlePlayer()
    {
        if (_titlePlayer is not { } player || _camera is null)
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

        _logger.Information("Player", _playerMode ? "Walking." : "Flying.");
    }
}
