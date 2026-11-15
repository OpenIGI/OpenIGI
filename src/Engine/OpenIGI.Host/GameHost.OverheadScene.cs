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
/// <remarks>
/// Made in two halves. What reads the running level -- the terrain, the placements, the collision, the files (the resource
/// registry keeps what it reads, so it is asked from the frame's thread only) -- is gathered on the frame's thread, a few
/// milliseconds a frame for the editor; the textures' decoding, the ground's index and every face's ground are worked out from
/// that alone, off it.
/// </remarks>
public sealed partial class GameHost
{
    // How long a frame gives to gathering the editor's map, in milliseconds.
    private const double OverheadFrameBudgetMs = 4;

    private OverheadJob? _overheadJob;

    /// <summary>The loaded level from above, for the editor's map: gathered over the next frames, finished off them.</summary>
    /// <returns>The scene to come; null when no level is up.</returns>
    /// <remarks>One at a time: a scene asked for again drops the one being gathered (its task is cancelled).</remarks>
    internal Task<OverheadScene>? EditorOverheadScene()
    {
        if (OverheadBox(out _) is not { } box || _scene is not { } level)
        {
            return null;
        }

        _overheadJob?.Done.TrySetCanceled();
        OverheadGathering gathering = new(box.MinX, box.MinY, box.MaxX, box.MaxY);
        _overheadJob = new OverheadJob(level, gathering, GatherOverhead(gathering, box.Bottom, box.Top).GetEnumerator());
        return _overheadJob.Done.Task;
    }

    // Gathers the editor's map for a few milliseconds of this frame; once it has all, finishes it off the frame. A level gone
    // from under it ends it.
    private void AdvanceOverheadGathering()
    {
        if (_overheadJob is not { } job)
        {
            return;
        }

        if (!ReferenceEquals(job.Level, _scene) || job.Done.Task.IsCompleted)
        {
            job.Done.TrySetCanceled();
            job.Steps.Dispose();
            _overheadJob = null;
            return;
        }

        System.Diagnostics.Stopwatch frame = System.Diagnostics.Stopwatch.StartNew();
        bool more = true;
        while (frame.Elapsed.TotalMilliseconds < OverheadFrameBudgetMs && (more = job.Steps.MoveNext()))
        {
        }

        job.Frames++;
        job.FrameMs += frame.Elapsed.TotalMilliseconds;
        job.LongestMs = Math.Max(job.LongestMs, frame.Elapsed.TotalMilliseconds);
        if (more)
        {
            return;
        }

        job.Steps.Dispose();
        _overheadJob = null;
        IOpenIgiLogger logger = _logger;
        _ = Task.Run(() =>
        {
            try
            {
                System.Diagnostics.Stopwatch finishing = System.Diagnostics.Stopwatch.StartNew();

                // Textures no finer than a map needs: a model's is seen from above at a few centimetres a texel at most.
                OverheadScene scene = job.Gathering.Finish(maxTextureSide: 256);
                logger.Information("Editor", string.Create(CultureInfo.InvariantCulture, $"The map's scene: gathered over {job.Frames} frames ({job.FrameMs:0} ms of them, the longest {job.LongestMs:0} ms), finished in {finishing.ElapsedMilliseconds} ms off the frame; {scene.Groups.Sum(static g => g.Corners.Length) / (3 * OverheadScene.Stride)} triangles, {scene.Textures.Count} textures, {scene.Collision.Length / OverheadScene.CollisionStride} collision triangles, ground {(scene.HasGround ? "drawn" : "missing")}."));
                job.Done.TrySetResult(scene);
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentException or InvalidOperationException or IndexOutOfRangeException)
            {
                job.Done.TrySetException(error);
            }
        });
    }

