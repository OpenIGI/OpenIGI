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
