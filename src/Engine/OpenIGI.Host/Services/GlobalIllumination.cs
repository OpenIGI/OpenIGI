using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Threading;
using OpenIGI.Game.World;
using OpenIGI.Host.Lighting;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Lighting;
using OpenIGI.Rendering;

namespace OpenIGI.Host.Services;

/// <summary>What the real-time lighting model reads of the host: the level up now, its sun, and the caches it keys by.</summary>
internal interface IGlobalIlluminationHost
{
    /// <summary>Gets the level's placed objects, once walked.</summary>
    WorldScene? Scene { get; }

    /// <summary>Gets the world's renderer, which captures the surface cache.</summary>
    IWorldPresenter? World { get; }

    /// <summary>Gets the level's authored sun.</summary>
    DirectionalLightSettings? Sun { get; }

    /// <summary>Gets the sun this frame lights with.</summary>
    DirectionalLightSettings? EffectiveSun { get; }

    /// <summary>Gets whether a level is up, played or behind the menu.</summary>
    bool HasLevel { get; }

    /// <summary>Gets the mission whose level is up, for keying its caches.</summary>
    int? CurrentMissionId { get; }

    /// <summary>Gets the rendered frame's number.</summary>
    int FrameCount { get; }

    /// <summary>Gets the cache the level's distance fields are kept in.</summary>
    DistanceFieldCache FieldCache { get; }

    /// <summary>The sun the frame lights with, as the clock and the level's night set it.</summary>
    /// <returns>The sun.</returns>
    DirectionalLightSettings CycleSun();

    /// <summary>The sky's fill under this frame's sun.</summary>
    /// <returns>The colour.</returns>
    Vector3f SkyFillColour();

    /// <summary>Applies the lite backdrop's overrides, which decide whether either structure is wanted at all.</summary>
    void UpdateBackdropProfile();

    /// <summary>Brings the terrain's height raster up to date for the frame's traces.</summary>
    void UpdateTerrainRaster();

    /// <summary>Describes the distance field the frame's traces march, around an eye.</summary>
    /// <param name="eye">The eye.</param>
    /// <returns>The binding.</returns>
    DistanceFieldBinding DescribeDistanceField(Vector3d eye);

    /// <summary>Gets the level's baked probe field, which the surface cache's radiance is gathered against, or null before it lands.</summary>
    IrradianceVolume? ProbeField { get; }
}

