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
    public int TemporalFrames { get => _relight.TemporalFrames; set => _relight.TemporalFrames = value; }

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
    public int BounceFrameSpread { get => _relight.BounceFrameSpread; set => _relight.BounceFrameSpread = value; }

    /// <summary>Where the steady-state refresh is centred, relative to this cache's own origin.</summary>
    /// <remarks>The camera, in practice. Meaningless unless <see cref="ActiveRadius"/> is positive.</remarks>
    public Vector3f ActiveCentre { get => _relight.ActiveCentre; set => _relight.ActiveCentre = value; }

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
    public float ActiveRadius { get => _relight.ActiveRadius; set => _relight.ActiveRadius = value; }

    /// <summary>Gets or sets whether the whole-cache slices recorded now belong to the load-time convergence.</summary>
    /// <remarks>Only how they are named in the timings; the convergence and the far refresh share the slices.</remarks>
    public bool Converging { get => _relight.Converging; set => _relight.Converging = value; }

    /// <summary>Gets or sets the fewest tiles a steady-state slice dispatches; see <see cref="SurfaceCacheRelight.MinSliceGroups"/>.</summary>
    public int MinSliceTiles { get => _relight.MinSliceTiles; set => _relight.MinSliceTiles = value; }

    /// <summary>How many texels lie between probes each way.</summary>
    /// <remarks>
    /// Must match the shader's <c>kProbeMask</c>, which is this less one: the mask picks out a probe as
    /// the texels whose low bits are clear, so a spacing of four is a mask of three.
    /// </remarks>
    private const int ProbeSpacing = 4;

    private readonly IGraphicsDevice _device;
    private readonly SurfaceCacheBindings _bindings;
    private readonly SurfaceCacheMovers _movers;
    private readonly SurfaceCacheLocalLights _localLights;
    private readonly SurfaceCacheRelight _relight;
    private readonly ScreenProbeGather _screen;
    private readonly SurfaceCacheRadianceVolume _radiance;
    private readonly StorageBufferHandle _pages;
    private readonly StorageBufferHandle _tiles;
    private readonly StorageBufferHandle _probePositions;
    private readonly StorageBufferHandle _activeTiles;
    private readonly Vector3d _origin;
    private bool _disposed;

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
        _pages = pages;
        _tiles = tiles;
        _probePositions = probePositions;
        _activeTiles = activeTiles;
        _origin = origin;

        DirectLighting = direct;
        IndirectLighting = indirect;
        FinalLighting = final;
        Width = width;
        Height = height;

        _bindings = new SurfaceCacheBindings(
            device, origin, device.Capabilities.SupportsRayQuery && RenderCVars.HardwareRayTracing.Value);
        _movers = new SurfaceCacheMovers(device, pages, origin, pageRecords, cards, instances);
        _localLights = new SurfaceCacheLocalLights(device, logger, origin);
        _relight = new SurfaceCacheRelight(
            device, _bindings, _localLights, EnsureTraceMode, shader, pages, tiles, probePositions, activeTiles,
            tilePages, pageSpheres, probeWidth, probeHeight, tileCount, direct, indirect, final, width, height);
        _screen = new ScreenProbeGather(device, _bindings, EnsureTraceMode, pages, final, width, height);
        _radiance = new SurfaceCacheRadianceVolume(
            device, logger, _bindings, EnsureTraceMode, origin, pages, final, width, height);
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

        if (_bindings.Lookup is not { } lookup)
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
        Vector3d origin = SurfaceCachePageRecords.CentreOf(instances);

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

            (float[] packed, int[] tiles) = SurfaceCachePageRecords.Describe(capture, instances, origin);

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

            int pageCount = packed.Length / SurfaceCachePageRecords.FloatsPerPage;
            float[] pageSpheres = new float[pageCount * 4];
            for (int p = 0; p < pageCount; p++)
            {
                int from = (p * SurfaceCachePageRecords.FloatsPerPage) + 36;
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
                + $"{SurfaceCachePageRecords.TileSize} square, into a {layout.Width}x{layout.Height} atlas.");

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
        _movers.MovePlacement(placement, position, orientation);
    }

    /// <summary>Uploads the pages moved this frame, and the movers' list if it grew.</summary>
    /// <remarks>
    /// Only the span from the first moved page to the last: a level's fans and radar heads turn every frame,
    /// and the whole record buffer is megabytes.
    /// </remarks>
    public void CommitMoves()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _movers.CommitMoves(_bindings.Lookup);
    }

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
        _relight.Light(sunDirection, sunColour, skyColour, shadows, probe, localScale, amortize);
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
        _relight.ConvergeLightSlice(sunDirection, sunColour, skyColour, shadows, slice, sliceCount, localScale);
    }

    /// <summary>Seeds one slice of the final atlas from the direct term, the first step of a spread convergence.</summary>
    /// <param name="shadows">The field the bounce traces against; nothing happens without one.</param>
    /// <param name="boost">The albedo boost, see <see cref="DefaultBoost"/>.</param>
    /// <param name="slice">Which slice, from zero.</param>
    /// <param name="sliceCount">How many slices the cache is divided into.</param>
    public void ConvergeSeedSlice(GlobalDistanceField? shadows, float boost, int slice, int sliceCount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _relight.ConvergeSeedSlice(shadows, boost, slice, sliceCount);
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
        _relight.ConvergeBounceSlice(shadows, boost, pass, slice, sliceCount);
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
        _relight.Bounce(shadows, boost, iterations, temporalIndex);
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

        _bindings.Lookup = lookup;

        // A lookup built afresh knows nothing of what has already moved.
        _movers.LookupReplaced();
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
        _localLights.SetLocalLights(fixtures);
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
        _localLights.SetLampMapEntries(entryPerFixture);
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
    public void SetTerrainHeights(TextureHandle heights, ShelterHeightField? field, float scale) =>
        _bindings.SetTerrainHeights(heights, field, scale);

    /// <summary>Hands the untraced gather the level's roof raster, or takes it away.</summary>
    /// <param name="roofs">The packed raster, as the shading reads it, or none.</param>
    /// <param name="transform">How to read it; <see cref="ShelterMapTransform.None"/> with none.</param>
    /// <remarks>
    /// A ray marched out of doors that lands on a surface with a roof over it went through a wall or a roof
    /// the field could not hold at that distance; the gather counts it as a miss rather than carrying the
    /// room's lamps out onto the wall outside.
    /// </remarks>
    public void SetRoofs(TextureHandle roofs, ShelterMapTransform transform) => _bindings.SetRoofs(roofs, transform);

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
        _localLights.SetLocalLightLevels(board);
    }

    /// <summary>Recompiles the tracing passes when the trace mode has been switched live.</summary>
    /// <remarks>
    /// The trace tier is baked into each pass's source — <c>VisibilityAt</c> is either a ray query or a
    /// distance-field march — so flipping <c>r_rt</c> cannot be a uniform. The stale shaders are
    /// destroyed here and each pass recreates its own on next use, exactly as on first use. The resolve
    /// and filter passes do not trace and are left alone.
    /// </remarks>
    private void EnsureTraceMode()
    {
        bool want = _device.Capabilities.SupportsRayQuery && RenderCVars.HardwareRayTracing.Value;
        if (want == _bindings.TracesRays)
        {
            return;
        }

        _bindings.TracesRays = want;
        _relight.DropShader();
        _screen.DropTracingShaders();
        _radiance.DropShader();
    }

    /// <inheritdoc cref="ScreenProbeGather.GatherScreen"/>
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
        _screen.GatherScreen(
            shadows, sceneDepth, sceneNormal, unproject, viewToWorld, cameraToCache, screenWidth, screenHeight,
            probeStep, output);
    }

    /// <inheritdoc cref="ScreenProbeGather.GatherPixels"/>
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
        return _screen.GatherPixels(
            shadows, sceneDepth, sceneNormal, unproject, viewToWorld, cameraToCache, screenWidth, screenHeight,
            skyColour, frame, output, sceneColour, screenTraceSteps, screenTraceDistance, screenThickness,
            colourWorldToView, colourCameraToCache);
    }

    /// <inheritdoc cref="ScreenProbeGather.GatherScreenTemporal"/>
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
        _screen.GatherScreenTemporal(
            shadows, sceneDepth, sceneNormal, unproject, viewToWorld, cameraToCache, screenWidth, screenHeight,
            probeStep, accumulated, positions, history, positionHistory, prevWorldToView, prevCameraToCache,
            frameIndex, maxAccumulated, hasHistory, sceneColour, screenTraceSteps, screenTraceDistance,
            screenThickness, skyColour, colourWorldToView, colourCameraToCache, raw, sceneVelocity,
            previousUnproject);
    }

    /// <inheritdoc cref="ScreenProbeGather.ResolveScreenTemporal"/>
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
        _screen.ResolveScreenTemporal(
            probe, sceneDepth, sceneNormal, unproject, viewToWorld, cameraToCache, prevWorldToView,
            prevCameraToCache, accum, accumPosition, history, historyPosition, screenWidth, screenHeight,
            hasHistory, maxFrames, probePosition, probeWorldToView, probeCameraToCache, probeStep,
            sceneVelocity, previousUnproject, probeFrame);
    }

    /// <inheritdoc cref="ScreenProbeGather.FilterScreen"/>
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
        _screen.FilterScreen(
            accumulated, positions, normals, filtered, gridWidth, gridHeight, radius);
    }

    /// <summary>Gets whether the radiance cache has been allocated and can be updated and sampled.</summary>
    public bool RadianceCacheReady => _radiance.RadianceCacheReady;

    /// <summary>Gets the cache's red-channel L1-SH volume, for the deferred pass to sample.</summary>
    public TextureHandle RadianceCacheRed => _radiance.RadianceCacheRed;

    /// <summary>Gets the cache's green-channel L1-SH volume.</summary>
    public TextureHandle RadianceCacheGreen => _radiance.RadianceCacheGreen;

    /// <summary>Gets the cache's blue-channel L1-SH volume.</summary>
    public TextureHandle RadianceCacheBlue => _radiance.RadianceCacheBlue;

    /// <summary>Gets the cache's distance-moment volume, for the Chebyshev leak test.</summary>
    public TextureHandle RadianceCacheDistance => _radiance.RadianceCacheDistance;

    /// <summary>Gets the world position of the cache probe at grid index (0, 0, 0), in metres.</summary>
    public Vector3d RadianceCacheOrigin => _radiance.RadianceCacheOrigin;

    /// <summary>Gets the distance between neighbouring cache probes, in metres.</summary>
    public float RadianceCacheSpacing => _radiance.RadianceCacheSpacing;

    /// <summary>Gets how many probes the cache spans along X.</summary>
    public int RadianceCacheCountX => _radiance.RadianceCacheCountX;

    /// <summary>Gets how many probes the cache spans along Y.</summary>
    public int RadianceCacheCountY => _radiance.RadianceCacheCountY;

    /// <summary>Gets how many probes the cache spans along Z.</summary>
    public int RadianceCacheCountZ => _radiance.RadianceCacheCountZ;

    /// <inheritdoc cref="SurfaceCacheRadianceVolume.ConfigureRadianceCache"/>
    public void ConfigureRadianceCache(Vector3d worldOrigin, float spacing, int countX, int countY, int countZ)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _radiance.ConfigureRadianceCache(worldOrigin, spacing, countX, countY, countZ);
    }

    /// <inheritdoc cref="SurfaceCacheRadianceVolume.UpdateRadianceCache"/>
    public void UpdateRadianceCache(
        GlobalDistanceField? shadows, Vector3f skyColour, int maxFrames, float scale, float skyReject)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _radiance.UpdateRadianceCache(shadows, skyColour, maxFrames, scale, skyReject);
    }

    /// <summary>Drops the radiance cache's volumes, for a level change or shutdown.</summary>
    public void ReleaseRadianceCache() => _radiance.ReleaseRadianceCache();

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _device.DestroyStorageBuffer(_tiles);
        _localLights.Destroy();
        _device.DestroyStorageBuffer(_pages);
        _device.DestroyStorageBuffer(_probePositions);
        _device.DestroyStorageBuffer(_activeTiles);
        _relight.DestroyShader();
        _screen.DestroyShaders();
        _radiance.Destroy();

        _device.DestroyTexture(DirectLighting);
        _device.DestroyTexture(IndirectLighting);
        _device.DestroyTexture(FinalLighting);
    }
}
