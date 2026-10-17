using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Igi1.Physics;

namespace OpenIGI.Game.Tests;

/// <summary>
/// A humanoid rig built by hand, so the ragdoll tests need no retail data and no game path.
/// </summary>
/// <remarks>
/// Sixteen bones with the shape that matters: a spine, two three-bone arms and two three-bone legs off a
/// pelvis, and — deliberately — two bones that fork into three children each. Lengths are engine units,
/// so a metre is 4096 and the figure is roughly a person.
/// </remarks>
internal static class RagdollRig
{
    /// <summary>The pelvis, which is the root and carries no rest translation.</summary>
    public const int Pelvis = 0;

    /// <summary>The lower spine.</summary>
    public const int Spine = 1;

    /// <summary>The chest, which forks into the head and both arms.</summary>
    public const int Chest = 2;

    /// <summary>The head.</summary>
    public const int Head = 3;

    /// <summary>The left upper arm.</summary>
    public const int LeftUpperArm = 4;

    /// <summary>The left forearm — the far side of the left elbow.</summary>
    public const int LeftForearm = 5;

    /// <summary>The left hand.</summary>
    public const int LeftHand = 6;

    /// <summary>The right upper arm.</summary>
    public const int RightUpperArm = 7;

    /// <summary>The right forearm.</summary>
    public const int RightForearm = 8;

    /// <summary>The right hand.</summary>
    public const int RightHand = 9;

    /// <summary>The left thigh.</summary>
    public const int LeftThigh = 10;

    /// <summary>The left shin — the far side of the left knee.</summary>
    public const int LeftShin = 11;

    /// <summary>The left foot.</summary>
    public const int LeftFoot = 12;

    /// <summary>The right thigh.</summary>
    public const int RightThigh = 13;

    /// <summary>The right shin.</summary>
    public const int RightShin = 14;

    /// <summary>The right foot.</summary>
    public const int RightFoot = 15;

    private static readonly int[] Parents =
    [
        BoneSkeleton.NoParent, Pelvis, Spine, Chest,
        Chest, LeftUpperArm, LeftForearm,
        Chest, RightUpperArm, RightForearm,
        Pelvis, LeftThigh, LeftShin,
        Pelvis, RightThigh, RightShin,
    ];

    private static readonly Vector3f[] RestTranslations =
    [
        new(0f, 0f, 0f),          // the pelvis sits on the object origin, as every retail rig's does
        new(0f, 0f, 1000f),
        new(0f, 0f, 1000f),
        new(0f, 0f, 800f),
        new(-600f, 0f, 200f),
        new(-900f, 0f, 0f),
        new(-500f, 0f, 0f),
        new(600f, 0f, 200f),
        new(900f, 0f, 0f),
        new(500f, 0f, 0f),
        new(-300f, 0f, -400f),
        new(0f, 0f, -1400f),
        new(0f, 400f, -300f),
        new(300f, 0f, -400f),
        new(0f, 0f, -1400f),
        new(0f, 400f, -300f),
    ];

    /// <summary>Builds the bone hierarchy.</summary>
    /// <returns>The skeleton.</returns>
    public static BoneSkeleton Skeleton() => BoneSkeleton.Create(Parents, RestTranslations, id: 0);

    /// <summary>Builds the rig the solver runs on, with the uniform default joint limits.</summary>
    /// <returns>The prepared rig.</returns>
    public static RagdollSkeleton Rig() => RagdollSkeleton.Build(Skeleton());

    /// <summary>Poses the rig standing, with every bone at its own rest orientation.</summary>
    /// <returns>The pose, with the pelvis at the origin.</returns>
    public static BonePose Standing()
    {
        Quaternion32[] identity = new Quaternion32[Parents.Length];
        Array.Fill(identity, Quaternion32.Identity);
        return BonePose.Evaluate(Skeleton(), identity);
    }

    /// <summary>Seeds a body from the standing pose.</summary>
    /// <param name="rig">The prepared rig.</param>
    /// <param name="origin">Where to put it, in engine units.</param>
    /// <returns>The body.</returns>
    public static RagdollBody Standing(RagdollSkeleton rig, Vector3d origin = default) =>
        RagdollBody.Seed(rig, Standing(), origin, Matrix3f.Identity);

    /// <summary>A weapon-shaped body, for the tests that need an armed corpse.</summary>
    /// <returns>The carried body — a metre of barrel, a hand's width thick.</returns>
    public static RagdollBone Weapon() =>
        RagdollWeapon.FromBounds(new Vector3f(-200f, -200f, 0f), new Vector3f(200f, 200f, 4096f));

    /// <summary>Seeds an armed body from the standing pose.</summary>
    /// <param name="rig">The prepared rig.</param>
    /// <param name="origin">Where to put it, in engine units.</param>
    /// <returns>The body, carrying a weapon in its right hand.</returns>
    public static RagdollBody Armed(RagdollSkeleton rig, Vector3d origin = default) =>
        RagdollBody.Seed(rig, Standing(), origin, Matrix3f.Identity, Weapon(), RightHand);

    /// <summary>Builds an animation set that bends one joint and leaves the rest alone.</summary>
    /// <param name="bone">Which bone the clip turns.</param>
    /// <param name="axis">The axis it turns about.</param>
    /// <param name="angle">How far, in radians.</param>
    /// <returns>The set, with one clip of two keys.</returns>
    /// <remarks>
    /// The fixture the measured-limits tests need: a corpus that demonstrates exactly one joint moving
    /// exactly one way, so what comes back out can be checked against what went in.
    /// </remarks>
    public static BoneAnimationSet Bending(int bone, Vector3f axis, float angle)
    {
        int count = Parents.Length;
        BoneRotationKey[][] tracks = new BoneRotationKey[count][];
        for (int i = 0; i < count; i++)
        {
            tracks[i] =
            [
                new BoneRotationKey(Quaternion32.Identity, 0f, Quaternion32.Identity, Quaternion32.Identity),
            ];
        }

        tracks[bone] =
        [
            new BoneRotationKey(Quaternion32.Identity, 0f, Quaternion32.Identity, Quaternion32.Identity),
            new BoneRotationKey(
                Quaternion32.FromAxisAngle(axis, angle), 160f, Quaternion32.Identity, Quaternion32.Identity),
        ];

        BoneAnimation clip = BoneAnimation.Create(id: 2, duration: 160f, rotations: tracks);

        return BoneAnimationSet.Create(Skeleton(), clip);
    }
}