    // The scene over a box in world units, gathered and finished at once (mp_mapshot), textures kept whole.
    private OverheadScene OverheadSceneNow(double minX, double maxX, double minY, double maxY, double bottom, double top)
    {
        OverheadGathering gathering = new(minX, minY, maxX, maxY);
        foreach (int _ in GatherOverhead(gathering, bottom, top))
        {
        }

        return gathering.Finish(int.MaxValue);
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

    // Everything the scene is made from that has to be read from the running level, a step at a time: the ground's atlas
    // file, a step a patch of ground, a step a placed model, then the collision.
    private IEnumerable<int> GatherOverhead(OverheadGathering gathering, double bottom, double top)
    {
        // The ground's atlas, read afresh (the one drawn from has given its texels to the device) and decoded when finished.
        if (_session.Mission is { } mission && _resources.TryLoad(mission.TerrainBasePath + ".tex", out ReadOnlyMemory<byte> atlas))
        {
            gathering.Atlas = atlas;
        }

        yield return 0;
        if (_terrain is { } terrain)
        {
            foreach (int patch in terrain.ExportOverhead(new Vector3d(gathering.MinX, gathering.MinY, bottom), new Vector3d(gathering.MaxX, gathering.MaxY, top), gathering))
            {
                yield return patch;
            }
        }

        Dictionary<string, int> textures = new(StringComparer.OrdinalIgnoreCase);
        foreach (WorldPlacement placement in _scene?.Placements.ToArray() ?? [])
        {
            AddModelFaces(gathering, placement, textures);
            yield return 0;
        }

        List<CollisionTriangle> triangles = [];
        _mission.Solids?.CollectTriangles(new Vector3d(gathering.MinX, gathering.MinY, bottom), new Vector3d(gathering.MaxX, gathering.MaxY, top), triangles);
        gathering.Collision = [.. triangles];
    }

    // A placed model's faces in the box, with their textures' files; a face seen edge-on from above is left out.
    private void AddModelFaces(OverheadGathering gathering, WorldPlacement placement, Dictionary<string, int> textures)
    {
        MefModel model = placement.Model;
        if (model.IsEmpty || model.BoneNames.Count > 0)
        {
            return;
        }

        Vector3d origin = WorldScene.ToUnits(placement.Position);
        double slack = 500 * WorldUnits.PerMetre;
        if (origin.X < gathering.MinX - slack || origin.X > gathering.MaxX + slack || origin.Y < gathering.MinY - slack || origin.Y > gathering.MaxY + slack)
        {
            return;
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
            int texture = OverheadTextureOf(placement, slot, gathering, textures);
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

                // Lit by how much it faces up and towards the north-west, so roofs' slopes and steps read. Its ground is
                // worked out once the ground's index is made, off the frame.
                double sign = n.Z < 0 ? -1 : 1;
                float light = (float)Math.Clamp(0.72 + (0.28 * sign * ((-n.X * 0.45) + (n.Y * 0.45) + (n.Z * 0.77)) / length), 0.45, 1.0);
                OverheadPass pass = cutOut ? OverheadPass.CutOut : OverheadPass.Solid;
                gathering.Corner(pass, texture, p, uv[a * 2], uv[(a * 2) + 1], light, double.NaN);
                gathering.Corner(pass, texture, q, uv[b * 2], uv[(b * 2) + 1], light, double.NaN);
                gathering.Corner(pass, texture, r, uv[c * 2], uv[(c * 2) + 1], light, double.NaN);
            }
        }
    }

    // A model face's texture, its file read once a name and decoded later; -1 for none.
    private int OverheadTextureOf(WorldPlacement placement, int slot, OverheadGathering gathering, Dictionary<string, int> known)
    {
        if (slot == MefDrawRecord.NoMaterialSlot || !placement.Pack.TryGetTextureName(placement.Instance, slot, out string name))
        {
            return -1;
        }

        if (!known.TryGetValue(name, out int id))
        {
            known[name] = id = _resources.TryLoad(LoadedPack.TextureResourceName(name), out ReadOnlyMemory<byte> bytes) ? gathering.TextureFile(bytes) : -1;
        }

        return id;
    }

