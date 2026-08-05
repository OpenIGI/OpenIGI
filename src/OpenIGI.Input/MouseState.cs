namespace OpenIGI.Input;

/// <summary>
/// Mouse position, per-frame motion and button state, with the same previous-frame shadow as
/// <see cref="KeyboardState"/>.
/// </summary>
/// <remarks>
/// <para>
/// Position is in client pixels with the origin at the top-left. <see cref="DeltaX"/> and
/// <see cref="DeltaY"/> are the motion accumulated since the last <see cref="BeginFrame"/> and are what
/// look control reads. They are <em>not</em> derived from this type's own position: the host measures
/// each step against its own baseline and drops that baseline whenever the pointer is moved by
/// something other than the player, so a cursor lock re-centring the pointer does not arrive here as a
/// movement. Position, which a shell needs to keep tracking across the same event, is left alone.
/// </para>
/// <para>
/// While the cursor is locked the host reports raw device counts, so motion is unbounded and unaffected
/// by the pointer sitting at the edge of the window. While it is <em>not</em> locked the backend clamps
/// the pointer to the window, and motion past the edge is genuinely lost — which is correct for a menu,
/// and the reason look control locks the cursor.
/// </para>
/// <para>
/// <see cref="BeginFrame"/> must run <em>before</em> the host dispatches the frame's input events: it
/// takes the button shadow and zeroes the motion accumulators, and everything the pump then delivers
/// belongs to the frame about to run.
/// </para>
/// </remarks>
public sealed class MouseState
{
    /// <summary>The number of buttons tracked.</summary>
    public const int ButtonCount = 5;

    private readonly bool[] _current = new bool[ButtonCount];
    private readonly bool[] _previous = new bool[ButtonCount];
    private float _deltaX;
    private float _deltaY;
    private float _wheelDelta;

    /// <summary>Gets or sets a value indicating whether the shell has taken this frame's mouse input.</summary>
    /// <remarks>
    /// While set, buttons and motion read as idle but the underlying state is left intact — see
    /// <c>InputState.Captured</c>. The pointer position is still reported, because a shell that has
    /// taken the mouse is the very thing that needs to know where it is.
    /// </remarks>
    public bool Captured { get; set; }

    /// <summary>Gets the pointer's horizontal position in client pixels.</summary>
    public float X { get; private set; }

    /// <summary>Gets the pointer's vertical position in client pixels.</summary>
    public float Y { get; private set; }

    /// <summary>Gets the horizontal motion accumulated so far this frame.</summary>
    public float DeltaX { get => Captured ? 0.0f : _deltaX; private set => _deltaX = value; }

    /// <summary>Gets the vertical motion accumulated so far this frame.</summary>
    public float DeltaY { get => Captured ? 0.0f : _deltaY; private set => _deltaY = value; }

    /// <summary>Gets the wheel movement accumulated so far this frame, in notches.</summary>
    public float WheelDelta { get => Captured ? 0.0f : _wheelDelta; private set => _wheelDelta = value; }

    /// <summary>Takes the button shadow and clears the motion accumulators for a new frame.</summary>
    public void BeginFrame()
    {
        Captured = false;
        _current.AsSpan().CopyTo(_previous);
        DeltaX = 0.0f;
        DeltaY = 0.0f;
        WheelDelta = 0.0f;
    }

    /// <summary>Records a pointer move.</summary>
    /// <param name="x">The new horizontal position in client pixels.</param>
    /// <param name="y">The new vertical position in client pixels.</param>
    public void SetPosition(float x, float y)
    {
        X = x;
        Y = y;
    }

    /// <summary>Accumulates relative motion for the current frame.</summary>
    /// <param name="dx">The horizontal motion.</param>
    /// <param name="dy">The vertical motion.</param>
    public void AddMotion(float dx, float dy)
    {
        DeltaX += dx;
        DeltaY += dy;
    }

    /// <summary>Accumulates wheel movement for the current frame.</summary>
    /// <param name="delta">The movement in notches.</param>
    public void AddWheel(float delta) => WheelDelta += delta;

    /// <summary>Sets a button's state.</summary>
    /// <param name="button">The button.</param>
    /// <param name="down"><see langword="true"/> when the button is down.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="button"/> is not a tracked button.</exception>
    public void SetButton(MouseButton button, bool down)
    {
        int index = (int)button;
        ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(button));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, ButtonCount, nameof(button));
        _current[index] = down;
    }

    /// <summary>Determines whether a button is down this frame.</summary>
    /// <param name="button">The button.</param>
    /// <returns><see langword="true"/> when the button is down.</returns>
    public bool IsDown(MouseButton button) => !Captured && _current[(int)button];

    /// <summary>Determines whether a button went down between the previous frame and this one.</summary>
    /// <param name="button">The button.</param>
    /// <returns><see langword="true"/> on the frame the button is first down.</returns>
    public bool WasPressed(MouseButton button) => !Captured && _current[(int)button] && !_previous[(int)button];

    /// <summary>Determines whether a button went up between the previous frame and this one.</summary>
    /// <param name="button">The button.</param>
    /// <returns><see langword="true"/> on the frame the button is first up again.</returns>
    public bool WasReleased(MouseButton button) => !Captured && !_current[(int)button] && _previous[(int)button];

    /// <summary>Clears buttons, motion and the shadow. Position is left alone.</summary>
    public void Reset()
    {
        _current.AsSpan().Clear();
        _previous.AsSpan().Clear();
        DeltaX = 0.0f;
        DeltaY = 0.0f;
        WheelDelta = 0.0f;
    }
}
