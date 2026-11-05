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
    /// aspect-correct world projection: at 16:9 retail draws the rig a third wider than it is tall.
    /// <para>
    /// With <see cref="WidescreenLens.HorPlus"/> the lens is fitted to the screen as the world's is instead: the
    /// vertical aperture stays the retail three quarters (so the glove still sits at the bottom edge) and the
    /// horizontal one widens with the screen, so the rig keeps its shape and lines up with the world behind it.
    /// At 4:3 the two are the same.
    /// </para>
    /// </remarks>
    /// <param name="horizontalHalfTangent">The rig's authored horizontal half-angle tangent, across 4:3.</param>
    /// <param name="farPlane">The far plane, in metres.</param>
    /// <param name="aspectRatio">The screen's width over its height.</param>
    public static Matrix4f Create(float horizontalHalfTangent, float farPlane, float aspectRatio)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(horizontalHalfTangent);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(farPlane);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(farPlane, NearPlane);

        float shownHalfTangent = horizontalHalfTangent;
        float verticalHalfTangent = 0.75f * horizontalHalfTangent;
        if (WidescreenLens.HorPlus && aspectRatio > 0f)
        {
            shownHalfTangent = WidescreenLens.HorizontalTangent(horizontalHalfTangent, aspectRatio);
            verticalHalfTangent = shownHalfTangent / aspectRatio;
        }

        return new Matrix4f(
            1f / shownHalfTangent, 0f, 0f, 0f,
            0f, 1f / verticalHalfTangent, 0f, 0f,
            0f, 0f, (farPlane + NearPlane) / (farPlane - NearPlane),
            -2f * farPlane * NearPlane / (farPlane - NearPlane),
            0f, 0f, 1f, 0f);
    }
}
