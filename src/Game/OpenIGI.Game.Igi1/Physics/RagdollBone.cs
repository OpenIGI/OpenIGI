using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi1.Physics;

/// <summary>One bone's rigid body, as the solver needs it: a capsule with a mass and an inertia.</summary>
/// <param name="Centre">
/// Where the bone's centre of mass sits in its own frame, in engine units. A bone's origin is its joint,
/// which is at one END of the limb it carries, so this is never zero for anything that has a length.
/// </param>
/// <param name="Aim">The bone's long axis in its own frame, unit length.</param>
/// <param name="Reference">
/// A unit vector perpendicular to <paramref name="Aim"/>, in the bone's own frame. The twist limit is
/// measured as the angle between where the parent says this should point and where it actually does, so
/// any perpendicular will do as long as it is the <em>same</em> one every tick.
/// </param>
/// <param name="HalfLength">Half the capsule's straight section, in engine units.</param>
/// <param name="Radius">The capsule's radius, in engine units.</param>
/// <param name="InverseMass">One over the bone's mass, in inverse kilograms; zero pins it.</param>
/// <param name="InverseInertia">
/// The inverse inertia tensor in the bone's own frame. Diagonal in the frame the capsule is aligned to,
/// but not in the bone's, so it is carried as a full matrix.
/// </param>
public readonly record struct RagdollBone(
    Vector3f Centre,
    Vector3f Aim,
    Vector3f Reference,
    float HalfLength,
    float Radius,
    float InverseMass,
    Matrix3f InverseInertia);

/// <summary>
/// A joint between a body and the body above it: a ball with a cone and a twist range, or a hinge.
/// </summary>
/// <param name="Parent">The body above the joint.</param>
/// <param name="Child">The body below it. The joint sits at this body's bone's own origin.</param>
/// <param name="ParentAnchor">Where the joint sits in the parent's frame, from its centre of mass.</param>
/// <param name="ChildAnchor">Where the joint sits in the child's frame, from its centre of mass.</param>
/// <param name="SwingAxis">
/// A ball's cone centre: the direction the child's <paramref name="TwistAxis"/> points, in the
/// <em>parent's</em> joint frame, when the joint is at the middle of its range.
/// </param>
/// <param name="SwingLimit">The cone's half-angle, in radians.</param>
/// <param name="TwistLow">How far the child may twist one way about its own axis, in radians.</param>
/// <param name="TwistHigh">And the other way.</param>
/// <param name="Kind">A ball or a hinge.</param>
/// <param name="TwistAxis">
/// The child's own length, in its own frame — what a twist is measured about, and what a hinge swings.
/// Zero means the same as <paramref name="SwingAxis"/>.
/// </param>
/// <param name="BendAxis">A hinge's axis, in the parent's joint frame, signed so positive is the way it bends.</param>
/// <param name="BendLow">A hinge's least bend, in radians.</param>
/// <param name="BendHigh">Its furthest bend, in radians.</param>
/// <param name="Tone">How long this joint's muscles hold after death, as a fraction of the whole fade.</param>
/// <param name="Coupled">
/// For a hip, the BODY of the shin below it, or −1. The hamstrings run over both the hip and the knee, so
/// how far a hip folds forwards depends on the knee: with the leg straight a body cannot fold much past a
/// right angle, which is what made a corpse sitting with its legs out jack-knife flat onto them.
/// </param>
/// <param name="FlexStraight">How far the hip may fold forwards with that knee straight, in radians.</param>
/// <param name="FlexBent">And with it bent a right angle or more.</param>
/// <param name="SwingSide">
/// For an oval cone, its side-to-side half-angle in radians, with <paramref name="SwingLimit"/> the
/// front-to-back one; zero for a round cone. "Front" is the joint frame's +Y, the human rig's forwards.
/// </param>
/// <param name="Slump">
/// How far forwards the muscles fold this joint as they hold it, in radians, about the joint frame's +X.
/// See <see cref="RagdollBody"/>'s held pose.
/// </param>
public readonly record struct RagdollJoint(
    int Parent,
    int Child,
    Vector3f ParentAnchor,
    Vector3f ChildAnchor,
    Vector3f SwingAxis,
    float SwingLimit,
    float TwistLow,
    float TwistHigh,
    RagdollJointKind Kind = RagdollJointKind.Ball,
    Vector3f TwistAxis = default,
    Vector3f BendAxis = default,
    float BendLow = 0f,
    float BendHigh = 0f,
    float Tone = 1f,
    int Coupled = -1,
    float FlexStraight = 0f,
    float FlexBent = 0f,
    float SwingSide = 0f,
    float Slump = 0f);

/// <summary>Two bodies far enough apart in the rig that they are kept from passing through each other.</summary>
/// <param name="First">The lower bone index.</param>
/// <param name="Second">The higher bone index.</param>
public readonly record struct RagdollPair(int First, int Second);
