using OpenIGI.Core.Numerics;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi1.Physics;

/// <content>
/// The contacts: the ground surveyed once a tick, the bones projected out of it and out of each other,
/// and the friction and restitution read back from the contact velocities.
/// </content>
public sealed partial class RagdollBody
{
    /// <summary>Finds the ground under every probe, once for the whole tick.</summary>
    /// <remarks>
    /// <para>
    /// <b>The one thing that decides whether ragdolls are affordable.</b> Asking the world where the
    /// floor is costs a ray cast against every placed collision mesh plus a terrain lookup — easily the
    /// most expensive call in the solver, and thousands of times more expensive than the arithmetic
    /// around it. Doing it inside the projection meant <b>1349 of them per corpse per tick</b>: three
    /// probes a bone, sixteen substeps, two passes. Ten corpses were costing more than the frame.
    /// </para>
    /// <para>
    /// It is also unnecessary. A bone moves a few units in a substep, so the <em>surface</em> under it
    /// does not change within a tick even though its distance to that surface does — and only the
    /// distance is wanted per projection. So the plane is found once and the penetration recomputed
    /// against it every time, which is the same split the XPBD paper makes between collecting collision
    /// pairs once a frame and recomputing each contact every substep.
    /// </para>
    /// <para>
    /// What it costs is a tick of lag on the surface itself: a bone that slides over a step edge uses the
    /// old floor until the next tick. At thirty ticks a second, on a corpse, that is not observable.
    /// </para>
    /// </remarks>
    private void Survey(RagdollWorld world)
    {
        Vector3d middle = Vector3d.Zero;
        for (int bone = 0; bone < _position.Length; bone++)
        {
            middle += _position[bone];
        }

        middle /= _position.Length;

        for (int bone = 0; bone < _position.Length; bone++)
        {
            // Whatever is beside this bone: one probe along its travel, one straight out from the body.
            //
            // The reach INCLUDES THE TICK'S TRAVEL, and it has to. The surfaces are collected once a tick
            // and resolved every substep, which is what makes them affordable — but a bone moving at ten
            // metres a second covers a metre and a half between two surveys, so a reach of only its own
            // size looks at empty air, finds nothing, and the bone is through the wall before anything is
            // asked again. Padding the look-ahead by the distance about to be travelled is the same thing
            // a broad phase does when it expands its bounds by velocity times the step.
            double reach =
                _radius[bone] + _halfLength[bone] + PenetrationSlop + _velocity[bone].Length;
            Vector3d outward = _position[bone] - middle;

            // The two wall probes have distinct jobs: travel catches a body thrown INTO a wall while
            // outward catches one already resting AGAINST it. They become the exact same ray only when
            // they are positively parallel: TrySurface normalizes direction, and both probes start at
            // this bone with the same reach. In that case a second world traversal cannot find a
            // different surface, so retain both contact slots but share the one result. This deliberately
            // uses exact parallelism, not an angular tolerance: nearly parallel rays can cross different
            // geometry and must remain independently safe.
            int travelSlot = bone * WallProbes;
            Vector3d travel = _velocity[bone];
            _walled[travelSlot] = world.TrySurface(
                _position[bone], travel, reach, out _wallPoint[travelSlot], out _wallNormal[travelSlot]);

            int outwardSlot = travelSlot + 1;
            if (SameRayDirection(travel, outward))
            {
                _walled[outwardSlot] = _walled[travelSlot];
                _wallPoint[outwardSlot] = _wallPoint[travelSlot];
                _wallNormal[outwardSlot] = _wallNormal[travelSlot];
            }
            else
            {
                _walled[outwardSlot] = world.TrySurface(
                    _position[bone], outward, reach, out _wallPoint[outwardSlot], out _wallNormal[outwardSlot]);
            }

            Vector3d along = Turned(bone, _aim[bone]) * _halfLength[bone];

            // A bone shorter than it is wide is a sphere, and its three probes are the same probe -- so
            // it is asked once and the answer shared. A human rig is full of them (a hand, a foot, the
            // head's tip, every short spine segment), and each one skipped is two fewer ray casts
            // against the world per corpse per tick.
            if (_halfLength[bone] <= _radius[bone])
            {
                int only = bone * 3;
                _grounded[only] = world.TryGround(_position[bone], out _surface[only]);
                _grounded[only + 1] = _grounded[only];
                _grounded[only + 2] = _grounded[only];
                _surface[only + 1] = _surface[only];
                _surface[only + 2] = _surface[only];
                continue;
            }

            for (int end = -1; end <= 1; end++)
            {
                int probe = (bone * 3) + end + 1;
                _grounded[probe] =
                    world.TryGround(_position[bone] + (along * end), out _surface[probe]);
            }
        }
    }

