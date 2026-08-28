using System.Collections;
using System.Reflection;
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
using OpenIGI.Game;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// Covers the renderer callback that <see cref="PlayerCamera"/> gives a player's retail hit-scan round.
/// It deliberately tests only the renderer seam: scope state and the input-to-fire transition belong to
/// the camera's integration coverage, while this callback owns the body collision work.
/// </summary>
public sealed class SoldierRendererHitscanTests
{
    private const double Metre = WorldScene.UnitsPerMetre;

    private static readonly FieldInfo GroupsField = typeof(SoldierRenderer)
        .GetField("_groups", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo RaySphereMethod = typeof(SoldierRenderer)
        .GetMethod("RaySphere", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo SnapToGroundMethod = typeof(SoldierRenderer)
        .GetMethod("SnapToGround", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly double PickCullRadiusUnits = (double)typeof(SoldierRenderer)
        .GetField("PickCullRadiusUnits", BindingFlags.Static | BindingFlags.NonPublic)!
        .GetRawConstantValue()!;
    private static readonly double PickChestUnits = (double)typeof(SoldierRenderer)
        .GetField("PickChestUnits", BindingFlags.Static | BindingFlags.NonPublic)!
        .GetRawConstantValue()!;

    private const double PresentationOffsetMetres = 8.0;

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

        Vector3d displayedRoot = Get<Vector3d>(target.Member, "DrawnWhere") * Metre;
        Matrix3f displayedOrientation = Get<Matrix3f>(target.Member, "DrawnOrientation");
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

        Vector3d root = Get<Vector3d>(target.Member, "DrawnWhere") * Metre;
        Matrix3f orientation = Get<Matrix3f>(target.Member, "DrawnOrientation");
        BlendedOnlyRay ray = FindBlendedOnlyRay(target.View.Bones.Skeleton, target.Mesh, root, orientation);

        // These are the exact two model-space pose snapshots the player collision path receives from
        // the animated mesh. The test fixture injects a deliberately separated pair so its visible
        // midpoint cannot accidentally be hit through either endpoint pose.
        Set(target.Member, "SkinnedPose", ray.Current);
        Set(target.Member, "PreviousSkinnedPose", ray.Previous);
        Set(target.Member, "PoseBlend", 0.5f);

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

    private static MeshHitRay FindAnyMeshHitRay(SoldierRenderer renderer)
    {
        // Select against the skinned mesh itself, not TryPick: the test must not use the renderer's other
        // consumer of the broad-phase gate to decide which round counts as a hit.
        foreach ((SoldierView soldier, MefCollisionMesh mesh) in MeshBackedSoldiers(renderer))
        {
            Vector3d feet = FeetOnGround(renderer, soldier);
            Matrix3f orientation = soldier.Orientation;
            Vector3f facing = new(orientation.M12, orientation.M22, orientation.M32);
            Vector3d bodySphereCentre = new(feet.X, feet.Y, feet.Z + PickChestUnits);

            foreach (double height in new[] { 0.5, 0.9, 1.3, 1.7 })
            {
                Vector3d aim = new(feet.X, feet.Y, feet.Z + (height * Metre));
                foreach ((double X, double Y) offset in new[]
                {
                    (-12.0, 0.0),
                    (12.0, 0.0),
                    (0.0, -12.0),
                    (0.0, 12.0),
                })
                {
                    Vector3d origin = new(aim.X + (offset.X * Metre), aim.Y + (offset.Y * Metre), aim.Z);
                    if (HumanHitTest.TryCastMesh(
                        origin,
                        aim,
                        feet,
                        facing,
                        mesh,
                        soldier.Pose(),
                        soldier.RootSample.Z,
                        out HumanHit hit))
                    {
                        return new MeshHitRay(origin, aim, hit, soldier, mesh, feet, facing, bodySphereCentre);
                    }
                }
            }
        }

        throw new Xunit.Sdk.XunitException("Mission four did not provide a ray to any loaded soldier collision mesh.");
    }

    private static MeshHitRay FindMeshHitRay(
        SoldierView soldier,
        MefCollisionMesh mesh,
        Vector3d feet,
        Vector3f facing)
    {
        Vector3d bodySphereCentre = new(feet.X, feet.Y, feet.Z + PickChestUnits);
        foreach (double height in new[] { 0.5, 0.9, 1.3, 1.7 })
        {
            Vector3d aim = new(feet.X, feet.Y, feet.Z + (height * Metre));
            foreach ((double X, double Y) offset in new[]
            {
                (-12.0, 0.0),
                (12.0, 0.0),
                (0.0, -12.0),
                (0.0, 12.0),
            })
            {
                Vector3d origin = new(aim.X + (offset.X * Metre), aim.Y + (offset.Y * Metre), aim.Z);
                if (HumanHitTest.TryCastMesh(
                    origin,
                    aim,
                    feet,
                    facing,
                    mesh,
                    soldier.Pose(),
                    soldier.RootSample.Z,
                    out HumanHit hit))
                {
                    return new MeshHitRay(origin, aim, hit, soldier, mesh, feet, facing, bodySphereCentre);
                }
            }
        }

        throw new Xunit.Sdk.XunitException($"Soldier '{soldier.Soldier.Name}' did not provide a ray to its collision mesh.");
    }

    private static BlendedOnlyRay FindBlendedOnlyRay(
        BoneSkeleton skeleton,
        MefCollisionMesh mesh,
        Vector3d root,
        Matrix3f orientation)
    {
        Quaternion32[] identity = Enumerable.Repeat(Quaternion32.Identity, skeleton.BoneCount).ToArray();
        BonePose baseline = BonePose.Evaluate(skeleton, identity);

        foreach (MefCollisionFace face in mesh.Faces)
        {
            Vector3d a = SkinVertex(mesh, face.I0, baseline);
            Vector3d b = SkinVertex(mesh, face.I1, baseline);
            Vector3d c = SkinVertex(mesh, face.I2, baseline);
            Vector3d normal = Vector3d.Cross(b - a, c - a);
            if (normal.LengthSquared <= 1.0)
            {
                continue;
            }

            normal = Unit(normal);
            Vector3d tangent = Vector3d.Cross(normal, Vector3d.UnitZ);
            if (tangent.LengthSquared <= 1e-12)
            {
                tangent = Vector3d.Cross(normal, Vector3d.UnitY);
            }
            tangent = Unit(tangent) * (1.5 * Metre);

            BonePose current = BonePose.Evaluate(
                skeleton,
                identity,
                new Vector3f((float)tangent.X, (float)tangent.Y, (float)tangent.Z));
            BonePose previous = BonePose.Evaluate(
                skeleton,
                identity,
                new Vector3f((float)-tangent.X, (float)-tangent.Y, (float)-tangent.Z));
            Vector3d centre = (a + b + c) / 3.0;
            Vector3d localOrigin = centre + (normal * (6.0 * Metre));
            Vector3d localEnd = centre - (normal * (6.0 * Metre));

            if (HumanHitTest.TryCastMesh(localOrigin, localEnd, Vector3d.Zero, Vector3f.UnitY, mesh, current, 0.0, out _)
                || HumanHitTest.TryCastMesh(localOrigin, localEnd, Vector3d.Zero, Vector3f.UnitY, mesh, previous, 0.0, out _)
                || !HumanHitTest.TryCastMeshBlended(
                    localOrigin,
                    localEnd,
                    Vector3d.Zero,
                    Vector3f.UnitY,
                    mesh,
                    current,
                    previous,
                    Matrix3f.Identity,
                    previousShare: 0.5f,
                    out _))
            {
                continue;
            }

            return new BlendedOnlyRay(
                ToWorld(localOrigin, root, orientation),
                ToWorld(localEnd, root, orientation),
                current,
                previous);
        }

        throw new Xunit.Sdk.XunitException("Retail human mesh did not provide a triangle unique to its blended pose.");
    }

    private static Vector3d SkinVertex(MefCollisionMesh mesh, int vertex, BonePose pose)
    {
        int bone = mesh.VertexBones[vertex];
        if ((uint)bone >= (uint)pose.BoneCount)
        {
            bone = 0;
        }

        Vector3f point = mesh.Vertices[vertex];
        Matrix3f rotation = pose.Rotations[bone];
        Vector3f translation = pose.Positions[bone];
        return new Vector3d(
            (rotation.M11 * point.X) + (rotation.M12 * point.Y) + (rotation.M13 * point.Z) + translation.X,
            (rotation.M21 * point.X) + (rotation.M22 * point.Y) + (rotation.M23 * point.Z) + translation.Y,
            (rotation.M31 * point.X) + (rotation.M32 * point.Y) + (rotation.M33 * point.Z) + translation.Z);
    }

    private static Vector3d ToWorld(Vector3d local, Vector3d root, Matrix3f orientation)
    {
        Vector3f turned = orientation.Transposed().Transform(new Vector3f(
            (float)local.X,
            (float)local.Y,
            (float)local.Z));
        return new Vector3d(root.X + turned.X, root.Y + turned.Y, root.Z + turned.Z);
    }

    private static MeshBackedSoldier FirstMeshBackedSoldier(SoldierRenderer renderer)
    {
        foreach (object group in Assert.IsAssignableFrom<IEnumerable>(GroupsField.GetValue(renderer)))
        {
            object? candidate = group.GetType()
                .GetProperty("CollisionMesh", BindingFlags.Instance | BindingFlags.Public)!
                .GetValue(group);
            if (candidate is not MefCollisionMesh mesh)
            {
                continue;
            }

            foreach (object member in Assert.IsAssignableFrom<IEnumerable>(
                group.GetType().GetProperty("Soldiers", BindingFlags.Instance | BindingFlags.Public)!.GetValue(group)))
            {
                SoldierView view = Assert.IsType<SoldierView>(
                    member.GetType().GetProperty("View", BindingFlags.Instance | BindingFlags.Public)!.GetValue(member));
                return new MeshBackedSoldier(view, mesh, member);
            }
        }

        throw new Xunit.Sdk.XunitException("Mission four did not provide a loaded soldier collision mesh.");
    }

    private static void KeepOnlyTarget(SoldierRenderer renderer, object target)
    {
        foreach (object group in Assert.IsAssignableFrom<IEnumerable>(GroupsField.GetValue(renderer)))
        {
            IList members = Assert.IsAssignableFrom<IList>(
                group.GetType().GetProperty("Soldiers", BindingFlags.Instance | BindingFlags.Public)!.GetValue(group));
            for (int i = members.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(members[i], target))
                {
                    members.RemoveAt(i);
                }
            }
        }
    }

    private static void MovePresentationAwayFromSimulation(SoldierRenderer renderer, object member, bool smooth)
    {
        Vector3d simulatedRoot = Get<Vector3d>(member, "TickWhere");
        Set(member, "PrevTickWhere", new Vector3d(
            simulatedRoot.X + PresentationOffsetMetres,
            simulatedRoot.Y,
            simulatedRoot.Z));
        if (smooth)
        {
            // Interpolate a turn too: the collision pose must follow the same DrawnOrientation as the
            // displayed mesh, not merely the displayed translation.
            Set(member, "PrevTickFacing", Get<float>(member, "TickFacing") + (MathF.PI * 0.5f));
        }
        Set(member, "HasTickState", true);
        renderer.BeginFrame(tickFraction: 0.5f, smooth);
    }

    private static T Get<T>(object instance, string property) => Assert.IsType<T>(
        instance.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public)!.GetValue(instance));

    private static void Set<T>(object instance, string property, T value) =>
        instance.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public)!.SetValue(instance, value);

