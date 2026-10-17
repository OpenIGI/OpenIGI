using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Host.Lighting;
using OpenIGI.Rendering;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Host;

internal sealed partial class GameHost
{
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

    // The next slice of the whole-cache refresh. See StepGiRelight.
    private int _giRefreshSlice;

    // Whole sweeps the refresh has completed, which is what advances its jitter from one sweep to the next.
    private int _giRefreshRounds;

    // What the refresh last saw its inputs as, and how many whole sweeps it has finished since any of them
    // changed. See StepGiRelight.
    private Vector3f _giRefreshSunDirection;
    private Vector3f _giRefreshSunColour;
    private int _giRefreshLampRevision = -1;

    // The coarse lamp maps as the far refresh last saw them: while they are being drawn the far cache has
    // to be relit, or the cards lit through a wall before a lamp's maps existed would keep that light.
    private int _giRefreshBounceMapVersion = -1;
    private int _giRefreshQuietRounds;
    private bool _giRefreshWorldSettled;

    // The rendered frame the refresh last advanced on; see StepGiRelight.
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
    // BeginGiConvergence.
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
    private int _lampsMissionId = -1;

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
    /// Reading the model through here instead makes the switch safe from both ends. <see cref="EnsureRealtimeGiStructures"/>
    /// starts building the moment the model is asked for, and until it finishes this reports
    /// <see cref="LightingModel.Baked"/> — so the frame keeps the lighting it already had and crosses over
    /// when there is something better to cross to. There is never a wrong frame, only an early one.
    /// </para>
    /// </remarks>
    private LightingModel EffectiveLighting =>
        RenderCVars.Lighting.Value is var wanted && wanted == LightingModel.Baked ? wanted
        : RealtimeGiReady ? wanted
        : LightingModel.Baked;

    /// <summary>Whether everything the dynamic model lights from is built and usable.</summary>
    /// <remarks>
    /// Both structures, not either: a gather with a distance field and no surface cache traces to a hit it
    /// cannot read the colour of, and one with a cache and no field cannot find the hit at all. The device
    /// check is the same one the builders make — without compute there is no field to build, so a level
    /// loaded on such a device stays on the bakes however the preset is set.
    /// </remarks>
    private bool RealtimeGiReady =>
        _cardLighting is not null && _sdfClipmaps is not null && _device.Capabilities.SupportsCompute;

    /// <summary>Whether the frame wants the dynamic model at all, on a device that could run it.</summary>
    private bool WantsRealtimeGi =>
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
    private bool RealtimeGiPending
    {
        get
        {
            if (!WantsRealtimeGi || _scene is null)
            {
                return false;
            }

            bool fieldWanted = RenderCVars.DistanceFields.Value;
            bool fieldPending = fieldWanted && _sdfClipmaps is null
                && (_sdfBake is not null || _attemptedFieldResolution != RenderCVars.DistanceFieldResolution.Value);

            bool cardsWanted = RenderCVars.SurfaceCache.Value && _world is not null;
            bool cardsPending = cardsWanted && _capturedCards is null
                && !RenderCVars.SurfaceCacheDensity.Value.Equals(_attemptedCardDensity);

            bool convergencePending = IsGiConverging
                || (RealtimeGiReady && WantsGiConvergence && !_giConverged);

            return fieldPending || cardsPending || convergencePending;
        }
    }

    /// <summary>Whether a convergence is stepping right now.</summary>
    private bool IsGiConverging => _giConvergence is { IsComplete: false };

    /// <summary>How far the convergence has come, or one when there is none to wait for.</summary>
    private float GiConvergeProgress => _giConvergence?.Progress ?? (_giConverged ? 1f : 0f);

    private static bool WantsGiConvergence =>
        RenderCVars.GiConverge.Value > 0 && RenderCVars.SurfaceCacheProbe.Value == 0;

