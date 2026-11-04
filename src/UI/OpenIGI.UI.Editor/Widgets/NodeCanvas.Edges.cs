using OpenIGI.Core.Graphics;
using OpenIGI.Input;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Editor.Widgets;

/// <summary>
/// The canvas's handles on frames and wires: a frame's corner to resize it and its title's menu, and reroute points a wire
/// runs through, added by double-clicking the wire, dragged, and taken away with Alt-click.
/// </summary>
public sealed partial class NodeCanvas
{
    // The grip in a frame's bottom-right corner, in canvas units.
    private const float CornerGrip = 18f;

    // The smallest a frame is sized down to, in canvas units.
    private static readonly UiVec2 SmallestFrame = new(160f, 70f);

    private string? _resizedFrame;
    private UiVec2 _resizedTo;
    private (CanvasWire Wire, int Index)? _knot;
    private UiVec2 _knotAt;

    /// <summary>Raised when a frame's corner is dragged and let go: the frame's id and its new size, in canvas units.</summary>
    public event Action<string, UiVec2>? FrameResizeRequested;

    /// <summary>Raised on a right click on a frame's title: the frame's id and the canvas point, for its menu.</summary>
    public event Action<string, UiVec2>? FrameContextRequested;

    /// <summary>Raised on a double click on a wire: the wire, where among its points the new one goes, and the canvas point.</summary>
    public event Action<CanvasWire, int, UiVec2>? RerouteAddRequested;

    /// <summary>Raised when a reroute point is dragged and let go: the wire, the point's index and where it was put.</summary>
    public event Action<CanvasWire, int, UiVec2>? RerouteMoveRequested;

    /// <summary>Raised on an Alt-click on a reroute point: the wire and the point's index, to take it away.</summary>
    public event Action<CanvasWire, int>? RerouteRemoveRequested;

    /// <summary>Raised on Alt+Left or the mouse's back button: go back to the graph shown before.</summary>
    public event Action? BackRequested;

    /// <summary>Raised on Alt+Right or the mouse's forward button: go forward again.</summary>
    public event Action? ForwardRequested;

    // The mouse's back and forward buttons: whether the press was one.
    private bool NavigateByButton(MouseButton button)
    {
        if (button == MouseButton.Extra1)
        {
            BackRequested?.Invoke();
            return true;
        }

        if (button == MouseButton.Extra2)
        {
            ForwardRequested?.Invoke();
            return true;
        }

        return false;
    }

    // Alt+Left and Alt+Right: whether the key was one.
    private bool NavigateByKey(in KeyEvent e)
    {
        if (!e.Alt || e.Key is not (KeyCode.Left or KeyCode.Right))
        {
            return false;
        }

        (e.Key == KeyCode.Left ? BackRequested : ForwardRequested)?.Invoke();
        return true;
    }

    // A press on a reroute point, a frame's corner or (double) a wire: whether it took the press.
    private bool BeginEdgeGesture(in PointerEvent e, UiVec2 canvas)
    {
        if (KnotAt(e.Position) is { } knot)
        {
            if (e.Alt)
            {
                RerouteRemoveRequested?.Invoke(knot.Wire, knot.Index);
                _gesture = Gesture.None;
                return true;
            }

            _knot = knot;
            _knotAt = knot.Wire.Via[knot.Index];
            _gesture = Gesture.MoveKnot;
            return true;
        }

        if (HitNode(canvas) is null && FrameCornerAt(canvas) is { } frame)
        {
            _resizedFrame = frame.Id;
            _resizedTo = new UiVec2(frame.Bounds.Width, frame.Bounds.Height);
            _gesture = Gesture.ResizeFrame;
            return true;
        }

        if (e.ClickCount >= 2 && HitNode(canvas) is null && WireSegmentNear(e.Position) is { } segment)
        {
            RerouteAddRequested?.Invoke(segment.Wire, segment.Index, canvas);
            _gesture = Gesture.None;
            return true;
        }

        return false;
    }

    // The drag of a reroute point or a frame's corner, drawn where the pointer has it until let go.
    private void MoveEdgeGesture(UiVec2 canvas)
    {
        if (_gesture == Gesture.MoveKnot)
        {
            _knotAt = canvas;
        }
        else if (_gesture == Gesture.ResizeFrame && Source.Frames.FirstOrDefault(f => f.Id == _resizedFrame) is { } frame)
        {
            _resizedTo = new UiVec2(MathF.Max(SmallestFrame.X, canvas.X - frame.Bounds.X), MathF.Max(SmallestFrame.Y, canvas.Y - frame.Bounds.Y));
        }
    }

    // Let go: one request for the whole drag, so the owner records one step.
    private void EndEdgeGesture()
    {
        if (_gesture == Gesture.MoveKnot && _knot is { } knot && _knotAt != knot.Wire.Via[knot.Index])
        {
            RerouteMoveRequested?.Invoke(knot.Wire, knot.Index, _knotAt);
        }
        else if (_gesture == Gesture.ResizeFrame && _resizedFrame is { } frame)
        {
            FrameResizeRequested?.Invoke(frame, _resizedTo);
        }

        CancelEdgeGesture();
    }

    private void CancelEdgeGesture()
    {
        _knot = null;
        _resizedFrame = null;
    }

    // Where a frame is drawn: its own bounds, or the size its corner is being dragged to.
    private UiRect FrameBounds(CanvasFrame frame) =>
        frame.Id == _resizedFrame ? frame.Bounds with { Width = _resizedTo.X, Height = _resizedTo.Y } : frame.Bounds;

