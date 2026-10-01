namespace OpenIGI.Rendering;

/// <summary>One level of a blurred copy of the frame: a texture the 2D pass can sample, and how blurred it is.</summary>
/// <param name="Texture">The blurred copy, covering the whole screen in texture coordinates 0 to 1.</param>
/// <param name="SigmaPx">The blur's standard deviation in full-resolution pixels.</param>
public readonly record struct BackdropLevel(TextureHandle Texture, float SigmaPx);

/// <summary>
/// The frame drawn so far, blurred at several strengths — what the interface's frosted glass samples to
/// show the scene behind a panel.
/// </summary>
/// <param name="Levels">The levels, ordered from least to most blurred; empty when the device has none.</param>
/// <param name="Width">The width of the frame the levels were made from, in pixels.</param>
/// <param name="Height">The height of the frame the levels were made from, in pixels.</param>
public sealed record BackdropBlur(IReadOnlyList<BackdropLevel> Levels, int Width, int Height)
{
    /// <summary>Gets the empty blur: nothing to sample, so glass is drawn as its tint alone.</summary>
    public static BackdropBlur None { get; } = new([], 0, 0);

    /// <summary>Gets a value indicating whether there is anything to sample.</summary>
    public bool IsEmpty => Levels.Count == 0 || Width <= 0 || Height <= 0;
}
