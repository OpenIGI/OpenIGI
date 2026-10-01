using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>How much light the level's lamps put on the first-person rig.</summary>
/// <remarks>
/// <para>
/// The lamps reach the renderers in VIEW space -- that is how the deferred shading wants them -- and the
/// rig sits at the eye, so the sample point is very nearly the view-space origin. A hand's length in front
/// of it, because the arms are, and because a fitting directly overhead otherwise reads as being exactly
/// at the sample point.
/// </para>
/// <para>
/// The falloff is the same windowed inverse square the shading applies, deliberately: the arms and the
/// wall behind them should agree about how bright the room is. What it does NOT do is trace -- the rig is
/// lit by a two-light studio setup that has never cast a shadow, and a shadow ray per lamp per frame to
/// shade two hands would cost more than the whole rest of the rig. Shared so both renderers light the rig
/// alike.
/// </para>
/// </remarks>
public static class LampFill
{
    /// <summary>The lamps' light a hand's length in front of the eye, in the same units as the sun.</summary>
    /// <param name="lamps">The frame's lamps, in view space.</param>
    /// <returns>The summed light.</returns>
    public static Vector3f AtEye(ReadOnlySpan<DeferredLocalLight> lamps)
    {
        // View space runs +Z into the scene, so this is a little way ahead of the camera.
        const float Ahead = 0.5f;

        Vector3f total = default;
        foreach (DeferredLocalLight lamp in lamps)
        {
            float dx = lamp.ViewPosition.X;
            float dy = lamp.ViewPosition.Y;
            float dz = lamp.ViewPosition.Z - Ahead;
            float distanceSq = (dx * dx) + (dy * dy) + (dz * dz);
            float reach = lamp.Radius;
            if (reach <= 0f || distanceSq >= reach * reach)
            {
                continue;
            }

            float ratio = distanceSq / (reach * reach);
            float window = Math.Clamp(1f - (ratio * ratio), 0f, 1f);
            window *= window;

            // The same soft core the shader uses, for the same reason: the position is a fixture's
            // centroid rather than a point, so standing at one must not divide by nothing.
            float attenuation = window / (distanceSq + 1f);
            total = new Vector3f(
                total.X + (lamp.Colour.X * lamp.Intensity * attenuation),
                total.Y + (lamp.Colour.Y * lamp.Intensity * attenuation),
                total.Z + (lamp.Colour.Z * lamp.Intensity * attenuation));
        }

        return total;
    }
}
