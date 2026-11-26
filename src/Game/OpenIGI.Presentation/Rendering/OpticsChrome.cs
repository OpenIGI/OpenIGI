using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Rendering;

/// <summary>The vector frame every remastered optic draws over its picture: the eyepiece's rails, its corner ticks and its reticle.</summary>
/// <remarks>
/// No full-screen wash and no opaque frame: each optic's display transform owns the glass, so the interface stays crisp chrome
/// over it. The binoculars and the thermal sensor share it, each in its own colour.
/// </remarks>
public static class OpticsChrome
{
    /// <summary>Draws the eyepiece's frame: thin rails top and bottom and short L-brackets at its corners.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="context">The layout.</param>
    /// <param name="colour">The frame's colour.</param>
    /// <returns>The frame, inset from the safe area, which the optic lays its readouts against.</returns>
    public static UiRect DrawFrame(UiPainter painter, LayoutContext context, Bgra32 colour)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(context);

        float inset = context.Px(30f);
        UiRect frame = new(inset, inset, context.SafeRect.Width - (inset * 2f), context.SafeRect.Height - (inset * 2f));
        painter.FillRect(new UiRect(frame.X, frame.Y, frame.Width, context.Px(2f)), colour.WithAlpha(190));
        painter.FillRect(new UiRect(frame.X, frame.Bottom - context.Px(2f), frame.Width, context.Px(2f)), colour.WithAlpha(120));
        DrawCornerTicks(painter, context, frame, colour.WithAlpha(170));
        return frame;
    }

    /// <summary>Draws the gapped reticle: crosshair arms, a framing ring and a centre dot.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="context">The layout.</param>
    /// <param name="centreX">Its centre across.</param>
    /// <param name="centreY">Its centre down.</param>
    /// <param name="arms">The arms' and the ring's colour.</param>
    /// <param name="dot">The centre dot's colour.</param>
    public static void DrawReticle(UiPainter painter, LayoutContext context, float centreX, float centreY, Bgra32 arms, Bgra32 dot)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(context);

        float arm = context.Px(24f);
        float gap = context.Px(10f);
        float weight = MathF.Max(1f, context.Px(1.5f));
        painter.FillRect(new UiRect(centreX - gap - arm, centreY - (weight * 0.5f), arm, weight), arms);
        painter.FillRect(new UiRect(centreX + gap, centreY - (weight * 0.5f), arm, weight), arms);
        painter.FillRect(new UiRect(centreX - (weight * 0.5f), centreY - gap - arm, weight, arm), arms);
        painter.FillRect(new UiRect(centreX - (weight * 0.5f), centreY + gap, weight, arm), arms);
        UiVectors.StrokeRing(
            painter, new UiVec2(centreX, centreY), context.Px(3.4f), MathF.Max(1f, context.Px(1.2f)), arms.WithAlpha(150));
        UiVectors.FillDisc(painter, new UiVec2(centreX, centreY), MathF.Max(1f, context.Px(1.6f)), dot);
    }

    /// <summary>Draws short L-brackets at a frame's corners.</summary>
    private static void DrawCornerTicks(UiPainter painter, LayoutContext context, UiRect frame, Bgra32 colour)
    {
        float length = context.Px(28f);
        float weight = MathF.Max(1f, context.Px(2f));
        painter.FillRect(new UiRect(frame.X, frame.Y, length, weight), colour);
        painter.FillRect(new UiRect(frame.X, frame.Y, weight, length), colour);
        painter.FillRect(new UiRect(frame.Right - length, frame.Y, length, weight), colour);
        painter.FillRect(new UiRect(frame.Right - weight, frame.Y, weight, length), colour);
        painter.FillRect(new UiRect(frame.X, frame.Bottom - weight, length, weight), colour);
        painter.FillRect(new UiRect(frame.X, frame.Bottom - length, weight, length), colour);
        painter.FillRect(new UiRect(frame.Right - length, frame.Bottom - weight, length, weight), colour);
        painter.FillRect(new UiRect(frame.Right - weight, frame.Bottom - length, weight, length), colour);
    }
}
