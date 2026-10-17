using OpenIGI.Formats;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.World;
using OpenIGI.Rendering;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// Finds the textures a level treats as light sources, by reading what the artists baked around them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this has to be inferred at all.</b> I.G.I. declares no lights. A survey of level 1's task
/// tree turns up a <c>Dirlight</c>, a <c>GlobalLight</c> and 290 lightmap records, and nothing else —
/// no omni, no point, no spot. Every lamp on a wall is geometry with a texture, and the light it
/// appears to cast was painted into a lightmap by hand. So a renderer that stops using the bakes, as
/// the dynamic lighting model does, has no way to know a lamp is a lamp.
/// </para>
/// <para>
/// <b>What gives them away.</b> A lightmap records how much light reached a surface. For nearly every
/// surface in a level that is some fraction of the sun or the sky. For a lamp's own fixture it is not:
/// the artist painted it bright, because it is the thing doing the lighting. So a texture whose
/// lightmap is consistently near-white, across every place the level uses it, is a texture the artists
/// treated as a source — and that is a far more reliable signal than the diffuse texture's own
/// brightness, which merely says the thing is pale.
/// </para>
/// <para>
/// <b>Consistency is the discriminating part.</b> One bright sample is a surface in sunlight. The same
/// texture bright in twenty different places, indoors and out, is a surface that carries its own light.
/// The survey therefore reports the share of a texture's uses that are bright as well as the mean, and
/// ranking on the share is what separates a lamp from a wall that happens to face south.
/// </para>
/// <para>
/// <b>What it is NOT.</b> This ranks textures by how they were LIT, and being brightly lit is not the
/// same as being a light — level 1's highest-scoring texture of all, baked pure white across every one
/// of its thirty uses, skins the office cabinets. It is also blind in the other direction: four of that
/// level's real ceiling fittings score exactly nought, because those rooms were lit without painting
/// the fitting bright. So this does not decide which textures are lamps and is not consulted on it;
/// <see cref="LampDetector"/> reads the geometry instead, and records why. What this is good for is a
/// starting list for <c>materials.json</c> and for any level export's emissive materials — it says
/// which textures are worth looking at, and someone still has to look.
/// </para>
/// </remarks>
public static class EmissiveTextureSurvey
{
    private const string LogCategory = "Emissive";

    /// <summary>The lightmap luminance, 0 to 1, above which a surface is called self-lit.</summary>
    /// <remarks>
    /// Well above what the sun alone puts on a surface in these levels, so that a sunlit wall does not
    /// qualify. Lower it and the survey fills with south-facing concrete.
    /// </remarks>
    public const float BrightThreshold = 0.82f;

    /// <summary>One texture's evidence.</summary>
    /// <param name="Texture">The texture's name, as the pack records it.</param>
    /// <param name="Uses">How many sub-meshes in the level draw with it.</param>
    /// <param name="BrightUses">How many of those have a lightmap brighter than <see cref="BrightThreshold"/>.</param>
    /// <param name="MeanLuminance">The mean lightmap luminance over every use.</param>
    /// <param name="PeakLuminance">The brightest single use.</param>
    public readonly record struct Finding(
        string Texture,
        int Uses,
        int BrightUses,
        float MeanLuminance,
        float PeakLuminance)
    {
        /// <summary>Gets the share of this texture's uses that are lit like a source.</summary>
        public float BrightShare => Uses > 0 ? (float)BrightUses / Uses : 0f;

        /// <summary>Gets how strongly this texture looks like a light.</summary>
        /// <remarks>
        /// The share weighted by the mean, so that a texture bright <em>everywhere</em> outranks one
        /// that is merely bright <em>somewhere</em> — and a texture used once cannot top the list on a
        /// single lucky sample.
        /// </remarks>
        public float Score => BrightShare * MeanLuminance * (1f - (1f / (Uses + 1f)));
    }

