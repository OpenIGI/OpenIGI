using OpenIGI.Core.Graphics;
using OpenIGI.Game;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Input;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Hud.Remastered;
using OpenIGI.Presentation.Hud.Remastered.Touch;
using OpenIGI.UI.Layout;

namespace OpenIGI.Host;

/// <summary>A touch screen's controls: the fingers read once a frame and pressed as the keys the player's actions are bound to.</summary>
/// <remarks>
/// Whatever has a touch screen -- the browser build on a phone, a tablet -- reports its fingers in <see cref="InputState.Touch"/>,
/// and nothing is shown or pressed until the first one comes down. The keys and mouse buttons pressed are the ones the player's
/// profile binds to each action, so a rebinding moves the button with it, and the game reads them as it reads a keyboard. A
/// phone's gyroscope (<see cref="InputState.Motion"/>) turns the view in play as a mouse would, when <see cref="TouchCVars.Gyro"/>
/// asks for it.
/// </remarks>
public sealed partial class GameHost
{
    private readonly TouchControls _touchControls = new();
    private readonly HashSet<KeyCode> _touchKeys = [];
    private readonly HashSet<KeyCode> _touchKeysWanted = [];
    private readonly HashSet<MouseButton> _touchButtons = [];
    private readonly HashSet<MouseButton> _touchButtonsWanted = [];
    private readonly HashSet<int> _touchWheels = [];
    private readonly HashSet<int> _touchWheelsWanted = [];
    private (bool Up, bool Down, bool Left, bool Right) _touchStickHeld;
    private TouchIconAtlas? _touchIcons;
    private bool _touchSaveHeld;
    private bool _touchLoadHeld;

    // The title's own bits the buttons hold this frame, and last frame: a title's control is pressed as its button comes down.
    private ulong _touchTitleHeld;
    private ulong _touchTitleHeldLast;

    // A title whose crouch is held (I.G.I. 2's) has its touch button latch it instead: one tap down, the next back up.
    private bool _touchCrouchDown;
    private bool _touchCrouchLatched;
    private ITitlePlayer? _touchCrouchPlayer;

    /// <summary>The action each button holds, as the profile names it.</summary>
    private static string? TouchAction(TouchButton button) => button switch
    {
        TouchButton.Fire => "Fire",
        TouchButton.Scope => "AlternateFire",
        TouchButton.Reload => "Reload",
        TouchButton.Jump => "Jump",
        TouchButton.Crouch => "Crouch",
        TouchButton.Use => "Activate",
        TouchButton.Weapon => "NextWeapon",
        TouchButton.Peek => "Peek",
        TouchButton.Map => "MapComputer",
        TouchButton.Binoculars => "Binoculars",
        TouchButton.ZoomIn => "ZoomIn",
        TouchButton.ZoomOut => "ZoomOut",
        TouchButton.Close => "MapComputer",
        TouchButton.Scoreboard => PlayerProfile.ScoreboardAction,
        TouchButton.Buy => PlayerProfile.BuyMenuAction,
        TouchButton.Drop => nameof(PlayerAction.DropWeapon),
        TouchButton.Flashlight => "Flashlight",
        TouchButton.WalkRun => "ToggleWalkRun",
        TouchButton.PreviousWeapon => "PrevWeapon",
        TouchButton.WeaponWheel => "WeaponWheel",
        _ => null,
    };

    // The key a match's action is pressed as when the profile, saved before the action was, binds it to nothing.
    private static KeyCode? TouchFallbackKey(string action) => action switch
    {
        PlayerProfile.ScoreboardAction => KeyCode.Tab,
        PlayerProfile.BuyMenuAction => KeyCode.G,
        nameof(PlayerAction.DropWeapon) => KeyCode.X,
        _ => null,
    };

    /// <summary>The match's own buttons this frame: each where it has something to do.</summary>
    private TouchMatchButtons CurrentMatchButtons()
    {
        if (TickObserver is not ISessionScoreboard)
        {
            return TouchMatchButtons.None;
        }

        TouchMatchButtons buttons = TouchMatchButtons.Scoreboard;
        Game.Multiplayer.Modes.MatchHud? hud = CurrentMatchHud();
        if (hud is { BuyClock: not null } or { FreeLoadout: true })
        {
            buttons |= TouchMatchButtons.Buy;
        }

        if (hud is { Teams: not null, Result: null } && hud.Phase != Game.Multiplayer.Modes.MatchPhase.Intermission)
        {
            buttons |= TouchMatchButtons.Team;
        }

        if (TickObserver is Multiplayer.ISessionMode { Mode: var mode } && Game.Multiplayer.Modes.ArenaLevel.IsVersus(mode))
        {
            buttons |= TouchMatchButtons.Drop;
        }

        return buttons;
    }

