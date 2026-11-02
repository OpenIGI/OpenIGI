using OpenIGI.Core.Numerics;
using OpenIGI.Host.Editor;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Viewport;

/// <summary>Projects world points to the window through the last frame's view, the way the renderer drew them.</summary>
/// <param name="view">The frame's view.</param>
internal readonly struct ViewportProjector(EditorView view)
{
    /// <summary>Gets the eye.</summary>
    public Vector3d Eye => view.Eye;

    /// <summary>Gets whether the view has been rendered at least once.</summary>
    public bool IsValid => view.Width > 0f && view.Height > 0f;

    /// <summary>Projects a point in metres to window pixels.</summary>
    /// <param name="point">The point, in world metres.</param>
    /// <param name="screen">Receives the window position.</param>
    /// <returns><see langword="false"/> when the point is behind the eye.</returns>
    public bool TryProject(Vector3d point, out UiVec2 screen)
    {
        Matrix4f m = view.ViewProjection;
        float x = (float)(point.X - view.Eye.X);
        float y = (float)(point.Y - view.Eye.Y);
        float z = (float)(point.Z - view.Eye.Z);
        float cx = (m.M11 * x) + (m.M12 * y) + (m.M13 * z) + m.M14;
        float cy = (m.M21 * x) + (m.M22 * y) + (m.M23 * z) + m.M24;
        float cw = (m.M41 * x) + (m.M42 * y) + (m.M43 * z) + m.M44;
        if (cw <= 1e-4f)
        {
            screen = default;
            return false;
        }

        screen = new UiVec2(((cx / cw) + 1f) * 0.5f * view.Width, (1f - (cy / cw)) * 0.5f * view.Height);
        return true;
    }

    /// <summary>Gets the ray from the eye through a window pixel.</summary>
    /// <param name="screen">The pixel.</param>
    /// <param name="direction">Receives the ray's direction, unit length, in world space.</param>
    /// <returns><see langword="false"/> before the view has been rendered.</returns>
    /// <remarks>
    /// The view-projection is relative to the eye, so every point of the ray satisfies two plane equations through the
    /// eye: clip X equal to the pixel's X times clip W, and the same for Y. The ray runs along both planes, which is
    /// their normals' cross product, turned to face forward (positive W).
    /// </remarks>
    public bool TryRay(UiVec2 screen, out Vector3d direction)
    {
        direction = default;
        if (!IsValid)
        {
            return false;
        }

        Matrix4f m = view.ViewProjection;
        double nx = ((screen.X / view.Width) * 2.0) - 1.0;
        double ny = 1.0 - ((screen.Y / view.Height) * 2.0);
        Vector3d a = new(m.M11 - (nx * m.M41), m.M12 - (nx * m.M42), m.M13 - (nx * m.M43));
        Vector3d b = new(m.M21 - (ny * m.M41), m.M22 - (ny * m.M42), m.M23 - (ny * m.M43));
        Vector3d along = Vector3d.Cross(a, b);
        double length = along.Length;
        if (length <= 1e-12)
        {
            return false;
        }

        along /= length;
        double w = (m.M41 * along.X) + (m.M42 * along.Y) + (m.M43 * along.Z);
        direction = w < 0 ? -along : along;
        return true;
    }

    /// <summary>Gets the world length that projects to a number of pixels at a point, for screen-sized gizmos.</summary>
    /// <param name="point">The point, in metres.</param>
    /// <param name="pixels">The on-screen length wanted.</param>
    /// <returns>The world length in metres.</returns>
    public double WorldSizeForPixels(Vector3d point, float pixels)
    {
        if (!TryProject(point, out UiVec2 a))
        {
            return 1.0;
        }

        // Measured along the view's up so the answer does not depend on which axis the gizmo points along.
        double distance = Math.Max(0.1, (point - view.Eye).Length);
        Vector3d probe = new(point.X, point.Y, point.Z + (distance * 0.01));
        if (!TryProject(probe, out UiVec2 b))
        {
            return distance * 0.1;
        }

        float perHundredth = MathF.Max(0.001f, MathF.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y))));
        return distance * 0.01 * (pixels / perHundredth);
    }
}
