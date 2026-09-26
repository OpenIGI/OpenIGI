using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>The absolute world transform a traced world places a draw at, recovered from its model-view.</summary>
/// <remarks>
/// The game hands every draw a model-view, camera-relative by construction; a traced world holds its
/// placements in absolute world space, because a structure built this frame is traced again the next from
/// a camera somewhere else. Undoing the view needs the frame's own rotation and position, which is why only
/// a draw made with the frame's camera -- the G-buffer and the first-person rig, not a cascade or a card --
/// can be placed at all. Written once so both renderers place the same draw at the same bits.
/// </remarks>
public static class TracedWorldTransform
{
    /// <summary>Composes a draw's object-to-world transform.</summary>
    /// <param name="modelView">The draw's object-to-view transform.</param>
    /// <param name="viewToWorld">The frame's view-to-world rotation.</param>
    /// <param name="cameraPosition">Where the camera is, in absolute world space.</param>
    /// <returns>The object-to-world transform.</returns>
    public static Matrix4f From(in Matrix4f modelView, in Matrix3f viewToWorld, Vector3f cameraPosition)
    {
        Matrix3f modelViewRotation = new(
            modelView.M11, modelView.M12, modelView.M13,
            modelView.M21, modelView.M22, modelView.M23,
            modelView.M31, modelView.M32, modelView.M33);
        Matrix3f normalMatrix = viewToWorld * modelViewRotation;

        Matrix3f m = viewToWorld;
        float tx = (m.M11 * modelView.M14) + (m.M12 * modelView.M24) + (m.M13 * modelView.M34);
        float ty = (m.M21 * modelView.M14) + (m.M22 * modelView.M24) + (m.M23 * modelView.M34);
        float tz = (m.M31 * modelView.M14) + (m.M32 * modelView.M24) + (m.M33 * modelView.M34);

        return new Matrix4f(
            normalMatrix.M11, normalMatrix.M12, normalMatrix.M13, tx + cameraPosition.X,
            normalMatrix.M21, normalMatrix.M22, normalMatrix.M23, ty + cameraPosition.Y,
            normalMatrix.M31, normalMatrix.M32, normalMatrix.M33, tz + cameraPosition.Z,
            0f, 0f, 0f, 1f);
    }
}
