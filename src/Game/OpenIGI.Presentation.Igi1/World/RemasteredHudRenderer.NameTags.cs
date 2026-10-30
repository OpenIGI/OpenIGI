using OpenIGI.Core.Graphics;
using OpenIGI.Game.Ai;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Text;

namespace OpenIGI.Presentation.Igi1.World;

/// <summary>Other players' names over their heads.</summary>
internal sealed partial class RemasteredHudRenderer
{
    // The name's size on its canvas, in canvas units.
    private const float NameTagSize = 26f;

    // Up to here a tag grows with distance, so a name across a compound stays readable; past it, it shrinks as the world does.
    private const double NameTagGrowMetres = 30.0;

    // How far one is read at all, and the stretch before that over which it fades.
    private const double NameTagReachMetres = 140.0;
    private const double NameTagFadeMetres = 30.0;

    /// <inheritdoc/>
    /// <remarks>
    /// Each name stands on a <see cref="WorldCanvas"/> over its player's head, turned to the eye, as the interaction marker
    /// does. A plane of fixed size would make a name across the yard unreadable, so the canvas is scaled with distance up to
    /// <see cref="NameTagGrowMetres"/> (near constant on the screen, as a modern shooter's tag is) and fades out towards
    /// <see cref="NameTagReachMetres"/>. Not the original's: it had no other players to name.
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
        if ((_display ?? _body) is not { } font)
        {
            return;
        }

        foreach (WorldNameTag tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag.Name))
            {
                continue;
            }

            WorldCanvas probe = new(tag.AnchorMetres, eye, horizontalTangent, aspectRatio, screenWidth, screenHeight, MetresPerUnit);
            double distance = probe.DistanceMetres;
            if (distance > NameTagReachMetres)
            {
                continue;
            }

            float growth = (float)Math.Clamp(distance / 6.0, 1.0, NameTagGrowMetres / 6.0);
            WorldCanvas canvas = new(
                tag.AnchorMetres, eye, horizontalTangent, aspectRatio, screenWidth, screenHeight, MetresPerUnit * growth);
            double fade = Math.Clamp((NameTagReachMetres - distance) / NameTagFadeMetres, 0.0, 1.0);
            byte alpha = (byte)(255 * fade);
            float width = font.MeasureWidth(tag.Name, NameTagSize);
            DrawCanvasWorldLabel(
                canvas,
                batch,
                font,
                tag.Name,
                -width * 0.5f,
                InkTop(font, tag.Name, NameTagSize, -InkHeight(font, tag.Name, NameTagSize)),
                NameTagSize,
                Text.WithAlpha(alpha));

            // A short rule under the name in the accent colour: whose side it is, as a squad marker is drawn.
            if (_surfaces is { } surfaces)
            {
                canvas.DrawSlice(batch, surfaces.Rounded, 0f, 7f, MathF.Min(width * 0.5f, 26f), 1.6f, Accent.WithAlpha((byte)(alpha * 0.9f)));
            }
        }
    }
}
