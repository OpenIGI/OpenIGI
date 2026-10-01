using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>
/// One slice of the view distance, and the light's-eye view fitted to it.
/// </summary>
/// <param name="LightRotation">
/// The world-to-light rotation, rows being the light's axes — the same convention
/// <see cref="Camera3D.Orientation"/> uses.
/// </param>
/// <param name="Projection">The orthographic projection covering this slice.</param>
/// <param name="Centre">
/// The slice's bounding-sphere centre, <b>relative to the camera</b>. World positions run into the
/// thousands and a shadow matrix is single precision, so the camera offset is kept out of it for the
/// same reason <c>Camera3D.CreateModelView</c> keeps it out of the model-view.
/// </param>
/// <param name="Radius">The slice's bounding-sphere radius in metres.</param>
/// <param name="SplitDistance">The view depth at which this cascade stops and the next begins.</param>
/// <param name="TexelSize">How much world space one shadow texel covers, in metres.</param>
public readonly record struct ShadowCascade(
    Matrix3f LightRotation,
    Matrix4f Projection,
    Vector3f Centre,
    float Radius,
    float SplitDistance,
    float TexelSize)
{
    /// <summary>Transforms a camera-relative world position into this cascade's clip space.</summary>
    /// <param name="cameraRelativePosition">The position, relative to the camera.</param>
    /// <returns>The position in the cascade's clip space.</returns>
    public Vector3f ToLightClip(Vector3f cameraRelativePosition)
    {
        Vector3f lightSpace = LightRotation.Transposed().Transform(cameraRelativePosition);
        return Projection.TransformPoint(lightSpace);
    }
}
