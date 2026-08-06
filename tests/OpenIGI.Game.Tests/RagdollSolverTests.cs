using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Covers the rigid-body solver: that a body stays a body, that it lands on a floor rather than in it,
/// and that a joint stays inside the range it was given.
/// </summary>
public sealed class RagdollSolverTests
{
    /// <summary>No joint comes apart, however long the body is thrown about.</summary>
    /// <remarks>
    /// The one thing that must hold absolutely, and the one an audience notices first. A joint is two
    /// anchor points that have to stay the same point; how far they drift is the whole measure of
    /// whether the solver is converging.
    /// </remarks>
    [Fact]
    public void NoJointComesApartOverAThousandTicks()
    {
        RagdollSkeleton rig = RagdollRig.Rig();
        RagdollBody body = RagdollRig.Standing(rig, new Vector3d(0.0, 0.0, 20_000.0));
        body.AddImpulse(RagdollRig.Head, new Vector3d(400.0, 120.0, 80.0));

        RagdollWorld plane = RagdollWorld.Plane(0.0);
        for (int tick = 0; tick < 1000; tick++)
        {
            body.Step(plane, substeps: 8);
        }

        for (int bone = 1; bone < rig.BoneCount; bone++)
        {
            int parent = rig.Skeleton.Parents[bone];
            Vector3d joint = body.PositionOf(bone);
            Vector3f offset = Turn(body, parent, rig.Skeleton.RestTranslations[bone]);
            Vector3d expected = body.PositionOf(parent) + new Vector3d(offset.X, offset.Y, offset.Z);

            // A tenth of a metre over a thousand ticks of tumbling. Position-based dynamics converges
            // rather than solving exactly, so demanding equality would be pinning the substep count.
            Assert.True(
                Vector3d.Distance(joint, expected) < 410.0,
                $"Bone {bone} drifted {Vector3d.Distance(joint, expected):F0} units from its parent.");
        }
    }

    /// <summary>A body dropped onto a floor lands on top of it, and stops.</summary>
    /// <remarks>
    /// <b>On top of it, not in it.</b> Every bone is a capsule tested along its whole length, so a corpse
    /// rests on its own surface — testing a bone at its centre instead is what buries one to its waist.
    /// </remarks>
    [Fact]
    public void ABodyDroppedOnAFloorSettlesOnTopOfIt()
    {
        RagdollSkeleton rig = RagdollRig.Rig();
        RagdollBody body = RagdollRig.Standing(rig, new Vector3d(0.0, 0.0, 20_000.0));
        RagdollWorld plane = RagdollWorld.Plane(0.0);

        for (int tick = 0; tick < 900 && !body.IsAtRest; tick++)
        {
            body.Step(plane, substeps: 8);
        }

        Assert.True(body.IsAtRest, "The body never stopped moving.");

        double deepest = 0.0;
        double lowest = double.MaxValue;
        for (int bone = 0; bone < rig.BoneCount; bone++)
        {
            RagdollBone shape = rig.BoneAt(bone);
            double surface = body.PositionOf(bone).Z - shape.Radius;
            deepest = Math.Min(deepest, surface);
            lowest = Math.Min(lowest, surface);
        }

        // Sinking is allowed only as far as the deadband the contact deliberately leaves.
        Assert.True(
            deepest > -(RagdollBody.PenetrationSlop * 4.0),
            $"A bone settled {-deepest:F0} units into the floor.");

        // And it is ON the floor rather than hovering above it.
        Assert.True(lowest < 2048.0, $"The lowest bone rests {lowest:F0} units up.");
    }

    /// <summary>A body never falls through a floor it started above, however fast it is thrown at it.</summary>
    /// <remarks>
    /// The failure this replaces was not a solver problem at all: the port's ground query only ever looks
    /// <em>downwards</em>, so a bone that ended a step below the floor found no floor at all and fell for
    /// ever. Searching from above the bone is what makes it recoverable — see
    /// <see cref="RagdollWorld.GroundSearchAboveInUnits"/>.
    /// </remarks>
    [Fact]
    public void ABodyDrivenHardAtTheFloorDoesNotGoThroughIt()
    {
        RagdollSkeleton rig = RagdollRig.Rig();
        RagdollBody body = RagdollRig.Standing(rig, new Vector3d(0.0, 0.0, 6_000.0));
        RagdollWorld plane = RagdollWorld.Plane(0.0);

        body.AddVelocity(new Vector3d(0.0, 0.0, -8_000.0));
        for (int tick = 0; tick < 200; tick++)
        {
            body.Step(plane, substeps: 8);

            for (int bone = 0; bone < rig.BoneCount; bone++)
            {
                Assert.True(
                    body.PositionOf(bone).Z > -4096.0,
                    $"Bone {bone} was a metre under the floor on tick {tick}.");
            }
        }
    }

