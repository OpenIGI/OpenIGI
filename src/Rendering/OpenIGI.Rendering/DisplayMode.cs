namespace OpenIGI.Rendering;

/// <summary>
/// A display mode: the back-buffer size, colour depth and refresh rate a device can be set to.
/// </summary>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
/// <param name="BitsPerPixel">The colour depth in bits, as the original's mode enumeration reports it.</param>
/// <param name="RefreshRate">The refresh rate in hertz, or zero when the display driver picks one.</param>
/// <remarks>
/// The original enumerates these through the <c>Enum</c> render-mode method (<c>0x4924D0</c>, backed by
/// <c>IDirectDraw7::EnumDisplayModes</c>) and applies one through <c>DisplaySetMode</c>. Retail boots
/// at 640×480.
/// </remarks>
public readonly record struct DisplayMode(int Width, int Height, int BitsPerPixel, int RefreshRate)
{
    /// <summary>Gets the mode the original creates its window at before any user selection.</summary>
    public static DisplayMode Default => new(640, 480, 32, 0);

    /// <summary>Gets the aspect ratio, width divided by height.</summary>
    public double AspectRatio => Height == 0 ? 0.0 : (double)Width / Height;

    /// <inheritdoc/>
    public override string ToString() =>
        RefreshRate > 0
            ? $"{Width}x{Height}x{BitsPerPixel} @ {RefreshRate} Hz"
            : $"{Width}x{Height}x{BitsPerPixel}";
}
