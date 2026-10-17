using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Threading;
using OpenIGI.Engine.Resources;
using OpenIGI.Game.World;
using OpenIGI.Host.Lighting;
using OpenIGI.Presentation;
using OpenIGI.Rendering;

namespace OpenIGI.Host.Services;

/// <summary>
/// The baked lighting: a level's irradiance probe field and its path-traced object lightmaps, read from their caches or
/// traced on a worker, collected on the render thread, and eased onto the screen rather than snapped.
/// </summary>
/// <param name="device">The device the field is shown on.</param>
/// <param name="logger">Where bakes are reported.</param>
/// <param name="work">The workers the bakes trace on.</param>
/// <param name="lockstep">Whether frames are captured in lockstep, which waits for every bake at load.</param>
/// <param name="probeCache">Where probe fields are read from and written to.</param>
/// <param name="lightmapCache">Where lightmaps are read from and written to.</param>
internal sealed class BakedLighting(
    IGraphicsDevice device,
    IOpenIgiLogger logger,
    IWorkScheduler work,
    bool lockstep,
    GiBakeCache probeCache,
    LightmapBakeCache lightmapCache) : IHostService
{
    // One albedo for the whole level, shared by the load-time trace and the live re-bake. IGI's palette is
    // concrete, snow and dirt, so a warm grey stands in for reading each hit's own texture.
    private static readonly Vector3f ProbeBakeAlbedo = new(0.42f, 0.40f, 0.37f);

    // How the displayed field eases towards the newest bounce pass. The rate is an exponential approach in
    // reciprocal seconds -- at 2.0 the field covers ~86% of the gap in one second -- and the settle is how
    // close counts as there, so the ease stops re-uploading the volume rather than converging for ever.
    private const float ProbeChaseRate = 2.0f;
    private const float ProbeChaseSettle = 0.001f;

    private IrradianceProbeBake? _probeBake;

    // The newest bounce pass the display is easing towards. Held apart from ProbeField (what is on the
    // GPU right now) so a pass does not snap in: a bounce adds a chunk of indirect light, and eight snaps
    // read as the room stepping brighter eight times. ProbeField chases this instead.
    private IrradianceVolume? _probeTarget;

    // The cache key an in-flight interactive bake is written under once it finishes -- remembered here
    // because the load does not block for the field, so the save moves to the frame the bake completes.
    // The cast is kept too, so the live re-bake (F11) can trace the field again from the same inputs.
    private int _probeBakeMission;
    private float _probeBakeSpacing;
    private Vector3f _probeBakeSun;
    private WorldRayCast? _bakeCast;

    // The path-traced object-lightmap bake in flight, and the inputs its result is cached under once it
    // lands, exactly as the probe field's is.
    private LightmapBake? _lightmapBake;
    private int _lightmapBakeMission;
    private Vector3f _lightmapBakeSun;
    private Vector3f _lightmapBakeSunColour;
    private Vector3f _lightmapBakeSky;

    // How the ease is paced: every fourth frame, by elapsed time.
    private int _lastChaseMilliseconds;
    private int _chaseFrame;

    /// <summary>Gets the probe field on the screen, once there is one.</summary>
    public IrradianceVolume? ProbeField { get; private set; }

    /// <summary>Gets the cache probe fields are read from, which the menu's previews read too.</summary>
    public GiBakeCache ProbeCache { get; } = probeCache;

    /// <summary>Gets when the probe field landed, in the loop's own milliseconds, or -1 for no fade owed.</summary>
    /// <remarks>
    /// The field is traced on a worker and arrives several seconds into a level; until it does every surface is lit by one
    /// flat ambient colour, and swapping the two in a single frame relights the whole world at once, which reads as a fault
    /// rather than as a load finishing. A lockstep capture never fades: it waits for the field at load.
    /// </remarks>
    public long ProbeArrivedAt { get; private set; } = -1;

    /// <summary>Gets whether the field can be traced again from scratch, as F11 does.</summary>
    public bool CanRebake => _bakeCast is not null;

    /// <inheritdoc/>
    public string Name => "Baked lighting";

    /// <summary>Forgets the last level's field and cancels its bakes, at a session's start.</summary>
    public void Forget()
    {
        ProbeField = null;

        // The bake in flight, while it is in flight. Non-null only between the level load starting one and
        // the render thread collecting it; a lockstep capture never sees it, because that path waits.
        _probeBake = null;
        _lightmapBake?.Cancel();
        _lightmapBake = null;
        ProbeArrivedAt = -1;
    }

    /// <summary>Starts the clock the field's ease is paced by, once the loop has one.</summary>
    /// <param name="now">The loop's milliseconds.</param>
    public void StartChaseClock(int now)
    {
        _lastChaseMilliseconds = now;
        _chaseFrame = 0;
    }

    /// <summary>Shows a field a menu backdrop read, as its level's own.</summary>
    /// <param name="field">The field, or <see langword="null"/> for a mission with no shipped bake.</param>
    public void AdoptPreviewField(IrradianceVolume? field)
    {
        ProbeField = field;
        device.SetIrradianceVolume(ProbeField);
    }

    /// <summary>Reads or starts a level's lightmaps and probe field, as its mission is brought up.</summary>
    /// <param name="missionId">The mission, which keys both caches.</param>
    /// <param name="scene">The level's placed objects.</param>
    /// <param name="world">The world's renderer, which takes the lightmaps.</param>
    /// <param name="resources">The level's resources, which the lightmap bake reads each surface's albedo from.</param>
    /// <param name="sun">The level's sun, if it authors one.</param>
    /// <param name="bakeCast">What the bakes trace against: the level with every door shut, glass passed through.</param>
    public void Start(
        int missionId,
        WorldScene scene,
        IWorldPresenter? world,
        ResourceRegistry resources,
        DirectionalLightSettings? sun,
        WorldRayCast bakeCast)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(bakeCast);
        Vector3f bakeSunDirection = sun is { } declared && !declared.IsBelowHorizon
            ? declared.Direction
            : default;

        // Path-traced object lightmaps: the same cast and sun, the sky's own colour, and the result swapped in
        // for the shipped pages (r_lightmap 2) or used where the level ships none (1).
        bool discardRetailLightmaps = RenderCVars.BakedLightmaps.Value >= 2;
        if (RenderCVars.BakedLightmaps.Value > 0 && world is not null)
        {
            Vector3f lmSun = sun?.Colour ?? new Vector3f(1f, 1f, 1f);

            // The sky's own colour, from the Dirlight's ambient term -- a level with no Dirlight has a neutral
            // sky -- at the player's sky-light setting.
            Vector3f lmSky = (sun?.Ambient ?? new Vector3f(0.27f, 0.29f, 0.33f)) * RenderCVars.SkyLight.Value;
            _lightmapBakeMission = missionId;
            _lightmapBakeSun = bakeSunDirection;
            _lightmapBakeSunColour = lmSun;
            _lightmapBakeSky = lmSky;

            if (lightmapCache.TryLoad(missionId, bakeSunDirection, lmSun, lmSky) is { } cachedLightmaps)
            {
                world.SetBakedLightmaps(cachedLightmaps, discardRetailLightmaps);
            }
            else
            {
                // Set the mode straight away so the worker's seconds are not spent showing the shipped
                // lightmaps under an exclusive (r_lightmap 2) request: with no bake yet, lightmapped surfaces
                // read black until the bake lands. Mode 1 keeps the shipped pages meanwhile.
                world.SetBakedLightmaps(null, discardRetailLightmaps);

                // Resolving each surface's albedo reads the resource registry, so the texel maps, textured
                // triangles and their BVH are built here on the main thread; the worker then does only the
                // tracing, which touches immutable data and the cast.
                IReadOnlyList<LightmapBaker.LightmapTarget> lmTargets = LightmapBaker.EnumerateTargets(scene);
                LightmapBaker.LightmapTriangle[] lmTris = LightmapBaker.BuildTriangles(scene, lmTargets, resources);
                LightmapBaker.LightmapBvh lmBvh = LightmapBaker.LightmapBvh.Build(lmTris);

                _lightmapBake?.Cancel();
                _lightmapBake = LightmapBake.Start(
                    scene, lmTargets, lmTris, lmBvh, bakeCast, bakeSunDirection, lmSun, lmSky,
                    work, bounces: 4, hemisphereRays: 128, logger: logger);

                if (lockstep && _lightmapBake.WaitForResult() is { } readyLightmaps)
                {
                    world.SetBakedLightmaps(readyLightmaps, discardRetailLightmaps);
                    lightmapCache.Save(missionId, bakeSunDirection, lmSun, lmSky, readyLightmaps);
                    _lightmapBake = null;
                }
            }
        }
        else
        {
            // The setting is off, or off since the last load: make sure no stale bake lingers.
            world?.SetBakedLightmaps(null, discardRetail: false);
        }

        // The probe field, guarded on its own cvar so it does not trace under a lightmap-only request (r_gi off,
        // r_lightmap on -- the ultra preset is exactly that).
        if (!RenderCVars.GlobalIllumination.Value)
        {
            return;
        }

        // Whatever the console says, but never so fine that a big level spends a minute of load tracing rays
        // nobody asked for.
        float giSpacing = RenderCVars.GlobalIlluminationSpacing.Value;

        // Remembered so the live re-bake (F11) can trace the field again from exactly the inputs this load
        // used -- the same cast, sun and spacing -- whether the field arrived from the cache or the tracer.
        _bakeCast = bakeCast;
        _probeBakeMission = missionId;
        _probeBakeSpacing = giSpacing;
        _probeBakeSun = bakeSunDirection;

        // A shipped (or previously-written) bake loads instantly; only a mission without one pays to trace it
        // here, and then writes the result so no load pays for it again.
        if (ProbeCache.TryLoad(missionId, giSpacing, bakeSunDirection) is { } cachedField)
        {
            ProbeField = cachedField;
            return;
        }

        // One albedo for the whole level. The alternative is reading each hit's own texture, which needs the
        // ray to report a material rather than a triangle -- and IGI's palette is concrete, snow and dirt, so
        // a warm grey is not far off any of them.
        _probeBake = IrradianceProbeBake.Start(scene, bakeCast, bakeSunDirection, giSpacing, ProbeBakeAlbedo, work, logger);

        if (lockstep)
        {
            // A capture is not allowed to race the worker: a reference image has to either contain the field
            // or not, never "whatever frame 30 caught". So a lockstep run blocks for the finished field and
            // writes the cache inline.
            ProbeField = _probeBake.WaitForResult();
            _probeBake = null;

            if (ProbeField is not null)
            {
                ProbeCache.Save(missionId, giSpacing, bakeSunDirection, ProbeField);
            }
        }
        else
        {
            // Until the worker lands, the device keeps whatever field it already has -- the previous level's,
            // or none -- which is the honest state of things for the few seconds the trace takes.
            ProbeField = null;
        }
    }

    /// <summary>Puts the field this level has on the device.</summary>
    public void Show() => device.SetIrradianceVolume(ProbeField);

    /// <summary>The frame's share of the bakes: collect what landed, and ease the field towards the newest pass.</summary>
    /// <param name="world">The world's renderer, which takes lightmaps that land.</param>
    /// <param name="now">The loop's milliseconds.</param>
    public void Step(IWorldPresenter? world, int now)
    {
        // The probe bake, if one is still tracing. Collected HERE rather than on the worker because what comes
        // back becomes a GL texture, and the context belongs to this thread. TryCollect answers true once, so
        // this costs a completed-flag read per frame until then and nothing after.
        if (_probeBake is { } pendingBake)
        {
            // Each bounce pass becomes the field the display eases towards, not one shown outright: the smooth
            // chase below fills the light in over about a second rather than stepping it.
            if (pendingBake.TryCollectLatest(out IrradianceVolume? streamedField) && streamedField is not null)
            {
                if (ProbeField is null)
                {
                    // First light: shown at once and faded up from flat ambient by the device's own blend, so
                    // there is a starting field for later passes to ease away from.
                    ProbeField = streamedField;
                    _probeTarget = streamedField;
                    device.SetIrradianceVolume(ProbeField);
                    ProbeArrivedAt = now;
                }
                else
                {
                    _probeTarget = streamedField;
                }
            }

            // Once the bake is done, the authoritative final field is both what the display settles on and
            // what the cache keeps, so the next load reads it instead of tracing again.
            if (pendingBake.Poll())
            {
                if (pendingBake.TryCollect(out IrradianceVolume? finalField) && finalField is not null)
                {
                    _probeTarget = finalField;
                    ProbeCache.Save(_probeBakeMission, _probeBakeSpacing, _probeBakeSun, finalField);
                }

                _probeBake = null;
            }
        }

        // The path-traced lightmap bake, once its worker finishes: swap the pages in and write them to the cache
        // so the next load reads instead of tracing. Collected here for the same reason the probe field is.
        if (_lightmapBake is { } pendingLightmaps && pendingLightmaps.TryCollect(out BakedLightmaps? bakedLightmaps))
        {
            _lightmapBake = null;
            if (bakedLightmaps is not null)
            {
                world?.SetBakedLightmaps(bakedLightmaps, RenderCVars.BakedLightmaps.Value >= 2);
                lightmapCache.Save(
                    _lightmapBakeMission, _lightmapBakeSun, _lightmapBakeSunColour, _lightmapBakeSky, bakedLightmaps);
            }
        }

        // Ease the displayed field towards the newest baked pass. Framerate-independent, so the fill looks the
        // same however fast frames go out, and it keeps running after the bake handle is gone until the last
        // pass has settled -- at which point the field points straight at the target and the re-upload stops.
        if (_probeTarget is not null && ProbeField is not null && !ReferenceEquals(ProbeField, _probeTarget)

            // Every fourth frame, not every frame. The blend walks the whole field on the CPU and the re-upload
            // sends the four probe volumes over again -- together roughly ten milliseconds and half a gigabyte a
            // second while a bake streams. The ease runs over about a second, so a quarter-rate step is
            // invisible; the elapsed-time step below keeps the pace identical.
            && (++_chaseFrame & 3) == 0)
        {
            float chaseSeconds = MathF.Max(0f, (now - _lastChaseMilliseconds) / 1000f);
            _lastChaseMilliseconds = now;

            float step = 1f - MathF.Exp(-ProbeChaseRate * chaseSeconds);
            float moved = ProbeField.BlendToward(_probeTarget, step);
            device.SetIrradianceVolume(ProbeField);

            if (moved < ProbeChaseSettle)
            {
                ProbeField = _probeTarget;
            }
        }
        else
        {
            // Keep the clock current while idle, so the first frame of the next ease steps by one frame's worth
            // of time rather than the whole idle gap.
            _lastChaseMilliseconds = now;
        }
    }

    /// <summary>Traces the level's field again from scratch, clearing every cached one, as F11 asks.</summary>
    /// <param name="scene">The level's placed objects.</param>
    /// <param name="now">The loop's milliseconds.</param>
    public void Rebake(WorldScene scene, int now)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (_bakeCast is null)
        {
            return;
        }

        _probeBake?.Cancel();
        int cleared = ProbeCache.DeleteAll();

        ProbeField = null;
        _probeTarget = null;
        device.SetIrradianceVolume(null);
        ProbeArrivedAt = -1;
        _lastChaseMilliseconds = now;

        _probeBake = IrradianceProbeBake.Start(
            scene, _bakeCast, _probeBakeSun, _probeBakeSpacing, ProbeBakeAlbedo, work, logger);

        logger.Information("GI", $"Re-baking the lighting from scratch (F11); cleared {cleared} cached field(s).");
    }

    /// <summary>Cancels the bakes still tracing and lets go of them, as their level is left.</summary>
    /// <remarks>
    /// Both are worker threads holding the whole of what they trace -- the lightmap bake's texel maps, triangles and BVH
    /// run to a few hundred megabytes -- so leaving a level without this left every core tracing a level nobody was in.
    /// </remarks>
    public void Abandon()
    {
        Cancel();
        _probeBake = null;
        _lightmapBake = null;
    }

    /// <summary>Cancels the bakes still tracing, for a level nobody will see again.</summary>
    public void Cancel()
    {
        _probeBake?.Cancel();
        _lightmapBake?.Cancel();
    }

    /// <inheritdoc/>
    public void Dispose() => Cancel();
}
