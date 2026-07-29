namespace OpenIGI.Core.Graphics;

/// <summary>
/// How much the 640×480-authored user interface is magnified for the back buffer it is drawn into.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a deliberate divergence, and the only one in the interface.</b> The original authors
/// every screen at 640×480 and never scales: the menus are <i>centred</i> at that size
/// (<c>(width &gt;&gt; 1) + x − 320</c>) and the HUD draws its sprites at 1:1 with a literal scale of
/// <c>1.0</c>. That was reasonable when the highest mode the game offered was 1024×768. On a modern
/// display it leaves the menu a small island in the middle of the screen and the crosshair a nine-pixel
/// speck, which is faithful and unusable.
/// </para>
/// <para>
/// The magnification is a <b>whole number</b>. Interface art of this era is pixel art with hard edges
/// and a colour key; a fractional scale resamples it into a blur, while doubling or tripling keeps it
/// exactly as drawn. It is the smaller of the two axes' ratios so nothing is ever cropped, and never
/// below one so a small window behaves as the original did.
/// </para>
/// <para>
/// Passing <see cref="Native"/> restores retail behaviour exactly.
/// </para>
/// </remarks>
public static class UiScale
{
    /// <summary>The width every screen is authored at.</summary>
    public const int AuthoredWidth = 640;

    /// <summary>The height every screen is authored at.</summary>
    public const int AuthoredHeight = 480;

    /// <summary>The scale that reproduces the original exactly.</summary>
    public const int Native = 1;

    /// <summary>The value that asks for the scale to be derived from the back buffer.</summary>
    public const int Automatic = 0;

    /// <summary>The largest magnification offered, so a very large display stays sane.</summary>
    public const int Maximum = 8;

    /// <summary>Works out the magnification for a back buffer.</summary>
    /// <param name="screenWidth">The back buffer's width in pixels.</param>
    /// <param name="screenHeight">The back buffer's height in pixels.</param>
    /// <returns>A whole-number scale, at least <see cref="Native"/>.</returns>
    /// <remarks>
    /// The <b>smaller</b> of the two ratios, so a wide display magnifies by its height and nothing runs
    /// off the edge. 1920×1080 gives 2 (its height allows 2.25), 2560×1440 gives 3.
    /// </remarks>
    public static int For(int screenWidth, int screenHeight)
    {
        if (screenWidth <= 0 || screenHeight <= 0)
        {
            return Native;
        }

        int horizontal = screenWidth / AuthoredWidth;
        int vertical = screenHeight / AuthoredHeight;
        int scale = horizontal < vertical ? horizontal : vertical;

        return scale < Native ? Native : scale > Maximum ? Maximum : scale;
    }

    /// <summary>Resolves a requested scale against a back buffer.</summary>
    /// <param name="requested">
    /// <see cref="Automatic"/> to derive one, or a whole number to force it.
    /// </param>
    /// <param name="screenWidth">The back buffer's width in pixels.</param>
    /// <param name="screenHeight">The back buffer's height in pixels.</param>
    /// <returns>The scale to draw at.</returns>
    public static int Resolve(int requested, int screenWidth, int screenHeight)
    {
        if (requested <= Automatic)
        {
            return For(screenWidth, screenHeight);
        }

        return requested > Maximum ? Maximum : requested;
    }
}
