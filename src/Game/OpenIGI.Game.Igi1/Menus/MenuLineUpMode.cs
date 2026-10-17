
namespace OpenIGI.Game.Igi1.Menus;

/// <summary>
/// How a menu window arranges its children, as the <c>LINEUPMODE_*</c> script constants number it.
/// </summary>
/// <remarks>
/// Registered by the <c>MenuWindow</c> registrar (<c>0x423FC0</c>) and consumed by the line-up
/// (<c>0x4244B0</c>). The values are part of the data: a script writes <c>LINEUPMODE_VERT_CENTRE</c>
/// and the compiled QVM carries the constant. Note the two <c>FILLOUT</c> modes are 7 and 8, after the
/// six ordinary ones, rather than grouped with their own axis.
/// </remarks>
public enum MenuLineUpMode
{
    /// <summary>Children keep the positions they already have.</summary>
    None = 0,

    /// <summary>Stacked downward from the top edge, one spacing in.</summary>
    VerticalTop = 1,

    /// <summary>Stacked downward, with the leftover height shared evenly above, between and below.</summary>
    VerticalCentre = 2,

    /// <summary>Stacked downward, ending one spacing short of the bottom edge.</summary>
    VerticalBottom = 3,

    /// <summary>Laid left to right from the left edge, one spacing in.</summary>
    HorizontalLeft = 4,

    /// <summary>Laid left to right, with the leftover width shared evenly.</summary>
    HorizontalCentre = 5,

    /// <summary>Laid left to right, ending one spacing short of the right edge.</summary>
    HorizontalRight = 6,

    /// <summary>Stacked downward, spread so the first and last touch the spacing margins.</summary>
    VerticalFillOut = 7,

    /// <summary>Laid left to right, spread so the first and last touch the spacing margins.</summary>
    HorizontalFillOut = 8,
}
