using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Host.DevTools;

/// <summary>
/// Draws what the developer console puts over every window: the mark on the entity selected in the
/// world panel, and the console's own pointer.
/// </summary>
/// <remarks>
/// Both are drawn last and at full opacity, after the panels, which is all they have in common — and
/// the reason they sit together rather than inside any one panel.
/// </remarks>
internal static class SelectionMarkerRenderer
{
    /// <summary>
    /// Marks the entity selected in the world panel, wherever it is.
    /// </summary>
    /// <param name="painter">The painter to draw into.</param>
    /// <param name="viewport">The back buffer's size in pixels.</param>
    /// <param name="theme">The palette and faces, or <see langword="null"/> before they load.</param>
    /// <param name="inspector">The world panel, or <see langword="null"/> before it is built.</param>
    /// <param name="inspectorWindow">The world panel's window.</param>
    /// <remarks>
    /// <para>
    /// Drawn in the interface pass, over the scene, which is what makes it visible through a wall with
    /// no depth-test trickery — and the reason this is a screen-space bracket rather than a
    /// world-space box.
    /// </para>
    /// <para>
    /// The bracket shrinks with distance but never below a size that can be seen, so something a
    /// kilometre away is still a mark rather than a pixel. When it leaves the viewport the mark pins
    /// to the nearest edge and becomes an arrow pointing at it, so a selection is never simply lost
    /// the moment the reader turns around.
    /// </para>
    /// </remarks>
    public static void DrawSelectionMarker(
        UiPainter painter,
        UiVec2 viewport,
        DevConsoleTheme? theme,
        InspectorBody? inspector,
        UiWindow? inspectorWindow)
    {
        if (theme is null ||
            inspector is null ||
            inspectorWindow is not { IsOpen: true } ||
            inspector.Selected is not { } entity ||
            inspector.World is not { } world ||
            !world.TryProject(entity.Id, out ScreenMarker marker))
        {
            return;
        }

        float margin = theme.Gutter * 2f;
        bool onScreen = marker.IsInFront &&
            marker.X >= margin && marker.X <= viewport.X - margin &&
            marker.Y >= margin && marker.Y <= viewport.Y - margin;

        float x = Math.Clamp(marker.X, margin, viewport.X - margin);
        float y = Math.Clamp(marker.Y, margin, viewport.Y - margin);

        if (onScreen)
        {
            DrawBracket(painter, theme, x, y, marker.DistanceMetres, entity);
        }
        else
        {
            DrawEdgeArrow(painter, theme, x, y, viewport, marker, entity);
        }
    }

    /// <summary>
    /// Draws the pointer, last of all.
    /// </summary>
    /// <param name="painter">The painter to draw into.</param>
    /// <param name="theme">The palette and faces, or <see langword="null"/> before they load.</param>
    /// <param name="pointer">Where the pointer is, in viewport pixels.</param>
    /// <remarks>
    /// The console draws its own rather than letting the window system show the operating system's.
    /// A composited cursor sits on top of everything including the menu the reader is aiming at, it
    /// does not match the one the shell already draws, and turning it on while the shell was drawing
    /// its own put two arrows on screen at once.
    /// </remarks>
    public static void DrawPointer(UiPainter painter, DevConsoleTheme? theme, UiVec2 pointer)
    {
        if (theme is null)
        {
            return;
        }

        var tip = new UiVec2(pointer.X, pointer.Y);
        float size = Math.Clamp(painter.Context.Viewport.Y / 42f, 18f, 40f);

        DrawArrow(painter, tip, size + 3f, Bgra32.FromRgba(0, 0, 0, 200));
        DrawArrow(painter, tip, size, Bgra32.White);
    }