    /// <summary>A scene being gathered for the editor: the level it is of, the steps left, and the task it ends in.</summary>
    private sealed class OverheadJob(WorldScene level, OverheadGathering gathering, IEnumerator<int> steps)
    {
        public WorldScene Level { get; } = level;

        public OverheadGathering Gathering { get; } = gathering;

        public IEnumerator<int> Steps { get; } = steps;

        public TaskCompletionSource<OverheadScene> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Frames { get; set; }

        public double FrameMs { get; set; }

        public double LongestMs { get; set; }
    }

    /// <summary>
    /// What an <see cref="OverheadScene"/> is made from, as read from the level: the ground's passes and atlas file, the
    /// models' faces and their textures' files, the collision; <see cref="Finish"/> makes the scene from it on any thread.
    /// </summary>
    private sealed class OverheadGathering(double minX, double minY, double maxX, double maxY) : IOverheadSink
    {
        // A face's ground, at most this far over its lowest corner: a floor is cut by its own storey.
        private const double MostGroundOverFace = 2.0;

        private readonly Dictionary<(OverheadPass Pass, int Texture), List<float>> _groups = [];

        // The models' textures' files, decoded when finished; a ground pass names its atlas tile as -2 - tile.
        private readonly List<ReadOnlyMemory<byte>> _files = [];

        public double MinX => minX;

        public double MinY => minY;

        public double MaxX => maxX;

        public double MaxY => maxY;

        public ReadOnlyMemory<byte> Atlas { get; set; }

        public CollisionTriangle[] Collision { get; set; } = [];

        public int TextureFile(ReadOnlyMemory<byte> file)
        {
            _files.Add(file);
            return _files.Count - 1;
        }

        public void GroundPass(int tile, bool first, ReadOnlySpan<OverheadVertex> corners)
        {
            foreach (OverheadVertex v in corners)
            {
                Corner(first ? OverheadPass.GroundFirst : OverheadPass.GroundAdded, -2 - tile, new Vector3d(v.X, v.Y, v.Z), v.U, v.V, v.Weight, v.Z);
            }
        }

        // One corner, in world units; its ground NaN while it is still to be worked out.
        public void Corner(OverheadPass pass, int texture, Vector3d at, float u, float v, float shade, double under)
        {
            if (!_groups.TryGetValue((pass, texture), out List<float>? floats))
            {
                _groups[(pass, texture)] = floats = [];
            }

            floats.Add((float)((at.X - minX) / WorldUnits.PerMetre));
            floats.Add((float)((at.Y - minY) / WorldUnits.PerMetre));
            floats.Add((float)(at.Z / WorldUnits.PerMetre));
            floats.Add(u);
            floats.Add(v);
            floats.Add(shade);
            floats.Add((float)(under / WorldUnits.PerMetre));
        }

