using OpenIGI.Rendering;

namespace OpenIGI.UI.Rhi;

/// <summary>One level of the blurred scene pyramid: a texture and how blurred it is.</summary>
/// <param name="Texture">The blurred copy of the frame, the size of the screen in texture coordinates.</param>
/// <param name="SigmaPx">The blur's standard deviation in the interface's units.</param>
public readonly record struct RhiBackdropLevel(TextureHandle Texture, float SigmaPx);

/// <summary>
/// The blurred copies of this frame's scene that frosted glass samples, from least to most blurred.
/// </summary>
/// <remarks>
/// The host fills it once per frame from the device's backdrop blur, after the 3D scene and before the
/// interface. A glass panel asking for a blur between two levels draws the sharper one and cross-fades
/// the blurrier one over it, so an animated blur radius moves smoothly instead of stepping.
/// </remarks>
public sealed class RhiBackdrop
{
    /// <summary>Initializes a new instance of the <see cref="RhiBackdrop"/> class.</summary>
    /// <param name="levels">The levels, ordered from least to most blurred.</param>
    /// <param name="width">The width of the screen the levels cover, in the interface's units.</param>
    /// <param name="height">The height of the screen the levels cover, in the interface's units.</param>
    /// <exception cref="ArgumentNullException"><paramref name="levels"/> is <see langword="null"/>.</exception>
    public RhiBackdrop(IReadOnlyList<RhiBackdropLevel> levels, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(levels);
        Levels = levels;
        Width = MathF.Max(1f, width);
        Height = MathF.Max(1f, height);
    }

    /// <summary>Wraps a device's blurred frame for the renderer, or returns <see langword="null"/> when it is empty.</summary>
    /// <param name="blur">The device's blurred frame.</param>
    /// <param name="width">The width of the 2D coordinate space the interface draws in.</param>
    /// <param name="height">The height of the 2D coordinate space the interface draws in.</param>
    /// <returns>The backdrop, or <see langword="null"/>.</returns>
    /// <remarks>
    /// The interface draws in window units and the levels are measured in frame pixels; on a high-density
    /// display the two differ, so each level's blur is restated in the interface's units.
    /// </remarks>
    public static RhiBackdrop? From(BackdropBlur blur, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(blur);
        if (blur.IsEmpty || width <= 0f || height <= 0f)
        {
            return null;
        }

        float unitsPerPixel = width / blur.Width;
        var levels = new RhiBackdropLevel[blur.Levels.Count];
        for (int i = 0; i < levels.Length; i++)
        {
            levels[i] = new RhiBackdropLevel(blur.Levels[i].Texture, blur.Levels[i].SigmaPx * unitsPerPixel);
        }

        return new RhiBackdrop(levels, width, height);
    }

    /// <summary>Gets the levels, ordered from least to most blurred.</summary>
    public IReadOnlyList<RhiBackdropLevel> Levels { get; }

    /// <summary>Gets the width of the screen the levels cover, in device pixels.</summary>
    public float Width { get; }

    /// <summary>Gets the height of the screen the levels cover, in device pixels.</summary>
    public float Height { get; }

    /// <summary>Picks the two levels either side of a requested blur and how far between them it lies.</summary>
    /// <param name="sigmaPx">The requested blur in device pixels.</param>
    /// <param name="lower">The sharper level.</param>
    /// <param name="upper">The blurrier level.</param>
    /// <param name="blend">How much of <paramref name="upper"/> to lay over <paramref name="lower"/>, 0 to 1.</param>
    /// <returns><see langword="false"/> when there are no levels.</returns>
    public bool TryPick(float sigmaPx, out RhiBackdropLevel lower, out RhiBackdropLevel upper, out float blend)
    {
        lower = upper = default;
        blend = 0f;
        if (Levels.Count == 0)
        {
            return false;
        }

        lower = upper = Levels[0];
        if (sigmaPx <= Levels[0].SigmaPx)
        {
            return true;
        }

        for (int i = 1; i < Levels.Count; i++)
        {
            if (sigmaPx <= Levels[i].SigmaPx)
            {
                lower = Levels[i - 1];
                upper = Levels[i];

                // Blur radii compose roughly geometrically, so interpolate in log space.
                float a = MathF.Log2(MathF.Max(1e-3f, lower.SigmaPx));
                float b = MathF.Log2(MathF.Max(1e-3f, upper.SigmaPx));
                blend = b <= a ? 1f : Math.Clamp((MathF.Log2(sigmaPx) - a) / (b - a), 0f, 1f);
                return true;
            }
        }

        lower = upper = Levels[^1];
        return true;
    }
}
