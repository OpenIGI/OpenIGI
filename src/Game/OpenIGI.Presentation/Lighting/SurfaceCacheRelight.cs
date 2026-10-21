using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using OpenIGI.Shaders;

namespace OpenIGI.Presentation.Lighting;

/// <summary>
/// The card relight: the one program that lights every page directly, gathers its bounce and folds the
/// two into the atlas a trace reads, dispatched a tile per work group over the whole cache, a slice of
/// it, or the compacted list of tiles near the camera.
/// </summary>
/// <remarks>
/// Owns the program, the tile cursors that spread the steady state over frames, and the active tile
/// list. <see cref="SurfaceCacheLighting"/> exposes it and decides when each pass runs.
/// </remarks>
internal sealed class SurfaceCacheRelight
{
    /// <summary>How far the camera may drift from where the active list was built before a rebuild.</summary>
    /// <remarks>
    /// The list is built against <see cref="ActiveRadius"/> plus this, so every page the shader's exact
    /// test could accept stays on the list until the centre has moved further than the slack -- the
    /// superset guarantee that makes the list invisible in the image.
    /// </remarks>
    private const float ActiveRebuildMargin = 4f;

    /// <summary>The fewest tiles a steady-state slice dispatches, when there is more than a slice left.</summary>
    /// <remarks>
    /// An even split of the active list can be a very small dispatch -- a few hundred work groups at an
    /// outdoor spawn -- and this shader's cost is ray latency, which a small dispatch cannot hide: per
    /// tile it measured about four times slower at four hundred groups than at three thousand. So a
    /// slice is never smaller than this; when the active list is short, the round finishes its coverage
    /// in the first few frames at full occupancy and the remaining frames dispatch nothing at all,
    /// rather than every frame paying the latency of an underfilled machine. Refresh cadence is
    /// unchanged either way: one full pass over the active set per <see cref="BounceFrameSpread"/>.
    /// <para>
    /// 1024 rather than the 4096 it was: at 4096 a round's bounce landed on a handful of frames at up to
    /// 6.5 ms each at 1440p ultra and idled the rest, which was the largest single cause of the 1% low.
    /// At 1024 the peak is about 2 ms for some 0.2 ms more on average; at 512 the latency cost began to
    /// show in the average (the bounce doubled). The image is the same at any value.
    /// </para>
    /// </remarks>
    internal const int MinSliceGroups = 1024;

    private readonly IGraphicsDevice _device;
    private readonly SurfaceCacheBindings _bindings;
    private readonly SurfaceCacheLocalLights _localLights;
    private readonly Action _ensureTraceMode;
    private readonly TextureHandle _direct;
    private readonly TextureHandle _indirect;
    private readonly TextureHandle _final;
    private readonly int _width;
    private readonly int _height;

    // Which slice of the cache the next steady-state bounce relights. Walks the active tile list (or
    // the whole set, when there is none) over BounceFrameSpread frames and wraps.
    private int _bounceCursor;

    // The same, for the direct relight. The sun is fixed, so a card's direct term only changes as the
    // clipmap scrolls new coverage under it; tracing a slice a frame keeps up with that without the
    // whole-cache relight that a clipmap rebuild used to force in one frame.
    private int _lightCursor;

    // The compacted active tile list the steady-state slices walk. Without it a slice's cost depended
    // on where the cursor stood: the pages inside ActiveRadius sit together in the tile order, so the
    // one slice per round that crossed them carried nearly all of the round's unculled work in a single
    // frame -- a several-millisecond spike, once per BounceFrameSpread frames, that was most of what
    // dragged the 1% low. Walking only the active tiles gives every frame an even share. The list is a
    // superset built with ActiveRebuildMargin of slack; the shader's exact radius test still decides
    // every tile, so the lighting is identical to walking the whole array.
    private readonly int[] _tilePages;
    private readonly float[] _pageSpheres;
    private readonly StorageBufferHandle _activeTiles;
    private readonly int[] _activeScratch;
    private readonly bool[] _pageActive;
    private int _activeCount = -1;
    private bool _activeBuilt;
    private Vector3f _activeBuiltCentre;
    private float _activeBuiltRadius;

