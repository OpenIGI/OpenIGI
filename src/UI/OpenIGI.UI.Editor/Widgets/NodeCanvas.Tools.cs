using System.Globalization;
using OpenIGI.Input;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Text;

namespace OpenIGI.UI.Editor.Widgets;

/// <content>
/// The tools node editors give: snapping to the grid, aligning and straightening, the palette on Space, comments on C, a
/// zoom readout, and hover help on nodes, pins and wires.
/// </content>
public sealed partial class NodeCanvas
{
    /// <summary>The grid nodes snap to while dragged, in canvas units.</summary>
    public const float SnapStep = 16f;

    private const float ZoomReadoutSeconds = 1.2f;

    private string? _grabbed;
    private UiVec2 _grabbedAt;
    private UiVec2 _grabOffset;
    private float _zoomShownFor;

    /// <summary>Raised on C: put a comment round the selection.</summary>
    public event Action? CommentRequested;

    /// <summary>Gets or sets a value indicating whether dragged nodes land on the grid.</summary>
    public bool SnapToGrid { get; set; } = true;

    /// <summary>Lines the selected nodes up along one edge: the edge furthest that way among them.</summary>
    /// <param name="edge">Which edge.</param>
    public void Align(CanvasEdge edge)
    {
        CanvasNode[] nodes = [.. Source.Nodes.Where(n => _selected.Contains(n.Id))];
        if (nodes.Length < 2)
        {
            return;
        }

        UiRect[] boxes = [.. nodes.Select(SizeOf)];
        float target = edge switch
        {
            CanvasEdge.Top => boxes.Min(static b => b.Y),
            CanvasEdge.Bottom => boxes.Max(static b => b.Bottom),
            CanvasEdge.Left => boxes.Min(static b => b.X),
            _ => boxes.Max(static b => b.Right),
        };
        for (int i = 0; i < nodes.Length; i++)
        {
            UiRect box = boxes[i];
            UiVec2 by = edge switch
            {
                CanvasEdge.Top => new UiVec2(0f, target - box.Y),
                CanvasEdge.Bottom => new UiVec2(0f, target - box.Bottom),
                CanvasEdge.Left => new UiVec2(target - box.X, 0f),
                _ => new UiVec2(target - box.Right, 0f),
            };
            if (by.X != 0f || by.Y != 0f)
            {
                MoveRequested?.Invoke([nodes[i].Id], by);
            }
        }
    }

    /// <summary>
    /// Straightens the wires into the selected nodes: each moves up or down so its first wired input sits level with the pin
    /// that feeds it.
    /// </summary>
    public void Straighten()
    {
        Dictionary<string, CanvasNode> byId = Source.Nodes.ToDictionary(static n => n.Id, StringComparer.Ordinal);
        foreach (CanvasNode node in Source.Nodes.Where(n => _selected.Contains(n.Id)))
        {
            CanvasWire? wire = node.Inputs
                .Select(p => Source.Wires.FirstOrDefault(w => w.ToNode == node.Id && w.ToPin == p.Id))
                .FirstOrDefault(static w => w is not null);
            if (wire is null || !byId.TryGetValue(wire.FromNode, out CanvasNode? from))
            {
                continue;
            }

            float by = PinAt(from, wire.FromPin, output: true).Y - PinAt(node, wire.ToPin, output: false).Y;
            if (MathF.Abs(by) > 0.5f)
            {
                MoveRequested?.Invoke([node.Id], new UiVec2(0f, by));
            }
        }
    }

    // A drag starts on a node: it is the one that snaps, the rest of the selection keeping their places relative to it.
    private void BeginGrab(CanvasNode node, UiVec2 canvas)
    {
        _grabbed = node.Id;
        _grabbedAt = node.Position;
        _grabOffset = new UiVec2(canvas.X - node.Position.X, canvas.Y - node.Position.Y);
    }

    // How far the selection moves for the pointer at a canvas point: where the grabbed node lands, snapped, less where it is.
    private UiVec2 GrabDelta(UiVec2 canvas)
    {
        if (_grabbed is null)
        {
            return new UiVec2(canvas.X - _lastCanvas.X, canvas.Y - _lastCanvas.Y);
        }

        UiVec2 target = new(canvas.X - _grabOffset.X, canvas.Y - _grabOffset.Y);
        if (SnapToGrid)
        {
            target = new UiVec2(MathF.Round(target.X / SnapStep) * SnapStep, MathF.Round(target.Y / SnapStep) * SnapStep);
        }

        UiVec2 delta = new(target.X - _grabbedAt.X, target.Y - _grabbedAt.Y);
        _grabbedAt = target;
        return delta;
    }

    // The keys past the basics: Space searches, C comments, Q straightens, Shift with W/A/S/D aligns.
    private bool OnToolKey(in KeyEvent e)
    {
        UiVec2 at = Root?.PointerPosition is { } pointer && Bounds.Contains(pointer) ? ToCanvas(pointer) : ToCanvas(Bounds.Center);
        switch (e.Key)
        {
            case KeyCode.Space:
                ContextRequested?.Invoke(null, at);
                return true;

            case KeyCode.C when !e.Control && _selected.Count > 0:
                CommentRequested?.Invoke();
                return true;

            case KeyCode.Q when _selected.Count > 0:
                Straighten();
                return true;

            case KeyCode.W when e.Shift:
                Align(CanvasEdge.Top);
                return true;

            case KeyCode.S when e.Shift:
                Align(CanvasEdge.Bottom);
                return true;

            case KeyCode.A when e.Shift:
                Align(CanvasEdge.Left);
                return true;

            case KeyCode.D when e.Shift:
                Align(CanvasEdge.Right);
                return true;

            default:
                return false;
        }
    }

