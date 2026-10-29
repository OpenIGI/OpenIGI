using OpenIGI.Game.Config;
using OpenIGI.Input;
using OpenIGI.Presentation.Hud.Remastered;
using OpenIGI.Presentation.Hud.Remastered.Touch;
using OpenIGI.UI.Layout;

namespace OpenIGI.Host;

/// <summary>A touch screen's controls: the fingers read once a frame and pressed as the keys the player's actions are bound to.</summary>
/// <remarks>
/// Whatever has a touch screen -- the browser build on a phone, a tablet -- reports its fingers in <see cref="InputState.Touch"/>,
/// and nothing is shown or pressed until the first one comes down. The keys and mouse buttons pressed are the ones the player's
/// profile binds to each action, so a rebinding moves the button with it, and the game reads them as it reads a keyboard.
/// </remarks>
public sealed partial class GameHost
{
    private readonly TouchControls _touchControls = new();
    private readonly HashSet<KeyCode> _touchKeys = [];
    private readonly HashSet<KeyCode> _touchKeysWanted = [];
    private readonly HashSet<MouseButton> _touchButtons = [];
    private readonly HashSet<MouseButton> _touchButtonsWanted = [];

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
        _ => null,
    };

    /// <summary>What the game is showing, for the controls: a cutscene, play with the pointer locked, or anything with a pointer.</summary>
    private TouchControlMode CurrentTouchMode()
    {
        if (!_menuFlow.MenuMode && (_mission.Cutscenes?.IsPlaying == true || TitlePlayer is { IsCutscenePlaying: true }))
        {
            return TouchControlMode.Cutscene;
        }

        return _window.CursorLocked && !_menuFlow.MenuMode ? TouchControlMode.Play : TouchControlMode.Pointer;
    }

    /// <summary>Reads the frame's fingers and presses what they hold. Called straight after the window's events are taken.</summary>
    private void ApplyTouchControls()
    {
        InputState input = _window.Input;
        if (!input.Touch.InUse || _window.Width <= 0 || _window.Height <= 0)
        {
            return;
        }

        TouchControlMode mode = CurrentTouchMode();
        _touchControls.Update(input.Touch, mode, new LayoutContext(_window.Width, _window.Height));
        _touchKeysWanted.Clear();
        _touchButtonsWanted.Clear();
        PlayerProfile? profile = _configuration.Active;

        if (mode == TouchControlMode.Play)
        {
            foreach (TouchButton button in _touchControls.Held)
            {
                if (button == TouchButton.Pause)
                {
                    _touchKeysWanted.Add(KeyCode.Escape);
                }
                else if (TouchAction(button) is { } action)
                {
                    Want(profile, action);
                }
            }

            UiVec2 stick = _touchControls.Stick;
            if (stick.Y < -TouchControls.StickDeadZone)
            {
                Want(profile, "MoveUp");
            }

            if (stick.Y > TouchControls.StickDeadZone)
            {
                Want(profile, "MoveDown");
            }

            if (stick.X < -TouchControls.StickDeadZone)
            {
                Want(profile, "MoveLeft");
            }

            if (stick.X > TouchControls.StickDeadZone)
            {
                Want(profile, "MoveRight");
            }

            if (_touchControls.Look is { } look && (look.X != 0f || look.Y != 0f))
            {
                input.Mouse.AddMotion(look.X, look.Y);
            }
        }

        if (_touchControls.Pointer is { } pointer)
        {
            input.Mouse.SetPosition(pointer.At.X, pointer.At.Y);
            if (pointer.Down)
            {
                _touchButtonsWanted.Add(MouseButton.Left);
            }
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

        _touchKeys.Clear();
        _touchKeys.UnionWith(_touchKeysWanted);
        _touchButtons.Clear();
        _touchButtons.UnionWith(_touchButtonsWanted);
    }

    private void Want(PlayerProfile? profile, string action)
    {
        if (!PlayerProfileInputBindings.TryBoundControl(profile, action, out KeyCode key, out MouseButton? button))
        {
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
        if (!_window.Input.Touch.InUse || _touchControls.Mode != TouchControlMode.Play || _computerOpen)
        {
            return;
        }

        ITouchHudSource? source = _hudOverlay as ITouchHudSource ?? _titleHud as ITouchHudSource;
        _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
        state.Batch.Begin();
        TouchControlsRenderer.Draw(
            state.Batch, new LayoutContext(_window.Width, _window.Height), _touchControls, source?.Theme, source?.Icons, source?.Model);
        state.Batch.End();
    }
}