    // How far apart the tiles of the dispatch being recorded stand; see uTileStride. One except inside a
    // whole-cache slice.
    private int _tileStride = 1;

    // Not readonly: the trace mode is live-switchable, and a flipped mode recompiles this and every
    // other pass that traces.
    private ComputeShaderHandle _shader;

    private readonly StorageBufferHandle _pages;
    private readonly StorageBufferHandle _tiles;

    // Each probe's world position, one entry per probe rather than per texel, so the combine can weight a
    // probe by how far it lies off the plane of the texel it is lighting. A storage buffer rather than a
    // texture because the position needs full float precision -- a half would resolve a probe six hundred
    // metres from the origin no finer than half a metre, and the plane test wants centimetres.
    private readonly StorageBufferHandle _probePositions;
    private readonly int _probeWidth;
    private readonly int _probeHeight;

    private readonly int _tileCount;

    /// <summary>Initializes the relight over everything the build allocated for it.</summary>
    /// <param name="device">The device to dispatch on.</param>
    /// <param name="bindings">The bindings every dispatch shares.</param>
    /// <param name="localLights">The level's local lights.</param>
    /// <param name="ensureTraceMode">Brings every tracing pass up to the current trace mode.</param>
    /// <param name="shader">The relight program as built.</param>
    /// <param name="pages">The page records.</param>
    /// <param name="tiles">The tiles the pages are cut into.</param>
    /// <param name="probePositions">Each probe's world position.</param>
    /// <param name="activeTiles">The compacted active tile list.</param>
    /// <param name="tilePages">Which page each tile belongs to.</param>
    /// <param name="pageSpheres">Each page's world bounding sphere.</param>
    /// <param name="probeWidth">The probe grid's width.</param>
    /// <param name="probeHeight">The probe grid's height.</param>
    /// <param name="tileCount">How many tiles there are.</param>
    /// <param name="direct">The direct lighting atlas.</param>
    /// <param name="indirect">The indirect lighting atlas.</param>
    /// <param name="final">The final lighting atlas.</param>
    /// <param name="width">The atlas width in texels.</param>
    /// <param name="height">The atlas height in texels.</param>
    internal SurfaceCacheRelight(
        IGraphicsDevice device,
        SurfaceCacheBindings bindings,
        SurfaceCacheLocalLights localLights,
        Action ensureTraceMode,
        ComputeShaderHandle shader,
        StorageBufferHandle pages,
        StorageBufferHandle tiles,
        StorageBufferHandle probePositions,
        StorageBufferHandle activeTiles,
        int[] tilePages,
        float[] pageSpheres,
        int probeWidth,
        int probeHeight,
        int tileCount,
        TextureHandle direct,
        TextureHandle indirect,
        TextureHandle final,
        int width,
        int height)
    {
        _device = device;
        _bindings = bindings;
        _localLights = localLights;
        _ensureTraceMode = ensureTraceMode;
        _shader = shader;
        _pages = pages;
        _tiles = tiles;
        _probePositions = probePositions;
        _activeTiles = activeTiles;
        _tilePages = tilePages;
        _pageSpheres = pageSpheres;
        _activeScratch = new int[tileCount];
        _pageActive = new bool[pageSpheres.Length / 4];
        _probeWidth = probeWidth;
        _probeHeight = probeHeight;
        _tileCount = tileCount;
        _direct = direct;
        _indirect = indirect;
        _final = final;
        _width = width;
        _height = height;
    }

    /// <summary>Gets or sets how many frames the temporal accumulation converges over.</summary>
    internal int TemporalFrames { get; set; } = 8;

    /// <summary>Gets or sets how many frames a steady-state bounce spreads the cache's tiles across.</summary>
    internal int BounceFrameSpread { get; set; } = 12;

    /// <summary>Gets or sets where the steady-state refresh is centred.</summary>
    internal Vector3f ActiveCentre { get; set; }

    /// <summary>Gets or sets how far from the centre the steady-state refresh reaches, in metres.</summary>
    internal float ActiveRadius { get; set; }

    /// <summary>Gets or sets whether the whole-cache slices recorded now belong to the load-time convergence.</summary>
    internal bool Converging { get; set; }

