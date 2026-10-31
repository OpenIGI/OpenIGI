using System.Diagnostics;
using OpenIGI.Core.Configuration.Presets;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Host.DevTools;
using OpenIGI.Host.Services;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Lighting;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    // The title's first-person view, what its player has put in the world, and its viewpoint; each made the
    // first time a frame asks for it.
    private IMissionFirstPersonView? _firstPerson;
    private IMissionWorldView? _worldView;
    private IMissionViewpoint? _viewpoint;

    /// <summary>Draws the level's opaque geometry: the sky, the world's objects, the ground, the foliage and the props in flight.</summary>
    /// <summary>Gets the ground, unless the title's culling leaves the world outside its buildings out of this frame.</summary>
    /// <remarks>The cascades take it from here too, so a ground the eye cannot see out to casts nothing into the room.</remarks>
    private ITerrainPresenter? OutsideTerrain => _world?.Visibility?.IsOutsideVisible == false ? null : _terrain;

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
        // Every model whole, under every preset; the original's levels of detail only when asked for (r_model_lod).
        if (_world is { } detailedWorld)
        {
            detailedWorld.Lod = FrameCVars.ModelLod.Value ? _modelLod : null;

            // The original's face groups on its own look: the opaque ones without an alpha test, the sorted ones blended after.
            detailedWorld.FaceGroupBlending = GraphicsPresetRegistry.Default.Current == GraphicsPreset.Vanilla;
        }

        bool retailFog = FogCVars.RetailFog.Value;
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
            _sky?.SetSunTint(TimeOfDayLighting.SkyWarmthTint((_timeOfDay.EffectiveSun ?? _timeOfDay.Sun)?.Direction.Z ?? 1f));

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

            // The original draws its whole sky unfogged: the dome (SkyDome_Draw) and the cloud layers' strips, whose QVertex
            // states turn fog off (Direct3DQVertex_SetGenericState). Added clouds fogged here dimmed toward black.
            bool unfoggedSky = retailFog && GraphicsPresetRegistry.Default.Current == GraphicsPreset.Vanilla;
            if (unfoggedSky)
            {
                _device.SetFog(SceneFog.None);
            }

            _sky?.Draw(new SkyFrame(eye, projection, _loop.TimeSource.Milliseconds / 1000f));
            if (unfoggedSky)
            {
                _device.SetFog(RetailAir(_globalLight.ObjectFog));
            }

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
        _world.Draw(_scene, eye, projection, new WorldDrawOptions(OcclusionCulling: GeometryCVars.WorldOcclusion.Value, FromEye: true));
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

        // Nothing outside the buildings is drawn while the title's culling says the eye cannot see out: I.G.I. 2 skips its
        // terrain and its forests outright while the camera is in a room no window or doorway shows the outside through.
        bool outsideVisible = _world.Visibility?.IsOutsideVisible != false;

        // ...and seen through the outside's windows, I.G.I. 2 clips them to the frustum enclosing those windows.
        ViewClip? outsideClip = _world.Visibility?.OutsideClip;
        if (outsideClip is not null)
        {
            _device.SetDrawClip(outsideClip);
        }

        OutsideTerrain?.Draw(new TerrainFrame(
            eye,
            projection,
            verticalFieldOfView,
            aspectRatio,

            // -viewdist still wins when it is given, so an existing diagnostic command line keeps
            // meaning what it did; otherwise the setting decides.
            _options.TerrainViewDistance > 0 ? _options.TerrainViewDistance : GeometryCVars.TerrainDetail.Value,

            // The original's own picture -- drawn straight to the back buffer, with no G-buffer to
            // keep the passes apart -- draws the ground as the original does, chunk by chunk with
            // its lightmap multiplied over it. Decided by the frame rather than by the preset's
            // name: a vanilla player who changes one setting reads as Custom, and used to lose the
            // ground's light with it.
            captured,
            GeometryCVars.TerrainOcclusion.Value,

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
        if (outsideVisible)
        {
            // The original fogs its trees in the objects' colour at four fifths of the ground's density
            // (ForestRender_SetGenericState_*), on its own look.
            bool retailForest = GraphicsPresetRegistry.Default.Current == GraphicsPreset.Vanilla;
            if (retailForest && retailFog && _forest is not null)
            {
                _device.SetFog(RetailAir(_globalLight.ObjectFog) with
                {
                    DensityPerMetre = RetailAir(_globalLight.TerrainFog).DensityPerMetre * 0.8f,
                });
            }

            _forest?.Draw(new ForestFrame(
                eye,
                MathF.Tan(verticalFieldOfView * 0.5f) * aspectRatio,
                MathF.Tan(verticalFieldOfView * 0.5f),
                retailForest,
                _world.Shading.Sun?.Direction ?? Vector3f.Zero));
            if (retailForest && retailFog && _forest is not null)
            {
                _device.SetFog(RetailAir(_globalLight.ObjectFog));
            }
        }

        if (outsideClip is not null)
        {
            _device.SetDrawClip(null);
        }
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
        // never swaps in the view-model block for them.
        _soldiers?.ObserveView(eye, projection);
        _soldiers?.Draw(eye, projection);

        // A co-op look being chosen, standing in the level behind the menu.
        _previewBodies?.ObserveView(eye, projection);
        _previewBodies?.DrawPreviewBody(eye, projection);

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
        if (_marks is not null && CombatEffectCVars.BulletMarks.Value)
        {
            WorldView?.DrawMarks(_marks, eye, projection, _world.DrawPoses);
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
            _world.Draw(_scene, eye, projection, new WorldDrawOptions(GlassOnly: true, FromEye: true));

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
        _world.Draw(_scene, eye, projection, new WorldDrawOptions(RotorsOnly: true, FromEye: true));
        if (_world.FaceGroupBlending)
        {
            _world.Draw(_scene, eye, projection, new WorldDrawOptions(FromEye: true, BlendedOnly: true));
        }

        // The level's glazing, over a picture the lighting has finished. It has to follow the
        // opaque world for the ordinary reason anything blended does, and it is a pass of its own
        // rather than part of the rotor pass above because the two share nothing: a rotor is a
        // whole placement that blends, and a pane is half of a texture on a placement whose other
        // half was drawn into the G-buffer.
        _world.Draw(_scene, eye, projection, new WorldDrawOptions(GlassOnly: true, FromEye: true));
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

        // And the level's own bodies' marks on the ground, on the original's look (Shadow_MagicObjEnterHandler).
        if (_world.FaceGroupBlending)
        {
            _soldiers?.DrawGroundShadows(eye, projection);
        }

        // The rounds' tracers (one pass in eleven, and every pass through a wall): the original's thin
        // unfogged line under the vanilla preset, the remaster's glowing streak under every other. Sized against
        // the image they are rasterised into.
        if (_soldiers is { } tracerSoldiers)
        {
            tracerSoldiers.DrawGunShotTracers(
                eye,
                projection,
                captured ? resolution.RenderHeight : _window.Height,
                GraphicsPresetRegistry.Default.Current == GraphicsPreset.Vanilla);
        }

        // The soldiers' muzzle flames, for the same reason: drawn from the soldier pass they
        // landed in the G-buffer and shaded as scenery -- the one-frame black square at a
        // firing guard's muzzle. Light draws over the lit scene.
        (_soldiers ?? _menuBackdrop.Soldiers)?.DrawMuzzleFlashes(eye, projection);

        // And another game's: the title's own models and those its presenters stand in the level.
        DrawTitleMuzzleFlashes(eye, projection);
        foreach (ITaskPresenter flashPresenter in _taskPresenters)
        {
            flashPresenter.DrawMuzzleFlashes(eye, projection);
        }

        // The world's effects over the lit scene: the impact bursts, the original's own dust, chips and blood,
        // the smoke trails and columns, and the fireball over all of it as light.
        _effects.Draw(eye, projection, smoothPresentation ? tickBlend : 1f);

        // And another game's 2-D particles -- its blood, its smoke -- in the engine both titles share, with that game's sprites.
        DrawTitleParticles(eye, projection, smoothPresentation ? tickBlend : 1f);
        foreach (ITaskPresenter particlePresenter in _taskPresenters)
        {
            particlePresenter.DrawParticles(eye, projection, smoothPresentation ? tickBlend : 1f);
        }

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
            DirectionalLightSettings stormSun = _timeOfDay.EffectiveSun ?? default;
            Vector3f stormSunLight =
                stormSun.Colour * LightingCVars.SunScale.Value * LightingCVars.LightScale.Value;

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
                _timeOfDay.EffectiveSun is null ? Vector3f.UnitZ : stormSun.Direction,
                stormSunLight,
                _timeOfDay.AmbientSkyLight() + (stormSunLight * 0.2f),
                _loop.TimeSource.Milliseconds / 1000f,
                _stormLamps.AsSpan(0, _stormLampCount),

                // A display level, taken back through the frame's exposure into the scene's
                // units, so the drops never sink below a faint grey whatever the night.
                RainVeilDisplay / MathF.Max(_device.SceneExposure, 1e-3f));
        }
        else if (GraphicsPresetRegistry.Default.Current == GraphicsPreset.Vanilla && _retailWeather is { } retailWeather)
        {
            // The original's look draws the level's weather as the original does, stopping on whatever is above the ground.
            retailWeather.Draw(new WeatherFrame(eye, projection, _window.Height, _mission.EffectWalls, _loop.Clock.TickCount));
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
        if (_snow is { IsWeatherEnabled: true } snow
            && !(GraphicsPresetRegistry.Default.Current == GraphicsPreset.Vanilla && _retailWeather is not null))
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

        // An alarm light's turning reflector throws its red flare at the eye (0x439F10), with the camera lamps' sprite.
        if (_mission.AlarmSpeakers is { Reflectors.Count: > 0 } lights)
        {
            _cameraStatusLights?.DrawGlows(
                lights.ReflectorFlares(eye.Position * WorldScene.UnitsPerMetre, smoothPresentation ? tickBlend : 1f),
                eye,
                projection);
        }

        // The through-wall glow over the enemies the binocular scan marked, in the same FORWARD
        // pass and for the same reason as the inspector selection below: submitted from the world
        // pass it would be lit and fogged into a smear. Here it composites over the finished
        // scene and is carried into the resolve, so the lens and bloom pick it up. Only while the
        // remastered optics are up.
        if (remasteredOptics
            && HudCVars.BinocularOutline.Value
            && BinocularsOpen)
        {
            // Whom a title's own player's binoculars picked out, highlighted by what draws them.
            DrawTitleBinocularMarks(eye);

            _soldiers?.DrawBinocularOutlines(eye, radiusPixels: 8f);

            // Another game's humans the scan marked, glowed by what draws them.
            if (_soldiers?.BinocularMarkedOthers is { Count: > 0 } marked)
            {
                foreach (ITaskPresenter taskPresenter in _taskPresenters)
                {
                    taskPresenter.DrawOutlines(marked, eye, radiusPixels: 8f);
                }
            }
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
                _loadedMissionForWeapon,
                _world?.Shading.Sun)) ?? default;
        if (!drawn.DrewRig)
        {
            DrawTitleFirstPerson(eye);
        }

        return drawn.BinocularRangeMetres;
    }
}
