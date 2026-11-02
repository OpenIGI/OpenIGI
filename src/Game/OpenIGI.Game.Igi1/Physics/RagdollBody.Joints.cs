using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi1.Physics;

/// <content>
/// The joints: the friction that bleeds a body's spin relative to its parent, the attachment that holds
/// each body to its parent, the limits on how each joint may turn, and the muscle that pulls them towards
/// the death clip.
/// </content>
public sealed partial class RagdollBody
{
    // Joint friction: bleed the RELATIVE spin between a body and its parent, which is what makes a
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
    // near the root get the final word. The bodies being in tree order means walking the list backwards
    // is already leaf-first.
    private void SolveJoints()
    {
        for (int i = _links.Length - 1; i >= 0; i--)
        {
            SolveLimit(i);
            SolveAttachment(_links[i]);
        }
    }

    // The joint itself: the two bodies' anchor points are the same point, and they must stay so. Solved
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

    // The parent's joint frame in the world: the parent body's orientation, turned on by whatever bones it
    // carries between itself and the joint.
    private Matrix3f JointFrame(int link) =>
        BonePose.ToMatrix((_orientation[_links[link].Parent] * _jointOffset[link]).Normalized());

    /// <summary>Holds one joint inside the range it is allowed.</summary>
    /// <remarks>
    /// <para>
    /// <b>A ball</b> is held by two limits measured in its own terms: the <em>swing</em>, how far the
    /// child's length has moved off the centre of its cone, and the <em>twist</em>, how far it has rolled
    /// about that length. The roll limit is the one that matters most to how a corpse looks. Nothing else
    /// holds a bone's roll — the attachment says where a joint is and nothing about which way the bone is
    /// turned around it — and a skinned mesh whose bones roll against their neighbours pinches like a
    /// sweet wrapper, which reads as the model breaking.
    /// </para>
    /// <para>
    /// <b>A hinge</b> keeps its two copies of the hinge axis together — which takes away both the sideways
    /// bend and the roll at once — and then bounds the bend to the one way a knee or an elbow goes. The
    /// limit this replaced was a single symmetric cone about the rest pose, so a knee bent forwards as
    /// readily as backwards; that was most of what looked impossible about a fallen body.
    /// </para>
    /// <para>
    /// <b>These corrections are motion, not repairs</b>, exactly as the joint's attachment is. A limit turns
    /// a body about its centre, which moves the joint's anchor, which the attachment then pulls back — and
    /// the two have to be booked the same way. Booking the turn as a repair and the pull as motion made
    /// every limit a small engine: on the real rig a dropped arm swung itself back up to shoulder height
    /// over and over, which was the flailing a corpse could not stop doing. Booked alike, the pair is plain
    /// XPBD (Müller et al., <i>Detailed Rigid Body Simulation with Extended Position Based Dynamics</i>, 2020),
    /// and a limb that reaches its stop simply stops.
    /// </para>
    /// </remarks>
    private void SolveLimit(int i)
    {
        RagdollJoint link = _links[i];
        Matrix3f frame = JointFrame(i);
        Vector3f axis = link.TwistAxis.LengthSquared > 0f ? link.TwistAxis : link.SwingAxis;
        if (axis.LengthSquared <= 0f)
        {
            return;
        }

        if (link.Kind == RagdollJointKind.Hinge)
        {
            Vector3f hinge = RagdollSolver.Rotate(frame, link.BendAxis);
            Vector3f own = Direction(link.Child, link.BendAxis);
            Vector3f apart = Vector3f.Cross(own, hinge);
            float sine = apart.Length;
            if (sine > RagdollSolver.ParallelEpsilon)
            {
                float angle = MathF.Atan2(sine, Vector3f.Dot(own, hinge));
                ApplyAngular(link.Child, link.Parent, apart / sine, angle * LimitStiffness);
            }

            // The bend is measured from the MIDDLE of the range, not from straight. An angle read with atan2
            // wraps at half a turn from wherever it is measured from, and measured from straight that is a
            // knee folded just past 180 -- which then reads as hyperextended, is "corrected" the wrong way
            // round, and flips the shin through the thigh. Measured from the middle, the wrap sits half a
            // turn from the middle of the range, deep in the impossible, where nothing ever gets.
            float middle = (link.BendLow + link.BendHigh) * 0.5f;
            Matrix3f centred = BonePose.ToMatrix(Quaternion32.FromAxisAngle(hinge, middle));
            Vector3f rest = RagdollSolver.Rotate(centred, RagdollSolver.Rotate(frame, axis));
            Vector3f along = Direction(link.Child, axis);
            float bend = MathF.Atan2(
                Vector3f.Dot(Vector3f.Cross(rest, along), hinge), Vector3f.Dot(rest, along));
            Bound(link, hinge, bend, link.BendLow - middle, link.BendHigh - middle);
            _bend[link.Child] = bend + middle;
            return;
        }

        if (link.SwingLimit > 0f)
        {
            Vector3f centre = RagdollSolver.Rotate(frame, link.SwingAxis.LengthSquared > 0f ? link.SwingAxis : axis);
            Vector3f along = Direction(link.Child, axis);
            float cosine = Math.Clamp(Vector3f.Dot(along, centre), -1f, 1f);
            float off = MathF.Acos(cosine);
            float limit = link.SwingSide > 0f ? OvalLimit(link, frame, centre, along) : link.SwingLimit;
            if (off > limit)
            {
                Vector3f turn = Vector3f.Cross(along, centre);
                if (turn.LengthSquared <= RagdollSolver.ParallelEpsilon)
                {
                    // Exactly backwards: any square axis brings it round.
                    turn = RagdollSkeleton.Perpendicular(along);
                }

                ApplyAngular(link.Child, link.Parent, turn.Normalized(), (off - limit) * LimitStiffness);
            }
        }

        if (link.Coupled >= 0)
        {
            Hamstring(link, frame, axis);
        }

        if (link.TwistLow < link.TwistHigh)
        {
            Quaternion32 parent = RagdollPose.QuaternionOf(frame).Normalized();
            Quaternion32 relative = (parent.Conjugate() * _orientation[link.Child]).Normalized();
            (float twist, _) = RagdollLimits.Decompose(relative, axis);
            Bound(link, Direction(link.Child, axis), twist, link.TwistLow, link.TwistHigh);
        }
    }

