using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Hud.Remastered.Widgets;

/// <summary>How the player has the remastered crosshair look: its colour and opacity, its ticks' length and thickness, the gap at
/// its centre, a centre dot, a dark edge, and whether it opens with the weapon's spread.</summary>
/// <param name="Ink">Its colour, with its opacity.</param>
/// <param name="Size">Its ticks' length, as a share of their own.</param>
/// <param name="Thickness">Its ticks' thickness, as a share of their own.</param>
/// <param name="Gap">Extra space at its centre, in design pixels.</param>
/// <param name="Dot">Whether it has a centre dot.</param>
/// <param name="Outline">Whether it has a faint dark edge.</param>
/// <param name="Dynamic">Whether it opens with the weapon's spread.</param>
public sealed record CrosshairStyle(Bgra32 Ink, float Size, float Thickness, float Gap, bool Dot, bool Outline, bool Dynamic)
{
    /// <summary>Gets the style the player's settings pick (the <c>r_crosshair_*</c> variables).</summary>
    public static CrosshairStyle Current => new(
        HudCrosshair.ColourAt(HudCVars.CrosshairColour.Value).WithAlpha((byte)(255 * Math.Clamp(HudCVars.CrosshairOpacity.Value, 20, 100) / 100)),
        HudCVars.CrosshairSize.Value / 100f,
        HudCVars.CrosshairThickness.Value / 100f,
        HudCVars.CrosshairGap.Value,
        HudCVars.CrosshairDot.Value,
        HudCVars.CrosshairOutline.Value,
        HudCVars.CrosshairDynamic.Value);
}