    /// <summary>A body folds around itself rather than through itself.</summary>
    /// <remarks>
    /// <b>What gives a corpse a volume</b>, and the thing whose absence reads as a body with no weight:
    /// without it an arm comes to rest inside the chest and a leg through the other leg, and the whole
    /// shape goes flat. It is also what settled the solver — bones passing through one another leave the
    /// joints holding an impossible configuration, and they fight it for ever.
    /// </remarks>
    [Fact]
    public void BonesDoNotPassThroughOneAnother()
    {
        RagdollSkeleton rig = RagdollRig.Rig();
        RagdollBody body = RagdollRig.Standing(rig, new Vector3d(0.0, 0.0, 20_000.0));
        body.AddImpulse(RagdollRig.LeftHand, new Vector3d(700.0, -400.0, 0.0));
        body.AddImpulse(RagdollRig.RightFoot, new Vector3d(-500.0, 300.0, 0.0));

        RagdollWorld plane = RagdollWorld.Plane(0.0);
        for (int tick = 0; tick < 300; tick++)
        {
            body.Step(plane, substeps: 8);
        }

        foreach (RagdollPair pair in rig.SelfCollisionPairs)
        {
            double apart = Vector3d.Distance(body.PositionOf(pair.First), body.PositionOf(pair.Second));
            double wanted = rig.BoneAt(pair.First).Radius + rig.BoneAt(pair.Second).Radius;

            // Measured joint to joint rather than surface to surface, so it is a weaker statement than
            // the solver makes -- but it catches the failure that matters, which is two bones occupying
            // the same space.
            Assert.True(
                apart > wanted * 0.4,
                $"Bones {pair.First} and {pair.Second} are {apart:F0} apart against radii of {wanted:F0}.");
        }
    }

    /// <summary>A bone never ends up turned further from its parent than the joint allows.</summary>
    /// <remarks>
    /// <b>What keeps the limbs from going thin.</b> Nothing else in the solver constrains a bone's roll
    /// about its own length, and a skinned mesh whose bones roll relative to their neighbours pinches to
    /// a point — so an unbounded joint reads as a broken model rather than as broken physics. The bound
    /// is on the TOTAL angle between the two orientations, which is why it can be stated at all: split
    /// into a swing and a twist, the same joint reads two different numbers.
    /// </remarks>
    [Fact]
    public void ABoneNeverTurnsFurtherFromItsParentThanTheJointAllows()
    {
        RagdollSkeleton rig = RagdollRig.Rig();
        RagdollBody body = RagdollRig.Standing(rig, new Vector3d(0.0, 0.0, 20_000.0));
        body.AddImpulse(RagdollRig.LeftHand, new Vector3d(900.0, -600.0, 400.0));
        body.AddImpulse(RagdollRig.RightFoot, new Vector3d(-700.0, 500.0, 300.0));

        RagdollWorld plane = RagdollWorld.Plane(0.0);
        for (int tick = 0; tick < 400 && !body.IsAtRest; tick++)
        {
            body.Step(plane, substeps: 8);
        }

        Assert.True(body.IsAtRest, "The body never settled.");

        foreach (RagdollJoint link in rig.Joints)
        {
            Quaternion32 relative =
                (body.OrientationOf(link.Parent).Conjugate() * body.OrientationOf(link.Child)).Normalized();
            if (relative.W < 0f)
            {
                relative = new Quaternion32(-relative.X, -relative.Y, -relative.Z, -relative.W);
            }

            float angle = 2f * MathF.Atan2(relative.Vector.Length, relative.W);

            // Within a few degrees of its allowance. A soft spring has a steady-state offset by
            // definition -- it pulls in proportion to the excess, so a joint the attachment is still
            // pushing outwards comes to rest where the two balance rather than exactly on the limit.
            // What is pinned is that the offset is small: it was seventeen degrees before the spring was
            // matched to the substep count, and is three now.
            Assert.True(
                angle <= link.SwingLimit + 0.12f,
                $"Joint {link.Child} came to rest {angle:F2} rad from its parent, against {link.SwingLimit:F2}.");
        }
    }