    // The grip a frame is resized by, in its bottom-right corner: three short diagonals.
    private void DrawFrameGrip(UiPainter painter, CanvasFrame frame)
    {
        UiRect box = ToScreen(FrameBounds(frame));
        float step = 4f * Zoom;
        Bgra32 ink = Mix(frame.Colour, Bgra32.White, 0.55f).WithAlpha(170);
        for (int i = 1; i <= 3; i++)
        {
            float d = step * i;
            painter.StrokeLine(new UiVec2(box.Right - d - (3f * Zoom), box.Bottom - (3f * Zoom)), new UiVec2(box.Right - (3f * Zoom), box.Bottom - d - (3f * Zoom)), MathF.Max(1f, 1.2f * Zoom), ink);
        }
    }

    // A wire drawn through its reroute points: a curve from the pin to each point in turn and on to the other pin, and a
    // dot on every point to take hold of.
    private void DrawRoutedWire(UiPainter painter, UiVec2 from, UiVec2 to, CanvasWire wire, bool lit)
    {
        UiVec2 last = from;
        IReadOnlyList<UiVec2> via = ViaOf(wire);
        foreach (UiVec2 point in via)
        {
            UiVec2 at = ToScreen(point);
            DrawWire(painter, last, at, wire.Colour, wire.IsExec, lit);
            last = at;
        }

        DrawWire(painter, last, to, wire.Colour, wire.IsExec, lit);
        foreach (UiVec2 point in via)
        {
            UiVec2 at = ToScreen(point);
            painter.FillDot(at, MathF.Max(3f, 5.5f * Zoom), Theme.Background);
            painter.FillDot(at, MathF.Max(2f, 4f * Zoom), wire.Colour.WithAlpha(255));
        }
    }

    // A wire's reroute points, the one being dragged where the drag has it.
    private IReadOnlyList<UiVec2> ViaOf(CanvasWire wire)
    {
        if (wire.Via.Count == 0 || _knot is not { } knot || !ReferenceEquals(knot.Wire, wire))
        {
            return wire.Via;
        }

        UiVec2[] via = [.. wire.Via];
        via[knot.Index] = _knotAt;
        return via;
    }

    // A wire's whole run on screen: its output pin, its reroute points, its input pin; null when either end is not drawn.
    private List<UiVec2>? ChainOf(CanvasWire wire, Dictionary<string, CanvasNode> byId)
    {
        if (!byId.TryGetValue(wire.FromNode, out CanvasNode? from) || !byId.TryGetValue(wire.ToNode, out CanvasNode? to))
        {
            return null;
        }

        List<UiVec2> chain = [ToScreen(PinAt(from, wire.FromPin, output: true))];
        chain.AddRange(ViaOf(wire).Select(ToScreen));
        chain.Add(ToScreen(PinAt(to, wire.ToPin, output: false)));
        return chain;
    }

    // The wire whose run passes within a few pixels of a screen point, and which stretch of it (where a new point goes).
    private (CanvasWire Wire, int Index)? WireSegmentNear(UiVec2 point)
    {
        Dictionary<string, CanvasNode> byId = Source.Nodes.ToDictionary(static n => n.Id, StringComparer.Ordinal);
        float reach = Theme.Px(6f);
        foreach (CanvasWire wire in Source.Wires)
        {
            if (ChainOf(wire, byId) is not { } chain)
            {
                continue;
            }

            for (int i = 0; i + 1 < chain.Count; i++)
            {
                if (CurveNear(point, chain[i], chain[i + 1], reach))
                {
                    return (wire, i);
                }
            }
        }

        return null;
    }

    // The reroute point under a screen point.
    private (CanvasWire Wire, int Index)? KnotAt(UiVec2 point)
    {
        float reach = MathF.Max(Theme.Px(6f), 7f * Zoom);
        foreach (CanvasWire wire in Source.Wires)
        {
            for (int i = 0; i < wire.Via.Count; i++)
            {
                if (UiVec2.Distance(ToScreen(wire.Via[i]), point) <= reach)
                {
                    return (wire, i);
                }
            }
        }

        return null;
    }

    // The frame whose bottom-right grip holds a canvas point, the topmost first.
    private CanvasFrame? FrameCornerAt(UiVec2 canvas)
    {
        for (int i = Source.Frames.Count - 1; i >= 0; i--)
        {
            UiRect box = FrameBounds(Source.Frames[i]);
            if (new UiRect(box.Right - CornerGrip, box.Bottom - CornerGrip, CornerGrip, CornerGrip).Contains(canvas))
            {
                return Source.Frames[i];
            }
        }

        return null;
    }

    // Whether a screen point lies within reach of the curve a wire draws between two screen points.
    private bool CurveNear(UiVec2 point, UiVec2 start, UiVec2 end, float reach)
    {
        if (point.X < MathF.Min(start.X, end.X) - (120f * Zoom) || point.X > MathF.Max(start.X, end.X) + (120f * Zoom)
            || point.Y < MathF.Min(start.Y, end.Y) - reach || point.Y > MathF.Max(start.Y, end.Y) + reach)
        {
            return false;
        }

        (UiVec2 a, UiVec2 b, UiVec2 c1, UiVec2 c2) = WireCurve(start, end);
        UiVec2 last = start;
        for (int i = 0; i <= 24; i++)
        {
            float t = i / 24f;
            float u = 1f - t;
            UiVec2 p = i == 0 ? a : new(
                (u * u * u * a.X) + (3f * u * u * t * c1.X) + (3f * u * t * t * c2.X) + (t * t * t * b.X),
                (u * u * u * a.Y) + (3f * u * u * t * c1.Y) + (3f * u * t * t * c2.Y) + (t * t * t * b.Y));
            if (DistanceToSegment(point, last, p) <= reach)
            {
                return true;
            }

            last = p;
        }

        return DistanceToSegment(point, last, end) <= reach;
    }
}
