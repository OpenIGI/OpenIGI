using OpenIGI.Core.Numerics;
using OpenIGI.Formats;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The rigid-model line handler against the shipped collision meshes.</summary>
/// <remarks>
/// These pin the geometry, not the last bit: each hit is checked against an independent double-precision search
/// over every face. Bit-exactness against the retail client needs the probe.
/// </remarks>
public sealed class RetailIgi2RigidTraceTests
{
    private static readonly Vector3d Origin = new(123456.5, -654321.25, 2048.0);

    /// <summary>A line through each sampled face's centre strikes the mesh no further than a centimetre past the nearest face.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Lines_through_face_centres_strike_the_nearest_surface()
    {
        int checkedLines = 0;
        foreach ((string name, MefCollisionMesh mesh) in Meshes().Take(25))
        {
            Igi2RigidObject model = Place(name, mesh);
            for (int f = 0; f < mesh.Faces.Length; f += Math.Max(1, mesh.Faces.Length / 8))
            {
                MefCollisionFace face = mesh.Faces[f];
                if (mesh.IsPortal(face.Material) || mesh.MaterialRow(face.Material) == Igi2LineTrace.AirMaterial)
                {
                    continue;
                }

                (Vector3d start, Vector3d end) = Through(mesh, face);
                Igi2LineTrace trace = new(1);
                trace.Starts[0] = start;
                trace.Ends[0] = end;

                Igi2RigidTrace.Trace(model, trace);
                double? expected = Nearest(mesh, start, end);

                Igi2Intersection hit = trace.Intersections[0];
                Assert.True(expected is not null, $"{name} face {f}: the reference finds nothing.");
                Assert.True(hit.IsHit, $"{name} face {f}: the line through the face's centre struck nothing.");
                Assert.InRange(hit.Distance, expected!.Value - 0.5, expected.Value + 41.0);
                Assert.Same(name, hit.HitObject?.Owner);
                checkedLines++;
            }
        }

        Assert.True(checkedLines > 50, $"Only {checkedLines} lines were checked.");
    }

    /// <summary>A model replaces an earlier hit only when it is nearer; a farther one leaves the record alone.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_farther_model_leaves_a_nearer_hit_alone()
    {
        (string name, MefCollisionMesh mesh) = Meshes().First();
        Igi2RigidObject model = Place(name, mesh);
        MefCollisionFace face = mesh.Faces[0];
        (Vector3d start, Vector3d end) = Through(mesh, face);

        Igi2LineTrace trace = new(1);
        trace.Starts[0] = start;
        trace.Ends[0] = end;
        Igi2Intersection earlier = trace.Intersections[0];
        earlier.IsHit = true;
        earlier.Distance = 1.0;
        earlier.Position = start;
        Igi2RigidObject nearer = Place("nearer", mesh);
        earlier.HitObject = nearer;

        Igi2RigidTrace.Trace(model, trace);

        Assert.Same(nearer, earlier.HitObject);
        Assert.Equal(1.0, earlier.Distance);
    }

    /// <summary>Faces of the ignored material row never stop a line.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_ignored_material_is_passed_through()
    {
        (string name, MefCollisionMesh mesh) = Meshes().First(static m => Distinct(m.Mesh) == 1);
        Igi2RigidObject model = Place(name, mesh);
        MefCollisionFace face = mesh.Faces[0];
        (Vector3d start, Vector3d end) = Through(mesh, face);

        Igi2LineTrace trace = new(1) { IgnoreMaterial = mesh.MaterialRow(face.Material) };
        trace.Starts[0] = start;
        trace.Ends[0] = end;

        Igi2RigidTrace.Trace(model, trace);

        Assert.False(trace.Intersections[0].IsHit);
    }

    /// <summary>A trace that leaves the model out never reaches it.</summary>
    [RequiresTitleDataFact("igi2")]
    public void An_excluded_model_is_not_struck()
    {
        (string name, MefCollisionMesh mesh) = Meshes().First();
        Igi2RigidObject model = Place(name, mesh);
        (Vector3d start, Vector3d end) = Through(mesh, mesh.Faces[0]);

        Igi2LineTrace trace = new(1) { Includes = static owner => owner is not string };
        trace.Starts[0] = start;
        trace.Ends[0] = end;

        Igi2RigidTrace.Trace(model, trace);

        Assert.False(trace.Intersections[0].IsHit);
    }

