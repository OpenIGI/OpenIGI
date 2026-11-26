using OpenIGI.Core.Graphics;
using OpenIGI.Core.Threading;
using OpenIGI.Host.Editor;

namespace OpenIGI.Editor.Map;

/// <summary>What part of the level a map picture shows, how finely, and at which floor.</summary>
/// <param name="WestMetres">The world X of its left edge, in metres.</param>
/// <param name="NorthMetres">The world Y of its top edge.</param>
/// <param name="MetresPerPixel">How much of the world a pixel covers.</param>
/// <param name="Width">Its width in pixels.</param>
/// <param name="Height">Its height in pixels.</param>
/// <param name="Floor">The storey it is cut at.</param>
/// <param name="Walls">Whether the walls are drawn as the plan's lines over it.</param>
internal readonly record struct OverheadView(double WestMetres, double NorthMetres, double MetresPerPixel, int Width, int Height, Floor Floor, bool Walls)
{
    /// <summary>Gets the world X of its right edge.</summary>
    public double EastMetres => WestMetres + (Width * MetresPerPixel);

    /// <summary>Gets the world Y of its bottom edge.</summary>
    public double SouthMetres => NorthMetres - (Height * MetresPerPixel);
}

/// <summary>
/// Draws an <see cref="OverheadScene"/> from straight above into a picture on the processor, as the multiplayer layout page
/// draws it on the graphics card: the ground's first passes with their heights, its later passes added where they lie, then the
/// models' faces with a depth test, every face cut away where it stands higher over the ground than the floor being looked
/// at; and over that the plan's walls -- thin lines for those under the cut, thick black where the cut passes through one.
/// </summary>
/// <remarks>
/// Built once a level (each texture's smaller copies made then); a picture is drawn in bands of rows side by side, each band
/// keeping to the triangles over it in the scene's order. A panel's worth of a level takes a tenth of a second or so.
/// </remarks>
internal sealed class OverheadRaster
{
    private readonly IWorkScheduler _work;
    private const int Band = 16;

    // Where an added ground pass still counts as lying on the first, in metres.
    private const float SameHeight = 0.02f;

    private static readonly (float R, float G, float B) Stone = (0.66f, 0.64f, 0.59f);
    private static readonly Bgra32 Background = Bgra32.FromRgb(8, 8, 10);

    private readonly OverheadScene _scene;
    private readonly FloorPlan _plan;
    private readonly Mips[] _textures;

    /// <summary>Initializes a new instance of the <see cref="OverheadRaster"/> class.</summary>
    /// <param name="scene">The level from above.</param>
    /// <param name="plan">Its storeys, for the walls.</param>
    /// <param name="work">Where its bands of rows are drawn side by side.</param>
    public OverheadRaster(OverheadScene scene, FloorPlan plan, IWorkScheduler work)
    {
        _work = work ?? throw new ArgumentNullException(nameof(work));
        _scene = scene;
        _plan = plan;
        _textures = [.. scene.Textures.Select(static t => new Mips(t))];
    }

    /// <summary>Draws a part of the level.</summary>
    /// <param name="view">What to draw.</param>
    /// <returns>The picture, row by row from the top.</returns>
    public Bgra32[] Render(OverheadView view)
    {
        int bands = (view.Height + Band - 1) / Band;
        List<(int Group, int Triangle)>[] faces = [.. Enumerable.Range(0, bands).Select(static _ => new List<(int, int)>())];
        List<(int Triangle, bool Cut)>[] walls = [.. Enumerable.Range(0, bands).Select(static _ => new List<(int, bool)>())];
        Bin(view, faces, walls);

        Bgra32[] picture = new Bgra32[view.Width * view.Height];
        _work.ForRange(0, bands, 0, band => DrawBand(view, band, faces[band], walls[band], picture));
        return picture;
    }