    /// <summary>How far an oval cone allows its child to swing in the direction it is swinging now.</summary>
    /// <remarks>
    /// The direction off the cone's centre is split into a front-to-back and a side-to-side part, in the
    /// plane square to the centre, and the allowance is where that direction meets the ellipse with the
    /// two half-angles as its axes. "Front" is the joint frame's +Y.
    /// </remarks>
    private static float OvalLimit(RagdollJoint link, Matrix3f frame, Vector3f centre, Vector3f along)
    {
        Vector3f off = along - (centre * Vector3f.Dot(along, centre));
        if (off.LengthSquared <= RagdollSolver.ParallelEpsilon)
        {
            return link.SwingLimit;
        }

        Vector3f forwards = RagdollSolver.Rotate(frame, Vector3f.UnitY);
        forwards -= centre * Vector3f.Dot(forwards, centre);
        if (forwards.LengthSquared <= RagdollSolver.ParallelEpsilon)
        {
            return link.SwingLimit;
        }

        forwards = forwards.Normalized();
        Vector3f sideways = Vector3f.Cross(centre, forwards);
        Vector3f way = off.Normalized();
        float front = Vector3f.Dot(way, forwards) / link.SwingLimit;
        float side = Vector3f.Dot(way, sideways) / link.SwingSide;
        return 1f / MathF.Sqrt((front * front) + (side * side));
    }

    /// <summary>Holds a hip's forward fold to what its knee allows.</summary>
    /// <remarks>
    /// <para>
    /// The fold is measured in the hip's own frame — forwards is +Y and down is −Z, the human rig's rest
    /// axes — and allowed to grow from <see cref="RagdollJoint.FlexStraight"/> with the leg straight to
    /// <see cref="RagdollJoint.FlexBent"/> with the knee bent a right angle. The knee is read as the hinge
    /// pass left it, which has already run: the joints are walked leaves first.
    /// </para>
    /// <para>
    /// A turn about the hip's own +X carries the leg from down towards forwards, so the excess is taken back
    /// out by turning the other way.
    /// </para>
    /// </remarks>
    private void Hamstring(RagdollJoint link, Matrix3f frame, Vector3f axis)
    {
        Vector3f along = RagdollSolver.Rotate(frame.Transposed(), Direction(link.Child, axis));
        float fold = MathF.Atan2(along.Y, -along.Z);
        float knee = Math.Clamp(_bend[link.Coupled] / (MathF.PI * 0.5f), 0f, 1f);
        float allowed = link.FlexStraight + ((link.FlexBent - link.FlexStraight) * knee);
        if (fold > allowed)
        {
            Vector3f side = RagdollSolver.Rotate(frame, Vector3f.UnitX);
            ApplyAngular(link.Child, link.Parent, side, -(fold - allowed) * LimitStiffness);
        }
    }

