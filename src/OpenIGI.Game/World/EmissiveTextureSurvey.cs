using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

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
/// The output is a starting list for <c>materials.json</c>, not a verdict: it says which textures are
/// worth looking at, and someone still has to look.
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
    /// <param name="logger">Where to report progress and anything unreadable.</param>
    /// <returns>The findings, most light-like first. Textures with no lightmap at all are omitted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> or <paramref name="resources"/> is null.</exception>
    public static IReadOnlyList<Finding> Survey(
        WorldScene scene,
        ResourceRegistry resources,
        IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(resources);

        IIgiLogger log = logger ?? NullLogger.Instance;

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
                lightmap = TryRead(entries, entryName, log);
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

                float luminance = MeanLuminance(lightmap.Images[subset.LightmapPage]);

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
        IIgiLogger log)
    {
        if (!entries.TryGetValue(entryName, out IresEntry? entry) || entry is null)
        {
            return null;
        }

        try
        {
            return ObjectLightmap.Read(entry.Data);
        }
        catch (Exception ex) when (ex is PackFormatException or ArgumentException)
        {
            log.Warning(LogCategory, $"Lightmap '{entryName}' did not read: {ex.Message}");
            return null;
        }
    }

    /// <summary>Averages a lightmap page's luminance.</summary>
    /// <remarks>
    /// <para>
    /// The whole page rather than the sub-mesh's own corner of it, and that is a deliberate first cut:
    /// a page is an atlas of <em>one model's</em> surfaces, so it is already local to the object, and
    /// walking each sub-mesh's lightmap UV bounds would sharpen the answer at the cost of needing the
    /// vertex data as well. The consistency test below carries most of the discrimination anyway.
    /// </para>
    /// <para>
    /// Fully black texels are skipped. An atlas is padded, and counting its empty margin would drag
    /// every page towards zero in proportion to how wastefully it was packed.
    /// </para>
    /// </remarks>
    private static float MeanLuminance(in ObjectLightmapImage image)
    {
        ReadOnlySpan<byte> pixels = image.Pixels.Span;
        int count = Math.Min(image.Width * image.Height, pixels.Length / 4);

        if (count == 0)
        {
            return -1f;
        }

        double sum = 0.0;
        int lit = 0;

        for (int i = 0; i < count; i++)
        {
            int at = i * 4;

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

        return lit > 0 ? (float)(sum / lit) : -1f;
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
