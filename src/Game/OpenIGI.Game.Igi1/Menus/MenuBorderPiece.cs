
namespace OpenIGI.Game.Igi1.Menus;

/// <summary>
/// The eight sprites a frame's border is assembled from, indexed as the original indexes them.
/// </summary>
/// <remarks>
/// <c>MenuFrame_Prepare</c> (<c>0x422640</c>) loads them as <c>&lt;sprite&gt;_1.spr</c> through
/// <c>_8.spr</c>, and <c>MenuFrame_Render</c> (<c>0x4226D0</c>) uses them in a clockwise ring from the
/// top-left. So <see cref="TopLeft"/> is <c>_1</c>, <see cref="Left"/> is <c>_8</c>, and the value is
/// one less than the number in the file name.
/// </remarks>
public enum MenuBorderPiece
{
    /// <summary>The top-left corner, <c>_1</c>.</summary>
    TopLeft = 0,

    /// <summary>The top edge, tiled, <c>_2</c>.</summary>
    Top = 1,

    /// <summary>The top-right corner, <c>_3</c>.</summary>
    TopRight = 2,

    /// <summary>The right edge, tiled, <c>_4</c>.</summary>
    Right = 3,

    /// <summary>The bottom-right corner, <c>_5</c>.</summary>
    BottomRight = 4,

    /// <summary>The bottom edge, tiled, <c>_6</c>.</summary>
    Bottom = 5,

    /// <summary>The bottom-left corner, <c>_7</c>.</summary>
    BottomLeft = 6,

    /// <summary>The left edge, tiled, <c>_8</c>.</summary>
    Left = 7,
}

/// <summary>Where one border piece is drawn, relative to the frame's top-left corner.</summary>
/// <param name="Piece">Which sprite to draw.</param>
/// <param name="X">The horizontal offset from the frame's left edge.</param>
/// <param name="Y">The vertical offset from the frame's top edge.</param>
public readonly record struct MenuBorderPlacement(MenuBorderPiece Piece, int X, int Y);
