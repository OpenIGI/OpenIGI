using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;

namespace OpenIGI.Game.Igi1.Physics;

/// <summary>Which way a body's capsule runs through its bone.</summary>
public enum RagdollShapeAxis
{
    /// <summary>Along the bone, from its joint towards the bone it carries: a limb.</summary>
    AlongBone,

    /// <summary>Across the body, side to side: a pelvis or a ribcage, which is wider than it is deep.</summary>
    /// <remarks>
    /// <b>This is what stops a body on its back rolling like a log.</b> A torso modelled as a cylinder up
    /// the spine is round in exactly the direction it lies on. Laid across, its radius is the body's
    /// depth and its length its width, so three of them make a back that is flat.
    /// </remarks>
    Across,

    /// <summary>Straight up the bone's own frame: a head.</summary>
    Upright,
}

/// <summary>How a joint is allowed to move.</summary>
public enum RagdollJointKind
{
    /// <summary>A cone the bone may swing within, and a range it may twist through about its own length.</summary>
    Ball,

    /// <summary>One axis only, and only one way along it: a knee or an elbow.</summary>
    Hinge,
}

/// <summary>One simulated body of a profile: which bone it is, what share of the weight it carries, and its shape.</summary>
/// <param name="Bone">The skeleton bone whose frame the body moves in.</param>
/// <param name="Name">What the body is, for logs and the debug view.</param>
/// <param name="MassFraction">Its share of the whole body's mass, or <see cref="double.NaN"/> to share by volume.</param>
/// <param name="Axis">Which way its capsule runs.</param>
public readonly record struct RagdollBodySpec(int Bone, string Name, double MassFraction, RagdollShapeAxis Axis);

/// <summary>How the joint above one body moves.</summary>
/// <param name="Kind">A ball or a hinge.</param>
/// <param name="Swing">A ball's cone half-angle, in radians.</param>
/// <param name="TwistLow">How far the bone may twist one way about its own length, in radians.</param>
/// <param name="TwistHigh">And the other way.</param>
/// <param name="BendLow">A hinge's least bend, in radians; slightly negative so a straight limb is not at its stop.</param>
/// <param name="BendHigh">A hinge's furthest bend, in radians.</param>
/// <param name="BendToward">
/// For a hinge, the direction in the PARENT's frame its child swings towards when it bends — backwards for
/// a knee, forwards for an elbow. It only settles the sign of the axis the animations measure.
/// </param>
/// <param name="Tone">
/// How long this joint's muscles hold after death, as a fraction of <c>g_ragdoll_muscle</c>: the trunk
/// longest, the knees and ankles shortest, which is the order a body gives way in.
/// </param>
/// <param name="CoupledKnee">
/// For a hip, the bone of the knee below it, whose bend decides how far the hip may fold forwards; −1 for
/// none. See <see cref="RagdollJoint.Coupled"/>.
/// </param>
/// <param name="FlexStraight">How far the hip may fold forwards with that knee straight, in radians.</param>
/// <param name="FlexBent">And with it bent a right angle or more.</param>
/// <param name="SwingSide">
/// For a ball whose range is not round, how far it may swing side to side, in radians; <see cref="Swing"/>
/// is then the front-to-back half of the ellipse. Zero for a round cone.
/// </param>
/// <param name="Slump">
/// How far forwards this joint folds as its tone goes, in radians: the muscles of a body losing
/// consciousness hold a curled version of its pose, not the upright one.
/// </param>
public readonly record struct RagdollJointSpec(
    RagdollJointKind Kind,
    float Swing,
    float TwistLow,
    float TwistHigh,
    float BendLow = 0f,
    float BendHigh = 0f,
    Vector3f BendToward = default,
    float Tone = 1f,
    int CoupledKnee = -1,
    float FlexStraight = 0f,
    float FlexBent = 0f,
    float SwingSide = 0f,
    float Slump = 0f);

/// <summary>
/// How a skeleton becomes a ragdoll: which of its bones are simulated, what each weighs, which way each
/// is shaped, and how each joint moves.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not every bone is a body.</b> A rig carries bones that are no part of a limb — finger and toe
/// joints, the tip of the head, the three-bone weapon chain — and simulating each as a rigid body of its
/// own was the largest single source of corpses looking broken. A bone with no length is a point; as a
/// body it spins freely about itself, takes a share of the mass it has no business having, and twists
/// whatever skin is bound to it. Those bones are <em>carried</em> instead: they keep the turn they had
/// when the body died, relative to the nearest simulated bone above them.
/// </para>
/// <para>
/// The human rig gets its own table (<see cref="HumanRagdollProfile"/>); anything else gets
/// <see cref="Generic"/>, which works the same structure out from the tree alone.
/// </para>
/// </remarks>
public sealed class RagdollProfile
{
    private readonly string[] _boneNames;

