using OpenIGI.Core.Graphics;

namespace OpenIGI.Game.Menus;

/// <summary>
/// The presentation rules the menu types share: where an item lands on screen, and what colour it draws in.
/// </summary>
/// <remarks>
/// Transcribed from <c>MenuText_Prepare</c> (<c>0x420F40</c>) and <c>MenuText_Render</c>
/// (<c>0x421130</c>).
/// </remarks>
public static class MenuLayout
{
    /// <summary>The width the menus are authored against.</summary>
    public const int AuthoredWidth = 640;

    /// <summary>The height the menus are authored against.</summary>
    public const int AuthoredHeight = 480;

    /// <summary>The value the original's colour scale treats as full intensity.</summary>
    /// <remarks>
    /// The same 128 that <c>Font_Create</c> initialises its colour to. Menu colours are expressed on a
    /// 0–128 scale, not 0–255.
    /// </remarks>
    public const float FullIntensity = 128.0f;

    /// <summary>
    /// The colour table <c>MenuText</c> indexes with its <c>ColourIndex</c> parameter, on the
    /// original's 0–128 scale.
    /// </summary>
    /// <remarks>
    /// Read from the table at <c>0x53B778</c>: four entries of three floats, indexed by
    /// <c>ColourIndex &amp; 0xF</c>. The names come from the constants the <c>MenuText</c> registrar
    /// declares.
    /// </remarks>
    public static IReadOnlyList<(float R, float G, float B)> Colours { get; } =
    [
        (0.0f, 0.0f, 0.0f),        // TEXT_BLACK
        (128.0f, 128.0f, 128.0f),  // TEXT_WHITE
        (0.0f, 128.0f, 0.0f),      // TEXT_GREEN
        (128.0f, 95.0f, 0.0f),     // TEXT_ORANGE
    ];

    /// <summary>
    /// Maps an authored menu position to a screen position.
    /// </summary>
    /// <param name="x">The authored horizontal position.</param>
    /// <param name="y">The authored vertical position.</param>
    /// <param name="screenWidth">The back-buffer width in pixels.</param>
    /// <param name="screenHeight">The back-buffer height in pixels.</param>
    /// <returns>The screen position in pixels.</returns>
    /// <remarks>
    /// The original computes <c>(screenWidth &gt;&gt; 1) + x - 320</c> and
    /// <c>(screenHeight &gt;&gt; 1) + y - 240</c>. So the menu is <b>centred at its authored size, not
    /// scaled</b>: at 1024×768 the text stays 640×480-sized in the middle of the screen rather than
    /// growing. Scaling it to fill would be a visible divergence, and an easy one to introduce by
    /// accident.
    /// </remarks>
    public static (int X, int Y) ToScreen(int x, int y, int screenWidth, int screenHeight) =>
        ((screenWidth >> 1) + x - (AuthoredWidth / 2), (screenHeight >> 1) + y - (AuthoredHeight / 2));

    /// <summary>Gets a colour from the table, on the original's 0–128 scale.</summary>
    /// <param name="colourIndex">The <c>ColourIndex</c> parameter. Only the low four bits are used.</param>
    /// <param name="selected">Whether the item is highlighted.</param>
    /// <returns>The colour components.</returns>
    /// <remarks>
    /// A highlighted item is drawn at <c>c * 0.5 + 64</c> per channel, which brightens the dark
    /// channels toward the middle rather than scaling the whole colour up — <c>TEXT_ORANGE</c>'s blue
    /// goes from 0 to 64 while its red stays at 128.
    /// </remarks>
    public static (float R, float G, float B) GetColour(int colourIndex, bool selected)
    {
        (float r, float g, float b) = Colours[colourIndex & 0xF];
        return selected
            ? ((r * 0.5f) + 64.0f, (g * 0.5f) + 64.0f, (b * 0.5f) + 64.0f)
            : (r, g, b);
    }

    /// <summary>Converts a colour on the original's 0–128 scale to a renderable pixel.</summary>
    /// <param name="colour">The colour components.</param>
    /// <returns>The opaque pixel.</returns>
    /// <remarks>128 is full intensity, so the scale factor is <c>255 / 128</c>, saturating above that.</remarks>
    public static Bgra32 ToPixel((float R, float G, float B) colour) =>
        Bgra32.FromRgb(Channel(colour.R), Channel(colour.G), Channel(colour.B));

    /// <summary>Builds the name of one of a frame's eight border sprites.</summary>
    /// <param name="spriteResource">The frame's <c>Sprite Resource</c> parameter.</param>
    /// <param name="piece">The piece number, 1 through 8.</param>
    /// <returns>The sprite name.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="piece"/> is outside 1–8.</exception>
    /// <remarks>
    /// <c>MenuFrame_Prepare</c> (<c>0x422640</c>) loads exactly eight, formatted <c>"%s_%d.spr"</c> —
    /// which is why <c>menusystem.res</c> holds <c>win1_1.spr</c> through <c>win1_8.spr</c>. Eight
    /// pieces, not nine: the centre is not drawn.
    /// </remarks>
    public static string GetFramePieceName(string spriteResource, int piece)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spriteResource);
        ArgumentOutOfRangeException.ThrowIfLessThan(piece, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(piece, FramePieceCount);
        return $"{spriteResource}_{piece}.spr";
    }

    /// <summary>The number of border sprites a frame loads.</summary>
    public const int FramePieceCount = 8;

    private static byte Channel(float value) =>
        (byte)Math.Clamp(MathF.Round(value * 255.0f / FullIntensity), 0.0f, 255.0f);
}