    /// <summary>Gets or sets the fewest tiles a steady-state slice dispatches.</summary>
    internal int MinSliceTiles { get; set; } = MinSliceGroups;

    /// <summary>Lights every page, or one amortised slice of them, from the sun and the sky.</summary>
    internal void Light(
        Vector3f sunDirection,
        Vector3f sunColour,
        Vector3f skyColour,
        GlobalDistanceField? shadows,
        int probe,
        float? localScale,
        bool amortize)
    {
        // Full-cache in one dispatch by default (a dump, or the load-time seed); a slice a frame when
        // amortised, so a clipmap scroll's new shadows arrive over a few frames rather than in one stall.
        // A steady-state slice walks the compacted active list rather than the whole tile array -- see
        // _activeTiles for why a slice over everything spiked once per round.
        int lightOffset = 0;
        int lightTiles = _tileCount;
        bool lightList = false;
        if (amortize)
        {
            EnsureActiveTiles();
            lightList = _activeCount >= 0;
            int domain = lightList ? _activeCount : _tileCount;
            int spread = Math.Max(1, BounceFrameSpread);
            int slice = Math.Max(1, (domain + spread - 1) / spread);
            if (lightList)
            {
                slice = Math.Max(slice, Math.Max(1, MinSliceTiles));
            }

            int phase = _lightCursor % spread;
            _lightCursor = (phase + 1) % spread;
            lightOffset = phase * slice;
            if (lightOffset >= domain)
            {
                // This round's coverage is already complete -- see MinSliceGroups. Nothing to trace, and
                // dispatching nothing is the point.
                return;
            }

            lightTiles = Math.Min(slice, domain - lightOffset);
        }

        _device.SetComputeTimingLabel("cards.near.relight");
        LightTiles(
            sunDirection, sunColour, skyColour, shadows, probe, localScale,
            lightOffset, lightTiles, lightList, cull: amortize);
        _device.SetComputeTimingLabel(null);
    }

    /// <summary>Relights one interleaved slice of the whole cache with shadows.</summary>
    internal void ConvergeLightSlice(
        Vector3f sunDirection,
        Vector3f sunColour,
        Vector3f skyColour,
        GlobalDistanceField? shadows,
        int slice,
        int sliceCount,
        float? localScale)
    {
        if (!SliceRange(slice, sliceCount, out int offset, out int tiles))
        {
            return;
        }

        _tileStride = Math.Max(1, sliceCount);
        _device.SetComputeTimingLabel(Converging ? "cards.converge.relight" : "cards.far.relight");
        LightTiles(
            sunDirection, sunColour, skyColour, shadows, probe: 0, localScale,
            offset, tiles, useActiveList: false, cull: false);
        _device.SetComputeTimingLabel(null);
        _tileStride = 1;
    }

    /// <summary>Seeds one slice of the final atlas from the direct term.</summary>
    internal void ConvergeSeedSlice(GlobalDistanceField? shadows, float boost, int slice, int sliceCount)
    {
        if (shadows is null || _bindings.Lookup is null || !SliceRange(slice, sliceCount, out int offset, out int tiles))
        {
            return;
        }

        float radius = ActiveRadius;
        ActiveRadius = 0f;
        _tileStride = Math.Max(1, sliceCount);
        try
        {
            Dispatch(3, _direct, _final, shadows, boost, 0, offset, tiles, useActiveList: false);
        }
        finally
        {
            _tileStride = 1;
            ActiveRadius = radius;
        }
    }

    /// <summary>Folds one more bounce into one slice of the cache.</summary>
    internal void ConvergeBounceSlice(GlobalDistanceField? shadows, float boost, int pass, int slice, int sliceCount)
    {
        if (shadows is null || _bindings.Lookup is null || !SliceRange(slice, sliceCount, out int offset, out int tiles))
        {
            return;
        }

        float radius = ActiveRadius;
        ActiveRadius = 0f;
        _tileStride = Math.Max(1, sliceCount);
        try
        {
            _device.SetComputeTimingLabel(Converging ? "cards.converge.bounce" : "cards.far.bounce");
            Dispatch(1, _final, _indirect, shadows, boost, Math.Max(pass, 0), offset, tiles, useActiveList: false);
            Dispatch(2, _direct, _final, shadows, boost, 0, offset, tiles, useActiveList: false);
        }
        finally
        {
            _device.SetComputeTimingLabel(null);
            _tileStride = 1;
            ActiveRadius = radius;
        }
    }

