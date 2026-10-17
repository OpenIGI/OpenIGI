using OpenIGI.Core.Numerics;
using OpenIGI.Game.Physics;

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
/// A ball joint between a bone and its parent, with a cone on how far it may swing and a range on how
/// far it may twist.
/// </summary>
/// <param name="Parent">The bone above the joint.</param>
/// <param name="Child">The bone below it. The joint sits at this bone's own origin.</param>
/// <param name="ParentAnchor">Where the joint sits in the parent's frame, from its centre of mass.</param>
/// <param name="ChildAnchor">Where the joint sits in the child's frame, from its centre of mass.</param>
/// <param name="SwingAxis">
/// The direction the child's own axis points, in the <em>parent's</em> frame, when the joint is at the
/// middle of its range — the cone's centre line.
/// </param>
/// <param name="SwingLimit">The cone's half-angle, in radians.</param>
/// <param name="TwistLow">How far the child may twist one way about its own axis, in radians.</param>
/// <param name="TwistHigh">And the other way.</param>
public readonly record struct RagdollJoint(
    int Parent,
    int Child,
    Vector3f ParentAnchor,
    Vector3f ChildAnchor,
    Vector3f SwingAxis,
    float SwingLimit,
    float TwistLow,
    float TwistHigh);

/// <summary>Two bones far enough apart in the rig that they are kept from passing through each other.</summary>
/// <param name="First">The lower bone index.</param>
/// <param name="Second">The higher bone index.</param>
public readonly record struct RagdollPair(int First, int Second);
