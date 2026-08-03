using System.Runtime.InteropServices;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>Draws a loaded mission's static objects, uploading each model and texture the first time it is needed.</summary>
/// <remarks>
/// <para>
/// One draw call per sub-mesh, because a sub-mesh names its own texture through the <em>instance</em> —
/// the same model placed twice can wear different textures, so the material cannot be baked into the
/// mesh. Meshes and textures are cached by name, since level 1's 501 placements share about 200 models.
/// </para>
/// <para>
/// Terrain is absent: its format still has open questions, so the world is the placed objects over
/// nothing. That is why the horizon is empty rather than the sky being wrong.
/// </para>
/// </remarks>
public sealed class WorldRenderer : IDisposable
{
    private const string LogCategory = "World";

    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry _resources;
    private readonly IIgiLogger _logger;
    private MaterialLibrary _materials = MaterialLibrary.Empty;
    private readonly Dictionary<string, MeshGeometry?> _geometry = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MeshHandle> _meshes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextureHandle> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missingTextures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextureHandle[]> _lightmaps = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, IresEntry>? _lightmapArchive;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="WorldRenderer"/> class.</summary>
    /// <param name="device">The device to upload to and draw with.</param>
    /// <param name="resources">Where texture bytes are read from.</param>
    /// <param name="logger">Where to report upload problems.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public WorldRenderer(IGraphicsDevice device, ResourceRegistry resources, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(resources);

        _device = device;
        _resources = resources;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets how many sub-meshes the last <see cref="Draw"/> submitted.</summary>
    public int LastDrawCount { get; private set; }

    /// <summary>Gets how many triangles the last <see cref="Draw"/> submitted.</summary>
    public int LastTriangleCount { get; private set; }

    /// <summary>Gets how many of the last <see cref="Draw"/>'s sub-meshes sampled a baked lightmap.</summary>
    /// <remarks>
    /// Worth reporting rather than assuming: a sub-mesh whose bake is missing still draws, just unshaded,
    /// so a broken binding looks like a slightly flat picture rather than like an error.
    /// </remarks>
    public int LastLightmappedCount { get; private set; }

    /// <summary>Draws a scene from a camera.</summary>
    /// <param name="scene">The scene to draw.</param>
    /// <param name="camera">The camera to draw from.</param>
    /// <param name="projection">The view-to-clip transform.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public void Draw(WorldScene scene, in Camera3D camera, in Matrix4f projection)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _device.SetProjection(projection);
        _device.SetDepthState(DepthState.TestAndWrite);

        // Both facings are drawn. The original culls with its own winding rule inside a strip, and until
        // a level renders correctly there is no way to tell a culled face from a missing one — so this
        // starts permissive and tightens once the picture is right.
        _device.SetCullMode(CullMode.None);

        int draws = 0;
        int triangles = 0;
        int lit = 0;

        foreach (WorldPlacement placement in scene.Placements)
        {
            // Bent geometry is unique to its placement — a spline segment shaped to its own span — so
            // it is cached under the placement's task name rather than the shared model name.
            MeshGeometry? geometry = placement.BentGeometry ?? GetGeometry(placement);
            if (geometry is null)
            {
                continue;
            }

            MeshHandle mesh = GetMesh(
                placement.BentGeometry is null ? placement.Instance.Name : placement.TaskName,
                geometry);
            if (!mesh.IsValid)
            {
                continue;
            }

            Matrix4f modelView = camera.CreateModelView(placement.Position, placement.Orientation);
            TextureHandle[] pages = GetLightmapPages(scene, placement);

            foreach (MeshSubset subset in geometry.Subsets)
            {
                if (subset.IndexCount == 0)
                {
                    continue;
                }

                TextureHandle diffuse = TextureHandle.None;
                SurfaceMaterial material = _materials.Default;

                if (subset.HasMaterial
                    && placement.Pack.TryGetTextureName(placement.Instance, subset.MaterialSlot, out string texture))
                {
                    diffuse = GetTexture(texture);

                    // What this surface is made of. Only the sub-meshes that name a texture can be
                    // judged; one that names none keeps whatever the last draw set, which is why the
                    // else branch below puts the default back rather than leaving it.
                    material = _materials.For(texture);
                }

                // Each sub-mesh names the page it samples; a model with none, or one whose bake is
                // absent, draws with the device's white lightmap and is simply unshaded.
                TextureHandle lightmap = subset.LightmapPage < pages.Length
                    ? pages[subset.LightmapPage]
                    : TextureHandle.None;

                _device.SetMaterialOverride(material);
                _device.DrawMesh(
                    mesh,
                    subset.IndexOffset,
                    subset.IndexCount,
                    modelView,
                    diffuse,
                    lightmap,
                    BlendMode.Opaque);

                draws++;
                triangles += subset.TriangleCount;
                if (lightmap.IsValid)
                {
                    lit++;
                }
            }
        }

