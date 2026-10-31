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
    private const float NameTagSize = 17f;
    private const float NameTagFarScale = 0.78f;

    // Out to here a tag keeps its full size; by NameTagReachMetres it has shrunk to the far scale.
    private const double NameTagNearMetres = 8.0;

    // How far one is read at all, and the stretch before that over which it fades.
    private const double NameTagReachMetres = 140.0;
    private const double NameTagFadeMetres = 30.0;

    // Beyond here a tag says how far as well.
    private const double NameTagDistanceMetres = 25.0;

    /// <inheritdoc/>
    /// <remarks>
    /// Each name is a plate on the screen, not a sign in the level: the point over the player's head is projected through
    /// the frame's eye and the name is set there flat, on a dark rounded backing with the accent down its side, as a modern
    /// shooter's tag is. A sign turned to the eye was thin, low-contrast against the sky, and shrank to nothing across the
    /// compound. It still shrinks a little and fades towards <see cref="NameTagReachMetres"/>, so the nearest reads first.
    /// Not the original's: it had no other players to name.
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
        NineSlice rounded = _surfaces?.Rounded ?? default;
        NineSlice shadow = _surfaces?.Shadow ?? default;

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
            float padX = size * 0.62f;
            float bar = MathF.Max(2f, size * 0.16f);
            // Far off, how far: a teammate across the compound is found by the number as much as the name.
            string label = distance > NameTagDistanceMetres ? FormattableString.Invariant($"{tag.Name}  {distance:0} m") : tag.Name;
            float width = font.MeasureWidth(label, size) + (padX * 2f) + bar;
            float height = size * 1.75f;
            var plate = new UiRect(headX - (width * 0.5f), headY - height - context.Px(6f * scale), width, height);
            if (plate.Right < 0f || plate.X > screenWidth || plate.Bottom < 0f || plate.Y > screenHeight)
            {
                continue;
            }

            float radius = height * 0.22f;
            painter.DrawShadow(plate, shadow, Bgra32.FromRgba(0, 0, 0, (byte)(110 * fade)), context.Px(6f), new UiVec2(0f, context.Px(1.5f)));
            painter.FillRoundedRect(plate, rounded, UiPalette.InkAt(0.82f).WithAlpha((byte)(205 * fade)), radius);
            painter.FillRect(new UiRect(plate.X, plate.Y + (height * 0.2f), bar, height * 0.6f), Accent.WithAlpha((byte)(255 * fade)));
            painter.DrawLabel(
                font,
                label,
                new UiVec2(plate.X + bar + padX, UiRowStyle.CapsCenterY(font, size, plate)),
                size,
                Text.WithAlpha((byte)(255 * fade)),
                0.02f);
        }
    }
}