    /// <summary>Surveys a level and ranks its textures by how much they look like light sources.</summary>
    /// <param name="scene">The loaded level.</param>
    /// <param name="resources">Where the level's lightmap archive is read from.</param>
    /// <param name="formats">The title's format policy; a baked lightmap's descriptor stride differs.</param>
    /// <param name="logger">Where to report progress and anything unreadable.</param>
    /// <returns>The findings, most light-like first. Textures with no lightmap at all are omitted.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// The profile is asked for rather than assumed for the same reason the reader asks for it: reading
    /// a lightmap at the wrong stride does not fail everywhere, and a survey that quietly measured the
    /// wrong pixels would rank the wrong textures as lights.
    /// </remarks>
    public static IReadOnlyList<Finding> Survey(
        WorldScene scene,
        ResourceRegistry resources,
        AssetFormatProfile formats,
        IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(formats);

        IOpenIgiLogger log = logger ?? NullLogger.Instance;

        if (!scene.Mission.TryReadLightmapArchive(resources, out IresArchive? archive) || archive is null)
        {
            log.Warning(LogCategory, "This level ships no lightmap archive; nothing to survey.");
            return [];
        }

        Dictionary<string, IresEntry> entries = new(StringComparer.OrdinalIgnoreCase);

        foreach (IresEntry entry in archive.Entries)
        {
            entries[BareName(entry.Name)] = entry;
        }

        Dictionary<string, Accumulator> byTexture = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ObjectLightmap> cache = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, MeshGeometry> meshes = new(StringComparer.Ordinal);

        foreach (WorldPlacement placement in scene.Placements)
        {
            string entryName = placement.LightmapEntryName();

            if (entryName.Length == 0)
            {
                continue;
            }

            // A placement's geometry is built rather than stored, and a level draws the same model
            // many times over -- so it is built once per model here and reused.
            if (!meshes.TryGetValue(placement.Instance.Name, out MeshGeometry? geometry))
            {
                geometry = placement.BentGeometry
                    ?? MefMeshBuilder.Build(placement.Model, (float)(1.0 / WorldScene.UnitsPerMetre));
                meshes[placement.Instance.Name] = geometry!;
            }

            if (geometry is null)
            {
                continue;
            }

            if (!cache.TryGetValue(entryName, out ObjectLightmap? lightmap))
            {
                lightmap = TryRead(entries, entryName, formats, log);
                cache[entryName] = lightmap!;
            }

            if (lightmap is null)
            {
                continue;
            }

            foreach (MeshSubset subset in geometry.Subsets)
            {
                if (subset.IndexCount == 0
                    || !subset.HasMaterial
                    || subset.LightmapPage < 0
                    || subset.LightmapPage >= lightmap.Images.Count
                    || !placement.Pack.TryGetTextureName(placement.Instance, subset.MaterialSlot, out string texture))
                {
                    continue;
                }

                float luminance = MeanLuminance(lightmap.Images[subset.LightmapPage], geometry, subset);

                if (luminance < 0f)
                {
                    continue;
                }

                ref Accumulator slot = ref System.Runtime.InteropServices.CollectionsMarshal
                    .GetValueRefOrAddDefault(byTexture, texture, out _);

                slot.Uses++;
                slot.Sum += luminance;
                slot.Peak = MathF.Max(slot.Peak, luminance);

                if (luminance >= BrightThreshold)
                {
                    slot.Bright++;
                }
            }
        }

        List<Finding> findings = [];

        foreach ((string texture, Accumulator slot) in byTexture)
        {
            findings.Add(new Finding(
                texture,
                slot.Uses,
                slot.Bright,
                slot.Uses > 0 ? slot.Sum / slot.Uses : 0f,
                slot.Peak));
        }

        findings.Sort((a, b) => b.Score.CompareTo(a.Score));

        log.Information(
            LogCategory,
            $"Surveyed {byTexture.Count} textured surface(s); "
                + $"{findings.Count(f => f.BrightShare > 0.5f)} are lit like sources.");

        return findings;
    }