    // Hover help: what is under the pointer, as the tooltip. A pin says its name and type; a wire what it carries.
    private void UpdateHoverHelp(Dictionary<string, CanvasNode> byId)
    {
        if (Root?.PointerPosition is not { } pointer || !Bounds.Contains(pointer) || _gesture != Gesture.None)
        {
            Tooltip = null;
            _hoverWire = null;
            return;
        }

        UiVec2 canvas = ToCanvas(pointer);
        if (_hoverPin is { } pin && byId.TryGetValue(pin.Node, out CanvasNode? owner)
            && (pin.IsOutput ? owner.Outputs : owner.Inputs).FirstOrDefault(p => p.Id == pin.Pin) is { } hovered)
        {
            Tooltip = hovered.Tooltip ?? hovered.Label;
            _hoverWire = null;
            return;
        }

        _hoverWire = WireNear(pointer, byId);
        if (_hoverWire is { } wire && byId.TryGetValue(wire.FromNode, out CanvasNode? source) && byId.TryGetValue(wire.ToNode, out CanvasNode? target))
        {
            string from = source.Outputs.FirstOrDefault(p => p.Id == wire.FromPin)?.Label ?? wire.FromPin;
            string to = target.Inputs.FirstOrDefault(p => p.Id == wire.ToPin)?.Label ?? wire.ToPin;
            Tooltip = $"{source.Title} · {from}  →  {target.Title} · {to}\nAlt-click either pin to break it.";
            return;
        }

        Tooltip = HitNode(canvas) is { } node ? DetailAt(node, canvas)?.Tooltip ?? node.Tooltip ?? node.Subtitle : null;
    }

    // The setting row of a node under a canvas point, if any.
    private static CanvasDetail? DetailAt(CanvasNode node, UiVec2 canvas)
    {
        if (node.Details.Count == 0 || node.Collapsed)
        {
            return null;
        }

        UiRect box = SizeOf(node);
        float top = box.Bottom - ((node.Details.Count * DetailRowHeight) + 4f);
        int row = (int)MathF.Floor((canvas.Y - top) / DetailRowHeight);
        return row >= 0 && row < node.Details.Count ? node.Details[row] : null;
    }

    // The wire whose run, through its reroute points, passes within a few pixels of a screen point.
    private CanvasWire? WireNear(UiVec2 point, Dictionary<string, CanvasNode> byId)
    {
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
                    return wire;
                }
            }
        }

        return null;
    }

    // A wire leaves its pin straight for a short lead, then curves: the tangent eases with how sideways the wire runs and is
    // never longer than half the wire, so a short wire stays nearly straight instead of kinking into an S.
    private (UiVec2 A, UiVec2 B, UiVec2 C1, UiVec2 C2) WireCurve(UiVec2 from, UiVec2 to)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y;
        float length = MathF.Max(0.5f, MathF.Sqrt((dx * dx) + (dy * dy)));
        float sideways = 1f - MathF.Abs(dy / length);
        float ease = 1f - MathF.Pow(2f, -10f * sideways);
        float lead = Math.Clamp(16f * Zoom * ease, 0f, MathF.Max(0f, (length * 0.5f) - (16f * Zoom)));

        // Running backwards, the wire loops out and round rather than through its own nodes.
        float tangent = dx < 0f ? MathF.Max(80f * Zoom, MathF.Abs(dx) * 0.5f) : Math.Clamp(80f * Zoom * ease, 0f, length * 0.5f);
        tangent = MathF.Max(tangent, MathF.Abs(dx) * 0.4f);
        UiVec2 a = new(from.X + lead, from.Y);
        UiVec2 b = new(to.X - lead, to.Y);
        return (a, b, new UiVec2(a.X + tangent, a.Y), new UiVec2(b.X - tangent, b.Y));
    }

    // The zoom, top right, for a moment after it changes: "Zoom 1:1", "Zoom -3".
    private void DrawZoomReadout(UiPainter painter, float elapsed)
    {
        _zoomShownFor = MathF.Max(0f, _zoomShownFor - elapsed);
        if (_zoomShownFor <= 0f)
        {
            return;
        }

        int one = Array.IndexOf(ZoomSteps, 1f);
        string text = _zoomStep == one ? "Zoom 1:1" : string.Create(CultureInfo.InvariantCulture, $"Zoom {(_zoomStep > one ? "+" : string.Empty)}{_zoomStep - one}");
        byte alpha = (byte)(255 * MathF.Min(1f, _zoomShownFor / 0.3f));
        UiFont font = Theme.Fonts.Font(EditorFontFace.InterfaceStrong, Theme.Px(16f));
        float width = font.MeasureWidth(text, Theme.Px(16f));
        painter.DrawText(font, text, new UiVec2(Bounds.Right - width - Theme.Px(14f), Bounds.Y + Theme.Px(10f)), Theme.Px(16f), Theme.TextMuted.WithAlpha(alpha));
    }

    private static float DistanceToSegment(UiVec2 p, UiVec2 a, UiVec2 b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float lengthSquared = (dx * dx) + (dy * dy);
        float t = lengthSquared <= 1e-6f ? 0f : Math.Clamp((((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / lengthSquared, 0f, 1f);
        float x = a.X + (t * dx) - p.X, y = a.Y + (t * dy) - p.Y;
        return MathF.Sqrt((x * x) + (y * y));
    }
}

/// <summary>An edge of a box, to align nodes along.</summary>
public enum CanvasEdge
{
    /// <summary>The top edge.</summary>
    Top,

    /// <summary>The bottom edge.</summary>
    Bottom,

    /// <summary>The left edge.</summary>
    Left,

    /// <summary>The right edge.</summary>
    Right,
}