    /// <summary>Gets the tiles one slice of an evenly divided cache covers: every <paramref name="sliceCount"/>th.</summary>
    /// <returns><see langword="false"/> when the slice is past the end and there is nothing to dispatch.</returns>
    /// <remarks>
    /// Interleaved rather than contiguous, with <see cref="_tileStride"/> set to the slice count by the caller:
    /// tiles are laid out page by page, so a contiguous slice is one part of the level, and its cost was that
    /// part's -- a slice over a dense interior took several times the frame time of one over open ground,
    /// which is a spike once every sweep. Interleaved, every slice samples the whole level alike. The union
    /// over all the slices is still every tile exactly once.
    /// </remarks>
    private bool SliceRange(int slice, int sliceCount, out int offset, out int tiles)
    {
        int count = Math.Max(1, sliceCount);
        offset = Math.Clamp(slice, 0, count - 1);
        tiles = offset < _tileCount ? ((_tileCount - offset) + count - 1) / count : 0;
        return tiles > 0;
    }

    /// <summary>The direct relight over a range of tiles: the one dispatch every route into it shares.</summary>
    private void LightTiles(
        Vector3f sunDirection,
        Vector3f sunColour,
        Vector3f skyColour,
        GlobalDistanceField? shadows,
        int probe,
        float? localScale,
        int lightOffset,
        int lightTiles,
        bool useActiveList,
        bool cull)
    {
        _bindings.LastSunDirection = sunDirection;
        _bindings.LastSunColour = sunColour;
        EnsureLightShader();

        _device.BindStorageBuffer(0, _pages);
        _device.BindStorageBuffer(1, _tiles);
        _device.BindStorageBuffer(2, _localLights.Buffer);
        _localLights.BindLampMaps();
        _device.BindImage(0, _direct, ImageAccess.Write);

        _device.SetTexture(1, _device.CardAlbedo);
        _device.SetTexture(2, _device.CardNormal);
        _device.SetTexture(3, _device.CardMaterial);
        _device.SetTexture(4, _device.CardDepth);

        _device.SetComputeUniform(_shader, "uTileCount", _tileCount);

        // The uniforms persist on the program between calls, so every one is set every time -- a stale
        // one left by the bounce's slice would otherwise make this pass skip a band of tiles.
        _device.SetComputeUniform(_shader, "uUseActiveList", useActiveList ? 1 : 0);
        _device.SetComputeUniform(_shader, "uActiveCount", useActiveList ? _activeCount : 0);
        _device.BindStorageBuffer(6, _activeTiles);

        // Only the amortised pass culls. The seed relights the whole cache exactly as it always did, so
        // the far half of the level is lit once and keeps that; and the deterministic dump, which is also
        // unamortised, stays byte-for-byte what it was.
        _device.SetComputeUniform(_shader, "uActiveCentre", ActiveCentre);
        _device.SetComputeUniform(_shader, "uActiveRadius", cull ? MathF.Max(ActiveRadius, 0f) : 0f);

        _device.SetComputeUniform(_shader, "uSunDirection", sunDirection);
        _device.SetComputeUniform(_shader, "uSunColour", sunColour);
        _device.SetComputeUniform(_shader, "uSkyColour", skyColour);
        _bindings.WriteSkyHarmonics(_shader);
        _device.SetComputeUniform(_shader, "uProbe", probe);
        _device.SetComputeUniform(_shader, "uMode", 0);
        _device.SetComputeUniform(_shader, "uAtlasSize", new Vector3f(_width, _height, 0f));
        _device.SetComputeUniform(_shader, "uLightCount", _localLights.Count);
        _device.SetComputeUniform(_shader, "uLocalScale", localScale ?? SurfaceCacheLighting.DeferredLampScale);

        _device.BindStorageBuffer(3, _bindings.Lookup?.Cells ?? _pages);
        _device.BindStorageBuffer(4, _bindings.Lookup?.Pages ?? _tiles);

        // Direct lighting never reads the probes, but the shader source is shared across every mode and
        // so declares them. GL lets an unused buffer go unbound; a descriptor set has no such notion, and
        // an unwritten binding is a fault whether or not the shader reaches it.
        _device.BindStorageBuffer(5, _probePositions);
        _device.SetComputeUniform(_shader, "uLookupBuckets", _bindings.Lookup is null ? 0 : SurfaceCacheLookup.Buckets);

        if (_bindings.Lookup is { } index)
        {
            _device.SetComputeUniform(_shader, "uLookupMin", index.Minimum);
            _device.SetComputeUniform(_shader, "uLookupCell", SurfaceCacheLookup.CellMetres);
        }

        _bindings.BindShadows(_shader, shadows);

        DispatchTiles(lightOffset, lightTiles);
        _device.MemoryBarrier(MemoryBarrierScope.Image | MemoryBarrierScope.TextureFetch);
    }