        LastDrawCount = draws;
        LastTriangleCount = triangles;
        LastLightmappedCount = lit;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (MeshHandle mesh in _meshes.Values)
        {
            _device.DestroyMesh(mesh);
        }

        foreach (TextureHandle[] pages in _lightmaps.Values)
        {
            foreach (TextureHandle page in pages)
            {
                _device.DestroyTexture(page);
            }
        }

        foreach (TextureHandle texture in _textures.Values)
        {
            _device.DestroyTexture(texture);
        }

        _lightmaps.Clear();
        _meshes.Clear();
        _textures.Clear();
        _geometry.Clear();
    }

    private MeshGeometry? GetGeometry(WorldPlacement placement)
    {
        if (_geometry.TryGetValue(placement.Instance.Name, out MeshGeometry? cached))
        {
            return cached;
        }

        MeshGeometry? built = MefMeshBuilder.Build(placement.Model, (float)(1.0 / WorldScene.UnitsPerMetre));
        _geometry[placement.Instance.Name] = built;
        return built;
    }

    private MeshHandle GetMesh(string name, MeshGeometry geometry)
    {
        if (_meshes.TryGetValue(name, out MeshHandle cached))
        {
            return cached;
        }

        MeshHandle handle = MeshHandle.None;
        try
        {
            handle = _device.CreateMesh(geometry.Vertices, geometry.Indices);
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            _logger.Warning(LogCategory, $"Model '{name}' did not upload: {ex.Message}");
        }

        _meshes[name] = handle;
        return handle;
    }

    /// <summary>Gets the uploaded pages of a placement's baked lightmap, empty when it has none.</summary>
    /// <remarks>
    /// One <c>.olm</c> entry holds every page of one model at one level of detail, so this uploads them
    /// together and caches by entry name. Level 1's archive is 1308 entries, but only the ones actually
    /// drawn are ever touched.
    /// </remarks>
    private TextureHandle[] GetLightmapPages(WorldScene scene, WorldPlacement placement)
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
                ObjectLightmap lightmap = ObjectLightmap.Read(entry.Data);
                pages = new TextureHandle[lightmap.Images.Count];
                for (int i = 0; i < pages.Length; i++)
                {
                    ObjectLightmapImage image = lightmap.Images[i];

                    // A page is an atlas of this model's own surfaces, so it clamps rather than repeats:
                    // wrapping would bleed one surface's light onto another at the edges.
                    pages[i] = _device.CreateTexture(
                        new TextureDescription(
                            image.Width,
                            image.Height,
                            1,
                            TextureFilter.Linear,
                            TextureAddressMode.Clamp),
                        MemoryMarshal.Cast<byte, Bgra32>(image.Pixels.Span));
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

    /// <summary>Gets or sets what each of the level's textures is made of.</summary>
    /// <remarks>
    /// Settable rather than constructor-injected because it is data the shell loads once and may
    /// reload, and because a renderer with no library still draws -- every surface simply takes the
    /// default, which is the behaviour from before materials existed.
    /// </remarks>
    public MaterialLibrary Materials
    {
        get => _materials;
        set => _materials = value ?? MaterialLibrary.Empty;
    }

    /// <summary>Gets or sets the derived surface-detail maps, or <see langword="null"/> for none.</summary>
    /// <remarks>
    /// Settable for the same reason <see cref="Materials"/> is, and owned by the shell rather than by
    /// this renderer: the same pack serves the world, the soldiers and the terrain, and each of them
    /// loads its own textures.
    /// </remarks>
    public MaterialDetailPack? DetailMaps { get; set; }

    /// <summary>Captures every distinct model's surface into the card atlas.</summary>
    /// <param name="scene">The level, for the placements that name which models are used.</param>
    /// <param name="cardResolution">How many texels per world unit a card asks for.</param>
    /// <returns>What was captured, or <see langword="null"/> when there was nothing to capture.</returns>
    /// <remarks>
    /// <para>
    /// Here rather than in the shell because everything it needs is already here and private: the
    /// geometry cache, the uploaded meshes, the texture cache and the material library. Reaching in
    /// from outside would mean exposing all four, and the only caller wants none of them.
    /// </para>
    /// <para>
    /// <b>Once per distinct model, not per placement.</b> A level draws the same crate hundreds of
    /// times and it looks the same every time. Spline-bent geometry is skipped entirely: it is unique
    /// to its placement, so it has no shared model whose cards could be reused, and cards for one-off
    /// geometry would be atlas spent on a single object.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> is <see langword="null"/>.</exception>
    public (SurfaceCacheCapture.Result Capture, SurfaceCacheInstances? Instances)? CaptureSurfaceCache(
        WorldScene scene, float cardResolution = 8f)
    {
        ArgumentNullException.ThrowIfNull(scene);

        List<SurfaceCacheCapture.Subject> subjects = [];
        List<MeshHandle> meshes = [];

        // Which captured model each instance name became, so the per-placement lighting layout can join
        // a placement back to the cards its model contributed.
        Dictionary<string, int> modelOfName = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (WorldPlacement placement in scene.Placements)
        {
            if (placement.BentGeometry is not null || !seen.Add(placement.Instance.Name))
            {
                continue;
            }

            if (GetGeometry(placement) is not { } geometry || geometry.Indices.Length < 3)
            {
                continue;
            }

            MeshHandle mesh = GetMesh(placement.Instance.Name, geometry);

            if (!mesh.IsValid)
            {
                continue;
            }

            IReadOnlyList<MeshCard> cards = MeshCardBaker.Bake(geometry, cardResolution);

            if (cards.Count == 0)
            {
                continue;
            }

            // Captured by value so the closure does not depend on which placement the loop is on when
            // the capture actually runs.
            WorldPlacement owner = placement;

            subjects.Add(new SurfaceCacheCapture.Subject(
                geometry,
                cards,
                subset =>
                {
                    TextureHandle diffuse = TextureHandle.None;
                    SurfaceMaterial material = _materials.Default;

                    if (subset.HasMaterial
                        && owner.Pack.TryGetTextureName(owner.Instance, subset.MaterialSlot, out string texture))
                    {
                        diffuse = GetTexture(texture);
                        material = _materials.For(texture);
                    }

                    // No lightmap. A card belongs to a model and a bake belongs to a placement, so
                    // there is no single bake a shared card could carry -- see CardAtlasShaders.
                    return (diffuse, TextureHandle.None, material);
                }));

            modelOfName[placement.Instance.Name] = subjects.Count - 1;
            meshes.Add(mesh);
        }

        if (SurfaceCacheCapture.Capture(_device, subjects, meshes, 8192, _logger) is not { } captured)
        {
            return null;
        }

        // A card's SURFACE belongs to the model and is shared; its LIGHTING belongs to the placement and
        // is not -- one crate stands in sunlight and another in a hangar's shadow. So the lighting gets
        // a second layout, a page per placement of each card.
        SurfaceCacheInstances? instances = SurfaceCacheInstances.Build(
            captured,
            scene.Placements,
            placement => placement.BentGeometry is null
                && modelOfName.TryGetValue(placement.Instance.Name, out int model) ? model : -1,
            8192,
            _logger);

        return (captured, instances);
    }

    private TextureHandle GetTexture(string name)
    {
        if (_textures.TryGetValue(name, out TextureHandle cached))
        {
            return cached;
        }

        TextureHandle handle = TextureHandle.None;
        string resource = LoadedPack.TextureResourceName(name);

        if (_resources.TryLoad(resource, out ReadOnlyMemory<byte> bytes))
        {
            try
            {
                TextureImage image = TexFile.Load(bytes.Span);
                handle = _device.CreateTexture(
                    new TextureDescription(image.Width, image.Height, image.MipLevels, TextureFilter.Linear, TextureAddressMode.Repeat),
                    image.Pixels);

                // The derived normal and roughness this game never shipped, if a pack has been built.
                // Registered against the texture rather than carried through the draw, so nothing below
                // this line knows the maps exist.
                DetailMaps?.Attach(name, handle);
            }
            catch (Exception ex) when (ex is TextureFormatException or ArgumentException or GraphicsDeviceException)
            {
                _logger.Warning(LogCategory, $"Texture '{name}' did not load: {ex.Message}");
            }
        }
        else if (_missingTextures.Add(name))
        {
            _logger.Warning(LogCategory, $"Texture '{resource}' is not registered.");
        }

        _textures[name] = handle;
        return handle;
    }
}
