using OpenIGI.Core.Numerics;

namespace OpenIGI.Presentation.Rendering;

/// <summary>Builds the first-person model's retail projection.</summary>
/// <remarks>
/// Retail's model renderer (<c>0x49F700</c>) copies the active transform context, selects layer zero,
/// and installs it for the draw. The Direct3D setup at <c>0x493580</c> gives that layer a 204.8-unit
/// near plane, half of the 409.6-unit world layer. With 4096 game units per metre that is 0.05 m.
/// Keeping the model in this shallower foreground depth range preserves its own depth ordering. The
/// foreground pass then resets its depth before drawing the rig, so close world geometry cannot occlude it.
/// </remarks>
public static class ViewModelProjection
{
    /// <summary>The retail layer-zero near plane, in metres.</summary>
    internal const float NearPlane = 0.05f;

    /// <summary>
    /// Creates the first-person model projection for the supplied retail horizontal half-tangent.
    /// </summary>
    /// <remarks>
    /// Retail's <c>0x497D70</c> publishes independent pixel scales: the horizontal aperture is the
    /// supplied tangent and the vertical aperture is three quarters of it. This is deliberately not an
    /// aspect-correct world projection. Passing a 16:9 aspect ratio to <see cref="Matrix4f.CreatePerspective"/>
    /// widens a <c>tan(30 degrees)</c> foreground lens to about 75 degrees; retail keeps it at 60 degrees.
    /// </remarks>
    public static Matrix4f Create(float horizontalHalfTangent, float farPlane)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(horizontalHalfTangent);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(farPlane);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(farPlane, NearPlane);

        float verticalHalfTangent = 0.75f * horizontalHalfTangent;
        return new Matrix4f(
            1f / horizontalHalfTangent, 0f, 0f, 0f,
            0f, 1f / verticalHalfTangent, 0f, 0f,
            0f, 0f, (farPlane + NearPlane) / (farPlane - NearPlane),
            -2f * farPlane * NearPlane / (farPlane - NearPlane),
            0f, 0f, 1f, 0f);
    }
}
