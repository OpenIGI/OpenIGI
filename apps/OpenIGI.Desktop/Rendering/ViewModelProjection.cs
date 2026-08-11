using OpenIGI.Core.Numerics;

namespace OpenIGI.Desktop;

/// <summary>Builds the first-person model's retail projection.</summary>
/// <remarks>
/// Retail's model renderer (<c>0x49F700</c>) copies the active transform context, selects layer zero,
/// and installs it for the draw. The Direct3D setup at <c>0x493580</c> gives that layer a 204.8-unit
/// near plane, half of the 409.6-unit world layer. With 4096 game units per metre that is 0.05 m.
/// Keeping the model in this shallower foreground depth range preserves its own depth ordering while
/// preventing close world geometry from occluding it.
/// </remarks>
internal static class ViewModelProjection
{
    /// <summary>The retail layer-zero near plane, in metres.</summary>
    internal const float NearPlane = 0.05f;

    /// <summary>Creates the first-person model projection for the supplied field of view.</summary>
    public static Matrix4f Create(float verticalFieldOfView, float aspectRatio, float farPlane) =>
        Matrix4f.CreatePerspective(verticalFieldOfView, aspectRatio, NearPlane, farPlane);
}
