using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using OpenIGI.Shaders;

namespace OpenIGI.Presentation.Lighting;

/// <summary>
/// The world-space radiance cache: a grid of probes anchored to world cells that trace the surface
/// cache a slab a frame and hold the indirect irradiance in the baked probe field's L1-SH layout.
/// </summary>
/// <remarks>
/// Compiled and allocated on first configure, so a preset that never turns it on never pays for it.
/// <see cref="SurfaceCacheLighting"/> exposes it.
/// </remarks>
internal sealed class SurfaceCacheRadianceVolume
{
    private const string LogCategory = "SurfaceCache";

    /// <summary>How many frames the radiance cache spreads a full grid sweep across.</summary>
    /// <remarks>
    /// One Z-slab of probes is traced each frame and the cursor walks the depth, so the whole grid is
    /// refreshed every this-many frames. The same amortisation the steady-state <see cref="SurfaceCacheLighting.Bounce"/> uses,
    /// and sound for the same reason: the geometry and the sun are static, so a probe traced this frame
    /// holds the answer it would trace several frames from now, and the running average carries it between
    /// its updates.
    /// </remarks>
    private const int CacheFrameSpread = 4;

    /// <summary>How many rays each cache probe traces per update. A full sphere, unlike the gather's hemisphere.</summary>
    private const int CacheRays = 48;

    private readonly IGraphicsDevice _device;
    private readonly IOpenIgiLogger _logger;
    private readonly SurfaceCacheBindings _bindings;
    private readonly Action _ensureTraceMode;
    private readonly Vector3d _origin;
    private readonly StorageBufferHandle _pages;
    private readonly TextureHandle _final;
    private readonly int _width;
    private readonly int _height;

    // The world-space radiance cache: a grid of probes that trace the surface cache every frame and hold
    // the indirect irradiance in the same L1-SH layout the baked probe field uses, so the deferred pass
    // samples it unchanged. Unlike the screen gather -- which re-estimates at a MOVING screen-space grid
    // and reprojects, and so shimmers under motion -- these probes are anchored to world cells and only
    // ever accumulate onto themselves, which is what makes the indirect stable while the sun and dynamic
    // surfaces still feed it live. Compiled and allocated on first configure, not at construction, so a
    // preset that never turns it on never pays for it.
    private ComputeShaderHandle _cacheUpdate;

    private TextureHandle _cacheRed;
    private TextureHandle _cacheGreen;
    private TextureHandle _cacheBlue;
    private TextureHandle _cacheDistance;
    private int _cacheCountX;
    private int _cacheCountY;
    private int _cacheCountZ;
    private float _cacheSpacing;
    private Vector3f _cacheOriginRelative;
    private Vector3d _cacheOriginWorld;

    // Which Z-slab of the probe grid the next update traces, and how many complete sweeps have finished.
    // The whole grid is far too many probes to trace in one frame, so a slab is traced each frame and the
    // cursor walks the depth over CacheFrameSpread frames -- the same amortisation the steady-state bounce
    // uses, and sound for the same reason: a world-anchored probe under a fixed sun traces the same answer
    // whichever frame it is visited on. Until the first sweep completes every probe still holds the
    // garbage a freshly allocated volume came up with, so that sweep overwrites rather than blends.
    private int _cacheSlab;
    private int _cacheSweeps;
    private bool _cacheConfigured;

    /// <summary>Initializes the cache over the surface cache it traces.</summary>
    /// <param name="device">The device to allocate and dispatch on.</param>
    /// <param name="logger">Where to report what was allocated.</param>
    /// <param name="bindings">The bindings every dispatch shares.</param>
    /// <param name="ensureTraceMode">Brings every tracing pass up to the current trace mode.</param>
    /// <param name="origin">The world point the pass's float positions are measured from.</param>
    /// <param name="pages">The page records.</param>
    /// <param name="final">The final lighting atlas a hit reads.</param>
    /// <param name="width">The atlas width in texels.</param>
    /// <param name="height">The atlas height in texels.</param>
    internal SurfaceCacheRadianceVolume(
        IGraphicsDevice device,
        IOpenIgiLogger logger,
        SurfaceCacheBindings bindings,
        Action ensureTraceMode,
        Vector3d origin,
        StorageBufferHandle pages,
        TextureHandle final,
        int width,
        int height)
    {
        _device = device;
        _logger = logger;
        _bindings = bindings;
        _ensureTraceMode = ensureTraceMode;
        _origin = origin;
        _pages = pages;
        _final = final;
        _width = width;
        _height = height;
    }

