using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;

namespace OpenIGI.Game.Physics;

/// <summary>
/// Turns a cloud of ragdoll particles back into the per-bone rotations a skinned mesh is drawn from.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the one subtle part of the whole feature.</b> The solver knows where every bone <em>is</em>;
/// the skinner needs to know which way every bone <em>faces</em>. Two of the three degrees of freedom fall
/// out of the geometry — the direction from a bone to its child is where that bone's rest translation must
/// end up pointing — and the third, the twist about that direction, is not determined by the particles at
/// all. It is carried from the previous frame instead: the bone is turned by the <em>smallest</em> rotation
/// that satisfies the new direction, so whatever twist it had is preserved and nothing spins.
/// </para>
/// <para>
/// The result goes back through <see cref="BonePose.Evaluate"/> rather than being handed to the renderer as
/// positions. That costs a second forward pass and buys something worth far more: the drawn skeleton is
/// rebuilt from the skeleton's own rest translations, so it is <b>rigid by construction</b> and no amount
/// of solver drift can ever stretch a limb on screen.
/// </para>
/// </remarks>
public static class RagdollPose
{
    /// <summary>How near two directions must be before the turn between them is treated as nothing.</summary>
    public const float ParallelEpsilon = 1e-6f;

    /// <summary>Recomputes every bone's orientation from where the particles ended up.</summary>
    /// <param name="rig">The prepared rig.</param>
    /// <param name="position">Each bone's world position, in engine units.</param>
    /// <param name="world">Each bone's world orientation; read for its twist and rewritten.</param>
    /// <param name="local">Receives each bone's orientation relative to its parent.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rig"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A span does not match the rig.</exception>
    /// <remarks>
    /// Bones are walked in index order, which is enough because the hierarchy is topologically sorted: a
    /// bone's parent is always resolved before the bone itself. Each bone's world orientation is rebuilt
    /// from its parent's and its own freshly-normalized local quaternion, which is also what keeps the
    /// matrices orthonormal over the thousands of ticks a corpse survives.
    /// </remarks>
    public static void Resolve(
        RagdollSkeleton rig,
        ReadOnlySpan<Vector3d> position,
        Span<Matrix3f> world,
        Span<Quaternion32> local)
    {
        ArgumentNullException.ThrowIfNull(rig);
        if (position.Length != rig.BoneCount || world.Length != rig.BoneCount || local.Length != rig.BoneCount)
        {
            throw new ArgumentException(
                $"Every span must hold {rig.BoneCount} entries, one per bone.",
                nameof(position));
        }

        for (int bone = 0; bone < rig.BoneCount; bone++)
        {
            int parent = rig.Skeleton.Parents[bone];
            Matrix3f above = parent == BoneSkeleton.NoParent ? Matrix3f.Identity : world[parent];
            int child = rig.AimChildren[bone];

            if (child >= 0)
            {
                Vector3f rest = rig.Skeleton.RestTranslations[child].Normalized();
                Vector3f facing = Rotate(world[bone], rest);
                Vector3d aim = position[child] - position[bone];
                Vector3f wanted = new((float)aim.X, (float)aim.Y, (float)aim.Z);

                if (wanted.LengthSquared > 0f)
                {
                    Matrix3f swing = SwingBetween(facing, wanted.Normalized());
                    local[bone] = QuaternionOf(above.Transposed() * (swing * world[bone])).Normalized();
                }
            }

            // A bone with no child to aim at -- a hand, a foot, the head's tip -- keeps whatever local
            // orientation it had. Nothing hangs off it, so nothing measures it, and holding it still is
            // what stops the extremities of a settled corpse shimmering.
            world[bone] = above * BonePose.ToMatrix(local[bone]);
        }
    }

