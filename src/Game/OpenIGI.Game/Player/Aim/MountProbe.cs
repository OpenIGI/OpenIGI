using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Player.Aim;

/// <summary>How a weapon is braced.</summary>
public enum MountKind
{
    /// <summary>Not braced.</summary>
    None = 0,

    /// <summary>Laid on the top of something in front: a ledge, a table, a crate, a sill.</summary>
    Top = 1,

    /// <summary>Against a corner on the left, the view leaning out round it.</summary>
    CornerLeft = 2,

    /// <summary>Against a corner on the right, the view leaning out round it.</summary>
    CornerRight = 3,
}

/// <summary>Something in front the weapon can be braced on.</summary>
/// <param name="Kind">How it is braced.</param>
/// <param name="SurfaceZ">For a top, the height of the top, in world units.</param>
/// <param name="Raise">
/// How far the eye must rise (or, negative, drop) to lay the sights just over the top, in world units -- from the crouched eye
/// when <paramref name="Crouch"/>.
/// </param>
/// <param name="Crouch">Whether the top is low enough that the body crouches to it, as a player does behind low cover.</param>
public readonly record struct MountSurface(MountKind Kind, double SurfaceZ, float Raise, bool Crouch = false);

/// <summary>Looks for something in front of the eye a weapon can be braced on, with a handful of rays.</summary>
/// <remarks>
/// <para>
/// <b>A top</b> is found by dropping rays a short way ahead of the eye, nearest first: the first that lands on a level
/// surface between hip and a little over the eye, with a face under its front edge -- something to lean over, not a floor
/// sloping away -- and with nothing between the eye and the drop, is it. The eye then rises or drops so the sights clear the
/// top by <see cref="SightClearanceMetres"/>, within what a body can reach by stretching up or bending down. A top lower than a
/// standing body can bend to is braced on from a crouch, the crouched eye rising to it -- the peek over low cover.
/// </para>
/// <para>
/// <b>A corner</b> is a wall square in front of the eye with open space beside it: the eye leans out into the open side.
/// </para>
/// </remarks>
public static class MountProbe
{
    /// <summary>How far over a top the eye settles, in metres: the sights a hand's breadth above the surface.</summary>
    public const float SightClearanceMetres = 0.09f;

    /// <summary>How far the eye may rise to see over a top, in metres.</summary>
    public const float MaxRaiseMetres = 0.30f;

    /// <summary>How far the eye may drop to lay the weapon on a low top, in metres: further, and the body crouches instead.</summary>
    public const float MaxDropMetres = 0.25f;

    private static readonly float[] TopReach = [0.30f, 0.42f, 0.55f, 0.70f];
    private const float CornerReachMetres = 0.70f;
    private const float OverTopClearMetres = 1.5f;
    private const float CornerSideMetres = 0.40f;
    private const float CornerOpenMetres = 1.0f;
    private const float LevelNormal = 0.7f;
    private const float UprightNormal = 0.5f;

    /// <summary>Finds something to brace on in front of the eye.</summary>
    /// <param name="eye">The body's eye, in world units.</param>
    /// <param name="yaw">The body's heading, radians; its forward is (sin, cos).</param>
    /// <param name="walls">The world the rays are cast against.</param>
    /// <param name="crouchDrop">How far the eye drops when the body crouches, in world units; nought for a body that is crouched.</param>
    /// <returns>The surface, or <see langword="null"/> for none.</returns>
    public static MountSurface? Find(Vector3d eye, float yaw, WorldRayCast walls, float crouchDrop = 0f)
    {
        ArgumentNullException.ThrowIfNull(walls);
        double unitsPerMetre = WorldScene.UnitsPerMetre;
        Vector3d forward = new(Math.Sin(yaw), Math.Cos(yaw), 0.0);
        Vector3d right = new(forward.Y, -forward.X, 0.0);
        return Top(eye, forward, walls, unitsPerMetre, crouchDrop) ?? Corner(eye, forward, right, walls, unitsPerMetre);
    }

    private static MountSurface? Top(Vector3d eye, Vector3d forward, WorldRayCast walls, double unitsPerMetre, float crouchDrop)
    {
        Vector3d high = eye + new Vector3d(0.0, 0.0, MaxRaiseMetres * unitsPerMetre);
        foreach (float reach in TopReach)
        {
            Vector3d ahead = high + (forward * (reach * unitsPerMetre));
            if (walls(high, ahead, out _))
            {
                // Something stands in the way at the highest the eye could rise to: no top to lean over here, or any further.
                return null;
            }

            Vector3d floor = ahead - new Vector3d(0.0, 0.0, ((MaxRaiseMetres + 0.9) * unitsPerMetre) + crouchDrop);
            if (!walls(ahead, floor, out WorldRayHit top) || top.Normal.Z < LevelNormal)
            {
                continue;
            }

            float raise = (float)(top.Position.Z + (SightClearanceMetres * unitsPerMetre) - eye.Z);
            bool crouch = false;
            if (raise < -MaxDropMetres * unitsPerMetre && crouchDrop > 0f)
            {
                raise += crouchDrop;
                crouch = true;
            }

            if (raise > MaxRaiseMetres * unitsPerMetre || raise < -MaxDropMetres * unitsPerMetre)
            {
                continue;
            }

            // A face just under the front edge: the top is something's top, not the floor running on.
            Vector3d under = new(eye.X, eye.Y, top.Position.Z - (0.05 * unitsPerMetre));
            // And room to look over it: a step at a wall's foot is a top, but sights laid on it look at the wall.
            Vector3d sights = new(eye.X, eye.Y, top.Position.Z + (SightClearanceMetres * unitsPerMetre));
            if (walls(under, under + (forward * ((reach + 0.1) * unitsPerMetre)), out WorldRayHit face) && MathF.Abs(face.Normal.Z) < UprightNormal
                && !walls(sights, sights + (forward * ((reach + OverTopClearMetres) * unitsPerMetre)), out _))
            {
                return new MountSurface(MountKind.Top, top.Position.Z, raise, crouch);
            }
        }

        return null;
    }

    private static MountSurface? Corner(Vector3d eye, Vector3d forward, Vector3d right, WorldRayCast walls, double unitsPerMetre)
    {
        if (!walls(eye, eye + (forward * (CornerReachMetres * unitsPerMetre)), out WorldRayHit wall) || MathF.Abs(wall.Normal.Z) > UprightNormal)
        {
            return null;
        }

        foreach ((MountKind kind, double side) in ((MountKind, double)[])[(MountKind.CornerRight, 1.0), (MountKind.CornerLeft, -1.0)])
        {
            Vector3d beside = eye + (right * (side * CornerSideMetres * unitsPerMetre));
            if (!walls(eye, beside, out _) && !walls(beside, beside + (forward * (CornerOpenMetres * unitsPerMetre)), out _))
            {
                return new MountSurface(kind, 0.0, 0f);
            }
        }

        return null;
    }
}