/// <summary>
/// The real-time lighting model's structures: the level's distance field and the lit surface cache, built when the model is
/// asked for, converged so a level opens lit, kept current as the sun and the lamps change, and the lamps they light from.
/// </summary>
/// <param name="device">The device the structures live on.</param>
/// <param name="logger">Where builds and convergence are reported.</param>
/// <param name="work">The workers the distance field bakes on.</param>
/// <param name="lockstep">Whether frames are captured in lockstep, which runs the convergence in one go.</param>
/// <param name="host">The level, its sun and the caches.</param>
internal sealed class GlobalIllumination(
    IGraphicsDevice device,
    IOpenIgiLogger logger,
    IWorkScheduler work,
    bool lockstep,
    IGlobalIlluminationHost host) : IHostService
{
    private readonly IGraphicsDevice _device = device;
    private readonly IOpenIgiLogger _logger = logger;
    private readonly IWorkScheduler _work = work;
    private readonly bool _lockstep = lockstep;
    private readonly IGlobalIlluminationHost _host = host;

    /// <summary>How long after the sun last moved the GI keeps following it closely, in frames.</summary>
    /// <remarks>
    /// Long enough for the near cache to be relit a few times over at <c>GameHost.GiSunFollowSpread</c>, so a
    /// scrub that stops lands on the new hour's light and the slow blend takes over from there.
    /// </remarks>
    // How far the eye may move between two frames and still count as the same shot for the screen GI's
    // histories. Metres; a sprint covers a fraction of this in a frame, a cut or a teleport covers more.
    private const double GiCutDistance = 2.0;

    // How far the projection's scale may change between two frames and still count as the same shot: a tenth.
    // A cinematic zoom moves it a ten-thousandth a frame and is reprojected through; a scope snaps it by far
    // more than this in one, and its history is the unzoomed view's.
    private const float GiCutZoom = 0.1f;

    // Stand-down state for the surface-cache retrace. The world-space relight and the amortised bounce
    // re-derive the answer the cache already holds whenever nothing has moved -- and "moved" for a
    // VIEW-INDEPENDENT cache means the distance field scrolled new coverage in (a real walk of metres) or
    // the sun was scrubbed, NOT the camera turning or drifting a few centimetres. Once the cache has settled
    // after the last such change the retrace idles, which is what takes out the periodic multi-millisecond
    // spike that halved the frame rate at rest. It resumes the instant a level scrolls or the sun moves.
    private int _giStaticFrames;

    /// <summary>How many frames a card's estimate averages over while following a moving sun.</summary>
    /// <remarks>
    /// Short enough to keep up with a scrub, long enough that a card's single noisy bounce does not show:
    /// at one, every relight replaced the last outright and the walls clouded over while the key was held.
    /// </remarks>
    private const int GiSunFollowTemporalFrames = 4;
    private bool _giSunValid;
    private Vector3f _lastGiSunDirection;
    private Vector3f _lastGiSunColour;

    /// <summary>How many frames the near relight is spread over while following a moving sun.</summary>
    private const int GiSunFollowSpread = 4;

    // The shortest the retrace keeps running after the last change before it idles. The real window is
    // worked out per frame from the amortisation spread and the temporal frames -- see where retraceGi is
    // decided -- so the cache has converged before it stops.
    private const int GiSettleFrames = 64;

    public const int GiSunFollowFrames = 24;

    // How long that warm-up lasts. The radiance cache's running average settles over maxFrames sweeps and
    // one sweep is spread over several frames, so full convergence is a couple of seconds; this covers it.
    public const int GiExposureWarmupFrames = 150;

    /// <summary>Gets the lit surface cache, once captured and lit.</summary>
    public SurfaceCacheLighting? CardLighting { get; private set; }

    private SurfaceCacheLookup? _cardLookup;
    private SurfaceCacheCapture.Result? _capturedCards;
    private SurfaceCacheInstances? _cardInstances;

    /// <summary>Gets the distance-field bake in flight, if any.</summary>
    public DistanceFieldBake? FieldBake { get; private set; }

    /// <summary>Gets the level's exact distance field, which the clipmaps are checked against.</summary>
    public SceneDistanceField? DistanceFieldScene { get; private set; }

    /// <summary>Gets the clipmaps the level's distance field is resampled into around the camera, once baked.</summary>
    public GlobalDistanceField? DistanceField { get; private set; }

    /// <summary>Gets or sets whether the clipmaps have been checked against the exact field this level.</summary>
    public bool FieldValidated { get; set; }

    // When the surface cache's emitter buffer last matched the switchboard. The bounce is a GPU upload
    // rather than a uniform, so it follows a flickering lamp a few frames behind rather than every frame;
    // the cache's own accumulation is slower than that anyway.
    /// <summary>Gets or sets the switchboard revision the cache's emitters last matched.</summary>
    public int LampBounceRevision { get; set; } = -1;

    // Frames left in the exposure warm-up: for a moment after a level's GI starts converging, the adapted
    // exposure eases fast so it tracks the light filling in rather than over-lifting the dark first frames
    // and then walking visibly back down. Reset when the surface cache is (re)built, counted down per frame.
    /// <summary>Gets or sets the frames left in the exposure warm-up after the cache is (re)built.</summary>
    public int ExposureWarmup { get; set; }

    /// <summary>Gets or sets how many times the bounce has been relit since the cache was (re)built.</summary>
    public int CardBounceFrame { get; set; }

    // Frames since the sun last moved, for the fast follow a clock scrub needs. See GiSunFollowFrames.
    /// <summary>Gets or sets the frames since the sun last moved, for the fast follow a clock scrub needs.</summary>
    public int SunMovedFrames { get; set; } = GiSunFollowFrames + 1;

    // Which sub-meshes of this level are lamps, so a shared texture glows on the fixture and not on the
    // furniture that happens to be skinned with the same page. Held here because the world renderer is
    // rebuilt per mission and this outlives the rebuild.
    /// <summary>Gets which sub-meshes of the level are lamps.</summary>
    public LampDetector.Lamps Lamps { get; private set; } = LampDetector.Lamps.None;

    // The level's inferred lamp fixtures, kept so the frame loop can submit them to the deferred lighting
    // as real point lights each frame (transformed into view space there), the honest way to light an
    // interior the screen gather cannot reach. Also bounced through the surface cache. Empty until a
    // mission loads one; see EmissiveLightProposer.
    /// <summary>Gets the level's lamps, as fixtures.</summary>
    public IReadOnlyList<LightFixture> LightFixtures { get; private set; } = [];

    // Which of those lamps are actually lit at this moment, and how far through switching on or out they
    // are. Built with the fixtures, and read by all three of the things a lamp is -- the punctual light
    // below, the emission the world renderer puts on the fixture's own geometry, and the emitters the
    // surface cache bounces off -- so none of them can disagree about a lamp being off. Null until a
    // mission loads fixtures; see LampSwitchboard.
    /// <summary>Gets what decides each lamp's output, once a level has fixtures.</summary>
    public LampSwitchboard? LampSwitchboard { get; private set; }

    // Which mission's lamps are applied, so a promotion does not survey a level whose backdrop already
    // did -- that survey was the largest single cost of pressing Play.
    /// <summary>Gets the mission whose lamps are applied.</summary>
    public int LampsMissionId { get; private set; } = -1;

    /// <inheritdoc/>
    public string Name => "Global illumination";

    // The density and resolution the structures currently in memory were actually built at, so a change
    // to either can be noticed and rebuilt from rather than quietly ignored until the next level load.
    private float _builtCardDensity;
    private int _builtFieldResolution;

    // What has already been ATTEMPTED, as opposed to what succeeded. Both builders can fail and leave
    // nothing behind -- a level whose every model is spline-bent captures no cards at all, and a field
    // bake can come back with nothing to upload -- and both are called once a frame now. Without this,
    // one failure becomes a full re-traversal of every placement, every frame, for as long as the level
    // is up: the cheapest way to turn a missing feature into an unplayable one. A retry needs a reason,
    // and the reason is that one of the inputs changed.
    private float _attemptedCardDensity = float.NaN;
    private int _attemptedFieldResolution;

    // Whether this level's cache has already been solved forward. Both halves arrive independently -- the
    // field can land after the cards or before them -- so whichever completes the pair starts it, and this
    // is what stops the other one starting it again.
    private bool _giConverged;

    /// <summary>The most whole-cache bounces a load-time convergence folds in, whatever r_gi_converge says.</summary>
    private const int MaximumConvergencePasses = 3;

    // The convergence in flight, stepped a few slices a frame from the render loop. See
    // SurfaceCacheConvergence for why it is spread rather than run: as one call it was a multi-second
    // stall, and one that landed mid-play whenever the field arrived after the level did.
    private SurfaceCacheConvergence? _giConvergence;

    // The next slice of the whole-cache refresh. See StepRelight.
    private int _giRefreshSlice;

    // Whole sweeps the refresh has completed, which is what advances its jitter from one sweep to the next.
    private int _giRefreshRounds;

    // What the refresh last saw its inputs as, and how many whole sweeps it has finished since any of them
    // changed. See StepRelight.
    private Vector3f _giRefreshSunDirection;
    private Vector3f _giRefreshSunColour;
    private int _giRefreshLampRevision = -1;

    // The coarse lamp maps as the far refresh last saw them: while they are being drawn the far cache has
    // to be relit, or the cards lit through a wall before a lamp's maps existed would keep that light.
    private int _giRefreshBounceMapVersion = -1;
    private int _giRefreshQuietRounds;
    private bool _giRefreshWorldSettled;

    // The rendered frame the refresh last advanced on; see StepRelight.
    private int _giRefreshLastFrame = -1;

    /// <summary>How many slices of the whole cache the far refresh relights a frame while the sun is moving.</summary>
    /// <remarks>
    /// Three rather than eight. Each slice is a relight and a bounce of a 128th of the whole level, about one
    /// and a half milliseconds at ultra, and eight of them took the frame from a hundred frames a second to
    /// thirty-three for as long as the scrub key was held. Three keep a whole pass inside a second at sixty,
    /// still quicker than the scrub crosses dusk.
    /// </remarks>
    private const int GiSunFollowSlices = 3;

        /// <summary>How many whole sweeps the far refresh runs after its inputs last changed, before it idles.</summary>
    /// <remarks>
    /// One sweep relights every card under the new inputs; the rest fold more bounce in, at the steady
    /// bounce's weight, so far rooms reach their multi-bounce answer rather than stopping at one. At the
    /// ultra preset's 128 slices this is about eight seconds at sixty frames a second.
    /// </remarks>
    private const int FarRefreshQuietRounds = 4;

    // How many frames the load-time convergence has waited for the world's structure to settle. See
    // BeginConvergence.
    private int _giSettleWaitFrames;

    /// <summary>The longest the convergence waits for the traced world to stop growing, in frames.</summary>
    /// <remarks>
    /// Ten seconds at sixty. A level that never settles -- something keeps arriving at the edge of the
    /// admission radius -- converges against what it has rather than not at all, and the refresh sweep
    /// corrects it as the rest arrives.
    /// </remarks>
    private const int GiSettleTimeoutFrames = 600;

    // Which mission's lamps are applied, so a promotion does not survey a level whose backdrop already
    // did -- that survey was the largest single cost of pressing Play.


    /// <summary>
    /// The lighting model the frame may actually use, which is the one asked for only once the structures
    /// it lights from exist.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>dynamic</c> DISCARDS the artists' bakes: it does not sample an object lightmap at all. Everything
    /// it puts in their place — the screen probe gather, the world radiance cache, the lamps — reads the
    /// surface cache and the distance field, and both of those are built rather than switched on. So the
    /// instant between asking for dynamic lighting and having something to light with is a frame with the
    /// bakes thrown away and nothing yet to replace them: a flat, wrong picture.
    /// </para>
    /// <para>
    /// That instant used to be permanent. <c>r_cards</c> and <c>r_sdf</c> were <c>RequiresRestart</c> and
    /// built only during a mission load, so choosing Ultra from the settings menu mid-session — or moving
    /// the Global Illumination row — asked for a lighting model whose inputs could not arrive until the
    /// player restarted the game. Nothing said so; the frame simply went flat.
    /// </para>
    /// <para>
    /// Reading the model through here instead makes the switch safe from both ends. <see cref="EnsureStructures"/>
    /// starts building the moment the model is asked for, and until it finishes this reports
    /// <see cref="LightingModel.Baked"/> — so the frame keeps the lighting it already had and crosses over
    /// when there is something better to cross to. There is never a wrong frame, only an early one.
    /// </para>
    /// </remarks>
    public LightingModel EffectiveLighting =>
        RenderCVars.Lighting.Value is var wanted && wanted == LightingModel.Baked ? wanted
        : Ready ? wanted
        : LightingModel.Baked;

    /// <summary>Whether everything the dynamic model lights from is built and usable.</summary>
    /// <remarks>
    /// Both structures, not either: a gather with a distance field and no surface cache traces to a hit it
    /// cannot read the colour of, and one with a cache and no field cannot find the hit at all. The device
    /// check is the same one the builders make — without compute there is no field to build, so a level
    /// loaded on such a device stays on the bakes however the preset is set.
    /// </remarks>
    public bool Ready =>
        CardLighting is not null && DistanceField is not null && _device.Capabilities.SupportsCompute;

    /// <summary>Whether the frame wants the dynamic model at all, on a device that could run it.</summary>
    public bool Wanted =>
        RenderCVars.Lighting.Value == LightingModel.Dynamic && _device.Capabilities.SupportsCompute;

    /// <summary>
    /// Whether anything the dynamic model needs is still on its way: a field baking or not yet attempted, a
    /// cache not yet captured, or a convergence that has not finished.
    /// </summary>
    /// <remarks>
    /// This is what a loading screen waits on. It is deliberately "still coming" rather than "not ready":
    /// a level whose capture found nothing, or whose bake produced nothing, is not going to change, and a
    /// gate that waited for it would hold a panel over a level that was as lit as it would ever be.
    /// </remarks>
    public bool Pending
    {
        get
        {
            if (!Wanted || _host.Scene is null)
            {
                return false;
            }

            bool fieldWanted = RenderCVars.DistanceFields.Value;
            bool fieldPending = fieldWanted && DistanceField is null
                && (FieldBake is not null || _attemptedFieldResolution != RenderCVars.DistanceFieldResolution.Value);

            bool cardsWanted = RenderCVars.SurfaceCache.Value && _host.World is not null;
            bool cardsPending = cardsWanted && _capturedCards is null
                && !RenderCVars.SurfaceCacheDensity.Value.Equals(_attemptedCardDensity);

            bool convergencePending = IsConverging
                || (Ready && WantsConvergence && !_giConverged);

            return fieldPending || cardsPending || convergencePending;
        }
    }

    /// <summary>Whether a convergence is stepping right now.</summary>
    public bool IsConverging => _giConvergence is { IsComplete: false };

    /// <summary>How far the convergence has come, or one when there is none to wait for.</summary>
    public float ConvergeProgress => _giConvergence?.Progress ?? (_giConverged ? 1f : 0f);

    private static bool WantsConvergence =>
        RenderCVars.GiConverge.Value > 0 && RenderCVars.SurfaceCacheProbe.Value == 0;

    // The mission whose level is up, played or previewed, for keying its caches.
    private int? CurrentMissionId => _host.CurrentMissionId;

    /// <summary>
    /// Builds whatever the real-time lighting model needs and does not yet have, and throws away whatever
    /// it no longer needs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called once a frame from the render loop as well as during a mission load, which is what lets the
    /// settings menu turn real-time global illumination on in a running level. The work is the same work
    /// the load does; only its trigger is new.
    /// </para>
    /// <para>
    /// The costs are not symmetrical and it matters which is which. The distance-field bake is a worker
    /// thread that the loop already polls, so asking for it mid-session costs nothing on the frame it is
    /// asked for. The surface-cache capture is a render pass over every distinct model and cannot be
    /// anything else — the atlas is a framebuffer and the geometry belongs to this context — so it lands as
    /// one long frame. That is a visible hitch, and it is the honest price of the switch; it is logged so
    /// that a hitch nobody asked about can be explained afterwards.
    /// </para>
    /// <para>
    /// A backdrop counts. The menu's level used to be a picture and never gathered, which meant it was lit
    /// by the artists' bakes while the mission it became was lit by the real-time model -- and the picture
    /// changed the moment Play was pressed. Building the structures for the backdrop too is what makes
    /// the menu show the level the player is about to be standing in.
    /// </para>
    /// </remarks>
    public void EnsureStructures()
    {
        // First, because the lite backdrop's overrides decide whether either structure is wanted at all.
        _host.UpdateBackdropProfile();

        if (_host.Scene is null || !_host.HasLevel)
        {
            return;
        }

        EnsureDistanceFields();
        EnsureSurfaceCache();
    }

    public void EnsureDistanceFields()
    {
        bool wanted = RenderCVars.DistanceFields.Value && _device.Capabilities.SupportsCompute;
        int resolution = RenderCVars.DistanceFieldResolution.Value;

        if (!wanted)
        {
            DiscardDistanceFields();
            return;
        }

        // A resolution change invalidates the built field rather than being ignored until the next load:
        // the voxel grid IS the resolution, so there is nothing to adjust in place.
        if (DistanceField is not null && resolution != _builtFieldResolution)
        {
            DiscardDistanceFields();
        }

        if (DistanceField is not null || FieldBake is not null)
        {
            return;
        }

        // Nothing built, nothing baking, and this resolution has already been tried: the bake produced
        // nothing and starting it again would produce nothing again.
        if (resolution == _attemptedFieldResolution)
        {
            return;
        }

        _attemptedFieldResolution = resolution;
        _builtFieldResolution = resolution;

        // The cache first, and this is the whole point of it: a hit means the lighting model engages on
        // the NEXT frame rather than several seconds later, so the level opens already lit instead of
        // opening on the artists' lightmaps and crossing over while the player watches.
        if (CurrentMissionId is { } missionId
            && _host.FieldCache.TryLoad(missionId, resolution) is { } cached)
        {
            AdoptDistanceField(cached, DistanceFieldAtlas.Build(cached.Fields), fromCache: true);
            return;
        }

        FieldBake = DistanceFieldBake.Start(_host.Scene!.Placements, _work, resolution, _logger);
        _logger.Information(
            "GlobalIllumination",
            $"Baking the level's distance fields at {resolution} voxels; the level will light from the "
            + "artists' lightmaps until it lands, then cross over. The result is cached, so this happens "
            + "once per mission.");
    }

    /// <summary>The clipmaps' shape for the trace this device runs.</summary>
    /// <remarks>
    /// Twice the resolution where nothing is traced: a quarter-metre voxel near the eye and half a metre a
    /// building away do not hold the slab over a doorway or a canopy's roof, and the software gathers'
    /// rays slipped through it on some frames and not others -- which a night of small bright lamp pools
    /// showed as clouds crawling over every wall, fifteen per cent of the bounce changing frame to frame
    /// where the traced gather changed half of one. Eight megabytes, and about a millisecond of rebuilds
    /// while the camera travels. Where rays are traced the field answers only far and coarse questions,
    /// and keeps the cheaper shape.
    /// </remarks>
    private GlobalDistanceFieldSettings DistanceFieldSettings() =>
        _device.Capabilities.SupportsRayQuery && RenderCVars.HardwareRayTracing.Value
            ? GlobalDistanceFieldSettings.Default
            : GlobalDistanceFieldSettings.Default with { Resolution = 128 };

    /// <summary>Takes a baked field into use, and writes it to the cache when it was freshly baked.</summary>
    /// <param name="scene">The level's field.</param>
    /// <param name="atlas">Every distinct model's field, packed for the GPU.</param>
    /// <param name="fromCache">
    /// Whether this came off disk, in which case there is nothing to write back.
    /// </param>
    /// <remarks>
    /// One place, because the field arrives by four routes -- off the worker on an ordinary load, off the
    /// worker mid-level when the lighting model is switched on, straight off disk, and with a backdrop that
    /// read or baked it behind the menu -- and all of them have to produce the same clipmaps and leave the
    /// same state behind. Uploading the atlas is why this cannot live on the worker: a GPU texture belongs
    /// to the context that made it.
    /// </remarks>
    public void AdoptDistanceField(SceneDistanceField scene, DistanceFieldAtlas atlas, bool fromCache)
    {
        DistanceFieldScene = scene;
        FieldValidated = false;
        DistanceField?.Dispose();
        DistanceField = GlobalDistanceField.Create(_device, scene, atlas, DistanceFieldSettings(), _logger);

        BeginConvergence();

        if (fromCache || CurrentMissionId is not { } missionId)
        {
            return;
        }

        // Written under the resolution it was actually baked at rather than whatever the variable says
        // now: the player can move r_sdf_resolution while a bake is in flight, and a file keyed by the
        // new value would be a field that does not match its own key.
        _host.FieldCache.Save(missionId, _builtFieldResolution, scene);
    }

    public void EnsureSurfaceCache()
    {
        bool wanted = RenderCVars.SurfaceCache.Value && _host.World is not null;
        float density = RenderCVars.SurfaceCacheDensity.Value;

        if (!wanted)
        {
            DiscardSurfaceCache();
            return;
        }

        // As with the field: the atlas is sized by the density, so a change means a fresh capture.
        if (_capturedCards is not null && !density.Equals(_builtCardDensity))
        {
            DiscardSurfaceCache();
        }

        if (_capturedCards is not null || density.Equals(_attemptedCardDensity))
        {
            return;
        }

        _attemptedCardDensity = density;
        _builtCardDensity = density;

        // Timed, because whether this is worth caching to disk is a question about how long it takes
        // against how large the atlas is, and neither number was being reported. The capture logs its own
        // atlas dimensions and occupancy; this adds what it cost. A cache would have to read
        // width x height x 16 bytes back from disk and upload it, so it only pays if this is slow AND the
        // atlas is small -- which is a measurement, not a guess.
        var captureClock = System.Diagnostics.Stopwatch.StartNew();

        // The expensive half of the guard above: this walks every placement and bakes each distinct
        // model's cards before it can discover it has nothing to capture.
        if (_host.World!.CaptureSurfaceCache(_host.Scene!, density) is not { } cards)
        {
            _logger.Warning(
                "GlobalIllumination",
                "This level captured no surfaces, so it keeps the lighting it already has.");
            return;
        }

        captureClock.Stop();

        if (RenderCVars.SurfaceCacheDump.Value is { Length: > 0 } into)
        {
            SurfaceCacheDebugDumps.CardAtlas(_device, cards.Capture, into, _logger);
        }

        _capturedCards = cards.Capture;
        _cardInstances = cards.Instances;
        _logger.Information(
            "GlobalIllumination",
            $"Captured the level's surfaces at {density} texels per unit for real-time lighting "
            + $"in {captureClock.ElapsedMilliseconds} ms.");

        RebuildCardLighting();
        BeginConvergence();
    }

    /// <summary>
    /// Starts solving the surface cache forward to its multi-bounce equilibrium, so a level opens at the
    /// lighting it settles on rather than climbing towards it in front of the player.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two separate ramps are being collapsed here, and the direct one is the bigger of the two. In the
    /// steady state the shadowed relight walks the cache a slice at a time over <c>r_gi_amortize</c>
    /// frames -- that spread is what keeps a clipmap scroll from stalling the frame -- so a level that
    /// simply starts running opens with its cards lit but UNSHADOWED and darkens as the slices arrive.
    /// The convergence relights the whole cache once, with a field to trace shadows against.
    /// </para>
    /// <para>
    /// The second is the bounce, which is a progressive solve: each pass folds one more bounce into the
    /// last one's result at a weight of one over one plus the pass. Doing those passes one a frame in
    /// front of the player is the visible "lighting up".
    /// </para>
    /// <para>
    /// The whole cache rather than the active radius, deliberately. The radius exists to keep the
    /// STEADY-STATE refresh off cards the player is nowhere near, and it is centred on the camera -- which
    /// at this point in a load is not yet where the player will stand. Converging the level once costs a
    /// bounded amount of load time and means walking somewhere new does not re-run the ramp.
    /// </para>
    /// <para>
    /// Started here and STEPPED by <see cref="StepConvergence"/> from the frame loop, rather than run to
    /// completion: as one call it measured two and a half seconds on a large level, and it ran whenever the
    /// second half of the pair landed, which on a cold cache was several seconds into play. Spread over
    /// frames the same work goes on behind the loading screen, or under the held shot, while the picture
    /// keeps presenting. A lockstep capture still runs it in one go, because a capture has to be the same
    /// frame every time.
    /// </para>
    /// </remarks>
    private void BeginConvergence()
    {
        int passes = RenderCVars.GiConverge.Value;

        if (_giConverged || _giConvergence is not null || !WantsConvergence)
        {
            return;
        }

        if (CardLighting is not { } cards || DistanceField is not { } field)
        {
            // Not a failure: this runs from both halves of the pair and the first one to arrive has
            // nothing to converge yet. Said out loud anyway, because a convergence that silently does not
            // happen is invisible -- the level simply opens on the old ramp, which is exactly what it
            // looks like when the feature is off. That cost an afternoon once.
            _logger.Information(
                "GlobalIllumination",
                $"Cannot converge yet: the surface cache is {(CardLighting is null ? "not built" : "ready")} "
                + $"and the distance field is {(DistanceField is null ? "not built" : "ready")}. "
                + "Whichever arrives second will converge.");
            return;
        }

        // Not against the stand-in. Until the level's own acceleration structure exists, a tracing pass
        // binds one that every ray misses, so a convergence run now would light every surface as if no
        // wall stood between it and any lamp or the sun -- and the result is kept. The frame loop asks
        // again every frame until it can.
        if (!_device.WorldTraceReady)
        {
            return;
        }

        // And not against a world still filling in. The structure admits a few meshes a frame from what
        // the frames draw and offer, so for a while after it is ready most of the level is missing from
        // it -- and every wall missing is a wall this one-off lighting shines through. Wait until nothing
        // new has arrived for a while, or give up waiting and let the refresh sweep catch up.
        if (!_device.WorldTraceSettled && _giSettleWaitFrames++ < GiSettleTimeoutFrames)
        {
            return;
        }

        // The sun the frame will light with, derived the same way -- NOT _host.EffectiveSun ?? _host.Sun. During a
        // load the first is not set for this level yet, and the second is the level's raw authored
        // Dirlight, which every Project I.G.I. mission authors as a noon sun: a night mission's whole
        // cache was converged in daylight. See CycleSun.
        DirectionalLightSettings sun = _host.CycleSun();
        _giRefreshSlice = 0;
        _giRefreshQuietRounds = 0;
        _giRefreshWorldSettled = false;

        // Whole-cache bounces, so each one is worth a full pass -- and capped, because that is what makes
        // them affordable. The old one-shot convergence ran two whole-cache iterations and then seven
        // steady-state SLICES, which covered barely half the cache once; its eight "bounces" were mostly
        // not. Three full passes here are more bounce than that for about the same GPU time (measured
        // around 350 ms per whole-cache dispatch on the largest level), and the blend weight of one over
        // one plus the pass means the passes past the third carry almost none of the difference.
        int fullPasses = Math.Min(passes, MaximumConvergencePasses);

        // The same effective sun the deferred lighting shades with -- colour through both scales -- or
        // the cache converges towards a different key than the screen will show it under.
        _giConvergence = new SurfaceCacheConvergence(
            _device,
            cards,
            field,
            CacheSunDirection(sun),
            sun.Colour * (RenderCVars.SunScale.Value * RenderCVars.LightScale.Value),
            SkyLight.Fill(sun),
            RenderCVars.GlobalIlluminationBoost.Value,
            fullPasses);

        if (_lockstep)
        {
            _giConvergence.RunAll();
            FinishConvergence();
        }
    }

    /// <summary>Runs this frame's share of the convergence, if one is in flight. Once per frame.</summary>
    public void StepConvergence()
    {
        if (_giConvergence is null || _giConvergence.IsComplete)
        {
            // Nothing in flight. A level whose cache and field are both here but which has not converged
            // was waiting on the world's structure; ask again. One that has converged is relit when what it
            // was lit by has moved on.
            if (!_giConverged)
            {
                if (CardLighting is not null && DistanceField is not null)
                {
                    BeginConvergence();
                }
            }
            else
            {
                StepRelight();
            }

            if (_giConvergence is null || _giConvergence.IsComplete)
            {
                return;
            }
        }

        SurfaceCacheConvergence job = _giConvergence;

        if (CardLighting is null || DistanceField is null)
        {
            // Whatever it was converging has been discarded under it.
            _giConvergence = null;
            return;
        }

        job.Step();
        if (job.IsComplete)
        {
            FinishConvergence();
        }
    }

    /// <summary>Refreshes one slice of the WHOLE cache, direct light and one bounce, every frame.</summary>
    /// <remarks>
    /// <para>
    /// The steady-state refresh only reaches cards within <c>r_gi_cache_radius</c> of the camera, and the
    /// load-time convergence runs once. So whatever the far cards were converged under stayed there: a
    /// world structure still filling in (walls not yet admitted, and so not in anyone's shadow), a clock
    /// that has moved on, lamps that arrived or were switched after it. The player saw it put right only
    /// by walking up to each place in turn.
    /// </para>
    /// <para>
    /// The cache is kept current with a per-frame budget of card updates over the whole scene rather
    /// than a radius around the eye: the whole cache is swept a slice a frame, the direct term relit
    /// under this frame's sun and lamps and a bounce folded in at half weight, so a far surface follows
    /// every change within a few seconds and never pops -- both halves blend. The slices near the camera
    /// are refreshed twice, which costs little and changes nothing.
    /// </para>
    /// <para>
    /// Relighting the direct term alone, as this first did, changed nothing anyone could see: the far
    /// cards show the FINAL atlas, which only the bounce writes, and the steady bounce is radius-culled
    /// too.
    /// </para>
    /// </remarks>
    private void StepRelight()
    {
        // Once per RENDERED frame. This runs once per pass of the loop, and a pass that only ticks the
        // simulation renders nothing: its slice landed in the next frame beside that frame's own, so one frame
        // in every few carried two -- every fourth at a 30 Hz simulation under a 120 Hz picture, and on
        // mission 3's large cache that was a 3.5 ms frame becoming a 7 ms one, over and over.
        if (_host.FrameCount == _giRefreshLastFrame)
        {
            return;
        }

        _giRefreshLastFrame = _host.FrameCount;

        int slices = RenderCVars.GiFarRefresh.Value;
        if (slices <= 0
            || CardLighting is not { } cards
            || DistanceField is not { } field
            || _host.EffectiveSun is not { } sun)
        {
            return;
        }

        // Idle once nothing it is lit by has changed for a few whole sweeps. It ran every frame for good,
        // re-deriving the same far cache over and over: a slice of the WHOLE level, unculled, which at
        // ultra was some two and a half milliseconds of every frame -- the largest single cost in the GI.
        // What wakes it is exactly the list above: the sun (a scrub or a moving clock), a lamp switched,
        // and a traced world still taking in walls after the load. Only until that world has first
        // settled: past then it is view-dependent -- what leaves the view expires and comes back when the
        // player turns -- so it "grows" on every turn of the head, and waking on that kept the sweep going
        // for good while telling the far cards nothing new.
        Vector3f sunColour = sun.Colour * (RenderCVars.SunScale.Value * RenderCVars.LightScale.Value);
        Vector3f sunDirection = CacheSunDirection(sun);
        bool worldSettled = _device.WorldTraceSettled;
        _giRefreshWorldSettled |= worldSettled;
        bool changed = (!_giRefreshWorldSettled && !worldSettled)
            || LampBounceRevision != _giRefreshLampRevision
            || _device.LampBounceMapVersion != _giRefreshBounceMapVersion
            || !NearlyEqual(sunDirection, _giRefreshSunDirection)
            || !NearlyEqual(sunColour, _giRefreshSunColour);

        if (changed)
        {
            _giRefreshQuietRounds = 0;
            _giRefreshLampRevision = LampBounceRevision;
            _giRefreshBounceMapVersion = _device.LampBounceMapVersion;
            _giRefreshSunDirection = sunDirection;
            _giRefreshSunColour = sunColour;
        }
        else if (_giRefreshQuietRounds >= FarRefreshQuietRounds)
        {
            return;
        }

        // While the clock is being scrubbed the whole cache has to keep up with the sun, so the sweep runs
        // several slices a frame: at one a frame a full pass took two seconds, and every distant surface --
        // the hills, the far side of the yard -- went on returning the day's light to everything that
        // gathered from it, so a scrub into the night left the world lit under a black sky. Only while the
        // sun moves; at rest the sweep goes back to one slice a frame.
        int steps = SunMovedFrames <= GiSunFollowFrames ? Math.Min(GiSunFollowSlices, slices) : 1;
        for (int step = 0; step < steps; step++)
        {
            StepRelightSlice(cards, field, sun, sunDirection, sunColour, slices);
        }
    }

    /// <summary>Relights and bounces one slice of the whole cache, and moves the sweep on.</summary>
    private void StepRelightSlice(
        SurfaceCacheLighting cards,
        GlobalDistanceField field,
        DirectionalLightSettings sun,
        Vector3f sunDirection,
        Vector3f sunColour,
        int slices)
    {
        int slice = _giRefreshSlice % slices;
        _giRefreshSlice = (slice + 1) % slices;
        if (_giRefreshSlice == 0)
        {
            _giRefreshRounds = (_giRefreshRounds + 1) & 0x3fff;
            _giRefreshQuietRounds++;
        }

        cards.ConvergeLightSlice(
            sunDirection,
            sunColour,
            SkyLight.Fill(sun),
            field,
            slice,
            slices);

        // At the steady bounce's own weight and a new jitter every sweep. It passed pass 1, which blends at
        // one half and jitters every probe the same way every time: each sweep dragged every tile, the
        // ones by the player included, halfway to one fixed sixteen-ray estimate, and once the steady
        // bounce idled that estimate was all the cache held -- the per-probe blotches every ray read back.
        int steady = Math.Max(RenderCVars.GiTemporalFrames.Value, 1);
        // Right after a change -- a scrub of the clock, a lamp switched -- the first sweeps take the new
        // light at half weight, so the far cache follows the hour instead of easing towards it over the long
        // steady blend; the sweeps after fold bounce in at that steady weight as before.
        int pass = _giRefreshQuietRounds < 2 ? 1 : steady + 1 + _giRefreshRounds;
        cards.ConvergeBounceSlice(
            field, RenderCVars.GlobalIlluminationBoost.Value, pass: pass, slice, slices);
    }

    private static bool NearlyEqual(Vector3f a, Vector3f b) =>
        MathF.Abs(a.X - b.X) <= 1e-4f && MathF.Abs(a.Y - b.Y) <= 1e-4f && MathF.Abs(a.Z - b.Z) <= 1e-4f;

    // A sun under the horizon lights nothing directly, and the cache is told so with no direction at all.
    private static Vector3f CacheSunDirection(DirectionalLightSettings sun) =>
        sun.IsBelowHorizon ? default : sun.Direction;

    private void FinishConvergence()
    {
        if (_giConvergence is not { } job)
        {
            return;
        }

        _giConverged = true;

        // The frame loop picks the accumulation up from here rather than restarting it, so the passes
        // just run are not repeated at full weight on the first frames.
        CardBounceFrame = job.Passes;

        // Nothing for the eye adaptation to chase: the first frame is already the converged brightness,
        // so the fast warm-up rate would only make the exposure hunt around an answer it already has.
        ExposureWarmup = 0;

        _logger.Information(
            "GlobalIllumination",
            $"Converged the surface cache through {job.Passes} bounce(s) over {job.ElapsedMilliseconds} ms of "
            + "presented frames, so the level opens lit rather than filling in.");
        _giConvergence = null;
    }

    public void DiscardDistanceFields()
    {
        _giConverged = false;
        _giSettleWaitFrames = 0;
        _giConvergence = null;
        // Clearing the latch as well as the structures, so the next level -- or the next resolution --
        // gets its own attempt rather than inheriting this one's verdict.
        _attemptedFieldResolution = 0;
        FieldBake?.Cancel();
        FieldBake = null;
        DistanceField?.Dispose();
        DistanceField = null;
        DistanceFieldScene = null;
        FieldValidated = false;
    }

    public void DiscardSurfaceCache()
    {
        _giConverged = false;
        _giSettleWaitFrames = 0;
        _giConvergence = null;
        _attemptedCardDensity = float.NaN;
        _capturedCards = null;
        _cardInstances = null;
        CardLighting?.Dispose();
        CardLighting = null;
        _cardLookup?.Dispose();
        _cardLookup = null;
    }

    /// <summary>Puts a level's proposed lamps into every place that reads them.</summary>
    /// <param name="lamps">The level's fixtures and glowing geometry, or none.</param>
    /// <param name="missionId">The mission they belong to, which seeds the switchboard.</param>
    /// <remarks>
    /// The lamps a level implies but never declares, used in three places: as EMISSION on the fixture
    /// geometry itself, so a lamp looks like the thing doing the lighting; as real lights in the deferred
    /// pass, so a windowless room has a source at all; and as emitters the surface cache bounces off,
    /// where there is one. All three matter together -- a room lit from a fixture that does not glow
    /// reads as lit by nothing, and a fixture that glows without lighting reads as a decal.
    /// Shared by the backdrop and the played mission, so a promoted level keeps the lamps its backdrop
    /// was already showing rather than surveying them a second time.
    /// </remarks>
    public void ApplyLamps(EmissiveLightProposer.Proposal lamps, int missionId)
    {
        LampsMissionId = missionId;
        LightFixtures = lamps.Fixtures;

        // One switchboard per set of fixtures, seeded by the mission so two levels' broken tubes do not
        // stutter in step. Everything that reads a lamp's output reads it from here, and everything that
        // holds one is handed the same instance rather than a copy of its state.
        LampSwitchboard = new LampSwitchboard(lamps.Fixtures, missionId);
        LampBounceRevision = LampSwitchboard.Revision;

        // Which sub-meshes glow and how brightly. Per sub-mesh rather than per texture, because one
        // texture is not one brightness -- a floodlight and the lamp over a door draw with the same page
        // -- and because a lamp's page also skins the furniture beside it. r_lamp_emissive scales the
        // whole set on the way in rather than in the shader: it is a property of the material, so it
        // belongs where the material is decided and costs nothing per pixel.
        float emissiveScale = RenderCVars.LampEmissiveScale.Value;

        Lamps = emissiveScale == 1f
            ? lamps.Lamps
            : lamps.Lamps with
            {
                SubMeshes = lamps.Lamps.SubMeshes.ToDictionary(
                    e => e.Key, e => e.Value * emissiveScale, StringComparer.OrdinalIgnoreCase),
            };

        if (_host.World is not null)
        {
            _host.World.Lamps = Lamps;
            _host.World.Switchboard = LampSwitchboard;
        }

        // The lit cache reads the fixtures as area emitters; one built before they were known has to be
        // told about them. The refresh sweep carries them to the far cards.
        CardLighting?.SetLocalLights(LightFixtures);
    }

    /// <summary>Lights the captured card atlas from the level's own sun, sky and fixtures.</summary>
    /// <remarks>
    /// The pages hold unlit albedo by design, so this is the only thing that puts light into the cache. It
    /// runs once per capture, because this game's sun is a fixed direction per mission — the bargain the
    /// whole surface cache is built on — and again only if the capture itself is rebuilt.
    /// </remarks>
    private void RebuildCardLighting()
    {
        if (_capturedCards is not { } surfaces || _cardInstances is not { } placedCards)
        {
            return;
        }

        CardLighting?.Dispose();
        CardLighting = SurfaceCacheLighting.Create(_device, surfaces, placedCards, _logger);

        // A fresh pass has no history to accumulate onto, so the next bounce converges in one shot.
        CardBounceFrame = 0;

        // The cache now climbs from dark to its full bounce over the frames that follow; hold the exposure
        // on a fast rate for that stretch so it tracks the light in rather than dimming after.
        ExposureWarmup = GiExposureWarmupFrames;

        if (CardLighting is null)
        {
            return;
        }

        // And into the bounce: the fixtures are area emitters as far as the cache is concerned, so a
        // lamplit room lights its own walls off each other.
        CardLighting.SetLocalLights(LightFixtures);

        // Built once, from the same capture and placements the lighting was. A ray that stops somewhere
        // knows only that it stopped; this is what lets it read the surface it hit.
        _cardLookup?.Dispose();
        _cardLookup = SurfaceCacheLookup.Build(
            _device, surfaces, placedCards, CardLighting.Origin, _logger);
        CardLighting.SetLookup(_cardLookup);

        // A level with no Dirlight has no sun to light the cards with, so they take the sky alone -- which
        // is what such a level looks like anyway.
        //
        // Unshadowed here. At load the clipmaps do not exist yet -- the field bake is still on its worker --
        // so this establishes sun and sky, and the per-frame pass adds the shadow once there is a field to
        // trace against.
        CardLighting.Light(
            _host.Sun is { IsBelowHorizon: false } lit ? lit.Direction : default,
            _host.Sun?.Colour ?? default,
            _host.SkyFillColour());

        if (RenderCVars.SurfaceCacheDump.Value is { Length: > 0 } lightingInto)
        {
            SurfaceCacheDebugDumps.CardLighting(
                _device, CardLighting, lightingInto, _logger, 0, "cards-direct-load.png");
        }
    }

    /// <summary>Forgets the structures a session is about to replace, without freeing them.</summary>
    public void Forget()
    {
        CardLighting = null;
        _cardLookup = null;
        CardBounceFrame = 0;
        _capturedCards = null;
        _cardInstances = null;
        FieldBake = null;
        DistanceFieldScene = null;
        DistanceField = null;
        FieldValidated = false;
    }

    /// <summary>Takes a finished distance-field bake out of flight.</summary>
    /// <param name="baked">Receives what it produced.</param>
    /// <returns><see langword="true"/> when one finished.</returns>
    public bool TryCollectFieldBake(out DistanceFieldBake.Result baked)
    {
        if (FieldBake is { } pending && pending.TryCollect(out baked))
        {
            FieldBake = null;
            return true;
        }

        baked = default;
        return false;
    }

    /// <summary>Waits for the bake in flight and adopts it, as a capture must rather than race the worker.</summary>
    public void WaitForFieldBake()
    {
        if (FieldBake is { } pending && pending.WaitForResult() is { } ready)
        {
            FieldBake = null;
            AdoptDistanceField(ready.Scene, ready.Atlas, fromCache: false);
        }
    }

    /// <summary>Takes a backdrop's distance field, read or still baking, as the level's own.</summary>
    /// <param name="resolution">The resolution it was read or is being baked at.</param>
    /// <param name="read">The field read from the cache, if it was there.</param>
    /// <param name="baking">The bake in flight, if it was not.</param>
    public void AdoptBackdropField(int resolution, SceneDistanceField? read, DistanceFieldBake? baking)
    {
        if (!RenderCVars.DistanceFields.Value || !_device.Capabilities.SupportsCompute)
        {
            // Started before the lite profile was in force -- the very first backdrop is built before
            // there is one -- and wanted by nobody now. Play bakes its own if it needs one.
            baking?.Cancel();
            return;
        }

        _attemptedFieldResolution = resolution;
        _builtFieldResolution = resolution;

        if (read is not null)
        {
            AdoptDistanceField(read, DistanceFieldAtlas.Build(read.Fields), fromCache: true);
        }
        else if (baking is not null)
        {
            // Handed to the same per-frame collect a played mission's bake uses, which adopts it and
            // writes the cache when it lands -- under a held frame, so the menu does not show the
            // cross-over either.
            FieldBake = baking;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        CardLighting?.Dispose();
        _cardLookup?.Dispose();
        DistanceField?.Dispose();
    }

    /// <summary>Gathers the screen-space indirect light and resolves the deferred lighting.</summary>
    public void LightScene(FrameLoopState state, Camera3D eye, bool captured, SceneTargetDescription sceneTarget, Matrix4f unjitteredProjection)
    {
        // L6/L9: the live screen probe gather. Traced here, while the G-buffer the geometry pass just
        // filled is still live and BEFORE the pass closes -- because closing it runs the deferred
        // lighting, which reads the atlas as its diffuse fill. So the gather has to have written it
        // first. Only without a card probe, which fills the cache atlas with a diagnostic rather than
        // light. It replaces the ambient inside the shade rather than compositing over the frame, so
        // there is no ambient to zero and nothing runs after the lighting.
        const int giStep = 8;
        _host.UpdateTerrainRaster();
        // The probes' running average, capped at a temporal limit of ten frames. It was
        // thirty-two, and a history that long outlives every turn of the view -- each frame of
        // motion resamples it through the reprojection and clamps it into the window of one
        // frame's rays, and at one part in thirty-three the bias that leaves takes the better part
        // of a second to wash back out, which is the cloud that follows the camera and settles
        // after it stops.
        const int giMaxAccum = 10;

        // The screen trace tier: how far its rays reach across the frame and their step cap, plus the
        // surface thickness its hit test assumes. Metres, save the step count.
        const int giTraceSteps = 32;
        const float giTraceDistance = 24f;
        const float giTraceThickness = 0.5f;
        // The surface cache, handed to the backend for the reflection pass's traced half: a ray
        // that leaves the screen has no answer in the frame, and the cards are the only record of
        // what stands off it. Independent of whether the screen gather runs below -- the two read
        // the same cards for different questions, and a player who has turned the indirect light
        // off has not turned reflections off. Cleared to nothing while there is no lit cache, so
        // the backend traces nothing rather than against stale handles.
        _device.SetSurfaceCache(
            CardLighting is { } reflectionCards && RenderCVars.SurfaceCacheProbe.Value == 0
                ? reflectionCards.Describe(
                    new Vector3f(
                        (float)(eye.Position.X - reflectionCards.Origin.X),
                        (float)(eye.Position.Y - reflectionCards.Origin.Y),
                        (float)(eye.Position.Z - reflectionCards.Origin.Z)),
                    RenderCVars.GiBounce.Value)
                : SurfaceCacheBinding.None);

        // And the distance field, for the same passes and by the same rule. Where the adapter can
        // trace, the acceleration structure finds a reflection's hit; where it cannot, this is the
        // only description of the world a shader can walk, and without it glass reflected the sky
        // and nothing else. The boxes are CAMERA-RELATIVE, which is why this is handed over every
        // frame rather than when the level loads: they move with the camera the clipmaps centre on.
        _device.SetDistanceField(_host.DescribeDistanceField(eye.Position));

        float giStrength = RenderCVars.ScreenGather.Value;
        bool giActive = captured
            && giStrength > 0f
            && DistanceField is not null
            && CardLighting is not null
            && RenderCVars.SurfaceCacheProbe.Value == 0;

        // Every placement that moves, carried in the surface cache to where it was drawn this frame,
        // before anything below looks a surface up -- a truck the camera rides in reads its own
        // interior rather than the yard it drove out of. See SurfaceCacheLighting.MovePlacement.
        if (CardLighting is { } movingCards && _host.World is { } movingWorld && movingWorld.Movers.Count > 0)
        {
            PlacementPose pose = movingWorld.DrawPoses;
            foreach (WorldPlacement mover in movingWorld.Movers)
            {
                if (pose(mover, out Vector3d moverPosition, out Matrix3f moverOrientation))
                {
                    movingCards.MovePlacement(mover, moverPosition, moverOrientation);
                }
            }

            movingCards.CommitMoves();
        }

        // Under ray reconstruction the light goes to it raw: a ray at every pixel, and none of the probes'
        // pooling, cadence, accumulation or resolve below, which is exactly the denoising it replaces.
        bool giPixelsRan = false;
        if (giActive && _device.RawLighting && DistanceField is { } pixelField && CardLighting is { } pixelCards)
        {
            _device.EndStageTiming();
            _device.BeginStageTiming(RenderStage.GlobalIllumination);

            if (state.ScreenGi.PixelsWidth != sceneTarget.Width || state.ScreenGi.PixelsHeight != sceneTarget.Height)
            {
                if (state.ScreenGi.Pixels.IsValid)
                {
                    _device.DestroyTexture(state.ScreenGi.Pixels);
                }

                state.ScreenGi.Pixels = _device.CreateStorageTexture(
                    sceneTarget.Width, sceneTarget.Height, VolumeFormat.Rgba16F, "cards.pixel");
                state.ScreenGi.PixelsWidth = sceneTarget.Width;
                state.ScreenGi.PixelsHeight = sceneTarget.Height;
            }

            Vector3f pixelCameraToCache = new(
                (float)(eye.Position.X - pixelCards.Origin.X),
                (float)(eye.Position.Y - pixelCards.Origin.Y),
                (float)(eye.Position.Z - pixelCards.Origin.Z));

            giPixelsRan = pixelCards.GatherPixels(
                pixelField,
                _device.SceneDepth,
                _device.SceneNormal,
                DepthUnprojection.From(unjitteredProjection, ViewPlanes.NearMetres, ViewPlanes.FarMetres),
                eye.Orientation.Transposed(),
                pixelCameraToCache,
                sceneTarget.Width,
                sceneTarget.Height,
                _host.SkyFillColour(),
                state.ScreenGi.PixelFrame++,
                state.ScreenGi.Pixels,

                // Last frame's lit picture and the camera it was rendered from, as the probes read it.
                _device.SceneColour,
                RenderCVars.ScreenGatherTrace.Value ? giTraceSteps : 0,
                giTraceDistance,
                giTraceThickness,
                state.ScreenGi.ResolvePrevWorldToView,
                state.ScreenGi.ResolvePrevCameraToCache);

            if (giPixelsRan)
            {
                // The probes' histories go with the else below: they describe frames they did not see.
                _device.SetScreenIndirect(state.ScreenGi.Pixels, giStrength, fullResolution: true);
                state.ScreenGi.ContinuityValid = false;

                // This frame's camera is what next frame's lit picture will have been rendered from.
                state.ScreenGi.ResolvePrevWorldToView = eye.Orientation;
                state.ScreenGi.ResolvePrevCameraToCache = pixelCameraToCache;
            }

            _device.EndStageTiming();
        }

        if (!giPixelsRan && giActive && DistanceField is { } liveGiField && CardLighting is { } liveGiCards)
        {
            // Timed, at last. This gather is the most expensive thing in a real-time-GI frame and
            // NOTHING measured it: the bracket the surface-cache relight runs under closes long
            // before this, and the backend's own geometry range -- ranges cannot nest -- took over
            // the one the game opens around the world draws. So the overlay and the benchmark both
            // reported a frame four milliseconds shorter than the one being drawn, and the
            // difference read as a CPU cost that was never there. GlobalIllumination is the stage
            // its own documentation already says this belongs to; same-stage ranges in a frame sum,
            // so it joins the relight rather than displacing it.
            _device.EndStageTiming();
            _device.BeginStageTiming(RenderStage.GlobalIllumination);

            // The gather reads the G-buffer, which under a render scale is smaller than the
            // window — probes are laid over the pixels that exist, not the ones the window has.
            int giSourceWidth = sceneTarget.Width;
            int giSourceHeight = sceneTarget.Height;
            int wantWidth = (giSourceWidth + giStep - 1) / giStep;
            int wantHeight = (giSourceHeight + giStep - 1) / giStep;

            if (state.ScreenGi.IndirectWidth != wantWidth || state.ScreenGi.IndirectHeight != wantHeight)
            {
                // A resize drops the history: the atlases are a different shape, so nothing in them
                // reprojects onto the new frame.
                for (int i = 0; i < 2; i++)
                {
                    if (state.ScreenGi.Accum[i].IsValid)
                    {
                        _device.DestroyTexture(state.ScreenGi.Accum[i]);
                    }

                    if (state.ScreenGi.Position[i].IsValid)
                    {
                        _device.DestroyTexture(state.ScreenGi.Position[i]);
                    }

                    state.ScreenGi.Accum[i] = _device.CreateStorageTexture(
                        wantWidth, wantHeight, VolumeFormat.Rgba16F, "cards.screen.accum");
                    state.ScreenGi.Position[i] = _device.CreateStorageTexture(
                        wantWidth, wantHeight, VolumeFormat.Rgba16F, "cards.screen.accum.pos");
                }

                // The spatial denoise's output, regenerated each frame from the accumulation, so a
                // single buffer rather than a ping-pong pair.
                if (state.ScreenGi.Filtered.IsValid)
                {
                    _device.DestroyTexture(state.ScreenGi.Filtered);
                }

                state.ScreenGi.Filtered = _device.CreateStorageTexture(
                    wantWidth, wantHeight, VolumeFormat.Rgba16F, "cards.screen.filtered");

                if (state.ScreenGi.Raw.IsValid)
                {
                    _device.DestroyTexture(state.ScreenGi.Raw);
                }

                state.ScreenGi.Raw = _device.CreateStorageTexture(
                    wantWidth, wantHeight, VolumeFormat.Rgba16F, "cards.screen.raw");

                state.ScreenGi.IndirectWidth = wantWidth;
                state.ScreenGi.IndirectHeight = wantHeight;
                state.ScreenGi.HasHistory = false;
            }

            // Both histories describe the scene as the last frames saw it. A camera cut (a cutscene, a
            // respawn, the death camera, the map taking the eye), a scope snapping its zoom in, or a
            // surface cache rebuilt from dark all make that description wrong, and the plane tests only
            // catch part of it: a cut still read one frame of the old view's light.
            //
            // A projection that merely MOVES is not a cut. Both passes reproject through the projection
            // their history was drawn with, so a cinematic zoom is just more camera motion. Any change at
            // all used to count: the intro's slow zoom inside the truck moved the scale a ten-thousandth
            // a tick, and the GI threw its whole history away thirty times a second -- the interior went
            // dark, noisy and flickering for the length of the shot.
            DepthUnprojection giUnprojection = DepthUnprojection.From(unjitteredProjection, ViewPlanes.NearMetres, ViewPlanes.FarMetres);
            bool giContinuous = state.ScreenGi.ContinuityValid
                && ReferenceEquals(state.ScreenGi.LastCards, liveGiCards)
                && MathF.Abs(giUnprojection.XScale - state.ScreenGi.LastUnprojection.XScale) < GiCutZoom * state.ScreenGi.LastUnprojection.XScale
                && MathF.Abs(giUnprojection.YScale - state.ScreenGi.LastUnprojection.YScale) < GiCutZoom * state.ScreenGi.LastUnprojection.YScale
                && (eye.Position - state.ScreenGi.LastEye).LengthSquared < GiCutDistance * GiCutDistance;
            if (!giContinuous)
            {
                state.ScreenGi.HasHistory = false;
                state.ScreenGi.ResolveHasHistory = false;
            }

            state.ScreenGi.ContinuityValid = true;
            state.ScreenGi.LastEye = eye.Position;
            state.ScreenGi.LastUnprojection = giUnprojection;
            state.ScreenGi.LastCards = liveGiCards;

            Vector3f giCameraToCache = new(
                (float)(eye.Position.X - liveGiCards.Origin.X),
                (float)(eye.Position.Y - liveGiCards.Origin.Y),
                (float)(eye.Position.Z - liveGiCards.Origin.Z));

            // The gather's cadence: it re-traces every Nth frame and the atlas stands in between.
            // See r_screen_gi_cadence for why that is sound and why it beats cutting the ray count.
            int giFilterRadius = RenderCVars.ScreenGatherFilter.Value;
            int giCadence = Math.Max(RenderCVars.ScreenGatherCadence.Value, 1);
            bool giTraceThisFrame = !state.ScreenGi.HasHistory || (state.ScreenGi.CadenceCursor % giCadence) == 0;
            state.ScreenGi.CadenceCursor++;

            int write = state.ScreenGi.Frame & 1;
            int read = 1 - write;

            if (giTraceThisFrame)
            {
                liveGiCards.GatherScreenTemporal(
                    liveGiField,
                    _device.SceneDepth,
                    _device.SceneNormal,
                    DepthUnprojection.From(unjitteredProjection, ViewPlanes.NearMetres, ViewPlanes.FarMetres),
                    eye.Orientation.Transposed(),
                    giCameraToCache,
                    giSourceWidth,
                    giSourceHeight,
                    giStep,
                    state.ScreenGi.Accum[write],
                    state.ScreenGi.Position[write],
                    state.ScreenGi.Accum[read],
                    state.ScreenGi.Position[read],
                    state.ScreenGi.PrevWorldToView,
                    state.ScreenGi.PrevCameraToCache,
                    state.ScreenGi.Frame,
                    giMaxAccum,
                    state.ScreenGi.HasHistory,

                    // The screen trace tier (L5b): the previous frame's lit scene, read at on-screen hits
                    // before the ray falls through to the surface cache. device.SceneColour holds last
                    // frame's linear scene here -- this runs before the current frame is lit.
                    _device.SceneColour,
                    RenderCVars.ScreenGatherTrace.Value ? giTraceSteps : 0,
                    giTraceDistance,
                    giTraceThickness,

                    // The sky-light fill (L9): a ray that escapes returns the mission's sky, the same sky
                    // the surface cache lights its cards with, so the indirect that replaces the ambient
                    // carries sky light -- with its occlusion, since a ray toward geometry hits it instead.
                    _host.SkyFillColour(),

                    // The camera the lit target was rendered through: the LAST frame's, which the
                    // resolve carries forward every frame -- unlike the gather's own previous camera,
                    // which only moves on a frame that traced.
                    state.ScreenGi.ResolvePrevWorldToView,
                    state.ScreenGi.ResolvePrevCameraToCache,
                    state.ScreenGi.Raw,

                    // Where a moving car's surface really came from, not where the camera alone
                    // would put it. Asking derives it: every G-buffer draw is in by now.
                    _device.SceneVelocity,
                    state.ScreenGi.PrevUnprojection);

                // The spatial denoise (L6c), if on: smooths the accumulation into a separate atlas that
                // the deferred fill reads, leaving the history the next frame reprojects onto unfiltered.
                if (giFilterRadius > 0)
                {
                    liveGiCards.FilterScreen(
                        state.ScreenGi.Accum[write],
                        state.ScreenGi.Position[write],
                        _device.SceneNormal,
                        state.ScreenGi.Filtered,
                        state.ScreenGi.IndirectWidth,
                        state.ScreenGi.IndirectHeight,
                        giFilterRadius);
                }

                // What this frame becomes to the NEXT GATHER: only a frame that actually traced moves
                // the gather's history and its camera on.
                state.ScreenGi.LastWrite = write;
                state.ScreenGi.PrevWorldToView = eye.Orientation;
                state.ScreenGi.PrevCameraToCache = giCameraToCache;
                state.ScreenGi.PrevUnprojection = giUnprojection;
                state.ScreenGi.HasHistory = true;
                state.ScreenGi.Frame++;
            }

            TextureHandle giRead = giFilterRadius > 0 ? state.ScreenGi.Filtered : state.ScreenGi.Accum[state.ScreenGi.LastWrite];

            // The full-resolution temporal resolve: upsample the sparse atlas once and accumulate
            // it per pixel, reprojecting against each surface's own world position, so the
            // indirect is pinned to the world instead of sliding with the coarse sparse-probe
            // reprojection. The deferred fill reads the full-res result.
            int resolveWrite = state.ScreenGi.ResolveFrame & 1;
            int resolveRead = 1 - resolveWrite;

            int resolveW = giSourceWidth;
            int resolveH = giSourceHeight;
            if (state.ScreenGi.ResolveWidth != resolveW || state.ScreenGi.ResolveHeight != resolveH)
            {
                for (int i = 0; i < 2; i++)
                {
                    if (state.ScreenGi.Resolve[i].IsValid)
                    {
                        _device.DestroyTexture(state.ScreenGi.Resolve[i]);
                    }

                    if (state.ScreenGi.ResolvePosition[i].IsValid)
                    {
                        _device.DestroyTexture(state.ScreenGi.ResolvePosition[i]);
                    }

                    state.ScreenGi.Resolve[i] = _device.CreateStorageTexture(
                        resolveW, resolveH, VolumeFormat.Rgba16F, "gi.resolve");
                    state.ScreenGi.ResolvePosition[i] = _device.CreateStorageTexture(
                        resolveW, resolveH, VolumeFormat.Rgba16F, "gi.resolve.pos");
                }

                state.ScreenGi.ResolveWidth = resolveW;
                state.ScreenGi.ResolveHeight = resolveH;
                state.ScreenGi.ResolveHasHistory = false;
            }

            liveGiCards.ResolveScreenTemporal(
                giRead,
                _device.SceneDepth,
                _device.SceneNormal,
                DepthUnprojection.From(unjitteredProjection, ViewPlanes.NearMetres, ViewPlanes.FarMetres),
                eye.Orientation.Transposed(),
                giCameraToCache,
                state.ScreenGi.ResolvePrevWorldToView,
                state.ScreenGi.ResolvePrevCameraToCache,
                state.ScreenGi.Resolve[resolveWrite],
                state.ScreenGi.ResolvePosition[resolveWrite],
                state.ScreenGi.Resolve[resolveRead],
                state.ScreenGi.ResolvePosition[resolveRead],
                resolveW,
                resolveH,
                state.ScreenGi.ResolveHasHistory,
                16,

                // The probes' own traced positions and the frame they were traced in -- the last
                // frame that traced, which is not this one whenever the gather's cadence skipped.
                state.ScreenGi.Position[state.ScreenGi.LastWrite],
                state.ScreenGi.PrevWorldToView,
                state.ScreenGi.PrevCameraToCache,
                giStep,
                _device.SceneVelocity,
                state.ScreenGi.ResolvePrevUnprojection,

                // The frame the atlas being read was traced in -- the counter has moved past it --
                // which places its probes in their tiles.
                state.ScreenGi.Frame - 1);
            state.ScreenGi.ResolveHasHistory = true;

            // Recorded for the deferred lighting to fold in when the pass closes. Full-resolution, so
            // it is read straight rather than upsampled from the sparse atlas a second time.
            _device.SetScreenIndirect(state.ScreenGi.Resolve[resolveWrite], giStrength, fullResolution: true);

            // The resolve's own carry-forward, every frame: this camera is what the next frame's
            // per-pixel reprojection measures its motion against. The gather's equivalent is set
            // above, inside the branch, because only a frame that traced may move it.
            state.ScreenGi.ResolvePrevWorldToView = eye.Orientation;
            state.ScreenGi.ResolvePrevCameraToCache = giCameraToCache;
            state.ScreenGi.ResolvePrevUnprojection = giUnprojection;
            state.ScreenGi.ResolveFrame++;

            _device.EndStageTiming();
        }
        else
        {
            // The gather did not run this frame, so next frame has nothing coherent to reproject onto.
            state.ScreenGi.HasHistory = false;
            state.ScreenGi.ResolveHasHistory = false;
        }
    }

    /// <summary>Hands the device this frame's indirect light: the probe field, the sky's fill, the surface cache and the distance field.</summary>
    public void PrepareSceneLighting(FrameLoopState state, Camera3D eye, DirectionalLightSettings light)
    {
        // The bake stored how much sky reaches each probe, never what colour it was, so the
        // mission's own sky is applied here -- and a level whose sun sits below the horizon leans
        // on its ambient exactly as the fog does.
        // The clipmaps follow the camera. Each level is rebuilt only when the camera has left it,
        // which for the outer levels is almost never -- I.G.I.'s geometry is static, so a clipmap
        // only becomes wrong by being walked out of.
        if (DistanceField is { } liveField)
        {
            // The real-time GI's compute runs here, before the G-buffer, so this is where it is
            // timed. Without this bracket the bounce below -- the frame's single most expensive pass
            // when real-time GI is on -- was measured by nothing, and the overlay's total was a frame
            // that did not exist.
            _device.BeginStageTiming(RenderStage.GlobalIllumination);

            // Scrolls the clipmaps to follow the camera, rebuilding whichever level it walked out
            // of. How many rebuilt is the retrace's "did the cache's coverage actually change"
            // signal: nought means the camera only turned or drifted within a cell, and a
            // view-independent cache needs no retrace for that -- which is what lets it idle at rest.
            int giRebuilt = liveField.Update(eye.Position);

            // Once, after the first build, and only when asked. A wrong distance field produces a
            // picture that is merely subtly wrong several stages later, so the only way to know it
            // is right is to compare it against the exact answer at the same points.
            if (!FieldValidated && RenderCVars.DistanceFieldValidate.Value && DistanceFieldScene is { } exact)
            {
                FieldValidated = true;
                // Every level, not just the innermost. A trace falls through to the outer
                // clipmaps as soon as it leaves the fine one, so an unbuilt level 2 is invisible to
                // a check that only ever looked at level 0 -- and it shadows everything.
                for (int level = 0; level < liveField.Settings.Levels; level++)
                {
                    DistanceFieldValidation.Compare(_device, liveField, exact, level, _logger);
                }
            }

            // Re-lit whenever a clipmap level moved, and only then. The clipmaps follow the camera
            // and reach a few hundred metres, so a page on the far side of the level has no field
            // to trace against -- re-lighting as they move is what brings each page's shadow in as
            // the player approaches it. Doing it every frame regardless would be tens of thousands
            // of work groups spent re-deriving an answer that had not changed.
            // The second clause is what makes a dump deterministic. Re-lighting is otherwise driven
            // by a clipmap having moved, which is a race against the field bake landing on its
            // worker: the same command produced two dumps on one run and one on the next, and when
            // it produced one the file held the UNSHADOWED load-time pass while looking exactly
            // like the shadowed one. That is how a probe came back reporting nothing at all.
            bool wantDump = !state.CardLightingDumped
                && RenderCVars.SurfaceCacheDump.Value is { Length: > 0 }

                // Not the first qualifying frame. On a backend that records a frame's compute and
                // submits it at the end, a readback taken on the same frame as the dispatches sees
                // the atlas as it was before them -- which reads exactly like a bounce that
                // produced nothing, and cost a long detour to tell apart from one.
                && CardBounceFrame >= 8;

            // Whether the surface cache's inputs actually changed this frame: a level scrolled new
            // coverage in, the sun was scrubbed, or a dump was asked for. Turning or nudging the
            // camera changes nothing a view-independent cache holds, so it does not count -- which is
            // what lets the retrace idle while the player stands roughly still. The sun is fixed
            // unless [ or ] is held, so a plain per-frame compare catches a scrub and nothing else.
            bool giSunMoved = !_giSunValid
                || MathF.Abs(light.Direction.X - _lastGiSunDirection.X) > 1e-4f
                || MathF.Abs(light.Direction.Y - _lastGiSunDirection.Y) > 1e-4f
                || MathF.Abs(light.Direction.Z - _lastGiSunDirection.Z) > 1e-4f
                || MathF.Abs(light.Colour.X - _lastGiSunColour.X) > 1e-4f
                || MathF.Abs(light.Colour.Y - _lastGiSunColour.Y) > 1e-4f
                || MathF.Abs(light.Colour.Z - _lastGiSunColour.Z) > 1e-4f;
            _lastGiSunDirection = light.Direction;
            _lastGiSunColour = light.Colour;
            _giSunValid = true;

            // Reset the settle counter on any real change; otherwise let it climb. The retrace runs
            // while something is moving and for a settling window after, then idles -- the cache
            // keeps its converged atlas with none of the ray tracing, so a still scene stops paying
            // the periodic covering-slice spike that halved its frame rate.
            bool giChanged = wantDump || giRebuilt > 0 || giSunMoved;
            // Long enough for the cache to actually converge before it stops: a tile is traced once
            // per amortisation spread and moves 1/(1+temporal frames) of the way each time, so its
            // time constant is spread x (1 + frames) -- some eight hundred frames at every modern
            // preset. A fixed 64 froze it a few traces in, with most of the error still there.
            int giSettle = Math.Max(
                GiSettleFrames,
                2 * Math.Max(RenderCVars.GiAmortize.Value, 1) * (1 + Math.Max(RenderCVars.GiTemporalFrames.Value, 1)));
            _giStaticFrames = giChanged ? 0 : Math.Min(_giStaticFrames + 1, giSettle + 1);

            // A scrub of the clock, and the moments after it: the cache follows the sun at once instead
            // of over the long blend tuned for a sun that does not move. Held to that long blend, a scrub
            // into the night left every surface lit by the day's bounce for twenty seconds -- the world
            // stayed bright under a black sky. The screen gather's own history is left alone: it is ten
            // frames already, and throwing it away every frame of a scrub showed its raw single-frame
            // noise as clouds on every wall.
            SunMovedFrames = giSunMoved ? 0 : Math.Min(SunMovedFrames + 1, GiSunFollowFrames + 1);
            bool followingSun = SunMovedFrames <= GiSunFollowFrames;
            bool retraceGi = _giStaticFrames <= giSettle;

            if (CardLighting is { } litCards)
            {
                // How many frames the relight and the bounce spread their tiles across, from the cvar
                // so the per-frame GPU cost can be traded against GI fill-in latency without a rebuild.
                litCards.BounceFrameSpread = followingSun
                    ? Math.Min(RenderCVars.GiAmortize.Value, GiSunFollowSpread)
                    : RenderCVars.GiAmortize.Value;
                litCards.MinSliceTiles = RenderCVars.GiMinSlice.Value;

                // How many frames a tile's estimate accumulates over, the pair to the spread above:
                // the two together set how much of one retrace is admitted and how often, which is
                // what governs the slow ripple across interior surfaces. See r_gi_temporal_frames.
                litCards.TemporalFrames = followingSun
                    ? Math.Min(RenderCVars.GiTemporalFrames.Value, GiSunFollowTemporalFrames)
                    : RenderCVars.GiTemporalFrames.Value;

                // Where the steady-state refresh is centred and how far it reaches. The cache spans
                // the whole level and the cursor used to walk all of it every round, so a fixed
                // share of every frame went on cards in parts of a map the player is nowhere near --
                // and past the distance field's own reach, where the relight cannot even arrive at a
                // different answer. Relative to the cache's origin, as everything in its space is,
                // and differenced in double precision before the cast: the level sits kilometres out.
                litCards.ActiveCentre = new Vector3f(
                    (float)(eye.Position.X - litCards.Origin.X),
                    (float)(eye.Position.Y - litCards.Origin.Y),
                    (float)(eye.Position.Z - litCards.Origin.Z));
                litCards.ActiveRadius = RenderCVars.GiCacheRadius.Value;

                // The direct relight: the whole cache at once for a deterministic dump, otherwise a
                // slice a frame. The sun is fixed, so the only thing the direct term has to track is
                // the clipmap scrolling new coverage under a card as the camera moves -- and doing
                // that over the whole cache on the scroll frame was a seventy-millisecond stall that
                // read as the game lurching every few steps. Sliced, the new shadows arrive over a
                // handful of frames with no spike, and the bounce keeps its long-running history
                // instead of being re-seeded from scratch on every scroll.
                // Skipped once the cache has settled and nothing is scrolling or scrubbing: the sun
                // is fixed and no new coverage is arriving, so the slice would re-trace the direct
                // term it already holds. That idle is the whole fix for the at-rest spike.
                if (retraceGi)
                {
                    litCards.Light(
                        light is { IsBelowHorizon: false } shadowSun ? shadowSun.Direction : default,

                        // The SAME effective sun the deferred lighting shades with -- colour times
                        // both scales -- or the bounce is lit by a different key than the screen and
                        // the two disagree wherever indirect light dominates.
                        light.Colour * (RenderCVars.SunScale.Value * RenderCVars.LightScale.Value),
                        _host.SkyFillColour(),
                        liveField,
                        RenderCVars.SurfaceCacheProbe.Value,
                        amortize: !wantDump);
                }
            }

            // The bounce accumulates EVERY frame -- an amortised radiosity. Each frame
            // reads last frame's final and folds ONE more bounce in, so a still scene keeps filling
            // with light toward its multi-bounce equilibrium (the "lighting up" over the first
            // second) instead of holding at the two bounces the load pass seeded. This was gated on
            // a clipmap rebuild, so a stationary interior never got past its first couple of bounces
            // -- which is most of why a sunless room read dark: bounces three, four and five are
            // what fill a room the sun never reaches. The feedback is clamped below one in the
            // shader, so it converges rather than running away, and the 1/(1+frames) blend gives the
            // ramp for free. Only when no diagnostic probe owns the direct atlas.
            // Same stand-down as the relight: once settled with nothing scrolling or scrubbing, the
            // scene has reached its multi-bounce equilibrium and folding another bounce in re-derives
            // the same final -- so the bounce, the frame's single most expensive pass, idles too. The
            // frame counter holds where it was, so the accumulation resumes rather than restarts when
            // motion or a scrub brings the retrace back.
            if (CardLighting is { } bounceCards && RenderCVars.SurfaceCacheProbe.Value == 0 && retraceGi)
            {
                bounceCards.Bounce(
                    liveField,
                    boost: RenderCVars.GlobalIlluminationBoost.Value,
                    temporalIndex: wantDump ? 0 : CardBounceFrame);
                CardBounceFrame++;

                // Dumped after the SHADOWED bounce rather than the load-time one, which runs before
                // there is any field to trace against and would show the unshadowed answer.
                if (wantDump && RenderCVars.SurfaceCacheDump.Value is { Length: > 0 } shadowInto)
                {
                    state.CardLightingDumped = true;
                    SurfaceCacheDebugDumps.CardLighting(
                        _device, bounceCards, shadowInto, _logger, RenderCVars.SurfaceCacheProbe.Value);
                }
            }

            // The world-space radiance cache: a grid of probes tracing the just-lit surface cache
            // into the same L1-SH field the deferred pass samples, but held in world cells rather
            // than on the moving screen -- so it stays stable under motion where the screen gather
            // shimmers. Configured to the baked field's own layout so the two are interchangeable at
            // the sampler, updated a slab a frame off the freshly bounced cache, and bound in place
            // of the baked field for the deferred fill. Needs the baked field only for its grid.
            if (RenderCVars.RadianceCache.Value != 0
                && CardLighting is { } cacheCards
                && _host.ProbeField is { } cacheGrid)
            {
                cacheCards.ConfigureRadianceCache(
                    new Vector3d(cacheGrid.Origin.X, cacheGrid.Origin.Y, cacheGrid.Origin.Z),
                    cacheGrid.Spacing,
                    cacheGrid.CountX,
                    cacheGrid.CountY,
                    cacheGrid.CountZ);

                cacheCards.UpdateRadianceCache(
                    liveField,
                    _host.SkyFillColour(),
                    // A short running average while the level is warming up, so the cache climbs to
                    // its converged value in a handful of frames rather than easing there over a
                    // couple of seconds -- which is what read as the scene loading bright and dimming.
                    // A long one after, where a slow average is what keeps the steady field from
                    // shimmering under the per-sweep ray jitter.
                    maxFrames: ExposureWarmup > 0 ? 6 : 32,
                    scale: RenderCVars.RadianceCacheScale.Value,
                    skyReject: RenderCVars.GlobalIlluminationLeakReject.Value);

                _device.SetRealtimeProbeVolume(
                    cacheCards.RadianceCacheRed,
                    cacheCards.RadianceCacheGreen,
                    cacheCards.RadianceCacheBlue,
                    cacheCards.RadianceCacheDistance,
                    cacheGrid.Origin,
                    cacheGrid.Spacing,
                    cacheGrid.CountX,
                    cacheGrid.CountY,
                    cacheGrid.CountZ);
            }
            else
            {
                _device.ClearRealtimeProbeVolume();
            }

            _device.EndStageTiming();
        }

        _device.SetDebugView(RenderCVars.Debug.Value);
    }
}
