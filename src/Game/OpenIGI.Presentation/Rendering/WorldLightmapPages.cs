using System.Runtime.InteropServices;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>The objects' baked lightmap pages: read from the level's archive, overridden by a path-traced bake, uploaded once.</summary>
/// <remarks>
/// One <c>.olm</c> entry holds every page of one model at one level of detail, so pages are uploaded a
/// whole entry at a time and cached by entry name.
/// </remarks>
internal sealed class WorldLightmapPages
{
    private const string LogCategory = "World";

    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry _resources;
    private readonly AssetFormatProfile _formats;
    private readonly IOpenIgiLogger _logger;
    private readonly Dictionary<string, TextureHandle[]> _lightmaps = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, IresEntry>? _lightmapArchive;

    // Path-traced lightmap pages that override the retail .olm ones, keyed by entry name then page index.
    // Empty unless a bake has been applied; GetLightmapPages substitutes these pixels for the shipped ones.
    private readonly Dictionary<string, Dictionary<int, Bgra32[]>> _bakedPages = new(StringComparer.OrdinalIgnoreCase);

    // When true, a lightmapped page the bake did not cover reads black instead of falling back to the
    // shipped .olm -- so the frame shows only our own lighting, with its gaps visible rather than papered
    // over by the artists' light.
    private bool _discardRetailLightmaps;

    // The headroom the baked pages are divided by before their sRGB encode when the bake is exclusive, so a
    // sunlit or sky-lit surface's radiance keeps its value above one in an eight-bit page instead of clipping;
    // the shader multiplies back up. One when the shipped LDR pages are in play (they have no such range).
    private const float HdrHeadroom = 4f;
    private float _lightmapScale = 1f;

    /// <summary>Initializes the pages over the device, the registry and the title's format policy.</summary>
    /// <param name="device">The device to upload to.</param>
    /// <param name="resources">Where the lightmap archive is read from.</param>
    /// <param name="formats">The title's format policy; a baked lightmap's descriptor stride differs.</param>
    /// <param name="logger">Where to report upload problems.</param>
    internal WorldLightmapPages(
        IGraphicsDevice device,
        ResourceRegistry resources,
        AssetFormatProfile formats,
        IOpenIgiLogger logger)
    {
        _device = device;
        _resources = resources;
        _formats = formats;
        _logger = logger;
    }

    /// <summary>Gets the headroom the pages are encoded with: one unless an exclusive HDR bake is applied.</summary>
    internal float Scale => _lightmapScale;

    /// <summary>Gets how many object entries the applied bake covers.</summary>
    internal int BakedEntryCount => _bakedPages.Count;

    /// <summary>Takes a bake's pixels in place of the shipped pages and drops every page already uploaded.</summary>
    /// <param name="baked">The composited pages, or <see langword="null"/> to restore the shipped lightmaps.</param>
    /// <param name="discardRetail">Whether a lightmapped page with no bake reads black.</param>
    internal void ApplyBake(BakedLightmaps? baked, bool discardRetail)
    {
        _bakedPages.Clear();

        // Independent of whether a bake has landed yet: under the exclusive mode a surface with no bake
        // reads black even while the worker is still tracing, so the shipped lightmaps never flash up first.
        _discardRetailLightmaps = discardRetail;

        // The exclusive path carries our own high-dynamic-range lighting, so its pages are encoded with the
        // headroom and the shader scales them back up; the fallback path mixes with the shipped LDR pages,
        // so it clamps to one and scales by one.
        _lightmapScale = discardRetail ? HdrHeadroom : 1f;

        if (baked is not null)
        {
            foreach ((string entryName, Dictionary<int, BakedLightmaps.Page> pages) in baked.Pages)
            {
                Dictionary<int, Bgra32[]> entry = new(pages.Count);
                foreach ((int index, BakedLightmaps.Page page) in pages)
                {
                    Vector3f[] radiance = page.Radiance;
                    Bgra32[] pixels = new Bgra32[radiance.Length];
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        Vector3f r = radiance[i];
                        pixels[i] = new Bgra32(
                            r: LinearToSrgb(r.X / _lightmapScale),
                            g: LinearToSrgb(r.Y / _lightmapScale),
                            b: LinearToSrgb(r.Z / _lightmapScale),
                            a: 255);
                    }

                    entry[index] = pixels;
                }

                _bakedPages[entryName] = entry;
            }
        }

        foreach (TextureHandle[] uploaded in _lightmaps.Values)
        {
            foreach (TextureHandle page in uploaded)
            {
                if (page.IsValid)
                {
                    _device.DestroyTexture(page);
                }
            }
        }

