using System.Runtime.InteropServices;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// The light a Project I.G.I. level bakes into its buildings' floors, read where a human stands on one: indoors, as I.G.I. 2
/// reads its own player's from the lightmapped face under its feet.
/// </summary>
/// <remarks>
/// <para>
/// The level's data, not its picture: each lightmapped model's own <c>.olm</c> pages (<c>lightmaps/lightmaps.res</c>), sampled
/// at the lightmap coordinates of the face under the feet. Every graphics preset sees the same guards.
/// </para>
/// <para>
/// A line goes down from just above the feet; the first lightmapped face it meets is what the player stands on. Where it meets
/// none, the player is on the ground or on something the level does not light, and <see cref="Igi1GroundLight"/> answers. Models
/// are taken where the level places them: a lift's floor is read where the lift starts.
/// </para>
/// <para>
/// A texel's light is its brightness as it is: Project I.G.I. multiplies its models by their lightmaps once, so its brightest
/// texel shows a floor as bright as its texture, which is I.G.I. 2's one (a byte times 2/255, drawn doubled). See
/// <see cref="Igi1GroundLight"/>.
/// </para>
/// </remarks>
public sealed class Igi1ObjectLight : IBakedLight
{
    /// <summary>How far above the feet the line starts, in metres: a step's height, so the floor the feet are on is met.</summary>
    public const double Above = 0.5;

    /// <summary>How far below the feet it reaches, in metres.</summary>
    public const double Below = 1.5;

    private readonly List<Lit> _models;

    private Igi1ObjectLight(List<Lit> models) => _models = models;

    /// <summary>Gets how many lightmapped models it reads.</summary>
    public int Count => _models.Count;

    /// <summary>Reads a level's lit models, or <see langword="null"/> where it bakes no object lightmaps.</summary>
    /// <param name="placements">The level's placed models.</param>
    /// <param name="archive">The level's <c>lightmaps/lightmaps.res</c>, or <see langword="null"/> where it has none.</param>
    /// <returns>The object light.</returns>
    public static Igi1ObjectLight? Of(IEnumerable<WorldPlacement> placements, IresArchive? archive)
    {
        ArgumentNullException.ThrowIfNull(placements);
        if (archive is null)
        {
            return null;
        }

        Dictionary<string, IresEntry> entries = new(StringComparer.OrdinalIgnoreCase);
        foreach (IresEntry entry in archive.Entries)
        {
            entries[Path.GetFileName(entry.Name.Replace('\\', '/'))] = entry;
        }

        Dictionary<string, ObjectLightmapImage[]?> pages = new(StringComparer.OrdinalIgnoreCase);
        List<Lit> models = [];
        foreach (WorldPlacement placement in placements)
        {
            if (!placement.HasLightmap || placement.IsSuppressed)
            {
                continue;
            }

            string name = placement.LightmapEntryName();
            if (!pages.TryGetValue(name, out ObjectLightmapImage[]? images))
            {
                images = entries.TryGetValue(name, out IresEntry? entry) ? Read(entry) : null;
                pages[name] = images;
            }

            MeshGeometry? mesh = placement.BentGeometry ?? MefMeshBuilder.Build(placement.Model, (float)(1.0 / WorldScene.UnitsPerMetre));
            if (images is not { Length: > 0 } || mesh is null)
            {
                continue;
            }

            models.Add(Lit.Place(placement, mesh, images));
        }

        return models.Count == 0 ? null : new Igi1ObjectLight(models);
    }

    /// <inheritdoc/>
    /// <remarks>The point is the feet: the light is the lightmapped face they stand on's, and nothing where they stand on none.</remarks>
    public float? LightAt(Vector3d point)
    {
        double x = point.X / WorldScene.UnitsPerMetre, y = point.Y / WorldScene.UnitsPerMetre, z = point.Z / WorldScene.UnitsPerMetre;
        double top = z + Above;
        double nearest = Above + Below;
        float? light = null;
        foreach (Lit model in _models)
        {
            double dx = x - model.Centre.X, dy = y - model.Centre.Y;
            if ((dx * dx) + (dy * dy) > model.Radius * model.Radius
                || model.Centre.Z - model.Radius > top
                || model.Centre.Z + model.Radius < z - Below)
            {
                continue;
            }

            if (model.Down(x, y, top, nearest) is { } hit)
            {
                nearest = hit.Depth;
                light = hit.Light;
            }
        }

        return light;
    }

