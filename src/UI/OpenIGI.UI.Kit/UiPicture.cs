using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit;

/// <summary>
/// A picture made ready to draw: a mission's map, a player's avatar, the launcher's backdrop or a content pack's tile. The
/// host that made the texture keeps it; this is what the interface draws it by, with its size for its proportions.
/// </summary>
/// <param name="Image">The picture, as the interface draws it.</param>
/// <param name="Width">Its width in pixels.</param>
/// <param name="Height">Its height in pixels.</param>
public readonly record struct UiPicture(UiImage Image, int Width, int Height)
{
    /// <summary>Gets the picture's width over its height.</summary>
    public float Aspect => Width / (float)Math.Max(1, Height);

    /// <summary>Draws it to fill a box: scaled to cover it and cut to it about its middle, as a photo in a frame is.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="box">The box, in pixels.</param>
    /// <param name="opacity">How opaque, 0 to 1.</param>
    public void DrawCover(UiPainter painter, UiRect box, float opacity = 1f)
    {
        ArgumentNullException.ThrowIfNull(painter);
        float frame = box.Width / MathF.Max(1f, box.Height);
        UiRect source = Aspect > frame
            ? new UiRect((1f - (frame / Aspect)) * 0.5f, 0f, frame / Aspect, 1f)
            : new UiRect(0f, (1f - (Aspect / frame)) * 0.5f, 1f, Aspect / frame);
        painter.DrawSprite(Image, box, source, UiPalette.Fade(Bgra32.FromRgb(255, 255, 255), opacity));
    }
}