    // TrySurface casts from the same origin to the same distance after it normalizes its direction.
    // Sharing is therefore exact only for non-zero, co-directed vectors with a zero cross product; an
    // opposite vector is a different ray, and an approximate comparison could miss a thin wall.
    private static bool SameRayDirection(Vector3d first, Vector3d second) =>
        first.LengthSquared > 0.0
        && second.LengthSquared > 0.0
        && Vector3d.Cross(first, second) == Vector3d.Zero
        && Vector3d.Dot(first, second) > 0.0;

    // Every bone is a capsule, and it is tested along its whole length rather than at its centre -- which
    // is the difference between a corpse lying ON a floor and a corpse buried to its waist in one.
    private void SolveContacts(double step, bool record)
    {
        double budget = MaximumDepenetrationSpeed * step;

        for (int bone = 0; bone < _position.Length; bone++)
        {
            Vector3d along = Turned(bone, _aim[bone]) * _halfLength[bone];

            // Both caps and the middle. Three probes is what a capsule against a heightfield needs to
            // stop it standing on one end.
            for (int end = -1; end <= 1; end++)
            {
                int index = (bone * 3) + end + 1;
                if (!_grounded[index])
                {
                    continue;
                }

                Vector3d probe = _position[bone] + (along * end);
                double depth = _surface[index] + _radius[bone] - probe.Z - PenetrationSlop;
                if (depth <= 0.0)
                {
                    continue;
                }

                // Recorded on the FIRST pass, and that is not a detail. By the last pass the earlier ones
                // have already pushed the bone clear, so the contact is no longer found -- and a contact
                // that is not recorded never has its push-out velocity cancelled, which is precisely the
                // energy that makes a corpse bounce higher each time it lands.
                Vector3d arm = probe - _position[bone];

                // Only ever a substep's worth of it -- see MaximumDepenetrationSpeed for why that cap
                // is what stops a corpse launching off the floor rather than landing on it.
                Remember(record, bone, arm, Vector3d.UnitZ);
                ApplyPositional(
                    -1, bone, Vector3d.Zero, arm, Vector3d.UnitZ, Math.Min(depth, budget), repair: true);
            }

            // And the walls. Pushed at the bone's centre rather than at the contact point: a wall shove
            // that also spins the bone reads as a body crawling up the wall it was thrown against.
            for (int probe = 0; probe < WallProbes; probe++)
            {
                int slot = (bone * WallProbes) + probe;
                if (!_walled[slot])
                {
                    continue;
                }

                double clear = Vector3d.Dot(_position[bone] - _wallPoint[slot], _wallNormal[slot]);
                double into = _radius[bone] - clear - PenetrationSlop;
                if (into <= 0.0)
                {
                    continue;
                }

                Remember(record, bone, Vector3d.Zero, _wallNormal[slot]);
                ApplyPositional(
                    -1,
                    bone,
                    Vector3d.Zero,
                    Vector3d.Zero,
                    _wallNormal[slot],
                    Math.Min(into, budget),
                    repair: true);
            }
        }
    }

    // One contact, kept for the velocity pass -- including the speed the bone was already carrying into
    // the surface, which is the one thing that cannot be recovered afterwards.
    private void Remember(bool record, int bone, Vector3d arm, Vector3d normal)
    {
        if (!record || _contactCount >= _contactBone.Length)
        {
            return;
        }

        _contactBone[_contactCount] = bone;
        _contactArm[_contactCount] = arm;
        _contactNormal[_contactCount] = normal;
        _contactApproach[_contactCount] =
            Vector3d.Dot(_velocity[bone] + Vector3d.Cross(_spin[bone], arm), normal);
        _contactCount++;
    }