    private static IEnumerable<(string Name, MefCollisionMesh Mesh)> Meshes()
    {
        foreach ((string name, MefModel model) in Igi2CommonModels.Models())
        {
            if (model.BoneHierarchy is not null)
            {
                continue;
            }

            if (MefCollisionMesh.TryRead(model.Container, 0, out MefCollisionMesh? mesh, AssetFormatProfile.Igi2.Collision)
                && mesh is { Faces.Length: > 4, Spheres.Count: > 0 })
            {
                yield return (name, mesh);
            }
        }
    }

    private static int Distinct(MefCollisionMesh mesh)
    {
        HashSet<int> rows = [];
        foreach (MefCollisionFace face in mesh.Faces)
        {
            rows.Add(mesh.MaterialRow(face.Material));
        }

        return rows.Count;
    }

    /// <summary>The model at a far-from-origin position, turned a quarter about Z, so the transforms are exercised.</summary>
    private static Igi2RigidObject Place(string name, MefCollisionMesh mesh)
    {
        float radius = 0f;
        foreach (Vector3f v in mesh.Vertices)
        {
            radius = MathF.Max(radius, MathF.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z)));
        }

        Igi2RigidObject model = new(name, [new Igi2CollisionMesh(mesh)], radius + 1f, isBuildingMesh: false)
        {
            Position = Origin,
        };

        // World = M·local with M's rows as stored: local X goes to world Y, local Y to world -X.
        float[] m = model.Orientation;
        m[0] = 0f; m[1] = -1f; m[2] = 0f;
        m[3] = 1f; m[4] = 0f; m[5] = 0f;
        m[6] = 0f; m[7] = 0f; m[8] = 1f;
        return model;
    }

    /// <summary>A line through a face's centroid along its normal, starting a metre outside the face.</summary>
    private static (Vector3d Start, Vector3d End) Through(MefCollisionMesh mesh, MefCollisionFace face)
    {
        Vector3d a = World(mesh.Vertices[face.I0]), b = World(mesh.Vertices[face.I1]), c = World(mesh.Vertices[face.I2]);
        Vector3d centre = new((a.X + b.X + c.X) / 3.0, (a.Y + b.Y + c.Y) / 3.0, (a.Z + b.Z + c.Z) / 3.0);
        Vector3d n = Cross(b - a, c - a);
        double length = Math.Sqrt((n.X * n.X) + (n.Y * n.Y) + (n.Z * n.Z));
        n = new Vector3d(n.X / length, n.Y / length, n.Z / length);
        return (centre + (n * 4096.0), centre - (n * 4096.0));
    }

    /// <summary>The placement's transform: world = M·local + origin, with M the rows <see cref="Place"/> sets.</summary>
    private static Vector3d World(Vector3f v) => new(Origin.X - v.Y, Origin.Y + v.X, Origin.Z + v.Z);

    private static Vector3d Cross(Vector3d a, Vector3d b) =>
        new((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));

    /// <summary>The nearest solid, non-air face along the segment, in double precision; its distance from the start.</summary>
    private static double? Nearest(MefCollisionMesh mesh, Vector3d start, Vector3d end)
    {
        Vector3d d = end - start;
        double length = Math.Sqrt((d.X * d.X) + (d.Y * d.Y) + (d.Z * d.Z));
        double? best = null;
        foreach (MefCollisionFace face in mesh.Faces)
        {
            if (mesh.IsPortal(face.Material) || mesh.MaterialRow(face.Material) == Igi2LineTrace.AirMaterial)
            {
                continue;
            }

            if (RayTriangle.Intersect(start, d, World(mesh.Vertices[face.I0]), World(mesh.Vertices[face.I1]), World(mesh.Vertices[face.I2]), out double t)
                && t <= 1.0 && (best is null || t * length < best))
            {
                best = t * length;
            }
        }

        return best;
    }
}