    // Each face and wall into the bands of rows it lies over, the scene's order kept.
    private void Bin(OverheadView view, List<(int, int)>[] faces, List<(int, bool)>[] walls)
    {
        double sceneWest = _scene.WestMetres, sceneSouth = _scene.SouthMetres;
        double left = view.WestMetres - sceneWest, right = view.EastMetres - sceneWest;
        double top = view.NorthMetres - sceneSouth, bottom = view.SouthMetres - sceneSouth;
        int bands = faces.Length;
        int BandOf(double y) => Math.Clamp((int)Math.Floor((top - y) / view.MetresPerPixel / Band), 0, bands - 1);

        for (int g = 0; g < _scene.Groups.Count; g++)
        {
            float[] v = _scene.Groups[g].Corners;
            for (int t = 0, o = 0; o + (3 * OverheadScene.Stride) <= v.Length; t++, o += 3 * OverheadScene.Stride)
            {
                float x0 = v[o], x1 = v[o + OverheadScene.Stride], x2 = v[o + (2 * OverheadScene.Stride)];
                float y0 = v[o + 1], y1 = v[o + OverheadScene.Stride + 1], y2 = v[o + (2 * OverheadScene.Stride) + 1];
                double minY = Math.Min(y0, Math.Min(y1, y2)), maxY = Math.Max(y0, Math.Max(y1, y2));
                if (Math.Max(x0, Math.Max(x1, x2)) < left || Math.Min(x0, Math.Min(x1, x2)) > right || maxY < bottom || minY > top)
                {
                    continue;
                }

                for (int b = BandOf(maxY); b <= BandOf(minY); b++)
                {
                    faces[b].Add((g, t));
                }
            }
        }

        if (!view.Walls || view.Floor.IsRoofs)
        {
            return;
        }

        float[] c = _scene.Collision;
        double reach = 3 * view.MetresPerPixel;
        for (int t = 0; t < _plan.Kinds.Length; t++)
        {
            if (_plan.Kinds[t] != PlanKind.Wall || _plan.Low[t] > view.Floor.Cut)
            {
                continue;
            }

            int o = t * OverheadScene.CollisionStride;
            double minY = Math.Min(c[o + 1], Math.Min(c[o + 4], c[o + 7])) - reach, maxY = Math.Max(c[o + 1], Math.Max(c[o + 4], c[o + 7])) + reach;
            if (Math.Max(c[o], Math.Max(c[o + 3], c[o + 6])) + reach < left || Math.Min(c[o], Math.Min(c[o + 3], c[o + 6])) - reach > right || maxY < bottom || minY > top)
            {
                continue;
            }

            bool cut = _plan.High[t] > view.Floor.Cut;
            for (int b = BandOf(maxY); b <= BandOf(minY); b++)
            {
                walls[b].Add((t, cut));
            }
        }
    }

