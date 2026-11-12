using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>
/// Finds the lamps a level's glows do not mark, by what the artists baked onto them.
/// </summary>
/// <remarks>
/// <para>
/// <b>A glow is a bloom, and not every lamp was given one.</b> An office's ceiling battens are a sub-mesh
/// of the building, carry no glow, and are skinned with a page no glowing lamp shares; neither
/// <see cref="GlowLamps"/> nor the page it confirms can find them, and the room stays as dark as its bake
/// drawn at a fraction.
/// </para>
/// <para>
/// <b>The bake knows.</b> The original's lightmapper writes a flat mid-grey over a surface that lights
/// itself (see <see cref="SelfLit"/>), where a surface the light merely fell on carries the uneven light
/// that fell. So a sub-mesh whose separate pieces are each the size of a fitting, skinned with a bright
/// page, and with a piece baked flat at exactly that value, is a lamp or a lit panel; a sheet of paper
/// under a desk lamp is small, pale and brightly lit, and is not flat.
/// </para>
/// <para>
/// Only where a level declares lamps at all (its models carry glows): Project I.G.I.'s levels are read
/// by <see cref="LampDetector"/> and the list a person keeps, which this does not second-guess.
/// </para>
/// </remarks>
public static class BakedLamps
{
    /// <summary>The widest a separate piece can be and still be one fitting, in metres; a ceiling batten is two.</summary>
    public const float MaximumSpan = 3f;

    /// <summary>The value the original's lightmapper writes over a surface that lights itself: mid-grey, 128 in 255.</summary>
    /// <remarks>
    /// Measured over the sequel's levels. A surface the radiosity LIT carries whatever landed on it, uneven
    /// and seldom this; a lamp's glass, a lit screen and a whole lamp model each carry exactly this, flat
    /// from edge to edge -- the bake's way of saying the surface is drawn as it is painted.
    /// </remarks>
    public const float SelfLit = 128f / 255f;

    /// <summary>How far from <see cref="SelfLit"/> a texel may sit and still be it: two steps of a byte.</summary>
    public const float SelfLitTolerance = 2.5f / 255f;

    /// <summary>The least area baked flat at the self-lit value for a sub-mesh to be a lamp, in square metres.</summary>
    /// <remarks>A hand's breadth square: below it one stray texel would speak for the whole fitting.</remarks>
    public const float MinimumFlatArea = 0.01f;

    /// <summary>How bright a page must be, averaged whole, for what it skins to be a lamp.</summary>
    /// <remarks>
    /// Lower than the detector asks. A work light is skinned from one page that is mostly its black
    /// housing and stand, with the glass a corner of it, and the page's average says housing. The flat
    /// bake is the evidence here; this only keeps out what is black all over.
    /// </remarks>
    public const float MinimumBrightness = 0.2f;

    /// <summary>The radiance a lit panel is given: enough to read as lit, far short of a lamp.</summary>
    public const float PanelRadiance = 0.75f;

    private const string LogCategory = "Emissive";

    /// <summary>Finds the sub-meshes a level's bake lights as sources.</summary>
    /// <param name="scene">The built world.</param>
    /// <param name="resources">Where the textures and the lightmaps are read from.</param>
    /// <param name="formats">The title's asset formats, which the lightmaps are read by.</param>
    /// <param name="judged">Whether a sub-mesh, by its key, has been judged already and is to be left alone.</param>
    /// <param name="subMeshes">Receives each lamp sub-mesh and the strength it emits at.</param>
    /// <param name="textures">Receives the textures those sub-meshes draw with.</param>
    /// <param name="logger">Where the conclusion is reported.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static void Find(
        WorldScene scene,
        ResourceRegistry resources,
        AssetFormatProfile formats,
        Func<string, string, bool> judged,
        Dictionary<string, float> subMeshes,
        HashSet<string> textures,
        IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(judged);
        ArgumentNullException.ThrowIfNull(subMeshes);
        ArgumentNullException.ThrowIfNull(textures);

        IOpenIgiLogger log = logger ?? NullLogger.Instance;

        if (!scene.Mission.TryReadLightmapArchive(resources, out IresArchive? archive) || archive is null)
        {
            return;
        }

        Dictionary<string, IresEntry> entries = new(StringComparer.OrdinalIgnoreCase);

        foreach (IresEntry entry in archive.Entries)
        {
            string name = entry.Name;
            int slash = name.LastIndexOfAny(['\\', '/', ':']);
            entries[slash >= 0 ? name[(slash + 1)..] : name] = entry;
        }


        float metres = (float)(1.0 / WorldScene.UnitsPerMetre);
        Dictionary<MefModel, MeshGeometry?> built = [];
        Dictionary<string, (float Brightness, float Saturation)> looks = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> tested = new(StringComparer.OrdinalIgnoreCase);
        int found = 0;

