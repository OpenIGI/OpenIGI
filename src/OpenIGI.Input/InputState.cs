namespace OpenIGI.Input;

/// <summary>
/// The per-frame input snapshot the engine reads: keyboard and mouse, each with its previous-frame
/// shadow.
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

    /// <summary>Gets or sets a value indicating whether input is suppressed.</summary>
    /// <remarks>
    /// The original zeroes its key array while a global disable flag is set — during loading screens
    /// and cutscenes — rather than skipping the poll, so keys held across the disable are seen as
    /// released and then freshly pressed. <see cref="BeginFrame"/> reproduces that.
    /// </remarks>
    public bool Suppressed { get; set; }

    /// <summary>Advances both devices to a new frame, taking their shadows.</summary>
    /// <remarks>
    /// Call this <em>before</em> the host dispatches the frame's input events, so that the shadow
    /// describes the previous frame and everything the pump delivers belongs to this one. That is the
    /// ordering <c>Input_Poll</c> uses, and it is what makes edge detection exact.
    /// </remarks>
    public void BeginFrame()
    {
        if (Suppressed)
        {
            // The original zeroes its key array while input is disabled rather than skipping the
            // poll, so keys held across the disable come back as fresh presses.
            Keyboard.Release();
            Mouse.BeginFrame();
            Mouse.Reset();
            return;
        }

        Keyboard.BeginFrame();
        Mouse.BeginFrame();
    }

    /// <summary>Clears both devices.</summary>
    public void Reset()
    {
        Keyboard.Reset();
        Mouse.Reset();
    }
}