    /// <summary>Gets whether the radiance cache has been allocated and can be updated and sampled.</summary>
    internal bool RadianceCacheReady => _cacheConfigured;

    /// <summary>Gets the cache's red-channel L1-SH volume, for the deferred pass to sample.</summary>
    internal TextureHandle RadianceCacheRed => _cacheRed;

    /// <summary>Gets the cache's green-channel L1-SH volume.</summary>
    internal TextureHandle RadianceCacheGreen => _cacheGreen;

    /// <summary>Gets the cache's blue-channel L1-SH volume.</summary>
    internal TextureHandle RadianceCacheBlue => _cacheBlue;

    /// <summary>Gets the cache's distance-moment volume, for the Chebyshev leak test.</summary>
    internal TextureHandle RadianceCacheDistance => _cacheDistance;

    /// <summary>Gets the world position of the cache probe at grid index (0, 0, 0), in metres.</summary>
    internal Vector3d RadianceCacheOrigin => _cacheOriginWorld;

    /// <summary>Gets the distance between neighbouring cache probes, in metres.</summary>
    internal float RadianceCacheSpacing => _cacheSpacing;

    /// <summary>Gets how many probes the cache spans along X.</summary>
    internal int RadianceCacheCountX => _cacheCountX;

    /// <summary>Gets how many probes the cache spans along Y.</summary>
    internal int RadianceCacheCountY => _cacheCountY;

    /// <summary>Gets how many probes the cache spans along Z.</summary>
    internal int RadianceCacheCountZ => _cacheCountZ;

    /// <summary>Allocates the radiance cache's probe grid, matching a baked field's layout so the sampler is unchanged.</summary>
    /// <param name="worldOrigin">The world position of probe (0, 0, 0), in metres.</param>
    /// <param name="spacing">The distance between neighbouring probes, in metres.</param>
    /// <param name="countX">How many probes span X.</param>
    /// <param name="countY">How many probes span Y.</param>
    /// <param name="countZ">How many probes span Z.</param>
    /// <remarks>
    /// Given the baked field's own layout so the two are interchangeable at the sampler: the deferred pass
    /// reads whichever volume the device binds with the same origin, spacing and counts. A re-configure with
    /// the identical layout is a no-op -- the cache keeps accumulating rather than starting cold -- so this
    /// is safe to call every frame from the game layer without resetting the field each time.
    /// </remarks>
    internal void ConfigureRadianceCache(Vector3d worldOrigin, float spacing, int countX, int countY, int countZ)
    {
        if (spacing <= 0f || countX < 1 || countY < 1 || countZ < 1)
        {
            return;
        }

        if (_cacheConfigured
            && _cacheCountX == countX
            && _cacheCountY == countY
            && _cacheCountZ == countZ
            && _cacheSpacing == spacing
            && _cacheOriginWorld == worldOrigin)
        {
            return;
        }

        ReleaseRadianceCache();

        _cacheRed = _device.CreateVolumeTexture(countX, countY, countZ, VolumeFormat.Rgba16F, "cache.gi.red");
        _cacheGreen = _device.CreateVolumeTexture(countX, countY, countZ, VolumeFormat.Rgba16F, "cache.gi.green");
        _cacheBlue = _device.CreateVolumeTexture(countX, countY, countZ, VolumeFormat.Rgba16F, "cache.gi.blue");
        _cacheDistance = _device.CreateVolumeTexture(countX, countY, countZ, VolumeFormat.Rgba16F, "cache.gi.dist");

        _cacheCountX = countX;
        _cacheCountY = countY;
        _cacheCountZ = countZ;
        _cacheSpacing = spacing;
        _cacheOriginWorld = worldOrigin;
        _cacheOriginRelative = new Vector3f(
            (float)(worldOrigin.X - _origin.X),
            (float)(worldOrigin.Y - _origin.Y),
            (float)(worldOrigin.Z - _origin.Z));
        _cacheSlab = 0;
        _cacheSweeps = 0;
        _cacheConfigured = true;

        _logger.Information(
            LogCategory,
            $"Radiance cache: {countX}x{countY}x{countZ} probes at {spacing:0.#} m.");
    }

