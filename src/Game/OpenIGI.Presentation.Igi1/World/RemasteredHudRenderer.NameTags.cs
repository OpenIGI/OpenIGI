using OpenIGI.Core.Graphics;
using OpenIGI.Game.Ai;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Kit.Hud;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Presentation.Igi1.World;

/// <summary>Other players' names over their heads.</summary>
internal sealed partial class RemasteredHudRenderer
{
    /// <inheritdoc/>
    /// <remarks>
    /// Each name is projected over the player's head through the frame's eye and drawn flat there (<see cref="AllyTag"/>).
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

        var painter = UiPainter.Create(batch, new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight)));
        foreach (WorldNameTag tag in tags)
        {
            WorldCanvas canvas = new(tag.AnchorMetres, eye, horizontalTangent, aspectRatio, screenWidth, screenHeight, MetresPerUnit);
            if (canvas.DistanceMetres <= AllyTag.ReachMetres && canvas.TryProject(0f, 0f, out float headX, out float headY)
                && headX >= 0f && headX <= screenWidth && headY >= 0f && headY <= screenHeight)
            {
                AllyTag.Draw(painter, font, new UiVec2(headX, headY), tag.Name, canvas.DistanceMetres, tag.Health);
            }
        }
    }
}