    /// <summary>A corpse falls at real gravity, not at the exaggerated rate the player jumps under.</summary>
    /// <remarks>
    /// Both numbers are the original's own — see <see cref="RagdollBody.Gravity"/>. Nothing is invented;
    /// what is chosen is which of the two describes a body rather than a platformer jump.
    /// </remarks>
    [Fact]
    public void ACorpseFallsAtRealGravity()
    {
        Assert.Equal(HumanMotion.LadderSlideGravityPerTick, RagdollBody.Gravity, 1e-4);
        Assert.True(RagdollBody.Gravity < HumanMotion.GravityPerTick);

        // 9.8 m/s^2, in engine units per tick squared: 9.8 * 4096 / 900.
        Assert.Equal(9.8, RagdollBody.Gravity * 900.0 / 4096.0, 1e-3);
    }

    /// <summary>The gravity scale is a look, and it is kept where it can be seen and turned.</summary>
    /// <remarks>
    /// The constant above is honest physics; this is the deliberate slow-down on top of it, because real
    /// gravity reads fast on a corpse. Keeping the two apart is the point — one is measured and one is
    /// chosen, and a number that is chosen should not be hiding inside a constant that claims otherwise.
    /// </remarks>
    [Fact]
    public void TheGravityScaleSlowsTheFallWithoutTouchingThePhysics()
    {
        RagdollSkeleton rig = RagdollRig.Rig();

        double Drop(double scale)
        {
            RagdollBody body = RagdollRig.Standing(rig, new Vector3d(0.0, 0.0, 200_000.0));
            for (int tick = 0; tick < 20; tick++)
            {
                body.Step(RagdollWorld.Empty, substeps: 8, scale);
            }

            return 200_000.0 - body.PositionOf(RagdollRig.Pelvis).Z;
        }

        // Distance under constant acceleration goes with the acceleration, so halving one halves the other.
        Assert.Equal(Drop(1.0) * 0.5, Drop(0.5), Drop(1.0) * 0.02);
        Assert.True(Drop(GameplayCVars.RagdollGravity.Value) < Drop(1.0));
        Assert.InRange(GameplayCVars.RagdollGravity.Value, 0.1f, 1f);
    }

    /// <summary>A body stops soon after it lands, not seconds later.</summary>
    /// <remarks>
    /// <b>The settling help is counted from first contact, not from death</b>, which is what makes this
    /// possible: a body still falling keeps all its energy, and one that has landed is losing it to
    /// whatever it landed on. Timing it from the moment of death instead meant a corpse got no help at
    /// all for the first two seconds — exactly the window in which it was visibly twitching on the
    /// ground.
    /// </remarks>
    [Fact]
    public void ABodyStopsSoonAfterItLands()
    {
        RagdollSkeleton rig = RagdollRig.Rig();
        RagdollWorld plane = RagdollWorld.Plane(0.0);
        RagdollBody body = RagdollRig.Standing(rig, new Vector3d(0.0, 0.0, 6_000.0));
        body.AddImpulse(RagdollRig.Chest, new Vector3d(0.0, 900.0, 0.0));

        int settled = -1;
        for (int tick = 0; tick < 200 && settled < 0; tick++)
        {
            body.Step(plane, substeps: 8, 0.7);
            if (body.IsAtRest)
            {
                settled = tick;
            }
        }

        Assert.True(settled >= 0, "The body never came to rest.");

        // Two seconds from a standing fall, all in. It was over three before the ramp was moved onto
        // contact and the confirmation latch shortened; what is left is mostly the fall itself.
        Assert.True(settled < 60, $"It took {settled} ticks — {settled / 30.0:F1}s — to settle.");
    }