    /// <summary>Runs the lighting program over a range of tiles, one workgroup a tile.</summary>
    /// <param name="offset">The first tile, or the first slot of the active list.</param>
    /// <param name="count">How many.</param>
    /// <remarks>
    /// In slices no wider than the device takes along x, moving the program's tile offset along: a level's
    /// atlas runs to a quarter of a million tiles, and WebGPU refuses a dispatch past 65535 workgroups. Every
    /// tile is its own work, so the slices light exactly what one dispatch would.
    /// </remarks>
    private void DispatchTiles(int offset, int count)
    {
        int most = Math.Max(_device.Capabilities.MaxComputeGroupsX, 1);

        int stride = Math.Max(1, _tileStride);
        _device.SetComputeUniform(_shader, "uTileStride", stride);

        for (int done = 0; done < count; done += most)
        {
            _device.SetComputeUniform(_shader, "uTileOffset", offset + (done * stride));
            _device.Dispatch(_shader, Math.Min(most, count - done));
        }
    }

    /// <summary>Gathers the bounce, then folds it into the atlas a trace should read.</summary>
    internal void Bounce(GlobalDistanceField? shadows, float boost, int iterations, int temporalIndex)
    {
        if (shadows is null || _bindings.Lookup is null)
        {
            return;
        }

        if (temporalIndex <= 0)
        {
            // The first pass, and every dump. Seed the final atlas with the direct term's outgoing
            // radiance so the first gather reads what a surface sends rather than the raw irradiance that
            // reached it, then converge in place with no history.
            Dispatch(3, _direct, _final, shadows, boost);

            for (int pass = 0; pass < Math.Max(iterations, 1); pass++)
            {
                Dispatch(1, _final, _indirect, shadows, boost);
                Dispatch(2, _direct, _final, shadows, boost);
            }

            return;
        }

        // Steady state: one gather that reads last frame's final, accumulated onto the running estimate,
        // then folded back into the final. No re-seed -- the final already carries the bounce. Only a slice
        // of the cache is traced each frame (see BounceFrameSpread): the gather and the combine work the
        // same slice so the two stay coherent, and the cursor walks the whole set over several frames. A
        // tile outside this frame's slice keeps the estimate it last converged to, which for static
        // geometry under a fixed sun is the answer it would trace again anyway.
        // Over the compacted active list, as the relight is -- see _activeTiles for why a slice over the
        // whole array spiked once per round, and MinSliceGroups for why a slice is never small.
        EnsureActiveTiles();
        bool useList = _activeCount >= 0;
        int domain = useList ? _activeCount : _tileCount;
        int spread = Math.Max(1, BounceFrameSpread);
        int slice = Math.Max(1, (domain + spread - 1) / spread);
        if (useList)
        {
            slice = Math.Max(slice, Math.Max(1, MinSliceTiles));
        }

        // Staggered half a round from the direct relight. Both amortised passes cover their active slices
        // over the first ceil(domain/slice) phases of the round and idle the rest -- and left in step (both
        // cursors advance one per frame from the same start) every relight slice AND every bounce slice land
        // on the same handful of frames, which is the recurring GI spike that drags the 1% low. Advancing
        // the bounce's own cursor as before but reading its phase half a spread ahead lands the bounce's
        // heavy frames inside the relight's idle frames. Every phase 0..spread-1 is still visited exactly
        // once per round, so coverage and the once-per-spread refresh cadence are unchanged -- only which
        // frame each slice falls on moves.
        int rawPhase = _bounceCursor % spread;
        _bounceCursor = (rawPhase + 1) % spread;
        int phase = (rawPhase + (spread / 2)) % spread;
        int offset = phase * slice;
        if (offset >= domain)
        {
            return;
        }

        int groups = Math.Min(slice, domain - offset);
        _device.SetComputeTimingLabel("cards.near.bounce");
        Dispatch(1, _final, _indirect, shadows, boost, temporalIndex, offset, groups, useList, spread);
        Dispatch(2, _direct, _final, shadows, boost, 0, offset, groups, useList);
        _device.SetComputeTimingLabel(null);
    }

