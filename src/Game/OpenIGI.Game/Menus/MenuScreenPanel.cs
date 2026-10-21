
namespace OpenIGI.Game.Menus;

/// <summary>
/// The green panel a screen frames its contents with, and the way it opens as the screen appears.
/// </summary>
/// <param name="X0">The panel's left edge in authored pixels.</param>
/// <param name="Y0">The panel's top edge.</param>
/// <param name="X1">The panel's right edge.</param>
/// <param name="Y1">The panel's bottom edge.</param>
/// <param name="Alpha">The fade, 0 to 1.</param>
/// <remarks>
/// <para>
/// From <c>MenuScreen_DrawPanel</c> (<c>0x421CD0</c>). The rectangle is the screen's own
/// <c>Frame x0/y0/x1/y1</c> parameters, each plus one — so the panel is data, and every screen has
/// its own.
/// </para>
/// <para>
/// The horizontal edges are interpolated out of the centre of the authored 640 by the manager's fade
/// alpha: <c>320 + (edge - 320) * alpha</c>. At alpha 0 the panel is a vertical line down the middle
/// of the display and at alpha 1 it is its full width, so it <b>opens outwards</b> as a screen fades
/// in and closes as it fades out. That is the animation, and it is why the panel cannot be drawn from
/// the parameters alone.
/// </para>
/// <para>
/// The vertical edges do not animate.
/// </para>
/// </remarks>
public readonly record struct MenuScreenPanel(int X0, int Y0, int X1, int Y1, float Alpha)
{
    /// <summary>The green channel the border and the title rule are drawn in.</summary>
    /// <remarks>The original's <c>0.75</c> on a 0-to-1 scale, against the fill's full <c>1.0</c>.</remarks>
    public const byte EdgeGreen = 191;

    /// <summary>The fill's alpha at the top of the panel, before the fade is applied.</summary>
    public const float TopFillAlpha = 0.2f;

    /// <summary>The fill's alpha at the bottom.</summary>
    /// <remarks>The gradient is what makes the panel read as lit from above rather than as a flat wash.</remarks>
    public const float BottomFillAlpha = 0.05f;

    /// <summary>How far below the top edge the rule under the screen's title sits.</summary>
    public const int TitleRuleOffset = 20;

    /// <summary>Gets a value indicating whether there is nothing to draw.</summary>
    public bool IsEmpty => X1 <= X0 || Y1 <= Y0 || Alpha <= 0f;

    /// <summary>Builds the panel for a screen at a given fade.</summary>
    /// <param name="screen">The screen.</param>
    /// <param name="fadeAlpha">The manager's fade, on its own 0-to-16 scale.</param>
    /// <returns>The panel.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="screen"/> is <see langword="null"/>.</exception>
    public static MenuScreenPanel For(MenuScreenTask screen, int fadeAlpha)
    {
        ArgumentNullException.ThrowIfNull(screen);

        // The original divides the manager's alpha by eight and clamps at one, so the panel is fully
        // open at half the fade and holds there.
        float alpha = Math.Clamp(fadeAlpha * 0.125f, 0f, 1f);

        int centre = MenuLayout.AuthoredWidth / 2;
        int left = (int)(centre + ((screen.FrameX0 + 1 - centre) * alpha));
        int right = (int)(centre + ((screen.FrameX1 + 1 - centre) * alpha));

        return new MenuScreenPanel(left, screen.FrameY0 + 1, right, screen.FrameY1 + 1, alpha);
    }
}
