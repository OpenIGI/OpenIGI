using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud;

/// <summary>Takes a point of the world to the pixel it is drawn at, as the frame's own projection takes it.</summary>
/// <remarks>
/// The world-to-view step is the camera's own (<see cref="Camera3D.CreateModelView(Vector3d)"/>): the subtraction in double
/// precision, then the orientation's rows. The projection is applied to a column vector, as
/// <see cref="Matrix4f.CreateProjection"/> lays it out, and its device coordinates are read with +Y up: that is what puts a
/// view point at the original's own pixel, the centre plus each scale times the coordinate over the depth.
/// </remarks>
public static class HudProjection
{
    /// <summary>Finds where a point of the world is on the screen.</summary>
    /// <param name="eye">Where the view is from.</param>
    /// <param name="projection">The view's projection.</param>
    /// <param name="world">The point, in world units.</param>
    /// <param name="width">The screen's width, in pixels.</param>
    /// <param name="height">The screen's height, in pixels.</param>
    /// <returns>Its position in pixels from the top-left corner, or <see langword="null"/> when it is behind the eye.</returns>
    public static UiVec2? ToScreen(in Camera3D eye, in Matrix4f projection, Vector3d world, float width, float height)
    {
        Matrix4f view = eye.CreateModelView(world);
        float x = view.M14;
        float y = view.M24;
        float z = view.M34;
        float clipX = (projection.M11 * x) + (projection.M12 * y) + (projection.M13 * z) + projection.M14;
        float clipY = (projection.M21 * x) + (projection.M22 * y) + (projection.M23 * z) + projection.M24;
        float clipW = (projection.M41 * x) + (projection.M42 * y) + (projection.M43 * z) + projection.M44;
        if (clipW <= 1e-6f)
        {
            return null;
        }

        float ndcX = clipX / clipW;
        float ndcY = clipY / clipW;
        return new UiVec2((ndcX + 1f) * 0.5f * width, (1f - ndcY) * 0.5f * height);
    }
}
