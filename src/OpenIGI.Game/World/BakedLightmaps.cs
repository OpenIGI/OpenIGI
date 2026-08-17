using OpenIGI.Core.Graphics;
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
    private const byte FormatVersion = 1;

    private readonly Dictionary<string, Dictionary<int, Page>> _pages;

    private BakedLightmaps(Dictionary<string, Dictionary<int, Page>> pages) => _pages = pages;

    /// <summary>One page's dimensions and its row-major, width-fastest pixels.</summary>
    public readonly record struct Page(int Width, int Height, Bgra32[] Pixels);

    /// <summary>Gets the pages, by lightmap entry name then page index.</summary>
    public IReadOnlyDictionary<string, Dictionary<int, Page>> Pages => _pages;

    /// <summary>Gets how many lightmapped objects the bake covers.</summary>
    public int ObjectCount => _pages.Count;

    /// <summary>Composites baked targets into ready-to-upload pages.</summary>
    /// <param name="scene">The scene the targets were baked against, for each placement's lightmap entry.</param>
    /// <param name="targets">The baked pages from <see cref="LightmapBaker"/>, their radiance filled.</param>
    /// <returns>The composited pages.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static BakedLightmaps Compose(WorldScene scene, IReadOnlyList<LightmapBaker.LightmapTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(targets);

        Dictionary<string, Dictionary<int, Page>> pages = new(StringComparer.OrdinalIgnoreCase);

        foreach (LightmapBaker.LightmapTarget target in targets)
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

            if (!entry.TryGetValue(target.Page, out Page page) || page.Pixels.Length != target.Width * target.Height)
            {
                page = new Page(target.Width, target.Height, new Bgra32[target.Width * target.Height]);
                entry[target.Page] = page;
            }

            Bgra32[] pixels = page.Pixels;
            int count = Math.Min(pixels.Length, target.Radiance.Length);
            for (int i = 0; i < count; i++)
            {
                if (!target.Texels[i].IsCovered)
                {
                    continue;
                }

                Vector3f rad = target.Radiance[i];
                pixels[i] = new Bgra32(
                    r: LinearToSrgb(rad.X), g: LinearToSrgb(rad.Y), b: LinearToSrgb(rad.Z), a: 255);
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
                foreach (Bgra32 pixel in page.Pixels)
                {
                    writer.Write(pixel.B);
                    writer.Write(pixel.G);
                    writer.Write(pixel.R);
                    writer.Write(pixel.A);
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

                Bgra32[] pixels = new Bgra32[width * height];
                for (int i = 0; i < pixels.Length; i++)
                {
                    byte b = reader.ReadByte();
                    byte g = reader.ReadByte();
                    byte r = reader.ReadByte();
                    byte a = reader.ReadByte();
                    pixels[i] = new Bgra32(r: r, g: g, b: b, a: a);
                }

                entry[index] = new Page(width, height, pixels);
            }

            pages[entryName] = entry;
        }

        return new BakedLightmaps(pages);
    }

    // The pages are sampled and sRGB-decoded by the G-buffer, so the linear radiance the bake produces is
    // encoded to sRGB here -- the same encoding the shipped .olm pixels carry, so the swap is transparent.
    private static byte LinearToSrgb(float linear)
    {
        float c = Math.Clamp(linear, 0f, 1f);
        float s = c <= 0.0031308f ? c * 12.92f : (1.055f * MathF.Pow(c, 1f / 2.4f)) - 0.055f;
        return (byte)Math.Clamp((int)MathF.Round(s * 255f), 0, 255);
    }
}
