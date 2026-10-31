using OpenIGI.Core.Graphics;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Presentation.Igi1.World;

internal sealed partial class RemasteredHudRenderer
{
    // The chat column's text size and where it stands: on the left, above the lower edge's readouts. Layout pixels.
    private const float ChatSize = 16f;
    private const float ChatLeft = 40f;
    private const float ChatBottom = 210f;

    // The connection warning's mark: amber, a caution rather than a failure.
    private static readonly Bgra32 WarningInk = Bgra32.FromRgb(232, 170, 60);

    // A mark's diamond, and its label's size.
    private const float MarkSize = 9f;
    private const float MarkLabelSize = 14f;

    /// <inheritdoc/>
    /// <remarks>
    /// The lines run up the left from above the lower readouts, each speaker's name in the accent, on the same dark backing
    /// the name tags have; the line being typed sits below them. A mark is a diamond where it was put, its marker and distance
    /// beside it, and -- unlike a name tag -- it is kept on the edge of the screen when it is behind or beside the view.
    /// </remarks>
    public void DrawCoopTalk(
        SpriteBatch batch,
        IReadOnlyList<HudChatLine> chat,
        string? entry,
        string? warning,
        IReadOnlyList<string> talking,
        IReadOnlyList<HudMark> marks,
        in Camera3D eye,
        float horizontalTangent,
        float aspectRatio,
        int screenWidth,
        int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(marks);
        if ((_body ?? _display) is not { } font)
        {
            return;
        }

        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        var painter = UiPainter.Create(batch, context);
        NineSlice rounded = _surfaces?.Rounded ?? default;

        foreach (HudMark mark in marks)
        {
            DrawMark(painter, context, font, rounded, mark, eye, horizontalTangent, aspectRatio, screenWidth, screenHeight);
        }

        // A word about the connection, top and centre, small: the game goes on, but the player knows why it stutters.
        if (warning is { Length: > 0 })
        {
            float warnSize = context.Px(MarkLabelSize);
            float warnHeight = warnSize * 1.8f;
            float warnWidth = font.MeasureWidth(warning, warnSize) + (warnSize * 1.6f);
            var chip = new UiRect(context.SafeRect.X + ((context.SafeRect.Width - warnWidth) * 0.5f), context.SafeRect.Y + context.Px(18f), warnWidth, warnHeight);
            painter.FillRoundedRect(chip, rounded, UiPalette.InkAt(0.82f).WithAlpha(210), warnHeight * 0.22f);
            painter.FillRect(new UiRect(chip.X, chip.Y + (warnHeight * 0.2f), MathF.Max(2f, warnSize * 0.16f), warnHeight * 0.6f), WarningInk);
            painter.DrawLabel(font, warning, new UiVec2(chip.X + (warnSize * 0.8f), UiRowStyle.CapsCenterY(font, warnSize, chip)), warnSize, Text, 0.02f);
        }

        float size = context.Px(ChatSize);
        float lineHeight = size * 1.7f;
        float x = context.SafeRect.X + context.Px(ChatLeft);
        float y = context.SafeRect.Bottom - context.Px(ChatBottom);
        // Who is talking: a chip each, a microphone in the accent and the name, above the chat.
        foreach (string speaker in talking)
        {
            float icon = lineHeight * 0.62f;
            float width = (size * 0.55f) + icon + (size * 0.45f) + font.MeasureWidth(speaker, size) + (size * 0.8f);
            var chip = new UiRect(x, y - lineHeight, width, lineHeight);
            painter.FillRoundedRect(chip, rounded, UiPalette.InkAt(0.82f).WithAlpha(210), lineHeight * 0.5f);
            KitIcons.Draw(painter, KitIcon.Microphone, new UiVec2(chip.X + (size * 0.55f) + (icon * 0.5f), chip.Y + (lineHeight * 0.5f)), icon, Accent);
            painter.DrawLabel(font, speaker, new UiVec2(chip.X + (size * 1f) + icon, UiRowStyle.CapsCenterY(font, size, chip)), size, Text, 0.02f);
            y -= lineHeight + context.Px(4f);
        }

        if (entry is not null)
        {
            string typed = $"{entry}_";
            float width = Math.Max(context.Px(320f), font.MeasureWidth(typed, size) + font.MeasureWidth("SAY  ", size) + (size * 1.6f));
            var box = new UiRect(x, y - lineHeight, width, lineHeight);
            painter.FillRoundedRect(box, rounded, UiPalette.InkAt(0.82f).WithAlpha(220), lineHeight * 0.22f);
            UiVec2 pen = painter.DrawLabel(font, "SAY", new UiVec2(box.X + (size * 0.8f), UiRowStyle.CapsCenterY(font, size, box)), size, Accent, 0.08f);
            painter.DrawLabel(font, typed, new UiVec2(pen.X + (size * 0.6f), UiRowStyle.CapsCenterY(font, size, box)), size, Text, 0.02f);
            y -= lineHeight + context.Px(6f);
        }

        for (int i = chat.Count - 1; i >= 0; i--)
        {
            HudChatLine line = chat[i];
            byte alpha = (byte)(255 * Math.Clamp(line.Opacity, 0f, 1f));
            float nameWidth = font.MeasureWidth(line.Name, size);
            float width = nameWidth + font.MeasureWidth(line.Text, size) + (size * 2.4f);
            var row = new UiRect(x, y - lineHeight, width, lineHeight);
            painter.FillRoundedRect(row, rounded, UiPalette.InkAt(0.82f).WithAlpha((byte)(170 * line.Opacity)), lineHeight * 0.22f);
            painter.DrawLabel(font, line.Name, new UiVec2(row.X + (size * 0.7f), UiRowStyle.CapsCenterY(font, size, row)), size, Accent.WithAlpha(alpha), 0.02f);
            painter.DrawLabel(font, line.Text, new UiVec2(row.X + (size * 1.7f) + nameWidth, UiRowStyle.CapsCenterY(font, size, row)), size, Text.WithAlpha(alpha), 0.02f);
            y -= lineHeight + context.Px(4f);
        }
    }

