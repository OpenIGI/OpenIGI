using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using OpenIGI.Shaders;

namespace OpenIGI.Presentation.Lighting;

/// <summary>
/// Lights every card page, so that a ray hitting a surface reads light rather than albedo.
/// </summary>
/// <remarks>
/// <para>
/// The captured pages hold unlit albedo — see <c>CardAtlasShaders</c> for why a shared card cannot
/// carry a placement's bake. This pass is what puts light into the cache: for every texel of every
/// placement's pages it works out where that surface stands in the world, what direction it faces, and
/// what reaches it.
/// </para>
/// <para>
/// <b>The reconstruction is the whole difficulty.</b> A lighting texel knows only its position in an
/// atlas. Getting back to a world position means: which page, therefore which card and which placement;
/// where in the card's rectangle; how far behind the card's plane the surface sits; and then through
/// the placement's transform. Every step of that can be subtly wrong in a way that still produces lit
/// surfaces — just in the wrong places — which looks like nothing at all until a later stage bounces
/// light off them.
/// </para>
/// <para>
/// <b>Work is dispatched per tile, not per page.</b> Pages vary in size, so one dispatch each would be
/// thousands of dispatches; one dispatch over the whole atlas would have every invocation search for
/// its page. Instead the pages are cut into fixed tiles once, on the CPU, and a work group takes one
/// tile — so an invocation knows its page from its group and the atlas's empty space costs nothing.
/// </para>
/// </remarks>
public sealed class SurfaceCacheLighting : IDisposable
{
    private const string LogCategory = "SurfaceCache";

    /// <summary>The square of texels one work group covers.</summary>
    private const int TileSize = 8;

    /// <summary>Gets or sets how many frames the temporal accumulation converges over.</summary>
    /// <remarks>
    /// The cap on the accumulation weight: the blend's alpha bottoms out at one over this, so the
    /// estimate keeps adapting to a moving sun or a shifting shadow rather than freezing, while still
    /// integrating far more hemisphere directions than the sixteen a single frame traces.
    ///
    /// This is the knob for the slow travelling ripple over interior surfaces -- the "heat haze" visible
    /// with the camera standing perfectly still. See <c>r_gi_temporal_frames</c>, which owns the reasoning
    /// and the measurements; settable here, like <see cref="BounceFrameSpread"/>, so the trade between a
    /// steady still and a responsive scrubbed sun can be dialled without a rebuild. The default is the
    /// value the constant held before the cvar existed, so a caller that never sets it is unchanged.
    /// </remarks>
    public int TemporalFrames { get; set; } = 8;

    /// <summary>How many frames a steady-state bounce spreads the cache's tiles across.</summary>
    /// <remarks>
    /// The per-frame relight traces the whole surface cache against the distance field, which on a large
    /// level is the single most expensive pass in the frame -- measured at twenty-six milliseconds on
    /// mission 1, a frame all by itself. The geometry and the sun are static, so a tile re-traced this
    /// frame gives the same answer it would several frames from now: dividing the cache into this many
    /// slices and advancing one slice a frame cuts the per-frame cost by the same factor while a still
    /// scene still reaches the same equilibrium, only a few frames later. The temporal blend already
    /// carries a tile's estimate between its updates, so the seam between an updated slice and a stale one
    /// is not visible. The seed pass on a clipmap rebuild still relights every tile at once.
    ///
    /// Twelve, not six. This stage is world-space -- its cost is the same at any output resolution -- so on
    /// a slower GPU or a high-resolution frame it is the single biggest item in the budget (twelve
    /// milliseconds at 1440p on one machine); halving the per-frame slice roughly halves it. A converged
    /// still is identical either way (measured: 0.7% mean pixel difference against a six-slice frame); the
    /// only cost is that a tile the camera newly reveals reaches its multi-bounce answer over twelve frames
    /// rather than six -- a fifth of a second longer at sixty, under the temporal blend that hides the seam.
    ///
    /// Settable, not a constant, so the trade -- per-frame GPU cost against how quickly a newly revealed
    /// surface fills in -- can be dialled from a cvar without a rebuild. Higher is cheaper and lags more.
    /// </remarks>
    public int BounceFrameSpread { get; set; } = 12;

    /// <summary>Where the steady-state refresh is centred, relative to this cache's own origin.</summary>
    /// <remarks>The camera, in practice. Meaningless unless <see cref="ActiveRadius"/> is positive.</remarks>
    public Vector3f ActiveCentre { get; set; }

    /// <summary>How far from <see cref="ActiveCentre"/> the steady-state refresh reaches, in metres.</summary>
    /// <remarks>
    /// <para>
    /// Nought relights the whole level, which is what the load-time seed and the deterministic dump want
    /// and what every frame used to do. A positive radius is the steady-state case: the cursor still walks
    /// every tile, but a tile whose card lies beyond this returns before it reads a texel.
    /// </para>
    /// <para>
    /// <b>Why the whole level was ever wrong.</b> These maps are kilometres across and a player stands in
    /// a corner of one, so the great majority of the cache is somewhere they cannot see. Worse, the
    /// distance field the relight traces against is a clipmap that follows the camera and reaches a few
    /// hundred metres — beyond it there is no geometry to trace, so a far tile's relight cannot even
    /// produce a different answer from the one it already holds. It was spending real milliseconds
    /// recomputing what it had.
    /// </para>
    /// <para>
    /// Far cards are not left dark: the load-time seed lights the whole cache once with no radius, and
    /// they keep that until the camera brings them back inside. What they lose is only the tracking of
    /// changes — a lamp switched two hundred metres away does not relight the wall beside it until the
    /// player walks over, which is the trade this exists to make.
    /// </para>
    /// </remarks>
    public float ActiveRadius { get; set; }

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
    private const int MinSliceGroups = 1024;

    // How far apart the tiles of the dispatch being recorded stand; see uTileStride. One except inside a
    // whole-cache slice.
    private int _tileStride = 1;

    /// <summary>Gets or sets whether the whole-cache slices recorded now belong to the load-time convergence.</summary>
    /// <remarks>Only how they are named in the timings; the convergence and the far refresh share the slices.</remarks>
    public bool Converging { get; set; }

    /// <summary>Gets or sets the fewest tiles a steady-state slice dispatches; see <see cref="MinSliceGroups"/>.</summary>
    public int MinSliceTiles { get; set; } = MinSliceGroups;


    /// <summary>How many texels lie between probes each way.</summary>
    /// <remarks>
    /// Must match the shader's <c>kProbeMask</c>, which is this less one: the mask picks out a probe as
    /// the texels whose low bits are clear, so a spacing of four is a mask of three.
    /// </remarks>
    private const int ProbeSpacing = 4;

    /// <summary>How many floats one local light occupies. Two <c>vec4</c>s.</summary>
    private const int FloatsPerLight = 12;

    /// <summary>How many floats one page's record occupies. Ten <c>vec4</c>s: nine of card and placement,
    /// then a world-space bounding sphere the card lookup rejects candidates against.</summary>
    private const int FloatsPerPage = 40;

    /// <summary>How far past a card's own depth a point may sit and still be taken as on it.</summary>
    /// <remarks>Matches the trace shader's <c>kCardSkin</c>; the bounding sphere has to allow for it.</remarks>
    private const float CardSkin = 0.25f;

    private readonly IGraphicsDevice _device;
    private readonly IOpenIgiLogger _logger;

    // Not readonly: the trace mode is live-switchable, and a flipped mode recompiles this and every
    // other pass that traces.
    private ComputeShaderHandle _shader;

    // Which trace tier the compiled shaders carry: the hardware ray query, or the software distance
    // field. Tracked so a live toggle of r_rt recompiles them instead of being silently ignored.
    private bool _tracesRays;

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

    // The screen probe gather's shaders, compiled on first use rather than at construction: a level that
    // never turns the on-screen gather on never pays for it. The plain one produces a raw estimate for
    // the diagnostic dump; the temporal one reprojects and accumulates it for the live frame.
    private ComputeShaderHandle _screenGather;
    private ComputeShaderHandle _screenGatherTemporal;

    // The one-ray-a-pixel form DLSS ray reconstruction denoises; traced only, so it is never built for the march.
    private ComputeShaderHandle _pixelGather;
    private ComputeShaderHandle _screenGatherFilter;
    private ComputeShaderHandle _screenResolve;

    // The world-space radiance cache: a grid of probes that trace the surface cache every frame and hold
    // the indirect irradiance in the same L1-SH layout the baked probe field uses, so the deferred pass
    // samples it unchanged. Unlike the screen gather -- which re-estimates at a MOVING screen-space grid
    // and reprojects, and so shimmers under motion -- these probes are anchored to world cells and only
    // ever accumulate onto themselves, which is what makes the indirect stable while the sun and dynamic
    // surfaces still feed it live. Compiled and allocated on first configure, not at construction, so a
    // preset that never turns it on never pays for it.
    private ComputeShaderHandle _cacheUpdate;
    // The terrain's height raster for the untraced gather (see TerrainHeightRaster), and how to read it.
    private TextureHandle _terrainHeights;
    private (float X, float Y, float Z, float W) _terrainMap0;
    private (float X, float Y, float Z, float W) _terrainMap1;

    // The roof raster, which tells a marched ray that went through a wall (see SetRoofs).
    private TextureHandle _roofs;
    private ShelterMapTransform _roofTransform = ShelterMapTransform.None;

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

    private StorageBufferHandle _lights;
    private SurfaceCacheLookup? _lookup;
    private int _lightCount;

    // The lamps' coarse maps, where the device draws them: every map's projection from this pass's origin,
    // and the device's count of map changes they were written at. See SetLampMapEntries.
    private StorageBufferHandle _lampMapRecords;
    private float[]? _lampMapFloats;
    private int _lampMapVersion = -1;

    // The fixtures as they were handed over, and the buffer contents built from them, kept so that a lamp
    // being switched can rewrite one float per light rather than rebuild the whole buffer.
    private IReadOnlyList<LightFixture>? _fixtures;
    private float[]? _packed;
    private readonly Vector3d _origin;
    private bool _disposed;

    // What a page that moves needs to be written again: every page's record as uploaded, the cards they
    // project, and which instance each placement became. A mover's pages carry their placement's CURRENT
    // transform, as a rigid body's cards do: what was captured is in the model's own space, so moving the
    // record moves the surface with it and nothing is recaptured.
    private readonly float[] _pageRecords;
    private readonly MeshCard[] _cards;
    private readonly SurfaceCacheInstances _instances;
    private readonly Dictionary<WorldPlacement, int> _instanceOf = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<int> _movingInstances = [];
    private readonly List<int> _movingPages = [];

    // The sun the cache was last relit under, for what the screen gather's escaped rays meet. See GroundSun.
    private Vector3f _lastSunDirection;
    private Vector3f _lastSunColour;

    // Each moving placement's run of _movingPages and its bounding sphere, as the lookup lists them; the
    // spheres follow the placements every frame one of them moves.
    private readonly List<SurfaceCacheLookup.MoverObject> _movers = [];
    private bool _moversMoved;
    private int _dirtyLow = int.MaxValue;
    private int _dirtyHigh = -1;
    private bool _moversChanged;

    private SurfaceCacheLighting(
        IGraphicsDevice device,
        IOpenIgiLogger logger,
        TextureHandle direct,
        TextureHandle indirect,
        TextureHandle final,
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
        int width,
        int height,
        Vector3d origin,
        float[] pageRecords,
        MeshCard[] cards,
        SurfaceCacheInstances instances)
    {
        _device = device;
        _pageRecords = pageRecords;
        _cards = cards;
        _instances = instances;
        for (int i = 0; i < instances.Instances.Length; i++)
        {
            if (instances.Instances[i].Placement is { } placement)
            {
                _instanceOf[placement] = i;
            }
        }

        _logger = logger;
        _shader = shader;
        _tracesRays = device.Capabilities.SupportsRayQuery && RenderCVars.HardwareRayTracing.Value;
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
        _origin = origin;

        DirectLighting = direct;
        IndirectLighting = indirect;
        FinalLighting = final;
        Width = width;
        Height = height;
    }

    /// <summary>Gets the atlas holding what reaches each surface directly.</summary>
    public TextureHandle DirectLighting { get; }

    /// <summary>Gets the atlas holding what reaches each surface by way of another surface.</summary>
    public TextureHandle IndirectLighting { get; }

