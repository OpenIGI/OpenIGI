using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Lighting;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// Resolves what each placement draws with: its geometry and mesh upload, cached by model, and its
/// per-subset textures, materials, lightmap pages and lamp binding, cached per placement.
/// </summary>
/// <remarks>
/// The per-placement state bakes in the material library and the switchboard, so replacing either
/// melts it and the next draw resolves it again from the caches.
/// </remarks>
internal sealed class WorldSubsetResolver
{
    /// <summary>How far a lamp placement may stand from the light it houses, in metres.</summary>
    /// <remarks>See where it is used; the extractor's clustering is what sets the scale.</remarks>
    private const double LampBindingRadius = 4.0;

    private readonly IGraphicsDevice _device;
    private readonly IOpenIgiLogger _logger;
    private readonly WorldPlacementTransforms _transforms;
    private readonly WorldTextureCache _textures;
    private readonly WorldLightmapPages _lightmaps;
    private LampSwitchboard? _switchboard;
    private MaterialLibrary _materials = MaterialLibrary.Empty;
    private readonly Dictionary<string, MeshGeometry?> _geometry = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MeshHandle> _meshes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Each placement's mesh and per-subset draw state, resolved once and kept.</summary>
    /// <remarks>
    /// Keyed by reference: <see cref="WorldPlacement"/> is a record, and its value equality would hash
    /// every field of every placement on every lookup.
    /// </remarks>
    private readonly Dictionary<WorldPlacement, ResolvedWorldModel?> _resolved = new(ReferenceEqualityComparer.Instance);

    /// <summary>The destroyed-variant models, for placements whose head has been shot out.</summary>
    private readonly Dictionary<WorldPlacement, ResolvedWorldModel?> _resolvedDestroyed = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<WorldPlacement, ResolvedWorldModel?[]> _resolvedVariants = new(ReferenceEqualityComparer.Instance);

    /// <summary>The lower levels of detail, each placement the level-of-detail policy made for one.</summary>
    private readonly Dictionary<WorldPlacement, ResolvedWorldModel?> _resolvedLevels = new(ReferenceEqualityComparer.Instance);

    /// <summary>Initializes the resolver over the renderer's device and its texture and lightmap caches.</summary>
    /// <param name="device">The device meshes upload to.</param>
    /// <param name="logger">Where to report upload problems.</param>
    /// <param name="transforms">What each placement is displayed as.</param>
    /// <param name="textures">The diffuse texture cache.</param>
    /// <param name="lightmaps">The baked lightmap pages.</param>
    internal WorldSubsetResolver(
        IGraphicsDevice device,
        IOpenIgiLogger logger,
        WorldPlacementTransforms transforms,
        WorldTextureCache textures,
        WorldLightmapPages lightmaps)
    {
        _device = device;
        _logger = logger;
        _transforms = transforms;
        _textures = textures;
        _lightmaps = lightmaps;
    }

    /// <summary>Gets or sets which of the level's sub-meshes are light fixtures.</summary>
    internal LampDetector.Lamps Lamps { get; set; } = LampDetector.Lamps.None;

    /// <summary>Gets or sets which of the level's lamps are currently lit, or <see langword="null"/> for all of them.</summary>
    internal LampSwitchboard? Switchboard
    {
        get => _switchboard;
        set
        {
            if (ReferenceEquals(_switchboard, value))
            {
                return;
            }

            _switchboard = value;

            // Each placement's binding to the light it houses is resolved with its subsets, so a new
            // board has to melt them for the same reason a reloaded material library does. They
            // re-resolve on the next draw at the cost of a dictionary lookup each.
            MeltResolvedSubsets();
        }
    }

    /// <summary>Gets or sets what each of the level's textures is made of.</summary>
    internal MaterialLibrary Materials
    {
        get => _materials;
        set
        {
            _materials = value ?? MaterialLibrary.Empty;

            // The resolved subsets bake each surface's material in, so a reloaded library has to melt
            // them; they re-resolve from the caches on the next draw at dictionary-lookup cost.
            MeltResolvedSubsets();
        }
    }

    /// <summary>Resolves one placement's mesh, textures and lightmap pages without drawing it.</summary>
    /// <param name="scene">The scene the placement belongs to.</param>
    /// <param name="authored">The placement as authored.</param>
    internal void MakeResident(WorldScene scene, WorldPlacement authored)
    {
        WorldPlacement placement = _transforms.DisplayedPlacement(authored);

        MeshGeometry? geometry = placement.BentGeometry ?? GetGeometry(placement);
        if (geometry is null)
        {
            return;
        }

        GetMesh(placement.BentGeometry is null ? placement.Instance.Name : placement.TaskName, geometry);
        _lightmaps.GetLightmapPages(scene, placement);

        foreach (MeshSubset subset in geometry.Subsets)
        {
            if (subset.IndexCount > 0
                && subset.HasMaterial
                && placement.Pack.TryGetTextureName(placement.Instance, subset.MaterialSlot, out string texture))
            {
                _textures.GetTexture(texture);
            }
        }
    }

