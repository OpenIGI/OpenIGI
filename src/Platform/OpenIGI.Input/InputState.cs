namespace OpenIGI.Input;

/// <summary>
/// The per-frame input snapshot the engine reads: keyboard, mouse and game controller, each with its
/// previous-frame shadow.
/// </summary>
/// <remarks>
/// The host owns one of these and advances it once per frame, before the tick. Game code only reads
/// it, so a replay or a test can drive the same state without a window.
/// </remarks>
public sealed class InputState
{
    /// <summary>Gets the keyboard.</summary>
    public KeyboardState Keyboard { get; } = new();

    /// <summary>Gets the mouse.</summary>
    public MouseState Mouse { get; } = new();

    /// <summary>Gets the primary game controller.</summary>
    public GamepadState Gamepad { get; } = new();

    /// <summary>Gets the fingers on a touch screen.</summary>
    /// <remarks>Not suppressed with the rest: the screen's own controls read it, and skipping a cutscene is one of them.</remarks>
    public TouchState Touch { get; } = new();

    /// <summary>Gets or sets a value indicating whether input is suppressed.</summary>
    /// <remarks>
    /// The original zeroes its key array while a global disable flag is set — during loading screens
    /// and cutscenes — rather than skipping the poll, so keys held across the disable are seen as
    /// released and then freshly pressed. <see cref="BeginFrame"/> reproduces that.
    /// </remarks>
    public bool Suppressed { get; set; }

    /// <summary>Gets or sets a value indicating whether the shell has taken this frame's input.</summary>
    /// <remarks>
    /// <para>
    /// Distinct from <see cref="Suppressed"/>, which is the original's global disable. This is for the
    /// developer console and, later, the shell's text fields: something in front of the game is using
    /// the keyboard this frame, so game code must see nothing while the thing in front of it still
    /// can.
    /// </para>
    /// <para>
    /// Set it <em>after</em> the capturing code has read its input, and it lasts until the next
    /// <see cref="BeginFrame"/>. Unlike <see cref="Suppressed"/> it does not clear the device state,
    /// which matters on an event-driven host: a key held down is only reported once, so discarding it
    /// would lose the key until it was released and pressed again.
    /// </para>
    /// </remarks>
    public bool Captured
    {
        get => Keyboard.Captured || Mouse.Captured || Gamepad.Captured;
        set
        {
            Keyboard.Captured = value;
            Mouse.Captured = value;
            Gamepad.Captured = value;
        }
    }

    /// <summary>Advances both devices to a new frame, taking their shadows.</summary>
    /// <remarks>
    /// Call this <em>before</em> the host dispatches the frame's input events, so that the shadow
    /// describes the previous frame and everything the pump delivers belongs to this one. That is the
    /// ordering <c>Input_Poll</c> uses, and it is what makes edge detection exact.
    /// </remarks>
    public void BeginFrame()
    {
        Captured = false;
        Touch.BeginFrame();

        if (Suppressed)
        {
            // The original zeroes its key array while input is disabled rather than skipping the
            // poll, so keys held across the disable come back as fresh presses.
            Keyboard.Release();
            Mouse.BeginFrame();
            Mouse.Reset();
            Gamepad.BeginFrame();
            Gamepad.Release();
            return;
        }

        Keyboard.BeginFrame();
        Mouse.BeginFrame();
        Gamepad.BeginFrame();
    }

    /// <summary>Clears every device.</summary>
    public void Reset()
    {
        Keyboard.Reset();
        Mouse.Reset();
        Gamepad.Reset();
    }
}