    /// <summary>A body always comes to rest, whatever it was doing and however it was hit.</summary>
    /// <remarks>
    /// <b>A guarantee, not a measurement.</b> A solver of this shape has no proof that it converges, and
    /// a corpse on the ground is exactly the pile the XPBD paper says it cannot promise anything about —
    /// so a body that has not settled inside ten seconds is put to rest regardless. What a player would
    /// otherwise see is a corpse turning slowly on the spot for the rest of the mission, costing solver
    /// time the whole while.
    /// </remarks>
    [Fact]
    public void ABodyAlwaysComesToRest()
    {
        RagdollSkeleton rig = RagdollRig.Rig();
        RagdollWorld plane = RagdollWorld.Plane(0.0);

        // Counter-rotating hands: a hard spin about the vertical, which is the case sliding friction
        // cannot see at all because the contact points are barely moving.
        RagdollBody body = RagdollRig.Standing(rig, new Vector3d(0.0, 0.0, 2_000.0));
        body.AddImpulse(RagdollRig.LeftHand, new Vector3d(0.0, 2_000.0, 0.0));
        body.AddImpulse(RagdollRig.RightHand, new Vector3d(0.0, -2_000.0, 0.0));

        int settled = -1;
        for (int tick = 0; tick < RagdollBody.MaximumActiveTicks + 30; tick++)
        {
            body.Step(plane, substeps: 8);
            if (body.IsAtRest && settled < 0)
            {
                settled = tick;
            }
        }

        Assert.True(settled >= 0, "The body never came to rest.");
        Assert.True(
            settled <= RagdollBody.MaximumActiveTicks,
            $"It took {settled} ticks, past the backstop at {RagdollBody.MaximumActiveTicks}.");
    }

    /// <summary>An armed corpse still poses, and its weapon is simulated rather than drawn on a hand.</summary>
    /// <remarks>
    /// <b>Every other test here fights an unarmed body</b>, which is how a carried weapon growing the
    /// solver's arrays but not the skeleton's went unnoticed until a guard was shot holding one. A pose
    /// has one rotation per bone; the bodies are the bones plus whatever they are holding, and the two
    /// counts are not the same number.
    /// </remarks>
    [Fact]
    public void AnArmedCorpseStillPoses()
    {
        RagdollSkeleton rig = RagdollRig.Rig();
        RagdollBody body = RagdollRig.Armed(rig, new Vector3d(0.0, 0.0, 12_000.0));

        Assert.True(body.HasAttachment);
        Assert.Equal(rig.BoneCount, body.Pose().BoneCount);

        RagdollWorld plane = RagdollWorld.Plane(0.0);
        for (int tick = 0; tick < 200 && !body.IsAtRest; tick++)
        {
            body.Step(plane, substeps: 8);
            Assert.Equal(rig.BoneCount, body.Pose().BoneCount);
        }

        Assert.True(body.IsAtRest, "The armed body never settled.");

        // And the weapon came to rest ON the floor rather than through it -- the whole reason it is a
        // body at all. AttachmentPosition is relative to the body's frozen origin, the same frame the
        // drawn pose is in, so it has to be carried back into the world before it means a height.
        double world = body.Origin.Z + body.AttachmentPosition.Z;
        Assert.True(world > -RagdollBody.PenetrationSlop * 8.0, $"The weapon settled {world:F0} units down.");
    }

    /// <summary>A body thrown at a wall stops at it instead of going through it.</summary>
    /// <remarks>
    /// <b>The rewrite collided against the ground and nothing else</b> — the world ray cast that reaches
    /// buildings, crates and stairs sat unused, so a corpse thrown at a wall simply went into it. The
    /// ground query cannot stand in for it: it only ever answers downwards.
    /// </remarks>
    [Fact]
    public void ABodyThrownAtAWallStopsAtIt()
    {
        RagdollSkeleton rig = RagdollRig.Rig();

        // A floor, and an upright wall a metre and a half away facing the body.
        const double Wall = 6_000.0;
        RagdollWorld room = RagdollWorld.Room(0.0, Wall);
        RagdollBody body = RagdollRig.Standing(rig, new Vector3d(0.0, 0.0, 2_400.0));

        RagdollImpulse.Apply(
            body,
            RagdollRig.Chest,
            new Vector3d(0.0, 1.0, 0.0),
            RagdollImpulse.MomentumOf(rig, WeaponCategory.Shotgun, 3.0));

        double worst = 0.0;
        for (int tick = 0; tick < 200 && !body.IsAtRest; tick++)
        {
            body.Step(room, substeps: 8, 0.7);
            for (int bone = 0; bone < rig.BoneCount; bone++)
            {
                worst = Math.Max(worst, body.PositionOf(bone).Y - Wall);
            }
        }

        // Allowed as far past the wall as the deadband plus a bone's own reach, and no further. Before
        // walls existed at all this ran to several thousand.
        Assert.True(worst < 1024.0, $"A bone went {worst:F0} units into the wall.");

        // And it really did reach the wall, so the test is measuring a collision rather than a body that
        // never got there.
        double nearest = double.MaxValue;
        for (int bone = 0; bone < rig.BoneCount; bone++)
        {
            nearest = Math.Min(nearest, Wall - body.PositionOf(bone).Y);
        }

        Assert.True(nearest < 2048.0, $"The body stopped {nearest:F0} units short and never touched it.");
    }