    /// <summary>Chooses the model a placement shows this frame, resolving it the first time it is shown.</summary>
    /// <param name="authored">The placement as authored.</param>
    /// <param name="entry">The placement's frame entry, whose displayed placement is refreshed as the choice changes.</param>
    /// <param name="destroyed">Whether it shows its destroyed replacement.</param>
    /// <param name="variant">The variant it wears, or -1.</param>
    /// <returns>The model, or <see langword="null"/> when it has nothing to draw.</returns>
    internal ResolvedWorldModel? SelectModel(WorldPlacement authored, ref WorldFrameEntry entry, bool destroyed, int variant)
    {
        ResolvedWorldModel? model;
        if (variant >= 0)
        {
            // A placement wearing one of its variants: each resolved once, the displayed one refreshed as it changes.
            if (!_resolvedVariants.TryGetValue(authored, out ResolvedWorldModel?[]? resolvedVariants))
            {
                resolvedVariants = new ResolvedWorldModel?[authored.Variants!.Count];
                _resolvedVariants[authored] = resolvedVariants;
            }

            model = resolvedVariants[variant];
            if (model is null || entry.Displayed is null || variant != entry.Variant || destroyed != entry.WasDestroyed)
            {
                WorldPlacement displayed = _transforms.DisplayedPlacement(authored);
                model ??= resolvedVariants[variant] = ResolveModel(displayed);
                entry.Displayed = displayed;
            }
        }
        else
        {
            Dictionary<WorldPlacement, ResolvedWorldModel?> cache = destroyed ? _resolvedDestroyed : _resolved;
            if (!cache.TryGetValue(authored, out model))
            {
                WorldPlacement displayed = _transforms.DisplayedPlacement(authored);
                model = ResolveModel(displayed);
                entry.Displayed = displayed;
                cache[authored] = model;
            }
            else if (entry.Displayed is null || destroyed != entry.WasDestroyed || entry.Variant >= 0)
            {
                entry.Displayed = _transforms.DisplayedPlacement(authored);
            }
        }

        return model;
    }

    /// <summary>Drops every placement's resolved state, returning its occlusion queries to the device.</summary>
    internal void ForgetResolved()
    {
        DestroyResolvedQueries();
        _resolved.Clear();
        _resolvedDestroyed.Clear();
        _resolvedVariants.Clear();
        _resolvedLevels.Clear();
    }

    /// <summary>Resolves one of a placement's lower levels of detail, once.</summary>
    /// <param name="level">The level's placement.</param>
    /// <returns>Its model, or <see langword="null"/> when it does not build.</returns>
    internal ResolvedWorldModel? ResolveLevel(WorldPlacement level)
    {
        if (!_resolvedLevels.TryGetValue(level, out ResolvedWorldModel? model))
        {
            model = ResolveModel(level);
            _resolvedLevels[level] = model;
        }

        return model;
    }

    /// <summary>Destroys every uploaded model mesh, as disposing the renderer does.</summary>
    internal void DestroyMeshes()
    {
        foreach (MeshHandle mesh in _meshes.Values)
        {
            _device.DestroyMesh(mesh);
        }
    }

    /// <summary>Forgets every cached mesh, geometry and resolution, as disposing the renderer does.</summary>
    internal void Clear()
    {
        _meshes.Clear();
        _geometry.Clear();
        _resolved.Clear();
        _resolvedDestroyed.Clear();
        _resolvedVariants.Clear();
        _resolvedLevels.Clear();
    }

    /// <summary>Returns every resolved placement's occlusion query to the device.</summary>
    internal void DestroyResolvedQueries()
    {
        foreach (ResolvedWorldModel? model in _resolved.Values)
        {
            if (model is { Query.IsValid: true })
            {
                _device.DestroyOcclusionQuery(model.Query);
                model.Query = OcclusionQueryHandle.None;
            }
        }

        foreach (ResolvedWorldModel? model in _resolvedDestroyed.Values)
        {
            if (model is { Query.IsValid: true })
            {
                _device.DestroyOcclusionQuery(model.Query);
                model.Query = OcclusionQueryHandle.None;
            }
        }

        foreach (ResolvedWorldModel? model in _resolvedLevels.Values)
        {
            if (model is { Query.IsValid: true })
            {
                _device.DestroyOcclusionQuery(model.Query);
                model.Query = OcclusionQueryHandle.None;
            }
        }

        foreach (ResolvedWorldModel?[] variants in _resolvedVariants.Values)
        {
            foreach (ResolvedWorldModel? model in variants)
            {
                if (model is { Query.IsValid: true })
                {
                    _device.DestroyOcclusionQuery(model.Query);
                    model.Query = OcclusionQueryHandle.None;
                }
            }
        }
    }