    /// <summary>The title's own control a button stands for, where the title has one: its crouch, its prone stance, its goggles.</summary>
    private static TitleTouchControl TitleTouchOf(TouchButton button) => button switch
    {
        TouchButton.Crouch => TitleTouchControl.Crouch,
        TouchButton.Prone => TitleTouchControl.Prone,
        TouchButton.Thermal => TitleTouchControl.Thermal,
        _ => TitleTouchControl.None,
    };

    /// <summary>The title's bits a button holds, or nought where the button is every title's and goes through the profile's keys.</summary>
    private ulong TitleTouchMask(TouchButton button)
    {
        TitleTouchControl control = TitleTouchOf(button);
        if (control == TitleTouchControl.None || TitlePlayer is null || Gameplay is not { } gameplay)
        {
            return 0;
        }

        ulong mask = 0;
        foreach (TitleInputBinding binding in gameplay.InputBindings)
        {
            if (binding.Touch == control)
            {
                mask |= binding.Mask;
            }
        }

        return mask;
    }

    /// <summary>
    /// Steps the touch crouch's latch for a title whose crouch is held (I.G.I. 2's: <c>HumanPlayer_StateSignalHandler</c> reads
    /// it held): a tap crouches, the next stands, and a jump, going prone or another level lets it go. A thumb cannot hold a
    /// button down and still move and look, so the button is a switch where the key is held.
    /// </summary>
    /// <param name="mode">What the controls are showing.</param>
    /// <returns>The title's crouch bits, or nought where the title has no held crouch of its own and the button is the shared one.</returns>
    private ulong LatchTouchCrouch(TouchControlMode mode)
    {
        ulong bits = TitleTouchMask(TouchButton.Crouch);
        bool down = mode == TouchControlMode.Play && _touchControls.Held.Contains(TouchButton.Crouch);
        if (bits == 0 || !ReferenceEquals(_touchCrouchPlayer, TitlePlayer))
        {
            _touchCrouchLatched = false;
            _touchCrouchPlayer = TitlePlayer;
        }
        else if (down && !_touchCrouchDown)
        {
            _touchCrouchLatched = !_touchCrouchLatched;
        }
        else if (mode == TouchControlMode.Play
            && (_touchControls.Held.Contains(TouchButton.Jump) || _touchControls.Held.Contains(TouchButton.Prone)))
        {
            _touchCrouchLatched = false;
        }

        _touchCrouchDown = down;
        _touchControls.CrouchLatched = _touchCrouchLatched;
        return bits;
    }

    /// <summary>The buttons the title being played offers of its own, by the touch controls its bindings name.</summary>
    private TouchTitleButtons CurrentTitleButtons()
    {
        if (TitlePlayer is null || Gameplay is not { } gameplay)
        {
            return TouchTitleButtons.None;
        }

        TouchTitleButtons buttons = TouchTitleButtons.None;
        foreach (TitleInputBinding binding in gameplay.InputBindings)
        {
            buttons |= binding.Touch switch
            {
                TitleTouchControl.Thermal => TouchTitleButtons.Thermal,
                TitleTouchControl.Prone => TouchTitleButtons.Prone,
                _ => TouchTitleButtons.None,
            };
        }

        return buttons;
    }

    /// <summary>What the game is showing, for the controls: a cutscene, play with the pointer locked, or anything with a pointer.</summary>
    private TouchControlMode CurrentTouchMode()
    {
        if (_touchEditor.IsOpen)
        {
            return TouchControlMode.Edit;
        }

        if (!_menuFlow.MenuMode && (_mission.Scenes?.IsPlaying == true || TitlePlayer is { IsCutscenePlaying: true }))
        {
            return TouchControlMode.Cutscene;
        }

        // The computer, and a match's shop or team menu over play: a finger points, and the corner's button closes it.
        if ((_computerOpen || _buyMenuUp || _teamMenuUp) && !_menuFlow.MenuMode)
        {
            return TouchControlMode.Computer;
        }

        // Play is read from what is on the screen rather than from the cursor lock, which the look gives up and takes back for a
        // frame at a time (a settling frame, the weapon wheel): each change of mode lets every finger go, and the stick with it.
        bool play = _playerMode && !_menuFlow.MenuMode && !_computerOpen && !_session.FailedMenu;
        return play ? TouchControlMode.Play : TouchControlMode.Pointer;
    }

