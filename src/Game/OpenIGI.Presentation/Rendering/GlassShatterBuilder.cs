using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// Breaks a struck glass pane into shards the way retail <c>GlassShatter_CreateShards</c> does: the
/// pane's collision faces are filtered by the native winding test and split around the impact.
/// </summary>
/// <remarks>
/// Pure geometry plus the renderer's random stream. The draws come in exactly the order the
/// recursion visits the faces, so the shards and every later particle draw what they always drew.
/// </remarks>
internal static class GlassShatterBuilder
{
    /// <summary>Builds the shard records for one hit, in native creation order.</summary>
    /// <param name="placement">The exact glass placement that was hit.</param>
    /// <param name="impactUnits">The bullet's world-space impact, in engine units.</param>
    /// <param name="random">The renderer's random stream, drawn from as the recursion splits.</param>
    /// <returns>The shards, or <see langword="null"/> when the pane has no readable collision mesh.</returns>
    internal static List<GlassShard>? Build(WorldPlacement placement, Vector3d impactUnits, IgiRandom random)
    {
        MefCollisionMesh? mesh;
        try
        {
            if (!MefCollisionMesh.TryRead(placement.Model.Container, out mesh) || mesh is null)
            {
                return null;
            }
        }
        catch (PackFormatException)
        {
            // A malformed optional collision payload must not take down the render loop.
            return null;
        }

        Vector3d originUnits = placement.Position * WorldScene.UnitsPerMetre;
        Matrix3f localToWorld = placement.Orientation.Transposed();
        // FUN_0044BD70 compares the local collision-face normal with (object position - contact
        // position), not with the rendered world-space normal. Matrix3f.Transform is the inverse of
        // the draw transform for this row-vector convention, so transform the contact back into
        // collision-local space before applying the strict native '< 0' test.
        Vector3d impactLocal = placement.Orientation.Transform(impactUnits - originUnits);
        Vector3d faceFilterDirection = -impactLocal;
        List<GlassShard> candidates = [];

        foreach (MefCollisionFace face in mesh.Faces)
        {
            if ((uint)face.I0 >= (uint)mesh.Vertices.Count
                || (uint)face.I1 >= (uint)mesh.Vertices.Count
                || (uint)face.I2 >= (uint)mesh.Vertices.Count)
            {
                continue;
            }

            Vector3d a = new(mesh.Vertices[face.I0].X, mesh.Vertices[face.I0].Y, mesh.Vertices[face.I0].Z);
            Vector3d b = new(mesh.Vertices[face.I1].X, mesh.Vertices[face.I1].Y, mesh.Vertices[face.I1].Z);
            Vector3d c = new(mesh.Vertices[face.I2].X, mesh.Vertices[face.I2].Y, mesh.Vertices[face.I2].Z);
            Vector3d edgeA = b - a;
            Vector3d edgeB = c - a;
            Vector3d normal = Vector3d.Cross(edgeA, edgeB);
            double normalLength = normal.Length;
            if (!(normalLength > 1e-6))
            {
                continue;
            }

            // This is intentionally strict. The retail code calls FUN_0044A670 only when the
            // signed face test is negative; zero and the opposite winding are skipped.
            if (!PassesRetailGlassFaceFilter(faceFilterDirection, a, b, c))
            {
                continue;
            }

            AppendGlassShards(
                random,
                candidates,
                a,
                b,
                c,
                normal / normalLength,
                impactLocal,
                originUnits,
                localToWorld,
                depth: 0);
        }

        return candidates;
    }

