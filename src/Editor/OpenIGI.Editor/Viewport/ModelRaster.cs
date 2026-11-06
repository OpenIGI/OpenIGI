using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Editor.Viewport;

/// <summary>A texture a <see cref="ModelRaster"/> samples: its pixels, row by row, and whether holes in it are cut out.</summary>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
/// <param name="Pixels">The pixels.</param>
/// <param name="AlphaTest">Whether texels under half alpha are not drawn (fences, foliage).</param>
internal sealed record RasterTexture(int Width, int Height, Bgra32[] Pixels, bool AlphaTest);

/// <summary>
/// Draws a textured model into a picture on the processor, for previews: perspective-correct texturing, a depth buffer, and
/// one light from over the viewer's shoulder. Small and dependable rather than fast; a few thousand triangles at preview sizes
/// take a few milliseconds.
/// </summary>
internal sealed class ModelRaster
{
    private const float Near = 0.01f;

    private Bgra32[] _colour = [];
    private float[] _depth = [];

    /// <summary>Gets the picture's width.</summary>
    public int Width { get; private set; }

    /// <summary>Gets the picture's height.</summary>
    public int Height { get; private set; }

    /// <summary>Gets the picture, row by row.</summary>
    public ReadOnlySpan<Bgra32> Pixels => _colour.AsSpan(0, Width * Height);

    /// <summary>Clears the picture to a colour, at a size.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="background">The colour.</param>
    public void Begin(int width, int height, Bgra32 background)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        int size = Width * Height;
        if (_colour.Length < size)
        {
            _colour = new Bgra32[size];
            _depth = new float[size];
        }

        _colour.AsSpan(0, size).Fill(background);
        _depth.AsSpan(0, size).Fill(float.MaxValue);
    }

    /// <summary>Draws triangles.</summary>
    /// <param name="vertices">The vertices, in model space.</param>
    /// <param name="triangles">The triangles, as indices into <paramref name="vertices"/>.</param>
    /// <param name="texture">The texture, or <see langword="null"/> for flat grey.</param>
    /// <param name="view">The camera: model space to view space (row vectors), looking down +Y with +Z up.</param>
    /// <param name="eye">The camera's position in view space before its rotation; that is, the translation applied after <paramref name="view"/>.</param>
    /// <param name="focal">The focal length in pixels.</param>
    public void Draw(ReadOnlySpan<Vertex3D> vertices, IEnumerable<(int A, int B, int C)> triangles, RasterTexture? texture, Matrix3f view, Vector3f eye, float focal)
    {
        Vector3f light = new Vector3f(-0.4f, -0.7f, 0.6f).Normalized();
        foreach ((int a, int b, int c) in triangles)
        {
            if ((uint)a >= (uint)vertices.Length || (uint)b >= (uint)vertices.Length || (uint)c >= (uint)vertices.Length)
            {
                continue;
            }

            Vector3f pa = view.Transform(vertices[a].Position) - eye;
            Vector3f pb = view.Transform(vertices[b].Position) - eye;
            Vector3f pc = view.Transform(vertices[c].Position) - eye;
            if (pa.Y < Near || pb.Y < Near || pc.Y < Near)
            {
                continue;
            }

            // Shade by the face's own normal, lit from the viewer's side whichever way it was wound.
            Vector3f n = Vector3f.Cross(pb - pa, pc - pa).NormalizedOrSelf(1e-6f);
            float shade = 0.6f + (0.55f * MathF.Abs(Vector3f.Dot(n, light)));
            Fill(Project(pa, focal), Project(pb, focal), Project(pc, focal), vertices[a], vertices[b], vertices[c], texture, shade);
        }
    }

    private (float X, float Y, float W) Project(Vector3f p, float focal) =>
        ((Width * 0.5f) + (p.X * focal / p.Y), (Height * 0.5f) - (p.Z * focal / p.Y), 1f / p.Y);

    // A triangle's pixels, by edge functions over its bounding box, with texture coordinates interpolated over depth.
    private void Fill((float X, float Y, float W) a, (float X, float Y, float W) b, (float X, float Y, float W) c, in Vertex3D va, in Vertex3D vb, in Vertex3D vc, RasterTexture? texture, float shade)
    {
        float area = ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));
        if (MathF.Abs(area) < 1e-6f)
        {
            return;
        }

        int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))));
        int maxX = Math.Min(Width - 1, (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))));
        int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))));
        int maxY = Math.Min(Height - 1, (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))));
        if (minX > maxX || minY > maxY)
        {
            return;
        }

        float inverse = 1f / area;
        float ua = va.DiffuseU * a.W, ub = vb.DiffuseU * b.W, uc = vc.DiffuseU * c.W;
        float wa = va.DiffuseV * a.W, wb = vb.DiffuseV * b.W, wc = vc.DiffuseV * c.W;
        for (int y = minY; y <= maxY; y++)
        {
            float py = y + 0.5f;
            int row = y * Width;
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f;
                float l0 = (((b.X - px) * (c.Y - py)) - ((b.Y - py) * (c.X - px))) * inverse;
                float l1 = (((c.X - px) * (a.Y - py)) - ((c.Y - py) * (a.X - px))) * inverse;
                float l2 = 1f - l0 - l1;
                if (l0 < 0f || l1 < 0f || l2 < 0f)
                {
                    continue;
                }

                float w = (l0 * a.W) + (l1 * b.W) + (l2 * c.W);
                float depth = 1f / w;
                if (depth >= _depth[row + x])
                {
                    continue;
                }

                Bgra32 texel = new(200, 200, 200, 255);
                if (texture is not null)
                {
                    float u = ((l0 * ua) + (l1 * ub) + (l2 * uc)) * depth;
                    float v = ((l0 * wa) + (l1 * wb) + (l2 * wc)) * depth;
                    texel = Sample(texture, u, v);
                    if (texture.AlphaTest && texel.A < 128)
                    {
                        continue;
                    }
                }

                _depth[row + x] = depth;
                _colour[row + x] = new Bgra32(
                    (byte)Math.Min(255f, texel.B * shade),
                    (byte)Math.Min(255f, texel.G * shade),
                    (byte)Math.Min(255f, texel.R * shade),
                    255);
            }
        }
    }

    private static Bgra32 Sample(RasterTexture texture, float u, float v)
    {
        float fu = u - MathF.Floor(u);
        float fv = v - MathF.Floor(v);
        int x = Math.Min(texture.Width - 1, (int)(fu * texture.Width));
        int y = Math.Min(texture.Height - 1, (int)(fv * texture.Height));
        return texture.Pixels[(y * texture.Width) + x];
    }
}
