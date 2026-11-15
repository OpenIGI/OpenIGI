using System.Globalization;
using System.Runtime.InteropServices;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Host.Editor;
using OpenIGI.Presentation;

namespace OpenIGI.Host;

/// <content>
/// The level as it looks from straight above (<see cref="OverheadScene"/>): the ground's passes as the terrain draws them,
/// every placed model's faces with its own textures, and the collision triangles of everything built, each face carrying the
/// height of the ground under it. The editor's map draws it; <c>mp_mapshot</c> writes it for the layout page.
/// </content>
public sealed partial class GameHost
{
    /// <summary>The loaded level from above, for the editor's map; null when no level is up.</summary>
    /// <returns>The scene.</returns>
    internal OverheadScene? EditorOverheadScene()
    {
        System.Diagnostics.Stopwatch took = System.Diagnostics.Stopwatch.StartNew();
        if (OverheadBox(out _) is not { } box)
        {
            return null;
        }

        _logger.Information("Editor", string.Create(CultureInfo.InvariantCulture, $"The map's box took {took.ElapsedMilliseconds} ms."));

        // Textures no finer than a map needs: a model's is seen from above at a few centimetres a texel at most.
        OverheadScene scene = BuildOverheadScene(box.MinX, box.MaxX, box.MinY, box.MaxY, box.Bottom, box.Top, maxTextureSide: 256);
        _logger.Information("Editor", string.Create(CultureInfo.InvariantCulture, $"The map's scene took {took.ElapsedMilliseconds} ms: {scene.Groups.Sum(static g => g.Corners.Length) / (3 * OverheadScene.Stride)} triangles, {scene.Textures.Count} textures, {scene.Collision.Length / OverheadScene.CollisionStride} collision triangles, ground {(scene.HasGround ? "drawn" : "missing")}."));
        return scene;
    }

    // The level's extent from above, in world units: its objects, the odd far-flung one left out, a margin round them, and
    // heights well over and under everything; null and the problem when there is no level.
    private (double MinX, double MaxX, double MinY, double MaxY, double Top, double Bottom)? OverheadBox(out string? problem)
    {
        problem = null;
        if (_scene is not { } scene)
        {
            problem = "No level is loaded.";
            return null;
        }

        Vector3d[] at = [.. scene.Placements.Select(static p => WorldScene.ToUnits(p.Position))];
        if (at.Length == 0)
        {
            problem = "The level has no objects to frame.";
            return null;
        }

        double margin = MapShotMarginMetres * WorldUnits.PerMetre;
        return (
            Percentile(at.Select(static p => p.X), 0.01) - margin,
            Percentile(at.Select(static p => p.X), 0.99) + margin,
            Percentile(at.Select(static p => p.Y), 0.01) - margin,
            Percentile(at.Select(static p => p.Y), 0.99) + margin,
            at.Max(static p => p.Z) + (300 * WorldUnits.PerMetre),
            at.Min(static p => p.Z) - (600 * WorldUnits.PerMetre));
    }

