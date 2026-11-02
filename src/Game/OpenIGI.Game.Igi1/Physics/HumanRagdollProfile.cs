using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;

namespace OpenIGI.Game.Igi1.Physics;

/// <summary>The ragdoll of the 33-bone human rig every soldier and the player wear.</summary>
/// <remarks>
/// <para>
/// <b>The bone roles are read, not guessed.</b> The tree itself is checked bone for bone before this is
/// used (<see cref="Matches"/>), and the roles come from three places in the original that each name part
/// of it. The hit-zone table at <c>0x5412A4</c> groups the thirty non-weapon bones as 4 / 4 / 6 / 6 / 5 /
/// 5 — trunk, head, two arms, two legs — with zone numbers that run from the shoulder down each limb.
/// <c>Human_GetHitZone</c> (<c>0x489C60</c>) measures a headshot from bone 4. And the weapon chain is
/// 20–22 hanging off bone 1, with 22 the gun hand (<c>HumanAttachPoints.ThirdPersonWeapon</c>). The rest
/// translations settle the remainder: the toes point along +Y, so +Y is forward, and with Z up +X is the
/// body's right — which makes 8–13 the left arm and 23–27 the left leg.
/// </para>
/// <para>
/// <b>The masses are anthropometric.</b> Segment fractions of total body mass after Dempster, as tabulated
/// in D. A. Winter, <i>Biomechanics and Motor Control of Human Movement</i> (4th ed., 2009), table 4.1:
/// head and neck 8.1%, thorax 21.6%, abdomen 13.9%, pelvis 14.2%, upper arm 2.8%, forearm 1.6%, hand
/// 0.6%, thigh 10%, leg 4.65%, foot 1.45%. A hand weighing a fortieth of the chest it hangs off is what a
/// body is; flattening that so the solver had an easier time was why corpses read as weightless.
/// </para>
/// <para>
/// <b>The ranges of motion are a living adult's</b>, from the usual clinical tables (the American Academy of
/// Orthopaedic Surgeons' averages): a knee flexes to about 140° and does not extend past straight, an
/// elbow to about 145°, a hip swings well forwards and little backwards. A corpse goes a little past a
/// living range, never far past it. Which way each hinge bends is not taken from the table but measured
/// from the rig's own animations (<see cref="RagdollLimits"/>), and so is the centre of each cone.
/// </para>
/// </remarks>
public static class HumanRagdollProfile
{
    /// <summary>The bones of the human rig.</summary>
    public const int BoneCount = 33;

    /// <summary>The pelvis, the root.</summary>
    public const int Pelvis = 0;

    /// <summary>The lower spine.</summary>
    public const int Spine = 1;

    /// <summary>The upper spine, carried by the lower: it holds eight vertices and no limb.</summary>
    public const int UpperSpine = 2;

    /// <summary>The ribcage, which both arms and the head hang off.</summary>
    public const int Chest = 3;

    /// <summary>The head — the bone retail measures a headshot from.</summary>
    public const int Head = 4;

    /// <summary>The left upper arm.</summary>
    public const int LeftUpperArm = 8;

    /// <summary>The left forearm.</summary>
    public const int LeftForearm = 9;

    /// <summary>The left hand.</summary>
    public const int LeftHand = 10;

    /// <summary>The right upper arm.</summary>
    public const int RightUpperArm = 14;

    /// <summary>The right forearm.</summary>
    public const int RightForearm = 15;

    /// <summary>The right hand.</summary>
    public const int RightHand = 16;

    /// <summary>The left thigh.</summary>
    public const int LeftThigh = 23;

    /// <summary>The left shin.</summary>
    public const int LeftShin = 24;

    /// <summary>The left foot.</summary>
    public const int LeftFoot = 25;

    /// <summary>The right thigh.</summary>
    public const int RightThigh = 28;

    /// <summary>The right shin.</summary>
    public const int RightShin = 29;

    /// <summary>The right foot.</summary>
    public const int RightFoot = 30;

    private static readonly int[] Parents =
    [
        BoneSkeleton.NoParent, 0, 1, 2, 3, 4, 4, 6,
        3, 8, 9, 10, 11, 12,
        3, 14, 15, 16, 17, 18,
        1, 20, 21,
        0, 23, 24, 25, 26,
        0, 28, 29, 30, 31,
    ];

    private static readonly string[] Names =
    [
        "pelvis", "spine", "upper spine", "chest", "head", "head tip", "jaw", "jaw tip",
        "L upper arm", "L forearm", "L hand", "L fingers", "L fingers 2", "L finger tip",
        "R upper arm", "R forearm", "R hand", "R fingers", "R fingers 2", "R finger tip",
        "gun chain 1", "gun chain 2", "gun hand",
        "L thigh", "L shin", "L foot", "L toes", "L toe tip",
        "R thigh", "R shin", "R foot", "R toes", "R toe tip",
    ];