    /// <summary>Reads a level's lit models from its mission directory's <c>lightmaps/lightmaps.res</c>.</summary>
    /// <param name="placements">The level's placed models.</param>
    /// <param name="missionDirectory">The level's mission directory.</param>
    /// <returns>The object light, or <see langword="null"/> where the level bakes no object lightmaps.</returns>
    public static Igi1ObjectLight? InMission(IEnumerable<WorldPlacement> placements, string missionDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(missionDirectory);
        string path = Path.Combine(missionDirectory, "lightmaps", "lightmaps.res");
        return Of(placements, File.Exists(path) ? IresArchive.Read(File.ReadAllBytes(path)) : null);
    }

    private static ObjectLightmapImage[]? Read(IresEntry entry)
    {
        try
        {
            return [.. ObjectLightmap.Read(entry.Data, AssetFormatProfile.Igi1).Images];
        }
        catch (PackFormatException)
        {
            return null;
        }
    }

    // One lightmapped model where the level places it: its faces in world metres, each with its page and lightmap coordinates.
    private sealed class Lit(Vector3d centre, double radius, Vector3d[] world, Vertex3D[] local, ushort[] indices, int[] pageOf, ObjectLightmapImage[] pages)
    {
        public Vector3d Centre => centre;

        public double Radius => radius;

        public static Lit Place(WorldPlacement placement, MeshGeometry mesh, ObjectLightmapImage[] pages)
        {
            // As the level's collision places a model: its origin plus its orientation's transpose applied to each vertex.
            Matrix3f rotation = placement.Orientation.Transposed();
            Vector3d[] world = new Vector3d[mesh.Vertices.Length];
            for (int i = 0; i < world.Length; i++)
            {
                Vector3f turned = rotation.Transform(mesh.Vertices[i].Position);
                world[i] = new Vector3d(placement.Position.X + turned.X, placement.Position.Y + turned.Y, placement.Position.Z + turned.Z);
            }

            Vector3f centreTurned = rotation.Transform(mesh.Bounds.Centre);
            Vector3d centre = new(placement.Position.X + centreTurned.X, placement.Position.Y + centreTurned.Y, placement.Position.Z + centreTurned.Z);

            int[] pageOf = new int[mesh.Indices.Length / 3];
            Array.Fill(pageOf, -1);
            foreach (MeshSubset subset in mesh.Subsets)
            {
                if (subset.LightmapPage < 0 || subset.LightmapPage >= pages.Length)
                {
                    continue;
                }

                for (int i = subset.IndexOffset; i + 2 < subset.IndexOffset + subset.IndexCount; i += 3)
                {
                    pageOf[i / 3] = subset.LightmapPage;
                }
            }

            return new Lit(centre, mesh.Bounds.Radius, world, mesh.Vertices, mesh.Indices, pageOf, pages);
        }

        // The nearest lit face a vertical line down from (x, y, top) meets within a depth, and its light there.
        public (double Depth, float Light)? Down(double x, double y, double top, double within)
        {
            (double Depth, float Light)? found = null;
            for (int t = 0; t < pageOf.Length; t++)
            {
                if (pageOf[t] < 0)
                {
                    continue;
                }

                int i0 = indices[t * 3], i1 = indices[(t * 3) + 1], i2 = indices[(t * 3) + 2];
                Vector3d a = world[i0], b = world[i1], c = world[i2];

                // Where the vertical meets the triangle's plane, as barycentric weights in X and Y.
                double det = ((b.Y - c.Y) * (a.X - c.X)) + ((c.X - b.X) * (a.Y - c.Y));
                if (Math.Abs(det) < 1e-12)
                {
                    continue;
                }

                double wa = (((b.Y - c.Y) * (x - c.X)) + ((c.X - b.X) * (y - c.Y))) / det;
                double wb = (((c.Y - a.Y) * (x - c.X)) + ((a.X - c.X) * (y - c.Y))) / det;
                double wc = 1.0 - wa - wb;
                if (wa < 0 || wb < 0 || wc < 0)
                {
                    continue;
                }

                double depth = top - ((wa * a.Z) + (wb * b.Z) + (wc * c.Z));
                if (depth < 0 || depth >= within || (found is { } best && depth >= best.Depth))
                {
                    continue;
                }

                double u = (wa * local[i0].LightmapU) + (wb * local[i1].LightmapU) + (wc * local[i2].LightmapU);
                double v = (wa * local[i0].LightmapV) + (wb * local[i1].LightmapV) + (wc * local[i2].LightmapV);
                found = (depth, Sample(pages[pageOf[t]], u, v));
            }

            return found;
        }

        private static float Sample(ObjectLightmapImage page, double u, double v)
        {
            int column = Math.Clamp((int)(u * page.Width), 0, page.Width - 1);
            int row = Math.Clamp((int)(v * page.Height), 0, page.Height - 1);
            Bgra32 texel = MemoryMarshal.Cast<byte, Bgra32>(page.Pixels.Span)[(row * page.Width) + column];
            return (texel.R + texel.G + texel.B) / (3f * 255f);
        }
    }
}