    /// <summary>Traces one amortised slab of the radiance cache against the surface cache and the sky.</summary>
    /// <param name="shadows">The clipmaps to trace against; without them there is nothing to gather.</param>
    /// <param name="skyColour">The radiance a ray that escapes the geometry carries.</param>
    /// <param name="maxFrames">The ceiling on the running average, so the field keeps adapting rather than freezing.</param>
    /// <param name="scale">A calibration factor on the stored radiance, to dial the bounce into the tuned range.</param>
    /// <param name="skyReject">The escape fraction under which a probe's sky reads as a wall leak and is dropped.</param>
    /// <remarks>
    /// Each probe fires a sphere of rays through the shared software trace, reads the surface cache's final
    /// lighting at each hit and the sky where a ray escapes, projects the result into first-order harmonics
    /// -- the exact scale the baked <see cref="IrradianceVolume"/> stores, so the two read back identically
    /// -- and blends it onto the probe's running estimate. Because the estimate lives in a world cell rather
    /// than a screen probe, it does not move with the camera and does not need reprojection: that is the
    /// whole reason it is stable where the screen gather is not. Only a slab is traced each frame; the first
    /// full sweep overwrites the freshly allocated garbage, and every sweep after it blends.
    /// </remarks>
    internal void UpdateRadianceCache(
        GlobalDistanceField? shadows, Vector3f skyColour, int maxFrames, float scale, float skyReject)
    {
        if (!_cacheConfigured || shadows is null || _bindings.Lookup is null)
        {
            return;
        }

        _ensureTraceMode();
        if (!_cacheUpdate.IsValid)
        {
            _cacheUpdate = _device.CreateComputeShader(
                ShaderModuleId.CardsRadianceCache,
                new ShaderVariant(_bindings.TracesRays ? "rayquery" : "software"),
                "cache.gi.update");
        }

        int slabDepth = Math.Max(1, (_cacheCountZ + CacheFrameSpread - 1) / CacheFrameSpread);
        int slabBegin = _cacheSlab;
        int slabEnd = Math.Min(slabBegin + slabDepth, _cacheCountZ);

        // The first sweep overwrites; every sweep after it eases the new estimate in over maxFrames so a
        // moving shadow or a relit card arrives smoothly rather than popping. A seeding probe cannot read
        // its own garbage back, so its alpha is one whatever maxFrames says.
        bool seeding = _cacheSweeps == 0;
        float alpha = seeding ? 1f : 1f / Math.Max(maxFrames, 1);

        _device.BindStorageBuffer(0, _pages);
        _device.BindStorageBuffer(3, _bindings.Lookup.Cells);
        _device.BindStorageBuffer(4, _bindings.Lookup.Pages);

        _device.BindImage(0, _cacheRed, ImageAccess.ReadWrite);
        _device.BindImage(1, _cacheGreen, ImageAccess.ReadWrite);
        _device.BindImage(2, _cacheBlue, ImageAccess.ReadWrite);
        _device.BindImage(3, _cacheDistance, ImageAccess.ReadWrite);

        _device.SetTexture(1, _device.CardAlbedo);
        _device.SetTexture(4, _device.CardDepth);
        _device.SetTexture(6, _final);

        _device.SetComputeUniform(_cacheUpdate, "uSkyColour", skyColour);
        _device.SetComputeUniform(_cacheUpdate, "uBounceScale", SurfaceCacheCVars.GiBounce.Value);
        _bindings.WriteSkyHarmonics(_cacheUpdate);
        _device.SetComputeUniform(_cacheUpdate, "uAtlasSize", new Vector3f(_width, _height, 0f));
        _device.SetComputeUniform(_cacheUpdate, "uLookupBuckets", SurfaceCacheLookup.Buckets);
        _device.SetComputeUniform(_cacheUpdate, "uLookupMin", _bindings.Lookup.Minimum);
        _device.SetComputeUniform(_cacheUpdate, "uLookupCell", SurfaceCacheLookup.CellMetres);

        _device.SetComputeUniform(_cacheUpdate, "uCacheOrigin", _cacheOriginRelative);
        _device.SetComputeUniform(_cacheUpdate, "uCacheSpacing", _cacheSpacing);
        _device.SetComputeUniform(
            _cacheUpdate, "uCacheCount", new Vector3f(_cacheCountX, _cacheCountY, _cacheCountZ));
        _device.SetComputeUniform(_cacheUpdate, "uSlabBegin", slabBegin);
        _device.SetComputeUniform(_cacheUpdate, "uSlabEnd", slabEnd);
        _device.SetComputeUniform(_cacheUpdate, "uAlpha", alpha);
        _device.SetComputeUniform(_cacheUpdate, "uRayCount", CacheRays);
        _device.SetComputeUniform(_cacheUpdate, "uSweep", _cacheSweeps);
        _device.SetComputeUniform(_cacheUpdate, "uCacheScale", MathF.Max(scale, 0f));
        _device.SetComputeUniform(_cacheUpdate, "uSkyReject", Math.Clamp(skyReject, 0f, 1f));

        _bindings.BindShadows(_cacheUpdate, shadows);

        _device.Dispatch(
            _cacheUpdate,
            (_cacheCountX + 3) / 4,
            (_cacheCountY + 3) / 4,
            ((slabEnd - slabBegin) + 3) / 4);

        _device.MemoryBarrier(MemoryBarrierScope.Image | MemoryBarrierScope.TextureFetch);

        // Advance the slab cursor; a completed wrap counts a sweep, which is what turns the seeding
        // overwrite into a running average once every probe has been written at least once.
        if (slabEnd >= _cacheCountZ)
        {
            _cacheSlab = 0;
            _cacheSweeps = Math.Min(_cacheSweeps + 1, 1_000_000);
        }
        else
        {
            _cacheSlab = slabEnd;
        }
    }

