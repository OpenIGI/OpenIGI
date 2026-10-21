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
    /// <summary>What one frame of the world is drawn from, settled a step at a time by <see cref="RenderWorld"/>.</summary>
    /// <remarks>
    /// A value on the frame's own stack rather than fields on the host: each step reads what the steps before it settled
    /// and adds its own, so the order the steps run in is the order the frame was always worked out in, and nothing of
    /// one frame's view can be read on the next.
    /// </remarks>
    private struct FrameView
    {
        // The presentation blend and whether it is in force.
        public bool SmoothPresentation;
        public float TickBlend;

        // The lens: the sight zoom, the window's aspect and the field of view the frame is projected through.
        public SightZoomFrame Sight;
        public float AspectRatio;
        public float HorizontalTangent;
        public float VerticalFieldOfView;

        // The map computer's own view of the level, when it owns the frame.
        public MapSceneView? MapScene;

        // The resolve's settings.
        public PostProcessSettings PostProcess;

        // The projection with and without the temporal jitter, and the size the frame is rendered at.
        public Matrix4f UnjitteredProjection;
        public Matrix4f Projection;
        public bool WantsTemporal;
        public float JitterX;
        public float JitterY;
        public RenderResolution Resolution;

        // The eye, the body the flashlight hangs off, and what of the character is shown.
        public Camera3D Eye;
        public Camera3D FlashlightBody;
        public bool FlashlightShown;
        public bool ThirdPersonBody;

        // The offscreen scene, the sun it is lit by and the cascades fitted to it.
        public SceneTargetDescription SceneTarget;
        public DirectionalLightSettings Light;
        public Vector3f ViewSunDirection;
        public ShadowSettings ShadowSettings;
        public int CascadeCount;

        // What the HUD shows over the frame.
        public bool RemasterHud;
        public bool RemasteredOptics;
        public bool TacticalMessages;
        public bool ShowPlayerHud;
        public bool ShowFirstPersonHud;
        public bool ShowViewModel;
        public bool WorldInteractionPrompt;
        public IReadOnlyList<EnemyOverlayBox> BinocularTargets;
    }

    // The exposure adaptation rate during the warm-up and in steady play. Fast while the GI converges so the
    // exposure follows it and the picture holds its brightness; slow after, for an unobtrusive eye-adaptation.
    private const float GiExposureWarmupRate = 0.35f;
    private const float ExposureSteadyRate = 0.04f;

    // The binocular glass, and the wall clocks its fade and the motion blur's shutter are measured on.
    private readonly BinocularOptics _binocularOptics = new();
    private int _lastBinocularMilliseconds;
    private int _lastMotionBlurMilliseconds;

    /// <summary>Settles the presentation blend and the lens: the sight zoom, the aspect and the field of view.</summary>
    private void ResolveFrameLens(HostConsole console, ref FrameView view)
    {
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
        if (_mission.CutsceneFrameShown is { FieldOfView: > 0f } cutsceneLens)
        {
            // EditCamera.FOV is the horizontal half-angle tangent submitted at 0x4F604A,
            // not an angle in radians. A value of 1 is retail's 90-degree horizontal lens.
            // The authored framing was composed through retail's fixed 4:3 viewport. Keep that
            // vertical aperture at modern aspects and let the wider projection reveal the sides;
            // deriving it from a 16:9 window crops the top and bottom by 25 percent, which cut
            // Anya's upper body out of the Mission 1 briefing.
            verticalFieldOfView = CutsceneVerticalFieldOfView(cutsceneLens.FieldOfView);
        }

        view.SmoothPresentation = smoothPresentation;
        view.TickBlend = tickBlend;
        view.Sight = sight;
        view.AspectRatio = aspectRatio;
        view.HorizontalTangent = horizontalTangent;
        view.VerticalFieldOfView = verticalFieldOfView;
    }

    /// <summary>Settles the map computer's own view of the level, when the computer owns the frame.</summary>
    private void ResolveMapScene(ref FrameView view)
    {
        // The map's own view of the level, when the computer owns the frame. Under the tactical
        // interface the transition owns it for as long as it is flying — same viewport, but its
        // camera and its field of view, which is what makes the climb and the fall a move of the
        // player's own eye rather than a cut to a second one.
        MapSceneView? mapScene = null;
        if (_computerOpen && Viewpoint?.BodyPositionUnits is { } mapBody)
        {
            if (TacticalComputer is { } tacticalView)
            {
                // No device, no overhead view: the world stays the player's own behind the pages.
                if (tacticalView.MapAvailable)
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
            }
            else if (_mapComputer is { IsReady: true, ShowsMap: true } computerView)
            {
                mapScene = computerView.SceneView(
                    mapBody, _window.Width, _window.Height);
            }
        }

        view.MapScene = mapScene;
    }

    /// <summary>Settles the resolve's settings, and the map's own lens where the map owns the frame.</summary>
    private void ResolvePostProcess(ref FrameView view)
    {
        MapSceneView? mapScene = view.MapScene;
        float aspectRatio = view.AspectRatio;
        float verticalFieldOfView = view.VerticalFieldOfView;

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
            NightShift: RenderCVars.NightShift.Value * TimeOfDayLighting.NightVisionWeight(RenderCVars.Nightfall.Value));

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

        view.PostProcess = postProcess;
        view.AspectRatio = aspectRatio;
        view.VerticalFieldOfView = verticalFieldOfView;
    }

    /// <summary>Settles the projection, the size the frame is rendered at and the temporal jitter.</summary>
    private void ResolveProjection(FrameLoopState state, ref FrameView view)
    {
        float aspectRatio = view.AspectRatio;
        float verticalFieldOfView = view.VerticalFieldOfView;

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

        view.UnjitteredProjection = unjitteredProjection;
        view.Projection = projection;
        view.WantsTemporal = wantsTemporal;
        view.JitterX = jitterX;
        view.JitterY = jitterY;
        view.Resolution = resolution;
    }

    /// <summary>Settles the eye: whichever camera claims the frame, the flashlight's body and whether the character is drawn.</summary>
    private void ComposeFrameEye(FrameLoopState state, HostConsole console, ref FrameView view)
    {
        Debug.Assert(_camera is not null, "Composed only while a level is up; see RenderWorld.");
        bool smoothPresentation = view.SmoothPresentation;
        float tickBlend = view.TickBlend;
        MapSceneView? mapScene = view.MapScene;

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
            Vector3f loggedSun = _timeOfDay.EffectiveSun?.Direction ?? Vector3f.UnitZ;

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
        else if (_mission.CutsceneFrameShown is { } cutsceneView)
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

        view.Eye = eye;
        view.FlashlightBody = flashlightBody;
        view.FlashlightShown = flashlightShown;
        view.ThirdPersonBody = thirdPersonBody;
    }

    // The camera's projection, in one place: the terrain traversal needs the same field of view the
    // projection matrix is built from, or it would cull against a frustum the picture does not have.
    // The far plane is shared with the sky, which sizes its dome from it.
    //
    // The field of view is the original's, not a chosen one. HumanView's constructor (0x4643A0) sets
    // its half-tangent to tan(45 degrees) = 1.0, the camera copies it to HumanCamera+96 and submits it
    // to the view (0x4D9870) -- and it is HORIZONTAL, which the terrain traversal's own
    // viewDistance/sqrt(tanX) independently confirms. So the horizontal field of view is 90 degrees and the
    // vertical one follows from the window's aspect, rather than the other way round.
    private static float VerticalFieldOfView(float aspectRatio, float horizontalTangent) =>
        2f * MathF.Atan(horizontalTangent / MathF.Max(aspectRatio, 0.0001f));

    /// <summary>Converts an authored EditCamera lens through the retail 4:3 composition aperture.</summary>
    internal static float CutsceneVerticalFieldOfView(float horizontalTangent) =>
        VerticalFieldOfView(4f / 3f, horizontalTangent);
}
