using OpenIGI.Game;
using OpenIGI.Input;
using OpenIGI.Presentation.Hud.Remastered;
using OpenIGI.Presentation.Hud.Remastered.Touch;
using OpenIGI.UI.Layout;

namespace OpenIGI.Host;

/// <content>
/// A touch screen's layout, and its editor: the player's three saved layouts (<see cref="TouchCVars.Layouts"/>) are what the
/// touch controls and the remastered display's readouts stand by, and the editor (<see cref="TouchLayoutEditor"/>), opened from
/// the pause menu over the mission, is where they are moved, resized, faded, hidden and brought back.
/// </content>
/// <remarks>
/// The editor is up over play, not over the menu: the level and the display are drawn as they are played, so what is arranged is
/// what will be seen. Alone, the mission stands still under it as it does under the pause menu; in a session with other players
/// it runs on, and the player only lets go of the controls.
/// </remarks>
public sealed partial class GameHost
{
    private readonly TouchLayoutEditor _touchEditor = new();
    private readonly HudLayoutOverrides _touchHudLayout = new(new TouchLayout());

    // The layout in force, as the settings spelt it when it was last read: read again only when that changes.
    private (int Slot, string Text)? _touchLayoutRead;

    // The finger the mouse stands in for (touch_emulate), while its button is down.
    private bool _touchEmulatedDown;
    private const int EmulatedFinger = 1 << 20;

    /// <summary>Gets whether the layout's editor holds the level still: up, and no one else is playing in it.</summary>
    private bool TouchEditorHoldsLevel => _touchEditor.IsOpen && TickObserver is null;

    /// <summary>Hooks the editor to the settings, and registers <c>touch_edit</c>, which opens it.</summary>
    /// <param name="state">The frame loop's state, whose console saves the settings.</param>
    private void RegisterTouchLayoutEditor(FrameLoopState state)
    {
        _touchEditor.Saved += (layouts, slot) =>
        {
            for (int i = 0; i < TouchCVars.Layouts.Length && i < layouts.Count; i++)
            {
                TouchCVars.Layouts[i].Value = layouts[i];
            }

            TouchCVars.Layout.Value = slot + 1;
            if (_options.SavesUserFiles)
            {
                state.Console.SaveSettings();
            }
        };
        _touchEditor.Closed += () =>
        {
            _touchControls.Editing = false;
            _touchHudLayout.Editing = false;
            _touchLayoutRead = null;
            _window.CursorLocked = true;
            _window.CursorVisible = false;
        };
        state.Console.System.Commands.Add(
            "touch_edit",
            "Opens the touch layout editor over the mission being played: move, resize, fade, hide or add the touch controls and the display's readouts.",
            _ => OpenTouchLayoutEditor());
        state.Console.System.Commands.Add(
            "touch_finger",
            "touch_finger <down|move|up> <x> <y> [id]: a scripted finger on the touch screen, in window pixels, read at the next frame. For captures.",
            arguments =>
            {
                if (arguments.Count >= 3
                    && float.TryParse(arguments[1], System.Globalization.CultureInfo.InvariantCulture, out float x)
                    && float.TryParse(arguments[2], System.Globalization.CultureInfo.InvariantCulture, out float y))
                {
                    int id = arguments.Count > 3 && int.TryParse(arguments[3], out int given) ? given : 1;
                    _touchScript.Enqueue((arguments[0], x, y, id));
                }
            });
    }

    // Scripted fingers (touch_finger), kept until the frame's own touches are read so each is seen coming down.
    private readonly Queue<(string Verb, float X, float Y, int Id)> _touchScript = new();

    /// <summary>Opens the layout's editor over the mission being played.</summary>
    /// <returns>Whether it opened: there is a mission, and no menu over it.</returns>
    private bool OpenTouchLayoutEditor()
    {
        if (_session.Mission is null || _menuFlow.MenuMode || _touchEditor.IsOpen)
        {
            return false;
        }

        string[] saved = new string[TouchCVars.Layouts.Length];
        for (int i = 0; i < saved.Length; i++)
        {
            saved[i] = TouchCVars.Layouts[i].Value;
        }

        _touchEditor.Open(saved, TouchCVars.Layout.Value - 1);
        return true;
    }