        foreach (WorldPlacement placement in scene.Placements)
        {
            string entryName = placement.LightmapEntryName();

            if (entryName.Length == 0 || placement.BentGeometry is not null)
            {
                continue;
            }

            if (!built.TryGetValue(placement.Model, out MeshGeometry? geometry))
            {
                geometry = MefMeshBuilder.Build(placement.Model, metres);
                built[placement.Model] = geometry;
            }

            if (geometry is null)
            {
                continue;
            }

            ObjectLightmap? lightmap = null;
            bool read = false;
            float modelBake = -1f;

            foreach (MeshSubset subset in geometry.Subsets)
            {
                if (subset.IndexCount < 3
                    || !subset.HasMaterial
                    || !placement.Pack.TryGetTextureName(placement.Instance, subset.MaterialSlot, out string texture)
                    || texture.Length == 0)
                {
                    continue;
                }

                string key = LampDetector.Lamps.Key(placement.Instance.Name, subset.MaterialSlot);

                if (!tested.Add(key) || judged(placement.Instance.Name, key))
                {
                    continue;
                }

                if (!looks.TryGetValue(texture, out (float Brightness, float Saturation) look))
                {
                    look = (LampDetector.Brightness(texture, resources), LampDetector.Saturation(texture, resources));
                    looks[texture] = look;
                }

                // The cheap half first: a dark or a coloured page is no lamp whatever was baked on it.
                if (look.Brightness <= MinimumBrightness)
                {
                    continue;
                }

                if (!read)
                {
                    read = true;
                    lightmap = Read(entries, entryName, formats);
                    modelBake = lightmap is null ? -1f : Bake(geometry, -1, lightmap).Mean;
                }

                if (lightmap is null || modelBake < 0f)
                {
                    break;
                }

                (float span, float flat, _) = Bake(geometry, subset.MaterialSlot, lightmap);

                if (span > MaximumSpan || flat < MinimumFlatArea)
                {
                    continue;
                }

                // A coloured page is a lit panel -- a screen, a map, a sign -- which shows and barely lights.
                float radiance = look.Saturation >= LampDetector.MaximumSaturation
                    ? PanelRadiance
                    : LampDetector.LampRadiance;

                subMeshes[key] = LampDetector.Strength(look.Brightness, radiance);
                textures.Add(texture);
                found++;
            }
        }