    /// <summary>Resolves a displayed placement's geometry and mesh; subsets stay lazy.</summary>
    /// <remarks>
    /// Deliberately in two stages: the mesh is needed before culling, but the textures and lightmaps
    /// are not — resolving those only when a placement first passes a frustum keeps residency
    /// spreading over the walk exactly as it did when the draw loop resolved them inline.
    /// </remarks>
    internal ResolvedWorldModel? ResolveModel(WorldPlacement displayed)
    {
        MeshGeometry? geometry = displayed.BentGeometry ?? GetGeometry(displayed);
        if (geometry is null)
        {
            return null;
        }

        MeshHandle mesh = GetMesh(
            displayed.BentGeometry is null ? displayed.Instance.Name : displayed.TaskName,
            geometry);
        return mesh.IsValid ? new ResolvedWorldModel(geometry, mesh, displayed.Model.IsSector) : null;
    }

    /// <summary>The same surface with its lamp turned down, or turned off altogether.</summary>
    /// <remarks>
    /// <para>
    /// Emission is not a brightness on this path — it is a SHADING MODEL. A surface with any emission at
    /// all is written to the G-buffer as unlit and comes out of the lighting pass as its own albedo
    /// times that number, so scaling it towards zero does not fade a panel back into the room: it fades
    /// it to black, and a switched-off lamp in a lit corridor ends up a hole in the wall.
    /// </para>
    /// <para>
    /// So the last part of the fade is a handover rather than a ramp. Below the threshold the emission
    /// is dropped outright and the panel becomes an ordinary lit surface, which is what a dead fitting
    /// is. The threshold is low — under a third of albedo, dimmer than the same panel would be lit in
    /// any room worth lighting — so the step it costs is smaller than the step of leaving it out, and it
    /// lands two hundred milliseconds into a fade nobody is looking at the panel during.
    /// </para>
    /// <para>
    /// It also has to clear <see cref="SurfaceMaterial.ExcludedFromOcclusion"/>, which is a quarter and
    /// is not a brightness at all: the G-buffer has no flags channel, so that one value in the emissive
    /// slot means "leave this surface out of the ambient occlusion". A lamp allowed to fade through it
    /// would spend a frame claiming to be that instead.
    /// </para>
    /// </remarks>
    internal static SurfaceMaterial Dimmed(SurfaceMaterial material, float glow)
    {
        float emissive = material.Emissive * glow;

        return material with { Emissive = emissive < 0.3f ? 0f : emissive };
    }