    /// <summary>Lays the touch screen out by the layout the settings name, when that has changed since it was last read.</summary>
    private void FollowTouchLayout()
    {
        if (_touchEditor.IsOpen)
        {
            return;
        }

        int slot = Math.Clamp(TouchCVars.Layout.Value, 1, TouchCVars.Layouts.Length);
        string text = TouchCVars.Layouts[slot - 1].Value;
        if (_touchLayoutRead is { } read && read.Slot == slot && read.Text == text)
        {
            return;
        }

        _touchLayoutRead = (slot, text);
        TouchLayout layout = TouchLayoutCodec.Decode(text);
        _touchControls.Arrangement = layout;
        _touchHudLayout.Layout = layout;
    }

    /// <summary>Reads the mouse as a finger, for trying the touch controls at a desk (<see cref="TouchCVars.Emulate"/>).</summary>
    private void EmulateTouch(InputState input)
    {
        while (_touchScript.TryDequeue(out (string Verb, float X, float Y, int Id) step))
        {
            switch (step.Verb)
            {
                case "down":
                    input.Touch.Down(step.Id, step.X, step.Y);
                    break;
                case "move":
                    input.Touch.Move(step.Id, step.X, step.Y);
                    break;
                case "up":
                    input.Touch.Up(step.Id, step.X, step.Y);
                    break;
            }
        }

        bool down = TouchCVars.Emulate.Value && input.Mouse.IsDown(MouseButton.Left);
        if (down && !_touchEmulatedDown)
        {
            input.Touch.Down(EmulatedFinger, input.Mouse.X, input.Mouse.Y);
        }
        else if (down)
        {
            input.Touch.Move(EmulatedFinger, input.Mouse.X, input.Mouse.Y);
        }
        else if (_touchEmulatedDown)
        {
            input.Touch.Up(EmulatedFinger, input.Mouse.X, input.Mouse.Y);
        }

        _touchEmulatedDown = down;
    }

    /// <summary>Runs the editor for a frame: the fingers are its own, and nothing they do reaches the game.</summary>
    /// <param name="input">The window's input.</param>
    /// <param name="context">The screen's layout.</param>
    private void UpdateTouchLayoutEditor(InputState input, LayoutContext context)
    {
        // A menu over the mission, or the mission gone, and there is nothing to arrange over.
        if (_menuFlow.MenuMode || _session.Mission is null)
        {
            _touchEditor.Close();
            return;
        }

        if (input.Keyboard.WasPressed(KeyCode.Escape))
        {
            _touchEditor.RequestExit();
        }

        _touchEditor.Update(input.Touch, context, _touchControls, _hudOverlay is ITouchHudSource || _titleHud is ITouchHudSource ? _touchHudLayout : null);
        if (_touchEditor.IsOpen)
        {
            // At a desk (touch_emulate) the pointer is the finger, and has to be seen.
            _window.CursorLocked = false;
            _window.CursorVisible = true;
            input.Captured = true;
        }
    }

    /// <summary>Draws the editor: its dimmed screen under the controls, the controls as the layout has them, and its bar over them.</summary>
    /// <param name="state">The frame loop's state, whose sprite batch it draws into.</param>
    /// <param name="source">What the title's display lends the controls, or null.</param>
    private void DrawTouchLayoutEditor(FrameLoopState state, ITouchHudSource? source)
    {
        LayoutContext context = new(_window.Width, _window.Height);
        _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
        state.Batch.Begin();
        TouchLayoutEditorRenderer.DrawBackdrop(state.Batch, context);
        TouchControlsRenderer.Draw(state.Batch, context, _touchControls, source?.Theme, source?.Icons, source?.Model, _shellType?.Heading);
        TouchLayoutEditorRenderer.DrawOverlay(state.Batch, context, _touchEditor, source?.Theme, _shellType?.Heading);
        state.Batch.End();
    }
}
