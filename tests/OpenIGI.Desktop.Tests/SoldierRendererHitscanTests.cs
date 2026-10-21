using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Presentation.Igi1;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using static OpenIGI.Desktop.Tests.MeshHitRays;
using static OpenIGI.Desktop.Tests.SoldierRendererProbe;
using static OpenIGI.Desktop.Tests.SoldierRendererScenario;
using Scenario = OpenIGI.Desktop.Tests.SoldierRendererScenario;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// Covers the renderer callback that <see cref="PlayerCamera"/> gives a player's retail hit-scan round.
/// It deliberately tests only the renderer seam: scope state and the input-to-fire transition belong to
/// the camera's integration coverage, while this callback owns the body collision work.
/// </summary>
[Collection(SoldierRendererScenario.CollectionName)]
public sealed class SoldierRendererHitscanTests
{
    // The global rows a soldier hull names: NormalMetal, Flesh, BloodyFlesh, Runway (material/material.qvm).
    private static readonly int[] BodyMaterials = [6, 8, 9, 12];

    [RequiresGameDataFact]
    public void DefaultPlayerHitscanPathRegistersTheMeshItAimsAt()
    {
        // Tactical ballistics is opt-in, so a normal player shot resolves synchronously through this
        // renderer-owned callback rather than spawning an in-flight projectile.
        Assert.False(GameplayCVars.Ballistics.DefaultValue);

        using Scenario scenario = CreateScenario();
        MeshHitRay ray = FindAnyMeshHitRay(scenario.Renderer);

        Assert.True(scenario.Renderer.TryShoot(ray.Origin, ray.End, damageFactor: 0.5f, WeaponCategory.Gun, out HumanHit hit));
        Assert.True(hit.Distance > 0.0);
        Assert.InRange(hit.Zone, 0, HumanHitZoneTable.Soldier.Count - 1);
    }

    /// <summary>
    /// A round that goes through a body is cast again from just inside it, so the mesh cast must say which
    /// way it crosses the skin: in from outside, out from inside -- <c>0x47B750</c>'s <c>+76</c> and
    /// <c>+80</c> slots. The face's own material comes with it, for the round's power to be tested against.
    /// </summary>
    [RequiresGameDataFact]
    public void TheMeshCastTellsAWayInFromAWayOut()
    {
        using Scenario scenario = CreateScenario();
        MeshHitRay ray = FindAnyMeshHitRay(scenario.Renderer);
        BonePose pose = ray.View.Pose();

        Assert.False(ray.MeshHit.IsExit);
        Assert.Contains(ray.MeshHit.Material, BodyMaterials);

        IReadOnlyList<HumanBodyCrossing> crossings = HumanHitTest.CastMeshCrossings(
            ray.Origin, ray.End + ((ray.End - ray.Origin) * 0.5), ray.Feet, ray.Facing, ray.Mesh, pose, ray.View.RootSample.Z);
        HumanBodyCrossing last = crossings[^1];
        Assert.False(last.Entering);

        Vector3d heading = (ray.End - ray.Origin) / (ray.End - ray.Origin).Length;
        Vector3d inside = last.Hit.Position - (heading * GunShotBallistics.PenetrationStepOffUnits);
        Assert.True(HumanHitTest.TryCastMesh(
            inside,
            inside + (heading * Metre),
            ray.Feet,
            ray.Facing,
            ray.Mesh,
            pose,
            ray.View.RootSample.Z,
            out HumanHit way));
        Assert.True(way.IsExit);
        Assert.Equal(GunShotBallistics.PenetrationStepOffUnits, way.Distance, 1.0);
    }

    [RequiresGameDataFact]
    public void MissionLoadPrimesSoldierCollisionMeshesBeforeTheFirstShot()
    {
        // Collision chunks used to be decoded by TryShoot's first candidate. That made the first
        // scoped round pay an avoidable synchronous allocation/parse on the render thread. Loading the
        // mission is the stable cache boundary, so every drawable group must already have attempted its
        // one-time read before a shot can be offered.
        using Scenario scenario = CreateScenario();
        int groups = 0;
        int prepared = 0;
        foreach (SoldierGroup group in scenario.Renderer.GroupsForTesting)
        {
            groups++;
            bool wasPrepared = group.Body.CollisionMeshPrepared;
            prepared += wasPrepared ? 1 : 0;
        }

        Assert.True(groups > 0, "Mission four must load at least one soldier group.");
        Assert.Equal(groups, prepared);

        if (GameplayCVars.Ragdoll.Value)
        {
            int rigs = scenario.Renderer.Simulation.RagdollRigs.Count;
            Assert.True(rigs > 0, "Ragdoll rigs must be prepared before the first lethal shot.");
        }
    }