    // Bone against bone, so a corpse has a volume instead of folding flat through itself. Capsule
    // against capsule: the nearest points on the two straight sections, pushed apart if they are closer
    // than the two radii together.
    private void SolveSelfCollision(double step)
    {
        double budget = MaximumDepenetrationSpeed * step;

        for (int i = 0; i < _pairs.Length; i++)
        {
            RagdollPair pair = _pairs[i];

            // The cheapest possible rejection first: two capsules cannot touch if their centres are
            // further apart than their two half-lengths and two radii together. Most pairs on a body
            // fail this every substep, and failing it costs one subtraction and a dot product instead of
            // four closest-point solves.
            double reach = _halfLength[pair.First] + _halfLength[pair.Second]
                + _radius[pair.First] + _radius[pair.Second];
            if (Vector3d.DistanceSquared(_position[pair.First], _position[pair.Second]) > reach * reach)
            {
                continue;
            }

            Vector3d firstAlong = Turned(pair.First, _aim[pair.First]) * _halfLength[pair.First];
            Vector3d secondAlong = Turned(pair.Second, _aim[pair.Second]) * _halfLength[pair.Second];

            Vector3d a0 = _position[pair.First] - firstAlong;
            Vector3d a1 = _position[pair.First] + firstAlong;
            Vector3d b0 = _position[pair.Second] - secondAlong;
            Vector3d b1 = _position[pair.Second] + secondAlong;

            // Two rounds of alternating projection finds the nearest pair of points on two segments
            // closely enough for a push-apart; an exact solve buys nothing a corpse can show.
            Vector3d onFirst = Vector3d.ClosestOnSegment(a0, a1, _position[pair.Second]);
            Vector3d onSecond = Vector3d.ClosestOnSegment(b0, b1, onFirst);
            onFirst = Vector3d.ClosestOnSegment(a0, a1, onSecond);
            onSecond = Vector3d.ClosestOnSegment(b0, b1, onFirst);

            Vector3d apart = onFirst - onSecond;
            double distance = apart.Length;
            double wanted = _radius[pair.First] + _radius[pair.Second];
            if (distance >= wanted || distance <= 0.0)
            {
                continue;
            }

            ApplyPositional(
                pair.First,
                pair.Second,
                onFirst - _position[pair.First],
                onSecond - _position[pair.Second],
                apart / distance,
                -Math.Min(wanted - distance, budget),
                repair: true);
        }
    }

    // The velocity half of a contact, run after the velocities have been read back out of the motion.
    // Without it the body keeps whatever speed the push-out gave it, which is the difference between a
    // corpse landing and a corpse bouncing for ever.
    private void SolveContactVelocities()
    {
        for (int i = 0; i < _contactCount; i++)
        {
            int bone = _contactBone[i];
            Vector3d arm = _contactArm[i];
            Vector3d normal = _contactNormal[i];
            Vector3d point = _velocity[bone] + Vector3d.Cross(_spin[bone], arm);

            // The speed INTO the surface is replaced outright by what the bone was doing before the
            // solve, mirrored by the restitution -- which is zero, so a body meeting something simply
            // stops. Deriving it from the post-solve motion instead reads the push-out as an impact and
            // bounces on it. Along the surface's own normal, so a wall works exactly as a floor does.
            double wanted = Math.Min(-Restitution * _contactApproach[i], 0.0);
            ApplyVelocity(bone, arm, normal * (wanted - Vector3d.Dot(point, normal)));

            // And friction across the surface, bounded by the tangential speed itself so that it can slow
            // the bone but never reverse it.
            point = _velocity[bone] + Vector3d.Cross(_spin[bone], arm);
            Vector3d tangent = point - (normal * Vector3d.Dot(point, normal));
            if (tangent.LengthSquared > 0.0)
            {
                ApplyVelocity(bone, arm, tangent * (-Math.Min(Friction, 1.0)));
            }

            // And the twisting the sliding friction above cannot see -- a bone turning about the surface
            // it rests on has a contact point that is barely moving, so there is nothing for a velocity
            // at that point to oppose.
            _spin[bone] *= 1.0 - TorsionalFriction;
        }
    }

    // A velocity change applied at a point on one body, shared between its linear and angular parts the
    // same way a positional correction is.
    private void ApplyVelocity(int bone, Vector3d arm, Vector3d change)
    {
        double size = change.Length;
        if (size <= 0.0)
        {
            return;
        }

        double weight = GeneralizedMass(bone, arm, change / size);
        if (weight <= 0.0)
        {
            return;
        }

        Vector3d impulse = change / weight;
        _velocity[bone] += impulse * _inverseMass[bone];
        _spin[bone] += RagdollSolver.Rotate(_inverseInertia[bone], Vector3d.Cross(arm, impulse));
    }
}
