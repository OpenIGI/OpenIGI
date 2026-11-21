using OpenIGI.Core.Graphics;
using OpenIGI.Game.Ai;
using OpenIGI.Presentation.Hud.Remastered.Widgets;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered;

/// <summary>Other players' names over their heads.</summary>
public sealed partial class RemasteredHudRenderer
{
    /// <summary>How far a name tag is drawn at all, in metres.</summary>
    public const double NameTagReachMetres = 140.0;

    // Out to here a tag keeps its full size and opacity; it fades over the last stretch before the reach.
    private const double NameTagFadeMetres = 30.0;

    // Beyond here a tag says how far as well.
    private const double NameTagDistanceMetres = 25.0;

    /// <inheritdoc/>
    /// <remarks>
    /// Each name is projected over the player's head through the frame's eye and set flat there, kept spare: the name in the
    /// interface's face in the ally's ink, a small marker pointing down at the head, far off how far, and hurt, the title's meter in
    /// its health colour. No plate behind it: across a compound of allies, plates were a wall of boxes. Not the original's: it had
    /// no other players to name.
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
        if (tags.Count == 0 || Overlay(batch, screenWidth, screenHeight) is not { } o)
        {
            return;
        }

        foreach (WorldNameTag tag in tags)
        {
            WorldCanvas canvas = new(tag.AnchorMetres, eye, horizontalTangent, aspectRatio, screenWidth, screenHeight, MetresPerUnit);
            if (canvas.DistanceMetres <= NameTagReachMetres && canvas.TryProject(0f, 0f, out float headX, out float headY)
                && headX >= 0f && headX <= screenWidth && headY >= 0f && headY <= screenHeight && !string.IsNullOrWhiteSpace(tag.Name))
            {
                DrawNameTag(o, new UiVec2(headX, headY), tag.Name, canvas.DistanceMetres, tag.Health);
            }
        }
    }

    // One tag over a head: the marker, the name over it, and the meter between them while hurt.
    private static void DrawNameTag(HudOverlay o, UiVec2 head, string name, double distanceMetres, float? health)
    {
        float fade = (float)Math.Clamp((NameTagReachMetres - distanceMetres) / NameTagFadeMetres, 0.0, 1.0);
        float marker = o.Px(6f);
        float tipY = head.Y - o.Px(4f);
        o.Painter.FillTriangle(
            new UiVec2(head.X - marker - 1f, tipY - marker - 1f),
            new UiVec2(head.X + marker + 1f, tipY - marker - 1f),
            new UiVec2(head.X, tipY + 1f),
            Bgra32.FromRgba(0, 0, 0, (byte)(170 * fade)));
        o.Painter.FillTriangle(
            new UiVec2(head.X - marker, tipY - marker),
            new UiVec2(head.X + marker, tipY - marker),
            new UiVec2(head.X, tipY),
            HudOverlay.Fade(o.Ink.Ally, fade));

        // Whole, no meter, so a healthy squad stays quiet.
        bool hurt = health is < 0.995f;
        float barHeight = MathF.Max(2f, o.Px(3f));
        float barSpace = hurt ? barHeight + o.Px(5f) : 0f;
        float size = o.Px(HudOverlay.Body);
        float top = tipY - marker - o.Px(3f) - barSpace - o.Text.LineHeightAt(size);
        UiRect line = o.Caption(head.X, top, name, distanceMetres > NameTagDistanceMetres ? HudOverlay.Distance(distanceMetres) : null, o.Ink.Ally, fade);
        if (hurt && health is { } left)
        {
            float barWidth = MathF.Max(o.Px(36f), line.Width);
            var track = new UiRect(head.X - (barWidth * 0.5f), line.Bottom + o.Px(3f), barWidth, barHeight);
            o.Painter.FillRect(track, Bgra32.FromRgba(0, 0, 0, (byte)(150 * fade)));
            o.Painter.FillRect(new UiRect(track.X, track.Y, track.Width * Math.Clamp(left, 0f, 1f), track.Height), HudOverlay.Fade(o.Ink.Health(left), fade));
        }
    }
}
