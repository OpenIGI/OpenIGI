using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Host.Lighting;
using OpenIGI.Presentation.Lighting;
using OpenIGI.Rendering;

namespace OpenIGI.Host.Services;

/// <summary>
/// The real-time lighting model's share of a frame: before the G-buffer, the clipmaps followed, the
/// surface cache relit and bounced and the radiance cache updated; while it is still live, the screen
/// gather traced and resolved into the indirect the deferred lighting folds in.
/// </summary>
/// <param name="device">The device the frame is drawn on.</param>
/// <param name="logger">Where validation and dumps are reported.</param>
/// <param name="host">The level, its sky and its probe field.</param>
/// <param name="fields">The level's distance field.</param>
/// <param name="cards">The lit surface cache.</param>
internal sealed class SceneLightingAssembler(
    IGraphicsDevice device,
    IOpenIgiLogger logger,
    IGlobalIlluminationHost host,
    DistanceFieldResidency fields,
    SurfaceCacheResidency cards)
{
    private readonly IGraphicsDevice _device = device;
    private readonly IOpenIgiLogger _logger = logger;
    private readonly IGlobalIlluminationHost _host = host;
    private readonly DistanceFieldResidency _fields = fields;
    private readonly SurfaceCacheResidency _cards = cards;

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
            _cards.CardLighting is { } reflectionCards && RenderCVars.SurfaceCacheProbe.Value == 0
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
            && _fields.DistanceField is not null
            && _cards.CardLighting is not null
            && RenderCVars.SurfaceCacheProbe.Value == 0;

        // Every placement that moves, carried in the surface cache to where it was drawn this frame,
        // before anything below looks a surface up -- a truck the camera rides in reads its own
        // interior rather than the yard it drove out of. See SurfaceCacheLighting.MovePlacement.
        if (_cards.CardLighting is { } movingCards && _host.World is { } movingWorld && movingWorld.Movers.Count > 0)
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
        if (giActive && _device.RawLighting && _fields.DistanceField is { } pixelField && _cards.CardLighting is { } pixelCards)
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

        if (!giPixelsRan && giActive && _fields.DistanceField is { } liveGiField && _cards.CardLighting is { } liveGiCards)
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
        if (_fields.DistanceField is { } liveField)
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
            if (!_fields.FieldValidated && RenderCVars.DistanceFieldValidate.Value && _fields.DistanceFieldScene is { } exact)
            {
                _fields.FieldValidated = true;
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
                && _cards.CardBounceFrame >= 8;

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
            _cards.SunMovedFrames = giSunMoved ? 0 : Math.Min(_cards.SunMovedFrames + 1, GlobalIllumination.GiSunFollowFrames + 1);
            bool followingSun = _cards.SunMovedFrames <= GlobalIllumination.GiSunFollowFrames;
            bool retraceGi = _giStaticFrames <= giSettle;

            if (_cards.CardLighting is { } litCards)
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
            if (_cards.CardLighting is { } bounceCards && RenderCVars.SurfaceCacheProbe.Value == 0 && retraceGi)
            {
                bounceCards.Bounce(
                    liveField,
                    boost: RenderCVars.GlobalIlluminationBoost.Value,
                    temporalIndex: wantDump ? 0 : _cards.CardBounceFrame);
                _cards.CardBounceFrame++;

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
                && _cards.CardLighting is { } cacheCards
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
                    maxFrames: _cards.ExposureWarmup > 0 ? 6 : 32,
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