    /// <summary>Reads the frame's fingers and presses what they hold. Called straight after the window's events are taken.</summary>
    private void ApplyTouchControls()
    {
        InputState input = _window.Input;
        EmulateTouch(input);
        _window.SetMotionSensors(input.Touch.InUse && TouchCVars.Gyro.Value != GyroAim.Off);

        // The display's readouts stand by the player's layout only on a touch screen: at a desk, by the title's own.
        bool touching = input.Touch.InUse || _touchEditor.IsOpen;
        HudLayoutOverrides.Current = touching ? _touchHudLayout : null;
        _touchTitleHeldLast = _touchTitleHeld;
        _touchTitleHeld = 0;
        if (!touching || _window.Width <= 0 || _window.Height <= 0)
        {
            return;
        }

        FollowTouchLayout();
        LayoutContext layout = new(_window.Width, _window.Height);
        if (_touchEditor.IsOpen)
        {
            UpdateTouchLayoutEditor(input, layout);
        }

        TouchControlMode mode = CurrentTouchMode();
        _touchControls.MatchButtons = CurrentMatchButtons();
        _touchControls.TitleButtons = CurrentTitleButtons();
        _touchControls.SavesShown = MissionSaveGames is not null;
        _touchControls.ScopeSwitchesMode = TitlePlayer?.AlternateFireSwitchesMode == true;
        _touchControls.LookScale = TouchCVars.LookSensitivity.Value;
        _touchControls.Update(input.Touch, mode, layout);
        _touchKeysWanted.Clear();
        _touchButtonsWanted.Clear();
        _touchWheelsWanted.Clear();
        PlayerProfile? profile = _configuration.Active;

        // A quick save or load is asked for once as its button comes down, as its key asks.
        bool saveHeld = mode == TouchControlMode.Play && _touchControls.Held.Contains(TouchButton.QuickSave);
        bool loadHeld = mode == TouchControlMode.Play && _touchControls.Held.Contains(TouchButton.QuickLoad);
        if ((saveHeld && !_touchSaveHeld) || (loadHeld && !_touchLoadHeld))
        {
            QuickSaveOrLoad(save: saveHeld && !_touchSaveHeld);
        }

        _touchSaveHeld = saveHeld;
        _touchLoadHeld = loadHeld;

        if (mode == TouchControlMode.Computer)
        {
            foreach (TouchButton button in _touchControls.Held)
            {
                // Over a match's menu the corner's button backs out of it, as Escape does at a keyboard.
                if (button == TouchButton.Close && !_computerOpen)
                {
                    _touchKeysWanted.Add(KeyCode.Escape);
                }
                else if (TouchAction(button) is { } action)
                {
                    Want(profile, action);
                }
            }
        }

        // One tap, one line of chat: opened as the finger comes down, the page raising the keyboard as it lifts.
        bool chatHeld = mode == TouchControlMode.Play && _touchControls.Held.Contains(TouchButton.Chat);
        if (chatHeld && !_touchChatHeld && _chatEntry is null)
        {
            OpenChat(team: false);
        }

        _touchChatHeld = chatHeld;

        ulong crouchBits = LatchTouchCrouch(mode);
        if (mode == TouchControlMode.Play)
        {
            foreach (TouchButton button in _touchControls.Held)
            {
                if (button == TouchButton.Crouch && crouchBits != 0)
                {
                    // Latched below: holding the button is not what keeps the body down.
                }
                else if (button == TouchButton.Pause)
                {
                    _touchKeysWanted.Add(KeyCode.Escape);
                }
                else if (button == TouchButton.Team)
                {
                    // The team menu's key is a setting of its own, not an action the profile binds.
                    if (BindKeyNames.TryParse(TeamMenuCVars.Key.Value, out KeyCode team))
                    {
                        _touchKeysWanted.Add(team);
                    }
                }
                else if (TitleTouchMask(button) is not 0 and var own)
                {
                    // The title's own control, held as its bits: the shared key may be one the title's binding stands in for.
                    _touchTitleHeld |= own;
                }
                else if (TouchAction(button) is { } action)
                {
                    Want(profile, action);
                }
            }

            if (_touchCrouchLatched)
            {
                _touchTitleHeld |= crouchBits;
            }

            // A title's control is pressed on the frame its button comes down, as a key going down is (GatherTitleInput).
            _titleExtraPressed |= _touchTitleHeld & ~_touchTitleHeldLast;

            // Each direction is taken past the dead zone and let go only back inside the smaller release zone.
            UiVec2 stick = _touchControls.Stick;
            static bool Hold(bool held, float along) =>
                along > (held ? TouchControls.StickReleaseZone : TouchControls.StickDeadZone);
            _touchStickHeld = (
                Hold(_touchStickHeld.Up, -stick.Y),
                Hold(_touchStickHeld.Down, stick.Y),
                Hold(_touchStickHeld.Left, -stick.X),
                Hold(_touchStickHeld.Right, stick.X));
            if (_touchStickHeld.Up)
            {
                Want(profile, "MoveUp");
            }

            if (_touchStickHeld.Down)
            {
                Want(profile, "MoveDown");
            }

            if (_touchStickHeld.Left)
            {
                Want(profile, "MoveLeft");
            }

            if (_touchStickHeld.Right)
            {
                Want(profile, "MoveRight");
            }

            if (_touchControls.Look is { } look && (look.X != 0f || look.Y != 0f))
            {
                input.Mouse.AddMotion(look.X, look.Y);
            }

            ApplyGyro(input.Motion, profile);
        }

        if (_touchControls.Pointer is { } pointer)
        {
            input.Mouse.SetPosition(pointer.At.X, pointer.At.Y);
            if (mode == TouchControlMode.Computer && _computerOpen && TacticalComputer is null && _mapComputer?.Pointer is { } computer)
            {
                SteerComputerPointer(input, pointer, computer, profile);
            }

            _touchButtonsWanted.Add(MouseButton.Left);
        }

        // Only what the touches pressed is let go: a key the keyboard holds at the same time stays the keyboard's.
        foreach (KeyCode key in _touchKeys)
        {
            if (!_touchKeysWanted.Contains(key))
            {
                input.Keyboard.SetKey(key, down: false);
            }
        }

        foreach (KeyCode key in _touchKeysWanted)
        {
            if (!_touchKeys.Contains(key))
            {
                input.Keyboard.SetKey(key, down: true);
            }
        }

        foreach (MouseButton button in _touchButtons)
        {
            if (!_touchButtonsWanted.Contains(button))
            {
                input.Mouse.SetButton(button, down: false);
            }
        }

        foreach (MouseButton button in _touchButtonsWanted)
        {
            if (!_touchButtons.Contains(button))
            {
                input.Mouse.SetButton(button, down: true);
            }
        }

        foreach (int direction in _touchWheelsWanted)
        {
            if (!_touchWheels.Contains(direction))
            {
                input.Mouse.AddWheel(direction);
            }
        }

        _touchWheels.Clear();
        _touchWheels.UnionWith(_touchWheelsWanted);
        _touchKeys.Clear();
        _touchKeys.UnionWith(_touchKeysWanted);
        _touchButtons.Clear();
        _touchButtons.UnionWith(_touchButtonsWanted);
    }

