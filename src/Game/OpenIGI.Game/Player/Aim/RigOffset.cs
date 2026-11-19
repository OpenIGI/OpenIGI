using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Player.Aim;

/// <summary>A rigid move of the first-person rig in the eye's own frame: a turn, then a shift, in world units.</summary>
/// <param name="Rotation">The turn, applied to a column vector (<c>M · v</c>) in the eye's frame -- x right, y ahead, z up.</param>
/// <param name="Translation">The shift after it, in world units in the same frame.</param>
/// <remarks>
/// The rig is drawn at <c>eye + look · p</c> for each of its points <c>p</c> in the eye's frame. Moved by this it is drawn at
/// <c>eye + look · (R · p + T)</c>: so its origin goes to <c>eye + look · (R · hold + T)</c> and its orientation to
/// <c>look · R</c>, and every bone hung off it follows without being touched.
/// </remarks>
public readonly record struct RigOffset(Matrix3f Rotation, Vector3f Translation)
{
    /// <summary>Gets the offset that moves nothing.</summary>
    public static RigOffset Identity { get; } = new(Matrix3f.Identity, default);

    /// <summary>Moves a point in the eye's frame.</summary>
    /// <param name="point">The point.</param>
    /// <returns>Where the offset puts it.</returns>
    public Vector3f Apply(Vector3f point) => Multiply(Rotation, point) + Translation;

    /// <summary>This offset followed by another, as one.</summary>
    /// <param name="outer">The offset applied after this one.</param>
    /// <returns><c>outer(this(p))</c> for every point.</returns>
    public RigOffset Then(RigOffset outer) =>
        new(outer.Rotation * Rotation, Multiply(outer.Rotation, Translation) + outer.Translation);

    /// <summary>Blends two offsets: the shifts in a straight line, the turns about the shortest way.</summary>
    /// <param name="from">The offset at nought.</param>
    /// <param name="to">The offset at one.</param>
    /// <param name="amount">How far from one to the other.</param>
    /// <returns>The blend.</returns>
    public static RigOffset Blend(RigOffset from, RigOffset to, float amount) =>
        amount <= 0f ? from
        : amount >= 1f ? to
        : new RigOffset(
            Interpolation.InterpolateRotation(from.Rotation, to.Rotation, amount),
            Vector3f.Lerp(from.Translation, to.Translation, amount));

    /// <summary>A column vector turned by a matrix, <c>M · v</c>: the convention the rig's palette uses.</summary>
    /// <param name="matrix">The matrix.</param>
    /// <param name="vector">The vector.</param>
    /// <returns>The product.</returns>
    public static Vector3f Multiply(in Matrix3f matrix, Vector3f vector) => new(
        (matrix.M11 * vector.X) + (matrix.M12 * vector.Y) + (matrix.M13 * vector.Z),
        (matrix.M21 * vector.X) + (matrix.M22 * vector.Y) + (matrix.M23 * vector.Z),
        (matrix.M31 * vector.X) + (matrix.M32 * vector.Y) + (matrix.M33 * vector.Z));
}