    private static IEnumerable<(SoldierView View, MefCollisionMesh Mesh)> MeshBackedSoldiers(SoldierRenderer renderer)
    {
        foreach (object group in Assert.IsAssignableFrom<IEnumerable>(GroupsField.GetValue(renderer)))
        {
            object? candidate = group.GetType()
                .GetProperty("CollisionMesh", BindingFlags.Instance | BindingFlags.Public)!
                .GetValue(group);
            if (candidate is not MefCollisionMesh mesh)
            {
                continue;
            }

            IEnumerable members = Assert.IsAssignableFrom<IEnumerable>(
                group.GetType().GetProperty("Soldiers", BindingFlags.Instance | BindingFlags.Public)!.GetValue(group));
            foreach (object member in members)
            {
                SoldierView view = Assert.IsType<SoldierView>(
                    member.GetType().GetProperty("View", BindingFlags.Instance | BindingFlags.Public)!.GetValue(member));
                yield return (view, mesh);
            }
        }
    }

    private static Vector3d FeetOnGround(SoldierRenderer renderer, SoldierView soldier) => new(
        soldier.Position.X,
        soldier.Position.Y,
        Assert.IsType<double>(SnapToGroundMethod.Invoke(
            renderer,
            [soldier.Position.X, soldier.Position.Y, soldier.Position.Z])));