    /// <summary>Turns the view as far as the phone turned, as mouse counts, when the gyroscope aims.</summary>
    /// <remarks>
    /// The counts are the profile's own (<see cref="MouseLookSettings.RadiansPerCount"/>), so a turn of the phone turns the
    /// view by the same angle whatever the mouse's sensitivity, times <see cref="TouchCVars.GyroSensitivity"/>; a sight's slower
    /// look still applies on top, as it does to a finger's. The vertical is the gyroscope's own to invert: the look inverts
    /// counts when the profile inverts the mouse, so they are turned back first.
    /// </remarks>
    private void ApplyGyro(MotionState motion, PlayerProfile? profile)
    {
        GyroAim aim = TouchCVars.Gyro.Value;
        if ((motion.Yaw == 0f && motion.Pitch == 0f) || aim == GyroAim.Off || (aim == GyroAim.Scope && !Sighted()))
        {
            return;
        }

        float countsPerRadian = TouchCVars.GyroSensitivity.Value / MouseLookSettings.FromProfile(profile).RadiansPerCount;
        bool invert = TouchCVars.GyroInvert.Value != (profile?.InvertMouse ?? false);
        float down = -motion.Pitch * countsPerRadian;
        _window.Input.Mouse.AddMotion(motion.Yaw * countsPerRadian, invert ? -down : down);
    }

