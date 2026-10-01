using OpenIGI.Core.Graphics;

namespace OpenIGI.UI.Drawing;

/// <summary>One colour stop along a gradient.</summary>
/// <param name="Offset">Where the stop sits, 0 at the start of the gradient to 1 at its end.</param>
/// <param name="Color">The colour at the stop, straight alpha.</param>
public readonly record struct UiGradientStop(float Offset, Bgra32 Color);

/// <summary>The direction a linear gradient runs across its rectangle.</summary>
public enum UiGradientDirection
{
    /// <summary>From the left edge to the right edge.</summary>
    Horizontal,

    /// <summary>From the top edge to the bottom edge.</summary>
    Vertical,
}