    /// <summary>Reports whether a skeleton is the human rig, bone for bone.</summary>
    /// <param name="skeleton">The hierarchy.</param>
    /// <returns><see langword="true"/> when its bone count and every parent match.</returns>
    public static bool Matches(BoneSkeleton skeleton)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        if (skeleton.BoneCount != BoneCount)
        {
            return false;
        }

        for (int bone = 0; bone < BoneCount; bone++)
        {
            if (skeleton.Parents[bone] != Parents[bone])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Gets a bone's name.</summary>
    /// <param name="bone">The bone index.</param>
    /// <returns>The name.</returns>
    public static string NameOf(int bone) =>
        bone >= 0 && bone < Names.Length ? Names[bone] : bone.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Builds the profile.</summary>
    /// <returns>Sixteen bodies, with the hands and feet each carrying their fingers and toes.</returns>
    public static RagdollProfile Create()
    {
        Vector3f backwards = new(0f, -1f, 0f);
        Vector3f forwards = new(0f, 1f, 0f);

        // Each joint's tone is how long its muscles hold, as a share of the fade: the trunk and neck the
        // whole of it, the arms half, the knees and ankles a third. A body shot standing keeps its trunk
        // together while its legs go -- which is what carries it onto whatever is in front of it, a sill, a
        // crate, a wall -- and only then goes limp across it.
        RagdollJointSpec none = new(RagdollJointKind.Ball, 0f, 0f, 0f);
        RagdollJointSpec spine = Ball(20f, 12f) with { Tone = 0.8f, Slump = Radians(15f) };
        RagdollJointSpec chest = Ball(15f, 15f) with { Tone = 1f, Slump = Radians(12f) };
        RagdollJointSpec neck = Ball(40f, 45f) with { Tone = 0.6f, Slump = Radians(35f) };
        RagdollJointSpec shoulder = Ball(85f, 60f) with { Tone = 0.5f };
        RagdollJointSpec elbow = Hinge(-5f, 145f, forwards) with { Tone = 0.4f };
        RagdollJointSpec wrist = Ball(55f, 40f) with { Tone = 0.3f };
        RagdollJointSpec knee = Hinge(-3f, 140f, backwards) with { Tone = 0.7f };
        RagdollJointSpec ankle = Ball(30f, 12f) with { Tone = 0.5f };
        // A hip folds far forwards and opens only a little sideways -- about 45 degrees, less the outward
        // lean the clips put the cone's centre at. As one round cone it let a falling body do the splits.
        static RagdollJointSpec Hip(int shin) => Ball(65f, 25f) with
        {
            SwingSide = Radians(20f),
            Tone = 0.35f,
            CoupledKnee = shin,
            FlexStraight = Radians(75f),
            FlexBent = Radians(115f),
        };

        (RagdollBodySpec Body, RagdollJointSpec Joint)[] table =
        [
            (new(Pelvis, "pelvis", 0.142, RagdollShapeAxis.Across), none),
            (new(Spine, "abdomen", 0.139, RagdollShapeAxis.Across), spine),
            (new(Chest, "chest", 0.216, RagdollShapeAxis.Across), chest),
            (new(Head, "head", 0.081, RagdollShapeAxis.Upright), neck),
            (new(LeftUpperArm, "L upper arm", 0.028, RagdollShapeAxis.AlongBone), shoulder),
            (new(LeftForearm, "L forearm", 0.016, RagdollShapeAxis.AlongBone), elbow),
            (new(LeftHand, "L hand", 0.006, RagdollShapeAxis.AlongBone), wrist),
            (new(RightUpperArm, "R upper arm", 0.028, RagdollShapeAxis.AlongBone), shoulder),
            (new(RightForearm, "R forearm", 0.016, RagdollShapeAxis.AlongBone), elbow),
            (new(RightHand, "R hand", 0.006, RagdollShapeAxis.AlongBone), wrist),
            (new(LeftThigh, "L thigh", 0.100, RagdollShapeAxis.AlongBone), Hip(LeftShin)),
            (new(LeftShin, "L shin", 0.0465, RagdollShapeAxis.AlongBone), knee),
            (new(LeftFoot, "L foot", 0.0145, RagdollShapeAxis.AlongBone), ankle),
            (new(RightThigh, "R thigh", 0.100, RagdollShapeAxis.AlongBone), Hip(RightShin)),
            (new(RightShin, "R shin", 0.0465, RagdollShapeAxis.AlongBone), knee),
            (new(RightFoot, "R foot", 0.0145, RagdollShapeAxis.AlongBone), ankle),
        ];

        return new RagdollProfile(
            [.. table.Select(row => row.Body)],
            [.. table.Select(row => row.Joint)],
            Names,
            isHuman: true);
    }

    private static float Radians(float degrees) => degrees * MathF.PI / 180f;

    private static RagdollJointSpec Ball(float swing, float twist) =>
        new(RagdollJointKind.Ball, Radians(swing), -Radians(twist), Radians(twist));

    private static RagdollJointSpec Hinge(float low, float high, Vector3f toward) =>
        new(RagdollJointKind.Hinge, 0f, 0f, 0f, Radians(low), Radians(high), toward);
}
