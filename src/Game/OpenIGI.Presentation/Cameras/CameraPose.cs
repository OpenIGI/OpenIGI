using OpenIGI.Core.Numerics;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Cameras;

/// <summary>
/// A camera as a position and two angles, which is the form every camera in this game actually keeps and
/// the only form that can be interpolated safely.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Camera3D"/> holds an orientation matrix, and blending two of those directly does not work
/// for anything but a very small angle: the obvious row-by-row lerp leaves a matrix that is no longer a
/// rotation, and re-orthonormalising it does not recover the turn that was meant — which is exactly why
/// <see cref="Interpolation.InterpolateRotation"/> documents itself as small-angle only. A cinematic that
/// swings the camera from an orbit to straight down covers about eighty degrees, so it interpolates the
/// angles and rebuilds the matrix instead.
/// </para>
/// <para>
/// That is sound here because <see cref="Camera3D.LookRotation"/> is itself a pure function of a yaw and
/// a pitch, so the round trip through this type is exact up to floating-point error. <c>DeathCamera</c>
/// and <c>InteractionCamera</c> already recover the same pair of angles from a look direction by
/// hand; this is that idiom, named.
/// </para>
/// <para>
/// The angles are the world's Z-up convention: yaw turns about Z with zero looking along +Y, and pitch is
/// positive looking up.
/// </para>
/// </remarks>
/// <param name="Position">The eye, in metres.</param>
/// <param name="Yaw">The heading in radians; zero looks along +Y.</param>
/// <param name="Pitch">The elevation in radians; positive looks up.</param>
public readonly record struct CameraPose(Vector3d Position, float Yaw, float Pitch)
{
    /// <summary>Recovers the pose a camera is in.</summary>
    /// <param name="camera">The camera to read.</param>
    /// <returns>Its position and angles.</returns>
    /// <remarks>
    /// The orientation's third row is the view's forward axis in world space, which
    /// <see cref="Camera3D.LookRotation"/> builds as <c>(sin y cos p, cos y cos p, sin p)</c> — so the
    /// pitch comes straight off Z and the yaw off the other two. Clamped before the arc sine because a
    /// forward axis that has drifted a hair outside the unit sphere would otherwise come back as NaN and
    /// take the whole frame's camera with it.
    /// </remarks>
    public static CameraPose FromCamera(in Camera3D camera)
    {
        Vector3f forward = camera.Orientation.Row3;
        float pitch = MathF.Asin(Math.Clamp(forward.Z, -1f, 1f));
        float yaw = MathF.Atan2(forward.X, forward.Y);
        return new CameraPose(camera.Position, yaw, pitch);
    }

    /// <summary>Builds the camera this pose describes.</summary>
    /// <returns>The camera.</returns>
    public Camera3D ToCamera() => new(Position, Camera3D.LookRotation(Yaw, Pitch));

    /// <summary>Interpolates between two poses, turning the short way round.</summary>
    /// <param name="from">The pose at <paramref name="t"/> = 0.</param>
    /// <param name="to">The pose at <paramref name="t"/> = 1.</param>
    /// <param name="t">The blend factor.</param>
    /// <returns>The blended pose.</returns>
    /// <remarks>
    /// At <paramref name="t"/> = 1 this returns <paramref name="to"/> <b>verbatim</b> rather than the
    /// arithmetic result. That is not tidiness: <c>from + (to - from) * 1f</c> is not bit-identical to
    /// <c>to</c> in floating point, and the whole no-pop guarantee at the end of the mission entry rests
    /// on the last cinematic frame being the same camera as the first gameplay frame.
    /// </remarks>
    public static CameraPose Lerp(in CameraPose from, in CameraPose to, float t)
    {
        if (t >= 1f)
        {
            return to;
        }

        if (t <= 0f)
        {
            return from;
        }

        return new CameraPose(
            new Vector3d(
                from.Position.X + ((to.Position.X - from.Position.X) * t),
                from.Position.Y + ((to.Position.Y - from.Position.Y) * t),
                from.Position.Z + ((to.Position.Z - from.Position.Z) * t)),
            LerpAngle(from.Yaw, to.Yaw, t),
            from.Pitch + ((to.Pitch - from.Pitch) * t));
    }

    /// <summary>Interpolates an angle the short way round the circle.</summary>
    /// <param name="from">The angle at <paramref name="t"/> = 0, in radians.</param>
    /// <param name="to">The angle at <paramref name="t"/> = 1, in radians.</param>
    /// <param name="t">The blend factor.</param>
    /// <returns>The blended angle.</returns>
    /// <remarks>
    /// Without the wrap a camera at 170 degrees turning to −170 would sweep 340 degrees the wrong way
    /// round rather than 20 degrees the right way. <see cref="MathF.IEEERemainder(float, float)"/> folds
    /// the difference into <c>[-pi, pi]</c>, which is that shorter way.
    /// </remarks>
    public static float LerpAngle(float from, float to, float t) =>
        from + (MathF.IEEERemainder(to - from, MathF.Tau) * t);
}