        _lightmaps.Clear();
    }

    /// <summary>Releases every uploaded page, as disposing the renderer does.</summary>
    internal void Release()
    {
        foreach (TextureHandle[] pages in _lightmaps.Values)
        {
            foreach (TextureHandle page in pages)
            {
                _device.DestroyTexture(page);
            }
        }

        _lightmaps.Clear();
        _bakedPages.Clear();
    }

    // Encodes one linear channel to sRGB, clamped to the displayable range. The page is decoded back to
    // linear in the G-buffer, so this is only the storage encoding; the HDR headroom is carried separately.
    private static byte LinearToSrgb(float linear)
    {
        float c = Math.Clamp(linear, 0f, 1f);
        float s = c <= 0.0031308f ? c * 12.92f : (1.055f * MathF.Pow(c, 1f / 2.4f)) - 0.055f;
        return (byte)Math.Clamp((int)MathF.Round(s * 255f), 0, 255);
    }

    /// <summary>Gets the uploaded pages of a placement's baked lightmap, empty when it has none.</summary>
    /// <remarks>
    /// One <c>.olm</c> entry holds every page of one model at one level of detail, so this uploads them
    /// together and caches by entry name. Level 1's archive is 1308 entries, but only the ones actually
    /// drawn are ever touched.
    /// </remarks>
    internal TextureHandle[] GetLightmapPages(WorldScene scene, WorldPlacement placement)
    {
        if (!placement.HasLightmap)
        {
            return [];
        }

        string entryName = placement.LightmapEntryName();
        if (_lightmaps.TryGetValue(entryName, out TextureHandle[]? cached))
        {
            return cached;
        }

        TextureHandle[] pages = [];
        if (GetLightmapArchive(scene).TryGetValue(entryName, out IresEntry? entry) && entry is not null)
        {
            try
            {
                ObjectLightmap lightmap = ObjectLightmap.Read(entry.Data, _formats);
                pages = new TextureHandle[lightmap.Images.Count];
                for (int i = 0; i < pages.Length; i++)
                {
                    ObjectLightmapImage image = lightmap.Images[i];

                    // A path-traced bake, if one was applied, replaces the shipped page's pixels here --
                    // same dimensions, same upload, so nothing downstream knows the difference. Falls back
                    // to the retail page when this entry or page was not baked, or the sizes disagree --
                    // unless the bake is exclusive, in which case an uncovered page reads black so only our
                    // own lighting shows.
                    ReadOnlySpan<Bgra32> pixels = MemoryMarshal.Cast<byte, Bgra32>(image.Pixels.Span);
                    if (_bakedPages.TryGetValue(entryName, out Dictionary<int, Bgra32[]>? bakedEntry)
                        && bakedEntry.TryGetValue(i, out Bgra32[]? bakedPage)
                        && bakedPage.Length == image.Width * image.Height)
                    {
                        pixels = bakedPage;
                    }
                    else if (_discardRetailLightmaps)
                    {
                        pixels = new Bgra32[image.Width * image.Height];
                    }

                    // A page is an atlas of this model's own surfaces, so it clamps rather than repeats:
                    // wrapping would bleed one surface's light onto another at the edges.
                    pages[i] = _device.CreateTexture(
                        new TextureDescription(
                            image.Width,
                            image.Height,
                            1,
                            TextureFilter.Linear,
                            TextureAddressMode.Clamp),
                        pixels);
                }
            }
            catch (Exception ex) when (ex is PackFormatException or ArgumentException or GraphicsDeviceException)
            {
                _logger.Warning(LogCategory, $"Lightmap '{entryName}' did not load: {ex.Message}");
                pages = [];
            }
        }

        _lightmaps[entryName] = pages;
        return pages;
    }

    private Dictionary<string, IresEntry> GetLightmapArchive(WorldScene scene)
    {
        if (_lightmapArchive is not null)
        {
            return _lightmapArchive;
        }

        _lightmapArchive = new Dictionary<string, IresEntry>(StringComparer.OrdinalIgnoreCase);
        if (scene.Mission.TryReadLightmapArchive(_resources, out IresArchive? archive) && archive is not null)
        {
            foreach (IresEntry entry in archive.Entries)
            {
                _lightmapArchive[BareEntryName(entry.Name)] = entry;
            }

            _logger.Information(LogCategory, $"Object lightmaps: {_lightmapArchive.Count} entries.");
        }
        else
        {
            _logger.Warning(LogCategory, "The level has no object-lightmap archive; the world draws unshaded.");
        }

        return _lightmapArchive;
    }

    private static string BareEntryName(string name)
    {
        int slash = name.LastIndexOfAny(['/', '\\']);
        return slash >= 0 ? name[(slash + 1)..] : name;
    }
}