    /// <summary>Drops the radiance cache's volumes, for a level change or shutdown.</summary>
    internal void ReleaseRadianceCache()
    {
        if (_cacheRed.IsValid)
        {
            _device.DestroyTexture(_cacheRed);
        }

        if (_cacheGreen.IsValid)
        {
            _device.DestroyTexture(_cacheGreen);
        }

        if (_cacheBlue.IsValid)
        {
            _device.DestroyTexture(_cacheBlue);
        }

        if (_cacheDistance.IsValid)
        {
            _device.DestroyTexture(_cacheDistance);
        }

        _cacheRed = default;
        _cacheGreen = default;
        _cacheBlue = default;
        _cacheDistance = default;
        _cacheConfigured = false;
        _cacheSlab = 0;
        _cacheSweeps = 0;
    }

    /// <summary>Drops the update program after a trace-mode switch, so the next update rebuilds it.</summary>
    internal void DropShader()
    {
        if (_cacheUpdate.IsValid)
        {
            _device.DestroyComputeShader(_cacheUpdate);
            _cacheUpdate = ComputeShaderHandle.None;
        }
    }

    /// <summary>Destroys the update program and the volumes, as disposing the pass does.</summary>
    internal void Destroy()
    {
        if (_cacheUpdate.IsValid)
        {
            _device.DestroyComputeShader(_cacheUpdate);
        }

        ReleaseRadianceCache();
    }
}
