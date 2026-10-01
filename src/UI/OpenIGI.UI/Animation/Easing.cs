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

    /// <summary>The design's signature curve, <c>cubic-bezier(.16,1,.3,1)</c>: a fast start that settles long and soft.</summary>
    Emphasized,

    /// <summary>The design's exit curve, <c>cubic-bezier(.7,0,.84,0)</c>: holds, then leaves quickly.</summary>
    EmphasizedAccelerate,

    /// <summary>A toggle knob's curve, <c>cubic-bezier(.3,.9,.3,1)</c>: quick and without overshoot.</summary>
    Snappy,

    /// <summary>A light sweep's curve, <c>cubic-bezier(.5,0,.2,1)</c>: eases in and out across the screen.</summary>
    Sweep,
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
            Ease.Emphasized => CubicBezier(0.16f, 1f, 0.3f, 1f, t),
            Ease.EmphasizedAccelerate => CubicBezier(0.7f, 0f, 0.84f, 0f, t),
            Ease.Snappy => CubicBezier(0.3f, 0.9f, 0.3f, 1f, t),
            Ease.Sweep => CubicBezier(0.5f, 0f, 0.2f, 1f, t),
            _ => t,
        };
    }

    /// <summary>Evaluates a CSS <c>cubic-bezier(x1, y1, x2, y2)</c> timing function.</summary>
    /// <param name="x1">The first control point's time.</param>
    /// <param name="y1">The first control point's progress.</param>
    /// <param name="x2">The second control point's time.</param>
    /// <param name="y2">The second control point's progress.</param>
    /// <param name="t">The elapsed fraction, 0 to 1.</param>
    /// <returns>The eased progress.</returns>
    /// <remarks>
    /// Solves the curve's x for the parameter by Newton's method with a bisection fallback, the way
    /// browsers do, so a curve lifted from a stylesheet moves here exactly as it did in the mock-up.
    /// </remarks>
    public static float CubicBezier(float x1, float y1, float x2, float y2, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        if (t is 0f or 1f)
        {
            return t;
        }

        float u = t;
        for (int i = 0; i < 8; i++)
        {
            float x = Bezier(u, x1, x2) - t;
            if (MathF.Abs(x) < 1e-5f)
            {
                return Bezier(u, y1, y2);
            }

            float slope = BezierSlope(u, x1, x2);
            if (MathF.Abs(slope) < 1e-6f)
            {
                break;
            }

            u -= x / slope;
        }

        float lo = 0f;
        float hi = 1f;
        u = t;
        for (int i = 0; i < 24; i++)
        {
            float x = Bezier(u, x1, x2);
            if (MathF.Abs(x - t) < 1e-5f)
            {
                break;
            }

            if (x < t)
            {
                lo = u;
            }
            else
            {
                hi = u;
            }

            u = (lo + hi) * 0.5f;
        }

        return Bezier(u, y1, y2);
    }

    private static float Bezier(float u, float p1, float p2)
    {
        float v = 1f - u;
        return (3f * v * v * u * p1) + (3f * v * u * u * p2) + (u * u * u);
    }

    private static float BezierSlope(float u, float p1, float p2)
    {
        float v = 1f - u;
        return (3f * v * v * p1) + (6f * v * u * (p2 - p1)) + (3f * u * u * (1f - p2));
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