    // One band of rows: its faces into a colour and height buffer of its own, its walls over them, then into the picture.
    private void DrawBand(OverheadView view, int band, List<(int Group, int Triangle)> faces, List<(int Triangle, bool Cut)> walls, Bgra32[] picture)
    {
        int rowFrom = band * Band, rowTo = Math.Min(view.Height, rowFrom + Band);
        int width = view.Width, size = (rowTo - rowFrom) * width;
        float[] colour = new float[size * 3];
        float[] depth = new float[size];
        Array.Fill(depth, float.NegativeInfinity);
        double scale = 1.0 / view.MetresPerPixel;
        double offsetX = (_scene.WestMetres - view.WestMetres) * scale, offsetY = (view.NorthMetres - _scene.SouthMetres) * scale;
        float cut = view.Floor.IsRoofs ? float.PositiveInfinity : (float)view.Floor.Cut;

        Span<float> corner = stackalloc float[3 * OverheadScene.Stride];
        foreach ((int g, int t) in faces)
        {
            OverheadGroup group = _scene.Groups[g];
            group.Corners.AsSpan(t * 3 * OverheadScene.Stride, 3 * OverheadScene.Stride).CopyTo(corner);
            Fill(group, corner, (float)scale, (float)offsetX, (float)offsetY, width, rowFrom, rowTo, colour, depth, cut);
        }

        // The walls under the cut thin and dark, the cut walls over them thick and black.
        float thin = (float)Math.Max(1.0, Math.Min(2.0, 0.15 * scale)), thick = (float)Math.Max(2.0, Math.Min(5.0, 0.35 * scale));
        float[] c = _scene.Collision;
        foreach (bool cutWalls in (ReadOnlySpan<bool>)[false, true])
        {
            foreach ((int t, bool isCut) in walls)
            {
                if (isCut != cutWalls)
                {
                    continue;
                }

                int o = t * OverheadScene.CollisionStride;
                for (int k = 0; k < 3; k++)
                {
                    int a = o + (k * 3), b = o + (((k + 1) % 3) * 3);
                    Line(
                        (float)((c[a] * scale) + offsetX), (float)(offsetY - (c[a + 1] * scale)),
                        (float)((c[b] * scale) + offsetX), (float)(offsetY - (c[b + 1] * scale)),
                        isCut ? thick : thin, isCut ? (0.043f, 0.039f, 0.035f, 1f) : (0.078f, 0.07f, 0.06f, 0.75f),
                        width, rowFrom, rowTo, colour);
                }
            }
        }

        for (int i = 0; i < size; i++)
        {
            picture[(rowFrom * width) + i] = float.IsNegativeInfinity(depth[i]) && colour[i * 3] + colour[(i * 3) + 1] + colour[(i * 3) + 2] == 0f
                ? Background
                : Bgra32.FromRgb(Channel(colour[i * 3]), Channel(colour[(i * 3) + 1]), Channel(colour[(i * 3) + 2]));
        }
    }