    /// <summary>Draws the corner bracket that sits on a visible selection.</summary>
    /// <param name="painter">The painter to draw into.</param>
    /// <param name="theme">The palette and faces.</param>
    /// <param name="x">Where it sits horizontally.</param>
    /// <param name="y">Where it sits vertically.</param>
    /// <param name="distance">How far away the entity is.</param>
    /// <param name="entity">What is selected.</param>
    /// <remarks>
    /// Corners rather than a closed rectangle: four short strokes read as a reticle placed over
    /// something, where a full box reads as a thing in its own right and hides what it frames. Each
    /// stroke is drawn twice, dark then bright, so it holds against a pale sky as well as a dark
    /// interior — the outline is what makes it legible over any scene at all.
    /// </remarks>
    private static void DrawBracket(UiPainter painter, DevConsoleTheme theme, float x, float y, double distance, in InspectableEntity entity)
    {
        float half = MathF.Round(Math.Clamp(900f / MathF.Max(4f, (float)distance), 14f, 90f));
        float arm = MathF.Round(half * 0.45f);
        float weight = MathF.Max(2f, MathF.Round(theme.Unit * 0.5f));

        x = MathF.Round(x);
        y = MathF.Round(y);

        for (int pass = 0; pass < 2; pass++)
        {
            bool shadow = pass == 0;
            float grow = shadow ? 1f : 0f;
            Bgra32 colour = shadow ? Bgra32.FromRgba(0, 0, 0, 190) : theme.Ui.Accent;
            float thickness = weight + (shadow ? 2f : 0f);

            float left = x - half - grow;
            float right = x + half + grow;
            float top = y - half - grow;
            float bottom = y + half + grow;

            painter.FillRect(new UiRect(left, top, arm, thickness), colour);
            painter.FillRect(new UiRect(left, top, thickness, arm), colour);

            painter.FillRect(new UiRect(right - arm, top, arm, thickness), colour);
            painter.FillRect(new UiRect(right - thickness, top, thickness, arm), colour);

            painter.FillRect(new UiRect(left, bottom - thickness, arm, thickness), colour);
            painter.FillRect(new UiRect(left, bottom - arm, thickness, arm), colour);

            painter.FillRect(new UiRect(right - arm, bottom - thickness, arm, thickness), colour);
            painter.FillRect(new UiRect(right - thickness, bottom - arm, thickness, arm), colour);
        }

        DrawMarkerLabel(painter, theme, x, y - half - (theme.Unit * 2f), entity.Name, distance);
    }

    /// <summary>Draws the pinned arrow standing in for a selection off the edge of the screen.</summary>
    /// <param name="painter">The painter to draw into.</param>
    /// <param name="theme">The palette and faces.</param>
    /// <param name="x">Where the mark was clamped to horizontally.</param>
    /// <param name="y">Where the mark was clamped to vertically.</param>
    /// <param name="viewport">The back buffer's size in pixels.</param>
    /// <param name="marker">Where the entity really projected to.</param>
    /// <param name="entity">What is selected.</param>
    private static void DrawEdgeArrow(
        UiPainter painter,
        DevConsoleTheme theme,
        float x,
        float y,
        UiVec2 viewport,
        in ScreenMarker marker,
        in InspectableEntity entity)
    {
        float size = MathF.Max(10f, theme.Unit * 3f);

        // Point away from the middle, along the direction the entity actually lies in.
        float dx = marker.X - (viewport.X * 0.5f);
        float dy = marker.Y - (viewport.Y * 0.5f);
        float length = MathF.Max(0.0001f, MathF.Sqrt((dx * dx) + (dy * dy)));
        dx /= length;
        dy /= length;

        var tip = new UiVec2(MathF.Round(x + (dx * size)), MathF.Round(y + (dy * size)));
        var left = new UiVec2(MathF.Round(x - (dy * size * 0.6f)), MathF.Round(y + (dx * size * 0.6f)));
        var right = new UiVec2(MathF.Round(x + (dy * size * 0.6f)), MathF.Round(y - (dx * size * 0.6f)));

        painter.FillTriangle(tip, left, right, theme.Ui.Accent);
        DrawMarkerLabel(painter, theme, x, y - (size * 2.2f), entity.Name, marker.DistanceMetres);
    }

    private static void DrawMarkerLabel(UiPainter painter, DevConsoleTheme theme, float centreX, float top, string name, double distance)
    {
        string text = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{name}  {distance:0.0} m");

        float width = theme.SmallFont.MeasureWidth(text, theme.SmallSize);
        float height = theme.SmallFont.LineHeight(theme.SmallSize);
        var box = new UiRect(
            MathF.Round(centreX - (width * 0.5f) - theme.Unit),
            MathF.Round(top - height - theme.Unit),
            MathF.Round(width + (theme.Unit * 2f)),
            MathF.Round(height + (theme.Unit * 1.5f)));

        painter.FillRect(box, Bgra32.FromRgba(0, 0, 0, 175));
        painter.DrawText(
            theme.SmallFont,
            text,
            new UiVec2(MathF.Round(box.X + theme.Unit), MathF.Round(box.Y + (theme.Unit * 0.75f))),
            theme.SmallSize,
            theme.Ui.Accent);
    }

    private static void DrawArrow(UiPainter painter, UiVec2 tip, float size, Bgra32 color)
    {
        var down = new UiVec2(tip.X, tip.Y + size);
        var elbow = new UiVec2(tip.X + (size * 0.36f), tip.Y + (size * 0.72f));
        var barb = new UiVec2(tip.X + (size * 0.72f), tip.Y + (size * 0.72f));
        painter.FillTriangle(tip, down, elbow, color);
        painter.FillTriangle(tip, elbow, barb, color);
    }
}
