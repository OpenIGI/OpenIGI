using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using OpenIGI.Shaders;

namespace OpenIGI.Presentation.Lighting;

/// <summary>
/// What every card-lighting, gather and cache dispatch binds the same way: the card lookup, the
/// clipmaps, the sky's harmonics, the terrain and roof rasters, and the sun the cache was last lit by.
/// </summary>
/// <remarks>
/// Shared by <see cref="SurfaceCacheLighting"/> and its passes so that each dispatch states these
/// bindings through one copy of the code; the order of the calls inside each binder is the order the
/// passes always made them in.
/// </remarks>
internal sealed class SurfaceCacheBindings
{
    private readonly IGraphicsDevice _device;
    private readonly Vector3d _origin;

    // The terrain's height raster for the untraced gather (see TerrainHeightRaster), and how to read it.
    private TextureHandle _terrainHeights;
    private (float X, float Y, float Z, float W) _terrainMap0;
    private (float X, float Y, float Z, float W) _terrainMap1;

    // The roof raster, which tells a marched ray that went through a wall (see SetRoofs).
    private TextureHandle _roofs;
    private ShelterMapTransform _roofTransform = ShelterMapTransform.None;

    /// <summary>Initializes the bindings over the device and the pass's origin.</summary>
    /// <param name="device">The device the passes dispatch on.</param>
    /// <param name="origin">The world point the pass's float positions are measured from.</param>
    /// <param name="tracesRays">Whether the compiled passes carry the hardware ray query.</param>
    internal SurfaceCacheBindings(IGraphicsDevice device, Vector3d origin, bool tracesRays)
    {
        _device = device;
        _origin = origin;
        TracesRays = tracesRays;
    }

    /// <summary>Gets or sets the index that turns a hit position into a card page, or none.</summary>
    internal SurfaceCacheLookup? Lookup { get; set; }

    // Which trace tier the compiled shaders carry: the hardware ray query, or the software distance
    // field. Tracked so a live toggle of r_rt recompiles them instead of being silently ignored.
    internal bool TracesRays { get; set; }

    // The sun the cache was last relit under, for what the screen gather's escaped rays meet. See GroundSun.
    internal Vector3f LastSunDirection { get; set; }

    /// <summary>Gets or sets the sun's colour the cache was last relit under.</summary>
    internal Vector3f LastSunColour { get; set; }

    /// <summary>The sun's light on open ground, as irradiance over pi, from the sun the cache was last lit by.</summary>
    internal Vector3f GroundSun() =>
        LastSunColour * (MathF.Max(LastSunDirection.Z, 0f) / MathF.PI);

    /// <summary>Hands the untraced gather the terrain's height raster, or takes it away.</summary>
    /// <param name="heights">The packed raster (<see cref="ShelterHeightField.Encode"/>), or none.</param>
    /// <param name="field">The raster it was packed from, which says where it lies.</param>
    /// <param name="scale">The metres one packed step stands for, as the packing reported.</param>
    /// <remarks>
    /// The distance field the untraced gather marches holds the level's placements and not its terrain, so
    /// a ray towards the ground went through it and came back with a guess at open ground. With this it
    /// finds the ground, and reads the cache's light where it landed.
    /// </remarks>
    internal void SetTerrainHeights(TextureHandle heights, ShelterHeightField? field, float scale)
    {
        if (!heights.IsValid || field is null || field.IsEmpty)
        {
            _terrainHeights = TextureHandle.None;
            _terrainMap0 = default;
            _terrainMap1 = default;
            return;
        }

        _terrainHeights = heights;
        _terrainMap0 = ((float)field.OriginX, (float)field.OriginY, (float)(1.0 / field.CellMetres), 1f);
        _terrainMap1 = (field.Width, field.Depth, field.LowestZ, scale);
    }

    /// <summary>Hands the untraced gather the level's roof raster, or takes it away.</summary>
    /// <param name="roofs">The packed raster, as the shading reads it, or none.</param>
    /// <param name="transform">How to read it; <see cref="ShelterMapTransform.None"/> with none.</param>
    /// <remarks>
    /// A ray marched out of doors that lands on a surface with a roof over it went through a wall or a roof
    /// the field could not hold at that distance; the gather counts it as a miss rather than carrying the
    /// room's lamps out onto the wall outside.
    /// </remarks>
    internal void SetRoofs(TextureHandle roofs, ShelterMapTransform transform)
    {
        _roofs = roofs.IsValid && transform.IsPresent ? roofs : TextureHandle.None;
        _roofTransform = _roofs.IsValid ? transform : ShelterMapTransform.None;
    }

    /// <summary>Binds the terrain's height raster to a gather, or a stand-in that says there is none.</summary>
    /// <param name="shader">The gather.</param>
    internal void BindTerrainHeights(ComputeShaderHandle shader)
    {
        _device.SetTexture(14, _terrainHeights);
        _device.SetComputeUniform(shader, "uTerrainMap0", _terrainMap0.X, _terrainMap0.Y, _terrainMap0.Z, _terrainMap0.W);
        _device.SetComputeUniform(shader, "uTerrainMap1", _terrainMap1.X, _terrainMap1.Y, _terrainMap1.Z, _terrainMap1.W);

        // And the roof raster beside it.
        ShelterMapTransform r = _roofTransform;
        _device.SetTexture(15, _roofs);
        _device.SetComputeUniform(
            shader, "uShelterMap0", r.OriginX, r.OriginY, r.IsPresent ? 1f / r.CellMetres : 0f, r.IsPresent ? 1f : 0f);
        _device.SetComputeUniform(shader, "uShelterMap1", r.Width, r.Height, r.LowestZ, r.HeightScale);
        _device.SetComputeUniform(shader, "uShelterMap2", r.HeadroomMetres, r.ReachMetres, 0f, 0f);
    }