        if (found > 0)
        {
            log.Information(LogCategory, $"Baked lamps: {found} sub-mesh(es) the level's lightmaps light as sources.");
        }
    }

    private static ObjectLightmap? Read(Dictionary<string, IresEntry> entries, string entryName, AssetFormatProfile formats)
    {
        if (!entries.TryGetValue(entryName, out IresEntry? entry) || entry is null)
        {
            return null;
        }

        try
        {
            return ObjectLightmap.Read(entry.Data, formats);
        }
        catch (Exception ex) when (ex is PackFormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>What one material slot's separate pieces span and how brightly each was baked.</summary>
    /// <param name="geometry">The model's geometry, in metres.</param>
    /// <param name="slot">The material slot, or a negative number for the whole model taken as one piece.</param>
    /// <param name="lightmap">The placement's bake.</param>
    /// <returns>
    /// The widest piece's largest dimension; the area in square metres of the pieces baked flat at the
    /// self-lit value; and the bake of all the pieces together, below nought where the bake does not
    /// cover them.
    /// </returns>
    private static (float Span, float Flat, float Mean) Bake(MeshGeometry geometry, int slot, ObjectLightmap lightmap)
    {
        Vertex3D[] vertices = geometry.Vertices;
        ushort[] indices = geometry.Indices;
        Dictionary<int, int> parent = [];

        int Find(int index)
        {
            while (parent[index] != index)
            {
                parent[index] = parent[parent[index]];
                index = parent[index];
            }

            return index;
        }

        List<(int A, int B, int C, int Page)> triangles = [];

        foreach (MeshSubset subset in geometry.Subsets)
        {
            if (slot >= 0 && subset.MaterialSlot != slot)
            {
                continue;
            }

            int start = Math.Max(subset.IndexOffset, 0);
            int end = Math.Min(subset.IndexOffset + subset.IndexCount, indices.Length);

            for (int at = start; at + 2 < end; at += 3)
            {
                int a = indices[at];
                int b = indices[at + 1];
                int c = indices[at + 2];

                if (a >= vertices.Length || b >= vertices.Length || c >= vertices.Length)
                {
                    continue;
                }

                triangles.Add((a, b, c, subset.LightmapPage));

                if (slot < 0)
                {
                    continue;
                }

                parent.TryAdd(a, a);
                parent.TryAdd(b, b);
                parent.TryAdd(c, c);
                parent[Find(b)] = Find(a);
                parent[Find(c)] = Find(a);
            }
        }

        Dictionary<int, (Vector3f Low, Vector3f High, double Area, float Dimmest, float Brightest)> pieces = [];
        double allSum = 0.0;
        double allArea = 0.0;

        foreach ((int a, int b, int c, int page) in triangles)
        {
            Vector3f pa = vertices[a].Position;
            Vector3f pb = vertices[b].Position;
            Vector3f pc = vertices[c].Position;
            Vector3f ab = pb - pa;
            Vector3f ac = pc - pa;
            Vector3f cross = new(
                (ab.Y * ac.Z) - (ab.Z * ac.Y),
                (ab.Z * ac.X) - (ab.X * ac.Z),
                (ab.X * ac.Y) - (ab.Y * ac.X));
            double area = 0.5 * Math.Sqrt(cross.LengthSquared);
            float dim = -1f;
            float lit = -1f;
            float baked = page >= 0 && page < lightmap.Images.Count
                ? Sample(lightmap.Images[page], vertices[a], vertices[b], vertices[c], out dim, out lit)
                : -1f;

            if (baked >= 0f)
            {
                allSum += baked * area;
                allArea += area;
            }

            if (slot < 0)
            {
                continue;
            }

            int piece = Find(a);
            (Vector3f low, Vector3f high, double covered, float dimmest, float brightest) = pieces.TryGetValue(piece, out var known)
                ? known
                : (pa, pa, 0.0, float.MaxValue, float.MinValue);

            low = Lower(Lower(Lower(low, pa), pb), pc);
            high = Upper(Upper(Upper(high, pa), pb), pc);

            if (baked >= 0f)
            {
                covered += area;
                dimmest = MathF.Min(dimmest, dim);
                brightest = MathF.Max(brightest, lit);
            }
            else
            {
                // A face the bake does not cover makes no claim, and a piece with one is not flat.
                dimmest = -1f;
            }

            pieces[piece] = (low, high, covered, dimmest, brightest);
        }

        float mean = allArea > 0.0 ? (float)(allSum / allArea) : -1f;

        if (slot < 0)
        {
            return (0f, 0f, mean);
        }

        float span = 0f;
        double flat = 0.0;

        foreach ((Vector3f low, Vector3f high, double covered, float dimmest, float brightest) in pieces.Values)
        {
            span = MathF.Max(span, MathF.Max(high.X - low.X, MathF.Max(high.Y - low.Y, high.Z - low.Z)));

            if (dimmest >= SelfLit - SelfLitTolerance && brightest <= SelfLit + SelfLitTolerance)
            {
                flat += covered;
            }
        }

        return (span, (float)flat, mean);
    }

    /// <summary>A triangle's bake: the luminance of its lightmap at its corners and its middle, averaged, with the least and most of them.</summary>
    private static float Sample(
        in ObjectLightmapImage image, in Vertex3D a, in Vertex3D b, in Vertex3D c, out float dimmest, out float brightest)
    {
        dimmest = -1f;
        brightest = -1f;

        ReadOnlySpan<byte> pixels = image.Pixels.Span;
        int width = image.Width;
        int height = image.Height;

        if (width <= 0 || height <= 0 || (long)width * height > pixels.Length / 4)
        {
            return -1f;
        }

        float u = (a.LightmapU + b.LightmapU + c.LightmapU) / 3f;
        float v = (a.LightmapV + b.LightmapV + c.LightmapV) / 3f;

        float at0 = At(pixels, width, height, a.LightmapU, a.LightmapV);
        float at1 = At(pixels, width, height, b.LightmapU, b.LightmapV);
        float at2 = At(pixels, width, height, c.LightmapU, c.LightmapV);
        float at3 = At(pixels, width, height, u, v);

        dimmest = MathF.Min(MathF.Min(at0, at1), MathF.Min(at2, at3));
        brightest = MathF.Max(MathF.Max(at0, at1), MathF.Max(at2, at3));

        return (at0 + at1 + at2 + at3) * 0.25f;
    }

    private static float At(ReadOnlySpan<byte> pixels, int width, int height, float u, float v)
    {
        int x = Math.Clamp((int)(u * width), 0, width - 1);
        int y = Math.Clamp((int)(v * height), 0, height - 1);
        int at = ((y * width) + x) * 4;

        // The pages are BGRA; Rec. 709 weights, as the survey measures them.
        return ((0.0722f * pixels[at]) + (0.7152f * pixels[at + 1]) + (0.2126f * pixels[at + 2])) / 255f;
    }

    private static Vector3f Lower(Vector3f a, Vector3f b) =>
        new(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Min(a.Z, b.Z));

    private static Vector3f Upper(Vector3f a, Vector3f b) =>
        new(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y), MathF.Max(a.Z, b.Z));
}
