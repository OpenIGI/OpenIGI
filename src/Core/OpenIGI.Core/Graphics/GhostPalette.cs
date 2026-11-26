namespace OpenIGI.Core.Graphics;

/// <summary>
/// A race ghost's colours: the tactical green its cloak, contour and marker are drawn in, and the near-white its brightest
/// edges burn at. Every place that draws the ghost reads them from here: the ghost pass, its fallback and the race HUD.
/// </summary>
public static class GhostPalette
{
    /// <summary>The ghost's green, in linear light.</summary>
    public static readonly (float R, float G, float B) Tint = (0.30f, 1f, 0.42f);

    /// <summary>The near-white of its brightest edges, in linear light, a little green.</summary>
    public static readonly (float R, float G, float B) Hot = (0.82f, 1f, 0.86f);

    /// <summary>The ghost's green as the interface draws it, in display (sRGB) bytes.</summary>
    public static Bgra32 Interface => Bgra32.FromRgb(110, 255, 140);

    /// <summary>How strong the ghost's light is, at the display's white before the exposure.</summary>
    public const float Strength = 1.25f;
}
