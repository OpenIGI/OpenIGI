using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Igi1;
using Xunit;
using static OpenIGI.Desktop.Tests.SoldierRendererProbe;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// Finds rounds that reach a soldier's skinned collision mesh, against the mesh itself rather than the
/// renderer's pick, and the small geometry the hit-scan tests reason with.
/// </summary>
internal static class MeshHitRays
{
    internal const double Metre = WorldScene.UnitsPerMetre;

    internal const double PickCullRadiusUnits = SoldierSimulation.PickCullRadiusUnits;
    internal const double PickChestUnits = SoldierSimulation.PickChestUnits;

    internal static MeshHitRay FindAnyMeshHitRay(
        SoldierRenderer renderer,
        Func<SoldierView, bool>? accepts = null)
    {
        // Select against the skinned mesh itself, not TryPick: the test must not use the renderer's other
        // consumer of the broad-phase gate to decide which round counts as a hit.
        foreach ((SoldierView soldier, MefCollisionMesh mesh) in MeshBackedSoldiers(renderer))
        {
            if (accepts is not null && !accepts(soldier))
            {
                continue;
            }

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

    internal static MeshHitRay FindMeshHitRay(
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

    internal static BlendedOnlyRay FindBlendedOnlyRay(
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

    internal static (bool Eligible, double EntryDistance) HitSegmentMayReachSphere(
        Vector3d origin,
        Vector3d direction,
        Vector3d centre,
        double radius,
        double reach)
    {
        bool eligible = SoldierSimulation.RaySphere(origin, direction, centre, radius, reach, out double entry);
        return (eligible, entry);
    }

    internal static bool IsInsideBodySphere(Vector3d point, Vector3d centre)
    {
        double dx = point.X - centre.X;
        double dy = point.Y - centre.Y;
        double dz = point.Z - centre.Z;
        return (dx * dx) + (dy * dy) + (dz * dz) <= PickCullRadiusUnits * PickCullRadiusUnits;
    }

    internal static Vector3d Unit(Vector3d vector)
    {
        double length = Math.Sqrt((vector.X * vector.X) + (vector.Y * vector.Y) + (vector.Z * vector.Z));
        Assert.True(length > 0.0);
        return new Vector3d(vector.X / length, vector.Y / length, vector.Z / length);
    }

    internal static Vector3d Offset(Vector3d point, Vector3d direction, double distance) => new(
        point.X + (direction.X * distance),
        point.Y + (direction.Y * distance),
        point.Z + (direction.Z * distance));
}

/// <summary>A round found to reach a soldier's collision mesh, with what it was cast against.</summary>
internal readonly record struct MeshHitRay(
    Vector3d Origin,
    Vector3d End,
    HumanHit MeshHit,
    SoldierView View,
    MefCollisionMesh Mesh,
    Vector3d Feet,
    Vector3f Facing,
    Vector3d BodySphereCentre);

/// <summary>A round that crosses only the halfway blend of two poses, and the two poses.</summary>
internal readonly record struct BlendedOnlyRay(
    Vector3d Origin,
    Vector3d End,
    BonePose Current,
    BonePose Previous);
