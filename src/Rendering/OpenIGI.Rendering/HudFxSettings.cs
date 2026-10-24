namespace OpenIGI.Rendering;

/// <summary>
/// How the remastered heads-up display is laid over the picture: the look of a visor the interface is projected on, and what the
/// moment does to it.
/// </summary>
/// <remarks>
/// <para>
/// The display is drawn into a layer of its own and laid over the picture by one full-screen pass that bends it as a visor
/// would (<see cref="Curvature"/>), splits its colours at the edges as a projection does (<see cref="FringePixels"/>), runs
/// scanlines through it and lets them roll (<see cref="ScanlineDensity"/>, <see cref="ScanlineStrength"/>), flickers it in
/// band by band as it appears (<see cref="Appear"/>), shakes it when the player is hit (<see cref="Shake"/>), tears the
/// picture on the side the hit came from (<see cref="Damage"/>), and lays its own glow back over it (<see cref="Glow"/>).
/// </para>
/// <para>
/// Every term is zero for a plain overlay, which is what <see cref="None"/> is: the pass then gives back the layer as drawn.
/// </para>
/// </remarks>
public readonly record struct HudFxSettings
{
    /// <summary>Gets an overlay with no effect: the layer as drawn.</summary>
    public static HudFxSettings None => default;

    /// <summary>Gets the strength of the whole treatment, nought to one; nought draws the layer as it is.</summary>
    public float Intensity { get; init; }

    /// <summary>Gets how far the visor bends the display at its corners, as a share of the screen; around 0.03.</summary>
    public float Curvature { get; init; }

    /// <summary>Gets how far the red and blue split from the green at the screen's edge, in pixels.</summary>
    public float FringePixels { get; init; }

    /// <summary>Gets the scanlines across the screen's height, per 1080 lines.</summary>
    public float ScanlineDensity { get; init; }

    /// <summary>Gets how dark a scanline's trough is, nought to one.</summary>
    public float ScanlineStrength { get; init; }

    /// <summary>Gets how far the display has flickered in, nought (gone) to one (fully up).</summary>
    public float Appear { get; init; }

    /// <summary>Gets how strongly the display's own glow is laid back over it.</summary>
    public float Glow { get; init; }

    /// <summary>Gets the seconds the scanlines and the noise roll by.</summary>
    public float Seconds { get; init; }

    /// <summary>Gets the display's shake this frame, in pixels.</summary>
    public (float X, float Y) Shake { get; init; }

    /// <summary>Gets the hit wash in each corner, nought to one: top left, top right, bottom left, bottom right.</summary>
    public (float TopLeft, float TopRight, float BottomLeft, float BottomRight) Damage { get; init; }

    /// <summary>Gets the colour of the hit wash, as linear red, green and blue.</summary>
    public (float R, float G, float B) DamageColour { get; init; }

    /// <summary>Gets a value indicating whether the pass changes anything at all.</summary>
    public bool IsActive => Intensity > 0f;
}
