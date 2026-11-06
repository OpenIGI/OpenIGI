using OpenIGI.Input;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Editor.Widgets;

/// <summary>
/// A node graph canvas: pans, zooms in steps, draws nodes, pins, wires and frames from a source, and turns gestures into
/// requests its owner carries out (move, wire, unwire, delete). It edits nothing itself, so its owner's commands and undo
/// history stay the one way a graph changes.
/// </summary>
/// <remarks>
/// <para>
/// The gestures follow the node editors people already know: the right or middle button pans, the wheel zooms about the
/// pointer in fixed steps from a tenth to double size, the left button selects (Shift adds, Ctrl toggles) and drags what is
/// selected, a drag on empty canvas draws a selection box, a drag from a pin draws a wire and dropping it on empty canvas
/// asks for a node to put there, Alt and a click on a pin breaks its wires, F frames the selection and Home frames it all.
/// </para>
/// <para>
/// Far out, detail goes first: pin names under 0.4 zoom, everything but titles under 0.25, so a whole level stays readable
/// as boxes and wires.
/// </para>
/// </remarks>
public sealed partial class NodeCanvas : EditorElement
{
    /// <summary>A node's width, in canvas units.</summary>
    public const float NodeWidth = 220f;

    /// <summary>A node's title bar height, in canvas units.</summary>
    public const float HeaderHeight = 28f;

    /// <summary>The extra title bar height a subtitle takes, in canvas units.</summary>
    public const float SubtitleHeight = 14f;

    /// <summary>How far in from its edge a pin sits, in canvas units.</summary>
    public const float PinInset = 12f;

    private const float BodyPadding = 5f;

    /// <summary>The height of one setting row under the pins, in canvas units.</summary>
    public const float DetailRowHeight = 20f;

    private const float DetailGap = 6f;

    /// <summary>The height of one pin row, in canvas units.</summary>
    public const float RowHeight = 24f;

    private static readonly float[] ZoomSteps =
    [
        0.1f, 0.125f, 0.15f, 0.175f, 0.2f, 0.25f, 0.3f, 0.375f, 0.5f, 0.625f, 0.75f, 0.875f, 1f, 1.25f, 1.5f, 1.75f, 2f,
    ];

    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
    private Gesture _gesture;
    private UiVec2 _pressAt;
    private UiVec2 _lastCanvas;
    private UiVec2 _hoverCanvas;
    private (string Node, string Pin, bool IsOutput)? _wireFrom;
    private string? _draggedFrame;
    private UiVec2 _dragDelta;
    private int _zoomStep = Array.IndexOf(ZoomSteps, 1f);