    private static void DrawMark(
        UiPainter painter,
        LayoutContext context,
        UiFont font,
        NineSlice rounded,
        HudMark mark,
        in Camera3D eye,
        float horizontalTangent,
        float aspectRatio,
        int screenWidth,
        int screenHeight)
    {
        WorldCanvas canvas = new(mark.AnchorMetres, eye, horizontalTangent, aspectRatio, screenWidth, screenHeight, MetresPerUnit);
        float edge = context.Px(48f);
        float x;
        float y;
        if (!canvas.TryProject(0f, 0f, out x, out y) || x < edge || x > screenWidth - edge || y < edge || y > screenHeight - edge)
        {
            // Out of the view: held on its edge, on the side it lies.
            Core.Numerics.Vector3f look = eye.Orientation.Row3;
            Core.Numerics.Vector3d to = mark.AnchorMetres - eye.Position;
            double right = (to.X * look.Y) - (to.Y * look.X);
            x = right > 0 ? screenWidth - edge : edge;
            y = Math.Clamp(canvas.TryProject(0f, 0f, out _, out float projectedY) ? projectedY : screenHeight * 0.5f, edge, screenHeight - edge);
        }

        byte alpha = (byte)(255 * Math.Clamp(mark.Opacity, 0f, 1f));
        float half = context.Px(MarkSize);

        // A diamond: four triangles about its centre, by two rotated squares' worth of rects at small size reading as one.
        for (float d = 0f; d <= half; d += 1f)
        {
            float w = half - d;
            painter.FillRect(new UiRect(x - w, y - d, w * 2f, 1f), Accent.WithAlpha(alpha));
            painter.FillRect(new UiRect(x - w, y + d, w * 2f, 1f), Accent.WithAlpha(alpha));
        }

        string label = FormattableString.Invariant($"{mark.Name}  {canvas.DistanceMetres:0} m");
        float size = context.Px(MarkLabelSize);
        float height = size * 1.6f;
        float width = font.MeasureWidth(label, size) + (size * 1.2f);
        float left = Math.Clamp(x - (width * 0.5f), 0f, screenWidth - width);
        var plate = new UiRect(left, y - half - height - context.Px(4f), width, height);
        painter.FillRoundedRect(plate, rounded, UiPalette.InkAt(0.82f).WithAlpha((byte)(190 * mark.Opacity)), height * 0.22f);
        painter.DrawLabel(font, label, new UiVec2(plate.X + (size * 0.6f), UiRowStyle.CapsCenterY(font, size, plate)), size, Text.WithAlpha(alpha), 0.02f);
    }
}