    private void Dispatch(
        int mode,
        TextureHandle previous,
        TextureHandle target,
        GlobalDistanceField? shadows,
        float boost,
        int temporalIndex = 0,
        int tileOffset = 0,
        int dispatchTiles = -1,
        bool useActiveList = false,
        int jitterStride = 1)
    {
        // As the direct relight does. The bounce shares the relight's program, and a mid-frame r_rt toggle
        // drops it (see EnsureTraceMode) -- so the gather cannot assume the relight already rebuilt it.
        EnsureLightShader();

        _device.BindStorageBuffer(0, _pages);
        _device.BindStorageBuffer(1, _tiles);
        _device.BindStorageBuffer(2, _localLights.Buffer);
        _localLights.BindLampMaps();
        _device.BindStorageBuffer(3, _bindings.Lookup?.Cells ?? _pages);
        _device.BindStorageBuffer(4, _bindings.Lookup?.Pages ?? _tiles);
        _device.BindStorageBuffer(5, _probePositions);

        // Read-write, because the gather's temporal accumulation reads a probe's previous value back
        // before blending this frame's onto it.
        _device.BindImage(0, target, ImageAccess.ReadWrite);

        _device.SetTexture(1, _device.CardAlbedo);
        _device.SetTexture(2, _device.CardNormal);
        _device.SetTexture(3, _device.CardMaterial);
        _device.SetTexture(4, _device.CardDepth);
        _device.SetTexture(6, previous);
        _device.SetTexture(7, _indirect);

        _device.SetComputeUniform(_shader, "uProbeWidth", _probeWidth);
        _device.SetComputeUniform(_shader, "uProbeHeight", _probeHeight);
        _device.SetComputeUniform(_shader, "uTemporalIndex", temporalIndex);
        _device.SetComputeUniform(_shader, "uMaxFrames", TemporalFrames);
        _device.SetComputeUniform(_shader, "uJitterStride", Math.Max(jitterStride, 1));

        _device.SetComputeUniform(_shader, "uTileCount", _tileCount);
        _device.SetComputeUniform(_shader, "uUseActiveList", useActiveList ? 1 : 0);
        _device.SetComputeUniform(_shader, "uActiveCount", useActiveList ? _activeCount : 0);
        _device.BindStorageBuffer(6, _activeTiles);

        // As the direct relight: only a sliced, steady-state pass culls. A full dispatch is the seed or a
        // dump, and both must cover the level.
        _device.SetComputeUniform(_shader, "uActiveCentre", ActiveCentre);
        _device.SetComputeUniform(
            _shader, "uActiveRadius", dispatchTiles >= 0 ? MathF.Max(ActiveRadius, 0f) : 0f);

        _device.SetComputeUniform(_shader, "uMode", mode);
        _device.SetComputeUniform(_shader, "uBoost", boost);
        _device.SetComputeUniform(_shader, "uProbe", 0);
        _device.SetComputeUniform(_shader, "uAtlasSize", new Vector3f(_width, _height, 0f));
        _device.SetComputeUniform(_shader, "uLightCount", 0);
        _device.SetComputeUniform(
            _shader, "uLookupBuckets", _bindings.Lookup is null ? 0 : SurfaceCacheLookup.Buckets);

        if (_bindings.Lookup is { } index)
        {
            _device.SetComputeUniform(_shader, "uLookupMin", index.Minimum);
            _device.SetComputeUniform(_shader, "uLookupCell", SurfaceCacheLookup.CellMetres);
        }

        _bindings.BindShadows(_shader, shadows);

        DispatchTiles(tileOffset, dispatchTiles < 0 ? _tileCount : Math.Max(dispatchTiles, 0));

        // The storage buffer scope as well, because the gather writes each probe's position through it
        // and the combine that follows reads its neighbours' -- not just the images the split is usually
        // about.
        _device.MemoryBarrier(
            MemoryBarrierScope.Image | MemoryBarrierScope.TextureFetch | MemoryBarrierScope.StorageBuffer);
    }