    /// <summary>Hands a pass the sky's harmonics, when the backend has an atmosphere to report.</summary>
    /// <param name="shader">The pass to write the uniforms of.</param>
    /// <remarks>
    /// The seven coefficient vectors the deferred sky term itself consumes. They hold diffuse irradiance
    /// divided by pi, not unconvolved sky radiance; the tracing shaders name and restore that contract at
    /// the point where they integrate it. A backend without them leaves the pass on its flat colour.
    /// </remarks>
    internal void WriteSkyHarmonics(ComputeShaderHandle shader)
    {
        Span<float> sh = stackalloc float[28];
        bool valid = _device.TryGetSkyIrradiance(sh);
        _device.SetComputeUniform(shader, "uSkyShValid", valid ? 1 : 0);
        if (!valid)
        {
            return;
        }

        for (int i = 0; i < 7; i++)
        {
            int at = i * 4;
            _device.SetComputeUniform(
                shader,
                FormattableString.Invariant($"uSkySh[{i}]"),
                sh[at],
                sh[at + 1],
                sh[at + 2],
                sh[at + 3]);
        }
    }

    /// <summary>The brightest a single screen GI ray may be, in scene units.</summary>
    /// <remarks>
    /// r_screen_gi_max is a cap on what a ray contributes as the eye SEES it -- a cap on pre-exposed
    /// radiance -- so it is divided by the exposure before it reaches the shader. Applied in raw scene
    /// units it was a different cap at every time of day: at night the exposure is sixteen or more, so it
    /// let rays through sixteen times brighter than by day, and the few of a probe's sixteen that landed on
    /// a lamp's fixture or its pool of light swung its estimate frame to frame into the drifting clouds
    /// around every lamp.
    /// </remarks>
    internal float ExposedRayCap()
        => ScreenGatherCVars.ScreenGatherMaxIntensity.Value / MathF.Max(_device.SceneExposure, 1e-3f);

    /// <summary>Hands the clipmaps to the pass, or tells it there are none.</summary>
    /// <remarks>
    /// Every position the shader works in is relative to this pass's own origin, and the clipmaps are
    /// relative to theirs, so their corners are rebased on the way in. Two structures each measuring
    /// from their own centre is what keeps both in float range across a level kilometres from the world
    /// origin; converting between them here is the price of that.
    /// </remarks>
    /// <param name="shader">The pass to bind them to.</param>
    /// <param name="shadows">The clipmaps, or <see langword="null"/> for none.</param>
    internal void BindShadows(ComputeShaderHandle shader, GlobalDistanceField? shadows)
    {
        // Where this pass's origin sits in the world the ray-traced scene was built in. Harmless on a
        // backend that marches the distance field instead: the uniform simply is not there to set.
        _device.SetComputeUniform(
            shader,
            "uTraceOrigin",
            new Vector3f((float)_origin.X, (float)_origin.Y, (float)_origin.Z));

        if (shadows is null)
        {
            _device.SetComputeUniform(shader, "uShadowLevels", 0);
            return;
        }

        GlobalDistanceFieldSettings settings = shadows.Settings;
        int levels = Math.Clamp(settings.Levels, 0, 4);

        _device.SetVolumeTexture(5, shadows.Clipmaps);
        _device.SetComputeUniform(shader, "uShadowLevels", levels);
        _device.SetComputeUniform(shader, "uShadowResolution", settings.Resolution);

        for (int level = 0; level < levels; level++)
        {
            Vector3d corner = shadows.Origins[level];

            _device.SetComputeUniform(
                shader,
                FormattableString.Invariant($"uClipmapOrigin[{level}]"),
                (float)(corner.X - _origin.X),
                (float)(corner.Y - _origin.Y),
                (float)(corner.Z - _origin.Z),
                settings.VoxelSizeOf(level));

            _device.SetComputeUniform(
                shader,
                FormattableString.Invariant($"uClipmapRange[{level}]"),
                shadows.RangeOfLevel(level));
        }

        // The outermost level's box, for the position probe: a reconstructed position mapped through
        // this lands inside the unit cube exactly when the trace could have seen it at all.
        Vector3d outer = shadows.Origins[levels - 1];
        float extent = settings.VoxelSizeOf(levels - 1) * settings.Resolution;

        _device.SetComputeUniform(
            shader,
            "uProbeMin",
            new Vector3f(
                (float)(outer.X - _origin.X),
                (float)(outer.Y - _origin.Y),
                (float)(outer.Z - _origin.Z)));
        _device.SetComputeUniform(shader, "uProbeInvExtent", extent > 0f ? 1f / extent : 0f);
    }
}
