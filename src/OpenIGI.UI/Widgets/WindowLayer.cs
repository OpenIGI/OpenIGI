using OpenIGI.Input;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Widgets;

/// <summary>
/// Hosts floating <see cref="UiWindow"/>s: arranges each into its own pixel frame, keeps them in a
/// front-to-back order, and owns the one drag or resize that can be in flight.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Canvas"/> anchors children to <em>fractions</em> of its own box, which is exactly right
/// for a screen that must survive going from 4:3 to 32:9 and exactly wrong for a window the reader has
/// dragged to a spot. This container arranges each child into the absolute rectangle it carries.
/// </para>
/// <para>
/// <b>Why the drag lives here and not in the window.</b> Only one window can be dragged at a time, the
/// grab has to survive the pointer leaving the window it started on, and a press has to raise the
/// window it landed on above the others. All three are facts about the set, not about any member of
/// it.
/// </para>
/// <para>
/// <b>Pointer positions, never deltas.</b> The mouse's frame deltas are zeroed while input is captured
/// — and a console that has taken the keyboard has captured it — whereas its position keeps being
/// reported precisely so a shell can use it. Dragging is therefore resolved against the position the
/// grab began at.
/// </para>
/// </remarks>
public class WindowLayer : UiElement
{
    private readonly List<UiWindow> _windows = [];

    private UiWindow? _active;
    private WindowRegion _mode;
    private UiVec2 _grabPoint;
    private UiRect _grabFrame;

    /// <summary>Gets the windows, back to front.</summary>
    public IReadOnlyList<UiWindow> Windows => _windows;

    /// <summary>Gets the window in front, or <see langword="null"/> when there is none.</summary>
    public UiWindow? Focused { get; private set; }

    /// <summary>Gets a value indicating whether a drag or resize is in progress.</summary>
    public bool IsManipulating => _active is not null;

    /// <summary>Gets the window being dragged or resized, or <see langword="null"/> when none is.</summary>
    public UiWindow? Active => _active;

    /// <summary>Gets what the live manipulation is doing — a title drag, or which edges are moving.</summary>
    /// <remarks>
    /// Exposed so a panel that sizes itself to its contents can tell when the reader has taken that
    /// decision away from it. A window whose height keeps snapping back to its content while it is
    /// being dragged taller is worse than one that never sized itself at all.
    /// </remarks>
    public WindowRegion ActiveRegion => _active is null ? WindowRegion.None : _mode;

    /// <summary>Gets or sets how near a viewport edge a window snaps, in pixels. Zero disables it.</summary>
    public float SnapDistance { get; set; } = 12f;

    /// <summary>Adds a window in front of the others.</summary>
    /// <param name="window">The window to add.</param>
    /// <returns>The window, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="window"/> is <see langword="null"/>.</exception>
    public UiWindow Add(UiWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        AddChild(window);
        _windows.Add(window);
        Raise(window);
        return window;
    }

    /// <summary>Brings a window to the front.</summary>
    /// <param name="window">The window to raise.</param>
    /// <remarks>
    /// Reorders the child list, because drawing walks it forwards and hit-testing walks it backwards —
    /// so one order serves both, and the window drawn last is the window clicked first.
    /// </remarks>
    public void Raise(UiWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (_windows.Remove(window))
        {
            _windows.Add(window);
            RemoveChild(window);
            AddChild(window);
        }

        Focused = window;
        foreach (UiWindow other in _windows)
        {
            other.IsFocusedWindow = ReferenceEquals(other, window);
        }
    }

    /// <summary>Finds the frontmost open window under a point.</summary>
    /// <param name="point">The point in back-buffer pixels.</param>
    /// <returns>The window and the region hit, or <see langword="null"/> when the point misses them all.</returns>
    public (UiWindow Window, WindowRegion Region)? WindowAt(UiVec2 point)
    {
        for (int i = _windows.Count - 1; i >= 0; i--)
        {
            UiWindow window = _windows[i];
            WindowRegion region = window.RegionAt(point);
            if (region != WindowRegion.None)
            {
                return (window, region);
            }
        }

        return null;
    }

