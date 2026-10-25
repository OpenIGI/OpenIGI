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
    private readonly int[] _parents;

    private BonePose(Matrix3f[] rotations, Vector3f[] positions, int[] parents)
    {
        _rotations = rotations;
        _positions = positions;
        _parents = parents;
    }

    /// <summary>A pose another evaluator has already composed, bone by bone.</summary>
    /// <param name="rotations">Each bone's world rotation.</param>
    /// <param name="positions">Each bone's world position.</param>
    /// <param name="parents">Each bone's parent, or <see cref="BoneSkeleton.NoParent"/>.</param>
    /// <returns>The pose, over copies of the arrays.</returns>
    /// <exception cref="ArgumentException">The arrays differ in length.</exception>
    public static BonePose FromBones(ReadOnlySpan<Matrix3f> rotations, ReadOnlySpan<Vector3f> positions, ReadOnlySpan<int> parents)
    {
        if (positions.Length != rotations.Length || parents.Length != rotations.Length)
        {
            throw new ArgumentException("One rotation, position and parent per bone.", nameof(positions));
        }

        return new BonePose(rotations.ToArray(), positions.ToArray(), parents.ToArray());
    }

    /// <summary>Gets each bone's world rotation.</summary>
    public ReadOnlySpan<Matrix3f> Rotations => _rotations;

    /// <summary>Gets each bone's world position, in engine units.</summary>
    public ReadOnlySpan<Vector3f> Positions => _positions;

    /// <summary>Gets each bone's parent, or <see cref="BoneSkeleton.NoParent"/> for the root.</summary>
    /// <remarks>
    /// Carried through from the skeleton so a consumer can treat the pose as a set of <b>segments</b>
    /// rather than a cloud of points — which is what anything measuring a distance to a limb needs.
    /// </remarks>
    public ReadOnlySpan<int> Parents => _parents;

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
        ReadOnlySpan<Quaternion32> localRotations,
        Vector3f rootPosition = default,
        Matrix3f? rootRotation = null)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        if (localRotations.Length != skeleton.BoneCount)
        {
            throw new ArgumentException(
                $"The skeleton has {skeleton.BoneCount} bones but {localRotations.Length} rotations were given.",
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
        ReadOnlySpan<int> parents = skeleton.Parents;
        ReadOnlySpan<Vector3f> rest = skeleton.RestTranslations;

        for (int bone = 0; bone < count; bone++)
        {
            int parent = parents[bone];
            Matrix3f parentRotation = parent == BoneSkeleton.NoParent ? above : rotations[parent];
            Vector3f parentPosition = parent == BoneSkeleton.NoParent ? rootPosition : positions[parent];

            // The position comes first and uses the *parent's* rotation: a bone sits at its parent's
            // origin, offset by its rest translation turned into the parent's frame.
            positions[bone] = Add(Rotate(parentRotation, rest[bone]), parentPosition);

            // The world orientation is the parent's composed with this bone's local one, parent on the
            // left. The original composes the quaternions as parent * local and converts once;
            // composing the matrices in that order is the same rotation. Reversing it is the transpose
            // trap that M6's camera fell into, so `AChainComposesParentFirst` pins the order.
            rotations[bone] = parentRotation * ToMatrix(localRotations[bone]);
        }

        return new BonePose(rotations, positions, parents.ToArray());
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
    /// <summary>Turns a bone and everything below it about the bone's own joint — <c>Bone_Rotate</c> (<c>0x4D7070</c>).</summary>
    /// <param name="bone">The bone whose joint the turn is about.</param>
    /// <param name="axis">The turn's unit axis, in the pose's own frame.</param>
    /// <param name="angle">The turn, in radians.</param>
    /// <returns>The turned pose; this one when the turn is nothing or the bone is not in the rig.</returns>
    /// <remarks>
    /// The original multiplies the turn onto the left of every world rotation in the subtree and re-seats each child on
    /// its parent's turned frame, which is the subtree turned rigidly about the bone's position; the bone itself does not
    /// move. The turn's quaternion is made the way <c>Quat_SetAxisAngle</c> (<c>0x4B4660</c>) makes it.
    /// </remarks>
    public BonePose TurnSubtree(int bone, Vector3f axis, float angle)
    {
        if (angle == 0f || (uint)bone >= (uint)_rotations.Length)
        {
            return this;
        }

        float half = angle * 0.5f;
        float sin = MathF.Sin(half);
        Matrix3f turn = ToMatrix(new Quaternion32(axis.X * sin, axis.Y * sin, axis.Z * sin, MathF.Cos(half)));

        Matrix3f[] rotations = (Matrix3f[])_rotations.Clone();
        Vector3f[] positions = (Vector3f[])_positions.Clone();
        bool[] turned = new bool[rotations.Length];
        Vector3f joint = _positions[bone];
        turned[bone] = true;
        rotations[bone] = turn * rotations[bone];
        for (int i = bone + 1; i < rotations.Length; i++)
        {
            int parent = _parents[i];
            if (parent == BoneSkeleton.NoParent || !turned[parent])
            {
                continue;
            }

            turned[i] = true;
            rotations[i] = turn * rotations[i];
            Vector3f offset = new(positions[i].X - joint.X, positions[i].Y - joint.Y, positions[i].Z - joint.Z);
            positions[i] = Add(Rotate(turn, offset), joint);
        }

        return new BonePose(rotations, positions, _parents);
    }

    private static Vector3f Rotate(Matrix3f m, Vector3f v) => new(
        (m.M11 * v.X) + (m.M12 * v.Y) + (m.M13 * v.Z),
        (m.M21 * v.X) + (m.M22 * v.Y) + (m.M23 * v.Z),
        (m.M31 * v.X) + (m.M32 * v.Y) + (m.M33 * v.Z));

    private static Vector3f Add(Vector3f left, Vector3f right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
}
