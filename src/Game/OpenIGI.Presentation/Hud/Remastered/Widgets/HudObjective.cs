using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered.Widgets;

/// <summary>
/// The objective projected onto its place in the level, as a mark the player reads where the objective is: a diamond in a
/// ring, the objective's words beside it and how far it is; off the screen, held at its edge and pointing the way.
/// </summary>
/// <remarks>It fades as the view comes square onto it, so it does not sit over what the player is aiming at.</remarks>
public static class HudObjective
{
    /// <summary>Draws the mark.</summary>
    /// <param name="context">The frame's drawing.</param>
    /// <param name="model">The frame's model.</param>
    /// <param name="projector">The player's view.</param>
    public static void Draw(HudDrawContext context, HudModel model, HudProjector projector)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(model);
        if ((model.Show & HudParts.Status) == 0 || model.ObjectivePosition is not { } place)
        {
            return;
        }

        HudPalette palette = context.Palette;
        bool ahead = projector.TryProject(place, out UiVec2 at);
        float margin = context.Px(48f);
        float cx = context.Width * 0.5f, cy = context.Height * 0.5f;
        bool onScreen = ahead && at.X > margin && at.X < context.Width - margin && at.Y > margin && at.Y < context.Height - margin;
        if (!onScreen)
        {
            // Off the screen: held at its edge, along the way to it; behind, the way is mirrored.
            float dx = ahead ? at.X - cx : cx - at.X, dy = ahead ? at.Y - cy : cy - at.Y;
            if (!ahead && dy < 0f)
            {
                dy = -dy;
            }

            float scale = MathF.Min((cx - margin) / MathF.Max(1e-3f, MathF.Abs(dx)), (cy - margin) / MathF.Max(1e-3f, MathF.Abs(dy)));
            at = new UiVec2(cx + (dx * scale), cy + (dy * scale));
        }

        // Faded as it nears the middle of the screen, where the aim is.
        float fromAim = MathF.Sqrt(((at.X - cx) * (at.X - cx)) + ((at.Y - cy) * (at.Y - cy)));
        float opacity = Math.Clamp((fromAim - context.Px(40f)) / context.Px(120f), 0.25f, 1f) * context.Reactive.Appear;

        // The mark: a diamond in a ring that breathes.
        float size = context.Px(9f);
        float ring = size + context.Px(5f) + (context.Px(2f) * MathF.Sin(context.Seconds * 3f));
        Bgra32 colour = HudDrawContext.Fade(palette.Accent, opacity);
        context.Painter.FillDiamond(at, size, colour);
        Span<UiVec2> circle = stackalloc UiVec2[20];
        for (int i = 0; i < circle.Length; i++)
        {
            float a = i * MathF.Tau / circle.Length;
            circle[i] = new UiVec2(at.X + (MathF.Cos(a) * ring), at.Y + (MathF.Sin(a) * ring));
        }

        context.Painter.StrokePolygon(circle, MathF.Max(1f, context.Px(1.5f)), HudDrawContext.Fade(palette.Accent, 0.6f * opacity));

        if (!onScreen)
        {
            // A pointer outward, the way to it.
            float angle = MathF.Atan2(at.Y - cy, at.X - cx);
            UiVec2 tip = new(at.X + (MathF.Cos(angle) * (ring + context.Px(10f))), at.Y + (MathF.Sin(angle) * (ring + context.Px(10f))));
            UiVec2 left = new(at.X + (MathF.Cos(angle + 0.5f) * ring), at.Y + (MathF.Sin(angle + 0.5f) * ring));
            UiVec2 right = new(at.X + (MathF.Cos(angle - 0.5f) * ring), at.Y + (MathF.Sin(angle - 0.5f) * ring));
            context.Painter.FillTriangle(left, tip, right, colour);
        }

        // Its words and its distance, beside it, on the side towards the middle.
        string distance = string.Create(CultureInfo.InvariantCulture, $"{model.ObjectiveDistanceMetres:0} M");
        string text = model.Objective is { Length: > 0 } words ? words : string.Empty;
        float textSize = context.Px(14f);
        float width = MathF.Max(
            UiPainter.MeasureLabel(context.Fonts.Name, text, textSize, 0.03f),
            UiPainter.MeasureLabel(context.Fonts.Value, distance, textSize, 0.06f));
        bool leftward = at.X > cx;
        float x = leftward ? at.X - ring - context.Px(10f) - width : at.X + ring + context.Px(10f);
        float y = at.Y - textSize;
        if (text.Length > 0)
        {
            context.Painter.DrawLabel(context.Fonts.Name, text, new UiVec2(x, y - context.Px(2f)), textSize, HudDrawContext.Fade(palette.Highlight, opacity), 0.03f);
        }

        context.Painter.DrawLabel(context.Fonts.Value, distance, new UiVec2(x, y + textSize), textSize, HudDrawContext.Fade(palette.Accent, opacity), 0.06f);
    }
}
