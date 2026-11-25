using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Player.Aim;

/// <summary>The move that brings a held weapon's sights onto the eye's line of sight.</summary>
/// <remarks>
/// <para>
/// The rig is posed in the eye's frame -- x right, y ahead, z up, as the original's view model is -- and hangs the weapon off
/// one bone. Its two sights, given in that bone's frame, are carried into the eye's; the move turns the line through them onto
/// the y axis and then shifts the rear sight to <see cref="AdsProfile.EyeReliefMetres"/> ahead of the eye. Both the world's and
/// the view model's projections are centred on that axis, so a sight on it sits in the middle of the screen at any field of
/// view.
/// </para>
/// <para>
/// A weapon's clip moves the bone every tick -- a breath at rest, a kick on every shot. The move is therefore worked out twice:
/// once for the weapon's still stand pose (<see cref="Reference"/>), and once for the pose being drawn. Blending the two by the
/// profile's <see cref="AdsProfile.Steadiness"/> keeps that share of the clip's motion off the sight picture.
/// </para>
/// </remarks>
public static class SightAlignment
{
    private static readonly Vector3f Ahead = new(0f, 1f, 0f);

    /// <summary>The move that aligns a weapon held on a bone at this pose.</summary>
    /// <param name="profile">The weapon's profile.</param>
    /// <param name="hold">The rig's hold offset, its root-motion track, in world units in the eye's frame.</param>
    /// <param name="bonePosition">The weapon bone's position from the rig's origin, in world units.</param>
    /// <param name="boneRotation">The weapon bone's rotation, applied as <c>M · v</c>.</param>
    /// <returns>The move, in world units.</returns>
    public static RigOffset Solve(AdsProfile profile, Vector3f hold, Vector3f bonePosition, in Matrix3f boneRotation)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Kind == AdsKind.Focus)
        {
            return new RigOffset(Matrix3f.Identity, profile.FocusOffsetMetres * (float)WorldScene.UnitsPerMetre);
        }

        float unitsPerMetre = (float)WorldScene.UnitsPerMetre;
        Vector3f bone = hold + bonePosition;
        Vector3f rear = bone + RigOffset.Multiply(boneRotation, profile.RearSight * unitsPerMetre);
        Vector3f front = bone + RigOffset.Multiply(boneRotation, profile.FrontSight * unitsPerMetre);

        Matrix3f turn = Turn(front - rear, Ahead);
        Vector3f relief = new(0f, profile.EyeReliefMetres * unitsPerMetre, 0f);
        return new RigOffset(turn, relief - RigOffset.Multiply(turn, rear));
    }

    /// <summary>The move that aligns a weapon at its own stand pose: its stand clip's first frame, still.</summary>
    /// <param name="profile">The weapon's profile.</param>
    /// <param name="set">The view rig's animation set.</param>
    /// <param name="standAnimation">The weapon's stand clip.</param>
    /// <returns>The move, or the identity when the set has no such clip.</returns>
    public static RigOffset Reference(AdsProfile profile, BoneAnimationSet set, int standAnimation)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(set);
        BoneObject bones = new(set);
        if (bones.Play(0, standAnimation, loops: true) is null)
        {
            return RigOffset.Identity;
        }

        BonePose pose = bones.Pose(Matrix3f.Identity);
        int bone = HumanAttachPoints.FirstPersonWeapon;
        return bone < pose.BoneCount
            ? Solve(profile, bones.SampleRootMotion(), pose.Positions[bone], pose.Rotations[bone])
            : RigOffset.Identity;
    }

    // The shortest turn that takes one direction onto another.
    private static Matrix3f Turn(Vector3f from, Vector3f to)
    {
        float length = from.Length;
        if (length <= 1e-6f)
        {
            return Matrix3f.Identity;
        }

        Vector3f direction = from / length;
        Vector3f axis = Vector3f.Cross(direction, to);
        float sin = axis.Length;
        float cos = Vector3f.Dot(direction, to);
        return sin <= 1e-7f
            ? Matrix3f.Identity
            : Matrix3f.RotationAxis(new Vector3d(axis.X, axis.Y, axis.Z), SimMathF.Atan2(sin, cos));
    }
}
