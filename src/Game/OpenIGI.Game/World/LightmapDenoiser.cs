using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// The edge-aware à-trous filter behind <see cref="LightmapBaker.Denoise"/>, run over one page at a time.
/// </summary>
internal static class LightmapDenoiser
{
    // The B3-spline row an à-trous filter uses along each axis; the 2D kernel is its outer product.
    private static readonly float[] AtrousKernel = [1f / 16f, 1f / 4f, 3f / 8f, 1f / 4f, 1f / 16f];

    internal static void DenoiseTarget(LightmapTarget target, int iterations, float positionSigmaMetres, float normalPower)
    {
        int width = target.Width;
        int height = target.Height;
        float inversePositionVariance = 1f / (2f * positionSigmaMetres * positionSigmaMetres);

        Vector3f[] source = target.Radiance;
        Vector3f[] destination = new Vector3f[source.Length];

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            int step = 1 << iteration;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int centre = (y * width) + x;
                    if (!target.Texels[centre].IsCovered)
                    {
                        destination[centre] = source[centre];
                        continue;
                    }

                    Vector3d centrePosition = target.Texels[centre].WorldPosition;
                    Vector3f centreNormal = target.Texels[centre].Normal;

                    Vector3f sum = default;
                    float weightSum = 0f;

                    for (int ky = 0; ky < 5; ky++)
                    {
                        int sy = y + ((ky - 2) * step);
                        if (sy < 0 || sy >= height)
                        {
                            continue;
                        }

                        for (int kx = 0; kx < 5; kx++)
                        {
                            int sx = x + ((kx - 2) * step);
                            if (sx < 0 || sx >= width)
                            {
                                continue;
                            }

                            int sample = (sy * width) + sx;
                            if (!target.Texels[sample].IsCovered)
                            {
                                continue;
                            }

                            Vector3d samplePosition = target.Texels[sample].WorldPosition;
                            double dx = samplePosition.X - centrePosition.X;
                            double dy = samplePosition.Y - centrePosition.Y;
                            double dz = samplePosition.Z - centrePosition.Z;
                            float positionWeight = MathF.Exp(-(float)((dx * dx) + (dy * dy) + (dz * dz)) * inversePositionVariance);

                            Vector3f sampleNormal = target.Texels[sample].Normal;
                            float alignment = (centreNormal.X * sampleNormal.X)
                                + (centreNormal.Y * sampleNormal.Y)
                                + (centreNormal.Z * sampleNormal.Z);
                            if (alignment <= 0f)
                            {
                                continue;
                            }

                            float weight = AtrousKernel[kx] * AtrousKernel[ky] * positionWeight * MathF.Pow(alignment, normalPower);

                            Vector3f radiance = source[sample];
                            sum = new Vector3f(sum.X + (radiance.X * weight), sum.Y + (radiance.Y * weight), sum.Z + (radiance.Z * weight));
                            weightSum += weight;
                        }
                    }

                    destination[centre] = weightSum > 1e-6f
                        ? new Vector3f(sum.X / weightSum, sum.Y / weightSum, sum.Z / weightSum)
                        : source[centre];
                }
            }

            (source, destination) = (destination, source);
        }

        // Ensure the smoothed result lands in the target's own array, whichever buffer the ping-pong ended on.
        if (!ReferenceEquals(source, target.Radiance))
        {
            Array.Copy(source, target.Radiance, source.Length);
        }
    }
}
