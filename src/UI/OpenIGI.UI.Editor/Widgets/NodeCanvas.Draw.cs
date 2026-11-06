using OpenIGI.UI.Drawing;
using OpenIGI.Core.Graphics;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;

namespace OpenIGI.UI.Editor.Widgets;

/// <content>Drawing: grid, frames, wires, nodes, the wire being dragged, the selection box and the minimap.</content>
public sealed partial class NodeCanvas
{
    // Below these zooms pin names, then everything but titles, are left out.
    private const float PinLabelZoom = 0.4f;
    private const float TitleOnlyZoom = 0.25f;
    private const float GridStep = 32f;
    private const int WireSegments = 24;

    // Curve segments a frame may spend on wires before the rest are drawn straight: about a fifth of the 2D ring, a feathered
    // segment costing some twenty vertices.
    private const int SegmentBudget = 1200;

    private int _segmentsLeft;

    // This frame: the pin under the pointer, and the outputs a wire leaves, so each pin draws as hovered or wired.
    private (string Node, string Pin, bool IsOutput)? _hoverPin;
    private readonly HashSet<(string Node, string Pin)> _wiredOutputs = [];
    private CanvasWire? _hoverWire;


    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        _segmentsLeft = SegmentBudget;
        _hoverPin = Root?.PointerPosition is { } pointer && Bounds.Contains(pointer) ? HitPin(ToCanvas(pointer)) : null;
        _wiredOutputs.Clear();
        foreach (CanvasWire wire in Source.Wires)
        {
            _wiredOutputs.Add((wire.FromNode, wire.FromPin));
        }

        painter.FillRect(Bounds, Theme.Background);
        painter.PushClip(Bounds);
        DrawGrid(painter);
        foreach (CanvasFrame frame in Source.Frames)
        {
            DrawFrame(painter, frame);
            DrawFrameGrip(painter, frame);
        }

        // Nodes being dragged are drawn where the drag has them; the source moves them on release.
        IReadOnlyList<CanvasNode> nodes = Dragged();
        Dictionary<string, CanvasNode> byId = nodes.ToDictionary(static n => n.Id, StringComparer.Ordinal);
        UpdateHoverHelp(byId);
        foreach (CanvasWire wire in Source.Wires)
        {
            if (byId.TryGetValue(wire.FromNode, out CanvasNode? from) && byId.TryGetValue(wire.ToNode, out CanvasNode? to))
            {
                bool lit = _selected.Contains(wire.FromNode) || _selected.Contains(wire.ToNode) || ReferenceEquals(wire, _hoverWire);
                DrawRoutedWire(painter, ToScreen(PinAt(from, wire.FromPin, output: true)), ToScreen(PinAt(to, wire.ToPin, output: false)), wire, lit);
            }
        }

        foreach (CanvasNode node in nodes)
        {
            DrawNode(painter, node);
        }

        DrawGestures(painter, byId);
        if (ShowMinimap)
        {
            DrawMinimap(painter);
        }