    /// <summary>Initializes a new instance of the <see cref="NodeCanvas"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="source">What to draw.</param>
    public NodeCanvas(EditorTheme theme, INodeCanvasSource source)
        : base(theme)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
    }

    /// <inheritdoc/>
    public override bool Focusable => true;

    /// <summary>Raised to move nodes by a canvas-unit offset; raised on every step of a drag, so the owner merges them.</summary>
    public event Action<IReadOnlyCollection<string>, UiVec2>? MoveRequested;

    /// <summary>Raised to move a frame or comment by a canvas-unit offset, with the nodes it covers.</summary>
    public event Action<string, UiVec2>? FrameMoveRequested;

    /// <summary>Raised to wire an output pin to an input pin: from node and pin, to node and pin.</summary>
    public event Action<string, string, string, string>? LinkRequested;

    /// <summary>Raised when a wire is dropped on empty canvas: the pin it came from, whether that is an output, and where it fell.</summary>
    public event Action<string, string, bool, UiVec2>? LinkDroppedOnEmpty;

    /// <summary>Raised to break every wire of a pin.</summary>
    public event Action<string, string>? UnlinkRequested;

    /// <summary>Raised to delete the selected nodes.</summary>
    public event Action<IReadOnlyCollection<string>>? DeleteRequested;

    /// <summary>Raised on a double click: a node's id, or <see langword="null"/> on empty canvas, and the canvas point.</summary>
    public event Action<string?, UiVec2>? DoubleClicked;

    /// <summary>Raised on a right click without a drag: a node's id or <see langword="null"/>, and the canvas point.</summary>
    public event Action<string?, UiVec2>? ContextRequested;

    /// <summary>Raised when the selection changes.</summary>
    public event Action? SelectionChanged;

    /// <summary>Raised when an inline value or tick box is clicked: the node and the input pin, to edit or flip its value.</summary>
    public event Action<string, string>? ValueClicked;

    /// <summary>Raised on Ctrl+F: find a node, in this graph or another.</summary>
    public event Action? FindRequested;

    /// <summary>Raised on Ctrl+C with nodes selected: the selection, to copy.</summary>
    public event Action<IReadOnlyCollection<string>>? CopyRequested;

    /// <summary>Raised on Ctrl+V: the canvas point under the pointer, to paste at.</summary>
    public event Action<UiVec2>? PasteRequested;

    /// <summary>Raised on Ctrl+D with nodes selected: the selection, to copy and paste beside itself.</summary>
    public event Action<IReadOnlyCollection<string>>? DuplicateRequested;

    private enum Gesture
    {
        None,
        Pan,
        Marquee,
        MoveNodes,
        Wire,
        MoveFrame,
        ResizeFrame,
        MoveKnot,
    }

    /// <summary>Gets or sets what the canvas draws.</summary>
    public INodeCanvasSource Source { get; set; }

    /// <summary>Gets the canvas point at the view's top-left corner.</summary>
    public UiVec2 Scroll { get; private set; }

    /// <summary>Gets the zoom: screen pixels per canvas unit.</summary>
    public float Zoom => ZoomSteps[_zoomStep] * Theme.Scale;

    /// <summary>Gets the selected nodes.</summary>
    public IReadOnlyCollection<string> Selected => _selected;

    /// <summary>Gets a value indicating whether the minimap is drawn.</summary>
    public bool ShowMinimap { get; set; } = true;

    /// <summary>Selects exactly these nodes.</summary>
    /// <param name="ids">The nodes.</param>
    public void Select(IEnumerable<string> ids)
    {
        _selected.Clear();
        _selected.UnionWith(ids);
        SelectionChanged?.Invoke();
    }

    /// <summary>Centres the view on some nodes, zoomed to fit them, or on all of them with none given.</summary>
    /// <param name="ids">The nodes, or <see langword="null"/> for all.</param>
    public void Frame(IReadOnlyCollection<string>? ids = null)
    {
        UiRect[] boxes = [.. Source.Nodes.Where(n => ids is null || ids.Contains(n.Id)).Select(SizeOf)];
        if (boxes.Length == 0)
        {
            return;
        }

        float left = boxes.Min(static b => b.X) - 40f;
        float top = boxes.Min(static b => b.Y) - 40f;
        float right = boxes.Max(static b => b.Right) + 40f;
        float bottom = boxes.Max(static b => b.Bottom) + 40f;
        float fit = MathF.Min(Bounds.Width / (right - left), Bounds.Height / (bottom - top)) / Theme.Scale;
        _zoomStep = Math.Max(0, Array.FindLastIndex(ZoomSteps, z => z <= MathF.Min(fit, 1f)));
        UiVec2 centre = new((left + right) * 0.5f, (top + bottom) * 0.5f);
        Scroll = new UiVec2(centre.X - (Bounds.Width * 0.5f / Zoom), centre.Y - (Bounds.Height * 0.5f / Zoom));
    }

    /// <summary>The screen point of a canvas point.</summary>
    /// <param name="canvas">The canvas point.</param>
    /// <returns>The screen point.</returns>
    public UiVec2 ToScreen(UiVec2 canvas) =>
        new(Bounds.X + ((canvas.X - Scroll.X) * Zoom), Bounds.Y + ((canvas.Y - Scroll.Y) * Zoom));

    /// <summary>The canvas point of a screen point.</summary>
    /// <param name="screen">The screen point.</param>
    /// <returns>The canvas point.</returns>
    public UiVec2 ToCanvas(UiVec2 screen) =>
        new(Scroll.X + ((screen.X - Bounds.X) / Zoom), Scroll.Y + ((screen.Y - Bounds.Y) / Zoom));

    /// <summary>The height of a node's title bar: its title, and its subtitle when it has one.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The height, in canvas units.</returns>
    public static float HeaderOf(CanvasNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Subtitle is null ? HeaderHeight : HeaderHeight + SubtitleHeight;
    }

    /// <summary>A node's box, in canvas units.</summary>
    /// <param name="node">The node.</param>
    /// <returns>Its box.</returns>
    public static UiRect SizeOf(CanvasNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        int rows = node.Collapsed ? 0 : Math.Max(node.Inputs.Count, node.Outputs.Count);
        float details = node.Collapsed || node.Details.Count == 0 ? 0f : DetailGap + (node.Details.Count * DetailRowHeight);
        float body = node.Collapsed ? 0f : BodyPadding + (Math.Max(1, rows) * RowHeight) + BodyPadding + details;
        return new UiRect(node.Position.X, node.Position.Y, NodeWidth, HeaderOf(node) + body);
    }

    /// <summary>A pin's centre, in canvas units.</summary>
    /// <param name="node">The node.</param>
    /// <param name="pin">The pin's id.</param>
    /// <param name="output">Whether it is an output.</param>
    /// <returns>The centre, or the node's title edge when the node is folded or has no such pin.</returns>
    public static UiVec2 PinAt(CanvasNode node, string pin, bool output)
    {
        ArgumentNullException.ThrowIfNull(node);
        IReadOnlyList<CanvasPin> pins = output ? node.Outputs : node.Inputs;
        int row = -1;
        for (int i = 0; i < pins.Count; i++)
        {
            if (pins[i].Id == pin)
            {
                row = i;
                break;
            }
        }

        // Pins sit just inside the edge, as node editors draw them, so a wire meets the node rather than floating off it.
        float x = output ? node.Position.X + NodeWidth - PinInset : node.Position.X + PinInset;
        if (node.Collapsed || row < 0)
        {
            return new UiVec2(x, node.Position.Y + (HeaderOf(node) * 0.5f));
        }

        return new UiVec2(x, node.Position.Y + HeaderOf(node) + BodyPadding + (row * RowHeight) + (RowHeight * 0.5f));
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected internal override bool OnPointerDown(in PointerEvent e)
    {
        _pressAt = e.Position;
        _lastCanvas = ToCanvas(e.Position);
        if (e.Button is MouseButton.Right or MouseButton.Middle)
        {
            _gesture = Gesture.Pan;
            return true;
        }

        if (NavigateByButton(e.Button))
        {
            return true;
        }

        if (e.Button != MouseButton.Left)
        {
            return false;
        }

        if (ShowMinimap && MinimapRect() is { } map && map.Contains(e.Position))
        {
            JumpTo(map, e.Position);
            _gesture = Gesture.None;
            return true;
        }

        // An inline value or tick box takes the click before its node does.
        foreach ((CanvasNode fieldNode, CanvasPin fieldPin) in Source.Nodes.SelectMany(static n => n.Inputs.Select(p => (n, p))))
        {
            if (ValueField(fieldNode, fieldPin) is { } box && box.Contains(e.Position))
            {
                ValueClicked?.Invoke(fieldNode.Id, fieldPin.Id);
                _gesture = Gesture.None;
                return true;
            }
        }

        if (BeginEdgeGesture(e, _lastCanvas))
        {
            return true;
        }

        if (HitPin(_lastCanvas) is { } pin)
        {
            if (e.Alt)
            {
                UnlinkRequested?.Invoke(pin.Node, pin.Pin);
                return true;
            }

            _wireFrom = pin;
            _gesture = Gesture.Wire;
            return true;
        }

        if (HitNode(_lastCanvas) is { } node)
        {
            if (e.ClickCount >= 2)
            {
                DoubleClicked?.Invoke(node.Id, _lastCanvas);
                return true;
            }

            if (e.Control)
            {
                Toggle(node.Id);
            }
            else if (!_selected.Contains(node.Id))
            {
                if (!e.Shift)
                {
                    _selected.Clear();
                }

                _selected.Add(node.Id);
                SelectionChanged?.Invoke();
            }

            BeginGrab(node, _lastCanvas);
            _gesture = Gesture.MoveNodes;
            return true;
        }

        if (HitFrameTitle(_lastCanvas) is { } frame)
        {
            _draggedFrame = frame.Id;
            _gesture = Gesture.MoveFrame;
            return true;
        }

        if (e.ClickCount >= 2)
        {
            DoubleClicked?.Invoke(null, _lastCanvas);
            return true;
        }

        if (!e.Shift && !e.Control && _selected.Count > 0)
        {
            _selected.Clear();
            SelectionChanged?.Invoke();
        }

        _gesture = Gesture.Marquee;
        return true;
    }

    /// <inheritdoc/>
    protected internal override void OnPointerMove(in PointerEvent e)
    {
        UiVec2 canvas = ToCanvas(e.Position);
        _hoverCanvas = canvas;
        switch (_gesture)
        {
            case Gesture.Pan:
                UiVec2 moved = new((e.Position.X - _pressAt.X) / Zoom, (e.Position.Y - _pressAt.Y) / Zoom);
                Scroll = new UiVec2(Scroll.X - moved.X, Scroll.Y - moved.Y);
                _pressAt = e.Position;
                return;

            case Gesture.MoveNodes when _selected.Count > 0:
                // Moved only on screen while dragged; one move is asked for on release, so the owner rebuilds once.
                UiVec2 step = GrabDelta(canvas);
                _dragDelta = new UiVec2(_dragDelta.X + step.X, _dragDelta.Y + step.Y);

                break;

            case Gesture.MoveFrame when _draggedFrame is { } frame:
                FrameMoveRequested?.Invoke(frame, new UiVec2(canvas.X - _lastCanvas.X, canvas.Y - _lastCanvas.Y));
                break;

            case Gesture.ResizeFrame or Gesture.MoveKnot:
                MoveEdgeGesture(canvas);
                break;
        }

        _lastCanvas = canvas;
    }

    /// <inheritdoc/>
    protected internal override void OnPointerUp(in PointerEvent e)
    {
        _grabbed = null;
        if (_gesture == Gesture.MoveNodes && (_dragDelta.X != 0f || _dragDelta.Y != 0f) && _selected.Count > 0)
        {
            MoveRequested?.Invoke([.. _selected], _dragDelta);
        }

        _dragDelta = default;
        UiVec2 canvas = ToCanvas(e.Position);
        bool clicked = UiVec2.Distance(e.Position, _pressAt) < Theme.Px(4f);
        switch (_gesture)
        {
            case Gesture.Pan when clicked && e.Button == MouseButton.Right && HitNode(canvas) is null && HitFrameTitle(canvas) is { } titled:
                FrameContextRequested?.Invoke(titled.Id, canvas);
                break;

            case Gesture.Pan when clicked && e.Button == MouseButton.Right:
                ContextRequested?.Invoke(HitNode(canvas)?.Id, canvas);
                break;

            case Gesture.ResizeFrame or Gesture.MoveKnot:
                EndEdgeGesture();
                break;

            case Gesture.Marquee:
                SelectInBox(ToCanvas(_pressAt), canvas, add: e.Shift || e.Control);
                break;

            case Gesture.Wire when _wireFrom is { } from:
                FinishWire(from, canvas);
                break;
        }

        _gesture = Gesture.None;
        _wireFrom = null;
        _draggedFrame = null;
    }

    /// <inheritdoc/>
    protected internal override void OnCaptureLost()
    {
        CancelEdgeGesture();
        _gesture = Gesture.None;
        _wireFrom = null;
        _draggedFrame = null;
    }

    /// <inheritdoc/>
    protected internal override bool OnWheel(UiVec2 position, float notches, KeyModifiers modifiers)
    {
        int step = Math.Clamp(_zoomStep + (notches > 0 ? 1 : -1), 0, ZoomSteps.Length - 1);
        if (step == _zoomStep)
        {
            return true;
        }

        // Zoom about the pointer: the canvas point under it stays under it.
        UiVec2 anchor = ToCanvas(position);
        _zoomStep = step;
        _zoomShownFor = ZoomReadoutSeconds;
        Scroll = new UiVec2(anchor.X - ((position.X - Bounds.X) / Zoom), anchor.Y - ((position.Y - Bounds.Y) / Zoom));
        return true;
    }

    /// <inheritdoc/>
    protected internal override bool OnKeyDown(in KeyEvent e)
    {
        if (NavigateByKey(e))
        {
            return true;
        }

        switch (e.Key)
        {
            case KeyCode.Delete or KeyCode.Backspace when _selected.Count > 0:
                DeleteRequested?.Invoke([.. _selected]);
                return true;

            case KeyCode.F when e.Control:
                FindRequested?.Invoke();
                return true;

            case KeyCode.C when e.Control && _selected.Count > 0:
                CopyRequested?.Invoke([.. _selected]);
                return true;

            case KeyCode.V when e.Control:
                PasteRequested?.Invoke(_hoverCanvas);
                return true;

            case KeyCode.D when e.Control && _selected.Count > 0:
                DuplicateRequested?.Invoke([.. _selected]);
                return true;

            case KeyCode.F:
                Frame(_selected.Count > 0 ? _selected : null);
                return true;

            case KeyCode.Home:
                Frame();
                return true;

            case KeyCode.A when e.Control:
                Select(Source.Nodes.Select(static n => n.Id));
                return true;

            case KeyCode.Escape when _selected.Count > 0:
                Select([]);
                return true;

            default:
                return OnToolKey(e);
        }
    }


    private void FinishWire((string Node, string Pin, bool IsOutput) from, UiVec2 canvas)
    {
        if (HitPin(canvas) is { } to && to.Node != from.Node && to.IsOutput != from.IsOutput)
        {
            (string Node, string Pin, bool IsOutput) output = from.IsOutput ? from : to;
            (string Node, string Pin, bool IsOutput) input = from.IsOutput ? to : from;
            LinkRequested?.Invoke(output.Node, output.Pin, input.Node, input.Pin);
        }
        else if (HitNode(canvas) is null)
        {
            LinkDroppedOnEmpty?.Invoke(from.Node, from.Pin, from.IsOutput, canvas);
        }
    }

    private void Toggle(string id)
    {
        if (!_selected.Remove(id))
        {
            _selected.Add(id);
        }

        SelectionChanged?.Invoke();
    }

    private void SelectInBox(UiVec2 a, UiVec2 b, bool add)
    {
        UiRect box = new(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Abs(a.X - b.X), MathF.Abs(a.Y - b.Y));
        if (!add)
        {
            _selected.Clear();
        }

        foreach (CanvasNode node in Source.Nodes)
        {
            UiRect size = SizeOf(node);
            if (size.X < box.Right && size.Right > box.X && size.Y < box.Bottom && size.Bottom > box.Y)
            {
                _selected.Add(node.Id);
            }
        }

        SelectionChanged?.Invoke();
    }

    private CanvasNode? HitNode(UiVec2 canvas)
    {
        for (int i = Source.Nodes.Count - 1; i >= 0; i--)
        {
            if (SizeOf(Source.Nodes[i]).Contains(canvas))
            {
                return Source.Nodes[i];
            }
        }

        return null;
    }

    private (string Node, string Pin, bool IsOutput)? HitPin(UiVec2 canvas)
    {
        float reach = 9f;
        foreach (CanvasNode node in Source.Nodes)
        {
            if (node.Collapsed)
            {
                continue;
            }

            foreach ((IReadOnlyList<CanvasPin> pins, bool output) in new[] { (node.Inputs, false), (node.Outputs, true) })
            {
                foreach (CanvasPin pin in pins)
                {
                    UiVec2 at = PinAt(node, pin.Id, output);
                    if (MathF.Abs(at.X - canvas.X) <= reach && MathF.Abs(at.Y - canvas.Y) <= reach)
                    {
                        return (node.Id, pin.Id, output);
                    }
                }
            }
        }

        return null;
    }

    private CanvasFrame? HitFrameTitle(UiVec2 canvas)
    {
        for (int i = Source.Frames.Count - 1; i >= 0; i--)
        {
            UiRect title = FrameBounds(Source.Frames[i]) with { Height = 26f };
            if (title.Contains(canvas))
            {
                return Source.Frames[i];
            }
        }

        return null;
    }
}
