using OpenIGI.Core.Numerics;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered;

/// <summary>Takes a point of the level to where it is on the screen, as the player's view sees it.</summary>
/// <param name="Eye">Where the view is from, in the level's units.</param>
/// <param name="Right">The view's right, a unit vector.</param>
/// <param name="Up">The view's up, a unit vector.</param>
/// <param name="Forward">Where the view looks, a unit vector.</param>
/// <param name="TanX">The tangent of half the view's width: how far right the screen's edge is, a step ahead.</param>
/// <param name="Width">The screen's width in pixels.</param>
/// <param name="Height">The screen's height in pixels.</param>
public readonly record struct HudProjector(Vector3d Eye, Vector3f Right, Vector3f Up, Vector3f Forward, float TanX, float Width, float Height)
{
    /// <summary>Where a point is on the screen, and whether it is ahead of the view at all.</summary>
    /// <param name="world">The point, in the level's units.</param>
    /// <param name="screen">Receives its place on the screen in pixels; for a point behind, the place it is mirrored to.</param>
    /// <returns>Whether it is ahead of the view.</returns>
    public bool TryProject(Vector3d world, out UiVec2 screen)
    {
        double dx = world.X - Eye.X, dy = world.Y - Eye.Y, dz = world.Z - Eye.Z;
        double x = (dx * Right.X) + (dy * Right.Y) + (dz * Right.Z);
        double y = (dx * Up.X) + (dy * Up.Y) + (dz * Up.Z);
        double z = (dx * Forward.X) + (dy * Forward.Y) + (dz * Forward.Z);
        double depth = Math.Max(Math.Abs(z), 1e-3);
        float tanY = TanX * Height / Math.Max(1f, Width);
        float cx = Width * 0.5f, cy = Height * 0.5f;
        screen = new UiVec2(cx + (float)(x / depth / TanX * cx), cy - (float)(y / depth / tanY * cy));
        return z > 0.0;
    }

    /// <summary>A view from yaw and pitch, as Project I.G.I.'s camera faces: forward is (sin yaw, cos yaw) on the ground.</summary>
    /// <param name="eye">Where the view is from.</param>
    /// <param name="yaw">The view's turn, in radians.</param>
    /// <param name="pitch">The view's tilt, in radians, up positive.</param>
    /// <param name="tanX">The tangent of half the view's width.</param>
    /// <param name="width">The screen's width in pixels.</param>
    /// <param name="height">The screen's height in pixels.</param>
    /// <returns>The projector.</returns>
    public static HudProjector FromYawPitch(Vector3d eye, float yaw, float pitch, float tanX, float width, float height)
    {
        float cp = MathF.Cos(pitch);
        Vector3f forward = new(MathF.Sin(yaw) * cp, MathF.Cos(yaw) * cp, MathF.Sin(pitch));
        Vector3f right = new(MathF.Cos(yaw), -MathF.Sin(yaw), 0f);
        Vector3f up = new(
            (right.Y * forward.Z) - (right.Z * forward.Y),
            (right.Z * forward.X) - (right.X * forward.Z),
            (right.X * forward.Y) - (right.Y * forward.X));
        return new HudProjector(eye, right, up, forward, tanX, width, height);
    }
}
