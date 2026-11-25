using OpenIGI.Core.Graphics;

namespace OpenIGI.Game.Player;

/// <summary>
/// The orbit a title's player is watched from around its own body (<see cref="HumanOrbitCamera"/>, the human camera's modes
/// 3 and 5), which the frame draws between ticks: turned by the mouse the next tick will be given, round the body as it is
/// drawn. The tick alone moves the orbit itself, so what the level reads of it stays the tick's.
/// </summary>
public interface ITitleOrbitView
{
    /// <summary>Gets a value indicating whether the orbit has the view.</summary>
    bool IsOrbiting { get; }

    /// <summary>The orbit as a frame between two ticks draws it, without changing it.</summary>
    /// <param name="tickBlend">How far through the tick the frame is.</param>
    /// <param name="mouseX">The horizontal counts waiting for the next tick.</param>
    /// <param name="mouseY">The vertical counts waiting for the next tick.</param>
    /// <param name="sensitivity">The current profile sensitivity.</param>
    /// <param name="invertMouse">Whether vertical look is inverted.</param>
    /// <returns>The eye to draw from, in metres.</returns>
    Camera3D PreviewOrbit(float tickBlend, float mouseX, float mouseY, float sensitivity, bool invertMouse);
}
