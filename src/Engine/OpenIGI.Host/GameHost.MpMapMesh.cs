using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Presentation;

namespace OpenIGI.Host;

/// <content>
/// The textured level of <c>mp_mapshot</c>, for the layout page to draw from straight above at any zoom: the ground's passes
/// as the terrain draws them, and every placed model's faces with its own textures, written as one mesh, its textures and a
/// manifest in <c>&lt;path&gt;.mesh/</c>. Each corner carries the height of the ground under its face, so the page can cut
/// the level at a floor over the ground and look into a building storey by storey.
/// </content>
public sealed partial class GameHost
{
    // The kinds of group the mesh holds, drawn in this order: the ground's first passes, the passes added over them, then the
    // models, solid and cut out.
    private const int MeshGroundFirst = 0;
    private const int MeshGroundAdded = 1;
    private const int MeshSolid = 2;
    private const int MeshCutOut = 3;

    // Seven floats a corner: east, north and up in metres from the box's south-west corner (up from nought), the texture's
    // coordinates, the corner's weight (a ground pass's share) or light (a model's shading), and the ground under its face.
    private const int MeshStride = 7;

    // Writes the mesh over the box; a problem, or null once written.
    private string? WriteMapMesh(string path, double minX, double maxX, double minY, double maxY, double[] ground, int groundWidth, int groundHeight, double groundPerPixel)
    {
        string folder = $"{path}.mesh";
        Directory.CreateDirectory(Path.Combine(folder, "textures"));
        MapMeshWriter mesh = new(folder, minX, minY);

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

        double[] known = [.. ground.Where(static h => !double.IsNaN(h))];
        double low = known.Length > 0 ? known.Min() : 0, high = known.Length > 0 ? known.Max() : 0;
        bool groundDrawn = _terrain?.ExportOverhead(new Vector3d(minX, minY, low), new Vector3d(maxX, maxY, high), tiles, mesh) == true;
        if (!groundDrawn)
        {
            _logger.Warning("Desktop", $"The overhead mesh has no ground: terrain {(_terrain is null ? "absent" : "present")}, atlas {(tiles is null ? "unread" : $"{tiles.Tiles.Count} tiles")}.");
        }

        // The ground under a point, from the cast's grid.
        double GroundAt(double x, double y)
        {
            int column = Math.Clamp((int)((x - minX) / groundPerPixel), 0, groundWidth - 1);
            int row = Math.Clamp((int)((maxY - y) / groundPerPixel), 0, groundHeight - 1);
            return ground[(row * groundWidth) + column];
        }

        int faces = 0;
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
                int texture = MeshTextureOf(placement, slot, mesh, textures);
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

                    // The ground under the face as a whole, never above its lowest corner: a floor is cut by its own storey.
                    double lowest = Math.Min(p.Z, Math.Min(q.Z, r.Z));
                    double under = GroundAt((p.X + q.X + r.X) / 3, (p.Y + q.Y + r.Y) / 3);
                    under = double.IsNaN(under) ? lowest : Math.Min(under, lowest + (2 * WorldUnits.PerMetre));
                    int kind = cutOut ? MeshCutOut : MeshSolid;
                    mesh.Corner(kind, texture, p, uv[a * 2], uv[(a * 2) + 1], light, under);
                    mesh.Corner(kind, texture, q, uv[b * 2], uv[(b * 2) + 1], light, under);
                    mesh.Corner(kind, texture, r, uv[c * 2], uv[(c * 2) + 1], light, under);
                    faces++;
                }
            }
        }

        mesh.Finish();
        _logger.Information("Desktop", $"Wrote the overhead mesh {folder}: {faces} model faces, ground {(groundDrawn ? "drawn" : "missing")}.");
        return null;
    }

    // A model face's texture, written once a name; -1 for none.
    private int MeshTextureOf(WorldPlacement placement, int slot, MapMeshWriter mesh, Dictionary<string, int> known)
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
                id = mesh.Texture(width, height, image.GetLevel(0), repeat: true);
            }
        }
        catch (TextureFormatException)
        {
            id = -1;
        }

        known[name] = id;
        return id;
    }

    /// <summary>Gathers the mesh's groups and textures and writes them out.</summary>
    private sealed class MapMeshWriter(string folder, double originX, double originY) : IOverheadSink
    {
        private readonly Dictionary<(int Kind, int Texture), List<float>> _groups = [];
        private readonly Dictionary<int, int> _tiles = [];
        private readonly List<(string File, bool Repeat)> _textures = [];

        // Writes a texture as the next one; its number.
        public int Texture(int width, int height, ReadOnlySpan<Bgra32> pixels, bool repeat)
        {
            int id = _textures.Count;
            string file = string.Create(CultureInfo.InvariantCulture, $"textures/{id}.png");
            PngWriter.Write(Path.Combine(folder, file), pixels, width, height);
            _textures.Add((file, repeat));
            return id;
        }

        public void GroundTexture(int texture, int width, int height, ReadOnlySpan<Bgra32> pixels) =>
            _tiles[texture] = Texture(width, height, pixels, repeat: false);

        public void GroundPass(int texture, bool first, ReadOnlySpan<OverheadVertex> corners)
        {
            int id = _tiles.TryGetValue(texture, out int known) ? known : -1;
            foreach (OverheadVertex v in corners)
            {
                Corner(first ? MeshGroundFirst : MeshGroundAdded, id, new Vector3d(v.X, v.Y, v.Z), v.U, v.V, v.Weight, v.Z);
            }
        }

        public void Corner(int kind, int texture, Vector3d at, float u, float v, float shade, double under)
        {
            if (!_groups.TryGetValue((kind, texture), out List<float>? floats))
            {
                _groups[(kind, texture)] = floats = [];
            }

            floats.Add((float)((at.X - originX) / WorldUnits.PerMetre));
            floats.Add((float)((at.Y - originY) / WorldUnits.PerMetre));
            floats.Add((float)(at.Z / WorldUnits.PerMetre));
            floats.Add(u);
            floats.Add(v);
            floats.Add(shade);
            floats.Add((float)(under / WorldUnits.PerMetre));
        }

        public void Finish()
        {
            using FileStream file = File.Create(Path.Combine(folder, "mesh.json"));
            using Utf8JsonWriter json = new(file, new JsonWriterOptions { Indented = true });
            using BinaryWriter raw = new(File.Create(Path.Combine(folder, "mesh.bin")));
            json.WriteStartObject();
            json.WriteNumber("stride", MeshStride);
            json.WriteStartArray("textures");
            foreach ((string name, bool repeat) in _textures)
            {
                json.WriteStartObject();
                json.WriteString("file", name);
                json.WriteBoolean("repeat", repeat);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteStartArray("groups");
            long offset = 0;
            foreach (KeyValuePair<(int Kind, int Texture), List<float>> group in _groups.OrderBy(static g => g.Key.Kind))
            {
                json.WriteStartObject();
                json.WriteNumber("kind", group.Key.Kind);
                json.WriteNumber("texture", group.Key.Texture);
                json.WriteNumber("first", offset);
                json.WriteNumber("count", group.Value.Count / MeshStride);
                json.WriteEndObject();
                foreach (float f in CollectionsMarshal.AsSpan(group.Value))
                {
                    raw.Write(f);
                }

                offset += group.Value.Count / MeshStride;
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }
    }
}