        /// <summary>Makes the scene: the textures decoded and no wider than a side, and every face's ground found.</summary>
        /// <param name="maxTextureSide">The widest a texture is kept.</param>
        /// <returns>The scene.</returns>
        public OverheadScene Finish(int maxTextureSide)
        {
            double west = minX / WorldUnits.PerMetre, south = minY / WorldUnits.PerMetre;
            double east = maxX / WorldUnits.PerMetre, north = maxY / WorldUnits.PerMetre;

            // The models' textures, then the ground's tiles after them.
            TexAtlasFile? tiles = null;
            try
            {
                tiles = Atlas.IsEmpty ? null : TexAtlasFile.Read(Atlas);
            }
            catch (TextureFormatException)
            {
                tiles = null;
            }

            int tileCount = tiles?.Tiles.Count ?? 0;
            OverheadTexture[] textures = new OverheadTexture[_files.Count + tileCount];
            Parallel.For(0, textures.Length, i => textures[i] = Fit(
                i < _files.Count ? Decode(_files[i]) : Tile(tiles!.Tiles[i - _files.Count]),
                maxTextureSide));
            int TextureOf(int key) => key >= -1 ? key : -2 - key < tileCount ? _files.Count + (-2 - key) : -1;
            List<OverheadGroup> groups = [.. _groups.OrderBy(static g => g.Key.Pass).Select(g => new OverheadGroup(g.Key.Pass, TextureOf(g.Key.Texture), [.. CollectionsMarshal.AsSpan(g.Value)]))];

            // The ground under a point, from its own first passes; each model face's from it, under its middle.
            OverheadScene ground = new(west, south, east, north, textures, [.. groups.Where(static g => g.Pass == OverheadPass.GroundFirst)]);
            float Under(float ax, float ay, float az, float bx, float by, float bz, float cx, float cy, float cz)
            {
                float lowest = MathF.Min(az, MathF.Min(bz, cz));
                double under = ground.GroundAt(west + ((ax + bx + cx) / 3), south + ((ay + by + cy) / 3));
                return double.IsNaN(under) ? lowest : (float)Math.Min(under, lowest + MostGroundOverFace);
            }

            const int S = OverheadScene.Stride;
            foreach (OverheadGroup group in groups.Where(static g => g.Pass >= OverheadPass.Solid))
            {
                float[] v = group.Corners;
                Parallel.For(0, v.Length / (3 * S), t =>
                {
                    int o = t * 3 * S;
                    float under = Under(v[o], v[o + 1], v[o + 2], v[o + S], v[o + S + 1], v[o + S + 2], v[o + (2 * S)], v[o + (2 * S) + 1], v[o + (2 * S) + 2]);
                    v[o + 6] = v[o + S + 6] = v[o + (2 * S) + 6] = under;
                });
            }

            float[] collision = new float[Collision.Length * OverheadScene.CollisionStride];
            for (int t = 0; t < Collision.Length; t++)
            {
                CollisionTriangle triangle = Collision[t];
                int o = t * OverheadScene.CollisionStride;
                foreach ((Vector3d corner, int k) in ((Vector3d, int)[])[(triangle.A, 0), (triangle.B, 3), (triangle.C, 6)])
                {
                    collision[o + k] = (float)((corner.X - minX) / WorldUnits.PerMetre);
                    collision[o + k + 1] = (float)((corner.Y - minY) / WorldUnits.PerMetre);
                    collision[o + k + 2] = (float)(corner.Z / WorldUnits.PerMetre);
                }

                collision[o + 9] = Under(collision[o], collision[o + 1], collision[o + 2], collision[o + 3], collision[o + 4], collision[o + 5], collision[o + 6], collision[o + 7], collision[o + 8]);
            }

            return new OverheadScene(west, south, east, north, textures, groups) { Collision = collision };
        }

        // A ground tile, which stops at its edges.
        private static OverheadTexture Tile(TextureImage tile) => new(tile.Width, tile.Height, tile.GetLevel(0).ToArray(), Repeat: false);

        // A model's texture from its file; plain stone where it does not decode.
        private static OverheadTexture Decode(ReadOnlyMemory<byte> file)
        {
            try
            {
                if (ImageSniffer.TryDecode(file.Span, out TextureImage? image) && image is not null)
                {
                    (int width, int height) = image.GetLevelSize(0);
                    return new OverheadTexture(width, height, image.GetLevel(0).ToArray(), Repeat: true);
                }
            }
            catch (TextureFormatException)
            {
                // Drawn as stone, below.
            }

            return new OverheadTexture(1, 1, [Bgra32.FromRgb(168, 163, 150)], Repeat: true);
        }

        // A texture halved until it fits the side.
        private static OverheadTexture Fit(OverheadTexture texture, int side)
        {
            (Bgra32[] texels, int width, int height) = (texture.Pixels, texture.Width, texture.Height);
            while ((width > side || height > side) && width > 1 && height > 1)
            {
                (texels, width, height) = Halve(texels, width, height);
            }

            return texture with { Width = width, Height = height, Pixels = texels };
        }

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
