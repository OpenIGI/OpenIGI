using OpenIGI.Audio;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Threading;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Frontend.Saves;
using OpenIGI.Frontend;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Host.Audio;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.DevTools;
using OpenIGI.Host.Diagnostics;
using OpenIGI.Host.Infrastructure;
using OpenIGI.Host.Lighting;
using OpenIGI.Host.Menu;
using OpenIGI.Host.Titles;
using OpenIGI.Platform;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Presentation.Lighting;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Presentation.Text;
using OpenIGI.Presentation;
using OpenIGI.Core.Configuration.Presets;
using OpenIGI.Rendering;
using OpenIGI.Scripting.Runtime;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;
using OpenIGI.UI;
using OpenIGI.Host.Services;

using System.Diagnostics;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    /// <summary>Draws a frame of the world: the shadows, the scene, its lighting and effects, and what is drawn over it.</summary>
    /// <param name="state">What the frame loop keeps from one frame to the next.</param>
    /// <param name="console">The developer console, drawn over the frame.</param>
    /// <remarks>Run once a frame while a level is up: there is a scene, a world renderer and a camera.</remarks>
    private void RenderWorld(FrameLoopState state, HostConsole console)
    {
        if (_scene is null || _world is null || _camera is null)
        {
            return;
        }

        StepScriptedFlight(state);

        // How far this frame sits between the last two simulation ticks. Compute it before
        // either projection: MP5 sight zoom changes the world and view-model FOV on those ticks,
        // and both need the same presentation blend to remain welded together at uncapped rates.
        //
        // Off entirely while the pause menu is up, and this is what makes a paused frame actually
        // still. Nothing is stepping, so the last two ticks stay where they are — while the blend
        // between them keeps sweeping 0 to 1 with real time, which rocked the eye, the arms and
        // every guard back and forth between two poses a thirtieth of a second apart. At blend 1
        // every one of those readings is simply the tick that was last run.
        // Cutscene camera and vehicle transforms are authored and committed on the 30 Hz task
        // tick. Blending only the SoldierRenderer between its previous/current records while the
        // camera and its parent vehicle stay on the current record makes a seated actor sweep
        // backwards after every tick, then glide forwards again. Retail presents the cinematic
        // transform set atomically, so keep the actor on that same current tick as its plane/train.
        bool smoothPresentation = !_menuFlow.PauseMenu
            && _mission.Cutscenes?.IsPlaying != true
            && !_options.Lockstep
            && RenderCVars.SmoothPresentation.Value;
        double pacingWall = _loop.Clock.FrameInstantMilliseconds;
        float tickBlend = smoothPresentation
            ? _loop.Clock.TickFraction(pacingWall)
            : 1f;
        _titlePresentationBlend = TitlePlayer is { IsCutscenePlaying: true } ? 1f : tickBlend;
        PresentTitleLook(smoothPresentation && !console.WantsPointer);

        // Whether the picture's own clock keeps step with the wall clock, behind
        // the Pacing log category at Debug. Sampled here because this pair -- the tick and the blend --
        // is exactly the moment in the simulation the frame is about to be drawn at.
        _pacing.Sample(
            pacingWall, _loop.Clock.TickCount, tickBlend, _loop.Clock.TickRate, smoothPresentation);
        _pacing.SampleGate(
            _menuFlow.PauseMenu,
            _mission.Cutscenes?.IsPlaying == true,
            _options.Lockstep,
            RenderCVars.SmoothPresentation.Value);
        if (_loop.Clock.FrameCount % 600 == 0)
        {
            _pacing.Report("vulkan");
        }
        SightZoomFrame sight = _playerMode && Viewpoint?.SightZoom(tickBlend) is { } playerSight
            ? playerSight
            : _playerMode && TitlePlayer is { } titleLens
                ? new SightZoomFrame(_titlePresentation.FieldOfView(_titlePresentationBlend), HumanView.DefaultViewModelTanX)
                : new SightZoomFrame(HumanView.DefaultTanX, HumanView.DefaultViewModelTanX);

        float aspectRatio = (float)_window.Width / Math.Max(1, _window.Height);
        // A window that reports no size at all -- which some window systems do for a frame
        // during a minimize or a dock -- would divide the projection by zero.
        if (aspectRatio <= 0f)
        {
            aspectRatio = 1.3333f;
        }

        // A pinned lens for side-by-side comparisons: -fov overrides the game's own ninety-degree
        // horizontal, so a capture can be framed exactly as an external render whose camera
        // differs. The sight zoom still governs ordinary play.
        float horizontalTangent = _options.FieldOfView is { } fixedFov
            ? MathF.Tan(fixedFov * (MathF.PI / 360f))
            : sight.TanX;
        float verticalFieldOfView = VerticalFieldOfView(aspectRatio, horizontalTangent);

        // Kept for the developer inspector, which projects a selected entity to the screen and
        // has no other way to know the lens the frame was actually drawn with.
        _inspectorTangent = horizontalTangent;
        _inspectorAspect = aspectRatio;
        if (_mission.Cutscenes?.Frame is { FieldOfView: > 0f } cutsceneLens)
        {
            // EditCamera.FOV is the horizontal half-angle tangent submitted at 0x4F604A,
            // not an angle in radians. A value of 1 is retail's 90-degree horizontal lens.
            // The authored framing was composed through retail's fixed 4:3 viewport. Keep that
            // vertical aperture at modern aspects and let the wider projection reveal the sides;
            // deriving it from a 16:9 window crops the top and bottom by 25 percent, which cut
            // Anya's upper body out of the Mission 1 briefing.
            verticalFieldOfView = CutsceneVerticalFieldOfView(cutsceneLens.FieldOfView);
        }
        // The map's own view of the level, when the computer owns the frame. Under the tactical
        // interface the transition owns it for as long as it is flying — same viewport, but its
        // camera and its field of view, which is what makes the climb and the fall a move of the
        // player's own eye rather than a cut to a second one.
        MapSceneView? mapScene = null;
        if (_computerOpen && Viewpoint?.BodyPositionUnits is { } mapBody)
        {
            if (TacticalComputer is { } tacticalView)
            {
                MapSceneView steady = tacticalView.SceneView(
                    mapBody, _window.Width, _window.Height);
                mapScene = _computerFlight.IsFlying
                    ? steady with
                    {
                        Camera = _computerFlight.Camera,
                        FieldOfView = _computerFlight.FieldOfView,
                    }
                    : steady;
            }
            else if (_mapComputer is { IsReady: true, ShowsMap: true } computerView)
            {
                mapScene = computerView.SceneView(
                    mapBody, _window.Width, _window.Height);
            }
        }

        // What the tube is doing this frame, and how hard the shot is smearing. Read here, next
        // to the camera they belong to, rather than at the resolve five hundred lines below,
        // where the transition would be a name with no visible source.
        bool tacticalUp = _computerOpen && TacticalComputer is not null;
        TacticalDisplaySettings computerDisplay = tacticalUp ? _computerFlight.Display : default;
        float computerFlightBlur = tacticalUp ? _computerFlight.MotionBlur : 0f;

        // The binocular glass, raised on its own wall clock so the lens fades in at the same
        // rate whatever the frame rate. Gated on the device being open (the fade sits at 0
        // otherwise) and the lens cvar; the map tube above and this glass are never both up.
        // How long this frame took, for the shutter the motion blur measures its smear against.
        int motionNow = _loop.TimeSource.Milliseconds;
        float frameDeltaSeconds =
            Math.Clamp((motionNow - _lastMotionBlurMilliseconds) / 1000f, 1f / 1000f, 1f / 10f);
        _lastMotionBlurMilliseconds = motionNow;

        int binocularNow = _loop.TimeSource.Milliseconds;
        float binocularElapsed = Math.Clamp((binocularNow - _lastBinocularMilliseconds) / 1000f, 0f, 0.1f);
        _lastBinocularMilliseconds = binocularNow;
        _binocularOptics.Update(Viewpoint?.IsBinocularsOpen == true, binocularElapsed);
        BinocularDisplaySettings binocularDisplay =
            RenderCVars.BinocularLens.Value ? _binocularOptics.Display : default;
        // A little extra edge fringing while the optics are up, on top of the ordinary value, so
        // the lens carries chromatic aberration that ramps in with the glass.
        float binocularAberration = binocularDisplay.Amount * 2f;
        PostProcessSettings postProcess = new(
            RenderCVars.ToneMap.Value,

            // The player's brightness less whatever the night is taking off it; see
            // RenderCVars.EffectiveExposure. Never r_exposure directly -- the night must not be
            // able to edit, and then have to put back, the setting the player chose.
            RenderCVars.EffectiveExposure,
            RenderCVars.Bloom.Value,
            RenderCVars.BloomThreshold.Value,
            RenderCVars.WhitePoint.Value,
            // The dynamic model now composites in linear space -- albedo decoded before the multiply
            // -- so its scene buffer holds linear radiance and the resolve tone maps it directly. The
            // baked and vanilla models keep the original's gamma-space compositing, whose buffer is
            // display-encoded and has to be decoded before the tone map.
            SceneIsDisplayEncoded: _gi.EffectiveLighting != LightingModel.Dynamic,
            Contrast: RenderCVars.Contrast.Value,
            Saturation: RenderCVars.Saturation.Value,
            Vignette: RenderCVars.Vignette.Value,
            Grain: RenderCVars.Grain.Value,
            ChromaticAberration: MathF.Max(RenderCVars.ChromaticAberration.Value, binocularAberration),
            Sharpen: RenderCVars.Sharpen.Value,
            MotionBlur: MathF.Max(RenderCVars.MotionBlur.Value, computerFlightBlur),
            SpatialAa: RenderCVars.SpatialAa.Value,
            SpatialAaThreshold: RenderCVars.SpatialAaThreshold.Value,
            ColourLookup: RenderCVars.ColourLookup.Value,
            AutoExposure: RenderCVars.AutoExposure.Value,
            GreenPhosphor: mapScene is not null && TacticalComputer is null,
            Tactical: computerDisplay,
            Binocular: binocularDisplay,

            // Fast while the GI is still filling in after a load, slow once it has settled. Counted
            // down here so a frame that never reaches the cache update (a menu, a paused load) does
            // not burn the window; the cache's own reset above is what refills it.
            ExposureRate: _gi.ExposureWarmup > 0 ? GiExposureWarmupRate : ExposureSteadyRate,

            // Read off the loop's own time source rather than the wall clock, which is what makes
            // a -lockstep capture reproducible: that mode swaps in a synthetic clock advanced a
            // fixed amount per pass, so every captured frame smears by exactly the same length.
            // Clamped because a frame that stalled on a load should not smear the whole screen.
            FrameDeltaSeconds: frameDeltaSeconds,

            // The eye's night vision, only as deep as the night: a day is untouched.
            // Only once the sun is some six degrees under (Nightfall past about 0.45): dusk keeps its
            // colour, and the eye's night vision takes over only as real night does.
            NightShift: RenderCVars.NightShift.Value * NightVisionWeight(RenderCVars.Nightfall.Value));

        if (_gi.ExposureWarmup > 0)
        {
            _gi.ExposureWarmup--;
        }
        if (mapScene is { } mapView)
        {
            aspectRatio = (float)mapView.Viewport.Width / Math.Max(1, mapView.Viewport.Height);
            if (aspectRatio <= 0f)
            {
                aspectRatio = 1.3333f;
            }

            verticalFieldOfView = mapView.FieldOfView;
        }
        // Both planes are the original's own: 0x493580 initialises the near one to 409.6 units,
        // which is 0.1 m, and the far one to 409,600,000, which is 100 km. (Scripts can move the
        // near plane through the SetNearClippingPlane native, 0x497D00.) At 0.2 the near plane
        // reached twice as far as retail's and cut into any wall the sweep lets the body stand
        // 2.5 mm from; at the 4000 m the far plane carried since the first fly-through, every
        // mountain past four kilometres was clipped away. See ViewPlanes.
        Matrix4f unjitteredProjection = Matrix4f.CreatePerspective(
            verticalFieldOfView,
            aspectRatio,
            near: NearPlane,
            far: FarPlane);

        // Temporal accumulation works by sampling a different point inside each pixel every frame
        // and averaging what comes back, so the jitter is not a refinement of the technique -- it
        // is the technique. Without it every frame samples the same pixel centre and the history
        // is an average of identical samples, which is a blur and nothing else.
        //
        // It is a translation in clip space, which is exactly a sub-pixel shift of the whole
        // frustum: clip.xy += offset * clip.w, applied uniformly at every depth. Moving the camera
        // instead would shift the near plane more than the far one and reproject wrongly.
        bool wantsTemporal = RenderCVars.TemporalFeedback.Value < 1f
            && RenderCVars.OffscreenScene.Value
            && RenderCVars.Deferred.Value;

        Matrix4f projection = unjitteredProjection;
        float jitterX = 0f;
        float jitterY = 0f;

        // What reconstructs the frame, and so what it is rendered at, decided once: the render size,
        // the jitter sequence's length and the textures' mip bias all follow from it together, and
        // working any of them out separately is how a jitter ends up sized for one buffer and applied
        // to another. See RenderResolutionPolicy.
        ReconstructionPlan reconstruction = _device.PlanReconstruction(new ReconstructionRequest(
            RenderCVars.Reconstruction.Value,
            RenderCVars.DlssQualityMode.Value,
            _window.Width,
            _window.Height,
            wantsTemporal));
        _reconstructionActive = reconstruction.Active;
        RenderResolution resolution = RenderResolutionPolicy.Choose(
            _window.Width,
            _window.Height,
            RenderCVars.RenderScale.Value,
            wantsTemporal,
            reconstruction);
        _device.SetTextureLodBias(resolution.MipBias);

        if (wantsTemporal)
        {
            (jitterX, jitterY) = TemporalJitter.At(state.TemporalFrame, resolution.JitterPhases);

            // In RENDER pixels, not window pixels: the offset has to be half a pixel of the buffer
            // being rasterised into, or at a render scale below one it moves the sample by less
            // than the pixel it is meant to be exploring and the upsample has nothing to gather.
            projection = Matrix4f.CreateTranslation(
                jitterX * 2f / resolution.RenderWidth,
                jitterY * 2f / resolution.RenderHeight,
                0f) * unjitteredProjection;
            state.TemporalFrame++;
        }

        // The sky is behind everything and writes no depth, so it goes first; then terrain, which
        // is opaque and covers most of the screen, so drawing it before the objects gives the
        // depth test the most to reject.
        //
        // The eye: the title's own viewpoint composes it -- the walking eye, the camera over a dead
        // body, the orbit around a climbing one and the move over the shoulder -- from the free
        // camera and a title player's eye, which are the host's. A title with no viewpoint of its
        // own looks through those two alone.
        Camera3D? titlePlayerEye = _playerMode && TitlePlayer is not null ? _titlePresentation.Camera(_titlePresentationBlend) : null;
        Camera3D eye;
        Camera3D flashlightBody;
        if (Viewpoint is { } viewpoint)
        {
            eye = viewpoint.ComposeEye(
                new ViewpointFrame(
                    _playerMode,
                    smoothPresentation,
                    tickBlend,
                    titlePlayerEye,
                    _camera.Camera,
                    ThirdPersonAllowed: !_session.FailedMenu
                        && !_computerOpen
                        && !_entry.IsCameraActive
                        && _mission.Cutscenes?.IsPlaying != true
                        && mapScene is null
                        && !_menuFlow.OverShowcase,
                    Paused: _menuFlow.MenuMode,
                    _loop.TimeSource.Milliseconds,
                    ThirdPersonWalls()),
                out flashlightBody);
        }
        else
        {
            eye = titlePlayerEye ?? _camera.Camera;
            flashlightBody = eye;
        }

        bool flashlightShown = _playerMode
            && !_mission.IsPlayerDead
            && !_session.FailedMenu
            && !_computerOpen
            && !_entry.IsCameraActive
            && _mission.Cutscenes?.IsPlaying != true
            && mapScene is null
            && !_menuFlow.OverShowcase
            && Viewpoint?.HasPlayer == true;

        // Whether the character is drawn at all, read here rather than at the draw because the
        // SHADOW cascades run before it: deciding at the geometry pass left the body's shadow a
        // frame behind the body on the frame it appeared.
        //
        // TWO gates, and both have to open. The follow above places and animates the body; THIS
        // is what actually draws it, and the third-person camera holds it shut until it has left
        // the head far enough for a body to be a body rather than the inside of a skull filling
        // the screen. r_player_body overrides that for first person deliberately -- see the cvar
        // for what it buys and what it costs.
        bool thirdPersonBody = Viewpoint?.ShowsBody == true || RenderCVars.PlayerBody.Value;
        if (_soldiers is { } shownBody)
        {
            shownBody.PlayerBodyFollowVisible = thirdPersonBody;
        }

        // The pose the frame is actually drawn from, whichever camera supplied it, in the form
        // -campos/-yaw/-pitch take. Without this a viewpoint reached by WALKING to it cannot be
        // captured: the free camera opens over the middle of the level, so the two modes can only
        // ever be compared at different places -- which is exactly the confusion that hid a
        // difference between them.
        if (_options.LogCamera)
        {
            Vector3f eyeForward = eye.Orientation.Row3;
            float eyePitch = MathF.Asin(Math.Clamp(eyeForward.Z, -1f, 1f));
            float eyeYaw = MathF.Atan2(eyeForward.X, eyeForward.Y);

            // The sun rides along, because the pose alone does not reproduce a viewpoint any more.
            // The day/night keys sweep the sun's elevation AND its bearing, so a scene reached by
            // holding one of them cannot be captured from -campos/-yaw/-pitch: the harness would
            // render the authored noon sun and light a different room. With this the whole line
            // pastes onto a capture command and the frame comes back as it was seen. One frame
            // stale while the sun is being scrubbed, which does not matter for standing still.
            Vector3f loggedSun = _effectiveSun?.Direction ?? Vector3f.UnitZ;

            // Warning rather than Information, which reads wrong and is not: the shell's logger
            // is built at Warning, so every Information call in the app is discarded and this
            // flag produced no output at all -- the flag existed and did nothing. It is opt-in
            // and its whole purpose is to be read, so it has to be emitted at a level that
            // survives. The same is true of the older mouse-look line just above.
            _logger.Warning(
                "Camera",
                $"eye {(_playerMode ? "player" : "free")} -campos {eye.Position.X:0.###},{eye.Position.Y:0.###},"
                    + $"{eye.Position.Z:0.###} -yaw {eyeYaw:0.#####} -pitch {eyePitch:0.#####}"
                    + $" -sundir {loggedSun.X:0.######},{loggedSun.Y:0.######},{loggedSun.Z:0.######}");
        }

        // Behind the cinematic shell the eye is the slow menu orbit, on real elapsed time, whatever
        // camera the scene would otherwise have used. Not behind the pause menu: the camera the
        // scene would otherwise have used is the player's own, and orbiting away from where they
        // were standing is the one thing a pause must not do. The showcase camera outlives the menu
        // it was built for -- starting a mission does not clear it -- so this has to say so.
        if (_menuFlow.OverShowcase && _showcaseCamera is not null)
        {
            int menuCameraNow = _loop.TimeSource.Milliseconds;
            float menuCameraElapsed = Math.Clamp((menuCameraNow - state.LastMenuCameraMilliseconds) / 1000f, 0f, 0.1f);
            state.LastMenuCameraMilliseconds = menuCameraNow;
            eye = _showcaseCamera.Update(menuCameraElapsed);
        }
        else if (mapScene is { } liveMap)
        {
            eye = liveMap.Camera;
        }

        // The mission-entry shot owns the view outright while it runs, over the menu's orbit and
        // over the player's own eye alike -- it begins at one and ends exactly at the other.
        if (_entry.IsCameraActive)
        {
            eye = _entry.Camera;
        }
        else if (_mission.Cutscenes?.Frame is { } cutsceneView)
        {
            eye = cutsceneView.Camera;
        }

        // The eye has settled by here -- every camera that could have claimed the frame has
        // had its say -- so this is where the inspector's projection can read it.
        _inspectorEye = eye;
        if (TitlePlayer is not null && _playerMode && !console.WantsPointer && !_menuFlow.MenuMode)
        {
            _titleLastLiveLook = _titlePresentation.HasLookPreview;
            Camera3D titleEye = _titlePresentation.Camera(_titlePresentationBlend);
            _titleLastCameraMatches = eye.Position == titleEye.Position && eye.Orientation == titleEye.Orientation;
        }

        // Everything soldier-shaped resolves its own frame state once, here, so the shadow
        // cascades and the geometry pass draw identical bodies in identical places.
        // The backdrop's soldiers resolve their frame state too, because the shadow pass below is
        // about to read their poses. Only one of the two is ever live: `_soldiers` belongs to a
        // played mission, `_menuBackdrop.Soldiers` to the menu's.
        ISoldierBodies? shadowCasters = _soldiers ?? _menuBackdrop.Soldiers;
        ICharacterPresenter? casterBodies = shadowCasters is null ? null : new SoldierBodies(shadowCasters);
        _world.BeginFrame(tickBlend, smoothPresentation);
        shadowCasters?.BeginFrame(tickBlend, smoothPresentation);
        _gunPickupRenderer?.BeginFrame(tickBlend, smoothPresentation);

        // The modern path renders the scene offscreen so that it still exists, with its depth, once
        // it has been drawn -- which is what every effect from stage R3 on needs. With r_hdr off,
        // or on a backend that cannot offer a target, nothing here happens and the frame is drawn
        // straight to the back buffer exactly as the original draws it.
        SceneTargetDescription sceneTarget = new(
            resolution.RenderWidth,
            resolution.RenderHeight,
            RenderCVars.OffscreenScene.Value ? Math.Max(1, RenderCVars.MultiSampling.Value) : 1,
            Deferred: RenderCVars.OffscreenScene.Value && RenderCVars.Deferred.Value,

            // An 8-bit target under the vanilla preset: with no tone map and unit exposure the whole
            // offscreen detour is then a copy, which is what keeps that preset identical to the
            // build that had no offscreen path at all.
            HighDynamicRange: RenderCVars.ToneMap.Value != ToneMapOperator.None,

            // Asked for only when something reconstructs to it -- the accumulation under a render scale,
            // or an NVIDIA reconstruction at any size: without one these are two buffers holding a
            // stretched copy. See RenderResolutionPolicy.
            OutputWidth: resolution.OutputWidth,
            OutputHeight: resolution.OutputHeight,
            GBuffer: (_gi.EffectiveLighting != LightingModel.Dynamic
                    ? GBufferLayout.BakedLighting
                    : GBufferLayout.Compact)
                | (wantsTemporal || RenderCVars.MotionBlur.Value > 0f
                    ? GBufferLayout.MotionVectors
                    : GBufferLayout.Compact));

        // The sun arrives in world space and the G-buffer's normals are in view space, so it is
        // rotated by the camera here. Transposed() is not decoration: the rows of the orientation
        // ARE the view axes, and Transform() applies the engine's row-vector convention which dots
        // the columns -- the inverse rotation. Getting it wrong lights the world from a direction
        // that swings as the player turns.
        // The authored sun swept to the current time of day. Held for the frame so every consumer
        // -- this deferred light, the shadows, the surface cache's relight and its radiance cache,
        // and the sky fill -- lights from the same swept sun rather than the raw authored one.
        // The sun -- or the moon -- this frame lights with. See CycleSun: the same answer the surface
        // cache is converged under, so the two cannot light the level differently.
        DirectionalLightSettings light = CycleSun();

        // What the night is doing to the picture, published for the exposure. The sweep is the only
        // thing that knows how far under the horizon the sun has gone, and the resolve is a long way
        // from here, so it travels as the one hidden variable rather than as another argument
        // threaded through every pass between. The fixed-sun path leaves it at nought, which is what
        // a comparison capture wants: a pinned sun and an unmodified exposure.
        RenderCVars.Nightfall.Value = _dayNight.Nightfall;

        // And what the LEVEL says about its own darkness, which the adaptation cannot work out
        // for itself: a scene lit dimly all over reads to it as a correctly-exposed dark subject.
        //
        // The SWEEP's reading, not the level's own constant. It used to be the constant, which
        // pinned the exposure to the hour the level opened at: scrub a night mission to noon and
        // the sun came up while the picture stayed exposed for midnight. A title that authors its
        // own night is bound to the cycle by it, so the cycle's own reading is the level's -- and
        // it moves with the clock, which is the whole point of having one.
        //
        // And ONLY under the dynamic model, because that is the only one it describes. The drop
        // answers an eye adaptation that misreads a scene whose light is COMPUTED at low levels
        // and lifts the whole thing back to mid grey. A model that reproduces the level's own
        // composited picture computes nothing to be misread: the picture is what the artists made,
        // and re-exposing it for a night it already contains is subtracting the same night twice.
        // Fitted for the dynamic model, it crushed the other two -- mission 1's baked ground came
        // out at 0.18 of vanilla where the dynamic one landed on 0.93.
        //
        // The REQUESTED model, not _gi.EffectiveLighting. That one reports Baked until the real-time
        // structures finish building, so reading it here would move the exposure partway through a
        // load -- and the adaptation that rides on it is temporal, so the step would be chased
        // rather than taken. What the exposure follows has to be a decision the player made, not a
        // state the loader is in.
        RenderCVars.AuthoredNightfall.Value =
            _deferredLighting is null || RenderCVars.Lighting.Value != LightingModel.Dynamic
                ? 0f
                : _dayNight.Nightfall;

        // And how much a full night of it is worth, which is the title's to say: see
        // DeferredLighting.NightExposureDrop.
        RenderCVars.AuthoredNightDropFor.Value =
            _deferredLighting?.NightExposureDrop ?? RenderCVars.AuthoredNightDrop;

        _effectiveSun = light;
        Vector3f viewSunDirection = eye.Orientation.Transposed().Transform(light.Direction);

        // The sun gizmo, if it is up: projected against this frame's camera and dragged this frame,
        // so there is no lag between the ring under the cursor and the sun it turns. Its rotation
        // feeds straight back into the cycle, the same knob the numpad keys turn.
        _sunGizmo.Update(
            eye,
            DepthUnprojection.From(unjitteredProjection, NearPlane, FarPlane),
            light.Direction,
            _window.Width,
            _window.Height,
            _window.Input.Mouse.X,
            _window.Input.Mouse.Y,
            _window.Input.Mouse.IsDown(Input.MouseButton.Left),
            (axis, delta) => _dayNight.RotateAxis(axis, delta));

        bool wantsShadows = RenderCVars.OffscreenScene.Value
            && RenderCVars.Deferred.Value
            && RenderCVars.Shadows.Value > 0;

        ShadowSettings shadowSettings = wantsShadows
            ? new ShadowSettings(
                RenderCVars.ShadowCascades.Value,
                RenderCVars.Shadows.Value,
                RenderCVars.ShadowDistance.Value)
            {
                ContactReach = RenderCVars.ContactShadows.Value,
                FilterRadius = RenderCVars.ShadowSoftness.Value,

                // The live software/hardware switch: off, a tracing backend leaves the sun to the
                // cascaded map like everyone else. r_rt_shadows moves the sun's shadow on its own,
                // leaving every other trace in the frame alone, which is what makes the two shadow
                // systems comparable in place.
                TracedSun = RenderCVars.HardwareRayTracing.Value && RenderCVars.TracedSunShadows.Value,

                // The day/night cycle fades the colour to exactly nought a tenth below the
                // horizon; from there every sun-shadow mask is multiplied by zero, and the whole
                // pipeline -- trace, denoise, cascades -- can stand down. Colour, not
                // IsBelowHorizon: through twilight a dim sun still lights and still shadows.
                SunLit = light.Colour.X > 0f || light.Colour.Y > 0f || light.Colour.Z > 0f,
            }
            : ShadowSettings.Off;

        int cascadeCount = ShadowCascades.Fit(
            eye,
            light.Direction,
            shadowSettings,
            verticalFieldOfView,
            aspectRatio,
            NearPlane,
            state.CascadeStorage);

        _device.SetShadows(
            shadowSettings,
            state.CascadeStorage.AsSpan(0, cascadeCount),
            DepthUnprojection.From(unjitteredProjection, NearPlane, FarPlane),

            // View space back into camera-relative world space, which is the frame the cascades
            // are fitted in. The same transpose, for the same reason.
            eye.Orientation.Transposed());

        // The medium. Density comes from the level's own FlatSky "Fog Amount" unless overridden:
        // a level that asked for thick fog gets thick fog, so the missions keep their own moods
        // instead of all being handed one number. The colours come from the sun, so a fog bank is
        // lit by the same light as the ground under it.
        // Lighter than it was: the authored FogAmount, read at full strength, laid a haze dense
        // enough to wash the yard white a few tens of metres out -- where the buildings should stay
        // crisp -- and it hung inside rooms besides. This carries the same shape at a
        // fraction of the extinction, so distance still reads but the far wall of a compound stays
        // legible.
        // A title that authors a real extinction gives it here, in the same units, and is
        // believed. I.G.I. 2's GlobalLight carries one per level -- mission 1 asks for 0.00174
        // per metre -- where the curve below is a fit over Project I.G.I.'s FlatSky "Fog Amount",
        // which is a 0-to-1 dial and not a length at all. Run through that fit the sequel's first
        // mission came out at 0.0070 per metre, four times its own figure, and the frame was a
        // blue wash with the buildings barely in it.
        float authoredDensity = _atmosphere is { FogDensity: > 0f } own
            ? own.FogDensity
            : _sky is { HasSky: true }
                ? 0.002f + (Math.Clamp(_sky.FogAmount, 0f, 1f) * 0.010f)
                : 0.006f;

        // Fog is an outdoor medium, and where it is outdoors is decided a froxel at a time inside
        // the march itself -- see the shelter lookup in the FogScatter shader module. It used to be
        // decided once for the whole frame, standing the march down while the camera was inside a
        // building, because the haze sitting between the eye and a window washed out the yard
        // beyond it: fog marched straight through the wall. Switching the march off answers that
        // by deleting the yard's fog too, so a compound seen from a doorway came up in clear air.
        // The room is now dry and the yard beyond the window is not, which is what is actually
        // out there.
        bool wantsFog = RenderCVars.FogQuality.Value > 0
            && RenderCVars.OffscreenScene.Value
            && RenderCVars.Deferred.Value;

        // The mission actually on screen, as the colour grade reads it. This read -mission's id, so a
        // mission entered from the campaign -- where that is nought -- was drawn in the generic
        // default haze instead of its own tuned fog, and looked different from the same mission
        // started any other way, a renderer switch included.
        int fogMission = _session.Mission?.Id ?? _menuBackdrop.MissionId ?? _options.StartMission;

        VolumetricFogSettings medium = VolumetricFogSettings.Off;
        if (wantsFog)
        {
            // The sky plane's height is an offset above the ground, not a world Z: a level's
            // coordinates carry a kilometres-large absolute offset, so it has to be anchored to
            // the scene's own ground before the march can compare a sample against it.
            medium = state.FogPresets.Resolve(
                fogMission,
                authoredDensity,
                (float)(_scene.GroundHeight + (_sky is { HasSky: true } ? _sky.PlaneHeight : 0f)),
                RenderCVars.FogQuality.Value);

            // A console override beats the authored preset, so a value can be dialled in live and
            // then written back into the file.
            //
            // "Overridden" is the variable having been moved off its own default, which is why
            // the graphics presets pin all four of these to their defaults on every tier: a
            // player choosing Ultra has not asked for one mission's authored atmosphere to be
            // replaced by a global number, and a preset that assigned one could not be told from
            // a player who had. Picking a preset therefore CLEARS an override and hands the
            // missions back their own air, which is the behaviour the settings page implies.
            // The haze is the colour the level says it is. Without this the medium scatters a
            // near-white droplet albedo lit by the sun, which for a night mission whose own fog is
            // a dark blue (0.00, 0.16, 0.29) is a pale daylight haze over a dark scene.
            if (_atmosphere is { } tint && tint.FogColour.LengthSquared > 0f)
            {
                medium = medium with { Albedo = tint.FogColour };
            }

            if (RenderCVars.FogDensity.Value > 0f)
            {
                medium = medium with { Density = RenderCVars.FogDensity.Value };
            }

            if (!RenderCVars.FogHeightFalloff.IsDefault)
            {
                medium = medium with { HeightFalloff = RenderCVars.FogHeightFalloff.Value };
            }

            if (!RenderCVars.FogAnisotropy.IsDefault)
            {
                medium = medium with { Anisotropy = RenderCVars.FogAnisotropy.Value };
            }

            if (!RenderCVars.FogDistance.IsDefault)
            {
                medium = medium with { Distance = RenderCVars.FogDistance.Value };
            }
        }

        PrepareSceneLighting(state, eye, light);

        // The hashed alpha test needs the temporal resolve to average its dither, so it follows the
        // temporal path: a per-frame seed above zero turns it on, zero leaves the hard cut-out. The
        // seed only has to change each frame for the dither to differ; the jitter counter does that.
        _device.SetAlphaHash(
            RenderCVars.HashedAlpha.Value && wantsTemporal
                ? 1f + (float)(state.TemporalFrame & 63)
                : 0f);

        // The derived detail is a deferred-path feature: it changes the normal and the roughness
        // written into the G-buffer, and without one there is nothing to write them into.
        //
        // r_pbr (F9) multiplies the authored strength by one or zero rather than replacing it, so
        // the maps can be taken out of the frame and put back without disturbing what the preset
        // chose. Read here, every frame, because that is what makes it instant: nothing is
        // unloaded and nothing is rebuilt -- the shader simply stops scaling its slope by it.
        _device.SetMaterialDetail(
            RenderCVars.OffscreenScene.Value
            && RenderCVars.Deferred.Value
            && RenderCVars.MaterialDetailEnabled.Value
                ? RenderCVars.MaterialDetail.Value
                : 0f);
        _device.SetFloorGloss(
            RenderCVars.OffscreenScene.Value && RenderCVars.Deferred.Value
                ? RenderCVars.FloorGloss.Value
                : 1f);
        _device.SetReflections(
            RenderCVars.OffscreenScene.Value && RenderCVars.Deferred.Value
                ? new ReflectionSettings(
                    RenderCVars.Reflections.Value,
                    RenderCVars.ReflectionIntensity.Value,
                    RenderCVars.ReflectionMaxRoughness.Value)
                : ReflectionSettings.Off);

        // How wet the ground is. Driven by the storm rather than by a number of its own: it is
        // raining or it is not, and a road that stays dry through a downpour is the single thing
        // that most gives away rain as an effect rather than as weather. Off the moment the storm
        // is not drawing, so a level with no rain is not quietly shaded damp.
        bool wetGround = RenderCVars.Wetness.Value
            && RenderCVars.OffscreenScene.Value
            && RenderCVars.Deferred.Value
            && _storm is { IsActive: true };
        _device.SetWetness(
            wetGround
                ? WetnessSettings.Storm.AtIntensity(
                    _storm!.Settings.Intensity,
                    _loop.TimeSource.Milliseconds / 1000f)
                : WetnessSettings.Dry);

        // Eased rather than linear. A linear ramp starts and stops abruptly, and the eye reads the
        // stop as a second, smaller event -- which is the thing this exists to remove.
        float probeFade = _baked.ProbeArrivedAt < 0
            ? 1f
            : Math.Clamp((_loop.TimeSource.Milliseconds - _baked.ProbeArrivedAt) / ProbeFadeMilliseconds, 0f, 1f);
        probeFade = probeFade * probeFade * (3f - (2f * probeFade));

        _device.SetIrradianceVolumeSettings(
            _baked.ProbeField is not null && RenderCVars.GlobalIllumination.Value
                ? new IrradianceVolumeSettings(
                    true,
                    RenderCVars.GlobalIlluminationIntensity.Value,
                    light.Ambient,
                    probeFade)
                : IrradianceVolumeSettings.Off);

        // Ambient occlusion needs the G-buffer's normals and depth, so it is offered only when
        // there is a G-buffer; without one the lighting has no ambient term of its own to shade.
        _device.SetAmbientOcclusion(
            RenderCVars.OffscreenScene.Value && RenderCVars.Deferred.Value
                ? new AmbientOcclusionSettings(
                    RenderCVars.AmbientOcclusion.Value,
                    RenderCVars.AmbientOcclusionRadius.Value,
                    RenderCVars.AmbientOcclusionIntensity.Value,
                    RenderCVars.ScreenSpaceGi.Value,
                    RenderCVars.ScreenSpaceGiIntensity.Value)
                : AmbientOcclusionSettings.Off);

        // Reprojection compares two frames, and the jitter is precisely what differs between them,
        // so the matrix handed over is the one without it.
        _device.SetTemporalAntiAliasing(
            new TemporalAaSettings(
                wantsTemporal,
                RenderCVars.TemporalFeedback.Value,
                jitterX,
                jitterY,
                RenderCVars.TemporalSharpening.Value),
            unjitteredProjection,
            eye.Position,
            eye.Orientation.Transposed());

        _device.SetVolumetricFog(
            medium,
            new Vector3f((float)eye.Position.X, (float)eye.Position.Y, (float)eye.Position.Z),
            wantsFog ? state.FogPresets.VolumesFor(fogMission) : ReadOnlySpan<LocalFogVolume>.Empty);

        // Filled BEFORE the scene capture opens: a cascade renders into its own framebuffer, and
        // beginning a capture first would leave the shell holding two render targets at once.
        //
        // And not filled at all on a frame where nothing can read it: with the traced sun the
        // ray mask is the ONLY sun shadow the lighting samples, and with the sun faded to
        // nothing the sun term is nought before the shadow is asked -- either way the cascades
        // are written and never read, raster spent on a map nobody looks at. The maps and their
        // matrices stay allocated and uploaded, so the first frame that needs them again (the
        // r_rt_shadows toggle, or a sunrise scrub) refits and refills BEFORE that frame's
        // lighting reads -- no stall and no frame of wrong shadows.
        bool cascadesRead = shadowSettings.SunLit && !_device.SunShadowTraced;
        RenderShadowMaps(state, cascadeCount, cascadesRead, eye, casterBodies);

        bool captured = OpenSceneCapture(sceneTarget, light, viewSunDirection, mapScene, resolution);

        // Whether the eye is under a roof, which the weather and the HUD read. A title's own screen over
        // the view (I.G.I. 2's map computer turns the rain off) keeps the weather off as a building's
        // shell does.
        bool cameraIndoors = IsCameraInsideBuilding(eye.Position) || TitlePlayer?.IsWeatherHidden == true;

        DrawSceneGeometry(mapScene, captured, eye, projection, verticalFieldOfView, aspectRatio, cascadesRead, cascadeCount, smoothPresentation, tickBlend);

        // The remastered-optics decisions and the binocular detector are resolved here, before
        // the soldiers are drawn, for two reasons: the detector is stateful and must be scanned
        // exactly once per frame, and the through-wall glow pass below has to mark the very
        // contacts it finds while the scene is still being captured. The HUD after the resolve
        // reuses this same target list rather than advancing the detector a second time.
        bool remasterHud = RemasteredHudPresentation.UsesRemasteredHud(RenderCVars.RemasteredHud.Value);
        bool remasteredOptics = RemasteredHudPresentation.UsesRemasteredOptics(
            TacticalComputer is not null,
            _remasteredHud is { IsReady: true });
        bool tacticalMessages = RemasteredHudPresentation.UsesTacticalMessages(
            remasterHud,
            RenderCVars.TacticalMessages.Value);
        bool showPlayerHud = _playerMode && !_computerOpen && !_entry.IsCameraActive
            && _mission.Cutscenes?.IsPlaying != true
            && Viewpoint?.IsDeathViewActive != true
            && Viewpoint?.HasPlayerHud == true;

        // On a stand retail draws no HUD at all -- captured in mission 4, the stance figure shown
        // beside the gun is gone the moment its sight takes the view -- hence the last condition.
        bool showFirstPersonHud = showPlayerHud
            && Viewpoint?.HasFirstPersonHud == true;

        // The arms and the held weapon hang off the eye, so the moment the camera leaves it they
        // are either filling the frame or behind it. When they stand down, and when they come
        // back, is the camera's own call -- see ThirdPersonCamera.ShowsViewModel.
        bool showViewModel = showFirstPersonHud && Viewpoint?.ShowsViewModel != false;

        // What the interaction resolver is offering, advanced on its own wall clock so the
        // marker fades at the same rate whatever the frame rate. Gated on showPlayerHud rather
        // than showFirstPersonHud on purpose: a timed interaction hides the hands and the
        // weapon, and that is exactly when the ring filling on the object is what the player
        // is reading.
        bool worldInteractionPrompt = InteractionPromptPresentation.UsesWorldPrompt(
            remasterHud,
            RenderCVars.InteractionPrompt.Value);
        int interactionPromptNow = _loop.TimeSource.Milliseconds;
        float interactionPromptElapsed =
            Math.Clamp((interactionPromptNow - _lastInteractionPromptMilliseconds) / 1000f, 0f, 0.1f);
        _lastInteractionPromptMilliseconds = interactionPromptNow;
        _interactionPrompt.Update(
            worldInteractionPrompt && showPlayerHud
                ? Viewpoint?.InteractionPrompt(smoothPresentation ? tickBlend : 1f)
                : null,
            interactionPromptElapsed);

        // The remastered detector's range grows with the zoom: from the base range at the
        // default field of view up to the zoomed range at the tightest magnification, so leaning
        // into the optics resolves a distant figure the wide view does not. The vanilla optics
        // stay retail-uncapped (positive infinity).
        float binocularZoomIn = Viewpoint?.BinocularZoomIn(sight.TanX) ?? 0f;
        float binocularDetectionRange = remasteredOptics
            ? RenderCVars.BinocularRange.Value
                + ((RenderCVars.BinocularRangeZoomed.Value - RenderCVars.BinocularRange.Value) * binocularZoomIn)
            : float.PositiveInfinity;
        IReadOnlyList<EnemyOverlayBox> binocularTargets =
            showFirstPersonHud
            && Viewpoint?.IsBinocularsOpen == true
            && _soldiers is { } binocularSoldiers
                ? binocularSoldiers.BinocularTargets(
                    // Use the same presented eye that rendered the world. The detector owns
                    // authoritative slot lifetime, but its screen rectangle must be projected
                    // through the frame's smoothed camera or moving/zooming binoculars make the
                    // box visibly lag behind the soldier it is meant to surround.
                    eye,
                    sight.TanX,
                    _window.Width,
                    _window.Height,
                    // FUN_00471F60/FUN_00471B60 use Sys_GetTickCount (the retail
                    // millisecond counter), not the 30 Hz simulation index. Passing
                    // TickCount here stretched the native 250 ms entry pulse and
                    // 3000 ms retention window by a factor of thirty-three.
                    _loop.Clock.Now,
                    binocularDetectionRange,
                    _loop.Clock.TickCount)
                : [];

        DrawCharacters(eye, projection, tickBlend, thirdPersonBody);

        LightScene(state, eye, captured, sceneTarget, unjitteredProjection);

        SubmitLocalLights(state, captured, eye, unjitteredProjection, flashlightShown, flashlightBody, mapScene, sceneTarget);

        DrawForwardPass(console, captured, eye, projection, mapScene, resolution, sceneTarget, smoothPresentation, tickBlend, cameraIndoors, remasteredOptics);

        float? binocularRangeMetres = DrawFirstPersonView(eye, showViewModel, sight, smoothPresentation, tickBlend);

        // L6a slice 2: gather screen-space indirect from the surface cache and dump it, once, while
        // the G-buffer is still live inside the capture. Verifies the cross-layer bridge and the
        // depth-to-world reconstruction before the gather feeds the frame. Only when no card probe is
        // running, since that fills the cache atlas with a diagnostic rather than light.
        if (captured
            && !state.ScreenGiDumped
            && RenderCVars.SurfaceCacheDump.Value is { Length: > 0 } giInto
            && _gi.DistanceField is { } giField
            && _gi.CardLighting is { } giCards
            && RenderCVars.SurfaceCacheProbe.Value == 0)
        {
            state.ScreenGiDumped = true;
            SurfaceCacheDebugDumps.ScreenGather(
                _device, giCards, giField, eye, unjitteredProjection, FarPlane,
                _window.Width, _window.Height, giInto, _logger);
        }

        DrawScreenOverlays(state, captured, postProcess, remasterHud, remasteredOptics, showFirstPersonHud, binocularTargets, binocularRangeMetres, tacticalMessages, showPlayerHud, smoothPresentation, tickBlend, sight, worldInteractionPrompt, eye, horizontalTangent, aspectRatio, cameraIndoors);
    }

    /// <summary>Moves the free camera along the scripted flight a capture asked for, by the rendered-frame index.</summary>
    private void StepScriptedFlight(FrameLoopState state)
    {
        Debug.Assert(_camera is not null, "Flown only while a level is up; see RenderWorld.");


        // The scripted flight, driven off the rendered-frame index so the motion is exactly
        // reproducible under lockstep. The opening pose is captured once; each subsequent frame
        // moves the camera a straight fraction of the way to the end pose. An unset end component
        // holds the opening value, so -camend alone dollies without turning and -yawend alone pans
        // in place. Only in the free camera -- a flight forces -freecam, so _playerMode is false.
        if (_options.HasFlight && !_playerMode)
        {
            if (!state.FlightStarted)
            {
                state.FlightStartPos = _camera.Position;
                state.FlightStartYaw = _camera.Yaw;
                state.FlightStartPitch = _camera.Pitch;
                state.FlightStarted = true;
            }

            // -flightframes shortens the move without shortening the capture, so the frames past it
            // hold the end pose and the flight comes to a stop. The clamp is what holds it.
            int span = Math.Max(
                1,
                (_options.FlightFrames
                ?? (_options.IsBenchmark ? BenchmarkSchedule.MeasurementEndFrame : _options.ExitAfterFrames)) - 1);
            float t = Math.Clamp((float)(state.FlightFrame - _options.FlightDelay) / span, 0f, 1f);

            Vector3d endPos = _options.CameraEnd is { } e
                ? new Vector3d(e.X, e.Y, e.Z)
                : state.FlightStartPos;
            float endYaw = _options.CameraYawEnd ?? state.FlightStartYaw;
            float endPitch = _options.CameraPitchEnd ?? state.FlightStartPitch;

            (float shakeYaw, float shakePitch) = HandheldShake(state.FlightFrame, _options.CameraShake);
            if ((state.FlightFrame & 1) == 1)
            {
                shakeYaw += _options.CameraSwing * (MathF.PI / 180f);
            }

            _camera.MoveTo(
                new Vector3d(
                    state.FlightStartPos.X + ((endPos.X - state.FlightStartPos.X) * t),
                    state.FlightStartPos.Y + ((endPos.Y - state.FlightStartPos.Y) * t),
                    state.FlightStartPos.Z + ((endPos.Z - state.FlightStartPos.Z) * t)),
                state.FlightStartYaw + ((endYaw - state.FlightStartYaw) * t) + shakeYaw,
                state.FlightStartPitch + ((endPitch - state.FlightStartPitch) * t) + shakePitch);
        }
    }

    /// <summary>Hands the device this frame's indirect light: the probe field, the sky's fill, the surface cache and the distance field.</summary>
    private void PrepareSceneLighting(FrameLoopState state, Camera3D eye, DirectionalLightSettings light)
    {
        // The bake stored how much sky reaches each probe, never what colour it was, so the
        // mission's own sky is applied here -- and a level whose sun sits below the horizon leans
        // on its ambient exactly as the fog does.
        // The clipmaps follow the camera. Each level is rebuilt only when the camera has left it,
        // which for the outer levels is almost never -- I.G.I.'s geometry is static, so a clipmap
        // only becomes wrong by being walked out of.
        if (_gi.DistanceField is { } liveField)
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
            if (!_gi.FieldValidated && RenderCVars.DistanceFieldValidate.Value && _gi.DistanceFieldScene is { } exact)
            {
                _gi.FieldValidated = true;
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
                && _gi.CardBounceFrame >= 8;

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
            _gi.SunMovedFrames = giSunMoved ? 0 : Math.Min(_gi.SunMovedFrames + 1, GlobalIllumination.GiSunFollowFrames + 1);
            bool followingSun = _gi.SunMovedFrames <= GlobalIllumination.GiSunFollowFrames;
            bool retraceGi = _giStaticFrames <= giSettle;

            if (_gi.CardLighting is { } litCards)
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
                        SkyFillColour(),
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
            if (_gi.CardLighting is { } bounceCards && RenderCVars.SurfaceCacheProbe.Value == 0 && retraceGi)
            {
                bounceCards.Bounce(
                    liveField,
                    boost: RenderCVars.GlobalIlluminationBoost.Value,
                    temporalIndex: wantDump ? 0 : _gi.CardBounceFrame);
                _gi.CardBounceFrame++;

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
                && _gi.CardLighting is { } cacheCards
                && _baked.ProbeField is { } cacheGrid)
            {
                cacheCards.ConfigureRadianceCache(
                    new Vector3d(cacheGrid.Origin.X, cacheGrid.Origin.Y, cacheGrid.Origin.Z),
                    cacheGrid.Spacing,
                    cacheGrid.CountX,
                    cacheGrid.CountY,
                    cacheGrid.CountZ);

                cacheCards.UpdateRadianceCache(
                    liveField,
                    SkyFillColour(),
                    // A short running average while the level is warming up, so the cache climbs to
                    // its converged value in a handful of frames rather than easing there over a
                    // couple of seconds -- which is what read as the scene loading bright and dimming.
                    // A long one after, where a slow average is what keeps the steady field from
                    // shimmering under the per-sweep ray jitter.
                    maxFrames: _gi.ExposureWarmup > 0 ? 6 : 32,
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

    /// <summary>Fills the sun's cascades and the nearest lamps' shadow maps, before the scene capture opens.</summary>
    private void RenderShadowMaps(FrameLoopState state, int cascadeCount, bool cascadesRead, Camera3D eye, ICharacterPresenter? casterBodies)
    {
        BeginStage();
        if (cascadeCount > 0 && _shadowPass is not null && cascadesRead)
        {
            // The cascades are filled through the title's presentation, not through this
            // host's renderer fields: the pass draws whichever game is loaded.
            _shadowPass.Render(state.CascadeStorage, cascadeCount, eye, _scene, _world, casterBodies, _terrain);
        }

        // The nearest lamps' own shadow maps, where the device marches lamp shadows rather than
        // tracing them. Whatever the sun is doing: these are the night's shadows.
        if (_shadowPass is not null && _device.WantsLampShadowMaps)
        {
            _lampMaps.Render(_shadowPass, _scene, _world, casterBodies, _gi.LightFixtures, LampMapPeople);
            _lampMaps.RenderBounce(_shadowPass, _scene, _world, _gi.CardLighting, _gi.LightFixtures, LampOrderEye);
        }
        EndStageIfSlow("the shadow cascades");
    }

    /// <summary>Opens the offscreen scene and sets its lighting, or draws straight to the back buffer where there is none.</summary>
    /// <returns><see langword="true"/> when the scene is being drawn offscreen.</returns>
    private bool OpenSceneCapture(
        SceneTargetDescription sceneTarget, DirectionalLightSettings light, Vector3f viewSunDirection, MapSceneView? mapScene,
        RenderResolution resolution)
    {
        bool captured = RenderCVars.OffscreenScene.Value && _device.BeginSceneCapture(sceneTarget);
        if (captured)
        {
            // The scheduler cleared the back buffer; the target it did not know about needs it too.
            _device.Clear(_scheduler.ClearColour);

            // The live sun, calibrated against the independently-normalised sky by r_sun_scale.
            // The baked model ignores it (it never adds a live sun), so the scale is a no-op there.
            Vector3f scaledSun = light.Colour * RenderCVars.SunScale.Value;

            // The atmospheric sky keeps its own source illuminance. Sharing scaledSun here made
            // r_sun_scale dim the sky SH together with the direct term, so their ratio could never
            // be corrected. Ultra's atmosphere remains at its already-measured source intensity.
            //
            // Under the moon the feed is knocked down hard rather than handed over at face
            // value: the atmosphere scatters whatever illuminance it is given, and the moon's
            // few thousandths of a sun still rendered as a dim but unmistakably blue daytime
            // sky where a night sky reads near black. The surfaces keep the full moonlight; the
            // sky keeps only a trace of it.
            // The same knock-down, for the other way a scene can be at night. The moon case
            // above exists because the atmosphere scatters whatever illuminance it is given and a
            // trace of moonlight still came out as a dim blue DAYTIME sky. I.G.I. 2 reaches the
            // same place by a different road: its night missions leave the sun up and paint it
            // nearly black, so the model -- which only knows night as a sun below the horizon --
            // has no way to tell. Told by the level instead, and taken to the same floor.
            //
            // Not for a level bound to the cycle by its night end. Its night IS the moon above, at
            // the hour it is bound to, so the level's constant adds nothing there -- and away from
            // that hour it is wrong: scrubbed up to noon, a constant night held the sky at the
            // floor under a risen sun.
            //
            // How far down is r_night_sky's to say. Two percent kept the moonlit sky from reading
            // as a dim day, and left it and the fill projected from it black; with the resolve's
            // night shift doing the job of making it read as night, a third lets the sky light the
            // moon's shadows again. See RenderCVars.NightSkyScale.
            float nightSky = RenderCVars.NightSkyScale.Value;
            float nightFeed = _deferredLighting is { Night: null } nightly
                ? 1f + ((nightSky - 1f) * Math.Clamp(nightly.Nightfall, 0f, 1f))
                : 1f;
            //
            // Under the day cycle the sky is handed the sun and the moon as two lights, not
            // whichever of them holds the direct slot. The sun comes untinted and from where it
            // really is, under the horizon too: the march reddens it, and the planet's shadow
            // puts it out a little at a time, which is the twilight. The moon comes as its own
            // light, at the knocked-down feed above, from the moment it rises. Handing the sky
            // only the direct light made it scatter a sun already reddened and dimmed -- a
            // second reddening, the orange-brown dusk -- and then, as the direct light passed to
            // the moon, a sun that had gone and a moon not yet up: a black sky and a black fill
            // for the half hour between.
            float atmosphereScale = RenderCVars.AtmosphereSunScale.Value;
            Vector3f atmosphereSun;
            Vector3f? atmosphereSunDirection = null;
            Vector3f atmosphereMoon = default;
            if (_options.SunDirection is null)
            {
                atmosphereSun = _dayNight.SkySunColour * (atmosphereScale * nightFeed);
                atmosphereSunDirection = _dayNight.SunDirection;
                // The moon above the weather, whole: the sky's cloud deck is lit by all of it and
                // passes on its own share (see SkyView's deck transmission), while the clear air
                // scatters only r_night_sky's. Knocked down or veiled here as well, the deck took
                // the cloud's toll twice and a rainy night came out black.
                atmosphereMoon = _dayNight.MoonColour * (RenderCVars.Moonlight.Value * atmosphereScale);
            }
            else
            {
                atmosphereSun = light.Colour * (atmosphereScale * nightFeed);
            }

            // TEMPORARY (r_ambient_scale): the dynamic model fills every surface with this one flat
            // ambient, so a sealed room reads as lit as open ground. Scaling it here -- towards 0 --
            // is how that stand-in is dialled down or removed to confirm what lights enclosed spaces.
            Vector3f scaledAmbient = light.Ambient * RenderCVars.AmbientScale.Value;

            _dumpWorldSun = light.Direction;
            _device.SetLighting(_dumpLighting = new SceneLighting(
                _gi.EffectiveLighting,
                viewSunDirection,
                scaledSun,
                scaledAmbient,
                RenderCVars.LightScale.Value,

                // Whether the level's bakes already carry its direct light, so the hybrid model
                // must not add a sun over them. See DeferredLighting.CompleteBake.
                _deferredLighting?.CompleteBake ?? false,

                // The fog scatters the level's OWN ambient, not the r_sky_light-boosted surface
                // fill: the fog was calibrated against this value and boosting it whites the frame
                // out. Kept independent of r_ambient_scale so the haze does not collapse when the
                // flat ambient is dialled to zero to watch the GI carry the surfaces.
                light.Ambient,

                // How brightly the sky lights the world, for a backend that models the sky as a
                // real light source rather than folding it into the flat ambient. Same dial that
                // scaled the flat stand-in, now driving the thing it stood in for.
                RenderCVars.SkyLight.Value,

                // The source that builds both the visible atmosphere and its diffuse SH. Separate
                // from scaledSun so direct-light calibration does not move the sky with it.
                atmosphereSun,

                // What the night sky holds beyond the air: the glow that lights a moonless night,
                // the cloud cover, the stars and the moon's disc. Nothing by day.
                FrameNightSky(),
                atmosphereSunDirection,
                atmosphereMoon,
                nightSky));
            _device.BeginPass(ScenePass.Geometry);

            // The sky is geometry as far as the G-buffer is concerned, so without this the dynamic
            // model would multiply the dome by an ambient term and the horizon would go dark. It
            // is not lit; it IS light.
            _device.SetMaterialOverride(SurfaceMaterial.Sky);
        }
        else
        {
            // Vanilla draws opaque geometry directly to the back buffer, so it has no backend
            // BeginPass bracket. Give that path the same complete GPU timing as deferred geometry.
            _device.BeginStageTiming(RenderStage.Geometry);
        }

        if (mapScene is { } insetMap)
        {
            // The render size over the window's, per axis: an NVIDIA reconstruction's render size is the
            // runtime's, not the render scale's, and need not keep the window's aspect to the pixel.
            float scaleX = captured ? resolution.RenderWidth / (float)Math.Max(1, _window.Width) : 1f;
            float scaleY = captured ? resolution.RenderHeight / (float)Math.Max(1, _window.Height) : 1f;
            _device.SetViewport(new Viewport(
                (int)(insetMap.Viewport.X * scaleX),
                (int)((_window.Height - insetMap.Viewport.Y - insetMap.Viewport.Height) * scaleY),
                Math.Max(1, (int)(insetMap.Viewport.Width * scaleX)),
                Math.Max(1, (int)(insetMap.Viewport.Height * scaleY))));
        }
        return captured;
    }

    /// <summary>Draws the level's opaque geometry: the sky, the world's objects, the ground, the foliage and the props in flight.</summary>
    private void DrawSceneGeometry(
        MapSceneView? mapScene, bool captured, Camera3D eye, Matrix4f projection, float verticalFieldOfView, float aspectRatio, bool cascadesRead,
        int cascadeCount, bool smoothPresentation, float tickBlend)
    {
        Debug.Assert(_scene is not null && _world is not null, "Drawn only while a level is up; see RenderWorld.");


        // The SIMOTECH map is the live terrain/object pass, not a view of the sky dome from
        // directly above. The green CRT wash is applied in the 2D computer pass below.
        //
        // The exception is the tactical transition. Its climb starts at eye height with the
        // horizon across the middle of the frame and only reaches straight down at the top, so
        // dropping the sky for the whole of it would leave the first half of the shot rising out
        // of a black void.
        // The level's own distance fog, which the whole world below is drawn into, and its flat
        // model light. Set before the sky, because the sky renderer puts back whatever it finds
        // here after drawing its unfogged dome. The modern tiers leave the fog off and march the
        // same authored haze through the froxel volume instead, and their models are lit by the
        // G-buffer pass rather than by this one.
        bool retailFog = RenderCVars.RetailFog.Value;
        _device.SetFog(retailFog ? RetailAir(_globalLight.ObjectFog) : SceneFog.None);
        _device.SetModelAmbient(
            _gi.EffectiveLighting == LightingModel.Baked
                ? _globalLight.ObjectAmbient
                : GlobalLightSettings.DefaultAmbient);

        if (mapScene is null || _computerFlight.IsFlying)
        {
            // Warm the sky dome towards the horizon as the sun sinks, so a window shows a golden dusk
            // sky rather than a flat blue one and the screen-space bounce off it carries that warmth
            // into the room instead of a cool tint. Untouched with the sun high.
            _sky?.SetSunTint(SkyWarmthTint((_effectiveSun ?? _sun)?.Direction.Z ?? 1f));

            // Kept rather than replaced by the atmosphere, and divided by the exposure on the way
            // in because what it carries is a picture. Only where there IS a resolve to survive:
            // vanilla draws straight to the back buffer.
            //
            // A BRACKET, closed below. The division is the sky's own, and everything drawn after
            // this is the world: leaving it open divided every emissive surface in the level --
            // every lamp -- by the exposure too.
            bool paintedSky = captured && _sky is { IsGameOwnSky: true };
            if (paintedSky)
            {
                _device.SetPaintedSky(true);
            }

            _sky?.Draw(new SkyFrame(eye, projection, _loop.TimeSource.Milliseconds / 1000f));

            if (paintedSky)
            {
                _device.SetPaintedSky(false);
            }
        }

        if (captured)
        {
            _device.SetMaterialOverride(SurfaceMaterial.Default);
        }

        // The world draws BEFORE the terrain, deliberately: buildings are the pass's best
        // occluders. Indoors the frustum still holds the whole outdoors, and with the ground
        // drawn first every one of those terrain fragments was shaded and then buried under
        // the walls; walls first, and early-Z rejects that fill instead. Outdoors the same
        // holds for the ground behind every building.
        //
        // This is the one place the frame knowingly differs from I.G.I. 2's, which puts terrain
        // in queue 2 and rigid objects in 5 and so draws the ground first. The picture is the
        // same either way -- both passes are opaque and depth-tested, so the order decides only
        // which fragments are shaded and then thrown away -- and the port's way throws away
        // fewer. Where the order does decide the picture, the render group is honoured: the sky
        // is drawn first, the water last, and the world's own objects come out grouped, which is
        // what BuildDrawOrder is for.
        _world.Draw(_scene, eye, projection, new WorldDrawOptions(OcclusionCulling: RenderCVars.WorldOcclusion.Value));
        EndStageIfSlow("the sky and the world's objects");

        if (captured)
        {
            // The world leaves its last sub-mesh's surface material in force; the terrain
            // must not wear it.
            _device.SetMaterialOverride(SurfaceMaterial.Default);
        }

        // The ground is drawn through its own band of the same keyframe. Retail keeps two --
        // "terrain" and "object category 1" -- and publishes them to different globals; every
        // shipping level happens to author them alike, which is not a reason to draw them alike.
        if (retailFog)
        {
            _device.SetFog(RetailAir(_globalLight.TerrainFog));
        }

        _terrain?.Draw(new TerrainFrame(
            eye,
            projection,
            verticalFieldOfView,
            aspectRatio,

            // -viewdist still wins when it is given, so an existing diagnostic command line keeps
            // meaning what it did; otherwise the setting decides.
            _options.TerrainViewDistance > 0 ? _options.TerrainViewDistance : RenderCVars.TerrainDetail.Value,
            GraphicsPresetRegistry.Default.Current != GraphicsPreset.Vanilla,
            RenderCVars.TerrainOcclusion.Value,

            // The ground casts into the cascades through the same caster the traced sun reads,
            // built here for the next frame's cascades.
            CastsIntoCascades: cascadesRead && cascadeCount > 0));
        EndStageIfSlow("the terrain");

        if (retailFog)
        {
            _device.SetFog(RetailAir(_globalLight.ObjectFog));
        }

        // The foliage after the ground it is planted in, so a tree's trunk depth-tests against
        // the hillside rather than against whatever was drawn there before it. Half-tangents
        // rather than the angle, because the cull's planes are built straight out of them.
        _forest?.Draw(new ForestFrame(
            eye,
            MathF.Tan(verticalFieldOfView * 0.5f) * aspectRatio,
            MathF.Tan(verticalFieldOfView * 0.5f)));
        EndStageIfSlow("the forest");

        // With the world and the terrain both in the depth buffer, the world's occlusion
        // queries can be answered honestly: walls hide the outdoors, and hills hide buildings.
        _world.IssueOcclusionQueries(eye);

        // Dropped guns, grenades and rockets move on their own: their draws stamp their motion,
        // or the temporal resolve trails them as it trailed every vehicle.
        // The title's own models lying in its level, at rest among the world's geometry.
        DrawTitleWorldModels(eye, projection);

        _device.SetObjectMotion(true);
        _gunPickupRenderer?.Draw(eye, projection);

        // The grenades, rockets and launcher rounds in the air, solid geometry among the world's own.
        WorldView?.DrawProjectiles(_grenadeRenderer, _missileRenderer, eye, projection, smoothPresentation ? tickBlend : 1f);

        _device.SetObjectMotion(false);
    }

    /// <summary>Draws the soldiers, the tasks' own presenters, the player's fallen body and what the characters hold, then the marks rounds left.</summary>
    private void DrawCharacters(Camera3D eye, Matrix4f projection, float tickBlend, bool thirdPersonBody)
    {
        Debug.Assert(_scene is not null && _world is not null, "Drawn only while a level is up; see RenderWorld.");


        // Soldiers draw under the same world projection: 0x4D9620 leaves +212 at 1, so 0x49F700
        // never swaps in the view-model block for them (player.md section 15.3).
        _soldiers?.ObserveView(eye, projection);
        _soldiers?.Draw(eye, projection);

        // Whatever the title, or a mod, draws for particular tasks of the level (PresenterRegistry.ForTask).
        foreach (ITaskPresenter taskPresenter in _taskPresenters)
        {
            taskPresenter.Draw(eye, projection, tickBlend);
        }

        // The player's own body, once it has fallen -- drawn only under the death camera, so it never
        // appears in front of the first-person view during normal play.
        // ...and, now, whenever the third-person camera has cleared the character's head, which
        // was settled with the camera above so the shadow cascades and this pass agree.
        _soldiers?.DrawPlayerBody(eye, projection);

        // What the character is holding. The first-person rig is not drawn at all from out here,
        // so its weapon has to be hung off the body's own hand -- bone 22, where a guard's rifle
        // rides -- under the WORLD projection rather than the view model's magnified one. Body
        // only, as a soldier's is: the muzzle flame is light, and the deferred pass would shade it
        // like scenery.
        if (thirdPersonBody
            && _soldiers is { } handBody
            && _heldWeapon is { IsReady: true } thirdPersonGun
            && WorldView?.TryHeldWeapon(out int heldShotSequence) == true)
        {
            // The cycle changes the model as well as the animation, and out here nothing else is
            // watching for that: the first-person block that normally does is not running.
            if (_loadedMissionForWeapon is { } missionForBody)
            {
                FirstPersonView?.FollowHeldWeapon(_heldWeapon, missionForBody);
            }

            if (handBody.PlayerBodyHandAttachment() is { } handAttachment)
            {
                thirdPersonGun.Draw(
                    handAttachment,
                    eye,
                    projection,
                    firedThisTick: false,
                    heldShotSequence,
                    WeaponDrawParts.BodyOnly);
            }
        }

        EndStageIfSlow("the soldiers");

        // The marks rounds have left, over the world and under the first-person rig.
        if (_marks is not null && RenderCVars.BulletMarks.Value)
        {
            WorldView?.DrawMarks(_marks, eye, projection, _world.DrawPoses);
        }
    }

    /// <summary>Gathers the screen-space indirect light and resolves the deferred lighting.</summary>
    private void LightScene(FrameLoopState state, Camera3D eye, bool captured, SceneTargetDescription sceneTarget, Matrix4f unjitteredProjection)
    {
        // L6/L9: the live screen probe gather. Traced here, while the G-buffer the geometry pass just
        // filled is still live and BEFORE the pass closes -- because closing it runs the deferred
        // lighting, which reads the atlas as its diffuse fill. So the gather has to have written it
        // first. Only without a card probe, which fills the cache atlas with a diagnostic rather than
        // light. It replaces the ambient inside the shade rather than compositing over the frame, so
        // there is no ambient to zero and nothing runs after the lighting.
        const int giStep = 8;
        UpdateTerrainRaster();
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
            _gi.CardLighting is { } reflectionCards && RenderCVars.SurfaceCacheProbe.Value == 0
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
        _device.SetDistanceField(DescribeDistanceField(eye.Position));

        float giStrength = RenderCVars.ScreenGather.Value;
        bool giActive = captured
            && giStrength > 0f
            && _gi.DistanceField is not null
            && _gi.CardLighting is not null
            && RenderCVars.SurfaceCacheProbe.Value == 0;

        // Every placement that moves, carried in the surface cache to where it was drawn this frame,
        // before anything below looks a surface up -- a truck the camera rides in reads its own
        // interior rather than the yard it drove out of. See SurfaceCacheLighting.MovePlacement.
        if (_gi.CardLighting is { } movingCards && _world is { } movingWorld && movingWorld.Movers.Count > 0)
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
        if (giActive && _device.RawLighting && _gi.DistanceField is { } pixelField && _gi.CardLighting is { } pixelCards)
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
                DepthUnprojection.From(unjitteredProjection, NearPlane, FarPlane),
                eye.Orientation.Transposed(),
                pixelCameraToCache,
                sceneTarget.Width,
                sceneTarget.Height,
                SkyFillColour(),
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

        if (!giPixelsRan && giActive && _gi.DistanceField is { } liveGiField && _gi.CardLighting is { } liveGiCards)
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
            DepthUnprojection giUnprojection = DepthUnprojection.From(unjitteredProjection, NearPlane, FarPlane);
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
                    DepthUnprojection.From(unjitteredProjection, NearPlane, FarPlane),
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
                    SkyFillColour(),

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
                DepthUnprojection.From(unjitteredProjection, NearPlane, FarPlane),
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

    /// <summary>Submits the level's lamps and the player's flashlight as point lights for the deferred pass.</summary>
    private void SubmitLocalLights(
        FrameLoopState state, bool captured, Camera3D eye, Matrix4f unjitteredProjection, bool flashlightShown, Camera3D flashlightBody,
        MapSceneView? mapScene, SceneTargetDescription sceneTarget)
    {
        // The level's lamp fixtures, submitted as real point lights for the deferred pass that
        // closes just below -- the same timing as the screen-space fill. Transformed into this
        // frame's view space here, in double precision: the level sits at large world coordinates,
        // so the camera-relative subtract is done before the cast to float. Only under a shading
        // model and only when the scale asks for them; a windowless interior the gather cannot
        // reach is exactly what these light. The shader also guards on the model and the count.
        float localLightScale = RenderCVars.LocalLightScale.Value;
        float localLightRange = RenderCVars.LocalLightRange.Value;
        _stormLampCount = 0;
        if (captured
            && localLightScale > 0f
            && _gi.EffectiveLighting != LightingModel.Baked
            && _gi.LightFixtures.Count > 0)
        {
            Matrix3f worldToView = eye.Orientation.Transposed();

            // WHICH lamps, before how they light. A level carries well over a hundred fixtures and
            // the shader's arrays hold thirty-two, so the set has to be chosen every frame rather
            // than taken off the top of a list -- the first thirty-two are the level's brightest,
            // which is a fact about the whole level and says nothing about where the player is
            // standing. Nearest to the camera, measured against each fixture's own reach so that a
            // long tube is not passed over for a desk lamp a metre closer.
            int localCount = SelectNearestLamps(eye.Position, localLightRange);

            for (int i = 0; i < localCount; i++)
            {
                LightFixture fixture = _gi.LightFixtures[_localLightOrder[i]];
                Vector3f cameraRelative = new(
                    (float)(fixture.Position.X - eye.Position.X),
                    (float)(fixture.Position.Y - eye.Position.Y),
                    (float)(fixture.Position.Z - eye.Position.Z));

                // Clamp the fixture's relative intensity before scaling. Intensity is area x
                // luminance, so a long fluorescent tube (area ~9) would outpower a desk lamp
                // (area ~0.5) sixteen to one and blow the room out around it -- but a bigger lamp
                // is not sixteen times brighter, only larger. The clamp flattens that to something
                // like real fixtures; the scale cvar then sets the overall level.
                //
                // Then by how much of that the lamp is actually giving out, which is one for
                // everything nobody has switched. The emission on the fixture's own geometry is
                // scaled by the same number in the world renderer, so a lamp fading out fades as
                // one thing rather than as a panel and a pool of light going at their own rates.
                int lampIndex = _localLightOrder[i];
                float output = _gi.LampSwitchboard?.LevelOf(lampIndex) ?? 1f;

                // Clamp the fixture's relative intensity before scaling. Intensity is area x
                // luminance, so a long fluorescent tube (area ~9) would outpower a desk lamp
                // (area ~0.5) sixteen to one and blow the room out around it -- but a bigger lamp
                // is not sixteen times brighter, only larger. The clamp flattens that to something
                // like real fixtures; the scale cvar then sets the overall level.
                float intensity = MathF.Min(fixture.Intensity, 1.5f) * localLightScale * output;

                // A cooling filament is a colder black body and therefore a redder one, which is
                // the last thing anybody sees of a light going out. White whenever the lamp is
                // not on its way out.
                Vector3f colour = fixture.Colour;

                if (_gi.LampSwitchboard?.TintOf(lampIndex) is { } tint && tint != Vector3f.One)
                {
                    colour = new Vector3f(colour.X * tint.X, colour.Y * tint.Y, colour.Z * tint.Z);
                }

                // The way the fixture faces, rotated into the same space its position is in. A
                // rotation and not a transform: this is a direction, so the camera's translation
                // must not touch it. Zero stays zero, which the shader reads as a point light.
                Vector3f facing = fixture.IsDirectional
                    ? worldToView.Transform(fixture.Direction)
                    : default;

                _stormLamps[i] = new StormLamp(
                    cameraRelative,
                    fixture.Radius * localLightRange,
                    colour * intensity,
                    fixture.IsDirectional ? fixture.Direction : default);

                _localLightScratch[i] = new DeferredLocalLight(
                    worldToView.Transform(cameraRelative),
                    fixture.Radius * localLightRange,
                    colour,
                    intensity,
                    facing,

                    // The fixture's own size, which is what gives its shadow a penumbra. Floored
                    // rather than taken raw: a fixture the extractor measured as a few
                    // centimetres across would cast an edge as hard as a point light's, and no
                    // real fitting is that small.
                    MathF.Max(fixture.Extent, 0.05f),

                    // The rectangle the fitting emits from, where its shadow rays are aimed.
                    fixture.HasRect ? worldToView.Transform(fixture.Axis) * fixture.HalfLength : default,
                    fixture.HasRect ? fixture.HalfWidth : 0f);
            }

            // Both backends, through the seam: a windowless interior needs its lamps whichever
            // renderer is drawing it, and the ultra preset -- the one that discards the shipped
            // bakes entirely and so has NOTHING else lighting an interior -- is the Vulkan path.
            _device.SetLocalLights(_localLightScratch.AsSpan(0, localCount));
            _lampMaps.Assign(localCount, unjitteredProjection, _localLightScratch, _localLightOrder, _gi.LightFixtures);
            _lampMaps.AssignCoarse(localCount, _localLightOrder);
            _stormLampCount = localCount;

            // And the distance field those lamps' shadows are marched through wherever the renderer
            // cannot trace them -- WebGPU always, Vulkan with the RT switch off. See
            // LampOcclusionField.
            _device.SetLampOcclusionField(LampFieldFor(_gi.DistanceField));
        }

        // The player's flashlight, outside the lamps' gate: it lights under every lighting model the
        // deferred path has, the GI's warm-up included, and needs no fixture in the level to exist.
        _flashlight.Submit(
            captured && flashlightShown && !_mission.IsPlayerDead,
            _menuFlow.MenuMode && _menuFlow.PauseMenu,
            flashlightBody,
            eye,
            Viewpoint?.IsThirdPersonEngaged == true,
            _loop.TimeSource.Milliseconds,
            _flashlightWalls ??= ThirdPersonWalls);

        if (mapScene is not null)
        {
            _device.SetViewport(new Viewport(
                0,
                0,
                captured ? sceneTarget.Width : _window.Width,
                captured ? sceneTarget.Height : _window.Height));
        }

        if (!captured)
        {
            _device.EndStageTiming();
            _device.BeginStageTiming(RenderStage.Forward);
        }
    }

    /// <summary>Draws what the forward pass owns over the lit picture: the translucent world, the water, the light streaks, the effects and the weather.</summary>
    private void DrawForwardPass(
        HostConsole console, bool captured, Camera3D eye, Matrix4f projection, MapSceneView? mapScene, RenderResolution resolution,
        SceneTargetDescription sceneTarget, bool smoothPresentation, float tickBlend, bool cameraIndoors, bool remasteredOptics)
    {
        Debug.Assert(_scene is not null && _world is not null, "Drawn only while a level is up; see RenderWorld.");


        // The view model and anything translucent belong to the forward pass: it is drawn with its
        // own narrower projection, so its depth must not end up in the buffer the fog and the
        // reflections read.
        EndStageIfSlow("the deferred lighting, occlusion, reflections and fog");

        if (captured)
        {
            // THE GLAZING IS DRAWN TWICE, and the two draws answer different questions.
            //
            // This one records which pane is nearest at each pixel and which way it faces, into a
            // pass that owns a depth buffer. That pass exists because the question has one answer
            // per pixel and the translucency pass could not settle it: two windows in a line both
            // blend, both used to record, and an image store is not ordered between fragments, so
            // the answer changed every frame and the reflection fizzed. A depth test settles it
            // and cannot disagree with itself.
            //
            // The second draw, below, is the one you can see: the pane's own tint blended over
            // the finished picture. It writes no depth, because the tracers and the smoke are
            // drawn after it through the same buffer.
            _device.BeginPass(ScenePass.GlassLayer);
            _world.Draw(_scene, eye, projection, new WorldDrawOptions(GlassOnly: true));

            _device.EndPass();
            _device.BeginPass(ScenePass.Forward);
        }

        // Retail rotor blades carry graded alpha (Bell: 72/255). The opaque pass's
        // alpha test discards them entirely; blend their phase samples over the lit world.
        if (mapScene is { } rotorMap)
        {
            // The render size over the window's, per axis: an NVIDIA reconstruction's render size is the
            // runtime's, not the render scale's, and need not keep the window's aspect to the pixel.
            float scaleX = captured ? resolution.RenderWidth / (float)Math.Max(1, _window.Width) : 1f;
            float scaleY = captured ? resolution.RenderHeight / (float)Math.Max(1, _window.Height) : 1f;
            _device.SetViewport(new Viewport(
                (int)(rotorMap.Viewport.X * scaleX),
                (int)((_window.Height - rotorMap.Viewport.Y - rotorMap.Viewport.Height) * scaleY),
                Math.Max(1, (int)(rotorMap.Viewport.Width * scaleX)),
                Math.Max(1, (int)(rotorMap.Viewport.Height * scaleY))));
        }
        _world.Draw(_scene, eye, projection, new WorldDrawOptions(RotorsOnly: true));

        // The level's glazing, over a picture the lighting has finished. It has to follow the
        // opaque world for the ordinary reason anything blended does, and it is a pass of its own
        // rather than part of the rotor pass above because the two share nothing: a rotor is a
        // whole placement that blends, and a pane is half of a texture on a placement whose other
        // half was drawn into the G-buffer.
        _world.Draw(_scene, eye, projection, new WorldDrawOptions(GlassOnly: true));
        if (mapScene is not null)
        {
            _device.SetViewport(new Viewport(0, 0,
                captured ? sceneTarget.Width : _window.Width,
                captured ? sceneTarget.Height : _window.Height));
        }

        // The water is translucent, so it is drawn over the lit picture as the glazing is, testing the
        // depth of everything under it -- the ground above all -- and writing none. Recorded into the
        // G-buffer it was written as if opaque and then lit away: the deferred frame showed no sea.
        _water?.Draw(new WaterFrame(eye, projection, _loop.TimeSource.Milliseconds / 1000.0));
        EndStageIfSlow("the water");
        DrawTitleGroundDecals(eye, projection);

        // The ballistic streaks are LIGHT, and light belongs to the forward pass -- drawn in
        // the geometry pass they are written into the g-buffer as scenery and come out lit
        // and fogged (a dim grey square was exactly what that looked like). Here they draw
        // additively over the finished scene, emissive like the muzzle flame, testing the
        // world's depth but writing none.
        if (_tracers is not null && WorldView?.Ballistics is { } ballistics)
        {
            _tracers.Draw(
                ballistics,
                eye,
                projection,
                smoothPresentation ? tickBlend : 1f);

            // The finite guard's confession, rate-limited: a growing count is a NaN reaching
            // the tracer geometry -- the exact stuff one-frame black squares were made of.
            if (_tracers.SkippedQuads > _tracerSkipsLogged
                && (_tracerSkipsLogged == 0 || _tracers.SkippedQuads - _tracerSkipsLogged >= 100))
            {
                _tracerSkipsLogged = _tracers.SkippedQuads;
                _logger.Warning(
                    "Tracer",
                    $"{_tracers.SkippedQuads} tracer quad(s) carried a non-finite value and were not drawn.");
            }
        }

        // The soldiers' muzzle flames, for the same reason: drawn from the soldier pass they
        // landed in the G-buffer and shaded as scenery -- the one-frame black square at a
        // firing guard's muzzle. Light draws over the lit scene.
        (_soldiers ?? _menuBackdrop.Soldiers)?.DrawMuzzleFlashes(eye, projection);

        // The world's effects over the lit scene: the impact bursts, the original's own dust, chips and blood,
        // the smoke trails and columns, and the fireball over all of it as light.
        _effects.Draw(eye, projection, smoothPresentation ? tickBlend : 1f);

        // Rain environmental particles for levels featuring rain weather (e.g. RainEffect task).
        // While the camera is inside a building's shell the rain is suppressed, exactly as the
        // reference editor does per frame — buildings keep the weather outside.
        _stormEyeMetres = eye.Position;
        if (_storm is { } liveStorm)
        {
            // Assigned BEFORE the active test, never inside it. IsActive reads Indoors, so
            // setting it under `if (IsActive)` latches: the first frame the eye passes through a
            // building shell turns the storm off, and the assignment that would turn it back on
            // is inside the branch that no longer runs. That is what stuck the free camera on
            // the original's rain for the rest of the mission.
            liveStorm.Indoors = cameraIndoors;

            // Rain lands on the ground the player is standing on. The free camera has no feet,
            // so it keeps whatever the player last supplied rather than dropping the splashes.
            // This is only the fallback now: the trace below is what the storm actually lands on,
            // and the plane is what it falls back to where the trace has not reached yet.
            if (Viewpoint?.BodyPositionUnits is { } stormFeet)
            {
                liveStorm.GroundHeightMetres =
                    stormFeet.Z / WorldScene.UnitsPerMetre;

                // The grenade caster, which is the one trace in the game that already covers the
                // terrain AND the placed collision meshes -- the same two things rain lands on.
                liveStorm.Ground = Viewpoint.GroundTrace;
                liveStorm.UnitsPerMetre = WorldScene.UnitsPerMetre;
            }
        }

        if (_storm is { IsActive: true } storm)
        {
            // Direction and colour from the one effective sun the deferred pass shades with, and
            // the fill from the sky the deferred lighting itself shades with, averaged over every
            // direction -- not the level's raw ambient triple, which is a chromaticity that says
            // what colour the fill is and nothing about how much of it there is, and at night is
            // several times the light the scene gets. The storm multiplies its colours by this,
            // so handing over more light than the scene has drew every drop brighter than the
            // ground behind it: a rainy night became a wall of rain with nothing visible through it.
            DirectionalLightSettings stormSun = _effectiveSun ?? default;
            Vector3f stormSunLight =
                stormSun.Colour * RenderCVars.SunScale.Value * RenderCVars.LightScale.Value;

            // What a refracting drop really shows is the frame BEHIND it, and outdoors that
            // is overwhelmingly sky rather than the flat fill the interiors are lit by. The sky
            // fill alone left daylight rain almost invisible against a bright sky, which is the
            // one place rain is most obviously there. A fifth of the sun's own illuminance is a
            // rough stand-in for a daylit sky's radiance, and it goes to nothing at night with
            // the sun it is taken from.
            storm.Draw(
                eye,
                projection,
                smoothPresentation ? tickBlend : 1f,
                _effectiveSun is null ? Vector3f.UnitZ : stormSun.Direction,
                stormSunLight,
                AmbientSkyLight() + (stormSunLight * 0.2f),
                _loop.TimeSource.Milliseconds / 1000f,
                _stormLamps.AsSpan(0, _stormLampCount),

                // A display level, taken back through the frame's exposure into the scene's
                // units, so the drops never sink below a faint grey whatever the night.
                RainVeilDisplay / MathF.Max(_device.SceneExposure, 1e-3f));
        }
        else if (_storm is not { Enabled: true } && _rain is { IsWeatherEnabled: true } rain)
        {
            // The original's band, drawn only where the storm is not the renderer in charge.
            // Enabled is the question, not IsActive: Enabled says which of the two owns the
            // weather -- the preset's r_storm, which only vanilla turns off -- while IsActive
            // says whether that owner has anything to draw this frame. Falling back on IsActive
            // made every reason the storm draws nothing into a reason to draw 1998's rain
            // instead, so dialling r_storm_intensity to zero on an ultra preset did not stop the
            // rain, it replaced it with the vanilla band.
            rain.Indoors = cameraIndoors;
            rain.Draw(eye, projection, _loop.TimeSource.Milliseconds / 1000f);
        }

        // Snow uses the same authored building shells and weather band, but renders small,
        // drifting flakes instead of rain droplets.
        if (_snow is { IsWeatherEnabled: true } snow)
        {
            snow.Indoors = cameraIndoors;
            snow.Draw(eye, projection, _loop.TimeSource.Milliseconds / 1000f);
        }

        // SCameraLens draws its own alarmlight.spr in the forward pass: green when clear,
        // red while confirming/detecting, and attached to the head as it pans.
        if (_mission.SecurityCameras is { } statusCameras)
        {
            _cameraStatusLights?.Draw(
                statusCameras.Cameras,
                eye,
                projection,
                tickBlend,
                smoothPresentation);
        }

        // The through-wall glow over the enemies the binocular scan marked, in the same FORWARD
        // pass and for the same reason as the inspector selection below: submitted from the world
        // pass it would be lit and fogged into a smear. Here it composites over the finished
        // scene and is carried into the resolve, so the lens and bloom pick it up. Only while the
        // remastered optics are up.
        if (remasteredOptics
            && RenderCVars.BinocularOutline.Value
            && Viewpoint?.IsBinocularsOpen == true)
        {
            _soldiers?.DrawBinocularOutlines(eye, radiusPixels: 8f);
        }

        // The developer inspector's selection, drawn as a real glow around the model rather
        // than a mark beside it. It belongs to the FORWARD pass, with the tracers and the muzzle
        // flames: submitted from the world pass it would be written into the g-buffer as
        // scenery and come back lit and fogged, which is a dull smear rather than a mark. Late
        // in the pass because it carves its silhouette out of the depth buffer to read through
        // walls, and the view model clears depth immediately after.
        _inspector.DrawOutline(console, eye);
    }

    /// <summary>Gets the title's own player's viewpoint, made from the title's presentation the first time it is asked for.</summary>
    private IMissionViewpoint? Viewpoint =>
        _viewpoint ??= _presentation.Presenters.Create<MissionPresentationRequest, IMissionViewpoint>(
            new MissionPresentationRequest(_device, _hosted, _logger));

    /// <summary>Gets what the title's player has put in the world, made from the title's presentation the first time a frame asks for it.</summary>
    private IMissionWorldView? WorldView =>
        _worldView ??= _presentation.Presenters.Create<MissionPresentationRequest, IMissionWorldView>(
            new MissionPresentationRequest(_device, _hosted, _logger));

    /// <summary>Gets the title's first-person view, made from the title's presentation the first time a frame asks for it.</summary>
    private IMissionFirstPersonView? FirstPersonView =>
        _firstPerson ??= _presentation.Presenters.Create<MissionPresentationRequest, IMissionFirstPersonView>(
            new MissionPresentationRequest(_device, _hosted, _logger));

    /// <summary>Draws the first-person rig, or the scope that replaces it.</summary>
    /// <returns>The binocular range to show, in metres, or <see langword="null"/> when the glasses are down.</returns>
    private float? DrawFirstPersonView(Camera3D eye, bool showViewModel, SightZoomFrame sight, bool smoothPresentation, float tickBlend)
    {
        FirstPersonResult drawn = FirstPersonView?.Draw(
            eye,
            new FirstPersonFrame(
                showViewModel,
                sight.ViewModelTanX,
                smoothPresentation,
                tickBlend,
                FarPlane,
                _mission.Terrain,
                _arms,
                _heldWeapon,
                _loadedMissionForWeapon)) ?? default;
        if (!drawn.DrewRig)
        {
            DrawTitleFirstPerson(eye);
        }

        return drawn.BinocularRangeMetres;
    }

    /// <summary>What the mission's HUD draws from this frame.</summary>
    private MissionHudFrame HudFrame(
        FrameLoopState state,
        bool remasterHud,
        bool remasteredOptics,
        bool showFirstPersonHud,
        bool showPlayerHud,
        bool tacticalMessages,
        bool worldInteractionPrompt,
        IReadOnlyList<EnemyOverlayBox> binocularTargets,
        float? binocularRangeMetres)
    {
        _hudOverlay ??= _presentation.Presenters.Create<MissionPresentationRequest, IMissionHudOverlay>(
            new MissionPresentationRequest(_device, _hosted, _logger));
        return new MissionHudFrame(
            _window.Width,
            _window.Height,
            SafeWindowWidth,
            SafeWindowHeight,
            UiScale.Resolve(_options.UiScale, _window.Width, _window.Height),
            _loop.Clock.TickCount,
            _binocularOptics.Seconds,
            remasterHud,
            remasteredOptics,
            showFirstPersonHud,
            showPlayerHud,
            tacticalMessages,
            worldInteractionPrompt,
            binocularTargets,
            binocularRangeMetres,
            _hud,
            _remasteredHud);
    }

    /// <summary>Resolves the frame and draws everything 2D over it: the HUD, the messages, the computer, the damage wash and the flash.</summary>
    private void DrawScreenOverlays(
        FrameLoopState state, bool captured, PostProcessSettings postProcess, bool remasterHud, bool remasteredOptics, bool showFirstPersonHud,
        IReadOnlyList<EnemyOverlayBox> binocularTargets, float? binocularRangeMetres, bool tacticalMessages, bool showPlayerHud, bool smoothPresentation,
        float tickBlend, SightZoomFrame sight, bool worldInteractionPrompt, Camera3D eye, float horizontalTangent, float aspectRatio,
        bool cameraIndoors)
    {
        // Everything 2D belongs AFTER the resolve, on the back buffer: the HUD, the damage wash,
        // the menus and the console are authored in display colours and must never be tone mapped.
        // Unconditional, and outside the player-mode block on purpose -- a free-camera frame draws
        // no view model, and leaving the capture open would strand it until the next frame threw.
        if (!captured)
        {
            _device.EndStageTiming();
        }

        if (captured)
        {
            _device.EndPass();
            _device.EndSceneCapture();
            _device.ResolveScene(postProcess);
            captured = false;
            EndStageIfSlow("the resolve (tone map, bloom, depth of field, temporal)");
        }

        _device.BeginStageTiming(RenderStage.UserInterface);

        // StatusScreen is a display-space layer. Keep every retail HUD component out of the
        // captured scene so camera motion blur, depth of field, bloom and tone mapping can only
        // affect the world and first-person rig. The damage and flash washes intentionally remain
        // later in the draw order and still cover the HUD exactly as they did before this separation.
        if (TitlePlayer is { } titleHudPlayer && _titleHud is { } titleHud
            && _playerMode && !_computerOpen && !_entry.IsCameraActive && _mission.Cutscenes?.IsPlaying != true
            && Viewpoint?.ShowsViewModel != false)
        {
            _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
            state.Batch.Begin();
            titleHud.Draw(state.Batch, titleHudPlayer, _window.Width, _window.Height);
            state.Batch.End();
        }

        MissionHudFrame hudFrame = HudFrame(state, remasterHud, remasteredOptics, showFirstPersonHud, showPlayerHud, tacticalMessages, worldInteractionPrompt, binocularTargets, binocularRangeMetres);
        _hudOverlay?.DrawWeaponHud(state.Batch, hudFrame);

        _hudOverlay?.DrawRetailStatus(state.Batch, hudFrame);

        if (_mission.Cutscenes?.Frame is { } cinematicFraming)
        {
            CutsceneFramingRenderer.Draw(
                _device, state.Batch, cinematicFraming, _window.Width, _window.Height, _hud, _loop.Clock.TickCount);
        }

        // ComputerMap renders the terrain through its own top-down camera and turns only that
        // captured view into green phosphor. The SIMOTECH casing is a separate retail panel that
        // is drawn after the resolve, in its authored colours, before the HUD fills its aperture.
        //
        // The tactical interface has no casing at all — that is the point of it — so this whole
        // block is the retail path's alone.
        if (_computerOpen
            && TacticalComputer is null
            && _playerMode
            && Viewpoint?.IsDeathViewActive != true
            && Viewpoint?.WalkingEye(smoothPresentation, tickBlend) is { } computerEye)
        {

            Matrix4f computerViewModelProjection = ViewModelProjection.Create(
                sight.ViewModelTanX,
                FarPlane);

            if (_mapComputerDevice is { IsReady: true } deviceModel)
            {
                Viewport deviceViewport = _mapComputer!.DeviceViewport(_window.Width, _window.Height);
                float horizontalHalfTangent = sight.ViewModelTanX;
                // The mapcomputer mesh is a close foreground prop. Its housing extends past the
                // view just as it does in retail; 2.50 is the retail asset's aperture-to-frame
                // calibration against the map task's 91% screen viewport. The casing is intentionally
                // cropped at the display edges: leaving its outside black behind the four corners
                // makes the computer look set back from the camera rather than held in the foreground.
                float horizontalCoverage = deviceViewport.Width / (float)Math.Max(1, _window.Width) * 2.50f;
                deviceModel.Draw(computerEye, computerViewModelProjection, horizontalHalfTangent, horizontalCoverage);
            }

            _device.SetDepthState(DepthState.Disabled);
        }

        if (_computerOpen
            && _mission.Objectives is { } objectiveRuntime
            && Viewpoint?.BodyPositionUnits is { } computerBody)
        {
            _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
            state.Batch.Begin();
            if (TacticalComputer is { } tacticalChrome)
            {
                tacticalChrome.Draw(
                    state.Batch,
                    objectiveRuntime,
                    computerBody,
                    _soldiers?.MapContacts() ?? [],
                    _computerHighlights?.Visible() ?? [],
                    _window.Width,
                    _window.Height,
                    _computerFlight.ChromeOpacity);
            }
            else if (_mapComputer is { IsReady: true } computer)
            {
                computer.Draw(
                    state.Batch,
                    objectiveRuntime,
                    computerBody,
                    _soldiers?.MapContacts() ?? [],
                    _computerHighlights?.Visible() ?? [],
                    _window.Width,
                    _window.Height);
            }

            state.Batch.End();
        }

        _hudOverlay?.DrawWashes(state.Batch, hudFrame);

        _hudOverlay?.DrawRemasteredHud(state.Batch, hudFrame);

        // The marker itself, over the finished frame and over the HUD. It is a surface standing
        // in the level rather than an overlay on the picture, so it is handed the same presented
        // eye and the same lens that drew the thing it belongs to, and it projects itself.
        if (worldInteractionPrompt
            && _interactionPrompt.Visible is { } promptTarget
            && _remasteredHud is { IsReady: true } promptHud)
        {
            _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
            state.Batch.Begin();
            promptHud.DrawInteractionPrompt(
                state.Batch,
                new InteractionPromptView(
                    promptTarget.AnchorUnits / WorldScene.UnitsPerMetre,
                    _interactionPrompt.Opacity,
                    _interactionPrompt.Pop,
                    InteractionPromptPresentation.BadgeLabel(
                        PlayerProfileInputBindings.BoundKeyName(
                            _configuration.Active,
                            PlayerInputBindings.ConfigName(PlayerAction.Activate))),
                    InteractionPromptPresentation.Title(promptTarget),
                    InteractionPromptPresentation.Detail(promptTarget),
                    InteractionPromptPresentation.RingProgress(promptTarget)),
                eye,
                horizontalTangent,
                aspectRatio,
                SafeWindowWidth,
                SafeWindowHeight);
            state.Batch.End();
        }

        // The HUD is in the condition, not asserted inside it. Every presenter is nullable so a
        // title under construction draws the passes it has and skips the rest -- and I.G.I. 2 has
        // no HUD at all yet, so the null-forgiving read of it here took the process down as soon
        // as one of its missions had an objective to post.
        if (tacticalMessages
            && showFirstPersonHud
            && _remasteredHud is { } messageHud
            && _mission.Objectives is { } modernMessages
            && _messages is { IsReady: true } modernCards)
        {
            _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
            state.Batch.Begin();
            float topOffset = new LayoutContext(_window.Width, _window.Height)
                .Px(messageHud.MessageTopInset);
            modernCards.Draw(
                state.Batch,
                modernMessages.DisplayedMessages.Where(static message => !message.IsCutscene),
                _window.Width,
                _window.Height,
                topOffset);
            state.Batch.End();
        }

        // Cutscene StatusMessages are subtitles, not HUD. They remain visible while the authored
        // camera owns the frame even though the arms, weapon and ordinary mission UI are hidden.
        if (_mission.Cutscenes?.IsPlaying == true && _mission.Objectives is { } subtitleRuntime)
        {
            _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
            state.Batch.Begin();
            // Retail cutscenes always use StatusMessageScreen's shipped bitmap face and its
            // left-aligned 620/640-width line. TacticalMessages only replaces the in-game feed;
            // letting it replace dialogue produced a centred modern subtitle unlike the original.
            if (_hud is { IsReady: true } subtitleHud)
            {
                subtitleHud.DrawStatusMessages(
                    state.Batch,
                    subtitleRuntime.DisplayedMessages,
                    _window.Width,
                    _window.Height,
                    // Retail scales this full-width normalized subtitle face with framebuffer
                    // height. Keeping the HUD's integer auto scale here made a 938px capture use
                    // the unscaled 480p glyphs, less than half the size of the original.
                    // The retail text objects use the shipped face at their 0.9 presentation
                    // scale; retain that calibration while growing it with framebuffer height.
                    Math.Max(1f, (_window.Height / (float)UiScale.AuthoredHeight) * 0.87f));
            }
            else if (_messages is { IsReady: true } subtitleCards)
            {
                subtitleCards.Draw(state.Batch, subtitleRuntime.DisplayedMessages, _window.Width, _window.Height);
            }
            state.Batch.End();
        }

        _device.EndStageTiming();

        DrawDebugOverlays(state, eye, cameraIndoors);
    }

    /// <summary>Draws the tools over the frame: the sun gizmo, the F12 overlay and its crosshair, and the r_perflog panel.</summary>
    /// <param name="state">What the frame loop keeps from one frame to the next.</param>
    /// <param name="eye">The eye the frame was drawn from, which the crosshair readout is cast from.</param>
    /// <param name="cameraIndoors">Whether the eye is under cover, as the weather was told.</param>
    private void DrawDebugOverlays(FrameLoopState state, Camera3D eye, bool cameraIndoors)
    {
        if (_world is null || _camera is null)
        {
            return;
        }

        float debugPanelHeight = 0f;

        // The sun gizmo, over everything, whenever it is up -- not gated on the F12 overlay, since it
        // is a tool being used rather than an inspection panel.
        if (_sunGizmo.Active)
        {
            _device.BeginStageTiming(RenderStage.DebugRendering);
            state.Batch.Begin();
            _sunGizmo.Draw(state.Batch);
            state.Batch.End();
            _device.EndStageTiming();
        }

        // The F12 debug overlay and crosshair. Works in both the walking player and the free (flying)
        // camera: the state lines come from whichever is active, the crosshair readout and the reticle
        // are cast from the eye that is actually drawing.
        if (state.ShowDebug && _debugOverlay is { IsReady: true } debugOverlay)
        {
            _device.BeginStageTiming(RenderStage.DebugRendering);
            static string I(FormattableString s) => FormattableString.Invariant(s);

            Vector3d originU = new(
                eye.Position.X * WorldScene.UnitsPerMetre,
                eye.Position.Y * WorldScene.UnitsPerMetre,
                eye.Position.Z * WorldScene.UnitsPerMetre);
            Matrix3f orient = eye.Orientation;
            Vector3d fwd = new(orient.M31, orient.M32, orient.M33);
            const double ReachU = 300.0 * WorldScene.UnitsPerMetre;
            Vector3d endU = new(
                originU.X + (fwd.X * ReachU), originU.Y + (fwd.Y * ReachU), originU.Z + (fwd.Z * ReachU));

            string target = "target: (nothing / terrain / sky)";
            string? detect = null;
            double bestT = double.MaxValue;
            if (_mission.Solids is not null
                && _mission.Solids.TryIdentify(
                    originU,
                    endU,
                    out string objLabel,
                    out WorldPlacement? objPlacement,
                    out WorldRayHit objHit))
            {
                bestT = objHit.Distance;

                // The texture, from the RENDER mesh, because mat= cannot give it. The contact
                // above is on the collision mesh and names a game-material row -- SoftMetal,
                // Glass -- which drives bullets and decals and is read by nothing in the
                // renderer. What the renderer reads is materials.json, keyed by texture name,
                // and until this line there was no way to learn that name from inside the game:
                // somebody wanting to say "this pane is glass" had to guess which of a level's
                // hundred numbered textures they were looking at.
                string surface =
                    objPlacement is not null
                    && RenderSurfaceProbe.TryFindTexture(
                        objPlacement,
                        originU,
                        new Vector3d(endU.X - originU.X, endU.Y - originU.Y, endU.Z - originU.Z),
                        out string objTexture,
                        out _)
                        ? objTexture
                        : "?";

                target = I($"target: OBJECT {objLabel} @ {objHit.Distance / WorldScene.UnitsPerMetre:F1}m ({objHit.Position.X:F0}, {objHit.Position.Y:F0}, {objHit.Position.Z:F0}) mat={objHit.Material} tex={surface}");
            }

            if (_soldiers is not null
                && _soldiers.TryPick(originU, fwd, ReachU, out string botName, out double botT, out Vector3d feetU, out int botZone)
                && botT < bestT)
            {
                // The floor the game's collision puts under this soldier's feet — so a sunk guard shows
                // its feet Z below the floor Z, and a correct one shows them level.
                string floor = "?";
                if (_mission.Solids is not null)
                {
                    Vector3d fTop = new(feetU.X, feetU.Y, feetU.Z + 2048.0);
                    Vector3d fBot = new(feetU.X, feetU.Y, feetU.Z - (10.0 * WorldScene.UnitsPerMetre));
                    if (_mission.Solids.TryIdentify(fTop, fBot, out string floorLbl, out WorldRayHit floorHit))
                    {
                        floor = I($"{floorHit.Position.Z:F0} ({floorLbl} mat{floorHit.Material}) Δ={(feetU.Z - floorHit.Position.Z) / WorldScene.UnitsPerMetre:F2}m");
                    }
                }

                string describedBot = ModelNameRegistry.DescribeModel(botName);
                target = I($"target: SOLDIER {describedBot} zone={HumanDamage.ZoneName(botZone)} feetZ={feetU.Z:F0} floorZ={floor}");
            }

            // Whatever soldier the crosshair is on, ask its brain which detection gate is (not) passing
            // against the point the guards actually test -- the player's own eye in engine units, the same
            // point soldiers.Tick is fed.
            Vector3d watchedEye = _playerMode && Viewpoint?.EyeUnits is { } playerEye
                ? playerEye
                : _camera.Position;
            if (_soldiers is not null
                && _soldiers.TryDetectionInfo(originU, fwd, ReachU, watchedEye, out string detectInfo))
            {
                detect = detectInfo;
            }

            List<string> lines;
            if (_playerMode && Viewpoint?.DebugLines() is { } walking)
            {
                lines = [.. walking, target];
            }
            else
            {
                Vector3d cm = _camera.Position;
                lines =
                [
                    "mode: FREE CAM",
                    I($"pos units ({cm.X * WorldScene.UnitsPerMetre:F0}, {cm.Y * WorldScene.UnitsPerMetre:F0}, {cm.Z * WorldScene.UnitsPerMetre:F0})"),
                    I($"pos m     ({cm.X:F2}, {cm.Y:F2}, {cm.Z:F2})"),
                    I($"yaw {_camera.Yaw * 180f / MathF.PI:F1}  pitch {_camera.Pitch * 180f / MathF.PI:F1}"),
                    target,
                ];
            }

            if (detect is not null)
            {
                lines.Add(detect);
            }

            // Which of the level's lamps the crosshair is on, if any -- so the switch keys can be
            // aimed rather than guessed at, and so a press that appeared to do nothing can be told
            // apart from a press that landed on a lamp whose light is off screen.
            if (DescribeAimedLamp() is { } aimedLamp)
            {
                lines.Add(aimedLamp);
            }

            _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
            state.Batch.Begin();
            debugPanelHeight = debugOverlay.Draw(state.Batch, lines, _window.Width, _window.Height);

            // A centre reticle so the target readout can be aimed — the crosshair the free camera lacks.
            float cx = _window.Width * 0.5f;
            float cy = _window.Height * 0.5f;
            const float Len = 9f;
            const float Th = 1f;
            static Bgra32 White() => new(255, 255, 255, 220);
            void Bar(float x0, float y0, float x1, float y1) => state.Batch.DrawQuad(
                TextureHandle.None,
                new Vertex2D(x0, y0, 0f, 0f, White()),
                new Vertex2D(x1, y0, 1f, 0f, White()),
                new Vertex2D(x1, y1, 1f, 1f, White()),
                new Vertex2D(x0, y1, 0f, 1f, White()));
            Bar(cx - Len, cy - Th, cx + Len, cy + Th);
            Bar(cx - Th, cy - Len, cx + Th, cy + Len);

            state.Batch.End();
            _device.EndStageTiming();
        }

        // r_perflog, its own overlay rather than a section of F12's: it answers a different question
        // and is read while the game is being played rather than while it is being inspected. Top
        // right, so the two can be up together. When F12 is also enabled, place this panel below
        // the measured F12 panel instead of letting both panels claim the same top-right corner.
        if (RenderCVars.PerformanceLog.Value
            && !_options.IsBenchmark
            && _debugOverlay is { IsReady: true } performanceOverlay)
        {
            _device.BeginStageTiming(RenderStage.DebugRendering);
            static string I(FormattableString s) => FormattableString.Invariant(s);

            IReadOnlyList<RenderStageTiming> timings = _device.StageTimings();
            GpuFrameTiming gpu = _device.GpuTiming();
            FramePerformanceStatistics frame = state.FramePerformance.Statistics();
            GraphicsPreset preset = GraphicsPresetRegistry.Default.Current;
            List<string> perf =
            [
                I($"{preset}  {_window.Width}x{_window.Height}  vsync {(_device.VSyncEnabled ? "on" : "off")}  indoor {(cameraIndoors ? "yes" : "no")}"),

                // Which of the two shadow systems is actually drawing, spelled out with the keys
                // that move it. Judging a shadow in a capture means knowing which one
                // produced it, and the answer is two cvars deep -- r_rt_shadows does nothing
                // while r_rt is off, and neither means anything with shadows turned off.
                I($"shadow   {ShadowModeLabel()}"),
                I($"texture  {TextureModeLabel()}"),
                I($"fps {frame.FramesPerSecond:0.0}  frame {frame.AverageMilliseconds:0.00} ms  1% {frame.OnePercentLowFramesPerSecond:0.0}"),

                // The whole GPU frame measured end to end, against the sum of the stages listed
                // below it. Everything else on this overlay is built from those stages, so a pass
                // recorded outside every one of them shows up nowhere -- and a millisecond of lamp
                // shadow did exactly that for a whole session while this panel read as though the
                // lamps were free. "lost" is that difference: a few hundredths is the timestamps'
                // own overhead, anything more is real work with nobody's name on it.
                MeasuredFrameLine(_device, gpu),

                "gpu ms   avg    p95   peak  budget",
            ];
            foreach (RenderStageTiming timing in timings)
            {
                if (!timing.Active)
                {
                    perf.Add($"{timing.Stage,-24} off");
                    continue;
                }

                // The budget beside the measurement, because a number without the number it was
                // supposed to beat is a number nobody acts on.
                string budget = RenderBudget.For(timing.Stage) is { } allowed
                    ? I($" /{allowed:0.0}{(timing.Milliseconds > allowed ? " OVER" : string.Empty)}")
                    : string.Empty;

                perf.Add(I($"{timing.Stage,-24} {timing.Milliseconds,5:0.00} {timing.P95Milliseconds,6:0.00} {timing.PeakMilliseconds,6:0.00}{budget}"));
            }

            perf.Add(gpu.SampleCount > 0
                ? I($"{"gpu total",-24} {gpu.Milliseconds,5:0.00} {gpu.P95Milliseconds,6:0.00} {gpu.PeakMilliseconds,6:0.00}")
                : "measuring...");

            if (RenderCVars.PerformanceLogPasses.Value is > 0 and int shown)
            {
                IReadOnlyList<RenderPassTiming> passes = _device.PassTimings();
                for (int p = 0; p < passes.Count && p < shown; p++)
                {
                    RenderPassTiming pass = passes[p];
                    perf.Add(I($"  {pass.Label,-22} {pass.Milliseconds,5:0.00}        {pass.PeakMilliseconds,6:0.00}"));
                }
            }

            // The CPU side the GPU timers cannot see. cpu is how much longer the frame's wall time
            // is than the GPU's -- submission, game logic and GC that did not overlap the GPU. alloc
            // is the managed allocation rate, eased so it is readable; a high figure is what feeds the
            // GC collections whose pauses read as the frame's stutter, and the LOH share of it drives
            // the Gen2 collections that stall hardest. gc shows the Gen0/1/2 collection counts so a
            // climbing Gen2 is visible while playing rather than only under a profiler.
            long allocNow = GC.GetTotalAllocatedBytes();
            long nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_perfAllocTicks != 0)
            {
                double seconds = (nowTicks - _perfAllocTicks) / (double)System.Diagnostics.Stopwatch.Frequency;
                if (seconds > 1e-4)
                {
                    double sample = (allocNow - _perfAllocBytes) / seconds / (1024.0 * 1024.0);
                    _perfAllocRate = (_perfAllocRate * 0.9) + (sample * 0.1);
                }
            }

            _perfAllocBytes = allocNow;
            _perfAllocTicks = nowTicks;
            double cpuMilliseconds = Math.Max(0.0, frame.AverageMilliseconds - gpu.Milliseconds);
            perf.Add(I($"cpu {cpuMilliseconds,5:0.00} ms   alloc {_perfAllocRate,6:0} MB/s   gc {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}"));

            _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
            state.Batch.Begin();
            float performanceTopOffset = debugPanelHeight > 0f
                ? debugPanelHeight + DebugOverlayRenderer.PanelGapPixels(_window.Width, _window.Height)
                : 0f;
            performanceOverlay.DrawTopRight(
                state.Batch,
                perf,
                _window.Width,
                _window.Height,
                performanceTopOffset);
            state.Batch.End();
            _device.EndStageTiming();
        }

        if (!state.ReportedWorld)
        {
            state.ReportedWorld = true;
            List<string> world = [];
            _world.Describe(world);
            _logger.Information("World", $"{string.Join("; ", world)}, from {_camera.Position}.");

            if (_terrain is not null)
            {
                // The presenter formats its own counters: what is worth reporting about a
                // streamed octree is not what is worth reporting about a grid of tiles.
                List<string> terrain = [];
                _terrain.Describe(terrain);
                _logger.Information("Terrain", string.Join("; ", terrain));
            }
        }
    }

    /// <summary>The soldiers' bodies as the shadow passes draw characters.</summary>
    /// <param name="soldiers">The soldiers.</param>
    private sealed class SoldierBodies(ISoldierBodies soldiers) : ICharacterPresenter
    {
        public void Draw(in Camera3D camera, in Matrix4f projection) => soldiers.Draw(camera, projection);

        public void DrawPlayerBody(in Camera3D camera, in Matrix4f projection) => soldiers.DrawPlayerBody(camera, projection);
    }
}