    /// <summary>Gets the atlas a trace should read: direct light plus the bounce, times albedo.</summary>
    /// <remarks>
    /// The one every consumer wants. A ray that hits a surface needs the radiance leaving it, which is
    /// everything that arrived multiplied by what the surface does with it -- not the direct term
    /// alone, which is only part of the story and is not what the surface is actually sending.
    /// </remarks>
    public TextureHandle FinalLighting { get; }

    /// <summary>Gets the lighting atlas width in texels.</summary>
    public int Width { get; }

    /// <summary>Gets the lighting atlas height in texels.</summary>
    public int Height { get; }

    /// <summary>Gets the world point the pass's float positions are measured from.</summary>
    public Vector3d Origin => _origin;

    /// <summary>Describes the cache to a backend pass that has to resolve a traced hit against it.</summary>
    /// <param name="cameraToCache">Where the camera stands relative to <see cref="Origin"/>.</param>
    /// <param name="bounceScale">How much of a card's radiance a traced ray is to keep.</param>
    /// <returns>
    /// The handover, or <see cref="SurfaceCacheBinding.None"/> while the cache has no lookup grid — which
    /// is every frame between a mission starting to load and its cards being filed.
    /// </returns>
    /// <remarks>
    /// The engine's own passes bind these by unit and never need them named; a pass the backend owns has
    /// its own descriptor set and cannot. So this is the one place the cache's private resources are
    /// handed out, as a value rather than as loose handles, and nothing about how it is lit escapes with
    /// them. See <see cref="SurfaceCacheBinding"/> for what a reader does with it.
    /// </remarks>
    public SurfaceCacheBinding Describe(Vector3f cameraToCache, float bounceScale)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_lookup is not { } lookup)
        {
            return SurfaceCacheBinding.None;
        }

        return new SurfaceCacheBinding(
            _pages,
            lookup.Cells,
            lookup.Pages,
            _device.CardAlbedo,
            _device.CardDepth,
            FinalLighting,
            Width,
            Height,
            SurfaceCacheLookup.Buckets,
            lookup.Minimum,
            SurfaceCacheLookup.CellMetres,
            cameraToCache,
            bounceScale);
    }

    /// <summary>Builds the lighting atlas and everything the pass needs to fill it.</summary>
    /// <param name="device">The device to allocate on. Must support compute.</param>
    /// <param name="capture">The captured surfaces, for the cards and their pages.</param>
    /// <param name="instances">The per-placement lighting pages.</param>
    /// <param name="logger">Where to report what was allocated.</param>
    /// <returns>The pass, or <see langword="null"/> when it cannot be built.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static SurfaceCacheLighting? Create(
        IGraphicsDevice device,
        SurfaceCacheCapture.Result capture,
        SurfaceCacheInstances instances,
        IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(instances);

        IOpenIgiLogger log = logger ?? NullLogger.Instance;

        if (!device.Capabilities.SupportsCompute)
        {
            log.Information(LogCategory, "No compute on this context; the card pages will not be lit.");
            return null;
        }

        SurfaceCacheLayout layout = instances.Layout;

        if (layout.Pages.Length == 0)
        {
            return null;
        }

        // Every float the GPU sees is an offset from here, for the reason the clipmaps record: this
        // game's world coordinates carry a kilometres-large absolute offset, and a float at that
        // magnitude resolves tens of centimetres -- coarser than the texels being placed.
        Vector3d origin = CentreOf(instances);

        try
        {
            TextureHandle direct = device.CreateStorageTexture(
                layout.Width, layout.Height, VolumeFormat.Rgba16F, "cards.direct");

            TextureHandle indirect = device.CreateStorageTexture(
                layout.Width, layout.Height, VolumeFormat.Rgba16F, "cards.indirect");

            TextureHandle final = device.CreateStorageTexture(
                layout.Width, layout.Height, VolumeFormat.Rgba16F, "cards.final");

            ComputeShaderHandle shader = device.CreateComputeShader(
                ShaderModuleId.CardsLighting,
                new ShaderVariant(
                    device.Capabilities.SupportsRayQuery && RenderCVars.HardwareRayTracing.Value
                        ? "rayquery"
                        : "software"),
                "cards.light");

            (float[] packed, int[] tiles) = Describe(capture, instances, origin);

            StorageBufferHandle pageBuffer = device.CreateStorageBuffer(
                packed.Length * sizeof(float), "cards.pages");
            device.UpdateStorageBuffer<float>(pageBuffer, packed);

            StorageBufferHandle tileBuffer = device.CreateStorageBuffer(
                tiles.Length * sizeof(int), "cards.tiles");
            device.UpdateStorageBuffer<int>(tileBuffer, tiles);

            int tileCount = tiles.Length / 4;

            // The CPU-side halves of the active tile list: which page each tile belongs to, and each
            // page's world bounding sphere -- the same sphere the shader's radius cull reads -- so the
            // list can be rebuilt without touching the GPU data. The buffer is sized for every tile,
            // because ActiveRadius can legitimately cover the whole level.
            int[] tilePages = new int[tileCount];
            for (int i = 0; i < tileCount; i++)
            {
                tilePages[i] = tiles[i * 4];
            }

            int pageCount = packed.Length / FloatsPerPage;
            float[] pageSpheres = new float[pageCount * 4];
            for (int p = 0; p < pageCount; p++)
            {
                int from = (p * FloatsPerPage) + 36;
                int to = p * 4;
                pageSpheres[to + 0] = packed[from + 0];
                pageSpheres[to + 1] = packed[from + 1];
                pageSpheres[to + 2] = packed[from + 2];
                pageSpheres[to + 3] = packed[from + 3];
            }

            StorageBufferHandle activeTiles = device.CreateStorageBuffer(
                Math.Max(tileCount, 1) * sizeof(int), "cards.active");

            // One probe every fourth texel each way, so the probe grid is a quarter of the atlas on a side.
            // Zeroed on the way up so a probe grid cell no page covers reads as invalid (its w stays zero)
            // rather than as a probe at the origin.
            int probeWidth = (layout.Width + ProbeSpacing - 1) / ProbeSpacing;
            int probeHeight = (layout.Height + ProbeSpacing - 1) / ProbeSpacing;

            StorageBufferHandle probePositions = device.CreateStorageBuffer(
                probeWidth * probeHeight * 4 * sizeof(float), "cards.probes");
            device.UpdateStorageBuffer<float>(probePositions, new float[probeWidth * probeHeight * 4]);

            log.Information(
                LogCategory,
                $"Card lighting atlases: direct {direct}, indirect {indirect}, final {final}. "
                + $"Card lighting: {layout.Pages.Length} page(s) over {tileCount} tile(s) of "
                + $"{TileSize} square, into a {layout.Width}x{layout.Height} atlas.");

            // The two halves of the reconstruction, measured at build so a collapsed world position
            // can be blamed on the right one: a texel's position is card geometry carried through the
            // placement, and if either half is in the wrong space or the wrong units the sum is too.
            float placementReach = 0f;
            float cardReach = 0f;

            foreach (SurfaceCacheInstances.Placed placed in instances.Instances)
            {
                placementReach = Math.Max(
                    placementReach,
                    (float)Math.Max(
                        Math.Abs(placed.Position.X - origin.X),
                        Math.Max(
                            Math.Abs(placed.Position.Y - origin.Y),
                            Math.Abs(placed.Position.Z - origin.Z))));
            }

            foreach (MeshCard card in capture.Cards)
            {
                cardReach = Math.Max(
                    cardReach,
                    Math.Max(
                        Math.Abs(card.Centre.X) + card.HalfWidth,
                        Math.Max(
                            Math.Abs(card.Centre.Y) + card.HalfHeight,
                            Math.Abs(card.Centre.Z) + card.Depth)));
            }

            log.Information(
                LogCategory,
                $"Card lighting reach: placements {placementReach:0.#} unit(s) from their centre, "
                + $"cards {cardReach:0.#} unit(s) from their model's origin.");

            return new SurfaceCacheLighting(
                device, log, direct, indirect, final, shader, pageBuffer, tileBuffer, probePositions,
                activeTiles, tilePages, pageSpheres,
                probeWidth, probeHeight, tileCount, layout.Width, layout.Height, origin,
                packed, capture.Cards, instances);
        }
        catch (GraphicsDeviceException error)
        {
            log.Error(LogCategory, $"The card lighting could not be built: {error.Message}");
            return null;
        }
    }

    /// <summary>Carries a placement's pages to where it stands this frame, if it has moved.</summary>
    /// <param name="placement">The placement, as the cache was built from it.</param>
    /// <param name="position">Where it is drawn this frame.</param>
    /// <param name="orientation">How it is turned this frame.</param>
    /// <remarks>
    /// <para>
    /// A truck that drives off with the camera inside it: its cards were captured in the model's own space
    /// and their records placed them where it stood at load. Unmoved, every ray that struck its interior
    /// looked the surface up where the truck USED to be and read a wall of the yard, or nothing -- the GI
    /// inside went dark and smeared. Rewritten here, the lookup finds the truck's own surface wherever it is.
    /// </para>
    /// <para>
    /// The first move also takes the pages out of the static lookup grid, which still files them at the
    /// load-time spot, and into the short list of movers every lookup walks. And it stops them being
    /// relit: the shadows the lighting traces come from a distance field built from the level as it loaded,
    /// in which this object still stands where it started, so lighting it at its new place would light it
    /// as if it were not there. Its pages carry the light they had, as the surface carries its colour.
    /// </para>
    /// <para>Uploaded once a frame, by <see cref="CommitMoves"/>.</para>
    /// </remarks>
    public void MovePlacement(WorldPlacement placement, Vector3d position, Matrix3f orientation)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_instanceOf.TryGetValue(placement, out int index))
        {
            return;
        }

        SurfaceCacheInstances.Placed placed = _instances.Instances[index];
        bool moving = _movingInstances.Contains(index);
        if (!moving && position == placed.Position && orientation == placed.Orientation)
        {
            return;
        }

        if (!moving)
        {
            _movingInstances.Add(index);
            _moversChanged = true;
        }

        int firstMoving = _movingPages.Count;
        for (int slot = 0; slot < placed.PageCount; slot++)
        {
            int page = placed.FirstPage + slot;
            if (!_instances.Layout.Pages[page].IsAllocated)
            {
                continue;
            }

            int at = page * FloatsPerPage;
            MeshCard card = _cards[_instances.CardForPage[page]];
            WritePlacement(_pageRecords, at, card, position, orientation, _origin);

            // The record's spare word marks a page that moves: the lookup grid skips it, the lighting
            // leaves it alone. See the shared card lookup.
            _pageRecords[at + 23] = 1f;

            if (!moving)
            {
                _movingPages.Add(page);
            }

            _dirtyLow = Math.Min(_dirtyLow, page);
            _dirtyHigh = Math.Max(_dirtyHigh, page);
        }

        if (!moving && _movingPages.Count > firstMoving)
        {
            _movers.Add(new SurfaceCacheLookup.MoverObject(firstMoving, _movingPages.Count - firstMoving, default, 0f));
        }

        _moversMoved = true;
    }

    /// <summary>Uploads the pages moved this frame, and the movers' list if it grew.</summary>
    /// <remarks>
    /// Only the span from the first moved page to the last: a level's fans and radar heads turn every frame,
    /// and the whole record buffer is megabytes.
    /// </remarks>
    public void CommitMoves()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_moversMoved || _moversChanged)
        {
            BoundMovers();
        }

        if (_moversChanged)
        {
            _lookup?.SetMovers(_movingPages, _movers);
            _moversChanged = false;
        }
        else if (_moversMoved)
        {
            _lookup?.UpdateMoverBounds(_movers);
        }

        _moversMoved = false;

        if (_dirtyHigh >= _dirtyLow)
        {
            int from = _dirtyLow * FloatsPerPage;
            int count = (_dirtyHigh - _dirtyLow + 1) * FloatsPerPage;
            _device.UpdateStorageBuffer<float>(_pages, from, _pageRecords.AsSpan(from, count));
            _dirtyLow = int.MaxValue;
            _dirtyHigh = -1;
        }
    }

    /// <summary>Encloses each mover's pages, where their records stand now, in one sphere for the lookup to test.</summary>
    private void BoundMovers()
    {
        for (int m = 0; m < _movers.Count; m++)
        {
            SurfaceCacheLookup.MoverObject mover = _movers[m];

            // The centre of the box around the pages' spheres, then the radius that reaches the far side of
            // every one of them from it.
            Vector3f low = new(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3f high = new(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < mover.PageCount; i++)
            {
                int at = (_movingPages[mover.FirstPage + i] * FloatsPerPage) + 36;
                float r = _pageRecords[at + 3];
                low = new Vector3f(
                    MathF.Min(low.X, _pageRecords[at] - r),
                    MathF.Min(low.Y, _pageRecords[at + 1] - r),
                    MathF.Min(low.Z, _pageRecords[at + 2] - r));
                high = new Vector3f(
                    MathF.Max(high.X, _pageRecords[at] + r),
                    MathF.Max(high.Y, _pageRecords[at + 1] + r),
                    MathF.Max(high.Z, _pageRecords[at + 2] + r));
            }

            Vector3f centre = new((low.X + high.X) * 0.5f, (low.Y + high.Y) * 0.5f, (low.Z + high.Z) * 0.5f);
            float radius = 0f;
            for (int i = 0; i < mover.PageCount; i++)
            {
                int at = (_movingPages[mover.FirstPage + i] * FloatsPerPage) + 36;
                float dx = _pageRecords[at] - centre.X;
                float dy = _pageRecords[at + 1] - centre.Y;
                float dz = _pageRecords[at + 2] - centre.Z;
                radius = MathF.Max(radius, MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) + _pageRecords[at + 3]);
            }

            _movers[m] = mover with { Centre = centre, Radius = radius };
        }
    }

    /// <summary>The sun's light on open ground, as irradiance over pi, from the sun the cache was last lit by.</summary>
    private Vector3f GroundSun() =>
        _lastSunColour * (MathF.Max(_lastSunDirection.Z, 0f) / MathF.PI);

    /// <summary>Lights every page from the sun and the sky.</summary>
    /// <param name="sunDirection">The world-space direction towards the sun. Expected to be unit length.</param>
    /// <param name="sunColour">The sun's radiance.</param>
    /// <param name="skyColour">What the open sky is worth.</param>
    /// <param name="shadows">
    /// The clipmaps to shadow the sun against, or <see langword="null"/> to leave it unoccluded.
    /// </param>
    /// <param name="probe">
    /// Which diagnostic to write into the atlas instead of light, or zero for light. A failed shadow
    /// trace presents as a black atlas whatever actually failed; the probes write the trace's own
    /// intermediates so a dump can tell a wrong position from an unreadable field.
    /// </param>
    /// <param name="localScale">
    /// What a proposed fixture's relative intensity is worth against the sun, or <see langword="null"/>
    /// for exactly what the deferred lamps give it (<see cref="DeferredLampScale"/>).
    /// </param>
    /// <param name="amortize">
    /// When set, relights only a slice of the cache this call and walks the rest over the following
    /// frames, so a clipmap scroll's new shadows arrive without the whole-cache stall. Left clear for the
    /// load-time seed and a deterministic dump, which want every tile at once.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>With shadows this has to run repeatedly, and that is a property of the clipmaps rather than
    /// of the lighting.</b> They follow the camera and reach a few hundred metres, so a page on the far
    /// side of the level has no field to trace against. Re-lighting as the camera moves is what brings
    /// each page's shadow in as the player approaches it, and is also what would let the light move at
    /// all.
    /// </para>
    /// <para>
    /// Without them it is a single pass at load: this game's sun is a fixed direction per mission, so
    /// unshadowed lighting never changes.
    /// </para>
    /// </remarks>
    public void Light(
        Vector3f sunDirection,
        Vector3f sunColour,
        Vector3f skyColour,
        GlobalDistanceField? shadows = null,
        int probe = 0,
        float? localScale = null,
        bool amortize = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

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

    /// <summary>
    /// Relights one slice of the whole cache with shadows, for a load-time convergence that is spread over
    /// frames rather than run as one stall.
    /// </summary>
    /// <param name="sunDirection">The direction TOWARDS the sun.</param>
    /// <param name="sunColour">The sun's colour, already through every scale the screen applies.</param>
    /// <param name="skyColour">The sky's fill colour.</param>
    /// <param name="shadows">The field to trace shadows against.</param>
    /// <param name="slice">Which slice, from zero.</param>
    /// <param name="sliceCount">How many slices the cache is divided into.</param>
    /// <param name="localScale">How much a fixture's relative intensity is worth.</param>
    /// <remarks>
    /// The whole cache and no culling, exactly as the unamortised seed: the radius exists to keep the
    /// steady-state refresh off cards the player is nowhere near, and a level converged once at load
    /// means walking somewhere new does not re-run the ramp. Split into slices only so the work can be
    /// interleaved with presented frames -- the sum over every slice is the same dispatch the seed makes.
    /// </remarks>
    public void ConvergeLightSlice(
        Vector3f sunDirection,
        Vector3f sunColour,
        Vector3f skyColour,
        GlobalDistanceField? shadows,
        int slice,
        int sliceCount,
        float? localScale = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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

    /// <summary>Seeds one slice of the final atlas from the direct term, the first step of a spread convergence.</summary>
    /// <param name="shadows">The field the bounce traces against; nothing happens without one.</param>
    /// <param name="boost">The albedo boost, see <see cref="DefaultBoost"/>.</param>
    /// <param name="slice">Which slice, from zero.</param>
    /// <param name="sliceCount">How many slices the cache is divided into.</param>
    public void ConvergeSeedSlice(GlobalDistanceField? shadows, float boost, int slice, int sliceCount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (shadows is null || _lookup is null || !SliceRange(slice, sliceCount, out int offset, out int tiles))
        {
            return;
        }

        float radius = ActiveRadius;
        ActiveRadius = 0f;
        _tileStride = Math.Max(1, sliceCount);
        try
        {
            Dispatch(3, DirectLighting, FinalLighting, shadows, boost, 0, offset, tiles, useActiveList: false);
        }
        finally
        {
            _tileStride = 1;
            ActiveRadius = radius;
        }
    }

    /// <summary>Folds one more bounce into one slice of the cache, a step of a spread convergence.</summary>
    /// <param name="shadows">The field the gather traces against; nothing happens without one.</param>
    /// <param name="boost">The albedo boost, see <see cref="DefaultBoost"/>.</param>
    /// <param name="pass">
    /// Which bounce this is, from zero. Zero converges in place with no history, exactly as the seed's
    /// own iterations do; each later pass accumulates at a weight of one over one plus the pass.
    /// </param>
    /// <param name="slice">Which slice, from zero.</param>
    /// <param name="sliceCount">How many slices the cache is divided into.</param>
    /// <remarks>
    /// Slices are folded in order, and a later slice's gather reads the earlier slices' already-folded
    /// final -- a Gauss-Seidel sweep rather than the whole-cache Jacobi step -- which converges at least
    /// as fast and lands on the same equilibrium.
    /// </remarks>
    public void ConvergeBounceSlice(GlobalDistanceField? shadows, float boost, int pass, int slice, int sliceCount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (shadows is null || _lookup is null || !SliceRange(slice, sliceCount, out int offset, out int tiles))
        {
            return;
        }

        float radius = ActiveRadius;
        ActiveRadius = 0f;
        _tileStride = Math.Max(1, sliceCount);
        try
        {
            _device.SetComputeTimingLabel(Converging ? "cards.converge.bounce" : "cards.far.bounce");
            Dispatch(1, FinalLighting, IndirectLighting, shadows, boost, Math.Max(pass, 0), offset, tiles, useActiveList: false);
            Dispatch(2, DirectLighting, FinalLighting, shadows, boost, 0, offset, tiles, useActiveList: false);
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
        _lastSunDirection = sunDirection;
        _lastSunColour = sunColour;
        EnsureLightShader();

        _device.BindStorageBuffer(0, _pages);
        _device.BindStorageBuffer(1, _tiles);
        _device.BindStorageBuffer(2, _lights);
        BindLampMaps();
        _device.BindImage(0, DirectLighting, ImageAccess.Write);

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
        WriteSkyHarmonics(_shader);
        _device.SetComputeUniform(_shader, "uProbe", probe);
        _device.SetComputeUniform(_shader, "uMode", 0);
        _device.SetComputeUniform(_shader, "uAtlasSize", new Vector3f(Width, Height, 0f));
        _device.SetComputeUniform(_shader, "uLightCount", _lightCount);
        _device.SetComputeUniform(_shader, "uLocalScale", localScale ?? DeferredLampScale);

        _device.BindStorageBuffer(3, _lookup?.Cells ?? _pages);
        _device.BindStorageBuffer(4, _lookup?.Pages ?? _tiles);

        // Direct lighting never reads the probes, but the shader source is shared across every mode and
        // so declares them. GL lets an unused buffer go unbound; a descriptor set has no such notion, and
        // an unwritten binding is a fault whether or not the shader reaches it.
        _device.BindStorageBuffer(5, _probePositions);
        _device.SetComputeUniform(_shader, "uLookupBuckets", _lookup is null ? 0 : SurfaceCacheLookup.Buckets);

        if (_lookup is { } index)
        {
            _device.SetComputeUniform(_shader, "uLookupMin", index.Minimum);
            _device.SetComputeUniform(_shader, "uLookupCell", SurfaceCacheLookup.CellMetres);
        }

        BindShadows(_shader, shadows);

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

    /// <summary>What a fixture's relative intensity is worth, as the deferred lamps weigh it.</summary>
    /// <remarks>
    /// <para>
    /// The SAME light the screen shows, because this is what the room's bounce is integrated from. The
    /// deferred pass gives a lamp <c>pi * r_local_light_scale * min(intensity, 1.5) / (d^2 + 1)</c> of
    /// irradiance (its Lambert albedo/pi times the kPi on its lamp term). This used to be a constant 0.35
    /// picked by eye with neither the pi nor the preset's scale -- at ultra some fifty times less lamp
    /// light in the cache than on screen, so a lamp-lit floor bounced next to nothing onto the ceiling
    /// above it and every interior's ceiling read black at night.
    /// </para>
    /// </remarks>
    public static float DeferredLampScale => MathF.PI * RenderCVars.LocalLightScale.Value;

    /// <summary>The most a fixture's relative intensity counts for, as the deferred lamps clamp it.</summary>
    public const float MaximumLampIntensity = 1.5f;

    /// <summary>Gathers the bounce, then folds it into the atlas a trace should read.</summary>
    /// <param name="shadows">The clipmaps to trace against. Without them there is nothing to gather.</param>
    /// <param name="boost">
    /// The albedo boost the surface's diffuse colour is passed through before it reflects light on.
    /// Clamped below one inside the shader, which is what stops the feedback running away: a surface that
    /// returned everything it received, every iteration, would diverge.
    /// </param>
    /// <param name="iterations">
    /// How many times to bounce on the first pass. Each one reads the last one's answer, so two
    /// iterations is light that has reflected twice, and the cost is linear while the benefit falls off
    /// quickly. Only used on the first pass; the steady state gathers once and lets the further bounce
    /// arrive over frames.
    /// </param>
    /// <param name="temporalIndex">
    /// Which frame of the accumulation this is. Zero is a one-shot with no history -- the load pass and
    /// every dump -- and stays deterministic; above it the gather jitters its hemisphere per frame and
    /// blends onto what each probe already held, so the estimate integrates far more directions than one
    /// frame's sixteen and settles as the count rises.
    /// </param>
    /// <remarks>
    /// <b>The first pass converges in one shot; the steady state accumulates.</b> On the first pass the
    /// final atlas is seeded with the direct term's outgoing radiance and then gathered and combined
    /// <paramref name="iterations"/> times, so a still scene is lit without waiting for frames. Once
    /// running, each relight gathers once -- reading last frame's final, so a further bounce arrives over
    /// frames -- blends the result onto the running estimate, and folds it back into the final. It does not
    /// re-seed, because that would throw away the bounce the final already carries. The gather cannot read
    /// and write one atlas at once -- invocations run in no known order -- which is what forces the split
    /// into separate dispatches, and is also what keeps the last answer around for the next to build on.
    /// </remarks>
    public void Bounce(
        GlobalDistanceField? shadows, float boost = DefaultBoost, int iterations = 2, int temporalIndex = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (shadows is null || _lookup is null)
        {
            return;
        }

        if (temporalIndex <= 0)
        {
            // The first pass, and every dump. Seed the final atlas with the direct term's outgoing
            // radiance so the first gather reads what a surface sends rather than the raw irradiance that
            // reached it, then converge in place with no history.
            Dispatch(3, DirectLighting, FinalLighting, shadows, boost);

            for (int pass = 0; pass < Math.Max(iterations, 1); pass++)
            {
                Dispatch(1, FinalLighting, IndirectLighting, shadows, boost);
                Dispatch(2, DirectLighting, FinalLighting, shadows, boost);
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
        Dispatch(1, FinalLighting, IndirectLighting, shadows, boost, temporalIndex, offset, groups, useList, spread);
        Dispatch(2, DirectLighting, FinalLighting, shadows, boost, 0, offset, groups, useList);
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
        _device.BindStorageBuffer(2, _lights);
        BindLampMaps();
        _device.BindStorageBuffer(3, _lookup?.Cells ?? _pages);
        _device.BindStorageBuffer(4, _lookup?.Pages ?? _tiles);
        _device.BindStorageBuffer(5, _probePositions);

        // Read-write, because the gather's temporal accumulation reads a probe's previous value back
        // before blending this frame's onto it.
        _device.BindImage(0, target, ImageAccess.ReadWrite);

        _device.SetTexture(1, _device.CardAlbedo);
        _device.SetTexture(2, _device.CardNormal);
        _device.SetTexture(3, _device.CardMaterial);
        _device.SetTexture(4, _device.CardDepth);
        _device.SetTexture(6, previous);
        _device.SetTexture(7, IndirectLighting);

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
        _device.SetComputeUniform(_shader, "uAtlasSize", new Vector3f(Width, Height, 0f));
        _device.SetComputeUniform(_shader, "uLightCount", 0);
        _device.SetComputeUniform(
            _shader, "uLookupBuckets", _lookup is null ? 0 : SurfaceCacheLookup.Buckets);

        if (_lookup is { } index)
        {
            _device.SetComputeUniform(_shader, "uLookupMin", index.Minimum);
            _device.SetComputeUniform(_shader, "uLookupCell", SurfaceCacheLookup.CellMetres);
        }

        BindShadows(_shader, shadows);

        DispatchTiles(tileOffset, dispatchTiles < 0 ? _tileCount : Math.Max(dispatchTiles, 0));

        // The storage buffer scope as well, because the gather writes each probe's position through it
        // and the combine that follows reads its neighbours' -- not just the images the split is usually
        // about.
        _device.MemoryBarrier(
            MemoryBarrierScope.Image | MemoryBarrierScope.TextureFetch | MemoryBarrierScope.StorageBuffer);
    }

    /// <summary>The albedo boost a surface's diffuse colour is passed through by default.</summary>
    /// <remarks>
    /// A diffuse colour boost. Below one it raises a dark surface's albedo towards but never to one, so
    /// light bouncing between dim interior surfaces does not vanish; at one or above it is identity. This
    /// is the single knob that decides whether interiors read as lit or as washed out, and it is the first
    /// thing to reach for. The clamp below one in the shader is also what keeps the multi-bounce feedback
    /// from diverging.
    /// </remarks>
    public const float DefaultBoost = 0.8f;

    /// <summary>Gives the pass the index that turns a hit position into a card page.</summary>
    /// <param name="lookup">The index, or <see langword="null"/> to leave hits unresolved.</param>
    /// <remarks>
    /// Held rather than owned: the index is built from the same capture and placements this pass was
    /// built from, and outlives any one call for the same reason the lights do.
    /// </remarks>
    public void SetLookup(SurfaceCacheLookup? lookup)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _lookup = lookup;

        // A lookup built afresh knows nothing of what has already moved.
        _moversChanged = _movingPages.Count > 0;
    }

    /// <summary>Gives the pass the lights this level implies but never declares.</summary>
    /// <param name="fixtures">The proposed fixtures, in world metres.</param>
    /// <remarks>
    /// <para>
    /// Rebased onto the pass's own origin on the way in, for the reason the clipmaps record: this
    /// game's world coordinates carry a kilometres-large absolute offset, and a float at that magnitude
    /// resolves more coarsely than the surfaces being lit.
    /// </para>
    /// <para>
    /// Held rather than applied. The lighting pass runs again whenever the clipmaps move, so the
    /// fixtures have to outlive one call -- and they never change, because they are a property of the
    /// level's geometry rather than of where the camera is.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="fixtures"/> is <see langword="null"/>.</exception>
    public void SetLocalLights(IReadOnlyList<LightFixture> fixtures)
    {
        ArgumentNullException.ThrowIfNull(fixtures);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _device.DestroyStorageBuffer(_lights);
        _lights = default;
        _lightCount = fixtures.Count;
        _fixtures = fixtures;

        // At least one light's worth even when there are none: a storage buffer of no bytes is not a
        // thing to bind, and the shader reads the count rather than the buffer's size.
        float[] packed = new float[Math.Max(fixtures.Count, 1) * FloatsPerLight];

        for (int i = 0; i < fixtures.Count; i++)
        {
            LightFixture fixture = fixtures[i];
            int at = i * FloatsPerLight;

            packed[at] = (float)(fixture.Position.X - _origin.X);
            packed[at + 1] = (float)(fixture.Position.Y - _origin.Y);
            packed[at + 2] = (float)(fixture.Position.Z - _origin.Z);
            packed[at + 3] = fixture.Radius;

            packed[at + 4] = fixture.Colour.X;
            packed[at + 5] = fixture.Colour.Y;
            packed[at + 6] = fixture.Colour.Z;
            packed[at + 7] = MathF.Min(fixture.Intensity, MaximumLampIntensity);

            // Zero unless the fixture's own geometry agreed on a facing, which the shader reads as a
            // source that shines every way rather than as one aimed at a guess.
            if (fixture.IsDirectional)
            {
                packed[at + 8] = fixture.Direction.X;
                packed[at + 9] = fixture.Direction.Y;
                packed[at + 10] = fixture.Direction.Z;
            }

            // No shadow map until SetLampMapEntries says otherwise.
            packed[at + 11] = BitConverter.Int32BitsToSingle(-1);
        }

        _packed = packed;
        _lights = _device.CreateStorageBuffer(packed.Length * sizeof(float), "cards.lights");
        _device.UpdateStorageBuffer<float>(_lights, packed);

        _logger.Information(LogCategory, $"Card lighting: {fixtures.Count} local light(s).");
    }

    /// <summary>Says which lamps hold coarse maps the relight can read, by fixture.</summary>
    /// <param name="entryPerFixture">
    /// One per fixture, indexed as <see cref="SetLocalLights"/> was given them: the first of the lamp's six
    /// faces in <see cref="IGraphicsDevice.LampBounceMapArray"/>, or -1 for a lamp the relight must march the
    /// field for.
    /// </param>
    /// <remarks>
    /// <para>
    /// Without ray tracing, whether a lamp reaches a texel of the cache was asked of the distance field
    /// alone. Far from the camera its voxels are a metre or two and a wall is not in it: a row of lamps lit
    /// the near side of the wall they are fixed to the far side of, and the screen gather bounced that onto
    /// the wall in a bright patch the traced frame had dark. A lamp with coarse maps answers from them.
    /// </para>
    /// <para>
    /// Only what changed is re-sent, so a frame whose maps are as they were costs a comparison per lamp.
    /// </para>
    /// </remarks>
    public void SetLampMapEntries(ReadOnlySpan<int> entryPerFixture)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_packed is not { } packed || !_lights.IsValid)
        {
            return;
        }

        bool changed = false;
        for (int i = 0; i < _lightCount; i++)
        {
            int entry = i < entryPerFixture.Length ? entryPerFixture[i] : -1;
            float bits = BitConverter.Int32BitsToSingle(entry);
            int at = (i * FloatsPerLight) + 11;
            if (BitConverter.SingleToInt32Bits(packed[at]) != entry)
            {
                packed[at] = bits;
                changed = true;
            }
        }

        if (changed)
        {
            _device.UpdateStorageBuffer<float>(_lights, packed);
        }
    }

    /// <summary>Binds the lamps' coarse maps and their projections, refreshed when the maps have changed.</summary>
    /// <remarks>
    /// Every dispatch of the program binds them, whatever its mode: the modes share one set of bindings and
    /// an unwritten one is a fault. Where the device draws no maps, the buffer holds no drawn map and the
    /// texture slot is left to the device's stand-in.
    /// </remarks>
    private void BindLampMaps()
    {
        int floats = IGraphicsDevice.LampBounceMapCapacity * IGraphicsDevice.LampMapRecordFloats;
        if (!_lampMapRecords.IsValid)
        {
            _lampMapFloats = new float[floats];
            _lampMapRecords = _device.CreateStorageBuffer(floats * sizeof(float), "cards.lampmaps");
            _device.UpdateStorageBuffer<float>(_lampMapRecords, _lampMapFloats);
            _lampMapVersion = -1;
        }

        bool maps = _device.LampBounceMapSlots > 0;
        int version = maps ? _device.LampBounceMapVersion : -2;
        if (version != _lampMapVersion && _lampMapFloats is { } records)
        {
            if (maps)
            {
                _device.WriteLampBounceMaps(_origin, records);
            }
            else
            {
                Array.Clear(records);
            }

            _device.UpdateStorageBuffer<float>(_lampMapRecords, records);
            _lampMapVersion = version;
        }

        _device.BindStorageBuffer(7, _lampMapRecords);
        _device.SetTexture(8, maps ? _device.LampBounceMapArray : TextureHandle.None);
    }

    /// <summary>Hands the untraced gather the terrain's height raster, or takes it away.</summary>
    /// <param name="heights">The packed raster (<see cref="ShelterHeightField.Encode"/>), or none.</param>
    /// <param name="field">The raster it was packed from, which says where it lies.</param>
    /// <param name="scale">The metres one packed step stands for, as the packing reported.</param>
    /// <remarks>
    /// The distance field the untraced gather marches holds the level's placements and not its terrain, so
    /// a ray towards the ground went through it and came back with a guess at open ground. With this it
    /// finds the ground, and reads the cache's light where it landed.
    /// </remarks>
    public void SetTerrainHeights(TextureHandle heights, ShelterHeightField? field, float scale)
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
    public void SetRoofs(TextureHandle roofs, ShelterMapTransform transform)
    {
        _roofs = roofs.IsValid && transform.IsPresent ? roofs : TextureHandle.None;
        _roofTransform = _roofs.IsValid ? transform : ShelterMapTransform.None;
    }

    /// <summary>Binds the terrain's height raster to a gather, or a stand-in that says there is none.</summary>
    /// <param name="shader">The gather.</param>
    private void BindTerrainHeights(ComputeShaderHandle shader)
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

    /// <summary>Re-sends the same lights at whatever output a switchboard says they are giving out.</summary>
    /// <param name="board">The level's lamps, indexed as the fixtures were.</param>
    /// <remarks>
    /// <para>
    /// The bounce has to follow the switch or a switched-off lamp goes on filling the room with indirect
    /// light out of a fitting that is visibly dark — the one artefact that would give the whole thing
    /// away, because it is exactly the light the player switched off, arriving one bounce later.
    /// </para>
    /// <para>
    /// An overwrite rather than a rebuild: <see cref="SetLocalLights"/> destroys and recreates the
    /// buffer, which is a per-level cost and not one to pay while a tube is stuttering. Only the strength
    /// is rewritten — position, reach and facing are properties of the fitting and a switch does not move
    /// it — so this is one upload of a few kilobytes over a buffer that is already the right shape.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="board"/> is <see langword="null"/>.</exception>
    public void SetLocalLightLevels(LampSwitchboard board)
    {
        ArgumentNullException.ThrowIfNull(board);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_packed is not { Length: > 0 } packed || _fixtures is not { } fixtures || !_lights.IsValid)
        {
            return;
        }

        for (int i = 0; i < fixtures.Count && i < board.Count; i++)
        {
            packed[(i * FloatsPerLight) + 7] = MathF.Min(fixtures[i].Intensity, MaximumLampIntensity) * board.LevelOf(i);
        }

        _device.UpdateStorageBuffer<float>(_lights, packed);
    }

    /// <summary>Hands the clipmaps to the pass, or tells it there are none.</summary>
    /// <remarks>
    /// Every position the shader works in is relative to this pass's own origin, and the clipmaps are
    /// relative to theirs, so their corners are rebased on the way in. Two structures each measuring
    /// from their own centre is what keeps both in float range across a level kilometres from the world
    /// origin; converting between them here is the price of that.
    /// </remarks>
    /// <summary>Recompiles the tracing passes when the trace mode has been switched live.</summary>
    /// <remarks>
    /// The trace tier is baked into each pass's source — <c>VisibilityAt</c> is either a ray query or a
    /// distance-field march — so flipping <c>r_rt</c> cannot be a uniform. The stale shaders are
    /// destroyed here and each pass recreates its own on next use, exactly as on first use. The resolve
    /// and filter passes do not trace and are left alone.
    /// </remarks>
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
        EnsureTraceMode();
        if (!_shader.IsValid)
        {
            _shader = _device.CreateComputeShader(
                ShaderModuleId.CardsLighting,
                new ShaderVariant(_tracesRays ? "rayquery" : "software"),
                "cards.light");
        }
    }

    private void EnsureTraceMode()
    {
        bool want = _device.Capabilities.SupportsRayQuery && RenderCVars.HardwareRayTracing.Value;
        if (want == _tracesRays)
        {
            return;
        }

        _tracesRays = want;

        if (_shader.IsValid)
        {
            _device.DestroyComputeShader(_shader);
            _shader = ComputeShaderHandle.None;
        }

        if (_screenGather.IsValid)
        {
            _device.DestroyComputeShader(_screenGather);
            _screenGather = ComputeShaderHandle.None;
        }

        if (_screenGatherTemporal.IsValid)
        {
            _device.DestroyComputeShader(_screenGatherTemporal);
            _screenGatherTemporal = ComputeShaderHandle.None;
        }

        if (_pixelGather.IsValid)
        {
            _device.DestroyComputeShader(_pixelGather);
            _pixelGather = ComputeShaderHandle.None;
        }

        if (_cacheUpdate.IsValid)
        {
            _device.DestroyComputeShader(_cacheUpdate);
            _cacheUpdate = ComputeShaderHandle.None;
        }
    }

    /// <summary>Hands a pass the sky's harmonics, when the backend has an atmosphere to report.</summary>
    /// <param name="shader">The pass to write the uniforms of.</param>
    /// <remarks>
    /// The seven coefficient vectors the deferred sky term itself consumes. They hold diffuse irradiance
    /// divided by pi, not unconvolved sky radiance; the tracing shaders name and restore that contract at
    /// the point where they integrate it. A backend without them leaves the pass on its flat colour.
    /// </remarks>
    private void WriteSkyHarmonics(ComputeShaderHandle shader)
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
    private float ExposedRayCap()
        => RenderCVars.ScreenGatherMaxIntensity.Value / MathF.Max(_device.SceneExposure, 1e-3f);

    private void BindShadows(ComputeShaderHandle shader, GlobalDistanceField? shadows)
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

    /// <summary>Gathers indirect light for a grid of screen probes and writes it to a probe atlas.</summary>
    /// <param name="shadows">The clipmaps to trace against. Without them there is nothing to gather.</param>
    /// <param name="sceneDepth">The frame's device depth, from the G-buffer.</param>
    /// <param name="sceneNormal">The frame's view-space normals, from the G-buffer.</param>
    /// <param name="unproject">The projection's scales and planes, for turning depth into a view position.</param>
    /// <param name="viewToWorld">The view-to-world rotation, matching what the deferred pass draws with.</param>
    /// <param name="cameraToCache">The camera's world position relative to this pass's origin.</param>
    /// <param name="screenWidth">The frame's width in pixels.</param>
    /// <param name="screenHeight">The frame's height in pixels.</param>
    /// <param name="probeStep">How many pixels lie between probes each way.</param>
    /// <param name="output">The probe atlas to write, sized to the screen divided by the step.</param>
    /// <remarks>
    /// <para>
    /// L6's screen probe gather, the pass that finally carries the surface cache to the screen. Each
    /// probe reads its pixel's depth and normal from the G-buffer, rebuilds where that pixel sits in the
    /// world -- rebased onto this pass's origin so it lands in the same space the trace works in -- and
    /// gathers a hemisphere of rays through the shared software trace, reading the surface cache's final
    /// lighting at each hit. What comes back is the pixel's indirect irradiance, which a later pass
    /// interpolates across the probes and folds into the frame.
    /// </para>
    /// <para>
    /// A probe grid rather than per pixel, because indirect light varies slowly and tracing every pixel
    /// would trace sixteen rays a pixel over the whole screen. Probes could be refined adaptively where
    /// depth or normal vary too fast for the grid to interpolate; that refinement is a later slice.
    /// </para>
    /// </remarks>
    public void GatherScreen(
        GlobalDistanceField? shadows,
        TextureHandle sceneDepth,
        TextureHandle sceneNormal,
        in DepthUnprojection unproject,
        Matrix3f viewToWorld,
        Vector3f cameraToCache,
        int screenWidth,
        int screenHeight,
        int probeStep,
        TextureHandle output)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (shadows is null || _lookup is null || screenWidth <= 0 || screenHeight <= 0)
        {
            return;
        }

        int step = Math.Max(probeStep, 1);

        EnsureTraceMode();
        if (!_screenGather.IsValid)
        {
            _screenGather = _device.CreateComputeShader(
                ShaderModuleId.CardsScreenGather,
                new ShaderVariant(_tracesRays ? "rayquery" : "software"),
                "cards.screen");
        }

        _device.BindStorageBuffer(0, _pages);
        _device.BindStorageBuffer(3, _lookup.Cells);
        _device.BindStorageBuffer(4, _lookup.Pages);
        _device.BindImage(0, output, ImageAccess.Write);

        _device.SetTexture(1, _device.CardAlbedo);
        _device.SetTexture(4, _device.CardDepth);
        _device.SetTexture(6, FinalLighting);
        _device.SetTexture(7, sceneDepth);
        _device.SetTexture(8, sceneNormal);

        // Bound so the sampler is valid, but the trace is disabled below: the dump stays a deterministic
        // single-frame surface-cache gather, with no dependence on a previous frame's colour.
        _device.SetTexture(11, _device.SceneColour);

        _device.SetComputeUniform(_screenGather, "uMaxRayIntensity", ExposedRayCap());
        _device.SetComputeUniform(_screenGather, "uScreenTraceSteps", 0);
        _device.SetComputeUniform(_screenGather, "uScreenTraceDistance", 0f);
        _device.SetComputeUniform(_screenGather, "uScreenThickness", 0f);
        _device.SetComputeUniform(_screenGather, "uSkyColour", default(Vector3f));
        _device.SetComputeUniform(_screenGather, "uGroundSun", default(Vector3f));

        // The dump shows traced bounce alone, so the harmonics are forced off along with the flat sky,
        // and the bounce is read at its physical strength whatever the live calibration says.
        _device.SetComputeUniform(_screenGather, "uSkyShValid", 0);
        _device.SetComputeUniform(_screenGather, "uBounceScale", 1f);
        _device.SetComputeUniform(_screenGather, "uDebugSource", RenderCVars.GiSource.Value);
        BindTerrainHeights(_screenGather);

        _device.SetComputeUniform(_screenGather, "uAtlasSize", new Vector3f(Width, Height, 0f));
        _device.SetComputeUniform(_screenGather, "uLookupBuckets", SurfaceCacheLookup.Buckets);
        _device.SetComputeUniform(_screenGather, "uLookupMin", _lookup.Minimum);
        _device.SetComputeUniform(_screenGather, "uLookupCell", SurfaceCacheLookup.CellMetres);

        _device.SetComputeUniform(
            _screenGather, "uUnproject", unproject.XScale, unproject.YScale, unproject.Near, unproject.Far);
        _device.SetComputeUniform(_screenGather, "uViewToWorld0", viewToWorld.Column1);
        _device.SetComputeUniform(_screenGather, "uViewToWorld1", viewToWorld.Column2);
        _device.SetComputeUniform(_screenGather, "uViewToWorld2", viewToWorld.Column3);
        _device.SetComputeUniform(_screenGather, "uCameraToCache", cameraToCache);
        _device.SetComputeUniform(_screenGather, "uScreenWidth", screenWidth);
        _device.SetComputeUniform(_screenGather, "uScreenHeight", screenHeight);
        _device.SetComputeUniform(_screenGather, "uProbeStep", step);

        BindShadows(_screenGather, shadows);

        int gridX = (screenWidth + step - 1) / step;
        int gridY = (screenHeight + step - 1) / step;

        _device.Dispatch(_screenGather, (gridX + 7) / 8, (gridY + 7) / 8);
        _device.MemoryBarrier(MemoryBarrierScope.Image | MemoryBarrierScope.TextureFetch);
    }

    /// <summary>Gathers one ray of indirect light at every pixel, unfiltered, for DLSS ray reconstruction.</summary>
    /// <param name="shadows">The clipmaps the ray falls back to past the traced world.</param>
    /// <param name="sceneDepth">The frame's device depth, from the G-buffer.</param>
    /// <param name="sceneNormal">The frame's normals, from the G-buffer.</param>
    /// <param name="unproject">The projection's scales and planes, for turning depth into a view position.</param>
    /// <param name="viewToWorld">The view-to-world rotation, matching what the deferred pass draws with.</param>
    /// <param name="cameraToCache">The camera's world position relative to this pass's origin.</param>
    /// <param name="screenWidth">The frame's width in pixels.</param>
    /// <param name="screenHeight">The frame's height in pixels.</param>
    /// <param name="skyColour">The sky's radiance, returned for a ray that escapes without a hit.</param>
    /// <param name="frame">The presented-frame count, which the white noise is keyed on.</param>
    /// <param name="output">The irradiance image to write, the frame's size.</param>
    /// <param name="sceneColour">The previous frame's lit scene, read at an on-screen hit.</param>
    /// <param name="screenTraceSteps">The screen trace's step cap, and its on/off (zero disables it).</param>
    /// <param name="screenTraceDistance">How far a screen-traced ray reaches, in metres.</param>
    /// <param name="screenThickness">The assumed surface thickness for the screen trace's hit test, in metres.</param>
    /// <param name="colourWorldToView">The world-to-view rotation <paramref name="sceneColour"/> was rendered with.</param>
    /// <param name="colourCameraToCache">The camera <paramref name="sceneColour"/> was rendered from, relative to this pass's origin.</param>
    /// <returns><see langword="true"/> when the gather ran; <see langword="false"/> where it cannot trace.</returns>
    /// <remarks>
    /// <para>
    /// The same gather as the probes' -- the traced world first, then the cache's light where it struck, the
    /// sky or the open ground on a miss -- with a probe at every pixel and a single ray from it. The probes'
    /// own denoising, their pooling, cadence, accumulation and resolve, is exactly what ray reconstruction
    /// replaces, and handed their output it would see a smooth, lagging signal it was not trained on.
    /// </para>
    /// <para>
    /// The screen comes first, as for the probes: a ray that lands on something in last frame's lit picture
    /// takes that light, read where the hit stood last frame. It is what carries a lamp's pool on the floor
    /// up onto the ceiling and a lit machine's glow onto the wall beside it, which the cards hold coarsely
    /// or not at all. Without it the interiors under ray reconstruction lost their bounce light.
    /// </para>
    /// </remarks>
    public bool GatherPixels(
        GlobalDistanceField? shadows,
        TextureHandle sceneDepth,
        TextureHandle sceneNormal,
        in DepthUnprojection unproject,
        Matrix3f viewToWorld,
        Vector3f cameraToCache,
        int screenWidth,
        int screenHeight,
        Vector3f skyColour,
        uint frame,
        TextureHandle output,
        TextureHandle sceneColour,
        int screenTraceSteps,
        float screenTraceDistance,
        float screenThickness,
        Matrix3f colourWorldToView,
        Vector3f colourCameraToCache)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (shadows is null || _lookup is null || screenWidth <= 0 || screenHeight <= 0)
        {
            return false;
        }

        EnsureTraceMode();
        if (!_tracesRays)
        {
            return false;
        }

        if (!_pixelGather.IsValid)
        {
            _pixelGather = _device.CreateComputeShader(
                ShaderModuleId.CardsScreenGather, new ShaderVariant("pixel"), "cards.pixel");
        }

        _device.BindStorageBuffer(0, _pages);
        _device.BindStorageBuffer(3, _lookup.Cells);
        _device.BindStorageBuffer(4, _lookup.Pages);
        _device.BindImage(0, output, ImageAccess.Write);

        _device.SetTexture(1, _device.CardAlbedo);
        _device.SetTexture(4, _device.CardDepth);
        _device.SetTexture(6, FinalLighting);
        _device.SetTexture(7, sceneDepth);
        _device.SetTexture(8, sceneNormal);
        _device.SetTexture(11, sceneColour);

        _device.SetComputeUniform(_pixelGather, "uMaxRayIntensity", ExposedRayCap());
        _device.SetComputeUniform(_pixelGather, "uScreenTraceSteps", Math.Max(screenTraceSteps, 0));
        _device.SetComputeUniform(_pixelGather, "uScreenTraceDistance", MathF.Max(screenTraceDistance, 0f));
        _device.SetComputeUniform(_pixelGather, "uScreenThickness", MathF.Max(screenThickness, 0f));
        _device.SetComputeUniform(_pixelGather, "uColourWorldToView0", colourWorldToView.Column1);
        _device.SetComputeUniform(_pixelGather, "uColourWorldToView1", colourWorldToView.Column2);
        _device.SetComputeUniform(_pixelGather, "uColourWorldToView2", colourWorldToView.Column3);
        _device.SetComputeUniform(_pixelGather, "uColourCameraToCache", colourCameraToCache);
        _device.SetComputeUniform(_pixelGather, "uSkyColour", skyColour);
        _device.SetComputeUniform(_pixelGather, "uGroundSun", GroundSun());
        _device.SetComputeUniform(_pixelGather, "uBounceScale", RenderCVars.GiBounce.Value);
        _device.SetComputeUniform(_pixelGather, "uDebugSource", RenderCVars.GiSource.Value);

        // Wrapped, as the traces' counts are: the noise only needs it to differ from one frame to the next.
        _device.SetComputeUniform(_pixelGather, "uFrame", (int)(frame & 0xFFFFFFu));
        BindTerrainHeights(_pixelGather);
        WriteSkyHarmonics(_pixelGather);

        _device.SetComputeUniform(_pixelGather, "uAtlasSize", new Vector3f(Width, Height, 0f));
        _device.SetComputeUniform(_pixelGather, "uLookupBuckets", SurfaceCacheLookup.Buckets);
        _device.SetComputeUniform(_pixelGather, "uLookupMin", _lookup.Minimum);
        _device.SetComputeUniform(_pixelGather, "uLookupCell", SurfaceCacheLookup.CellMetres);

        _device.SetComputeUniform(
            _pixelGather, "uUnproject", unproject.XScale, unproject.YScale, unproject.Near, unproject.Far);
        _device.SetComputeUniform(_pixelGather, "uViewToWorld0", viewToWorld.Column1);
        _device.SetComputeUniform(_pixelGather, "uViewToWorld1", viewToWorld.Column2);
        _device.SetComputeUniform(_pixelGather, "uViewToWorld2", viewToWorld.Column3);
        _device.SetComputeUniform(_pixelGather, "uCameraToCache", cameraToCache);
        _device.SetComputeUniform(_pixelGather, "uScreenWidth", screenWidth);
        _device.SetComputeUniform(_pixelGather, "uScreenHeight", screenHeight);
        _device.SetComputeUniform(_pixelGather, "uProbeStep", 1);

        BindShadows(_pixelGather, shadows);

        _device.SetComputeTimingLabel("cards.pixel");
        _device.Dispatch(_pixelGather, (screenWidth + 7) / 8, (screenHeight + 7) / 8);
        _device.SetComputeTimingLabel(null);
        _device.MemoryBarrier(MemoryBarrierScope.Image | MemoryBarrierScope.TextureFetch);
        return true;
    }

    /// <summary>Gathers indirect light per screen probe and accumulates it over frames.</summary>
    /// <param name="shadows">The clipmaps to trace against. Without them there is nothing to gather.</param>
    /// <param name="sceneDepth">The frame's device depth, from the G-buffer.</param>
    /// <param name="sceneNormal">The frame's view-space normals, from the G-buffer.</param>
    /// <param name="unproject">The projection's scales and planes, for turning depth into a view position.</param>
    /// <param name="viewToWorld">The view-to-world rotation, matching what the deferred pass draws with.</param>
    /// <param name="cameraToCache">The camera's world position relative to this pass's origin.</param>
    /// <param name="screenWidth">The frame's width in pixels.</param>
    /// <param name="screenHeight">The frame's height in pixels.</param>
    /// <param name="probeStep">How many pixels lie between probes each way.</param>
    /// <param name="accumulated">The atlas of accumulated irradiance to write and, next frame, read.</param>
    /// <param name="positions">The atlas of probe world positions, written beside the irradiance.</param>
    /// <param name="history">Last frame's accumulated irradiance, reprojected onto this frame.</param>
    /// <param name="positionHistory">Last frame's probe world positions, for the disocclusion test.</param>
    /// <param name="prevWorldToView">The previous frame's world-to-view rotation, for reprojection.</param>
    /// <param name="prevCameraToCache">The previous frame's camera relative to this pass's origin.</param>
    /// <param name="frameIndex">The running frame index, for the jitter.</param>
    /// <param name="maxAccumulated">The ceiling on how many frames accumulate into a probe.</param>
    /// <param name="hasHistory">Whether there is a previous frame to reproject onto.</param>
    /// <param name="sceneColour">The previous frame's lit scene, read at an on-screen trace hit.</param>
    /// <param name="screenTraceSteps">The screen trace's step cap, and its on/off (zero disables it).</param>
    /// <param name="screenTraceDistance">How far a screen-traced ray reaches, in metres.</param>
    /// <param name="screenThickness">The assumed surface thickness for the screen trace's hit test, in metres.</param>
    /// <param name="skyColour">The sky's radiance, returned for a ray that escapes without a hit.</param>
    /// <param name="colourWorldToView">The world-to-view rotation <paramref name="sceneColour"/> was rendered with.</param>
    /// <param name="colourCameraToCache">The camera <paramref name="sceneColour"/> was rendered from, relative to this pass's origin.</param>
    /// <param name="raw">Scratch for the raw per-probe estimates, the size of <paramref name="accumulated"/>.</param>
    /// <param name="sceneVelocity">The frame's motion field (<see cref="IGraphicsDevice.SceneVelocity"/>), or none.</param>
    /// <param name="previousUnproject">
    /// The projection the history was drawn through, or <see langword="null"/> for this frame's. A cinematic
    /// zoom changes it a little every frame, and reprojecting through this frame's instead misses.
    /// </param>
    /// <remarks>
    /// L6c's temporal accumulation of the gather, with L5b's screen trace tier. The raw estimate is noisy
    /// -- sixteen jittered rays a frame -- so reading it straight ghosts and shimmers. Each probe
    /// reprojects its surface onto last frame's screen, reads the irradiance that accumulated there, and
    /// blends this frame's estimate in with a weight that falls as the frame count climbs, a running
    /// average. A reprojection that lands on a different surface, or off screen, drops the history rather
    /// than smearing it. Kept separate from the frame's own temporal anti-aliasing: this stabilises the
    /// gather in probe space before it is read, and the two filters compose.
    /// </remarks>
    public void GatherScreenTemporal(
        GlobalDistanceField? shadows,
        TextureHandle sceneDepth,
        TextureHandle sceneNormal,
        in DepthUnprojection unproject,
        Matrix3f viewToWorld,
        Vector3f cameraToCache,
        int screenWidth,
        int screenHeight,
        int probeStep,
        TextureHandle accumulated,
        TextureHandle positions,
        TextureHandle history,
        TextureHandle positionHistory,
        Matrix3f prevWorldToView,
        Vector3f prevCameraToCache,
        int frameIndex,
        int maxAccumulated,
        bool hasHistory,
        TextureHandle sceneColour,
        int screenTraceSteps,
        float screenTraceDistance,
        float screenThickness,
        Vector3f skyColour,
        Matrix3f colourWorldToView,
        Vector3f colourCameraToCache,
        TextureHandle raw,
        TextureHandle sceneVelocity = default,
        DepthUnprojection? previousUnproject = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (shadows is null || _lookup is null || screenWidth <= 0 || screenHeight <= 0)
        {
            return;
        }

        int step = Math.Max(probeStep, 1);

        EnsureTraceMode();
        if (!_screenGatherTemporal.IsValid)
        {
            _screenGatherTemporal = _device.CreateComputeShader(
                ShaderModuleId.CardsScreenGatherTemporal,
                new ShaderVariant(_tracesRays ? "rayquery" : "software"),
                "cards.screen.temporal");
        }

        _device.BindStorageBuffer(0, _pages);
        _device.BindStorageBuffer(3, _lookup.Cells);
        _device.BindStorageBuffer(4, _lookup.Pages);
        _device.BindImage(0, accumulated, ImageAccess.Write);
        _device.BindImage(1, positions, ImageAccess.Write);

        _device.SetTexture(1, _device.CardAlbedo);
        _device.SetTexture(4, _device.CardDepth);
        _device.SetTexture(6, FinalLighting);
        _device.SetTexture(7, sceneDepth);
        _device.SetTexture(8, sceneNormal);
        _device.SetTexture(9, history);
        _device.SetTexture(10, positionHistory);
        _device.SetTexture(11, sceneColour);

        // Where a moving object's surface came from (IGraphicsDevice.SceneVelocity). Without a field the flag is
        // off and the normals, never read, fill the unit the program declares either way.
        _device.SetTexture(13, sceneVelocity.IsValid ? sceneVelocity : sceneNormal);
        _device.SetComputeUniform(_screenGatherTemporal, "uHasVelocity", sceneVelocity.IsValid ? 1 : 0);

        _device.SetComputeUniform(_screenGatherTemporal, "uMaxRayIntensity", ExposedRayCap());
        _device.SetComputeUniform(_screenGatherTemporal, "uScreenTraceSteps", Math.Max(screenTraceSteps, 0));
        _device.SetComputeUniform(_screenGatherTemporal, "uScreenTraceDistance", MathF.Max(screenTraceDistance, 0f));
        _device.SetComputeUniform(_screenGatherTemporal, "uScreenThickness", MathF.Max(screenThickness, 0f));
        _device.SetComputeUniform(_screenGatherTemporal, "uSkyColour", skyColour);
        _device.SetComputeUniform(_screenGatherTemporal, "uGroundSun", GroundSun());
        _device.SetComputeUniform(_screenGatherTemporal, "uBounceScale", RenderCVars.GiBounce.Value);
        _device.SetComputeUniform(_screenGatherTemporal, "uDebugSource", RenderCVars.GiSource.Value);
        BindTerrainHeights(_screenGatherTemporal);
        WriteSkyHarmonics(_screenGatherTemporal);

        _device.SetComputeUniform(_screenGatherTemporal, "uAtlasSize", new Vector3f(Width, Height, 0f));
        _device.SetComputeUniform(_screenGatherTemporal, "uLookupBuckets", SurfaceCacheLookup.Buckets);
        _device.SetComputeUniform(_screenGatherTemporal, "uLookupMin", _lookup.Minimum);
        _device.SetComputeUniform(_screenGatherTemporal, "uLookupCell", SurfaceCacheLookup.CellMetres);

        _device.SetComputeUniform(
            _screenGatherTemporal, "uUnproject", unproject.XScale, unproject.YScale, unproject.Near, unproject.Far);
        DepthUnprojection gatherPrevious = previousUnproject ?? unproject;
        _device.SetComputeUniform(
            _screenGatherTemporal,
            "uPrevUnproject",
            gatherPrevious.XScale,
            gatherPrevious.YScale,
            gatherPrevious.Near,
            gatherPrevious.Far);
        _device.SetComputeUniform(_screenGatherTemporal, "uViewToWorld0", viewToWorld.Column1);
        _device.SetComputeUniform(_screenGatherTemporal, "uViewToWorld1", viewToWorld.Column2);
        _device.SetComputeUniform(_screenGatherTemporal, "uViewToWorld2", viewToWorld.Column3);
        _device.SetComputeUniform(_screenGatherTemporal, "uCameraToCache", cameraToCache);
        _device.SetComputeUniform(_screenGatherTemporal, "uScreenWidth", screenWidth);
        _device.SetComputeUniform(_screenGatherTemporal, "uScreenHeight", screenHeight);
        _device.SetComputeUniform(_screenGatherTemporal, "uProbeStep", step);

        _device.SetComputeUniform(_screenGatherTemporal, "uHasHistory", hasHistory ? 1 : 0);
        _device.SetComputeUniform(_screenGatherTemporal, "uPrevWorldToView0", prevWorldToView.Column1);
        _device.SetComputeUniform(_screenGatherTemporal, "uPrevWorldToView1", prevWorldToView.Column2);
        _device.SetComputeUniform(_screenGatherTemporal, "uPrevWorldToView2", prevWorldToView.Column3);
        _device.SetComputeUniform(_screenGatherTemporal, "uPrevCameraToCache", prevCameraToCache);
        _device.SetComputeUniform(_screenGatherTemporal, "uColourWorldToView0", colourWorldToView.Column1);
        _device.SetComputeUniform(_screenGatherTemporal, "uColourWorldToView1", colourWorldToView.Column2);
        _device.SetComputeUniform(_screenGatherTemporal, "uColourWorldToView2", colourWorldToView.Column3);
        _device.SetComputeUniform(_screenGatherTemporal, "uColourCameraToCache", colourCameraToCache);
        _device.SetComputeUniform(_screenGatherTemporal, "uFrameIndex", frameIndex);
        _device.SetComputeUniform(_screenGatherTemporal, "uMaxAccumulated", Math.Max(maxAccumulated, 1));

        BindShadows(_screenGatherTemporal, shadows);

        int gridX = (screenWidth + step - 1) / step;
        int gridY = (screenHeight + step - 1) / step;

        // Two passes over the same program: the first traces every probe into the raw atlas, the second
        // pools each probe's whole neighbourhood from it and folds the result into the history. See
        // uGatherPass for why the pool cannot share the trace's dispatch.
        _device.BindImage(0, raw, ImageAccess.Write);
        _device.SetComputeUniform(_screenGatherTemporal, "uGatherPass", 0);
        _device.SetComputeTimingLabel("cards.screen.trace");
        _device.Dispatch(_screenGatherTemporal, (gridX + 7) / 8, (gridY + 7) / 8);
        _device.SetComputeTimingLabel(null);
        _device.MemoryBarrier(MemoryBarrierScope.Image | MemoryBarrierScope.TextureFetch);

        _device.BindImage(0, accumulated, ImageAccess.Write);
        _device.SetTexture(12, raw);
        _device.SetComputeUniform(_screenGatherTemporal, "uGatherPass", 1);
        _device.SetComputeTimingLabel("cards.screen.pool");
        _device.Dispatch(_screenGatherTemporal, (gridX + 7) / 8, (gridY + 7) / 8);
        _device.SetComputeTimingLabel(null);
        _device.MemoryBarrier(MemoryBarrierScope.Image | MemoryBarrierScope.TextureFetch);
    }

    /// <summary>Upsamples the sparse probe atlas to full resolution and accumulates it there over time.</summary>
    /// <param name="probe">The gather's filtered sparse probe atlas, to upsample.</param>
    /// <param name="sceneDepth">The scene depth, for the reconstruction and the upsample's depth weight.</param>
    /// <param name="sceneNormal">The scene normals.</param>
    /// <param name="unproject">The projection scales and planes.</param>
    /// <param name="viewToWorld">The view-to-world rotation.</param>
    /// <param name="cameraToCache">This frame's camera, relative to the pass origin.</param>
    /// <param name="prevWorldToView">Last frame's world-to-view rotation.</param>
    /// <param name="prevCameraToCache">Last frame's camera, relative to the pass origin.</param>
    /// <param name="accum">The full-resolution atlas to write this frame's accumulation into.</param>
    /// <param name="accumPosition">The full-resolution atlas to write each pixel's world position into.</param>
    /// <param name="history">Last frame's <paramref name="accum"/>.</param>
    /// <param name="historyPosition">Last frame's <paramref name="accumPosition"/>.</param>
    /// <param name="screenWidth">The full render width.</param>
    /// <param name="screenHeight">The full render height.</param>
    /// <param name="hasHistory">Whether there is a previous frame to reproject onto.</param>
    /// <param name="maxFrames">The ceiling on the running average.</param>
    /// <param name="probePosition">Where each probe of <paramref name="probe"/> stood when traced, relative to that frame's camera.</param>
    /// <param name="probeWorldToView">The world-to-view rotation of the frame the probes were traced in.</param>
    /// <param name="probeCameraToCache">That frame's camera, relative to this pass's origin.</param>
    /// <param name="probeStep">Pixels between probes.</param>
    /// <param name="sceneVelocity">The frame's motion field (<see cref="IGraphicsDevice.SceneVelocity"/>), or none.</param>
    /// <param name="probeFrame">The gather frame the probes were traced in, which places them in their tiles.</param>
    /// <param name="previousUnproject">
    /// The projection the history was drawn through, or <see langword="null"/> for this frame's. A cinematic
    /// zoom changes it a little every frame, and reprojecting through this frame's instead misses.
    /// </param>
    /// <remarks>
    /// The gather accumulates at the sparse probe grid, whose reprojection is coarse, so its upsampled indirect
    /// slides across a surface as the camera moves. This resolves it to full resolution and reprojects THERE,
    /// per pixel, against the surface's own world position -- pinning the indirect to the world. The deferred
    /// fill reads the result directly.
    /// </remarks>
    public void ResolveScreenTemporal(
        TextureHandle probe,
        TextureHandle sceneDepth,
        TextureHandle sceneNormal,
        DepthUnprojection unproject,
        Matrix3f viewToWorld,
        Vector3f cameraToCache,
        Matrix3f prevWorldToView,
        Vector3f prevCameraToCache,
        TextureHandle accum,
        TextureHandle accumPosition,
        TextureHandle history,
        TextureHandle historyPosition,
        int screenWidth,
        int screenHeight,
        bool hasHistory,
        int maxFrames,
        TextureHandle probePosition,
        Matrix3f probeWorldToView,
        Vector3f probeCameraToCache,
        int probeStep,
        TextureHandle sceneVelocity = default,
        DepthUnprojection? previousUnproject = null,
        int probeFrame = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (screenWidth <= 0 || screenHeight <= 0)
        {
            return;
        }

        if (!_screenResolve.IsValid)
        {
            _screenResolve = _device.CreateComputeShader(
                ShaderModuleId.CardsScreenResolve, ShaderVariant.Default, "cards.screen.resolve");
        }

        _device.BindImage(0, accum, ImageAccess.Write);
        _device.BindImage(1, accumPosition, ImageAccess.Write);

        _device.SetTexture(1, probe);
        _device.SetTexture(2, sceneDepth);
        _device.SetTexture(3, sceneNormal);
        _device.SetTexture(4, history);
        _device.SetTexture(5, historyPosition);
        _device.SetTexture(6, probePosition);
        _device.SetTexture(7, sceneVelocity.IsValid ? sceneVelocity : sceneNormal);

        _device.SetComputeUniform(_screenResolve, "uProbe", 1);
        _device.SetComputeUniform(_screenResolve, "uSceneDepth", 2);
        _device.SetComputeUniform(_screenResolve, "uSceneNormal", 3);
        _device.SetComputeUniform(_screenResolve, "uHistory", 4);
        _device.SetComputeUniform(_screenResolve, "uHistoryPosition", 5);
        _device.SetComputeUniform(_screenResolve, "uProbePosition", 6);
        _device.SetComputeUniform(_screenResolve, "uVelocity", 7);
        _device.SetComputeUniform(_screenResolve, "uHasVelocity", sceneVelocity.IsValid ? 1 : 0);
        _device.SetComputeUniform(_screenResolve, "uProbeWorldToView0", probeWorldToView.Column1);
        _device.SetComputeUniform(_screenResolve, "uProbeWorldToView1", probeWorldToView.Column2);
        _device.SetComputeUniform(_screenResolve, "uProbeWorldToView2", probeWorldToView.Column3);
        _device.SetComputeUniform(_screenResolve, "uProbeCameraToCache", probeCameraToCache);
        _device.SetComputeUniform(_screenResolve, "uProbeStep", Math.Max(probeStep, 1));
        _device.SetComputeUniform(_screenResolve, "uProbeFrame", probeFrame);

        _device.SetComputeUniform(
            _screenResolve, "uUnproject", unproject.XScale, unproject.YScale, unproject.Near, unproject.Far);
        DepthUnprojection resolvePrevious = previousUnproject ?? unproject;
        _device.SetComputeUniform(
            _screenResolve,
            "uPrevUnproject",
            resolvePrevious.XScale,
            resolvePrevious.YScale,
            resolvePrevious.Near,
            resolvePrevious.Far);
        _device.SetComputeUniform(_screenResolve, "uViewToWorld0", viewToWorld.Column1);
        _device.SetComputeUniform(_screenResolve, "uViewToWorld1", viewToWorld.Column2);
        _device.SetComputeUniform(_screenResolve, "uViewToWorld2", viewToWorld.Column3);
        _device.SetComputeUniform(_screenResolve, "uCameraToCache", cameraToCache);
        _device.SetComputeUniform(_screenResolve, "uPrevWorldToView0", prevWorldToView.Column1);
        _device.SetComputeUniform(_screenResolve, "uPrevWorldToView1", prevWorldToView.Column2);
        _device.SetComputeUniform(_screenResolve, "uPrevWorldToView2", prevWorldToView.Column3);
        _device.SetComputeUniform(_screenResolve, "uPrevCameraToCache", prevCameraToCache);
        _device.SetComputeUniform(_screenResolve, "uScreenWidth", screenWidth);
        _device.SetComputeUniform(_screenResolve, "uScreenHeight", screenHeight);
        _device.SetComputeUniform(_screenResolve, "uHasHistory", hasHistory ? 1 : 0);
        _device.SetComputeUniform(_screenResolve, "uMaxFrames", Math.Max(maxFrames, 1));

        _device.Dispatch(_screenResolve, (screenWidth + 7) / 8, (screenHeight + 7) / 8);
        _device.MemoryBarrier(MemoryBarrierScope.Image | MemoryBarrierScope.TextureFetch);
    }

    /// <summary>Spatially denoises the accumulated probe atlas, plane-weighted, into a separate atlas.</summary>
    /// <param name="accumulated">The accumulated irradiance to filter.</param>
    /// <param name="positions">The probe world positions, for the plane weight.</param>
    /// <param name="normals">The scene's G-buffer normals, for the facing weight.</param>
    /// <param name="filtered">The atlas to write the denoised result into, read by the deferred fill.</param>
    /// <param name="gridWidth">The probe grid's width.</param>
    /// <param name="gridHeight">The probe grid's height.</param>
    /// <param name="radius">How many probes each way the blur reaches.</param>
    /// <remarks>
    /// L6c's spatial half of the denoise: the per-frame gather is noisy and the temporal accumulation
    /// resets along every depth edge, so the probes freshest near a wall carry the raw estimate, which
    /// reads as a shimmer. Blending each probe with its neighbours -- weighted by how near their world
    /// positions sit, so light does not cross an edge -- smooths that. Writes a separate atlas rather
    /// than the accumulation itself, so the history the next frame reprojects onto stays unfiltered and
    /// the blur does not compound.
    /// </remarks>
    public void FilterScreen(
        TextureHandle accumulated,
        TextureHandle positions,
        TextureHandle normals,
        TextureHandle filtered,
        int gridWidth,
        int gridHeight,
        int radius)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (gridWidth <= 0 || gridHeight <= 0 || radius <= 0)
        {
            return;
        }

        if (!_screenGatherFilter.IsValid)
        {
            _screenGatherFilter = _device.CreateComputeShader(
                ShaderModuleId.CardsScreenFilter, ShaderVariant.Default, "cards.screen.filter");
        }

        _device.BindImage(0, filtered, ImageAccess.Write);
        _device.SetTexture(1, accumulated);
        _device.SetTexture(2, positions);
        _device.SetTexture(3, normals);

        _device.SetComputeUniform(_screenGatherFilter, "uAccum", 1);
        _device.SetComputeUniform(_screenGatherFilter, "uPosition", 2);
        _device.SetComputeUniform(_screenGatherFilter, "uSceneNormal", 3);
        _device.SetComputeUniform(_screenGatherFilter, "uWidth", gridWidth);
        _device.SetComputeUniform(_screenGatherFilter, "uHeight", gridHeight);
        _device.SetComputeUniform(_screenGatherFilter, "uRadius", radius);

        _device.Dispatch(_screenGatherFilter, (gridWidth + 7) / 8, (gridHeight + 7) / 8);
        _device.MemoryBarrier(MemoryBarrierScope.Image | MemoryBarrierScope.TextureFetch);
    }

    /// <summary>How many frames the radiance cache spreads a full grid sweep across.</summary>
    /// <remarks>
    /// One Z-slab of probes is traced each frame and the cursor walks the depth, so the whole grid is
    /// refreshed every this-many frames. The same amortisation the steady-state <see cref="Bounce"/> uses,
    /// and sound for the same reason: the geometry and the sun are static, so a probe traced this frame
    /// holds the answer it would trace several frames from now, and the running average carries it between
    /// its updates.
    /// </remarks>
    private const int CacheFrameSpread = 4;

    /// <summary>How many rays each cache probe traces per update. A full sphere, unlike the gather's hemisphere.</summary>
    private const int CacheRays = 48;

    /// <summary>Gets whether the radiance cache has been allocated and can be updated and sampled.</summary>
    public bool RadianceCacheReady => _cacheConfigured;

    /// <summary>Gets the cache's red-channel L1-SH volume, for the deferred pass to sample.</summary>
    public TextureHandle RadianceCacheRed => _cacheRed;

    /// <summary>Gets the cache's green-channel L1-SH volume.</summary>
    public TextureHandle RadianceCacheGreen => _cacheGreen;

    /// <summary>Gets the cache's blue-channel L1-SH volume.</summary>
    public TextureHandle RadianceCacheBlue => _cacheBlue;

    /// <summary>Gets the cache's distance-moment volume, for the Chebyshev leak test.</summary>
    public TextureHandle RadianceCacheDistance => _cacheDistance;

    /// <summary>Gets the world position of the cache probe at grid index (0, 0, 0), in metres.</summary>
    public Vector3d RadianceCacheOrigin => _cacheOriginWorld;

    /// <summary>Gets the distance between neighbouring cache probes, in metres.</summary>
    public float RadianceCacheSpacing => _cacheSpacing;

    /// <summary>Gets how many probes the cache spans along X.</summary>
    public int RadianceCacheCountX => _cacheCountX;

    /// <summary>Gets how many probes the cache spans along Y.</summary>
    public int RadianceCacheCountY => _cacheCountY;

    /// <summary>Gets how many probes the cache spans along Z.</summary>
    public int RadianceCacheCountZ => _cacheCountZ;

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
    public void ConfigureRadianceCache(Vector3d worldOrigin, float spacing, int countX, int countY, int countZ)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

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
    public void UpdateRadianceCache(
        GlobalDistanceField? shadows, Vector3f skyColour, int maxFrames, float scale, float skyReject)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_cacheConfigured || shadows is null || _lookup is null)
        {
            return;
        }

        EnsureTraceMode();
        if (!_cacheUpdate.IsValid)
        {
            _cacheUpdate = _device.CreateComputeShader(
                ShaderModuleId.CardsRadianceCache,
                new ShaderVariant(_tracesRays ? "rayquery" : "software"),
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
        _device.BindStorageBuffer(3, _lookup.Cells);
        _device.BindStorageBuffer(4, _lookup.Pages);

        _device.BindImage(0, _cacheRed, ImageAccess.ReadWrite);
        _device.BindImage(1, _cacheGreen, ImageAccess.ReadWrite);
        _device.BindImage(2, _cacheBlue, ImageAccess.ReadWrite);
        _device.BindImage(3, _cacheDistance, ImageAccess.ReadWrite);

        _device.SetTexture(1, _device.CardAlbedo);
        _device.SetTexture(4, _device.CardDepth);
        _device.SetTexture(6, FinalLighting);

        _device.SetComputeUniform(_cacheUpdate, "uSkyColour", skyColour);
        _device.SetComputeUniform(_cacheUpdate, "uBounceScale", RenderCVars.GiBounce.Value);
        WriteSkyHarmonics(_cacheUpdate);
        _device.SetComputeUniform(_cacheUpdate, "uAtlasSize", new Vector3f(Width, Height, 0f));
        _device.SetComputeUniform(_cacheUpdate, "uLookupBuckets", SurfaceCacheLookup.Buckets);
        _device.SetComputeUniform(_cacheUpdate, "uLookupMin", _lookup.Minimum);
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

        BindShadows(_cacheUpdate, shadows);

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
    public void ReleaseRadianceCache()
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

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _device.DestroyStorageBuffer(_tiles);
        _device.DestroyStorageBuffer(_lights);
        _device.DestroyStorageBuffer(_lampMapRecords);
        _device.DestroyStorageBuffer(_pages);
        _device.DestroyStorageBuffer(_probePositions);
        _device.DestroyStorageBuffer(_activeTiles);
        _device.DestroyComputeShader(_shader);

        if (_screenGather.IsValid)
        {
            _device.DestroyComputeShader(_screenGather);
        }

        if (_screenGatherTemporal.IsValid)
        {
            _device.DestroyComputeShader(_screenGatherTemporal);
        }

        if (_pixelGather.IsValid)
        {
            _device.DestroyComputeShader(_pixelGather);
        }

        if (_screenGatherFilter.IsValid)
        {
            _device.DestroyComputeShader(_screenGatherFilter);
        }

        if (_screenResolve.IsValid)
        {
            _device.DestroyComputeShader(_screenResolve);
        }

        if (_cacheUpdate.IsValid)
        {
            _device.DestroyComputeShader(_cacheUpdate);
        }

        ReleaseRadianceCache();

        _device.DestroyTexture(DirectLighting);
        _device.DestroyTexture(IndirectLighting);
        _device.DestroyTexture(FinalLighting);
    }

    /// <summary>The middle of everything lit, which the GPU's floats are measured from.</summary>
    private static Vector3d CentreOf(SurfaceCacheInstances instances)
    {
        if (instances.Instances.Length == 0)
        {
            return default;
        }

        Vector3d total = default;

        foreach (SurfaceCacheInstances.Placed placed in instances.Instances)
        {
            total = new Vector3d(
                total.X + placed.Position.X, total.Y + placed.Position.Y, total.Z + placed.Position.Z);
        }

        int count = instances.Instances.Length;
        return new Vector3d(total.X / count, total.Y / count, total.Z / count);
    }

    /// <summary>Packs every page's record, and cuts the pages into tiles.</summary>
    private static (float[] Pages, int[] Tiles) Describe(
        SurfaceCacheCapture.Result capture, SurfaceCacheInstances instances, Vector3d origin)
    {
        SurfaceCacheLayout layout = instances.Layout;
        float[] packed = new float[layout.Pages.Length * FloatsPerPage];
        List<int> tiles = [];

        foreach (SurfaceCacheInstances.Placed placed in instances.Instances)
        {
            for (int slot = 0; slot < placed.PageCount; slot++)
            {
                int page = placed.FirstPage + slot;
                SurfaceCacheLayout.Page lightPage = layout.Pages[page];

                if (!lightPage.IsAllocated)
                {
                    continue;
                }

                int cardIndex = instances.CardForPage[page];
                MeshCard card = capture.Cards[cardIndex];
                SurfaceCacheLayout.Page cardPage = capture.Layout.Pages[cardIndex];

                int at = page * FloatsPerPage;

                packed[at + 0] = lightPage.X;
                packed[at + 1] = lightPage.Y;
                packed[at + 2] = lightPage.Width;
                packed[at + 3] = lightPage.Height;

                packed[at + 4] = cardPage.X;
                packed[at + 5] = cardPage.Y;
                packed[at + 6] = cardPage.Width;
                packed[at + 7] = cardPage.Height;

                packed[at + 8] = card.Centre.X;
                packed[at + 9] = card.Centre.Y;
                packed[at + 10] = card.Centre.Z;
                packed[at + 11] = card.HalfWidth;

                packed[at + 12] = card.Right.X;
                packed[at + 13] = card.Right.Y;
                packed[at + 14] = card.Right.Z;
                packed[at + 15] = card.HalfHeight;

                packed[at + 16] = card.Up.X;
                packed[at + 17] = card.Up.Y;
                packed[at + 18] = card.Up.Z;
                packed[at + 19] = card.Depth;

                packed[at + 20] = card.Direction.X;
                packed[at + 21] = card.Direction.Y;
                packed[at + 22] = card.Direction.Z;
                packed[at + 23] = 0f;

                WritePlacement(packed, at, card, placed.Position, placed.Orientation, origin);

                // Cut into tiles now, on the CPU. A work group takes one of these and knows its page
                // without searching for it; a page that moves keeps its tiles, which address the atlas.
                int across = (lightPage.Width + TileSize - 1) / TileSize;
                int down = (lightPage.Height + TileSize - 1) / TileSize;

                for (int y = 0; y < down; y++)
                {
                    for (int x = 0; x < across; x++)
                    {
                        tiles.Add(page);
                        tiles.Add(x);
                        tiles.Add(y);
                        tiles.Add(0);
                    }
                }
            }
        }

        return (packed, tiles.Count > 0 ? [.. tiles] : [0, 0, 0, 0]);
    }

    /// <summary>Writes where one page's placement stands: its rotation, its position and its world bound.</summary>
    /// <param name="packed">Every page's record.</param>
    /// <param name="at">Where this page's record starts.</param>
    /// <param name="card">The page's card, in the model's own space.</param>
    /// <param name="position">Where the placement stands.</param>
    /// <param name="orientation">How it is turned.</param>
    /// <param name="origin">The level origin the cache works relative to.</param>
    private static void WritePlacement(
        float[] packed, int at, MeshCard card, Vector3d position, Matrix3f orientation, Vector3d origin)
    {
        // The placement's rotation, a row per register, with its position relative to the level
        // origin riding in the spare component. This is the model-to-world rotation, so the
        // shader applies it transposed -- matching how SceneDistanceField reaches world space.
        Matrix3f rotation = orientation;
        Vector3d placedPosition = position;

        packed[at + 24] = rotation.M11;
        packed[at + 25] = rotation.M12;
        packed[at + 26] = rotation.M13;
        packed[at + 27] = (float)(placedPosition.X - origin.X);

        packed[at + 28] = rotation.M21;
        packed[at + 29] = rotation.M22;
        packed[at + 30] = rotation.M23;
        packed[at + 31] = (float)(placedPosition.Y - origin.Y);

        packed[at + 32] = rotation.M31;
        packed[at + 33] = rotation.M32;
        packed[at + 34] = rotation.M33;
        packed[at + 35] = (float)(placedPosition.Z - origin.Z);

        // A world-space bounding sphere for the page, so the card lookup can reject a candidate
        // whose card cannot reach this point with one buffer read instead of the nine it takes to
        // project onto it -- the busiest grid cell holds most of a room's pages and the walk tests
        // every one, almost all nowhere near the point. Built from the card's eight corners put
        // through the very transform the lookup and SurfaceCacheLookup.TryBounds use (the placement
        // rotation carries scale, so a world radius cannot be inferred from the local half-extents),
        // with the depth range widened by the skin the projection tolerates. The AABB of those
        // corners bounds every point the projection can accept; the sphere bounds the AABB. It is
        // therefore never smaller than it must be, so no point the walk would have kept is rejected
        // -- the picture is identical, the walk merely shorter.
        float px = (float)(placedPosition.X - origin.X);
        float py = (float)(placedPosition.Y - origin.Y);
        float pz = (float)(placedPosition.Z - origin.Z);
        float loX = float.MaxValue, loY = float.MaxValue, loZ = float.MaxValue;
        float hiX = float.MinValue, hiY = float.MinValue, hiZ = float.MinValue;
        for (int corner = 0; corner < 8; corner++)
        {
            float cu = (corner & 1) == 0 ? -card.HalfWidth : card.HalfWidth;
            float cv = (corner & 2) == 0 ? -card.HalfHeight : card.HalfHeight;
            float cw = (corner & 4) == 0 ? CardSkin : -(card.Depth + CardSkin);

            float lx = card.Centre.X + (card.Right.X * cu) + (card.Up.X * cv) + (card.Direction.X * cw);
            float ly = card.Centre.Y + (card.Right.Y * cu) + (card.Up.Y * cv) + (card.Direction.Y * cw);
            float lz = card.Centre.Z + (card.Right.Z * cu) + (card.Up.Z * cv) + (card.Direction.Z * cw);

            float wx = px + (rotation.M11 * lx) + (rotation.M21 * ly) + (rotation.M31 * lz);
            float wy = py + (rotation.M12 * lx) + (rotation.M22 * ly) + (rotation.M32 * lz);
            float wz = pz + (rotation.M13 * lx) + (rotation.M23 * ly) + (rotation.M33 * lz);

            loX = MathF.Min(loX, wx); hiX = MathF.Max(hiX, wx);
            loY = MathF.Min(loY, wy); hiY = MathF.Max(hiY, wy);
            loZ = MathF.Min(loZ, wz); hiZ = MathF.Max(hiZ, wz);
        }

        float ex = (hiX - loX) * 0.5f, ey = (hiY - loY) * 0.5f, ez = (hiZ - loZ) * 0.5f;
        packed[at + 36] = (loX + hiX) * 0.5f;
        packed[at + 37] = (loY + hiY) * 0.5f;
        packed[at + 38] = (loZ + hiZ) * 0.5f;
        packed[at + 39] = (1.001f * MathF.Sqrt((ex * ex) + (ey * ey) + (ez * ez))) + 1e-3f;
    }
}
