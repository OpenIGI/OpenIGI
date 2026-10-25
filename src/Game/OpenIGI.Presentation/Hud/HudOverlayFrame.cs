using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;

namespace OpenIGI.Presentation.Hud;

/// <summary>One frame of the player's view, as a <see cref="IHudOverlay"/> draws over it.</summary>
/// <param name="Painter">What it draws with, in pixels from the window's top-left corner.</param>
/// <param name="Font">The menus' body face, for text set as the game sets its own.</param>
/// <param name="Width">The window's width, in pixels.</param>
/// <param name="Height">The window's height, in pixels.</param>
/// <param name="Eye">Where the view is from.</param>
/// <param name="Projection">The view's projection.</param>
/// <param name="Tick">The level's tick.</param>
public readonly record struct HudOverlayFrame(
    UiPainter Painter,
    UiFont Font,
    float Width,
    float Height,
    Camera3D Eye,
    Matrix4f Projection,
    int Tick)
{
    /// <summary>Finds where a point of the world is on the screen.</summary>
    /// <param name="world">The point, in world units.</param>
    /// <returns>Its position in pixels, or <see langword="null"/> when it is behind the eye.</returns>
    public UiVec2? ToScreen(Vector3d world) => HudProjection.ToScreen(Eye, Projection, world, Width, Height);
}