    /// <summary>Resolves a model's per-subset textures, materials and lightmap pages, once.</summary>
    internal WorldSubsetDraw[] ResolveSubsets(WorldScene scene, ResolvedWorldModel model, WorldPlacement displayed)
    {
        TextureHandle[] pages = _lightmaps.GetLightmapPages(scene, displayed);

        int drawable = 0;
        foreach (MeshSubset subset in model.Geometry.Subsets)
        {
            if (subset.IndexCount > 0)
            {
                drawable++;
            }
        }

        WorldSubsetDraw[] subsets = new WorldSubsetDraw[drawable];
        int cursor = 0;
        bool anyLamp = false;
        foreach (MeshSubset subset in model.Geometry.Subsets)
        {
            if (subset.IndexCount == 0)
            {
                continue;
            }

            TextureHandle diffuse = TextureHandle.None;
            SurfaceMaterial material = _materials.Default;
            bool lamp = false;

            if (subset.HasMaterial
                && displayed.Pack.TryGetTextureName(displayed.Instance, subset.MaterialSlot, out string texture))
            {
                diffuse = _textures.GetTexture(texture);

                // What this surface is made of. A sub-mesh that names no texture takes the default
                // rather than inheriting the previous draw's, exactly as the inline loop did.
                material = _materials.For(texture);

                // ...and whether this particular sub-mesh is a lamp, which is not a property of the
                // texture. A texture is shared -- the page that skins a ceiling tube also skins the panel
                // it hangs from and, in level 1, the side of an office cabinet -- so making a texture
                // emissive glowed a whole garage soffit and put a flat white slab down that cabinet.
                // Size alone does not save it either: the cabinet's panel is smaller than a fluorescent
                // batten. Only the model and material slot the detector or the manifest named will do.
                //
                // The strength comes from there too, because one texture is not one brightness: a
                // floodlight and the lamp over a door draw with the same page and are not the same light.
                //
                // Never over an authored material. A materials.json entry saying a surface glows is
                // somebody having looked at it, and nothing worked out here overrules that.
                if (material.Emissive <= 0f
                    && Lamps.TryGetStrength(displayed.Instance.Name, subset.MaterialSlot, out float glow))
                {
                    material = material with { Emissive = glow };

                    // ...and this, and only this, is what a wall switch may turn down. An authored
                    // emissive above -- a screen, a sign, the sky dome -- is somebody having decided that
                    // surface gives off light, and it is not a lamp anybody can switch off.
                    lamp = true;
                    anyLamp = true;
                }
            }

            // Each sub-mesh names the page it samples; a model with none, or one whose bake is
            // absent, draws with the device's white lightmap and is simply unshaded.
            TextureHandle lightmap = subset.LightmapPage < pages.Length
                ? pages[subset.LightmapPage]
                : TextureHandle.None;

            subsets[cursor++] = new WorldSubsetDraw(
                subset.IndexOffset,
                subset.IndexCount,
                subset.TriangleCount,
                diffuse,
                material,
                lightmap,
                lamp,
                subset.SkinIndexCount);
        }

        // Which of the level's lights this placement is the housing of, so that switching that light off
        // stops this geometry glowing. Resolved here rather than in the switchboard because here is where
        // the placement is already known to draw a lamp at all -- that is a fact about the model's
        // sub-meshes, and asking every placement in the level how near a fixture it stands would ask it
        // of five hundred crates as well.
        //
        // A generous radius: a fixture is the merged centroid of a whole cluster of emitting triangles,
        // and a long batten's centroid can sit a couple of metres from the origin of the placement that
        // draws part of it. Wrong only where two unrelated lamps stand within a few metres of each other
        // and were still not merged, which is a lamp switching its neighbour off -- visible, harmless,
        // and rarer than the alternative of a switch that does nothing.
        model.LampFixture = anyLamp && Switchboard is { } board
            ? board.NearestFixture(displayed.Position, LampBindingRadius)
            : -1;

        model.Subsets = subsets;

        bool glass = false;
        foreach (WorldSubsetDraw drawn in subsets)
        {
            glass |= drawn.Material.Glass;
        }

        model.HasGlass = glass;
        return subsets;
    }

    /// <summary>Drops every placement's resolved per-subset state, so the next draw resolves it again.</summary>
    internal void MeltResolvedSubsets()
    {
        foreach (ResolvedWorldModel? model in _resolved.Values)
        {
            if (model is not null)
            {
                model.Subsets = null;
            }
        }

        foreach (ResolvedWorldModel? model in _resolvedDestroyed.Values)
        {
            if (model is not null)
            {
                model.Subsets = null;
            }
        }

        foreach (ResolvedWorldModel? model in _resolvedLevels.Values)
        {
            if (model is not null)
            {
                model.Subsets = null;
            }
        }

        foreach (ResolvedWorldModel?[] variants in _resolvedVariants.Values)
        {
            foreach (ResolvedWorldModel? model in variants)
            {
                if (model is not null)
                {
                    model.Subsets = null;
                }
            }
        }
    }

    /// <summary>Builds a placement's model geometry, once per model name.</summary>
    internal MeshGeometry? GetGeometry(WorldPlacement placement)
    {
        if (_geometry.TryGetValue(placement.Instance.Name, out MeshGeometry? cached))
        {
            return cached;
        }

        MeshGeometry? built = MefMeshBuilder.Build(placement.Model, (float)(1.0 / WorldScene.UnitsPerMetre));
        _geometry[placement.Instance.Name] = built;
        return built;
    }

    /// <summary>Uploads a model's mesh, once per name.</summary>
    internal MeshHandle GetMesh(string name, MeshGeometry geometry)
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
            _logger.Warning(WorldRenderer.LogCategory, $"Model '{name}' did not upload: {ex.Message}");
        }

        _meshes[name] = handle;
        return handle;
    }

    /// <summary>Captures every distinct model's surface into the card atlas.</summary>
    /// <param name="scene">The level, for the placements that name which models are used.</param>
    /// <param name="cardResolution">How many texels per world unit a card asks for.</param>
    /// <returns>What was captured, or <see langword="null"/> when there was nothing to capture.</returns>
    internal (SurfaceCacheCapture.Result Capture, SurfaceCacheInstances? Instances)? CaptureSurfaceCache(
        WorldScene scene, float cardResolution)
    {
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
                        diffuse = _textures.GetTexture(texture);
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
}