    private static (bool Eligible, double EntryDistance) HitSegmentMayReachSphere(
        Vector3d origin,
        Vector3d direction,
        Vector3d centre,
        double radius,
        double reach)
    {
        object?[] arguments = [origin, direction, centre, radius, reach, 0.0];
        bool eligible = Assert.IsType<bool>(RaySphereMethod.Invoke(null, arguments));
        return (eligible, Assert.IsType<double>(arguments[5]));
    }

    private static bool IsInsideBodySphere(Vector3d point, Vector3d centre)
    {
        double dx = point.X - centre.X;
        double dy = point.Y - centre.Y;
        double dz = point.Z - centre.Z;
        return (dx * dx) + (dy * dy) + (dz * dz) <= PickCullRadiusUnits * PickCullRadiusUnits;
    }

    private static Vector3d Unit(Vector3d vector)
    {
        double length = Math.Sqrt((vector.X * vector.X) + (vector.Y * vector.Y) + (vector.Z * vector.Z));
        Assert.True(length > 0.0);
        return new Vector3d(vector.X / length, vector.Y / length, vector.Z / length);
    }

    private static Vector3d Offset(Vector3d point, Vector3d direction, double distance) => new(
        point.X + (direction.X * distance),
        point.Y + (direction.Y * distance),
        point.Z + (direction.Z * distance));