    [RequiresGameDataFact]
    public void SmoothedPlayerHitscanRegistersTheMeshAtItsDisplayedPosition()
    {
        // A shot is resolved on the input tick, but the target under the crosshair is the renderer's last
        // r_smooth frame. Make those two positions materially different, then aim through the exact mesh
        // the frame presents. This exercises the real player-shot seam rather than a private collision helper.
        using Scenario scenario = CreateScenario();
        MeshBackedSoldier target = FirstMeshBackedSoldier(scenario.Renderer);
        KeepOnlyTarget(scenario.Renderer, target.Member);
        MovePresentationAwayFromSimulation(scenario.Renderer, target.Member, smooth: true);

        Vector3d displayedRoot = target.Member.DrawnWhere * Metre;
        Matrix3f displayedOrientation = target.Member.DrawnOrientation;
        Vector3f displayedFacing = new(
            displayedOrientation.M12,
            displayedOrientation.M22,
            displayedOrientation.M32);
        Vector3d displayedFeet = new(
            displayedRoot.X,
            displayedRoot.Y,
            displayedRoot.Z - target.View.RootSample.Z);
        Matrix3f simulationOrientation = target.View.Orientation;
        MeshHitRay ray;
        try
        {
            // The mesh helper takes SoldierView's faced pose. Recreate the interpolation heading here to
            // independently find the mesh the display shows, then restore the simulation state before
            // invoking the public player-shot seam.
            target.View.PlaceAt(target.View.Position, displayedOrientation);
            ray = FindMeshHitRay(target.View, target.Mesh, displayedFeet, displayedFacing);
        }
        finally
        {
            target.View.PlaceAt(target.View.Position, simulationOrientation);
        }

        Assert.NotEqual(target.View.Position.X, displayedFeet.X);

        Assert.True(scenario.Renderer.TryShoot(ray.Origin, ray.End, damageFactor: 0.5f, WeaponCategory.Gun, out _));
    }

    [RequiresGameDataFact]
    public void SmoothedPlayerHitscanUsesTheRenderedBlendedCollisionVertices()
    {
        // The renderer interpolates the two already-skinned GPU streams. This ray crosses only the
        // halfway triangle: it misses both source poses, so it catches an implementation that merely
        // follows the displayed body transform while still tracing the current limb pose.
        using Scenario scenario = CreateScenario();
        MeshBackedSoldier target = FirstMeshBackedSoldier(scenario.Renderer);
        KeepOnlyTarget(scenario.Renderer, target.Member);
        MovePresentationAwayFromSimulation(scenario.Renderer, target.Member, smooth: true);

        Vector3d root = target.Member.DrawnWhere * Metre;
        Matrix3f orientation = target.Member.DrawnOrientation;
        BlendedOnlyRay ray = FindBlendedOnlyRay(target.View.Bones.Skeleton, target.Mesh, root, orientation);

        // These are the exact two model-space pose snapshots the player collision path receives from
        // the animated mesh. The test fixture injects a deliberately separated pair so its visible
        // midpoint cannot accidentally be hit through either endpoint pose.
        target.Member.SkinnedPose = ray.Current;
        target.Member.PreviousSkinnedPose = ray.Previous;
        target.Member.PoseBlend = 0.5f;

        Assert.True(scenario.Renderer.TryShoot(ray.Origin, ray.End, damageFactor: 0.5f, WeaponCategory.Gun, out _));
    }

    [RequiresGameDataFact]
    public void UnsmoothedPlayerHitscanUsesTheCurrentSimulationPosition()
    {
        // r_smooth is optional. Even when the old tick pair differs, the off branch must retain the
        // original current-tick collision point rather than reading stale presentation state.
        using Scenario scenario = CreateScenario();
        MeshBackedSoldier target = FirstMeshBackedSoldier(scenario.Renderer);
        KeepOnlyTarget(scenario.Renderer, target.Member);
        MovePresentationAwayFromSimulation(scenario.Renderer, target.Member, smooth: false);

        Vector3d currentFeet = FeetOnGround(scenario.Renderer, target.View);
        Matrix3f orientation = target.View.Orientation;
        MeshHitRay ray = FindMeshHitRay(
            target.View,
            target.Mesh,
            currentFeet,
            new Vector3f(orientation.M12, orientation.M22, orientation.M32));

        Assert.True(scenario.Renderer.TryShoot(ray.Origin, ray.End, damageFactor: 0.5f, WeaponCategory.Gun, out _));
    }

    [Fact]
    public void MeshHitBroadPhaseRejectsABodyBesideTheFiniteShotSegment()
    {
        // This is the guard before HumanHitTest.TryCastMesh skins a collision mesh. A body may be ahead
        // along the ray but still be too far to the side for this round to touch it.
        double radius = PickCullRadiusUnits;
        (bool eligible, _) = HitSegmentMayReachSphere(
            origin: new Vector3d(0.0, 0.0, 0.0),
            direction: new Vector3d(1.0, 0.0, 0.0),
            centre: new Vector3d(radius * 4.0, radius * 2.0, 0.0),
            radius,
            reach: radius * 8.0);

        Assert.False(eligible);
    }

