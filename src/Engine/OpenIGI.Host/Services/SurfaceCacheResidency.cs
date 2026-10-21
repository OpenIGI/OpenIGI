using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Host.Lighting;
using OpenIGI.Presentation.Lighting;
using OpenIGI.Rendering;

namespace OpenIGI.Host.Services;

/// <summary>
/// The lit surface cache as the real-time lighting model holds it: captured from the level, lit, solved
/// forward to its multi-bounce equilibrium once the distance field is there to trace against, and then
/// kept current as the sun and the lamps change by a slice-a-frame sweep of the whole cache.
/// </summary>
/// <param name="device">The device the cache lives on.</param>
/// <param name="logger">Where captures and convergence are reported.</param>
/// <param name="lockstep">Whether frames are captured in lockstep, which runs the convergence in one go.</param>
/// <param name="host">The level, its sun and its renderer.</param>
/// <param name="fields">The level's distance field, which the convergence and the sweep trace against.</param>
/// <param name="fixtures">The level's lamps, which the cache bounces off as area emitters.</param>
internal sealed class SurfaceCacheResidency(
    IGraphicsDevice device,
    IOpenIgiLogger logger,
    bool lockstep,
    IGlobalIlluminationHost host,
    DistanceFieldResidency fields,
    Func<IReadOnlyList<LightFixture>> fixtures) : IDisposable
{
    private readonly IGraphicsDevice _device = device;
    private readonly IOpenIgiLogger _logger = logger;
    private readonly bool _lockstep = lockstep;
    private readonly IGlobalIlluminationHost _host = host;
    private readonly DistanceFieldResidency _fields = fields;
    private readonly Func<IReadOnlyList<LightFixture>> _fixtures = fixtures;

    /// <summary>Gets the lit surface cache, once captured and lit.</summary>
    public SurfaceCacheLighting? CardLighting { get; private set; }

    private SurfaceCacheLookup? _cardLookup;
    private SurfaceCacheCapture.Result? _capturedCards;
    private SurfaceCacheInstances? _cardInstances;

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

    // Frames since the sun last moved, for the fast follow a clock scrub needs. See GlobalIllumination.GiSunFollowFrames.
    /// <summary>Gets or sets the frames since the sun last moved, for the fast follow a clock scrub needs.</summary>
    public int SunMovedFrames { get; set; } = GlobalIllumination.GiSunFollowFrames + 1;

    // The density the cache currently in memory was actually built at, so a change can be noticed and
    // rebuilt from rather than quietly ignored until the next level load.
    private float _builtCardDensity;

    // What has already been ATTEMPTED, as opposed to what succeeded. A level whose every model is
    // spline-bent captures no cards at all, and the capture is asked for once a frame. Without this, one
    // failure becomes a full re-traversal of every placement, every frame, for as long as the level is up:
    // the cheapest way to turn a missing feature into an unplayable one. A retry needs a reason, and the
    // reason is that the density changed.
    private float _attemptedCardDensity = float.NaN;

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

    /// <summary>Gets whether the cache is wanted and its capture has not yet been attempted at this density.</summary>
    public bool IsCapturePending =>
        SurfaceCacheCVars.SurfaceCache.Value && _host.World is not null && _capturedCards is null
        && !SurfaceCacheCVars.SurfaceCacheDensity.Value.Equals(_attemptedCardDensity);

    /// <summary>Whether a convergence is stepping, or one is wanted and has not yet run on a ready level.</summary>
    /// <param name="ready">Whether both structures the dynamic model lights from are built.</param>
    /// <returns><see langword="true"/> when the convergence is still to come.</returns>
    public bool IsConvergencePending(bool ready) =>
        IsConverging
        || (ready && WantsConvergence && !_giConverged);

    /// <summary>Whether a convergence is stepping right now.</summary>
    public bool IsConverging => _giConvergence is { IsComplete: false };

    /// <summary>How far the convergence has come, or one when there is none to wait for.</summary>
    public float ConvergeProgress => _giConvergence?.Progress ?? (_giConverged ? 1f : 0f);

    private static bool WantsConvergence =>
        SurfaceCacheCVars.GiConverge.Value > 0 && SurfaceCacheCVars.SurfaceCacheProbe.Value == 0;

    public void EnsureSurfaceCache()
    {
        bool wanted = SurfaceCacheCVars.SurfaceCache.Value && _host.World is not null;
        float density = SurfaceCacheCVars.SurfaceCacheDensity.Value;

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

        if (SurfaceCacheCVars.SurfaceCacheDump.Value is { Length: > 0 } into)
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
    public void BeginConvergence()
    {
        int passes = SurfaceCacheCVars.GiConverge.Value;

        if (_giConverged || _giConvergence is not null || !WantsConvergence)
        {
            return;
        }

        if (CardLighting is not { } cards || _fields.DistanceField is not { } field)
        {
            // Not a failure: this runs from both halves of the pair and the first one to arrive has
            // nothing to converge yet. Said out loud anyway, because a convergence that silently does not
            // happen is invisible -- the level simply opens on the old ramp, which is exactly what it
            // looks like when the feature is off. That cost an afternoon once.
            _logger.Information(
                "GlobalIllumination",
                $"Cannot converge yet: the surface cache is {(CardLighting is null ? "not built" : "ready")} "
                + $"and the distance field is {(_fields.DistanceField is null ? "not built" : "ready")}. "
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
            sun.Colour * (LightingCVars.SunScale.Value * LightingCVars.LightScale.Value),
            SkyLight.Fill(sun),
            SurfaceCacheCVars.GlobalIlluminationBoost.Value,
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
                if (CardLighting is not null && _fields.DistanceField is not null)
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

        if (CardLighting is null || _fields.DistanceField is null)
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

        int slices = SurfaceCacheCVars.GiFarRefresh.Value;
        if (slices <= 0
            || CardLighting is not { } cards
            || _fields.DistanceField is not { } field
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
        Vector3f sunColour = sun.Colour * (LightingCVars.SunScale.Value * LightingCVars.LightScale.Value);
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
        int steps = SunMovedFrames <= GlobalIllumination.GiSunFollowFrames ? Math.Min(GiSunFollowSlices, slices) : 1;
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
        int steady = Math.Max(SurfaceCacheCVars.GiTemporalFrames.Value, 1);
        // Right after a change -- a scrub of the clock, a lamp switched -- the first sweeps take the new
        // light at half weight, so the far cache follows the hour instead of easing towards it over the long
        // steady blend; the sweeps after fold bounce in at that steady weight as before.
        int pass = _giRefreshQuietRounds < 2 ? 1 : steady + 1 + _giRefreshRounds;
        cards.ConvergeBounceSlice(
            field, SurfaceCacheCVars.GlobalIlluminationBoost.Value, pass: pass, slice, slices);
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
        ExposureWarmup = GlobalIllumination.GiExposureWarmupFrames;

        if (CardLighting is null)
        {
            return;
        }

        // And into the bounce: the fixtures are area emitters as far as the cache is concerned, so a
        // lamplit room lights its own walls off each other.
        CardLighting.SetLocalLights(_fixtures());

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

        if (SurfaceCacheCVars.SurfaceCacheDump.Value is { Length: > 0 } lightingInto)
        {
            SurfaceCacheDebugDumps.CardLighting(
                _device, CardLighting, lightingInto, _logger, 0, "cards-direct-load.png");
        }
    }

    /// <summary>Stops whatever convergence is running and forgets that this level converged, as when its field is thrown away.</summary>
    public void ResetConvergence()
    {
        _giConverged = false;
        _giSettleWaitFrames = 0;
        _giConvergence = null;
    }

    /// <summary>Forgets the cache a session is about to replace, without freeing it.</summary>
    public void Forget()
    {
        CardLighting = null;
        _cardLookup = null;
        CardBounceFrame = 0;
        _capturedCards = null;
        _cardInstances = null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        CardLighting?.Dispose();
        _cardLookup?.Dispose();
    }
}