    // Turns the child about a world axis until an angle measured about it is back inside its range.
    private void Bound(RagdollJoint link, Vector3f worldAxis, float angle, float low, float high)
    {
        if (angle > high)
        {
            ApplyAngular(link.Child, link.Parent, worldAxis, -(angle - high) * LimitStiffness);
        }
        else if (angle < low)
        {
            ApplyAngular(link.Child, link.Parent, worldAxis, (low - angle) * LimitStiffness);
        }
    }

    // Reads the pose the muscles pull towards: each joint's turn in the death clip, expressed the same
    // way the solver measures it -- the child bone against the bone it actually hangs from.
    private void Aim(BonePose target)
    {
        BoneSkeleton skeleton = _rig.Skeleton;
        int joints = _rig.Joints.Count;
        for (int i = 0; i < joints; i++)
        {
            int bone = _rig.BoneOf(_links[i].Child);
            int above = skeleton.Parents[bone];
            Matrix3f local = target.Rotations[above].Transposed() * target.Rotations[bone];
            _driveTarget[i] = RagdollPose.QuaternionOf(local).Normalized();
        }
    }

    /// <summary>Pulls every joint part of the way towards its target: the pose it died in, or the death clip.</summary>
    /// <remarks>
    /// <para>
    /// <b>Each joint lets go on its own clock</b>: its strength fades to nothing over its
    /// <see cref="RagdollJoint.Tone"/> share of the fade, squared so it holds most of its strength through
    /// the first half and then lets go smoothly. Knees and ankles go first, the trunk last.
    /// </para>
    /// <para>
    /// Applied as a motion, not a repair. The muscles really do move the limbs, and the speed they give
    /// them is what carries a falling body through the clip's collapse once they let go.
    /// </para>
    /// <para>
    /// Only the joints are driven, never the body as a whole. Where a corpse ends up is the physics'
    /// business — the round, the ground, the wall it hits — and pulling the root after the clip as well
    /// would bring back the canned fall this replaces.
    /// </para>
    /// </remarks>
    private void Drive(double progress)
    {
        int joints = _rig.Joints.Count;
        for (int i = 0; i < joints; i++)
        {
            RagdollJoint link = _links[i];
            // Following the clip, every joint follows it for the whole fade: the animator's fall is played
            // out as authored and only then handed to the physics. The staggered tones belong to a body
            // holding its own pose, where the order joints give way in is the whole of the fall.
            float tone = _following ? 1f : link.Tone;
            double left = tone > 0f ? 1.0 - (progress / tone) : 0.0;
            if (left <= 0.0)
            {
                continue;
            }

            double strength = MuscleStrength * left * left;
            Quaternion32 frame = (_orientation[link.Parent] * _jointOffset[i]).Normalized();
            Quaternion32 wanted = (frame * _driveTarget[i]).Normalized();
            Quaternion32 error = (wanted * _orientation[link.Child].Conjugate()).Normalized();
            if (error.W < 0f)
            {
                error = new Quaternion32(-error.X, -error.Y, -error.Z, -error.W);
            }

            Vector3f vector = error.Vector;
            float sine = vector.Length;
            if (sine <= RagdollSolver.ParallelEpsilon)
            {
                continue;
            }

            float angle = 2f * MathF.Atan2(sine, error.W);
            ApplyAngular(link.Child, link.Parent, vector / sine, angle * strength);
        }
    }

    /// <summary>The XPBD angular projection: turn two bodies so a rotation violation is closed.</summary>
    /// <remarks>
    /// <para>
    /// Turns <paramref name="a"/> by the violation's share about <paramref name="axis"/> and
    /// <paramref name="b"/> the other way, so the turn of <paramref name="a"/> relative to
    /// <paramref name="b"/> changes by exactly <paramref name="violation"/>, divided between them by their
    /// inertias.
    /// </para>
    /// <para>
    /// A <em>repair</em> moves the bodies without the move being read back as speed. Only the contacts'
    /// push-out is one: the joints' corrections are motion (see <see cref="SolveLimit"/>).
    /// </para>
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
