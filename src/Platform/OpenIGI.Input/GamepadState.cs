namespace OpenIGI.Input;

/// <summary>
/// The per-frame state of a game controller: its buttons with a previous-frame shadow for edge
/// detection, its two analogue sticks and its two triggers.
/// </summary>
/// <remarks>
/// <para>
/// Modelled on <see cref="KeyboardState"/> and <see cref="MouseState"/>: the host writes the raw device
/// state each frame through the setters, game and UI code only read the edge tests. It carries no
/// dependency on any input backend — the desktop host maps Silk.NET's controller to it — so a test can
/// drive a gamepad without hardware.
/// </para>
/// <para>
/// Like the other devices it honours <see cref="Captured"/>: while a layer in front of the game has
/// taken the frame's input, every read returns idle so the game beneath sees nothing.
/// </para>
/// </remarks>
public sealed class GamepadState
{
    private const int ButtonCount = 14;
    private const uint AllButtons = (1u << ButtonCount) - 1u;

    private uint _current;
    private uint _previous;

    /// <summary>Gets or sets a value indicating whether a controller is connected.</summary>
    public bool IsConnected { get; set; }

    /// <summary>Gets or sets a value indicating whether the frame's input has been taken by a layer above.</summary>
    public bool Captured { get; set; }

    /// <summary>Gets the left stick's horizontal axis, -1 (left) to 1 (right).</summary>
    public float LeftStickX { get; private set; }

    /// <summary>Gets the left stick's vertical axis, -1 (up) to 1 (down).</summary>
    public float LeftStickY { get; private set; }

    /// <summary>Gets the right stick's horizontal axis, -1 (left) to 1 (right).</summary>
    public float RightStickX { get; private set; }

    /// <summary>Gets the right stick's vertical axis, -1 (up) to 1 (down).</summary>
    public float RightStickY { get; private set; }

    /// <summary>Gets the left trigger, 0 to 1.</summary>
    public float LeftTrigger { get; private set; }

    /// <summary>Gets the right trigger, 0 to 1.</summary>
    public float RightTrigger { get; private set; }

    /// <summary>Takes the previous-frame shadow. Call before applying the frame's device state.</summary>
    public void BeginFrame() => _previous = _current;

    /// <summary>Sets whether a button is held.</summary>
    /// <param name="button">The button.</param>
    /// <param name="down">Whether it is held.</param>
    public void SetButton(GamepadButton button, bool down)
    {
        uint bit = 1u << (int)button;
        if (down)
        {
            _current |= bit;
        }
        else
        {
            _current &= ~bit;
        }
    }

    /// <summary>Sets the left stick axes, with the vertical already flipped so positive is downwards.</summary>
    /// <param name="x">The horizontal axis, -1 to 1.</param>
    /// <param name="y">The vertical axis, -1 (up) to 1 (down).</param>
    public void SetLeftStick(float x, float y)
    {
        LeftStickX = x;
        LeftStickY = y;
    }

    /// <summary>Sets the right stick axes, with the vertical already flipped so positive is downwards.</summary>
    /// <param name="x">The horizontal axis, -1 to 1.</param>
    /// <param name="y">The vertical axis, -1 (up) to 1 (down).</param>
    public void SetRightStick(float x, float y)
    {
        RightStickX = x;
        RightStickY = y;
    }

    /// <summary>Sets the trigger pressures.</summary>
    /// <param name="left">The left trigger, 0 to 1.</param>
    /// <param name="right">The right trigger, 0 to 1.</param>
    public void SetTriggers(float left, float right)
    {
        LeftTrigger = left;
        RightTrigger = right;
    }

    /// <summary>Determines whether a button is held this frame.</summary>
    /// <param name="button">The button.</param>
    /// <returns><see langword="true"/> when it is held and input is not captured.</returns>
    public bool IsDown(GamepadButton button) => !Captured && (_current & (1u << (int)button)) != 0;

    /// <summary>Determines whether a button went down this frame.</summary>
    /// <param name="button">The button.</param>
    /// <returns><see langword="true"/> on the frame it was first pressed and input is not captured.</returns>
    public bool WasPressed(GamepadButton button)
    {
        if (Captured)
        {
            return false;
        }

        uint bit = 1u << (int)button;
        return (_current & bit) != 0 && (_previous & bit) == 0;
    }

    /// <summary>Determines whether a button came up this frame.</summary>
    /// <param name="button">The button.</param>
    /// <returns><see langword="true"/> on the frame it was released and input is not captured.</returns>
    public bool WasReleased(GamepadButton button)
    {
        if (Captured)
        {
            return false;
        }

        uint bit = 1u << (int)button;
        return (_current & bit) == 0 && (_previous & bit) != 0;
    }

    /// <summary>Clears all state.</summary>
    public void Reset()
    {
        _current = 0;
        _previous = 0;
        LeftStickX = LeftStickY = RightStickX = RightStickY = 0f;
        LeftTrigger = RightTrigger = 0f;
        IsConnected = false;
    }

    /// <summary>Releases every held button, as the global input disable does to the keyboard.</summary>
    public void Release()
    {
        _previous = _current & AllButtons;
        _current = 0;
        LeftStickX = LeftStickY = RightStickX = RightStickY = 0f;
        LeftTrigger = RightTrigger = 0f;
    }
}
