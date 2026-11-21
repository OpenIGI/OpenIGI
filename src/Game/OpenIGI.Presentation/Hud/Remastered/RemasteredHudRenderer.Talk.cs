using OpenIGI.Core.Graphics;
using OpenIGI.Presentation.Hud.Remastered.Widgets;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Kit;

namespace OpenIGI.Presentation.Hud.Remastered;

public sealed partial class RemasteredHudRenderer
{
    // Where the chat column stands: on the left, above the lower edge's readouts. Layout pixels.
    private const float ChatLeft = 34f;
    private const float ChatBottom = 210f;

    // A ping's pin: its head, and how far its point stands under it, in layout pixels; how long it settles and its ripples run.
    private const float PinHead = 12f;
    private const float PinDrop = 18f;
    private const float PinSettleSeconds = 0.22f;
    private const float PinRippleSeconds = 0.9f;

    /// <inheritdoc/>
    /// <remarks>
    /// Who is talking sits above the lower readouts at the left, and the chat above them (<see cref="DrawChat"/>). A mark is a
    /// pin in the title's accent where it was put, kept on the edge of the screen pointing the way when it is behind or beside
    /// the view.
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
        if (Overlay(batch, screenWidth, screenHeight) is not { } o)
        {
            return;
        }

        foreach (HudMark mark in marks)
        {
            DrawMark(o, mark, eye, horizontalTangent, aspectRatio, screenWidth, screenHeight);
        }

        // A word about the connection, top and centre, small: the game goes on, but the player knows why it stutters.
        float small = o.Px(HudOverlay.Small);
        if (warning is { Length: > 0 })
        {
            string said = o.Text.Capitals(warning);
            float width = o.Text.MeasureAt(said, small) + o.Px(30f);
            var plate = new UiRect(o.Safe.X + ((o.Safe.Width - width) * 0.5f), o.Safe.Y + o.Px(18f), width, o.Text.LineHeightAt(small) + o.Px(10f));
            o.Plate(plate, Bgra32.FromRgba(0, 0, 0, 190));
            o.Brackets(plate, HudOverlay.Fade(o.Ink.Accent, 0.9f), 4f);
            o.Diamond(new UiVec2(plate.X + o.Px(10f), plate.Y + (plate.Height * 0.5f)), o.Px(4f), o.Ink.Accent);
            o.Text.DrawAt(said, plate.X + o.Px(20f), plate.Y + o.Px(5f), small, o.Ink.Accent);
        }

        // Who is talking: a plate each above the chat, a microphone in the ally's ink and the name.
        float size = o.Px(HudOverlay.Body);
        float lineHeight = o.Text.LineHeightAt(size) + o.Px(10f);
        float x = o.Safe.X + o.Px(ChatLeft);
        float y = o.Safe.Bottom - o.Px(ChatBottom);
        foreach (string speaker in talking)
        {
            float icon = lineHeight * 0.6f;
            string name = o.Text.Prose(speaker);
            var plate = new UiRect(x, y - lineHeight, o.Px(10f) + icon + o.Px(8f) + o.Text.MeasureAt(name, size) + o.Px(12f), lineHeight);
            o.Plate(plate, Bgra32.FromRgba(0, 0, 0, 180));
            o.Brackets(plate, HudOverlay.Fade(o.Ink.Ally, 0.8f), 4f);
            KitIcons.Draw(o.Painter, KitIcon.Microphone, new UiVec2(plate.X + o.Px(10f) + (icon * 0.5f), plate.Y + (lineHeight * 0.5f)), icon, o.Ink.Ally);
            o.Text.DrawAt(name, plate.X + o.Px(18f) + icon, plate.Y + o.Px(5f), size, o.Ink.Plain);
            y -= lineHeight + o.Px(4f);
        }

        DrawChat(o, chat, entry, y, x);
    }

    // A ping where it was put, or waiting on the screen's edge pointing the way: a pin in the accent that drops in a touch large and
    // settles while two rings ripple out from the spot, who marked it and how far over it.
    private static void DrawMark(HudOverlay o, HudMark mark, in Camera3D eye, float horizontalTangent, float aspectRatio, int screenWidth, int screenHeight)
    {
        WorldCanvas canvas = new(mark.AnchorMetres, eye, horizontalTangent, aspectRatio, screenWidth, screenHeight, MetresPerUnit);
        bool inView = canvas.PinToScreen(o.Px(64f), out float x, out float y);
        float opacity = Math.Clamp(mark.Opacity, 0f, 1f);
        if (opacity <= 0f)
        {
            return;
        }

        var spot = new UiVec2(x, y);
        float age = mark.AgeSeconds;
        Bgra32 accent = HudOverlay.Fade(o.Ink.Accent, opacity);
        if (inView)
        {
            for (int ring = 0; ring < 2; ring++)
            {
                float t = (age - (ring * 0.18f)) / PinRippleSeconds;
                if (t is > 0f and < 1f)
                {
                    float eased = 1f - ((1f - t) * (1f - t));
                    float radius = o.Px(6f + (46f * eased));
                    float width = MathF.Max(1f, o.Px(2.5f * (1f - t)));
                    o.Painter.FillArc(spot, radius - width, radius, 0f, MathF.Tau, HudOverlay.Fade(o.Ink.Accent, 0.8f * (1f - t) * opacity));
                }
            }
        }

        // The pin: the theme's diamond for a head over a point on the spot; on the edge it is centred, with the arrow beside it.
        float settle = Math.Clamp(age / PinSettleSeconds, 0f, 1f);
        float scale = 1f + (0.35f * (1f - settle) * (1f - settle));
        float head = o.Px(PinHead * scale);
        float drop = o.Px(PinDrop * scale);
        var centre = inView ? new UiVec2(spot.X, spot.Y - drop) : spot;
        Bgra32 dark = Bgra32.FromRgba(0, 0, 0, (byte)(180 * opacity));
        float edge = MathF.Max(1.5f, o.Px(2f));
        o.Painter.FillTriangle(new UiVec2(centre.X - (head * 0.6f) - edge, centre.Y), new UiVec2(centre.X + (head * 0.6f) + edge, centre.Y), new UiVec2(centre.X, centre.Y + drop + edge), dark);
        o.Diamond(centre, head + edge, dark);
        o.Painter.FillTriangle(new UiVec2(centre.X - (head * 0.6f), centre.Y), new UiVec2(centre.X + (head * 0.6f), centre.Y), new UiVec2(centre.X, centre.Y + drop), accent);
        o.Diamond(centre, head, accent);
        o.Diamond(centre, head * 0.4f, dark);
        if (!inView)
        {
            o.EdgeArrow(centre, head + o.Px(6f), new UiVec2(x - (screenWidth * 0.5f), y - (screenHeight * 0.5f)), accent);
        }

        float size = o.Px(HudOverlay.Body);
        o.Caption(centre.X, centre.Y - head - o.Px(5f) - o.Text.LineHeightAt(size), mark.Name, HudOverlay.Distance(canvas.DistanceMetres), o.Ink.Accent, opacity);
    }
}