    /// <summary>
    /// Splits a collision triangle using the distance-aware, longest-edge recursion in native
    /// <c>FUN_0044A670</c>. The retail routine measures the contact against every edge, converts that
    /// distance with <c>1/5120</c>, and keeps splitting while any edge exceeds
    /// <c>409.6 + 4505.6 * normalizedDistance</c> engine units. Its split point is deliberately not a
    /// midpoint: the short/long edge ratio is clamped around 0.5 and offset by the retail random
    /// range of 0.1. Keeping the recursion order is important because the native 999-record cap is
    /// an insertion-order cap.
    /// </summary>
    private static void AppendGlassShards(
        IgiRandom random,
        List<GlassShard> shards,
        Vector3d a,
        Vector3d b,
        Vector3d c,
        Vector3d normal,
        Vector3d impactLocal,
        Vector3d originUnits,
        Matrix3f localToWorld,
        int depth)
    {
        if (shards.Count >= 999)
        {
            return;
        }

        // The depth guard is only a malformed-mesh safety net. Normal retail triangles terminate by
        // the edge threshold; it prevents a degenerate collision payload from exhausting the stack.
        if (depth >= 24)
        {
            AppendGlassShardLeaf(random, shards, a, b, c, normal, impactLocal, originUnits, localToWorld);
            return;
        }

        double edgeAbSquared = (b - a).LengthSquared;
        double edgeBcSquared = (c - b).LengthSquared;
        double edgeCaSquared = (a - c).LengthSquared;
        double distanceCa = NormalizedGlassEdgeDistance(impactLocal, c, a);
        double distanceAb = NormalizedGlassEdgeDistance(impactLocal, a, b);
        double distanceBc = NormalizedGlassEdgeDistance(impactLocal, b, c);
        double thresholdCa = (distanceCa * 4505.600006d) + 409.600006d;
        double thresholdAb = (distanceAb * 4505.600006d) + 409.600006d;
        double thresholdBc = (distanceBc * 4505.600006d) + 409.600006d;

        if ((thresholdCa * thresholdCa >= edgeCaSquared)
            && (thresholdAb * thresholdAb >= edgeAbSquared)
            && (thresholdBc * thresholdBc >= edgeBcSquared))
        {
            AppendGlassShardLeaf(random, shards, a, b, c, normal, impactLocal, originUnits, localToWorld);
            return;
        }

        if (edgeBcSquared <= edgeAbSquared && edgeCaSquared <= edgeAbSquared)
        {
            double ratio = edgeBcSquared <= edgeCaSquared
                ? 1d - (Math.Sqrt(edgeBcSquared) / Math.Sqrt(edgeAbSquared))
                : Math.Sqrt(edgeCaSquared) / Math.Sqrt(edgeAbSquared);
            if (edgeBcSquared <= edgeCaSquared)
            {
                ratio = ratio <= 0.5d ? 0.5d : ratio;
                ratio -= random.NextSingle() * 0.1d;
            }
            else
            {
                ratio = ratio >= 0.5d ? 0.5d : ratio;
                ratio += random.NextSingle() * 0.1d;
            }

            Vector3d split = a + ((b - a) * ratio);
            AppendGlassShards(random, shards, b, c, split, normal, impactLocal, originUnits, localToWorld, depth + 1);
            AppendGlassShards(random, shards, split, c, a, normal, impactLocal, originUnits, localToWorld, depth + 1);
            return;
        }

        if (edgeCaSquared <= edgeBcSquared && edgeAbSquared <= edgeBcSquared)
        {
            double ratio = edgeCaSquared <= edgeAbSquared
                ? 1d - (Math.Sqrt(edgeCaSquared) / Math.Sqrt(edgeBcSquared))
                : Math.Sqrt(edgeAbSquared) / Math.Sqrt(edgeBcSquared);
            if (edgeCaSquared <= edgeAbSquared)
            {
                ratio = ratio <= 0.5d ? 0.5d : ratio;
                ratio -= random.NextSingle() * 0.1d;
            }
            else
            {
                ratio = ratio >= 0.5d ? 0.5d : ratio;
                ratio += random.NextSingle() * 0.1d;
            }

            Vector3d split = b + ((c - b) * ratio);
            AppendGlassShards(random, shards, c, a, split, normal, impactLocal, originUnits, localToWorld, depth + 1);
            AppendGlassShards(random, shards, split, a, b, normal, impactLocal, originUnits, localToWorld, depth + 1);
            return;
        }

        // The remaining branch is the native CA-longest case. Ties intentionally fall through in
        // the same order as FUN_0044A670's three mutually exclusive comparisons.
        double caRatio = edgeAbSquared <= edgeBcSquared
            ? 1d - (Math.Sqrt(edgeAbSquared) / Math.Sqrt(edgeCaSquared))
            : Math.Sqrt(edgeBcSquared) / Math.Sqrt(edgeCaSquared);
        if (edgeAbSquared <= edgeBcSquared)
        {
            caRatio = caRatio <= 0.5d ? 0.5d : caRatio;
            caRatio -= random.NextSingle() * 0.1d;
        }
        else
        {
            caRatio = caRatio >= 0.5d ? 0.5d : caRatio;
            caRatio += random.NextSingle() * 0.1d;
        }

        Vector3d caSplit = c + ((a - c) * caRatio);
        AppendGlassShards(random, shards, a, b, caSplit, normal, impactLocal, originUnits, localToWorld, depth + 1);
        AppendGlassShards(random, shards, caSplit, b, c, normal, impactLocal, originUnits, localToWorld, depth + 1);
    }

