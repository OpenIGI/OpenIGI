using OpenIGI.Core.Graphics;
using OpenIGI.Game.Ai;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Presentation.Igi1.World;

/// <summary>Other players' names over their heads.</summary>
internal sealed partial class RemasteredHudRenderer
{
    // The name's size up close, in layout pixels; a tag across the yard is drawn at NameTagFarScale of it.
    private const float NameTagSize = 14f;
    private const float NameTagFarScale = 0.85f;

    // Out to here a tag keeps its full size; by NameTagReachMetres it has shrunk to the far scale.
    private const double NameTagNearMetres = 8.0;

    // How far one is read at all, and the stretch before that over which it fades.
    private const double NameTagReachMetres = 140.0;
    private const double NameTagFadeMetres = 30.0;

    // Beyond here a tag says how far as well.
    private const double NameTagDistanceMetres = 25.0;

    /// <inheritdoc/>
    /// <remarks>
    /// Each name is set flat on the screen over the player's head, as a modern shooter marks its allies, and kept spare: the
    /// name in small white type with a soft shadow under it to read against sky and wall alike, a small marker in the accent
    /// pointing down at the player, and -- far off -- how far, quieter beside it. No plate behind it: across a compound of
    /// allies, plates were a wall of boxes. It shrinks a little and fades towards <see cref="NameTagReachMetres"/>, so the
    /// nearest reads first. Not the original's: it had no other players to name.
    /// </remarks>
    public void DrawNameTags(
        SpriteBatch batch,
        IReadOnlyList<WorldNameTag> tags,
        in Camera3D eye,
        float horizontalTangent,
        float aspectRatio,
        int screenWidth,
        int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(tags);
        if ((_body ?? _display) is not { } font)
        {
            return;
        }

        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        var painter = UiPainter.Create(batch, context);

        foreach (WorldNameTag tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag.Name))
            {
                continue;
            }

            WorldCanvas canvas = new(tag.AnchorMetres, eye, horizontalTangent, aspectRatio, screenWidth, screenHeight, MetresPerUnit);
            double distance = canvas.DistanceMetres;
            if (distance > NameTagReachMetres || !canvas.TryProject(0f, 0f, out float headX, out float headY))
            {
                continue;
            }

            double far = Math.Clamp((distance - NameTagNearMetres) / (NameTagReachMetres - NameTagNearMetres), 0.0, 1.0);
            float scale = 1f - ((1f - NameTagFarScale) * (float)far);
            float fade = (float)Math.Clamp((NameTagReachMetres - distance) / NameTagFadeMetres, 0.0, 1.0);

            float size = context.Px(NameTagSize * scale);
            float marker = context.Px(5f * scale);
            float shadowOffset = MathF.Max(1f, context.Px(1f));
            Bgra32 shade = Bgra32.FromRgba(0, 0, 0, (byte)(170 * fade));

            // The marker: a small triangle pointing down at the head, in the accent.
            float tipY = headY - context.Px(4f * scale);
            painter.FillTriangle(
                new UiVec2(headX - marker, tipY - marker),
                new UiVec2(headX + marker, tipY - marker),
                new UiVec2(headX, tipY),
                Accent.WithAlpha((byte)(235 * fade)));

            // The name over it, and far off how far, quieter; each a hair of shadow under it.
            string? howFar = distance > NameTagDistanceMetres ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{distance:0} m") : null;
            float farSize = size * 0.82f;
            float gap = size * 0.45f;
            float nameWidth = font.MeasureWidth(tag.Name, size);
            float farWidth = howFar is null ? 0f : font.MeasureWidth(howFar, farSize);
            float total = nameWidth + (howFar is null ? 0f : gap + farWidth);
            float lineHeight = size * 1.2f;
            var line = new UiRect(headX - (total * 0.5f), tipY - marker - context.Px(3f * scale) - lineHeight, total, lineHeight);
            if (line.Right < 0f || line.X > screenWidth || line.Bottom < 0f || line.Y > screenHeight)
            {
                continue;
            }

            float baseline = UiRowStyle.CapsCenterY(font, size, line);
            painter.DrawLabel(font, tag.Name, new UiVec2(line.X + shadowOffset, baseline + shadowOffset), size, shade, 0.02f);
            painter.DrawLabel(font, tag.Name, new UiVec2(line.X, baseline), size, Text.WithAlpha((byte)(245 * fade)), 0.02f);
            if (howFar is not null)
            {
                float farX = line.X + nameWidth + gap;
                float farY = UiRowStyle.CapsCenterY(font, farSize, line);
                painter.DrawLabel(font, howFar, new UiVec2(farX + shadowOffset, farY + shadowOffset), farSize, shade, 0.02f);
                painter.DrawLabel(font, howFar, new UiVec2(farX, farY), farSize, UiPalette.TextMuted.WithAlpha((byte)(230 * fade)), 0.02f);
            }
        }
    }
}