    // One face's pixels in the band: edge functions over its box, everything across it interpolated straight (seen from
    // straight above, nothing is foreshortened).
    private void Fill(OverheadGroup group, ReadOnlySpan<float> v, float scale, float offsetX, float offsetY, int width, int rowFrom, int rowTo, float[] colour, float[] depth, float cut)
    {
        const int S = OverheadScene.Stride;
        float ax = (v[0] * scale) + offsetX, ay = offsetY - (v[1] * scale);
        float bx = (v[S] * scale) + offsetX, by = offsetY - (v[S + 1] * scale);
        float cx = (v[2 * S] * scale) + offsetX, cy = offsetY - (v[(2 * S) + 1] * scale);
        float area = ((bx - ax) * (cy - ay)) - ((by - ay) * (cx - ax));
        if (MathF.Abs(area) < 1e-8f)
        {
            return;
        }

        int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(ax, MathF.Min(bx, cx))));
        int maxX = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(ax, MathF.Max(bx, cx))));
        int minY = Math.Max(rowFrom, (int)MathF.Floor(MathF.Min(ay, MathF.Min(by, cy))));
        int maxY = Math.Min(rowTo - 1, (int)MathF.Ceiling(MathF.Max(ay, MathF.Max(by, cy))));
        if (minX > maxX || minY > maxY)
        {
            return;
        }

        OverheadPass pass = group.Pass;
        bool isModel = pass >= OverheadPass.Solid;

        // A face standing wholly over the cut is gone; one wholly under it needs no test a pixel.
        float over0 = v[2] - v[6], over1 = v[S + 2] - v[S + 6], over2 = v[(2 * S) + 2] - v[(2 * S) + 6];
        if (isModel && MathF.Min(over0, MathF.Min(over1, over2)) > cut)
        {
            return;
        }

        bool cutPerPixel = isModel && MathF.Max(over0, MathF.Max(over1, over2)) > cut;
        Mips? texture = group.Texture >= 0 && group.Texture < _textures.Length ? _textures[group.Texture] : null;
        int level = 0;
        if (texture is not null)
        {
            // The level whose texels come nearest a pixel each: the face's texels over its pixels.
            float texels = MathF.Abs(((v[S + 3] - v[3]) * (v[(2 * S) + 4] - v[4])) - ((v[S + 4] - v[4]) * (v[(2 * S) + 3] - v[3]))) * texture.Width * texture.Height;
            level = texture.Level(texels / MathF.Abs(area));
        }

        float inverse = 1f / area;
        for (int y = minY; y <= maxY; y++)
        {
            float py = y + 0.5f;
            int row = (y - rowFrom) * width;
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f;
                float l0 = (((bx - px) * (cy - py)) - ((by - py) * (cx - px))) * inverse;
                float l1 = (((cx - px) * (ay - py)) - ((cy - py) * (ax - px))) * inverse;
                float l2 = 1f - l0 - l1;
                if (l0 < 0f || l1 < 0f || l2 < 0f)
                {
                    continue;
                }

                int i = row + x;
                float z = (l0 * v[2]) + (l1 * v[S + 2]) + (l2 * v[(2 * S) + 2]);
                if (pass == OverheadPass.GroundAdded ? z < depth[i] - SameHeight : z < depth[i])
                {
                    continue;
                }

                if (cutPerPixel && (l0 * over0) + (l1 * over1) + (l2 * over2) > cut)
                {
                    continue;
                }

                (float r, float g, float b, float a) = texture is null
                    ? (Stone.R, Stone.G, Stone.B, 1f)
                    : texture.Sample(level, (l0 * v[3]) + (l1 * v[S + 3]) + (l2 * v[(2 * S) + 3]), (l0 * v[4]) + (l1 * v[S + 4]) + (l2 * v[(2 * S) + 4]));
                if (pass == OverheadPass.CutOut && a < 0.5f)
                {
                    continue;
                }

                float shade = (l0 * v[5]) + (l1 * v[S + 5]) + (l2 * v[(2 * S) + 5]);
                if (pass == OverheadPass.GroundAdded)
                {
                    colour[i * 3] += r * shade;
                    colour[(i * 3) + 1] += g * shade;
                    colour[(i * 3) + 2] += b * shade;
                    continue;
                }

                depth[i] = z;
                colour[i * 3] = r * shade;
                colour[(i * 3) + 1] = g * shade;
                colour[(i * 3) + 2] = b * shade;
            }
        }
    }

    // A line of a width, blended over the band's colour.
    private static void Line(float ax, float ay, float bx, float by, float thickness, (float R, float G, float B, float A) ink, int width, int rowFrom, int rowTo, float[] colour)
    {
        float half = thickness * 0.5f;
        int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(ax, bx) - half));
        int maxX = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(ax, bx) + half));
        int minY = Math.Max(rowFrom, (int)MathF.Floor(MathF.Min(ay, by) - half));
        int maxY = Math.Min(rowTo - 1, (int)MathF.Ceiling(MathF.Max(ay, by) + half));
        float dx = bx - ax, dy = by - ay, length = (dx * dx) + (dy * dy);
        for (int y = minY; y <= maxY; y++)
        {
            float py = y + 0.5f;
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f;
                float t = length > 1e-6f ? Math.Clamp((((px - ax) * dx) + ((py - ay) * dy)) / length, 0f, 1f) : 0f;
                float ex = px - (ax + (t * dx)), ey = py - (ay + (t * dy));
                if ((ex * ex) + (ey * ey) > half * half)
                {
                    continue;
                }

                int i = (((y - rowFrom) * width) + x) * 3;
                colour[i] += (ink.R - colour[i]) * ink.A;
                colour[i + 1] += (ink.G - colour[i + 1]) * ink.A;
                colour[i + 2] += (ink.B - colour[i + 2]) * ink.A;
            }
        }
    }

    private static byte Channel(float v) => (byte)Math.Clamp((int)((v * 255f) + 0.5f), 0, 255);

    /// <summary>A texture and its smaller copies, each half the last; the first is the scene's own.</summary>
    private sealed class Mips
    {
        private const float ToUnit = 1f / 255f;

        private readonly List<(int Width, int Height, Bgra32[] Texels)> _levels = [];
        private readonly bool _repeat;

        public Mips(OverheadTexture texture)
        {
            _repeat = texture.Repeat;
            int width = texture.Width, height = texture.Height;
            Bgra32[] texels = texture.Pixels.Length >= width * height ? texture.Pixels : new Bgra32[width * height];
            _levels.Add((width, height, texels));
            while (width > 1 || height > 1)
            {
                int w = Math.Max(1, width / 2), h = Math.Max(1, height / 2);
                Bgra32[] half = new Bgra32[w * h];
                for (int y = 0; y < h; y++)
                {
                    int y0 = Math.Min(height - 1, 2 * y) * width, y1 = Math.Min(height - 1, (2 * y) + 1) * width;
                    for (int x = 0; x < w; x++)
                    {
                        int x0 = Math.Min(width - 1, 2 * x), x1 = Math.Min(width - 1, (2 * x) + 1);
                        Bgra32 a = texels[y0 + x0], b = texels[y0 + x1], c = texels[y1 + x0], d = texels[y1 + x1];
                        half[(y * w) + x] = new Bgra32(
                            (byte)((a.B + b.B + c.B + d.B + 2) / 4),
                            (byte)((a.G + b.G + c.G + d.G + 2) / 4),
                            (byte)((a.R + b.R + c.R + d.R + 2) / 4),
                            (byte)((a.A + b.A + c.A + d.A + 2) / 4));
                    }
                }

                (texels, width, height) = (half, w, h);
                _levels.Add((width, height, texels));
            }
        }

        public int Width => _levels[0].Width;

        public int Height => _levels[0].Height;

        // The level for so many texels a pixel.
        public int Level(float texelsPerPixel) =>
            texelsPerPixel <= 1f || float.IsNaN(texelsPerPixel) ? 0 : Math.Min(_levels.Count - 1, (int)MathF.Floor(0.5f * MathF.Log2(texelsPerPixel)));

        // A colour at a level, blended between the four texels round the point.
        public (float R, float G, float B, float A) Sample(int level, float u, float v)
        {
            (int width, int height, Bgra32[] texels) = _levels[level];
            float fx = (u * width) - 0.5f, fy = (v * height) - 0.5f;
            int x0 = (int)MathF.Floor(fx), y0 = (int)MathF.Floor(fy);
            float tx = fx - x0, ty = fy - y0;
            Bgra32 a = texels[Index(x0, y0, width, height)], b = texels[Index(x0 + 1, y0, width, height)];
            Bgra32 c = texels[Index(x0, y0 + 1, width, height)], d = texels[Index(x0 + 1, y0 + 1, width, height)];
            float wa = (1 - tx) * (1 - ty), wb = tx * (1 - ty), wc = (1 - tx) * ty, wd = tx * ty;
            return (
                ((a.R * wa) + (b.R * wb) + (c.R * wc) + (d.R * wd)) * ToUnit,
                ((a.G * wa) + (b.G * wb) + (c.G * wc) + (d.G * wd)) * ToUnit,
                ((a.B * wa) + (b.B * wb) + (c.B * wc) + (d.B * wd)) * ToUnit,
                ((a.A * wa) + (b.A * wb) + (c.A * wc) + (d.A * wd)) * ToUnit);
        }

        private int Index(int x, int y, int width, int height)
        {
            if (_repeat)
            {
                x = ((x % width) + width) % width;
                y = ((y % height) + height) % height;
            }
            else
            {
                x = Math.Clamp(x, 0, width - 1);
                y = Math.Clamp(y, 0, height - 1);
            }

            return (y * width) + x;
        }
    }
}