    /// <summary>Gets a value indicating whether a point lands on any open window.</summary>
    /// <param name="point">The point in back-buffer pixels.</param>
    /// <returns><see langword="true"/> when a window is under the point.</returns>
    public bool IsOverAnyWindow(UiVec2 point) => WindowAt(point) is not null;

    /// <summary>
    /// Advances the drag state machine for a frame and returns whether the pointer was consumed.
    /// </summary>
    /// <param name="mouse">The frame's mouse state, read before input was captured.</param>
    /// <param name="viewport">The back buffer's size in pixels.</param>
    /// <returns><see langword="true"/> when the layer used the pointer and nothing below should.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mouse"/> is <see langword="null"/>.</exception>
    public bool UpdatePointer(MouseState mouse, UiVec2 viewport)
    {
        ArgumentNullException.ThrowIfNull(mouse);

        var point = new UiVec2(mouse.X, mouse.Y);
        bool held = mouse.IsDown(MouseButton.Left);

        UpdateHover(point);

        if (_active is not null)
        {
            if (!held)
            {
                EndManipulation(viewport);
                return true;
            }

            Continue(point, viewport);
            return true;
        }

        if (!mouse.WasPressed(MouseButton.Left))
        {
            return IsOverAnyWindow(point);
        }

        if (WindowAt(point) is not { } hit)
        {
            return false;
        }

        Raise(hit.Window);

        if (hit.Window.CanClose && hit.Window.CloseRect.Contains(point))
        {
            hit.Window.RequestClose();
            return true;
        }

        if ((hit.Region & WindowRegion.AnyEdge) != 0 && hit.Window.IsResizable)
        {
            Begin(hit.Window, hit.Region, point);
            return true;
        }

        if (hit.Region == WindowRegion.Title && hit.Window.IsMovable)
        {
            Begin(hit.Window, WindowRegion.Title, point);
            return true;
        }

        // A press on the content raises the window but belongs to whatever is inside it.
        return false;
    }

    /// <summary>Keeps every window reachable after the back buffer has changed size.</summary>
    /// <param name="viewport">The new back-buffer size in pixels.</param>
    /// <remarks>
    /// A window parked against the right edge of a wide display would otherwise sit entirely off a
    /// narrow one, and the settings file would faithfully restore it there on every launch.
    /// </remarks>
    public void ClampAll(UiVec2 viewport)
    {
        foreach (UiWindow window in _windows)
        {
            window.ClampIntoView(viewport);
        }
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        foreach (UiWindow window in _windows)
        {
            if (window.IsOpen)
            {
                window.Measure(context, available);
            }
        }

        return available;
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        foreach (UiWindow window in _windows)
        {
            if (!window.IsOpen)
            {
                window.Visibility = Visibility.Collapsed;
                continue;
            }

            window.Visibility = Visibility.Visible;
            window.Arrange(window.VisibleFrame);
        }
    }

    private void UpdateHover(UiVec2 point)
    {
        (UiWindow Window, WindowRegion Region)? hit = _active is null ? WindowAt(point) : (_active, _mode);

        foreach (UiWindow window in _windows)
        {
            window.PointerPosition = point;
            window.HoveredRegion = hit is { } h && ReferenceEquals(h.Window, window)
                ? h.Region
                : WindowRegion.None;
        }
    }

    private void Begin(UiWindow window, WindowRegion mode, UiVec2 point)
    {
        _active = window;
        _mode = mode;
        _grabPoint = point;
        _grabFrame = window.Frame;
    }

    private void Continue(UiVec2 point, UiVec2 viewport)
    {
        if (_active is not { } window)
        {
            return;
        }

        var delta = new UiVec2(point.X - _grabPoint.X, point.Y - _grabPoint.Y);

        if (_mode == WindowRegion.Title)
        {
            window.Frame = _grabFrame.Offset(delta.X, delta.Y);
            window.SnapToEdges(viewport, SnapDistance);
            window.ClampIntoView(viewport);
            return;
        }

        window.ResizeBy(_mode, delta, _grabFrame);
    }

    private void EndManipulation(UiVec2 viewport)
    {
        if (_active is { } window)
        {
            // Land on whole pixels so the border hairline does not straddle two of them.
            window.SnapToPixelGrid();
            window.ClampIntoView(viewport);
        }

        _active = null;
        _mode = WindowRegion.None;
    }
}