    /// <summary>Whether the player looks through a sight or the binoculars: the view narrower than the eyes' own.</summary>
    private bool Sighted() => TitlePlayer is { } title
        ? title.FieldOfViewScale < 0.98f
        : Viewpoint?.SightZoom(1f) is { } sight && sight.TanX < HumanView.DefaultTanX * 0.98f;

    /// <summary>Moves the original computer's pointer under the finger.</summary>
    /// <remarks>
    /// The computer moves its pointer by the mouse's counts alone (<c>0x46AC30</c>: half its width for 255 counts at the
    /// profile's sensitivity), so the finger's place is turned into the counts that carry the pointer there, less those already
    /// gathered for the next tick. While the map is dragged the pointer holds still and the counts turn the map, so then the
    /// finger's own travel is passed on, as a mouse's would be.
    /// </remarks>
    private void SteerComputerPointer(InputState input, TouchPointer finger, ComputerPointer computer, PlayerProfile? profile)
    {
        float scale = Math.Max(1, UiScale.Resolve(_options.UiScale, _window.Width, _window.Height));
        float sensitivity = MathF.Max(0.05f, profile?.MouseSensitivity ?? MouseLookSettings.DefaultSensitivity);
        float countsPerPixel = 2f * 255f / (MathF.Max(1f, computer.Width) * sensitivity);

        if (computer.Dragging)
        {
            input.Mouse.AddMotion(finger.Delta.X / scale * countsPerPixel, finger.Delta.Y / scale * countsPerPixel);
            return;
        }

        float wantX = ((finger.At.X / scale) - computer.X) * countsPerPixel;
        float wantY = ((finger.At.Y / scale) - computer.Y) * countsPerPixel;
        input.Mouse.AddMotion(wantX - _computerCountsX, wantY - _computerCountsY);
    }

    private void Want(PlayerProfile? profile, string action)
    {
        if (!PlayerProfileInputBindings.TryBoundControl(profile, action, out KeyCode key, out MouseButton? button))
        {
            // On the wheel, which has nothing to hold down: the button turns it a notch as it is pressed.
            if (PlayerProfileInputBindings.TryBoundWheel(profile, action, out int direction))
            {
                _touchWheelsWanted.Add(direction);
            }
            else if (TouchFallbackKey(action) is { } fallback)
            {
                _touchKeysWanted.Add(fallback);
            }

            return;
        }

        if (button is { } mouse)
        {
            _touchButtonsWanted.Add(mouse);
        }
        else
        {
            _touchKeysWanted.Add(key);
        }
    }

    /// <summary>Draws the controls over play, in the title's remastered look.</summary>
    /// <param name="state">The frame loop's state, whose sprite batch they draw into.</param>
    private void DrawTouchControls(FrameLoopState state)
    {
        ITouchHudSource? source = _hudOverlay as ITouchHudSource ?? _titleHud as ITouchHudSource;
        if (_window.Input.Touch.InUse || _touchEditor.IsOpen)
        {
            TouchControlsRenderer.Icons = _touchIcons ??= new TouchIconAtlas(_device);
        }

        if (_touchEditor.IsOpen)
        {
            DrawTouchLayoutEditor(state, source);
            return;
        }

        if (!_window.Input.Touch.InUse || _touchControls.Mode is not (TouchControlMode.Play or TouchControlMode.Computer))
        {
            return;
        }

        _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
        state.Batch.Begin();
        TouchControlsRenderer.Draw(
            state.Batch,
            new LayoutContext(_window.Width, _window.Height),
            _touchControls,
            source?.Theme,
            source?.Icons,
            source?.Model,
            _shellType?.Heading);
        state.Batch.End();
    }
}