    /// <summary>The same body, stepped the same way twice, produces bit-identical results.</summary>
    /// <remarks>
    /// Nothing in the solver may reach for a clock or a random number. The ragdoll does not feed the
    /// simulation, so it is not bound by the determinism contract in <c>PLAN.md</c> — but a
    /// non-reproducible one could not be tested at all, and a bug in it could not be reproduced either.
    /// </remarks>
    [Fact]
    public void TheSolverIsReproducible()
    {
        RagdollSkeleton rig = RagdollRig.Rig();
        RagdollWorld plane = RagdollWorld.Plane(0.0);

        Vector3d Run()
        {
            RagdollBody body = RagdollRig.Standing(rig, new Vector3d(0.0, 0.0, 12_000.0));
            body.AddImpulse(RagdollRig.Chest, new Vector3d(500.0, 200.0, 100.0));
            for (int tick = 0; tick < 120; tick++)
            {
                body.Step(plane, substeps: 8);
            }

            return body.PositionOf(RagdollRig.Head);
        }

        Assert.Equal(Run(), Run());
    }

    /// <summary>A settled body is not stepped again.</summary>
    /// <remarks>
    /// Nothing in this game ever removes a corpse, so without the latch every kill would cost solver
    /// time for the rest of the mission.
    /// </remarks>
    [Fact]
    public void ASettledBodyStopsCostingAnything()
    {
        RagdollSkeleton rig = RagdollRig.Rig();
        RagdollBody body = RagdollRig.Standing(rig, new Vector3d(0.0, 0.0, 4_000.0));
        RagdollWorld plane = RagdollWorld.Plane(0.0);

        for (int tick = 0; tick < 900 && !body.IsAtRest; tick++)
        {
            body.Step(plane, substeps: 8);
        }

        Assert.True(body.IsAtRest);

        Vector3d before = body.PositionOf(RagdollRig.Head);
        for (int tick = 0; tick < 100; tick++)
        {
            body.Step(plane, substeps: 8);
        }

        Assert.Equal(before, body.PositionOf(RagdollRig.Head));
    }

    /// <summary>A rig whose parents are not in index order is refused rather than silently mis-solved.</summary>
    [Fact]
    public void ARigThatCannotBeResolvedInOnePassIsRefused()
    {
        BoneSkeleton tangled = BoneSkeleton.Create(
            [1, BoneSkeleton.NoParent],
            [new Vector3f(0f, 0f, 100f), new Vector3f(0f, 0f, 0f)]);

        Assert.Throws<ArgumentException>(() => RagdollSkeleton.Build(tangled));
    }

    /// <summary>
    /// No bone weighs wildly more than the one it hangs off, and the body still weighs what it should.
    /// </summary>
    /// <remarks>
    /// The mass conditioning, which is the most effective anti-explosion measure in the whole solver: a
    /// chain solved one joint at a time diverges when a light link has to move a heavy sub-tree, and a
    /// rig taken at face value is full of exactly that.
    /// </remarks>
    [Fact]
    public void NoBoneWildlyOutweighsItsParentAndTheBodyStillWeighsWhatItShould()
    {
        RagdollSkeleton rig = RagdollRig.Rig();

        double total = 0.0;
        for (int bone = 0; bone < rig.BoneCount; bone++)
        {
            total += 1.0 / rig.BoneAt(bone).InverseMass;

            int parent = rig.Skeleton.Parents[bone];
            if (parent == BoneSkeleton.NoParent)
            {
                continue;
            }

            double ratio = rig.BoneAt(parent).InverseMass / rig.BoneAt(bone).InverseMass;
            Assert.InRange(
                ratio, 1.0 / RagdollSkeleton.MaximumMassRatio - 1e-6, RagdollSkeleton.MaximumMassRatio + 1e-6);
        }

        Assert.Equal(RagdollSkeleton.BodyMassInKilograms, total, 1e-4);
    }