        DrawZoomReadout(painter, 1f / 60f);
        painter.PopClip();
    }

    private void DrawGrid(UiPainter painter)
    {
        float step = GridStep * Zoom;
        if (step < Theme.Px(8f))
        {
            step *= 4f;
        }

        Bgra32 fine = Theme.Border.WithAlpha(40);
        Bgra32 strong = Theme.Border.WithAlpha(90);
        float startX = Bounds.X - Mod(Scroll.X * Zoom, step);
        float startY = Bounds.Y - Mod(Scroll.Y * Zoom, step);
        int index = (int)MathF.Floor(Scroll.X * Zoom / step);
        for (float x = startX; x < Bounds.Right; x += step, index++)
        {
            painter.FillRect(new UiRect(MathF.Round(x), Bounds.Y, 1f, Bounds.Height), index % 8 == 0 ? strong : fine);
        }

        index = (int)MathF.Floor(Scroll.Y * Zoom / step);
        for (float y = startY; y < Bounds.Bottom; y += step, index++)
        {
            painter.FillRect(new UiRect(Bounds.X, MathF.Round(y), Bounds.Width, 1f), index % 8 == 0 ? strong : fine);
        }
    }

    // A comment or gated frame, as node editors draw comment boxes: a rounded, faintly tinted body behind its nodes with a
    // hairline edge, under a solid title bar in its colour.
    private void DrawFrame(UiPainter painter, CanvasFrame frame)
    {
        UiRect box = ToScreen(FrameBounds(frame));
        float radius = 6f * Zoom;
        float bar = MathF.Min(box.Height, 30f * Zoom);
        Bgra32 edge = Mix(frame.Colour, Theme.Background, 0.35f);
        EditorDraw.Fill(painter, Theme, new UiRect(box.X - 1f, box.Y - 1f, box.Width + 2f, box.Height + 2f), edge.WithAlpha(150), radius + 1f);
        EditorDraw.Fill(painter, Theme, box, Mix(Theme.Background, frame.Colour, frame.IsComment ? 0.16f : 0.1f).WithAlpha(215), radius);
        UiRect title = box with { Height = bar };
        EditorDraw.Fill(painter, Theme, title, Mix(frame.Colour, Theme.Background, 0.3f), radius);
        painter.FillRect(UiRect.FromEdges(title.X, title.Bottom - radius, title.Right, title.Bottom), Mix(frame.Colour, Theme.Background, 0.3f));
        painter.FillGradient(
            UiRect.FromEdges(title.X + radius, title.Y + 1f, title.Right - radius, title.Y + (bar * 0.5f)),
            [new UiGradientStop(0f, Bgra32.White.WithAlpha(26)), new UiGradientStop(1f, Bgra32.White.WithAlpha(0))],
            UiGradientDirection.Vertical);
        if (Zoom >= TitleOnlyZoom * Theme.Scale)
        {
            // A gate says what it is beside its name, so a frame never reads as a mere comment.
            string text = frame.Title;
            Label(painter, text, title with { X = title.X + (12f * Zoom), Width = title.Width - (24f * Zoom) }, Bgra32.White.WithAlpha(235), strong: true, size: 15f);
            if (!frame.IsComment && Zoom >= PinLabelZoom * Theme.Scale)
            {
                Label(painter, "Runs while its condition holds", title with { X = title.X + (12f * Zoom), Width = title.Width - (24f * Zoom) }, Bgra32.White.WithAlpha(140), strong: false, size: 11f, alignRight: true);
            }
        }
    }

    // One quad, no feathered edges: far out a feather is under a pixel and would cost four more quads a segment.
    private static void HardLine(UiPainter painter, UiVec2 from, UiVec2 to, float thickness, Bgra32 colour)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length <= 1e-4f)
        {
            return;
        }

        float nx = -dy / length * thickness * 0.5f, ny = dx / length * thickness * 0.5f;
        Span<UiVec2> quad = [new(from.X + nx, from.Y + ny), new(to.X + nx, to.Y + ny), new(to.X - nx, to.Y - ny), new(from.X - nx, from.Y - ny)];
        painter.FillPolygon(quad, colour, smoothEdges: false);
    }

    // A wire leaves its pin straight for a short lead, then curves: the tangent eases with how sideways the wire runs and is
    // never longer than half the wire, so a short wire stays nearly straight instead of kinking into an S.
    private void DrawWire(UiPainter painter, UiVec2 from, UiVec2 to, Bgra32 colour, bool exec, bool lit)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length < 0.5f)
        {
            return;
        }

        (UiVec2 a, UiVec2 b, UiVec2 c1, UiVec2 c2) = WireCurve(from, to);

        float left = MathF.Min(MathF.Min(from.X, to.X), MathF.Min(c1.X, c2.X));
        float right = MathF.Max(MathF.Max(from.X, to.X), MathF.Max(c1.X, c2.X));
        if (right < Bounds.X || left > Bounds.Right || MathF.Max(from.Y, to.Y) < Bounds.Y || MathF.Min(from.Y, to.Y) > Bounds.Bottom)
        {
            return;
        }

        float width = (exec ? 3.4f : 2.6f) * MathF.Max(0.6f, Zoom / Theme.Scale) * Theme.Scale * (lit ? 1.45f : 1f);
        // Opaque: a see-through wire shows a bead wherever its segments overlap.
        Bgra32 ink = colour.WithAlpha(255);

        // As many segments as the curve needs on screen, within the frame budget: past it, straight lines.
        float span = MathF.Abs(dx) + MathF.Abs(dy);
        int segments = _segmentsLeft <= 0 ? 1 : Math.Clamp((int)(span / Theme.Px(10f)), 6, WireSegments);
        _segmentsLeft -= segments;
        bool soft = Zoom >= PinLabelZoom * Theme.Scale && segments > 1;
        // The whole wire as one list of points: its lead out, the curve, its lead in.
        Span<UiVec2> points = stackalloc UiVec2[segments + 3];
        int count = 0;
        points[count++] = from;
        if (segments > 1)
        {
            points[count++] = a;
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                float u = 1f - t;
                points[count++] = new UiVec2(
                    (u * u * u * a.X) + (3f * u * u * t * c1.X) + (3f * u * t * t * c2.X) + (t * t * t * b.X),
                    (u * u * u * a.Y) + (3f * u * u * t * c1.Y) + (3f * u * t * t * c2.Y) + (t * t * t * b.Y));
            }
        }

        points[count++] = to;
        if (soft)
        {
            // One strip, so the segments meet without a seam.
            painter.StrokeSoftPolyline(points[..count], width, ink);
            return;
        }

        for (int i = 0; i + 1 < count; i++)
        {
            HardLine(painter, points[i], points[i + 1], MathF.Max(1f, width * 0.6f), ink);
        }
    }

    private void DrawNode(UiPainter painter, CanvasNode node)
    {
        UiRect box = ToScreen(SizeOf(node));
        if (box.Right < Bounds.X || box.X > Bounds.Right || box.Bottom < Bounds.Y || box.Y > Bounds.Bottom)
        {
            return;
        }

        bool selected = _selected.Contains(node.Id);
        Bgra32 tint = node.IsEvent ? Theme.Accent : node.Header;

        // Far out a level's every node is in view: each is two flat boxes, no shadow, pins or text, so it fits the 2D ring.
        if (Zoom < TitleOnlyZoom * Theme.Scale)
        {
            painter.FillRect(box, Theme.Panel);
            painter.FillRect(box with { Height = MathF.Max(2f, HeaderOf(node) * Zoom) }, selected ? Theme.Accent : tint);
            if (node.Problem is not null)
            {
                painter.StrokeRect(box, 1f, node.IsError ? Theme.Error : Theme.Warning);
            }

            return;
        }

        float radius = 5f * Zoom;
        Bgra32 body = Mix(Theme.Background, tint, 0.08f).WithAlpha(244);
        EditorDraw.Shadow(painter, Theme, box);

        // Selection and problems ring the node: a rounded band just outside it, drawn under the body.
        Bgra32 band = selected ? Theme.Accent
            : node.Problem is not null ? (node.IsError ? Theme.Error : Theme.Warning)
            : Theme.Border.WithAlpha(160);
        float grow = selected || node.Problem is not null ? MathF.Max(1.5f, 2f * Zoom) : 1f;
        EditorDraw.Fill(painter, Theme, new UiRect(box.X - grow, box.Y - grow, box.Width + (grow * 2f), box.Height + (grow * 2f)), band, radius + grow);
        EditorDraw.Fill(painter, Theme, box, body, radius);
        DrawTitleBar(painter, node, box, tint, body, radius);
        if (node.Collapsed)
        {
            return;
        }

        foreach ((IReadOnlyList<CanvasPin> pins, bool output) in new[] { (node.Inputs, false), (node.Outputs, true) })
        {
            foreach (CanvasPin pin in pins)
            {
                DrawPin(painter, node, pin, output, body);
            }
        }

        DrawDetails(painter, node, box);
    }

    // The settings under the pins: a hairline, then each as its name, muted, and its value, cut to fit with the rest on hover.
    private void DrawDetails(UiPainter painter, CanvasNode node, UiRect box)
    {
        if (node.Details.Count == 0 || Zoom < PinLabelZoom * Theme.Scale)
        {
            return;
        }

        float top = box.Bottom - ((node.Details.Count * DetailRowHeight) + 4f) * Zoom;
        painter.FillRect(new UiRect(box.X + (8f * Zoom), top - (2f * Zoom), box.Width - (16f * Zoom), 1f), Theme.Border.WithAlpha(150));
        UiFont labelFont = Theme.Fonts.Font(EditorFontFace.Interface, 10.5f * Zoom);
        for (int i = 0; i < node.Details.Count; i++)
        {
            CanvasDetail detail = node.Details[i];
            UiRect row = new(box.X + (10f * Zoom), top + (i * DetailRowHeight * Zoom), box.Width - (20f * Zoom), DetailRowHeight * Zoom);
            float labelWidth = MathF.Min(labelFont.MeasureWidth(detail.Label, 10.5f * Zoom), row.Width * 0.42f);
            Label(painter, detail.Label, row with { Width = labelWidth }, Theme.TextMuted, strong: false, size: 10.5f);
            float valueX = row.X + labelWidth + (8f * Zoom);
            Label(painter, detail.Value, UiRect.FromEdges(valueX, row.Y, row.Right, row.Bottom), Theme.Text, strong: false, size: 11f);
        }
    }

    // The title bar: the node colour spilling from the left into the body, a gloss along its top, then title and subtitle.
    private void DrawTitleBar(UiPainter painter, CanvasNode node, UiRect box, Bgra32 tint, Bgra32 body, float radius)
    {
        UiRect header = box with { Height = HeaderOf(node) * Zoom };

        // A see-through header (a reference's) is made solid first, the colour it shows over the panel: the bar is drawn in
        // overlapping pieces, and two see-through layers where they overlap would read as a brighter strip.
        if (tint.A < 255)
        {
            tint = Mix(Theme.Background.WithAlpha(255), tint.WithAlpha(255), tint.A / 255f);
        }

        Bgra32 faded = Mix(tint, body.WithAlpha(255), 0.82f);

        // One bar, all of it flat: one gradient from the node colour to the faded colour, then a rounded cap of each colour
        // over its ends. The caps are flat polygons rather than the theme's rounded surface, whose shading showed as a lighter
        // strip; drawn over the gradient and reaching two pixels into it, their soft inner edge blends a colour into itself,
        // where the gradient's own soft edge, drawn over a cap, showed as a dark line. A folded node is all bar, rounded below too.
        float r = MathF.Min(radius, header.Height * 0.5f);
        UiRect middle = UiRect.FromEdges(header.X + r, header.Y, header.Right - r, header.Bottom);
        float hold = Math.Clamp(0.3f - ((r + 2f) / MathF.Max(1f, middle.Width)), 0f, 0.9f);
        painter.FillGradient(middle, [new UiGradientStop(0f, tint), new UiGradientStop(hold, tint), new UiGradientStop(1f, faded)], UiGradientDirection.Horizontal);
        Cap(painter, header, r, left: true, roundBelow: node.Collapsed, tint);
        Cap(painter, header, r, left: false, roundBelow: node.Collapsed, faded);

        // A dark line where the bar meets the body.
        if (!node.Collapsed)
        {
            painter.FillRect(new UiRect(header.X, header.Bottom - 1f, header.Width, 1f), Bgra32.Black.WithAlpha(110));
        }

        float left = header.X + (10f * Zoom);
        float right = header.Right - ((node.Problem is null ? 10f : 26f) * Zoom);
        float titleTop = header.Y + (4f * Zoom);
        Label(painter, node.Title, UiRect.FromEdges(left, titleTop, right, titleTop + (20f * Zoom)), node.IsEvent ? Theme.OnAccent : Bgra32.White.WithAlpha(240), strong: true, size: 13f);
        if (node.Subtitle is { } subtitle && Zoom >= PinLabelZoom * Theme.Scale)
        {
            float subTop = titleTop + (18f * Zoom);
            Label(painter, subtitle, UiRect.FromEdges(left, subTop, right, subTop + (SubtitleHeight * Zoom)), Bgra32.White.WithAlpha(node.IsEvent ? (byte)200 : (byte)150), strong: false, size: 10.5f);
        }

        if (node.Problem is not null)
        {
            Bgra32 badge = node.IsError ? Theme.Error : Theme.Warning;
            UiVec2 at = new(header.Right - (14f * Zoom), header.Y + (14f * Zoom));
            painter.FillDot(at, 7f * Zoom, badge);
            Label(painter, "!", new UiRect(at.X - (2.5f * Zoom), at.Y - (8f * Zoom), 8f * Zoom, 16f * Zoom), Bgra32.Black, strong: true, size: 11f);
        }
    }

    // One end of a title bar: a column the corner radius wide, its outer corners rounded, reaching two pixels inward.
    private static void Cap(UiPainter painter, UiRect header, float r, bool left, bool roundBelow, Bgra32 colour)
    {
        const int Steps = 8;
        float edge = left ? header.X : header.Right;
        float inner = left ? header.X + r + 2f : header.Right - r - 2f;
        float centreX = left ? header.X + r : header.Right - r;
        float sign = left ? -1f : 1f;
        Span<UiVec2> points = stackalloc UiVec2[(Steps * 2) + 6];
        int count = 0;
        points[count++] = new UiVec2(inner, header.Y);

        // Round the top corner: from the top edge out to the side.
        for (int i = 0; i <= Steps; i++)
        {
            float a = MathF.PI * 0.5f * i / Steps;
            points[count++] = new UiVec2(centreX + (sign * r * MathF.Sin(a)), header.Y + r - (r * MathF.Cos(a)));
        }

        if (roundBelow)
        {
            for (int i = 0; i <= Steps; i++)
            {
                float a = MathF.PI * 0.5f * i / Steps;
                points[count++] = new UiVec2(centreX + (sign * r * MathF.Cos(a)), header.Bottom - r + (r * MathF.Sin(a)));
            }
        }
        else
        {
            points[count++] = new UiVec2(edge, header.Bottom);
        }

        points[count++] = new UiVec2(inner, header.Bottom);
        painter.FillPolygon(points[..count], colour);
    }

    private void DrawPin(UiPainter painter, CanvasNode node, CanvasPin pin, bool output, Bgra32 body)
    {
        UiVec2 at = ToScreen(PinAt(node, pin.Id, output));
        float size = 5f * Zoom;
        bool hovered = _hoverPin is { } hover && hover.Node == node.Id && hover.Pin == pin.Id && hover.IsOutput == output;
        if (hovered)
        {
            painter.FillDot(at, size * 2f, pin.Colour.WithAlpha(70));
        }

        bool wired = pin.IsConnected || (output && _wiredOutputs.Contains((node.Id, pin.Id)));
        if (pin.IsExec)
        {
            UiVec2 tip = new(at.X + size, at.Y);
            UiVec2 top = new(at.X - size, at.Y - size);
            UiVec2 bottom = new(at.X - size, at.Y + size);
            if (wired)
            {
                painter.FillTriangle(top, tip, bottom, Theme.Text);
            }
            else
            {
                painter.StrokePolygon([top, tip, bottom], MathF.Max(1f, 1.5f * Zoom), Theme.Text);
            }
        }
        else
        {
            // Wired: a filled dot. Free: a ring in the type colour.
            painter.FillDot(at, size, pin.Colour);
            if (!wired)
            {
                painter.FillDot(at, size - MathF.Max(1.2f, 1.6f * Zoom), body);
            }
        }

        if (Zoom < PinLabelZoom * Theme.Scale)
        {
            return;
        }

        float half = (NodeWidth * 0.5f) - PinInset - 8f;
        UiRect row = new(0f, at.Y - (RowHeight * 0.5f * Zoom), half * Zoom, RowHeight * Zoom);
        Bgra32 ink = Theme.Text.WithAlpha(hovered ? (byte)255 : (byte)215);
        if (output)
        {
            Label(painter, pin.Label, row with { X = at.X - ((half + 10f) * Zoom) }, ink, strong: false, size: 11.5f, alignRight: true);
            return;
        }

        if (pin.Label.Length > 0)
        {
            Label(painter, pin.Label, row with { X = at.X + (10f * Zoom) }, ink, strong: false, size: 11.5f);
        }

        if (ValueField(node, pin) is not { } field)
        {
            return;
        }

        // A true-or-false value is a tick box, flipped by a click, as node editors draw a boolean pin's default.
        if (pin.IsToggle)
        {
            EditorDraw.Frame(painter, Theme, field, pin.IsOn ? Theme.Accent : Theme.Input, pin.IsOn ? Theme.Accent : Theme.Border, 3f * Zoom);
            if (pin.IsOn)
            {
                painter.StrokeLine(new UiVec2(field.X + (3.5f * Zoom), field.Y + (8.5f * Zoom)), new UiVec2(field.X + (6.5f * Zoom), field.Y + (11.5f * Zoom)), MathF.Max(1.5f, 2f * Zoom), Theme.OnAccent);
                painter.StrokeLine(new UiVec2(field.X + (6.5f * Zoom), field.Y + (11.5f * Zoom)), new UiVec2(field.X + (12.5f * Zoom), field.Y + (4.5f * Zoom)), MathF.Max(1.5f, 2f * Zoom), Theme.OnAccent);
            }

            return;
        }

        // An inline value, as node editors show a pin default: a small field by the pin, not a node of its own; a click edits it.
        bool hot = Root?.PointerPosition is { } pointer && field.Contains(pointer);
        EditorDraw.Frame(painter, Theme, field, Theme.Input, hot ? Theme.Accent : Theme.Border, 3f * Zoom);
        Label(painter, pin.Value ?? string.Empty, field with { X = field.X + (7f * Zoom), Width = field.Width - (10f * Zoom) }, Theme.Text, strong: false, size: 11f);
    }

    // The source's nodes, the dragged ones moved by the drag so far.
    private IReadOnlyList<CanvasNode> Dragged() => _dragDelta.X == 0f && _dragDelta.Y == 0f
        ? Source.Nodes
        : [.. Source.Nodes.Select(n => _selected.Contains(n.Id) ? n with { Position = new UiVec2(n.Position.X + _dragDelta.X, n.Position.Y + _dragDelta.Y) } : n)];

    /// <summary>Where an input's inline value or tick box is, on screen; drawn there and clicked there.</summary>
    /// <param name="node">The node.</param>
    /// <param name="pin">The input.</param>
    /// <returns>The box, or <see langword="null"/> when the input shows no value (wired, folded, or too far out to read).</returns>
    internal UiRect? ValueField(CanvasNode node, CanvasPin pin)
    {
        if (node.Collapsed || pin.IsConnected || pin.IsExec || Zoom < PinLabelZoom * Theme.Scale || (!pin.IsToggle && pin.Value is not { Length: > 0 }))
        {
            return null;
        }

        UiVec2 at = ToScreen(PinAt(node, pin.Id, output: false));
        float x = at.X + (10f * Zoom);
        if (pin.Label.Length > 0)
        {
            x += Theme.Fonts.Font(EditorFontFace.Interface, 11.5f * Zoom).MeasureWidth(pin.Label, 11.5f * Zoom) + (6f * Zoom);
        }

        if (pin.IsToggle)
        {
            return new UiRect(x, at.Y - (8f * Zoom), 16f * Zoom, 16f * Zoom);
        }

        UiFont font = Theme.Fonts.Font(EditorFontFace.Interface, 11f * Zoom);
        float room = ToScreen(new UiVec2(node.Position.X + NodeWidth - 12f, 0f)).X - x;
        float width = MathF.Min(font.MeasureWidth(pin.Value!, 11f * Zoom) + (14f * Zoom), room);
        return width > 16f * Zoom ? new UiRect(x, at.Y - (9f * Zoom), width, 18f * Zoom) : null;
    }

    private static Bgra32 Mix(Bgra32 a, Bgra32 b, float t) => Bgra32.FromRgba(
        (byte)(a.R + ((b.R - a.R) * t)),
        (byte)(a.G + ((b.G - a.G) * t)),
        (byte)(a.B + ((b.B - a.B) * t)),
        (byte)(a.A + ((b.A - a.A) * t)));

    private void DrawGestures(UiPainter painter, Dictionary<string, CanvasNode> byId)
    {
        if (Root?.PointerPosition is not { } pointer)
        {
            return;
        }

        if (_gesture == Gesture.Wire && _wireFrom is { } from && byId.TryGetValue(from.Node, out CanvasNode? node))
        {
            UiVec2 pin = ToScreen(PinAt(node, from.Pin, from.IsOutput));
            Bgra32 colour = (from.IsOutput ? node.Outputs : node.Inputs).FirstOrDefault(p => p.Id == from.Pin)?.Colour ?? Theme.Text;
            if (from.IsOutput)
            {
                DrawWire(painter, pin, pointer, colour, exec: false, lit: true);
            }
            else
            {
                DrawWire(painter, pointer, pin, colour, exec: false, lit: true);
            }
        }
        else if (_gesture == Gesture.Marquee)
        {
            UiRect box = new(MathF.Min(_pressAt.X, pointer.X), MathF.Min(_pressAt.Y, pointer.Y), MathF.Abs(pointer.X - _pressAt.X), MathF.Abs(pointer.Y - _pressAt.Y));
            painter.FillRect(box, Theme.Accent.WithAlpha(30));
            painter.StrokeRect(box, 1f, Theme.Accent);
        }
    }

    private void DrawMinimap(UiPainter painter)
    {
        if (MinimapRect() is not { } map || WorldBounds() is not { } world)
        {
            return;
        }

        EditorDraw.Fill(painter, Theme, map, Theme.Popup.WithAlpha(220), Theme.Px(4f));
        painter.StrokeRect(map, 1f, Theme.Border);
        float scale = MathF.Min(map.Width / world.Width, map.Height / world.Height);
        UiVec2 Map(UiVec2 p) => new(map.X + ((p.X - world.X) * scale), map.Y + ((p.Y - world.Y) * scale));
        foreach (CanvasNode node in Source.Nodes)
        {
            UiRect size = SizeOf(node);
            UiVec2 corner = Map(new UiVec2(size.X, size.Y));
            painter.FillRect(new UiRect(corner.X, corner.Y, MathF.Max(2f, size.Width * scale), MathF.Max(2f, size.Height * scale)), _selected.Contains(node.Id) ? Theme.Accent : node.Header);
        }

        UiVec2 a = Map(ToCanvas(new UiVec2(Bounds.X, Bounds.Y)));
        UiVec2 b = Map(ToCanvas(new UiVec2(Bounds.Right, Bounds.Bottom)));
        painter.StrokeRect(new UiRect(a.X, a.Y, b.X - a.X, b.Y - a.Y), 1.5f, Theme.Text.WithAlpha(180));
    }

    private UiRect? MinimapRect()
    {
        float width = Theme.Px(180f);
        float height = Theme.Px(120f);
        if (Bounds.Width < width * 2.5f || Bounds.Height < height * 2f)
        {
            return null;
        }

        // Nothing to find when the whole graph is already in view.
        if (WorldBounds() is { } world)
        {
            UiVec2 a = ToCanvas(new UiVec2(Bounds.X, Bounds.Y));
            UiVec2 b = ToCanvas(new UiVec2(Bounds.Right, Bounds.Bottom));
            if (world.X >= a.X && world.Y >= a.Y && world.Right <= b.X && world.Bottom <= b.Y)
            {
                return null;
            }
        }

        return new UiRect(Bounds.Right - width - Theme.Px(10f), Bounds.Bottom - height - Theme.Px(10f), width, height);
    }

    private UiRect? WorldBounds()
    {
        if (Source.Nodes.Count == 0)
        {
            return null;
        }

        UiRect[] boxes = [.. Source.Nodes.Select(SizeOf)];
        float left = boxes.Min(static b => b.X);
        float top = boxes.Min(static b => b.Y);
        return new UiRect(left, top, MathF.Max(1f, boxes.Max(static b => b.Right) - left), MathF.Max(1f, boxes.Max(static b => b.Bottom) - top));
    }

    // A click on the minimap centres the view there.
    private void JumpTo(UiRect map, UiVec2 at)
    {
        if (WorldBounds() is not { } world)
        {
            return;
        }

        float scale = MathF.Min(map.Width / world.Width, map.Height / world.Height);
        UiVec2 canvas = new(world.X + ((at.X - map.X) / scale), world.Y + ((at.Y - map.Y) / scale));
        Scroll = new UiVec2(canvas.X - (Bounds.Width * 0.5f / Zoom), canvas.Y - (Bounds.Height * 0.5f / Zoom));
    }

    private UiRect ToScreen(UiRect canvas)
    {
        UiVec2 corner = ToScreen(new UiVec2(canvas.X, canvas.Y));
        return new UiRect(corner.X, corner.Y, canvas.Width * Zoom, canvas.Height * Zoom);
    }

    private void Label(UiPainter painter, string text, UiRect box, Bgra32 colour, bool strong, float size, bool alignRight = false)
    {
        float px = size * Zoom;
        if (px < 5f)
        {
            return;
        }

        UiFont font = Theme.Fonts.Font(strong ? EditorFontFace.InterfaceStrong : EditorFontFace.Interface, px);
        string shown = EditorDraw.Fit(font, text, px, box.Width);
        float width = font.MeasureWidth(shown, px);
        float x = alignRight ? box.Right - width : box.X;
        float y = box.Y + ((box.Height - font.LineHeight(px)) * 0.5f);
        painter.DrawText(font, shown, new UiVec2(MathF.Round(x), MathF.Round(y)), px, colour);
    }

    private static float Mod(float value, float step) => ((value % step) + step) % step;
}
