using OpenIGI.Core.Numerics;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi1.Physics;

/// <content>
/// The joints: the friction that bleeds a bone's spin relative to its parent, and the attachment and
/// swing-limit projections that hold each bone to its parent.
/// </content>
public sealed partial class RagdollBody
{
    // Joint friction: bleed the RELATIVE spin between a bone and its parent, which is what makes a
    // corpse limp rather than loose. Done at the velocity level after the solve, so it never fights the
    // constraints -- damping inside the projection would show up as a joint that drifts apart.
    private void DampJoints(double survives)
    {
        for (int i = 0; i < _links.Length; i++)
        {
            RagdollJoint link = _links[i];
            Vector3d relative = _spin[link.Child] - _spin[link.Parent];
            Vector3d bleed = relative * (1.0 - survives) * 0.5;
            _spin[link.Child] -= bleed;
            _spin[link.Parent] += bleed;
        }
    }

    // Leaves first, root LAST. In a Gauss-Seidel sweep the constraint solved last is the one satisfied
    // most accurately, and a hip that has come apart is far worse than a wrist that has -- so the joints
    // near the root get the final word. The skeleton being topologically sorted means walking the list
    // backwards is already leaf-first.
    private void SolveJoints()
    {
        for (int pass = 0; pass < JointIterations; pass++)
        {
            for (int i = _links.Length - 1; i >= 0; i--)
            {
                RagdollJoint link = _links[i];
                SolveDeviation(link);
                SolveAttachment(link);
            }
        }
    }

    // The joint itself: the two bones' anchor points are the same point, and they must stay so. Solved
    // AFTER the angular limits, because it is the one violation an audience can actually see.
    private void SolveAttachment(RagdollJoint link)
    {
        Vector3d parentArm = Turned(link.Parent, link.ParentAnchor);
        Vector3d childArm = Turned(link.Child, link.ChildAnchor);
        Vector3d gap = (_position[link.Parent] + parentArm) - (_position[link.Child] + childArm);

        double distance = gap.Length;
        if (distance <= 0.0)
        {
            return;
        }

        ApplyPositional(link.Parent, link.Child, parentArm, childArm, gap / distance, distance);
    }

    /// <summary>
    /// Eases a bone back toward its parent whenever it has turned further from it than the joint allows —
    /// in any direction at all, roll included.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One constraint on the whole relative rotation, rather than a swing limit and a twist limit
    /// solved separately, and that is a correction rather than a simplification.</b> Splitting a rotation
    /// into a swing and a twist needs an axis to split about, and on a joint that is already heavily
    /// swung — every joint of a corpse on the ground — the split moves part of the swing into the twist
    /// and part of the twist into the swing. Two constraints then each measure a quantity the other is
    /// changing, and neither converges: measured one way a joint would read 45°, measured the other 83°,
    /// on the same body at the same instant. The total angle between two orientations needs no axis and
    /// has no such ambiguity.
    /// </para>
    /// <para>
    /// It is also what fixes the limbs going thin. Nothing else in the solver constrains a bone's roll
    /// about its own length — the attachment says where a joint is and nothing about which way the bone
    /// is turned around it — so bones rolled freely, and a skinned mesh whose bones roll relative to
    /// their neighbours pinches: the vertices wind up like a sweet wrapper and the limb narrows to a
    /// point. That reads as a broken model rather than as broken physics, which is exactly how it looked.
    /// </para>
    /// <para>
    /// A <em>soft</em> spring rather than a hard stop, deliberately. A limit is a one-sided inequality
    /// that does nothing until it snaps, and snapping is what made the hard version shake a landed body
    /// to pieces. Taking a fixed fraction of the excess every substep acts continuously, acts hardest
    /// where the violation is worst, and cannot snap.
    /// </para>
    /// </remarks>
    private void SolveDeviation(RagdollJoint link)
    {
        // The child's orientation expressed in its parent's frame. At rest this is the identity, because
        // a rig's rest pose is every local rotation being nothing.
        Quaternion32 relative =
            (_orientation[link.Parent].Conjugate() * _orientation[link.Child]).Normalized();

        // A quaternion and its negation are the same orientation but opposite halves of a turn; taking
        // the near half is what makes the angle below the SHORT way round.
        if (relative.W < 0f)
        {
            relative = new Quaternion32(-relative.X, -relative.Y, -relative.Z, -relative.W);
        }

        Vector3f vector = relative.Vector;
        float sine = vector.Length;
        if (sine <= RagdollSolver.ParallelEpsilon)
        {
            return;
        }

        float angle = 2f * MathF.Atan2(sine, relative.W);
        float over = angle - link.SwingLimit;
        if (over <= 0f)
        {
            return;
        }

        // The axis is in the parent's frame, so it has to be carried into the world before it can turn
        // anything -- the constraint acts on world orientations.
        Vector3f axis = Direction(link.Parent, vector / sine);
        ApplyAngular(link.Parent, link.Child, axis, over * JointStiffness, repair: true);
    }

    /// <summary>The XPBD angular projection: turn two bodies so a rotation violation is closed.</summary>
    /// <remarks>
    /// <b>A joint limit is applied as a repair, and that is what makes a corpse settle.</b> The reasoning
    /// is the contacts' exactly: the velocities here are measured from how far things turned, so a limb
    /// shoved back inside its cone reads afterwards as a limb that was <em>spinning</em> that fast. On
    /// the ground the contacts push bones past their limits every substep, the limits push back, and each
    /// push-back is handed to the body as angular speed it never had — a corpse that lands and then
    /// twitches, rolls and dances, which is precisely what this looked like before the correction was
    /// excluded from the velocity.
    /// </remarks>
    private void ApplyAngular(int a, int b, Vector3f axis, double violation, bool repair = false)
    {
        Vector3d n = new(axis.X, axis.Y, axis.Z);
        double weightA = Vector3d.Dot(n, RagdollSolver.Rotate(_inverseInertia[a], n));
        double weightB = Vector3d.Dot(n, RagdollSolver.Rotate(_inverseInertia[b], n));
        double total = weightA + weightB;
        if (total <= 0.0)
        {
            return;
        }

        Vector3d impulse = n * (violation / total);
        Vector3d turnA = RagdollSolver.Rotate(_inverseInertia[a], impulse);
        Vector3d turnB = RagdollSolver.Rotate(_inverseInertia[b], -impulse);

        Turn(a, turnA);
        Turn(b, turnB);

        if (repair)
        {
            _repairSpin[a] += turnA;
            _repairSpin[b] += turnB;
        }
    }
}