    /// <summary>The smallest rotation taking one direction onto another.</summary>
    /// <param name="from">The starting direction, unit length.</param>
    /// <param name="to">The direction wanted, unit length.</param>
    /// <returns>The rotation, in the <c>M · v</c> convention the bone palette uses.</returns>
    /// <remarks>
    /// The smallest one is the point: any rotation taking <paramref name="from"/> onto
    /// <paramref name="to"/> would satisfy the particles, and all of them differ by a twist about
    /// <paramref name="to"/>. Taking the one with no twist of its own is what makes the bone keep the
    /// twist it already had.
    /// </remarks>
    public static Matrix3f SwingBetween(Vector3f from, Vector3f to)
    {
        float dot = Vector3f.Dot(from, to);
        if (dot >= 1f - ParallelEpsilon)
        {
            return Matrix3f.Identity;
        }

        Vector3f axis = Vector3f.Cross(from, to);
        if (dot <= -1f + ParallelEpsilon || axis.LengthSquared <= ParallelEpsilon)
        {
            // Exactly reversed: every axis perpendicular to `from` is a valid half-turn, so pick one
            // that is definitely not parallel to it and turn about that.
            Vector3f perpendicular = MathF.Abs(from.X) < 0.9f ? Vector3f.UnitX : Vector3f.UnitY;
            axis = Vector3f.Cross(from, perpendicular).Normalized();
            return BonePose.ToMatrix(new Quaternion32(axis.X, axis.Y, axis.Z, 0f));
        }

        // The half-way quaternion: (axis, 1 + cos) normalized is the rotation through the angle between
        // the two, about their common perpendicular. No trigonometry is needed for it.
        return BonePose.ToMatrix(new Quaternion32(axis.X, axis.Y, axis.Z, 1f + dot).Normalized());
    }

    /// <summary>Reads a rotation matrix back out as the quaternion the bone palette stores.</summary>
    /// <param name="m">The rotation, in the <c>M · v</c> convention.</param>
    /// <returns>The quaternion, such that <see cref="BonePose.ToMatrix"/> returns the same rotation.</returns>
    /// <remarks>
    /// The inverse of <see cref="BonePose.ToMatrix"/>, and it has to be exactly that and not the inverse
    /// of <see cref="Quaternion32.ToMatrix"/> — the two differ by a transpose, which is the trap
    /// <c>BonePose</c> itself carries a warning about. The largest-diagonal branch is the standard
    /// numerically stable form; taking the trace branch alone loses all precision at a half turn.
    /// </remarks>
    public static Quaternion32 QuaternionOf(Matrix3f m)
    {
        float trace = m.M11 + m.M22 + m.M33;
        if (trace > 0f)
        {
            float s = MathF.Sqrt(trace + 1f) * 2f;
            return new Quaternion32(
                (m.M32 - m.M23) / s,
                (m.M13 - m.M31) / s,
                (m.M21 - m.M12) / s,
                0.25f * s);
        }

        if (m.M11 > m.M22 && m.M11 > m.M33)
        {
            float s = MathF.Sqrt(1f + m.M11 - m.M22 - m.M33) * 2f;
            return new Quaternion32(
                0.25f * s,
                (m.M12 + m.M21) / s,
                (m.M13 + m.M31) / s,
                (m.M32 - m.M23) / s);
        }

        if (m.M22 > m.M33)
        {
            float s = MathF.Sqrt(1f + m.M22 - m.M11 - m.M33) * 2f;
            return new Quaternion32(
                (m.M12 + m.M21) / s,
                0.25f * s,
                (m.M23 + m.M32) / s,
                (m.M13 - m.M31) / s);
        }

        float t = MathF.Sqrt(1f + m.M33 - m.M11 - m.M22) * 2f;
        return new Quaternion32(
            (m.M13 + m.M31) / t,
            (m.M23 + m.M32) / t,
            0.25f * t,
            (m.M21 - m.M12) / t);
    }

    private static Vector3f Rotate(in Matrix3f m, in Vector3f v) => new(
        (m.M11 * v.X) + (m.M12 * v.Y) + (m.M13 * v.Z),
        (m.M21 * v.X) + (m.M22 * v.Y) + (m.M23 * v.Z),
        (m.M31 * v.X) + (m.M32 * v.Y) + (m.M33 * v.Z));
}