    /// <summary>Keeps the compacted active tile list agreeing with <see cref="ActiveCentre"/> and <see cref="ActiveRadius"/>.</summary>
    /// <remarks>
    /// Rebuilt only when the centre has drifted past <see cref="ActiveRebuildMargin"/> or the radius
    /// changed, because the list is built against the radius plus that margin: until the centre leaves
    /// the slack, the list still holds every page the shader's exact test could accept. The test here is
    /// the shader's own -- the page bounding sphere against centre and radius -- so the list is a
    /// superset of what the GPU keeps, never a filter on it.
    /// </remarks>
    private void EnsureActiveTiles()
    {
        if (ActiveRadius <= 0f || _tileCount <= 0 || _pageActive.Length == 0)
        {
            _activeCount = -1;
            _activeBuilt = false;
            return;
        }

        Vector3f centre = ActiveCentre;
        if (_activeBuilt && _activeBuiltRadius == ActiveRadius)
        {
            float dx = centre.X - _activeBuiltCentre.X;
            float dy = centre.Y - _activeBuiltCentre.Y;
            float dz = centre.Z - _activeBuiltCentre.Z;
            if ((dx * dx) + (dy * dy) + (dz * dz) <= ActiveRebuildMargin * ActiveRebuildMargin)
            {
                return;
            }
        }

        float reach = ActiveRadius + ActiveRebuildMargin;
        for (int p = 0; p < _pageActive.Length; p++)
        {
            int at = p * 4;
            float dx = _pageSpheres[at + 0] - centre.X;
            float dy = _pageSpheres[at + 1] - centre.Y;
            float dz = _pageSpheres[at + 2] - centre.Z;
            float limit = reach + _pageSpheres[at + 3];
            _pageActive[p] = (dx * dx) + (dy * dy) + (dz * dz) <= limit * limit;
        }

        int count = 0;
        for (int tile = 0; tile < _tileCount; tile++)
        {
            if (_pageActive[_tilePages[tile]])
            {
                _activeScratch[count++] = tile;
            }
        }

        if (count > 0)
        {
            _device.UpdateStorageBuffer<int>(_activeTiles, _activeScratch.AsSpan(0, count));
        }

        _activeCount = count;
        _activeBuilt = true;
        _activeBuiltCentre = centre;
        _activeBuiltRadius = ActiveRadius;
    }

    /// <summary>Brings the shared relight/gather program up to the current trace mode, building it if the
    /// last mode change dropped it.</summary>
    private void EnsureLightShader()
    {
        _ensureTraceMode();
        if (!_shader.IsValid)
        {
            _shader = _device.CreateComputeShader(
                ShaderModuleId.CardsLighting,
                new ShaderVariant(_bindings.TracesRays ? "rayquery" : "software"),
                "cards.light");
        }
    }

    /// <summary>Drops the program after a trace-mode switch, so the next dispatch rebuilds it.</summary>
    internal void DropShader()
    {
        if (_shader.IsValid)
        {
            _device.DestroyComputeShader(_shader);
            _shader = ComputeShaderHandle.None;
        }
    }

    /// <summary>Destroys the program, as disposing the pass does.</summary>
    internal void DestroyShader() => _device.DestroyComputeShader(_shader);
}