    // The scene over a box in world units, textures no wider than a side.
    private OverheadScene BuildOverheadScene(double minX, double maxX, double minY, double maxY, double bottom, double top, int maxTextureSide)
    {
        OverheadSceneBuilder builder = new(minX, minY, maxTextureSide);
        double west = minX / WorldUnits.PerMetre, south = minY / WorldUnits.PerMetre;
        double east = maxX / WorldUnits.PerMetre, north = maxY / WorldUnits.PerMetre;

        // The ground, as the terrain draws it, from its atlas read afresh.
        TexAtlasFile? tiles = null;
        if (_session.Mission is { } mission && _resources.TryLoad(mission.TerrainBasePath + ".tex", out ReadOnlyMemory<byte> atlas))
        {
            try
            {
                tiles = TexAtlasFile.Read(atlas);
            }
            catch (TextureFormatException)
            {
                tiles = null;
            }
        }

        // The terrain is handed over as seen from above the box, its frustum just taking in the ground's own heights: the box's
        // heights reach far past them and would narrow the view at the ground.
        System.Diagnostics.Stopwatch stage = System.Diagnostics.Stopwatch.StartNew();
        (double low, double high) = GroundRange(minX, maxX, minY, maxY, bottom, top);
        long rangeMs = stage.ElapsedMilliseconds;
        stage.Restart();
        bool groundDrawn = _terrain?.ExportOverhead(new Vector3d(minX, minY, low), new Vector3d(maxX, maxY, high), tiles, builder) == true;
        long groundMs = stage.ElapsedMilliseconds;
        if (!groundDrawn)
        {
            _logger.Warning("Desktop", $"The overhead scene has no ground: terrain {(_terrain is null ? "absent" : "present")}, atlas {(tiles is null ? "unread" : $"{tiles.Tiles.Count} tiles")}.");
        }

        // The ground under a point, in world units: from its own triangles, or the editable ground's heights where it drew none.
        OverheadScene ground = builder.Scene(west, south, east, north);
        Game.World.IEditableTerrain? heights = _terrain?.Editable;
        double GroundAt(double x, double y)
        {
            double metres = ground.GroundAt(x / WorldUnits.PerMetre, y / WorldUnits.PerMetre);
            if (double.IsNaN(metres) && heights is not null && heights.TryGetHeight(x / WorldUnits.PerMetre, y / WorldUnits.PerMetre, out double z))
            {
                metres = z;
            }

            return metres * WorldUnits.PerMetre;
        }

        // A face's ground: under its middle, never above its lowest corner by more than two metres, so a floor is cut by its
        // own storey; its lowest corner where there is no ground.
        double Under(Vector3d p, Vector3d q, Vector3d r)
        {
            double lowest = Math.Min(p.Z, Math.Min(q.Z, r.Z));
            double under = GroundAt((p.X + q.X + r.X) / 3, (p.Y + q.Y + r.Y) / 3);
            return double.IsNaN(under) ? lowest : Math.Min(under, lowest + (2 * WorldUnits.PerMetre));
        }

        long indexMs = stage.ElapsedMilliseconds - groundMs;
        AddModelFaces(builder, minX, maxX, minY, maxY, Under);
        long modelsMs = stage.ElapsedMilliseconds - groundMs - indexMs;

        List<CollisionTriangle> triangles = [];
        _mission.Solids?.CollectTriangles(new Vector3d(minX, minY, bottom), new Vector3d(maxX, maxY, top), triangles);
        float[] collision = new float[triangles.Count * OverheadScene.CollisionStride];
        for (int t = 0; t < triangles.Count; t++)
        {
            CollisionTriangle triangle = triangles[t];
            int o = t * OverheadScene.CollisionStride;
            foreach ((Vector3d corner, int k) in ((Vector3d, int)[])[(triangle.A, 0), (triangle.B, 3), (triangle.C, 6)])
            {
                collision[o + k] = (float)((corner.X - minX) / WorldUnits.PerMetre);
                collision[o + k + 1] = (float)((corner.Y - minY) / WorldUnits.PerMetre);
                collision[o + k + 2] = (float)(corner.Z / WorldUnits.PerMetre);
            }

            collision[o + 9] = (float)(Under(triangle.A, triangle.B, triangle.C) / WorldUnits.PerMetre);
        }

        OverheadScene built = builder.Scene(west, south, east, north, collision);
        _logger.Information("Editor", string.Create(CultureInfo.InvariantCulture, $"Overhead scene stages: ground's heights {rangeMs} ms, ground {groundMs} ms, ground index {indexMs} ms, models {modelsMs} ms, collision and the rest {stage.ElapsedMilliseconds - groundMs - indexMs - modelsMs} ms."));
        return built;
    }

    // The ground's lowest and highest over a box, sampled on a grid; the box's own heights where there is no ground to sample.
    private (double Low, double High) GroundRange(double minX, double maxX, double minY, double maxY, double bottom, double top)
    {
        if (_terrain?.Editable is not { } heights)
        {
            return (bottom, top);
        }

        double low = double.MaxValue, high = double.MinValue;
        const int Samples = 16;
        for (int row = 0; row <= Samples; row++)
        {
            for (int column = 0; column <= Samples; column++)
            {
                double x = (minX + ((maxX - minX) * column / Samples)) / WorldUnits.PerMetre;
                double y = (minY + ((maxY - minY) * row / Samples)) / WorldUnits.PerMetre;
                if (heights.TryGetHeight(x, y, out double z))
                {
                    low = Math.Min(low, z * WorldUnits.PerMetre);
                    high = Math.Max(high, z * WorldUnits.PerMetre);
                }
            }
        }

        return low <= high ? (low - WorldUnits.PerMetre, high + WorldUnits.PerMetre) : (bottom, top);
    }

