using System.Threading;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Threading;

namespace OpenIGI.Game.World;

/// <summary>
/// The multi-bounce gather behind <see cref="LightmapBaker.BakeBounces"/> (phase 3): each pass traces a
/// cosine-weighted hemisphere from every covered texel through the <see cref="LightmapBvh"/> and folds the
/// coloured light it finds into the texel's radiance.
/// </summary>
internal static class LightmapBounceBaker
{
    /// <summary>The bounce passes, checked by <see cref="LightmapBaker.BakeBounces"/> before they are handed out.</summary>
    internal static IEnumerable<bool> BounceSlices(
        IReadOnlyList<LightmapTarget> targets,
        LightmapTriangle[] triangles,
        LightmapBvh bvh,
        Vector3f skyColour,
        int passes,
        int hemisphereRays,
        IWorkScheduler work,
        CancellationToken cancellationToken)
    {
        Vector3f[] hemisphere = LightmapBaker.CosineHemisphere(Math.Max(hemisphereRays, 1));
        float inverseRays = 1f / hemisphere.Length;
        float biasMetres = (float)(LightmapBaker.RayBiasUnits / WorldScene.UnitsPerMetre);
        double reachMetres = LightmapBaker.RayReachMetres;
        LightmapTexelBlock[] blocks = LightmapTexelBlock.Of(targets);

        for (int pass = 0; pass < Math.Max(passes, 1); pass++)
        {
            // Before the snapshot, not after: it clones every page in the level, and a bake already asked
            // to stop must not allocate a level's worth of radiance to throw away.
            if (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }

            // The light the last pass left, so this pass reads a stable snapshot rather than racing itself.
            Vector3f[][] previous = new Vector3f[targets.Count][];
            for (int t = 0; t < targets.Count; t++)
            {
                previous[t] = (Vector3f[])targets[t].Radiance.Clone();
            }

            IEnumerable<bool> gathered = Stepping.Range(work, blocks.Length, LightmapBaker.Parallelism, b =>
            {
                LightmapBaker.LowerPriority(work);

                LightmapTexelBlock block = blocks[b];
                LightmapTarget target = targets[block.Target];

                // As in the direct pass: the token is read per texel as well as per block.
                for (int i = block.From; i < block.To; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    LightmapTexel texel = target.Texels[i];
                    if (!texel.IsCovered)
                    {
                        continue;
                    }

                    Vector3d origin = new(
                        texel.WorldPosition.X + (texel.Normal.X * biasMetres),
                        texel.WorldPosition.Y + (texel.Normal.Y * biasMetres),
                        texel.WorldPosition.Z + (texel.Normal.Z * biasMetres));

                    (Vector3f tangent, Vector3f bitangent) = LightmapBaker.TangentFrame(texel.Normal);
                    Vector3f gather = default;

                    for (int r = 0; r < hemisphere.Length; r++)
                    {
                        Vector3f local = hemisphere[r];
                        Vector3d dir = new(
                            (tangent.X * local.X) + (bitangent.X * local.Y) + (texel.Normal.X * local.Z),
                            (tangent.Y * local.X) + (bitangent.Y * local.Y) + (texel.Normal.Y * local.Z),
                            (tangent.Z * local.X) + (bitangent.Z * local.Y) + (texel.Normal.Z * local.Z));

                        if (bvh.ClosestHit(origin, dir, reachMetres, out int hitTriangle, out double bu, out double bv))
                        {
                            LightmapTriangle tri = triangles[hitTriangle];
                            double bw = 1.0 - bu - bv;
                            float lightmapU = (float)((bw * tri.U0) + (bu * tri.U1) + (bv * tri.U2));
                            float lightmapV = (float)((bw * tri.V0) + (bu * tri.V1) + (bv * tri.V2));

                            LightmapTarget hitTarget = targets[tri.Target];
                            int hx = Math.Clamp((int)(lightmapU * hitTarget.Width), 0, hitTarget.Width - 1);
                            int hy = Math.Clamp((int)(lightmapV * hitTarget.Height), 0, hitTarget.Height - 1);
                            Vector3f incoming = previous[tri.Target][(hy * hitTarget.Width) + hx];

                            gather = new Vector3f(
                                gather.X + (tri.Albedo.X * incoming.X),
                                gather.Y + (tri.Albedo.Y * incoming.Y),
                                gather.Z + (tri.Albedo.Z * incoming.Z));
                        }
                        else
                        {
                            gather = new Vector3f(gather.X + skyColour.X, gather.Y + skyColour.Y, gather.Z + skyColour.Z);
                        }
                    }

                    Vector3f direct = target.Direct[i];
                    target.Radiance[i] = new Vector3f(
                        direct.X + (gather.X * inverseRays),
                        direct.Y + (gather.Y * inverseRays),
                        direct.Z + (gather.Z * inverseRays));
                }
            },
            cancellationToken);

            foreach (bool slice in gathered)
            {
                yield return slice;
            }
        }
    }
}
