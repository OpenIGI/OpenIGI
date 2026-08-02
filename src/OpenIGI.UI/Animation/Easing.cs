namespace OpenIGI.UI.Animation;

/// <summary>A named easing curve applied to a normalised <c>[0,1]</c> progress.</summary>
public enum Ease
{
    /// <summary>No easing; progress passes through unchanged.</summary>
    Linear,

    /// <summary>Accelerates from rest (quadratic).</summary>
    InQuad,

    /// <summary>Decelerates to rest (quadratic).</summary>
    OutQuad,

    /// <summary>Accelerates then decelerates (quadratic).</summary>
    InOutQuad,

    /// <summary>Accelerates from rest (cubic).</summary>
    InCubic,

    /// <summary>Decelerates to rest (cubic).</summary>
    OutCubic,

    /// <summary>Accelerates then decelerates (cubic).</summary>
    InOutCubic,

    /// <summary>Decelerates to rest (quintic); the workhorse for panels sliding in.</summary>
    OutQuint,

    /// <summary>Overshoots the target and settles back (a gentle "back" curve).</summary>
    OutBack,

    /// <summary>Springs past the target and oscillates in to it (elastic).</summary>
    OutElastic,
}

/// <summary>
/// Evaluates the easing curves the interface animates with.
/// </summary>
/// <remarks>
/// Every curve maps a progress <c>t</c> in <c>[0,1]</c> to an eased value that is 0 at 0 and 1 at 1
/// (except the overshoot curves, which pass outside the range in the middle and are the point of using
/// them). They are pure functions with no state, so a tween can pick a curve by <see cref="Ease"/> and
/// the same input always gives the same output — which is what lets the animation tests assert exact
/// values.
/// </remarks>
public static class Easing
{
    private const float BackOvershoot = 1.70158f;

    /// <summary>Applies an easing curve to a progress value.</summary>
    /// <param name="ease">The curve to apply.</param>
    /// <param name="t">The progress, clamped to <c>[0,1]</c> before evaluation.</param>
    /// <returns>The eased value.</returns>
    public static float Evaluate(Ease ease, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return ease switch
        {
            Ease.Linear => t,
            Ease.InQuad => t * t,
            Ease.OutQuad => 1f - ((1f - t) * (1f - t)),
            Ease.InOutQuad => t < 0.5f ? 2f * t * t : 1f - (MathF.Pow((-2f * t) + 2f, 2f) / 2f),
            Ease.InCubic => t * t * t,
            Ease.OutCubic => 1f - MathF.Pow(1f - t, 3f),
            Ease.InOutCubic => t < 0.5f ? 4f * t * t * t : 1f - (MathF.Pow((-2f * t) + 2f, 3f) / 2f),
            Ease.OutQuint => 1f - MathF.Pow(1f - t, 5f),
            Ease.OutBack => 1f + ((BackOvershoot + 1f) * MathF.Pow(t - 1f, 3f)) + (BackOvershoot * MathF.Pow(t - 1f, 2f)),
            Ease.OutElastic => OutElastic(t),
            _ => t,
        };
    }

    private static float OutElastic(float t)
    {
        if (t is <= 0f or >= 1f)
        {
            return t;
        }

        const float period = 0.3f;
        float amplitude = MathF.Pow(2f, -10f * t);
        return (amplitude * MathF.Sin((t - (period / 4f)) * (2f * MathF.PI) / period)) + 1f;
    }
}