    // Every placed model's faces in the box, with their textures; a face seen edge-on from above is left out.
    private void AddModelFaces(OverheadSceneBuilder builder, double minX, double maxX, double minY, double maxY, Func<Vector3d, Vector3d, Vector3d, double> under)
    {
        Dictionary<string, int> textures = new(StringComparer.OrdinalIgnoreCase);
        foreach (WorldPlacement placement in _scene?.Placements ?? [])
        {
            MefModel model = placement.Model;
            if (model.IsEmpty || model.BoneNames.Count > 0)
            {
                continue;
            }

            Vector3d origin = WorldScene.ToUnits(placement.Position);
            double slack = 500 * WorldUnits.PerMetre;
            if (origin.X < minX - slack || origin.X > maxX + slack || origin.Y < minY - slack || origin.Y > maxY + slack)
            {
                continue;
            }

            Matrix3f toWorld = placement.Orientation.Transposed();
            List<(Vector3d[] Corners, float[] Uv, List<(int A, int B, int C)> Faces, int Slot, bool CutOut)> parts = [];
            if (placement.BentGeometry is { } bent)
            {
                Vector3d[] corners = [.. bent.Vertices.Select(v => origin + toWorld.Transform(new Vector3d(v.Position.X, v.Position.Y, v.Position.Z) * WorldUnits.PerMetre))];
                float[] uv = [.. bent.Vertices.SelectMany(static v => (float[])[v.DiffuseU, v.DiffuseV])];
                foreach (MeshSubset subset in bent.Subsets)
                {
                    List<(int, int, int)> list = [];
                    for (int i = subset.IndexOffset; i + 2 < subset.IndexOffset + subset.IndexCount; i += 3)
                    {
                        list.Add((bent.Indices[i], bent.Indices[i + 1], bent.Indices[i + 2]));
                    }

                    parts.Add((corners, uv, list, subset.MaterialSlot, subset.Blend != SubsetBlend.Opaque));
                }
            }
            else
            {
                ReadOnlySpan<MefVertex> source = model.Vertices;
                Vector3d[] corners = new Vector3d[source.Length];
                float[] uv = new float[source.Length * 2];
                for (int i = 0; i < source.Length; i++)
                {
                    corners[i] = origin + toWorld.Transform(new Vector3d(source[i].Position.X, source[i].Position.Y, source[i].Position.Z));
                    uv[i * 2] = source[i].DiffuseU;
                    uv[(i * 2) + 1] = source[i].DiffuseV;
                }

                foreach (MefDrawRecord record in model.DrawRecords)
                {
                    parts.Add((corners, uv, [.. model.Triangles(record)], record.MaterialSlot, record.IsTransparent));
                }
            }

            foreach ((Vector3d[] corners, float[] uv, List<(int A, int B, int C)> list, int slot, bool cutOut) in parts)
            {
                int texture = OverheadTextureOf(placement, slot, builder, textures);
                foreach ((int a, int b, int c) in list)
                {
                    if ((uint)a >= (uint)corners.Length || (uint)b >= (uint)corners.Length || (uint)c >= (uint)corners.Length)
                    {
                        continue;
                    }

                    Vector3d p = corners[a], q = corners[b], r = corners[c];
                    Vector3d n = Vector3d.Cross(q - p, r - p);
                    double length = n.Length;
                    if (length < 1e-6 || Math.Abs(n.Z) / length < 0.02)
                    {
                        continue;
                    }

                    // Lit by how much it faces up and towards the north-west, so roofs' slopes and steps read.
                    double sign = n.Z < 0 ? -1 : 1;
                    float light = (float)Math.Clamp(0.72 + (0.28 * sign * ((-n.X * 0.45) + (n.Y * 0.45) + (n.Z * 0.77)) / length), 0.45, 1.0);
                    double ground = under(p, q, r);
                    OverheadPass pass = cutOut ? OverheadPass.CutOut : OverheadPass.Solid;
                    builder.Corner(pass, texture, p, uv[a * 2], uv[(a * 2) + 1], light, ground);
                    builder.Corner(pass, texture, q, uv[b * 2], uv[(b * 2) + 1], light, ground);
                    builder.Corner(pass, texture, r, uv[c * 2], uv[(c * 2) + 1], light, ground);
                }
            }
        }
    }