    private static Dictionary<SoldierView, AiSoldier>.KeyCollection RendererSoldiers(SoldierRenderer renderer) =>
        Assert.IsType<Dictionary<SoldierView, AiSoldier>>(
            typeof(SoldierRenderer)
                .GetField("_brains", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(renderer))
            .Keys;

    private readonly record struct MeshHitRay(
        Vector3d Origin,
        Vector3d End,
        HumanHit MeshHit,
        SoldierView View,
        MefCollisionMesh Mesh,
        Vector3d Feet,
        Vector3f Facing,
        Vector3d BodySphereCentre);

    private readonly record struct MeshBackedSoldier(SoldierView View, MefCollisionMesh Mesh, object Member);

    private readonly record struct BlendedOnlyRay(
        Vector3d Origin,
        Vector3d End,
        BonePose Current,
        BonePose Previous);

    private static Scenario CreateScenario()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        AiTaskTypes.RegisterAll(loop.Types);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);

        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        LoadedMission mission = new MissionLoader(
            missions,
            fileSystem,
            resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(4);

        SoldierRenderer renderer = new(new RecordingGraphicsDevice(), resources);
        Assert.True(renderer.Load(mission, root) > 0);
        Assert.True(renderer.Animate(new Dictionary<int, GraphFile>(), levelRoot: root) > 0);
        return new Scenario(renderer);
    }

    private sealed class Scenario(SoldierRenderer renderer) : IDisposable
    {
        public SoldierRenderer Renderer { get; } = renderer;

        public void Dispose() => Renderer.Dispose();
    }
}
