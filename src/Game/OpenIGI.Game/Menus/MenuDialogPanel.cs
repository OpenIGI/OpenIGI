namespace OpenIGI.Game.Menus;

/// <summary>
/// The green box a popup is drawn in, and the way it opens out of its own centre as it fades in.
/// </summary>
/// <param name="X0">The box's left edge in authored pixels.</param>
/// <param name="Y0">The box's top edge.</param>
/// <param name="X1">The box's right edge.</param>
/// <param name="Y1">The box's bottom edge.</param>
/// <param name="RuleY">The rule under the popup's title.</param>
/// <param name="Alpha">The fade, 0 to 1.</param>
/// <remarks>
/// <para>
/// From <c>DialogWindow_DrawBox</c> (<c>0x419D30</c>), which the popup's render (<c>0x419CB0</c>) calls after its controls. The
/// rectangle is the popup's own <c>X-Pos</c>, <c>Y-Pos</c>, <c>Width</c> and <c>Height</c>, and the rule sits
/// <see cref="MenuScreenPanel.TitleRuleOffset"/> below its top.
/// </para>
/// <para>
/// Unlike a screen's panel every edge, the rule's included, is pulled into the box's centre by the fade: <c>c + (edge - c) *
/// alpha</c>. At alpha 0 the box is a point and at alpha 1 its full size, so the popup <b>grows</b> as it fades in.
/// </para>
/// <para>
/// It is drawn as the screen's panel is, in the same greens, over a black backing at <see cref="BackingAlpha"/>, which is what
/// sets the popup apart from the screen beneath it.
/// </para>
/// </remarks>
public readonly record struct MenuDialogPanel(int X0, int Y0, int X1, int Y1, int RuleY, float Alpha)
{
    /// <summary>The alpha the black backing is filled at, before the fade is applied.</summary>
    /// <remarks>The original's <c>0.9</c>, passed to the rectangle fill (<c>0x4B4AF0</c>) times the fade.</remarks>
    public const float BackingAlpha = 0.9f;

    /// <summary>Gets a value indicating whether there is nothing to draw.</summary>
    public bool IsEmpty => X1 <= X0 || Y1 <= Y0 || Alpha <= 0f;

    /// <summary>Builds the box for a popup at a given fade.</summary>
    /// <param name="popup">The popup.</param>
    /// <param name="fadeAlpha">The manager's fade, on its own 0-to-16 scale.</param>
    /// <returns>The box.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="popup"/> is <see langword="null"/>.</exception>
    public static MenuDialogPanel For(DialogWindowTask popup, int fadeAlpha)
    {
        ArgumentNullException.ThrowIfNull(popup);

        // As the screen's panel: the manager's alpha over eight, clamped at one.
        float alpha = Math.Clamp(fadeAlpha * 0.125f, 0f, 1f);

        float left = popup.X;
        float top = popup.Y;
        float right = left + popup.Width;
        float bottom = top + popup.Height;
        float centreX = (right + left) * 0.5f;
        float centreY = (bottom + top) * 0.5f;

        return new MenuDialogPanel(
            (int)(((left - centreX) * alpha) + centreX),
            (int)(((top - centreY) * alpha) + centreY),
            (int)(((right - centreX) * alpha) + centreX),
            (int)(((bottom - centreY) * alpha) + centreY),
            (int)(((top + MenuScreenPanel.TitleRuleOffset - centreY) * alpha) + centreY),
            alpha);
    }
}