    // A model face's texture, taken once a name; -1 for none.
    private int OverheadTextureOf(WorldPlacement placement, int slot, OverheadSceneBuilder builder, Dictionary<string, int> known)
    {
        if (slot == MefDrawRecord.NoMaterialSlot || !placement.Pack.TryGetTextureName(placement.Instance, slot, out string name))
        {
            return -1;
        }

        if (known.TryGetValue(name, out int id))
        {
            return id;
        }

        id = -1;
        try
        {
            if (_resources.TryLoad(LoadedPack.TextureResourceName(name), out ReadOnlyMemory<byte> bytes)
                && ImageSniffer.TryDecode(bytes.Span, out TextureImage? image) && image is not null)
            {
                (int width, int height) = image.GetLevelSize(0);
                id = builder.Texture(width, height, image.GetLevel(0), repeat: true);
            }
        }
        catch (TextureFormatException)
        {
            id = -1;
        }

        known[name] = id;
        return id;
    }

    /// <summary>Gathers an <see cref="OverheadScene"/>'s textures and triangles as the ground and the models hand them over.</summary>
    private sealed class OverheadSceneBuilder(double originX, double originY, int maxTextureSide) : IOverheadSink
    {
        private readonly Dictionary<(OverheadPass Pass, int Texture), List<float>> _groups = [];
        private readonly Dictionary<int, int> _tiles = [];
        private readonly List<OverheadTexture> _textures = [];

        // Takes a texture as the next one, halved until it fits the side; its number.
        public int Texture(int width, int height, ReadOnlySpan<Bgra32> pixels, bool repeat)
        {
            Bgra32[] texels = pixels.ToArray();
            while ((width > maxTextureSide || height > maxTextureSide) && width > 1 && height > 1)
            {
                (texels, width, height) = Halve(texels, width, height);
            }

            _textures.Add(new OverheadTexture(width, height, texels, repeat));
            return _textures.Count - 1;
        }

        public void GroundTexture(int texture, int width, int height, ReadOnlySpan<Bgra32> pixels) =>
            _tiles[texture] = Texture(width, height, pixels, repeat: false);

        public void GroundPass(int texture, bool first, ReadOnlySpan<OverheadVertex> corners)
        {
            int id = _tiles.TryGetValue(texture, out int known) ? known : -1;
            foreach (OverheadVertex v in corners)
            {
                Corner(first ? OverheadPass.GroundFirst : OverheadPass.GroundAdded, id, new Vector3d(v.X, v.Y, v.Z), v.U, v.V, v.Weight, v.Z);
            }
        }

        // One corner, in world units.
        public void Corner(OverheadPass pass, int texture, Vector3d at, float u, float v, float shade, double under)
        {
            if (!_groups.TryGetValue((pass, texture), out List<float>? floats))
            {
                _groups[(pass, texture)] = floats = [];
            }

            floats.Add((float)((at.X - originX) / WorldUnits.PerMetre));
            floats.Add((float)((at.Y - originY) / WorldUnits.PerMetre));
            floats.Add((float)(at.Z / WorldUnits.PerMetre));
            floats.Add(u);
            floats.Add(v);
            floats.Add(shade);
            floats.Add((float)(under / WorldUnits.PerMetre));
        }

        // What has been gathered so far, the groups in the order they are drawn.
        public OverheadScene Scene(double west, double south, double east, double north, float[]? collision = null) =>
            new(west, south, east, north, [.. _textures], [.. _groups.OrderBy(static g => g.Key.Pass).Select(static g => new OverheadGroup(g.Key.Pass, g.Key.Texture, [.. CollectionsMarshal.AsSpan(g.Value)]))])
            {
                Collision = collision ?? [],
            };

        private static (Bgra32[] Texels, int Width, int Height) Halve(Bgra32[] texels, int width, int height)
        {
            int w = Math.Max(1, width / 2), h = Math.Max(1, height / 2);
            Bgra32[] half = new Bgra32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int b = 0, g = 0, r = 0, a = 0;
                    foreach ((int dx, int dy) in ((int, int)[])[(0, 0), (1, 0), (0, 1), (1, 1)])
                    {
                        Bgra32 t = texels[(Math.Min(height - 1, (2 * y) + dy) * width) + Math.Min(width - 1, (2 * x) + dx)];
                        b += t.B;
                        g += t.G;
                        r += t.R;
                        a += t.A;
                    }

                    half[(y * w) + x] = new Bgra32((byte)(b / 4), (byte)(g / 4), (byte)(r / 4), (byte)(a / 4));
                }
            }

            return (half, w, h);
        }
    }
}
