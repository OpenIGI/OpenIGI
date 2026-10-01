namespace OpenIGI.Rendering;

/// <summary>What a <see cref="DebugView"/> implies for the passes after the one that draws it.</summary>
public static class DebugViews
{
    /// <summary>Gets whether a view shows a channel being read rather than a picture being looked at.</summary>
    /// <param name="view">The view the frame shows.</param>
    /// <returns>
    /// <see langword="true"/> when what reaches the resolve is a code value, which must keep neither the
    /// curve, the lens, the film nor the grade: every one of those assumes radiance.
    /// </returns>
    /// <remarks>
    /// The two exceptions are pictures of light — the scene lit over a neutral grey, and the lamps alone —
    /// and are tone mapped like the scene they stand in for.
    /// </remarks>
    public static bool IsMask(this DebugView view) =>
        view is not DebugView.None and not DebugView.Lighting and not DebugView.LampLight;
}
