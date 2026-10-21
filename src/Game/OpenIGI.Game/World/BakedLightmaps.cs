using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// A path-traced bake of a level's object lightmaps, composited into ready-to-upload pages and keyed the
/// way the renderer samples them: by lightmap entry name, then page index.
/// </summary>
/// <remarks>
/// <para>
/// This is the hand-off between the bake and the frame. The expensive tracing (<see cref="LightmapBaker"/>)
/// and the compositing here are pure CPU and run on a worker; the renderer takes the finished pages and
/// swaps their pixels in where the shipped <c>.olm</c> ones would be sampled. Nothing here touches the
/// graphics API, which is why the whole thing can be built off the render thread and cached to disk.
/// </para>
/// <para>
/// Several of a model's draw records can name one page, each covering a different island of it, so a page
/// accumulates across every target that shares it. Uncovered texels stay black; they are atlas space no
/// surface maps to, and the page clamps rather than repeats, so the black does not reach a sampled texel.
/// </para>
/// </remarks>
public sealed class BakedLightmaps
{
    private const uint Magic = 0x4D4C4749; // "IGLM"

    // v2: pages store linear float radiance (HDR) rather than sRGB bytes, so the renderer can encode LDR or
    // HDR at upload.
    private const byte FormatVersion = 2;

    private readonly Dictionary<string, Dictionary<int, Page>> _pages;

    private BakedLightmaps(Dictionary<string, Dictionary<int, Page>> pages) => _pages = pages;

    /// <summary>One page's dimensions and its row-major, width-fastest LINEAR radiance, an uncovered texel zero.</summary>
    /// <remarks>
    /// The radiance is kept linear and unclamped -- the high dynamic range the bake produced -- so the
    /// renderer can encode it either as an LDR page (clamped to one) or as an HDR page (divided by a
    /// headroom then sRGB-encoded) depending on the mode, without the bake having to know which.
    /// </remarks>
    public readonly record struct Page(int Width, int Height, Vector3f[] Radiance);

    /// <summary>Gets the pages, by lightmap entry name then page index.</summary>
    public IReadOnlyDictionary<string, Dictionary<int, Page>> Pages => _pages;

    /// <summary>Gets how many lightmapped objects the bake covers.</summary>
    public int ObjectCount => _pages.Count;

    /// <summary>Composites baked targets into ready-to-upload pages.</summary>
    /// <param name="scene">The scene the targets were baked against, for each placement's lightmap entry.</param>
    /// <param name="targets">The baked pages from <see cref="LightmapBaker"/>, their radiance filled.</param>
    /// <returns>The composited pages.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static BakedLightmaps Compose(WorldScene scene, IReadOnlyList<LightmapTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(targets);

        Dictionary<string, Dictionary<int, Page>> pages = new(StringComparer.OrdinalIgnoreCase);

        foreach (LightmapTarget target in targets)
        {
            if (target.PlacementIndex < 0 || target.PlacementIndex >= scene.Placements.Count)
            {
                continue;
            }

            WorldPlacement placement = scene.Placements[target.PlacementIndex];
            if (!placement.HasLightmap)
            {
                continue;
            }

            string entryName = placement.LightmapEntryName();
            if (!pages.TryGetValue(entryName, out Dictionary<int, Page>? entry))
            {
                entry = [];
                pages[entryName] = entry;
            }

            if (!entry.TryGetValue(target.Page, out Page page) || page.Radiance.Length != target.Width * target.Height)
            {
                page = new Page(target.Width, target.Height, new Vector3f[target.Width * target.Height]);
                entry[target.Page] = page;
            }

            Vector3f[] radiance = page.Radiance;
            int count = Math.Min(radiance.Length, target.Radiance.Length);
            for (int i = 0; i < count; i++)
            {
                if (target.Texels[i].IsCovered)
                {
                    radiance[i] = target.Radiance[i];
                }
            }
        }

        return new BakedLightmaps(pages);
    }

    /// <summary>Writes the bake to a stream.</summary>
    /// <param name="writer">The writer to write to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
    public void Write(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write(_pages.Count);

        foreach ((string entryName, Dictionary<int, Page> entry) in _pages)
        {
            writer.Write(entryName);
            writer.Write(entry.Count);

            foreach ((int index, Page page) in entry)
            {
                writer.Write(index);
                writer.Write(page.Width);
                writer.Write(page.Height);
                foreach (Vector3f value in page.Radiance)
                {
                    writer.Write(value.X);
                    writer.Write(value.Y);
                    writer.Write(value.Z);
                }
            }
        }
    }

    /// <summary>Reads a bake from a stream written by <see cref="Write"/>.</summary>
    /// <param name="reader">The reader to read from.</param>
    /// <returns>The bake, or <see langword="null"/> when the header does not match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    public static BakedLightmaps? Read(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (reader.ReadUInt32() != Magic || reader.ReadByte() != FormatVersion)
        {
            return null;
        }

        int entryCount = reader.ReadInt32();
        Dictionary<string, Dictionary<int, Page>> pages = new(StringComparer.OrdinalIgnoreCase);

        for (int e = 0; e < entryCount; e++)
        {
            string entryName = reader.ReadString();
            int pageCount = reader.ReadInt32();
            Dictionary<int, Page> entry = [];

            for (int p = 0; p < pageCount; p++)
            {
                int index = reader.ReadInt32();
                int width = reader.ReadInt32();
                int height = reader.ReadInt32();
                if (width <= 0 || height <= 0)
                {
                    return null;
                }

                Vector3f[] radiance = new Vector3f[width * height];
                for (int i = 0; i < radiance.Length; i++)
                {
                    float x = reader.ReadSingle();
                    float y = reader.ReadSingle();
                    float z = reader.ReadSingle();
                    radiance[i] = new Vector3f(x, y, z);
                }

                entry[index] = new Page(width, height, radiance);
            }

            pages[entryName] = entry;
        }

        return new BakedLightmaps(pages);
    }
}
