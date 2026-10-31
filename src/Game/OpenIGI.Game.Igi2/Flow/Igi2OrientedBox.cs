using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>A turned box in the level, and whether a point lies in it: the test every area of I.G.I. 2 makes.</summary>
/// <remarks>
/// <para>
/// <c>AreaActivateQTask_IsInsideArea</c> (<c>igi2 0x593190</c>) and <c>SpawnArea_IsInsideArea</c> make the same test the same way:
/// the point is taken from the box's centre into its frame, the frame's columns times the offset's axes summed z, then y, then x,
/// and it is inside when it is not beyond half the dimensions along any axis, edges included. The frame is made from the box's
/// angles and the half dimensions from its dimensions as each area's internal-data handler does.
/// </para>
/// </remarks>
public struct Igi2OrientedBox
{
    private static readonly float[] Identity = EngineEuler.Rotation(0f, 0f, 0f);

    private float[] _frame;
    private Vector3f _half;

    /// <summary>Gets the box's centre.</summary>
    public Vector3d Centre { get; private set; }

    /// <summary>Readies the box from an area's parameters.</summary>
    /// <param name="centre">The centre.</param>
    /// <param name="angles">The orientation's angles.</param>
    /// <param name="dimensions">The full dimensions.</param>
    /// <returns>The box.</returns>
    public static Igi2OrientedBox From(Vector3d centre, Vector3f angles, Vector3f dimensions) => new()
    {
        Centre = centre,
        _frame = EngineEuler.Rotation(angles.X, angles.Y, angles.Z),
        _half = new Vector3f(((Fpu)dimensions.X * 0.5).F, ((Fpu)dimensions.Y * 0.5).F, ((Fpu)dimensions.Z * 0.5).F),
    };

    /// <summary>Whether a point lies in the box.</summary>
    /// <param name="point">The point.</param>
    /// <returns><see langword="true"/> within half the dimensions along each of the box's axes, edges included.</returns>
    public readonly bool IsInside(Vector3d point) => IsInside(point, Centre);

    /// <summary>Whether a point lies in the box with its centre moved, its frame and size as they were readied.</summary>
    /// <param name="point">The point.</param>
    /// <param name="centre">The centre to measure from: an area's position now, which may have moved since it was readied.</param>
    /// <returns><see langword="true"/> within half the dimensions along each of the box's axes, edges included.</returns>
    public readonly bool IsInside(Vector3d point, Vector3d centre)
    {
        float[] m = _frame ?? Identity;
        Fpu dx = (Fpu)point.X - centre.X, dy = (Fpu)point.Y - centre.Y, dz = (Fpu)point.Z - centre.Z;
        float x = ((((Fpu)dz * m[6]) + ((Fpu)dy * m[3])) + ((Fpu)dx * m[0])).F;
        float y = ((((Fpu)dz * m[7]) + ((Fpu)dy * m[4])) + ((Fpu)dx * m[1])).F;
        float z = ((((Fpu)dz * m[8]) + ((Fpu)dy * m[5])) + ((Fpu)dx * m[2])).F;

        // Each test passes when the point is not beyond the face, which a value that is not a number never is.
        return !(x > _half.X) && !(y > _half.Y) && !(z > _half.Z)
            && !(-_half.X > x) && !(-_half.Y > y) && !(-_half.Z > z);
    }
}