    /// <summary>The mission whose level is up, played or previewed, for keying its caches.</summary>
    private int? CurrentMissionId => _activeMission?.Id ?? _backdrop?.MissionId;

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
    private void EnsureRealtimeGiStructures()
    {
        // First, because the lite backdrop's overrides decide whether either structure is wanted at all.
        UpdateBackdropProfile();

        if (_scene is null || (_activeMission is null && _backdrop is null))
        {
            return;
        }

        EnsureDistanceFields();
        EnsureSurfaceCache();
    }

    private void EnsureDistanceFields()
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
        if (_sdfClipmaps is not null && resolution != _builtFieldResolution)
        {
            DiscardDistanceFields();
        }

        if (_sdfClipmaps is not null || _sdfBake is not null)
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
            && _sdfCache.TryLoad(missionId, resolution) is { } cached)
        {
            AdoptDistanceField(cached, DistanceFieldAtlas.Build(cached.Fields), fromCache: true);
            return;
        }

        _sdfBake = DistanceFieldBake.Start(_scene!.Placements, _work, resolution, _logger);
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
    private void AdoptDistanceField(SceneDistanceField scene, DistanceFieldAtlas atlas, bool fromCache)
    {
        _sdfScene = scene;
        _sdfValidated = false;
        _sdfClipmaps?.Dispose();
        _sdfClipmaps = GlobalDistanceField.Create(_device, scene, atlas, DistanceFieldSettings(), _logger);

        BeginGiConvergence();

        if (fromCache || CurrentMissionId is not { } missionId)
        {
            return;
        }

        // Written under the resolution it was actually baked at rather than whatever the variable says
        // now: the player can move r_sdf_resolution while a bake is in flight, and a file keyed by the
        // new value would be a field that does not match its own key.
        _sdfCache.Save(missionId, _builtFieldResolution, scene);
    }

    private void EnsureSurfaceCache()
    {
        bool wanted = RenderCVars.SurfaceCache.Value && _world is not null;
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
        if (_world!.CaptureSurfaceCache(_scene!, density) is not { } cards)
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
        BeginGiConvergence();
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
    /// Started here and STEPPED by <see cref="StepGiConvergence"/> from the frame loop, rather than run to
    /// completion: as one call it measured two and a half seconds on a large level, and it ran whenever the
    /// second half of the pair landed, which on a cold cache was several seconds into play. Spread over
    /// frames the same work goes on behind the loading screen, or under the held shot, while the picture
    /// keeps presenting. A lockstep capture still runs it in one go, because a capture has to be the same
    /// frame every time.
    /// </para>
    /// </remarks>
    private void BeginGiConvergence()
    {
        int passes = RenderCVars.GiConverge.Value;

        if (_giConverged || _giConvergence is not null || !WantsGiConvergence)
        {
            return;
        }

        if (_cardLighting is not { } cards || _sdfClipmaps is not { } field)
        {
            // Not a failure: this runs from both halves of the pair and the first one to arrive has
            // nothing to converge yet. Said out loud anyway, because a convergence that silently does not
            // happen is invisible -- the level simply opens on the old ramp, which is exactly what it
            // looks like when the feature is off. That cost an afternoon once.
            _logger.Information(
                "GlobalIllumination",
                $"Cannot converge yet: the surface cache is {(_cardLighting is null ? "not built" : "ready")} "
                + $"and the distance field is {(_sdfClipmaps is null ? "not built" : "ready")}. "
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

        // The sun the frame will light with, derived the same way -- NOT _effectiveSun ?? _sun. During a
        // load the first is not set for this level yet, and the second is the level's raw authored
        // Dirlight, which every Project I.G.I. mission authors as a noon sun: a night mission's whole
        // cache was converged in daylight. See CycleSun.
        DirectionalLightSettings sun = CycleSun();
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
            SkyFillColour(sun),
            RenderCVars.GlobalIlluminationBoost.Value,
            fullPasses);

        if (_options.Lockstep)
        {
            _giConvergence.RunAll();
            FinishGiConvergence();
        }
    }

    /// <summary>Runs this frame's share of the convergence, if one is in flight. Once per frame.</summary>
    private void StepGiConvergence()
    {
        if (_giConvergence is null || _giConvergence.IsComplete)
        {
            // Nothing in flight. A level whose cache and field are both here but which has not converged
            // was waiting on the world's structure; ask again. One that has converged is relit when what it
            // was lit by has moved on.
            if (!_giConverged)
            {
                if (_cardLighting is not null && _sdfClipmaps is not null)
                {
                    BeginGiConvergence();
                }
            }
            else
            {
                StepGiRelight();
            }

            if (_giConvergence is null || _giConvergence.IsComplete)
            {
                return;
            }
        }

        SurfaceCacheConvergence job = _giConvergence;

        if (_cardLighting is null || _sdfClipmaps is null)
        {
            // Whatever it was converging has been discarded under it.
            _giConvergence = null;
            return;
        }

        job.Step();
        if (job.IsComplete)
        {
            FinishGiConvergence();
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
    private void StepGiRelight()
    {
        // Once per RENDERED frame. This runs once per pass of the loop, and a pass that only ticks the
        // simulation renders nothing: its slice landed in the next frame beside that frame's own, so one frame
        // in every few carried two -- every fourth at a 30 Hz simulation under a 120 Hz picture, and on
        // mission 3's large cache that was a 3.5 ms frame becoming a 7 ms one, over and over.
        if (_loop.Clock.FrameCount == _giRefreshLastFrame)
        {
            return;
        }

        _giRefreshLastFrame = _loop.Clock.FrameCount;

        int slices = RenderCVars.GiFarRefresh.Value;
        if (slices <= 0
            || _cardLighting is not { } cards
            || _sdfClipmaps is not { } field
            || _effectiveSun is not { } sun)
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
            || _lampBounceRevision != _giRefreshLampRevision
            || _device.LampBounceMapVersion != _giRefreshBounceMapVersion
            || !NearlyEqual(sunDirection, _giRefreshSunDirection)
            || !NearlyEqual(sunColour, _giRefreshSunColour);

        if (changed)
        {
            _giRefreshQuietRounds = 0;
            _giRefreshLampRevision = _lampBounceRevision;
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
        int steps = _giSunMovedFrames <= GiSunFollowFrames ? Math.Min(GiSunFollowSlices, slices) : 1;
        for (int step = 0; step < steps; step++)
        {
            StepGiRelightSlice(cards, field, sun, sunDirection, sunColour, slices);
        }
    }

    /// <summary>Relights and bounces one slice of the whole cache, and moves the sweep on.</summary>
    private void StepGiRelightSlice(
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
            SkyFillColour(sun),
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

    private void FinishGiConvergence()
    {
        if (_giConvergence is not { } job)
        {
            return;
        }

        _giConverged = true;

        // The frame loop picks the accumulation up from here rather than restarting it, so the passes
        // just run are not repeated at full weight on the first frames.
        _cardBounceFrame = job.Passes;

        // Nothing for the eye adaptation to chase: the first frame is already the converged brightness,
        // so the fast warm-up rate would only make the exposure hunt around an answer it already has.
        _giExposureWarmup = 0;

        _logger.Information(
            "GlobalIllumination",
            $"Converged the surface cache through {job.Passes} bounce(s) over {job.ElapsedMilliseconds} ms of "
            + "presented frames, so the level opens lit rather than filling in.");
        _giConvergence = null;
    }

    private void DiscardDistanceFields()
    {
        _giConverged = false;
        _giSettleWaitFrames = 0;
        _giConvergence = null;
        // Clearing the latch as well as the structures, so the next level -- or the next resolution --
        // gets its own attempt rather than inheriting this one's verdict.
        _attemptedFieldResolution = 0;
        _sdfBake?.Cancel();
        _sdfBake = null;
        _sdfClipmaps?.Dispose();
        _sdfClipmaps = null;
        _sdfScene = null;
        _sdfValidated = false;
    }

    private void DiscardSurfaceCache()
    {
        _giConverged = false;
        _giSettleWaitFrames = 0;
        _giConvergence = null;
        _attemptedCardDensity = float.NaN;
        _capturedCards = null;
        _cardInstances = null;
        _cardLighting?.Dispose();
        _cardLighting = null;
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
    private void ApplyLamps(EmissiveLightProposer.Proposal lamps, int missionId)
    {
        _lampsMissionId = missionId;
        _lightFixtures = lamps.Fixtures;

        // One switchboard per set of fixtures, seeded by the mission so two levels' broken tubes do not
        // stutter in step. Everything that reads a lamp's output reads it from here, and everything that
        // holds one is handed the same instance rather than a copy of its state.
        _lampSwitchboard = new LampSwitchboard(lamps.Fixtures, missionId);
        _lampBounceRevision = _lampSwitchboard.Revision;

        // Which sub-meshes glow and how brightly. Per sub-mesh rather than per texture, because one
        // texture is not one brightness -- a floodlight and the lamp over a door draw with the same page
        // -- and because a lamp's page also skins the furniture beside it. r_lamp_emissive scales the
        // whole set on the way in rather than in the shader: it is a property of the material, so it
        // belongs where the material is decided and costs nothing per pixel.
        float emissiveScale = RenderCVars.LampEmissiveScale.Value;

        _lamps = emissiveScale == 1f
            ? lamps.Lamps
            : lamps.Lamps with
            {
                SubMeshes = lamps.Lamps.SubMeshes.ToDictionary(
                    e => e.Key, e => e.Value * emissiveScale, StringComparer.OrdinalIgnoreCase),
            };

        if (_world is not null)
        {
            _world.Lamps = _lamps;
            _world.Switchboard = _lampSwitchboard;
        }

        // The lit cache reads the fixtures as area emitters; one built before they were known has to be
        // told about them. The refresh sweep carries them to the far cards.
        _cardLighting?.SetLocalLights(_lightFixtures);
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

        _cardLighting?.Dispose();
        _cardLighting = SurfaceCacheLighting.Create(_device, surfaces, placedCards, _logger);

        // A fresh pass has no history to accumulate onto, so the next bounce converges in one shot.
        _cardBounceFrame = 0;

        // The cache now climbs from dark to its full bounce over the frames that follow; hold the exposure
        // on a fast rate for that stretch so it tracks the light in rather than dimming after.
        _giExposureWarmup = GiExposureWarmupFrames;

        if (_cardLighting is null)
        {
            return;
        }

        // And into the bounce: the fixtures are area emitters as far as the cache is concerned, so a
        // lamplit room lights its own walls off each other.
        _cardLighting.SetLocalLights(_lightFixtures);

        // Built once, from the same capture and placements the lighting was. A ray that stops somewhere
        // knows only that it stopped; this is what lets it read the surface it hit.
        _cardLookup?.Dispose();
        _cardLookup = SurfaceCacheLookup.Build(
            _device, surfaces, placedCards, _cardLighting.Origin, _logger);
        _cardLighting.SetLookup(_cardLookup);

        // A level with no Dirlight has no sun to light the cards with, so they take the sky alone -- which
        // is what such a level looks like anyway.
        //
        // Unshadowed here. At load the clipmaps do not exist yet -- the field bake is still on its worker --
        // so this establishes sun and sky, and the per-frame pass adds the shadow once there is a field to
        // trace against.
        _cardLighting.Light(
            _sun is { IsBelowHorizon: false } lit ? lit.Direction : default,
            _sun?.Colour ?? default,
            SkyFillColour());

        if (RenderCVars.SurfaceCacheDump.Value is { Length: > 0 } lightingInto)
        {
            SurfaceCacheDebugDumps.CardLighting(
                _device, _cardLighting, lightingInto, _logger, 0, "cards-direct-load.png");
        }
    }
}
