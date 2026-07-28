using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.Animation;

/// <summary>
/// A posed skeleton — the world rotation and world position of every bone, as the engine's bone matrix
/// palette holds them.
/// </summary>
/// <remarks>
/// <para>
/// This is what <c>0x4D3A90</c> fills, one 132-byte record per bone, and what the skinning path
/// (<c>0x49B700</c>) reads out of the record at byte offsets 24..71.
/// </para>
/// </remarks>
public sealed class BonePose
{
    private readonly Matrix3f[] _rotations;
    private readonly Vector3f[] _positions;

    private BonePose(Matrix3f[] rotations, Vector3f[] positions)
    {
        _rotations = rotations;
        _positions = positions;
    }

    /// <summary>Gets each bone's world rotation.</summary>
    public IReadOnlyList<Matrix3f> Rotations => _rotations;

    /// <summary>Gets each bone's world position, in engine units.</summary>
    public IReadOnlyList<Vector3f> Positions => _positions;

    /// <summary>Gets the number of bones.</summary>
    public int BoneCount => _rotations.Length;

    /// <summary>Poses a skeleton from a local rotation per bone.</summary>
    /// <param name="skeleton">The hierarchy and its rest translations.</param>
    /// <param name="localRotations">
    /// One orientation per bone, each relative to its parent — what a clip's rotation tracks yield.
    /// </param>
    /// <param name="rootPosition">Where the root sits; the clip's root-motion track supplies this.</param>
    /// <param name="rootRotation">The orientation applied above the root, usually the object's own.</param>
    /// <returns>The posed skeleton.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="localRotations"/> is the wrong length.</exception>
    /// <remarks>
    /// The original recurses down the child and sibling links; this walks the bones in index order,
    /// which is equivalent and needs no recursion because <see cref="BoneSkeleton.IsTopologicallySorted"/>
    /// holds for every retail skeleton — a parent is always at a lower index than its child.
    /// </remarks>
    public static BonePose Evaluate(
        BoneSkeleton skeleton,
        IReadOnlyList<Quaternion32> localRotations,
        Vector3f rootPosition = default,
        Matrix3f? rootRotation = null)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        ArgumentNullException.ThrowIfNull(localRotations);
        if (localRotations.Count != skeleton.BoneCount)
        {
            throw new ArgumentException(
                $"The skeleton has {skeleton.BoneCount} bones but {localRotations.Count} rotations were given.",
                nameof(localRotations));
        }

        if (!skeleton.IsTopologicallySorted())
        {
            throw new ArgumentException(
                "The skeleton's parents are not in index order, so it cannot be posed in one pass.",
                nameof(skeleton));
        }

        Matrix3f above = rootRotation ?? Matrix3f.Identity;
        int count = skeleton.BoneCount;
        Matrix3f[] rotations = new Matrix3f[count];
        Vector3f[] positions = new Vector3f[count];

        for (int bone = 0; bone < count; bone++)
        {
            int parent = skeleton.Parents[bone];
            Matrix3f parentRotation = parent == BoneSkeleton.NoParent ? above : rotations[parent];
            Vector3f parentPosition = parent == BoneSkeleton.NoParent ? rootPosition : positions[parent];

            // The position comes first and uses the *parent's* rotation: a bone sits at its parent's
            // origin, offset by its rest translation turned into the parent's frame.
            positions[bone] = Add(Rotate(parentRotation, skeleton.RestTranslations[bone]), parentPosition);

            // The world orientation is the parent's composed with this bone's local one, parent on the
            // left. The original composes the quaternions as parent * local and converts once;
            // composing the matrices in that order is the same rotation. Reversing it is the transpose
            // trap that M6's camera fell into, so `AChainComposesParentFirst` pins the order.
            rotations[bone] = parentRotation * ToMatrix(localRotations[bone]);
        }

        return new BonePose(rotations, positions);
    }

    /// <summary>Converts an orientation to the 3×3 the palette stores.</summary>
    /// <param name="q">The orientation.</param>
    /// <returns>The rotation matrix, row-major.</returns>
    /// <remarks>
    /// Transcribed from <c>0x4D3A90</c>'s tail. It is the textbook conversion for an <c>(x, y, z, w)</c>
    /// quaternion, which is a useful confirmation that the key records really are stored in that order.
    /// </remarks>
    public static Matrix3f ToMatrix(Quaternion32 q)
    {
        float xx = q.X * q.X;
        float xy = q.X * q.Y * 2f;
        float xz = q.X * q.Z * 2f;
        float xw = q.X * q.W * 2f;
        float yy = q.Y * q.Y * 2f;
        float yz = q.Y * q.Z * 2f;
        float yw = q.Y * q.W * 2f;
        float zz = q.Z * q.Z * 2f;
        float zw = q.Z * q.W * 2f;
        float oneMinusTwoXX = 1f - (xx + xx);

        return new Matrix3f(
            1f - yy - zz, xy - zw, yw + xz,
            zw + xy, oneMinusTwoXX - zz, yz - xw,
            xz - yw, yz + xw, oneMinusTwoXX - yy);
    }

    // The palette applies its 3x3 as M * v -- 0x4D3A90 dots the matrix's ROWS with the rest
    // translation. Matrix3f.Transform is the engine's other convention, v * M (the columns), which is
    // the inverse rotation; using it here would place every bone by the transpose of the rotation its
    // own matrix applies.
    private static Vector3f Rotate(Matrix3f m, Vector3f v) => new(
        (m.M11 * v.X) + (m.M12 * v.Y) + (m.M13 * v.Z),
        (m.M21 * v.X) + (m.M22 * v.Y) + (m.M23 * v.Z),
        (m.M31 * v.X) + (m.M32 * v.Y) + (m.M33 * v.Z));

    private static Vector3f Add(Vector3f left, Vector3f right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
}