    private static ObjectLightmap? TryRead(
        Dictionary<string, IresEntry> entries,
        string entryName,
        AssetFormatProfile formats,
        IOpenIgiLogger log)
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
            log.Warning(LogCategory, $"Lightmap '{entryName}' did not read: {ex.Message}");
            return null;
        }
    }

    /// <summary>Averages the luminance of the part of a lightmap page one sub-mesh actually covers.</summary>
    /// <remarks>
    /// <para>
    /// <b>The sub-mesh's own corner of the page, not the whole page.</b> The page is an atlas of one
    /// MODEL's surfaces, and that is not local enough: a ceiling model carries its slab, its beams and
    /// the one small tube bolted to it in a single <c>.olm</c>, so averaging the page divides the tube's
    /// near-white by the whole dark ceiling around it and the brightest thing in the room scores like
    /// concrete. That failure is why a page-wide survey needs a hand-written list of lamp textures to
    /// catch what it missed. Measuring only the texels this sub-mesh's own lightmap UVs reach
    /// takes the dilution away, and the list with it.
    /// </para>
    /// <para>
    /// The bounds are a rectangle rather than the exact footprint. A lightmap atlas is packed so that a
    /// sub-mesh's chart occupies a contiguous block, and the padding around one is either black — which
    /// is skipped below — or a bled copy of the chart's own edge, which is the same light. So the
    /// rectangle costs nothing in practice and saves rasterising every triangle into the page.
    /// </para>
    /// <para>
    /// Fully black texels are skipped. An atlas is padded, and counting its empty margin would drag
    /// every page towards zero in proportion to how wastefully it was packed.
    /// </para>
    /// </remarks>
    private static float MeanLuminance(in ObjectLightmapImage image, MeshGeometry geometry, in MeshSubset subset)
    {
        ReadOnlySpan<byte> pixels = image.Pixels.Span;
        int width = image.Width;
        int height = image.Height;

        if (width <= 0 || height <= 0 || (long)width * height > pixels.Length / 4)
        {
            return -1f;
        }

        if (!Bounds(geometry, subset, width, height, out int minX, out int minY, out int maxX, out int maxY))
        {
            return -1f;
        }

        double sum = 0.0;
        int lit = 0;

        for (int y = minY; y <= maxY; y++)
        {
            int row = y * width;

            for (int x = minX; x <= maxX; x++)
            {
                int at = (row + x) * 4;

                // The pages are BGRA.
                float luminance =
                    ((0.0722f * pixels[at]) + (0.7152f * pixels[at + 1]) + (0.2126f * pixels[at + 2])) / 255f;

                if (luminance <= 0.004f)
                {
                    continue;
                }

                sum += luminance;
                lit++;
            }
        }

        return lit > 0 ? (float)(sum / lit) : -1f;
    }

    /// <summary>The texel rectangle a sub-mesh's lightmap UVs cover, clamped into the page.</summary>
    /// <remarks>
    /// UVs come out of a pack file and are not guaranteed to sit inside the unit square -- a chart can
    /// be authored slightly over its edge, and a malformed record can be anywhere at all. Clamping is
    /// what keeps a bad one costing the accuracy of one measurement rather than reading past the page.
    /// </remarks>
    private static bool Bounds(
        MeshGeometry geometry,
        in MeshSubset subset,
        int width,
        int height,
        out int minX,
        out int minY,
        out int maxX,
        out int maxY)
    {
        minX = minY = maxX = maxY = 0;

        Vertex3D[] vertices = geometry.Vertices;
        ushort[] indices = geometry.Indices;

        int start = Math.Max(subset.IndexOffset, 0);
        int end = Math.Min(subset.IndexOffset + subset.IndexCount, indices.Length);

        float lowU = float.MaxValue;
        float lowV = float.MaxValue;
        float highU = float.MinValue;
        float highV = float.MinValue;

        for (int at = start; at < end; at++)
        {
            int index = indices[at];

            if (index >= vertices.Length)
            {
                continue;
            }

            Vertex3D vertex = vertices[index];

            lowU = MathF.Min(lowU, vertex.LightmapU);
            highU = MathF.Max(highU, vertex.LightmapU);
            lowV = MathF.Min(lowV, vertex.LightmapV);
            highV = MathF.Max(highV, vertex.LightmapV);
        }

        if (lowU > highU)
        {
            return false;
        }

        minX = Math.Clamp((int)MathF.Floor(lowU * width), 0, width - 1);
        maxX = Math.Clamp((int)MathF.Ceiling(highU * width), minX, width - 1);
        minY = Math.Clamp((int)MathF.Floor(lowV * height), 0, height - 1);
        maxY = Math.Clamp((int)MathF.Ceiling(highV * height), minY, height - 1);

        return true;
    }

    private static string BareName(string name)
    {
        int slash = name.LastIndexOfAny(['/', '\\']);
        return slash >= 0 ? name[(slash + 1)..] : name;
    }

    private struct Accumulator
    {
        public int Uses;
        public int Bright;
        public float Sum;
        public float Peak;
    }
}
