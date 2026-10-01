namespace OpenIGI.Rendering;

/// <summary>Shared bounds for the compact two-channel object-motion attachment.</summary>
public static class MotionVectorEncoding
{
    /// <summary>An out-of-screen vector that marks a pixel as having no valid history.</summary>
    public const float NoHistorySentinel = 2f;

    /// <summary>Returns whether a sampled vector is outside the representable screen-motion range.</summary>
    public static bool IsNoHistory(float x, float y) => MathF.Abs(x) > 1f || MathF.Abs(y) > 1f;
}