    [Fact]
    public void MeshHitBroadPhaseRejectsABodyBeyondTheFiniteShotSegment()
    {
        // The broad phase accepts a *segment*, not an infinite crosshair ray. This avoids skinning every
        // guard that happens to stand further downrange than the round's actual end point.
        double radius = PickCullRadiusUnits;
        (bool eligible, _) = HitSegmentMayReachSphere(
            origin: new Vector3d(0.0, 0.0, 0.0),
            direction: new Vector3d(1.0, 0.0, 0.0),
            centre: new Vector3d(radius * 3.0, 0.0, 0.0),
            radius,
            reach: radius);

        Assert.False(eligible);
    }

    [RequiresGameDataFact]
    public void PlayerRoundStartingInsideTheBodySphereStillReachesItsMesh()
    {
        using Scenario scenario = CreateScenario();
        MeshHitRay longRay = FindAnyMeshHitRay(scenario.Renderer);
        Vector3d direction = Unit(longRay.End - longRay.Origin);
        Vector3d origin = Offset(longRay.MeshHit.Position, direction, -0.5 * Metre);
        Vector3d end = Offset(longRay.MeshHit.Position, direction, 0.5 * Metre);

        Assert.True(IsInsideBodySphere(origin, longRay.BodySphereCentre));
        Assert.True(HumanHitTest.TryCastMesh(
            origin,
            end,
            longRay.Feet,
            longRay.Facing,
            longRay.Mesh,
            longRay.View.Pose(),
            longRay.View.RootSample.Z,
            out _));

        Assert.True(scenario.Renderer.TryShoot(origin, end, damageFactor: 0.5f, WeaponCategory.Gun, out _));
    }

    /// <summary>
    /// Zone 15 is decided by where the round STRUCK, about the ORIGIN: 0x489C60 promotes a front hit only when the hit
    /// record's point is within 15 cm of the point its bone-4 lookup returns -- and that lookup (0x414F40 -> 0x4D0930)
    /// writes zeros, so the point is Human+32 itself. A round to the head is never promoted.
    /// </summary>
    [RequiresGameDataFact]
    public void ZoneFifteenIsScoredAboutTheOriginNotTheHead()
    {
        using Scenario scenario = CreateScenario();
        int promoted = 0;
        foreach ((SoldierView soldier, MefCollisionMesh mesh) in MeshBackedSoldiers(scenario.Renderer))
        {
            Vector3d feet = FeetOnGround(scenario.Renderer, soldier);
            Matrix3f orientation = soldier.Orientation;
            Vector3f facing = new(orientation.M12, orientation.M22, orientation.M32);
            BonePose pose = soldier.Pose();
            Vector3d origin = new(feet.X, feet.Y, feet.Z + soldier.RootSample.Z);

            for (double height = 0.6; height <= 1.9; height += 0.05)
            {
                for (double across = -0.4; across <= 0.4; across += 0.05)
                {
                    Vector3d aim = new(
                        feet.X + (facing.Y * across * Metre),
                        feet.Y - (facing.X * across * Metre),
                        feet.Z + (height * Metre));
                    Vector3d from = new(
                        aim.X + (facing.X * 12.0 * Metre), aim.Y + (facing.Y * 12.0 * Metre), aim.Z);
                    Vector3d end = new(
                        aim.X - (facing.X * 2.0 * Metre), aim.Y - (facing.Y * 2.0 * Metre), aim.Z);
                    if (!HumanHitTest.TryCastMesh(
                            from, end, feet, facing, mesh, pose, soldier.RootSample.Z, out HumanHit hit)
                        || hit.Zone != HumanDamage.ProximityZone)
                    {
                        continue;
                    }

                    promoted++;
                    Assert.True(
                        (hit.Position - origin).Length < HumanDamage.HeadProximityUnits,
                        $"{soldier.Soldier.Name}: a zone-15 hit struck bone {hit.Bone} "
                            + $"{(hit.Position - origin).Length / Metre:0.00} m from the origin");
                    Assert.True(height < 1.4, $"{soldier.Soldier.Name}: a round at {height:0.00} m was promoted");
                }
            }
        }

        Assert.True(promoted > 0, "no ray reached any soldier's origin, so the rule was never exercised");
    }

    [RequiresGameDataFact]
    public void HitscanRayPointingAwayFromEverySoldierDoesNotRegister()
    {
        using Scenario scenario = CreateScenario();
        SoldierView[] soldiers = RendererSoldiers(scenario.Renderer).ToArray();
        double westOfEverySoldier = soldiers.Min(soldier => soldier.Position.X) - (100 * Metre);
        Vector3d origin = new(westOfEverySoldier, 0.0, 0.0);
        Vector3d end = new(westOfEverySoldier - (100 * Metre), 0.0, 0.0);

        Assert.False(scenario.Renderer.TryShoot(origin, end, damageFactor: 0.5f, WeaponCategory.Gun, out _));
    }
}