    /// <summary>Initializes a profile.</summary>
    /// <param name="bodies">The simulated bodies, root first, each bone after its simulated ancestors.</param>
    /// <param name="joints">One per body; the root's is ignored.</param>
    /// <param name="boneNames">A name for every bone of the skeleton.</param>
    /// <param name="isHuman">Whether this is the measured human table.</param>
    public RagdollProfile(
        IReadOnlyList<RagdollBodySpec> bodies,
        IReadOnlyList<RagdollJointSpec> joints,
        IReadOnlyList<string> boneNames,
        bool isHuman)
    {
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(joints);
        ArgumentNullException.ThrowIfNull(boneNames);
        if (bodies.Count == 0 || joints.Count != bodies.Count)
        {
            throw new ArgumentException("A profile needs at least one body and exactly one joint per body.");
        }

        Bodies = [.. bodies];
        Joints = [.. joints];
        _boneNames = [.. boneNames];
        IsHuman = isHuman;
    }

    /// <summary>Gets the simulated bodies.</summary>
    public IReadOnlyList<RagdollBodySpec> Bodies { get; }

    /// <summary>Gets each body's joint to the body above it.</summary>
    public IReadOnlyList<RagdollJointSpec> Joints { get; }

    /// <summary>Gets whether this is the human table rather than one worked out from a tree.</summary>
    public bool IsHuman { get; }

    /// <summary>Gets a bone's name.</summary>
    /// <param name="bone">The bone index.</param>
    /// <returns>Its name, or its number when the profile has none for it.</returns>
    public string BoneName(int bone) =>
        bone >= 0 && bone < _boneNames.Length ? _boneNames[bone] : bone.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The profile for a skeleton: the human table when it is the human rig, otherwise worked out from its tree.</summary>
    /// <param name="skeleton">The hierarchy.</param>
    /// <returns>The profile.</returns>
    public static RagdollProfile For(BoneSkeleton skeleton) =>
        HumanRagdollProfile.Matches(skeleton) ? HumanRagdollProfile.Create() : Generic(skeleton);

    /// <summary>Works a profile out from a tree alone.</summary>
    /// <param name="skeleton">The hierarchy.</param>
    /// <returns>Every bone that carries a limb simulated, every end bone carried by its parent.</returns>
    /// <remarks>
    /// A bone is simulated when it is the root or when something hangs off it at a distance — when it has
    /// a length. An end bone has none: it is the tip of a hand or a foot, and it rides on the bone above.
    /// Masses are shared by volume and joints are balls, because nothing about which joint is which can be
    /// read from a tree.
    /// </remarks>
    public static RagdollProfile Generic(BoneSkeleton skeleton)
    {
        ArgumentNullException.ThrowIfNull(skeleton);

        int count = skeleton.BoneCount;
        bool[] carries = new bool[count];
        for (int bone = 0; bone < count; bone++)
        {
            int parent = skeleton.Parents[bone];
            if (parent != BoneSkeleton.NoParent && skeleton.RestTranslations[bone].LengthSquared > 0f)
            {
                carries[parent] = true;
            }
        }

        List<RagdollBodySpec> bodies = [];
        List<RagdollJointSpec> joints = [];
        string[] names = new string[count];
        for (int bone = 0; bone < count; bone++)
        {
            names[bone] = $"bone {bone}";
            if (skeleton.Parents[bone] == BoneSkeleton.NoParent || carries[bone])
            {
                bodies.Add(new RagdollBodySpec(bone, names[bone], double.NaN, RagdollShapeAxis.AlongBone));
                joints.Add(new RagdollJointSpec(
                    RagdollJointKind.Ball,
                    RagdollSkeleton.DefaultSwingLimit,
                    -RagdollSkeleton.DefaultTwistLimit,
                    RagdollSkeleton.DefaultTwistLimit));
            }
        }

        return new RagdollProfile(bodies, joints, names, isHuman: false);
    }
}
