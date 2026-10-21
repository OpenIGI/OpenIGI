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

        // The frame's view, settled in the order each part of it reads the last: the lens, the map's own
        // view, the picture's settings, the projection and its jitter, and then the eye.
        FrameView view = default;
        ResolveFrameLens(console, ref view);
        ResolveMapScene(ref view);
        ResolvePostProcess(ref view);
        ResolveProjection(state, ref view);
        ComposeFrameEye(state, console, ref view);

        ICharacterPresenter? casterBodies = BeginCharacterFrame(view.TickBlend, view.SmoothPresentation);
        view.SceneTarget = DescribeSceneTarget(view.Resolution, view.WantsTemporal);
        ResolveFrameSun(ref view);
        FitSunShadows(state, ref view);

        VolumetricFogSettings medium = ResolveFogMedium(state, out bool wantsFog, out int fogMission);

        _gi.PrepareSceneLighting(state, view.Eye, view.Light);

        ApplyFrameSurfaces(state, view, medium, wantsFog, fogMission);

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
        bool cascadesRead = view.ShadowSettings.SunLit && !_device.SunShadowTraced;
        RenderShadowMaps(state, view.CascadeCount, cascadesRead, view.Eye, casterBodies);

        bool captured = OpenSceneCapture(view.SceneTarget, view.Light, view.ViewSunDirection, view.MapScene, view.Resolution);

        // Whether the eye is under a roof, which the weather and the HUD read. A title's own screen over
        // the view (I.G.I. 2's map computer turns the rain off) keeps the weather off as a building's
        // shell does.
        bool cameraIndoors = IsCameraInsideBuilding(view.Eye.Position) || TitlePlayer?.IsWeatherHidden == true;

        DrawSceneGeometry(
            view.MapScene, captured, view.Eye, view.Projection, view.VerticalFieldOfView, view.AspectRatio, cascadesRead,
            view.CascadeCount, view.SmoothPresentation, view.TickBlend);

        ResolveFrameHud(ref view);

        DrawCharacters(view.Eye, view.Projection, view.TickBlend, view.ThirdPersonBody);

        _gi.LightScene(state, view.Eye, captured, view.SceneTarget, view.UnjitteredProjection);

        SubmitLocalLights(
            state, captured, view.Eye, view.UnjitteredProjection, view.FlashlightShown, view.FlashlightBody, view.MapScene,
            view.SceneTarget);

        DrawForwardPass(
            console, captured, view.Eye, view.Projection, view.MapScene, view.Resolution, view.SceneTarget, view.SmoothPresentation,
            view.TickBlend, cameraIndoors, view.RemasteredOptics);

        float? binocularRangeMetres = DrawFirstPersonView(
            view.Eye, view.ShowViewModel, view.Sight, view.SmoothPresentation, view.TickBlend);

        DumpScreenGatherOnce(state, captured, view.Eye, view.UnjitteredProjection);

        DrawScreenOverlays(state, captured, view, binocularRangeMetres, cameraIndoors);
    }

    /// <summary>Gathers the screen-space indirect from the surface cache and dumps it, once, when a capture asks for it.</summary>
    private void DumpScreenGatherOnce(FrameLoopState state, bool captured, Camera3D eye, Matrix4f unjitteredProjection)
    {
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
                atmosphereSun = _timeOfDay.DayNight.SkySunColour * (atmosphereScale * nightFeed);
                atmosphereSunDirection = _timeOfDay.DayNight.SunDirection;
                // The moon above the weather, whole: the sky's cloud deck is lit by all of it and
                // passes on its own share (see SkyView's deck transmission), while the clear air
                // scatters only r_night_sky's. Knocked down or veiled here as well, the deck took
                // the cloud's toll twice and a rainy night came out black.
                atmosphereMoon = _timeOfDay.DayNight.MoonColour * (RenderCVars.Moonlight.Value * atmosphereScale);
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
                _timeOfDay.FrameNightSky(),
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
}