    /// <summary>Reading an angular velocity back out of a turn recovers the turn that was made.</summary>
    /// <remarks>
    /// Including past a half turn, which is the case the sign of the scalar part exists to handle — a
    /// body that rotated a little past 180° must not read as having spun almost all the way back.
    /// </remarks>
    /// <remarks>
    /// Measured at the substep size the solver actually runs at — a sixteenth of a tick. The orientation
    /// update is a first-order approximation, so it is only faithful for small turns per step, and
    /// testing it over a whole tick would be testing a regime nothing uses.
    /// </remarks>
    [Theory]
    [InlineData(0.4)]
    [InlineData(1.7)]
    [InlineData(3.0)]
    public void AnAngularVelocityRoundTripsThroughAnOrientation(double radians)
    {
        const double Step = 1.0 / 16.0;
        Vector3d spin = new Vector3d(0.3, -0.8, 0.5).Normalized() * radians;
        Quaternion32 start = Quaternion32.FromAxisAngle(new Vector3f(0.2f, 0.9f, -0.3f), 0.6f).Normalized();

        Quaternion32 turned = RagdollSolver.Advance(start, spin, Step);
        Vector3d recovered = RagdollSolver.Recover(start, turned, Step);

        Assert.True(Vector3d.Dot(recovered.Normalized(), spin.Normalized()) > 0.999);
        Assert.InRange(recovered.Length / radians, 0.95, 1.05);
    }

    /// <summary>The swing-twist split puts a pure twist wholly in the twist and a pure swing wholly in the swing.</summary>
    [Fact]
    public void ASwingTwistSplitSeparatesTheTwo()
    {
        Vector3f axis = Vector3f.UnitZ;

        (Quaternion32 swing, Quaternion32 twist) = RagdollSolver.SwingTwist(
            Quaternion32.FromAxisAngle(axis, 0.9f), axis);
        Assert.Equal(1f, MathF.Abs(swing.W), 1e-3);
        Assert.Equal(MathF.Cos(0.45f), MathF.Abs(twist.W), 1e-3);

        (swing, twist) = RagdollSolver.SwingTwist(
            Quaternion32.FromAxisAngle(Vector3f.UnitX, 0.7f), axis);
        Assert.Equal(1f, MathF.Abs(twist.W), 1e-3);
        Assert.Equal(MathF.Cos(0.35f), MathF.Abs(swing.W), 1e-3);
    }

    /// <summary>An angle inside its range needs no correction; one outside is brought back to the limit.</summary>
    [Fact]
    public void AnAngleLimitCorrectsOnlyWhatIsOutsideIt()
    {
        Vector3f axis = Vector3f.UnitZ;
        Vector3f from = Vector3f.UnitX;

        Vector3f inside = RotateAboutZ(from, 0.3f);
        Assert.False(RagdollSolver.LimitAngle(axis, from, inside, -0.5f, 0.5f, out _));

        Vector3f outside = RotateAboutZ(from, 1.2f);
        Assert.True(RagdollSolver.LimitAngle(axis, from, outside, -0.5f, 0.5f, out Vector3f correction));

        // The correction closes the overshoot and no more: it turns the far vector back to the limit,
        // not past it and not to the middle of the range.
        Assert.Equal(0.7f, MathF.Asin(Math.Clamp(correction.Length, -1f, 1f)), 0.05f);
    }

    private static Vector3f Turn(RagdollBody body, int bone, Vector3f local) =>
        RagdollSolver.Rotate(BonePose.ToMatrix(body.OrientationOf(bone)), local);

    private static Vector3f RotateAboutZ(Vector3f value, float radians) => new(
        (value.X * MathF.Cos(radians)) - (value.Y * MathF.Sin(radians)),
        (value.X * MathF.Sin(radians)) + (value.Y * MathF.Cos(radians)),
        value.Z);
}