    private static void AppendGlassShardLeaf(
        IgiRandom random,
        List<GlassShard> shards,
        Vector3d a,
        Vector3d b,
        Vector3d c,
        Vector3d normal,
        Vector3d impactLocal,
        Vector3d originUnits,
        Matrix3f localToWorld)
    {
        double nearestEdgeDistance = Math.Min(
            NormalizedGlassEdgeDistance(impactLocal, c, a),
            Math.Min(
                NormalizedGlassEdgeDistance(impactLocal, a, b),
                NormalizedGlassEdgeDistance(impactLocal, b, c)));
        int lifeTicks = Math.Max(
            1,
            (int)Math.Round(((1d - nearestEdgeDistance) + random.NextSingle()) * 0.5d * 30d));

        Vector3d centreLocal = (a + b + c) / 3d;
        Vector3d velocityLocal = new(
            ((random.NextSingle() * 4096d) - 2048d) * 0.0333333351d,
            ((random.NextSingle() * 4096d) - 2048d) * 0.0333333351d,
            ((random.NextSingle() * 4096d) - 2048d) * 0.0333333351d);
        Vector3d worldA = originUnits + localToWorld.Transform(a);
        Vector3d worldB = originUnits + localToWorld.Transform(b);
        Vector3d worldC = originUnits + localToWorld.Transform(c);
        Vector3d worldCentre = originUnits + localToWorld.Transform(centreLocal);
        Vector3d worldNormal = localToWorld.Transform(normal);
        double normalLength = worldNormal.Length;
        if (!(normalLength > 1e-9))
        {
            return;
        }

        Vector3d worldVelocity = localToWorld.Transform(velocityLocal);
        shards.Add(new GlassShard(
            worldCentre,
            worldA,
            worldB,
            worldC,
            worldNormal / normalLength,
            0.0f,
            new Vector3f((float)worldVelocity.X, (float)worldVelocity.Y, (float)worldVelocity.Z),
            lifeTicks));
    }

    internal static bool PassesRetailGlassFaceFilter(Vector3d direction, Vector3d a, Vector3d b, Vector3d c)
    {
        Vector3d normal = Vector3d.Cross(b - a, c - a);
        return normal.Length > 1e-9 && Vector3d.Dot(direction, normal) < 0d;
    }

    internal static double NormalizedGlassEdgeDistance(Vector3d point, Vector3d start, Vector3d end)
    {
        Vector3d edge = end - start;
        double edgeLengthSquared = edge.LengthSquared;
        if (!(edgeLengthSquared > 1e-12))
        {
            return Math.Clamp((point - start).Length * 0.000195312503d, 0d, 1d);
        }

        double projection = Vector3d.Dot(point - start, edge) / edgeLengthSquared;
        projection = Math.Clamp(projection, 0d, 1d);
        Vector3d nearest = start + (edge * projection);
        return Math.Clamp((point - nearest).Length * 0.000195312503d, 0d, 1d);
    }
}
