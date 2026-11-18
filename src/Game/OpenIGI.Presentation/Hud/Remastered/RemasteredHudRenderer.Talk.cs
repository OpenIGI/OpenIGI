using OpenIGI.Core.Graphics;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Kit.Hud;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Presentation.Hud.Remastered;

public sealed partial class RemasteredHudRenderer
{
    // The chat column's text size and where it stands: on the left, above the lower edge's readouts. Layout pixels.
    private const float ChatSize = 16f;
    private const float ChatLeft = 34f;
    private const float ChatBottom = 210f;

    // The connection warning's mark: amber, a caution rather than a failure.
    private static readonly Bgra32 WarningInk = Bgra32.FromRgb(232, 170, 60);

    // The connection warning's size.
    private const float MarkLabelSize = 14f;

    /// <inheritdoc/>
    /// <remarks>
    /// Who is talking sits above the lower readouts at the left, and the chat above them (<see cref="DrawChat"/>). A mark is a pin where it was put (<see cref="PingMark"/>), kept
    /// on the edge of the screen pointing the way when it is behind or beside the view.
    /// </remarks>
    public void DrawCoopTalk(
        SpriteBatch batch,
        IReadOnlyList<HudChatLine> chat,
        HudChatEntry? entry,
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
            DrawMark(painter, context, font, mark, eye, horizontalTangent, aspectRatio, screenWidth, screenHeight);
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

        DrawChat(painter, context, chat, entry, y, x);
    }

    // A ping where it was put, or waiting on the screen's edge pointing the way (PingMark).
    private static void DrawMark(
        UiPainter painter,
        LayoutContext context,
        UiFont font,
        HudMark mark,
        in Camera3D eye,
        float horizontalTangent,
        float aspectRatio,
        int screenWidth,
        int screenHeight)
    {
        WorldCanvas canvas = new(mark.AnchorMetres, eye, horizontalTangent, aspectRatio, screenWidth, screenHeight, MetresPerUnit);
        bool inView = canvas.PinToScreen(context.Px(64f), out float x, out float y);
        UiVec2? pointing = inView ? null : new UiVec2(x - (screenWidth * 0.5f), y - (screenHeight * 0.5f));
        PingMark.Draw(painter, font, new UiVec2(x, y), mark.Name, canvas.DistanceMetres, mark.AgeSeconds, mark.Opacity, pointing);
    }
}
