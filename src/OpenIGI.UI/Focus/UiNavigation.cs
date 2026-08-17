using OpenIGI.Input;

namespace OpenIGI.UI.Focus;

/// <summary>
/// Reads a frame's input into the handful of intents the focus model acts on — a repeating direction,
/// accept, and back — merging the arrow keys, the d-pad and the left stick into one stream.
/// </summary>
/// <remarks>
/// <para>
/// A held direction fires once immediately, then again after an initial delay, then at a steady repeat
/// interval — the familiar key-repeat feel — so holding down on a long list scrolls it rather than
/// moving one row and stopping. The left stick past a dead zone counts exactly as holding the matching
/// d-pad direction, which is what makes stick and pad interchangeable to everything above.
/// </para>
/// <para>
/// It reads through <see cref="InputState"/>'s edge tests, which honour the capture flag, so a screen
/// reads its navigation and then sets <see cref="InputState.Captured"/> to keep the same presses from
/// also reaching the game beneath it.
/// </para>
/// </remarks>
public sealed class UiNavigation
{
    private const float DeadZone = 0.5f;
    private const float InitialRepeatDelay = 0.40f;
    private const float RepeatInterval = 0.11f;

    private NavDirection? _held;
    private float _repeatCountdown;

    /// <summary>Gets the direction that fired this frame, or <see langword="null"/>.</summary>
    public NavDirection? Direction { get; private set; }

    /// <summary>Gets a value indicating whether an accept was pressed this frame.</summary>
    public bool Accept { get; private set; }

    /// <summary>Gets a value indicating whether a back/cancel was pressed this frame.</summary>
    public bool Back { get; private set; }

    /// <summary>Reads a frame of input into the navigation intents.</summary>
    /// <param name="input">The frame's input.</param>
    /// <param name="elapsedSeconds">The real seconds since the last read, for repeat timing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is <see langword="null"/>.</exception>
    public void Update(InputState input, float elapsedSeconds) =>
        Update(input, elapsedSeconds, consumeQueuedNavigation: true);

    /// <summary>Reads a frame of input into the navigation intents.</summary>
    /// <param name="input">The frame's input.</param>
    /// <param name="elapsedSeconds">The real seconds since the last read, for repeat timing.</param>
    /// <param name="consumeQueuedNavigation">
    /// When <see langword="false"/>, queued Enter/Space/arrows stay in the press queue so a listening
    /// settings row can bind them. Direction and accept still come from the live key state.
    /// </param>
    public void Update(InputState input, float elapsedSeconds, bool consumeQueuedNavigation)
    {
        ArgumentNullException.ThrowIfNull(input);

        Direction = null;
        NavDirection? current = ReadDirection(input);
        bool queuedAccept = false;
        NavDirection? queuedDirection = null;

        // A quick desktop key press can be released between game-loop polls. Consume the queued edge
        // while preserving the live-key repeat behavior used by held controls.
        while (consumeQueuedNavigation && input.Keyboard.TryDequeueNavigationPress(out KeyCode pressed))
        {
            switch (pressed)
            {
                case KeyCode.Return:
                case KeyCode.NumpadEnter:
                case KeyCode.Space:
                    queuedAccept = true;
                    break;
                case KeyCode.Up:
                case KeyCode.Numpad8:
                    queuedDirection = NavDirection.Up;
                    break;
                case KeyCode.Down:
                case KeyCode.Numpad2:
                    queuedDirection = NavDirection.Down;
                    break;
                case KeyCode.Left:
                case KeyCode.Numpad4:
                    queuedDirection = NavDirection.Left;
                    break;
                case KeyCode.Right:
                case KeyCode.Numpad6:
                    queuedDirection = NavDirection.Right;
                    break;
            }
        }

        if (current != _held)
        {
            _held = current;
            if (current is { } pressed)
            {
                Direction = pressed;              // fire the moment a direction is engaged
                _repeatCountdown = InitialRepeatDelay;
            }
        }
        else if (current is { } stillHeld)
        {
            _repeatCountdown -= elapsedSeconds;
            if (_repeatCountdown <= 0f)
            {
                Direction = stillHeld;
                _repeatCountdown += RepeatInterval;
            }
        }

        if (Direction is null)
        {
            Direction = queuedDirection;
        }

        KeyboardState keyboard = input.Keyboard;
        GamepadState pad = input.Gamepad;
        Accept = queuedAccept || keyboard.WasPressed(KeyCode.Return) || keyboard.WasPressed(KeyCode.NumpadEnter)
            || keyboard.WasPressed(KeyCode.Space) || pad.WasPressed(GamepadButton.South);
        Back = keyboard.WasPressed(KeyCode.Escape) || pad.WasPressed(GamepadButton.East);
    }

    /// <summary>Clears the held direction so the next press fires fresh, e.g. after a screen change.</summary>
    public void Reset()
    {
        _held = null;
        _repeatCountdown = 0f;
        Direction = null;
        Accept = false;
        Back = false;
    }

    private static NavDirection? ReadDirection(InputState input)
    {
        KeyboardState keyboard = input.Keyboard;
        GamepadState pad = input.Gamepad;

        bool up = keyboard.IsDown(KeyCode.Up) || keyboard.IsDown(KeyCode.Numpad8)
            || pad.IsDown(GamepadButton.DpadUp) || pad.LeftStickY < -DeadZone;
        bool down = keyboard.IsDown(KeyCode.Down) || keyboard.IsDown(KeyCode.Numpad2)
            || pad.IsDown(GamepadButton.DpadDown) || pad.LeftStickY > DeadZone;
        bool left = keyboard.IsDown(KeyCode.Left) || keyboard.IsDown(KeyCode.Numpad4)
            || pad.IsDown(GamepadButton.DpadLeft) || pad.LeftStickX < -DeadZone;
        bool right = keyboard.IsDown(KeyCode.Right) || keyboard.IsDown(KeyCode.Numpad6)
            || pad.IsDown(GamepadButton.DpadRight) || pad.LeftStickX > DeadZone;

        int vertical = (down ? 1 : 0) - (up ? 1 : 0);
        int horizontal = (right ? 1 : 0) - (left ? 1 : 0);

        if (vertical != 0 && horizontal != 0)
        {
            // Both axes engaged: let the stick's dominant axis decide, defaulting to vertical for the
            // digital case where the magnitudes are equal.
            if (MathF.Abs(pad.LeftStickX) > MathF.Abs(pad.LeftStickY))
            {
                return horizontal > 0 ? NavDirection.Right : NavDirection.Left;
            }

            return vertical > 0 ? NavDirection.Down : NavDirection.Up;
        }

        if (vertical != 0)
        {
            return vertical > 0 ? NavDirection.Down : NavDirection.Up;
        }

        if (horizontal != 0)
        {
            return horizontal > 0 ? NavDirection.Right : NavDirection.Left;
        }

        return null;
    }
}
