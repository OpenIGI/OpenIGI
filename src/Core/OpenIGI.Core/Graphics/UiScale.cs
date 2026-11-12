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
    public static int For(int screenWidth, int screenHeight) => For(screenWidth, screenHeight, AuthoredWidth, AuthoredHeight);

    /// <summary>Works out the magnification for a back buffer, for an interface authored at a size of its own.</summary>
    /// <param name="screenWidth">The back buffer's width in pixels.</param>
    /// <param name="screenHeight">The back buffer's height in pixels.</param>
    /// <param name="authoredWidth">The width the interface is laid out for.</param>
    /// <param name="authoredHeight">The height the interface is laid out for.</param>
    /// <returns>A whole-number scale, at least <see cref="Native"/>.</returns>
    /// <remarks>
    /// The same rule as <see cref="For(int, int)"/>, for a title whose interface was laid out at a larger display: I.G.I. 2 places
    /// its status art in the display's own pixels and was played at 720 lines and more, so it is magnified by how many times that
    /// the display holds, as Project I.G.I.'s is by how many times it holds 640×480.
    /// </remarks>
    public static int For(int screenWidth, int screenHeight, int authoredWidth, int authoredHeight)
    {
        if (screenWidth <= 0 || screenHeight <= 0 || authoredWidth <= 0 || authoredHeight <= 0)
        {
            return Native;
        }

        int horizontal = screenWidth / authoredWidth;
        int vertical = screenHeight / authoredHeight;
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
    public static int Resolve(int requested, int screenWidth, int screenHeight) =>
        Resolve(requested, screenWidth, screenHeight, AuthoredWidth, AuthoredHeight);

    /// <summary>Resolves a requested scale against a back buffer, for an interface authored at a size of its own.</summary>
    /// <param name="requested"><see cref="Automatic"/> to derive one, or a whole number to force it.</param>
    /// <param name="screenWidth">The back buffer's width in pixels.</param>
    /// <param name="screenHeight">The back buffer's height in pixels.</param>
    /// <param name="authoredWidth">The width the interface is laid out for.</param>
    /// <param name="authoredHeight">The height the interface is laid out for.</param>
    /// <returns>The scale to draw at.</returns>
    public static int Resolve(int requested, int screenWidth, int screenHeight, int authoredWidth, int authoredHeight)
    {
        if (requested <= Automatic)
        {
            return For(screenWidth, screenHeight, authoredWidth, authoredHeight);
        }

        return requested > Maximum ? Maximum : requested;
    }

    /// <summary>Gets the scale a desktop tool -- the editor -- draws at on a screen.</summary>
    /// <param name="requested">A chosen scale, or zero (or less) to pick one from the screen.</param>
    /// <param name="screenHeight">The screen's height in pixels.</param>
    /// <returns>
    /// The chosen scale; or, picked, one-to-one up to 1440 rows and half again larger at 2160, in quarter steps, the way
    /// desktop tools are sized for the screens they are shown on.
    /// </returns>
    /// <remarks>
    /// Not <see cref="Resolve(int, int, int)"/>: that magnifies the original's 640 by 480 screens by whole numbers, which is
    /// three times over on a 1440p display -- right for the game's menus, enormous for a tool laid out in its own pixels.
    /// </remarks>
    public static float Desktop(float requested, float screenHeight)
    {
        if (requested > 0f)
        {
            return Math.Clamp(requested, 0.5f, 4f);
        }

        float grown = 1f + (MathF.Max(0f, screenHeight - 1440f) / 1440f);
        return Math.Clamp(MathF.Round(grown * 4f) / 4f, 1f, 2f);
    }

    /// <summary>The smallest the menus are drawn, as a share of their authored 1080-line size, where the screen has room.</summary>
    public const float MenuReadableScale = 0.85f;

    /// <summary>The narrowest the menus' canvas is made to keep them readable, in authored units: a 4:3 screen's.</summary>
    public const float MenuNarrowestWidth = 1440f;

    /// <summary>Gets how much larger than in proportion to the screen the menus are drawn, so they can be read on a small one.</summary>
    /// <param name="screenWidth">The screen's width in pixels.</param>
    /// <param name="screenHeight">The screen's height in pixels.</param>
    /// <returns>
    /// One on a screen of 918 lines or more, and on one with no size. Below that, the factor that brings the menus
    /// up to <see cref="MenuReadableScale"/> of their authored size -- or as near it as leaves their canvas
    /// <see cref="MenuNarrowestWidth"/> units wide, and never less than one.
    /// </returns>
    /// <remarks>
    /// The menus are authored on a 1080-line canvas and scaled with the screen's height, which on a 720-line
    /// laptop makes 17-unit body text 11 pixels tall and an 11-unit caption 7. Drawing them larger costs canvas:
    /// at the floor a 1280 by 720 screen is 1506 by 847 units, which the pages reflow and scroll within. The
    /// width is held to what a 4:3 screen already gives them, the narrowest they are laid out for, so a
    /// 1024 by 768 screen is left as it was.
    /// </remarks>
    public static float MenuBoost(float screenWidth, float screenHeight)
    {
        if (screenWidth <= 0f || screenHeight <= 0f)
        {
            return 1f;
        }

        float natural = screenHeight / 1080f;
        float wanted = MathF.Min(MenuReadableScale, screenWidth / MenuNarrowestWidth);
        return wanted > natural ? wanted / natural : 1f;
    }
}
