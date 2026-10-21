using OpenIGI.Game.Hosting;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Threading;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Level;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Host.Infrastructure;
using OpenIGI.Host.Lighting;
using OpenIGI.Host.Menu;
using OpenIGI.Host.Services;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.Scripting.Runtime;
using OpenIGI.Host.Audio;
using OpenIGI.Host.Configuration;
using System.Diagnostics;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    /// <summary>The level's baked lighting: the probe field, the path-traced lightmaps and the distance fields, started.</summary>
    private void MissionLightBakes(MissionBuild b)
    {
        if (_scene is null)
        {
            throw MissingSystem(MissionSystemNames.Scene);
        }

        LevelTask levelRoot = b.LevelRoot;
        int id = b.Id;
        bool promoted = b.Promoted;
        ObjectCollisionIndex solids = b.Solids!;

        // The level's sun has to be known BEFORE the bakes below, not after: the Dirlight was read
        // some hundreds of lines further down (the `if (!promoted)` block), so every probe field and
        // path-traced lightmap was baked with `_timeOfDay.Sun` still null -- a zero sun direction and a fallback
        // sky -- which is exactly why the baked GI came out dim. Read it here, unless a promoted
        // backdrop already carried one in. The later read stays and simply re-confirms the same value.
        if (_timeOfDay.Sun is null && DirectionalLightSettings.TryRead(levelRoot, out DirectionalLightSettings bakeSun))
        {
            _timeOfDay.Sun = bakeSun;
        }

        // The probe field, traced against the level's collision on a WORKER. Inline it blocks for
        // about three seconds in Release and half a minute in Debug, and by this point the window
        // and its GL context are live -- so the process stops answering, the GPU is handed no
        // work for the whole of it, and Windows' watchdog takes the context away. Loading mission
        // 1 from the menu died that way, with exit 0xCFFFFFFF. The main thread carries on loading
        // and presenting; IrradianceProbeBake hands the volume over when it is ready.
        //
        // It traces with every door SHUT, which is two things at once. A door standing open would
        // otherwise be baked as a hole in the wall it belongs to, and the field is static, so the
        // hole would outlast the door -- that is why this used to have to run before the doors
        // did. And the shut cast is the only one that reads no state the game mutates, which is
        // what makes running it alongside a live level safe rather than merely early.
        //
        // A level with no Dirlight has no sun to trace towards, so the field is sky and bounce
        // alone -- which is what such a level looks like anyway.
        // The shared bake inputs -- sun and a glass-passing cast -- are built when EITHER the probe
        // field or the path-traced lightmaps are wanted; each bake below is then guarded on its own
        // cvar, so r_lightmap works at a preset (ultra) that runs no probe field.
        if (GlobalIlluminationCVars.GlobalIllumination.Value || GlobalIlluminationCVars.BakedLightmaps.Value > 0)
        {
            Vector3f bakeSunDirection = _timeOfDay.Sun is { } declared && !declared.IsBelowHorizon
                ? declared.Direction
                : default;

            WorldRayCast bakeCast = _mission.Collision.DaylightCast
                ?? throw new InvalidOperationException("A mission whose world is built has its collision.");

            // Dev verification for the path-traced lightmapper (phases 1-2): when IGI_BAKELM_DIR is
            // set, enumerate the lightmapped texels, bake direct sun + sky, and dump the largest few
            // pages' normals and radiance so the texel map and the direct pass can be checked by eye.
            // Off unless the env var is set.
            if (Environment.GetEnvironmentVariable("IGI_BAKELM_DIR") is { Length: > 0 } lightmapDir
                && _scene is { } lightmapScene)
            {
                DumpLightmapVerification(lightmapScene, bakeCast, bakeSunDirection, lightmapDir);
            }

            // Path-traced object lightmaps (r_lightmap): re-bake the level's static object lighting
            // -- direct sun, sky and multi-bounce indirect -- and swap the pages in where the shipped
            // .olm ones would be sampled. Cached to disk like the probe field, so only a mission
            // without a bake pays to trace it, and traced on a worker so the load does not stall.
            // The sky is the daytime skylight (r_sky_light), not the dim raw ambient, so sky-lit
            // surfaces read closer to what the artists painted. r_lightmap 2 discards the shipped
            // lightmaps entirely; 1 keeps them where the bake produced nothing.
            if (_scene is { } bakeScene)
            {
                _baked.Start(id, bakeScene, _world, _resources, _timeOfDay.Sun, bakeCast);
            }
        }

        // The distance fields, on a worker beside the probe bake. Tracing every distinct
        // model is arithmetic and moves off the render thread freely; only the upload cannot,
        // because a GL texture belongs to the context that made it.
        //
        // Started here rather than built here: _gi.EnsureDistanceFields is the same call the render
        // loop makes every frame, so a level begins its bake at load and a player who switches the
        // lighting model on later gets one started for them. See GameHost.RealtimeGi.cs.
        // The outgoing level's cards go first, before the incoming field is adopted. Adopting a
        // field is one of the two moments that can complete the pair and converge the cache, and
        // with the old cards still standing it would converge THOSE against the new level's field
        // -- then latch, so the real convergence a few hundred lines below was skipped and the
        // level opened on the ramp this is all meant to remove. It failed silently, because a
        // converged-looking cache and a stale one differ only in the picture.
        // Not on a promotion: the backdrop already carries this very mission's cards and field
        // -- built or baking behind the menu, which is what lets the menu show the lit level --
        // and discarding them here would throw the field away and bake it again in front of
        // the player.
        if (!promoted)
        {
            _gi.DiscardSurfaceCache();
            _gi.DiscardDistanceFields();
        }

        _gi.EnsureDistanceFields();

        // A capture must not race the worker, for the reason the probe bake records: a
        // reference image has to either contain the field or not, never whichever frame
        // the worker happened to reach.
        if (_options.Lockstep)
        {
            _gi.WaitForFieldBake();
        }

        // Every distinct model's surface is captured into the card atlas, but deliberately NOT
        // here -- even though this is where the geometry first exists. Lighting the cache needs
        // the level's own sun, which is not read until further down this load, and lighting the
        // cards before that gave every page a zero sun direction and a picture of pure sky, which
        // is exactly what the first run produced. _gi.EnsureSurfaceCache captures and lights in one
        // go, so it is called once the sun is known; the outgoing cards were dropped above.

        _baked.Show();
        ApplyColourLookup(_options.StartMission);
        EndStage("starting the light bakes");
    }

    /// <summary>The terrain, sky, sun, grade and camera; the lamps and the surface cache; what impact effects trace against; the player's state reset.</summary>
    private void MissionPicture(MissionBuild b)
    {
        if (_scene is null)
        {
            throw MissingSystem(MissionSystemNames.Scene);
        }

        LoadedMission loaded = b.Loaded;
        LevelTask levelRoot = b.LevelRoot;
        int id = b.Id;
        bool promoted = b.Promoted;
        WorldScene scene = b.Scene!;

        // Everything from here to the free camera is what makes the level a PICTURE, and a promoted
        // backdrop is already that picture -- AdoptPreview built the terrain (with its ground detail
        // derived on the worker, which is most of the cost), the sky, the shadow pass, the clear colour,
        // the grade and the camera, and the menu has been drawing them for as long as the player has been
        // reading the briefing. Rebuilding them here would throw away the one thing this whole path
        // exists to reuse, and would do it on the render thread.
        if (!promoted)
        {
            // A level whose terrain files are not live data — level 14 is exactly that — draws its objects
            // over nothing rather than following offsets into whatever the geometry blob happens to hold.
            // The surface comes from the level's own TextureModifier tasks, so it needs the loaded tree.
            // A title whose ground it reads itself answers CreateGround; one whose ground the host
            // already built as a TerrainSurface answers CreateTerrain. Asking in that order is what lets
            // the two coexist without either pretending to the other's shape.
            _terrain?.Dispose();
            _terrain = _presentation.Presenters.Create<GroundPresentationRequest, ITerrainPresenter>(
                new GroundPresentationRequest(_device, levelRoot, _resources, _logger));

            _terrain ??= loaded.Terrain is { IsUsable: true } usable
                ? _presentation.Presenters.Create<TerrainPresentationRequest, ITerrainPresenter>(new TerrainPresentationRequest(
                    _device, TerrainSurface.Build(usable, levelRoot, _logger), _logger, _detailMaps, null))
                : null;

            // The sky is script data on the level's task tree; its fog colour is also what the frame
            // clears to, so the horizon meets sky-coloured backing rather than black.
            _sky?.Dispose();
            _sky = _presentation.Presenters.Create<SkyPresentationRequest, ISkyPresenter>(
                new SkyPresentationRequest(_device, levelRoot, _fileSystem, _resources, FarPlane, _logger));
            _shadowPass = new ShadowCasterPass(_device);

            ApplyLevelConstants(levelRoot);

            if (DirectionalLightSettings.TryRead(levelRoot, out DirectionalLightSettings readSun))
            {
                _timeOfDay.Sun = readSun;
                _timeOfDay.BoundSun = DayNightBinding.Bind(readSun, _deferredLighting);

                // At the hour the level's authors placed the sun for, not a fixed one: the cycle lands
                // back on the authored direction only there.
                _timeOfDay.DayNight.ResetForLevel(_timeOfDay.BoundSun, ExposureCVars.TimeOfDay.Value);

                // After the reset, which clears it: a level that authors its own night hands the cycle
                // that night to cross to, instead of the faint one the cycle invents for a level that
                // does not have one. See DayNightCycle.SetNightLighting.
                if (_deferredLighting is { Night: { } boundNight } authoredNight)
                {
                    _timeOfDay.DayNight.SetNightLighting(
                        boundNight.SunIsMoon ? readSun.Colour : null,
                        authoredNight.Ambient);
                }
                _logger.Information(
                    "Light",
                    FormattableString.Invariant(
                        $"Sun at {MathF.Asin(Math.Clamp(readSun.Direction.Z, -1f, 1f)) * 180f / MathF.PI:F1} degrees ")
                    + FormattableString.Invariant(
                        $"elevation, time {readSun.TimeOfDay:F0}, colour ({readSun.Colour.X:F2}, {readSun.Colour.Y:F2}, {readSun.Colour.Z:F2}), ")
                    + FormattableString.Invariant(
                        $"ambient ({readSun.Ambient.X:F2}, {readSun.Ambient.Y:F2}, {readSun.Ambient.Z:F2})."));

                if (readSun.IsBelowHorizon)
                {
                    _logger.Warning("Light", "This level's authored sun is below the horizon.");
                }
            }
            else
            {
                _timeOfDay.BoundSun = null;
                _timeOfDay.DayNight.ResetForLevel(null, ExposureCVars.TimeOfDay.Value);
                _logger.Information("Light", "The level declares no Dirlight; the modern path will light it with a default sun.");
            }

            // A title may decline a sky entirely -- the factory returns null when the level declares
            // none -- so the clear colour keeps whatever it had rather than reading off nothing.
            if (_sky is { HasSky: true } sky)
            {
                _scheduler.ClearColour = new Bgra32(
                    (byte)Math.Clamp((int)(sky.FogColour.Z * 255f), 0, 255),
                    (byte)Math.Clamp((int)(sky.FogColour.Y * 255f), 0, 255),
                    (byte)Math.Clamp((int)(sky.FogColour.X * 255f), 0, 255),
                    255);
            }

            // The mission's own grade. It used to be applied with `_options.StartMission` from inside the
            // nav-graph block, which was wrong twice over: -mission happens to set that option so the grade
            // looked right under the flag that was being tested with, but a mission started from the shell
            // leaves it 0 and got mission 0's grade, and a level shipping no nav graph got no grade at all.
            // The backdrop always applied the right one (AdoptPreview passes the preview's id), so the two
            // disagreed and the picture changed colour the instant Play was pressed.
            ApplyColourLookup(id);

            // Open on the middle of what was placed, backed off and looking down slightly, so the first
            // frame shows the level rather than the inside of whichever object happens to be at the origin.
            // -campos overrides that with an exact spot, which is how a bug at a known position is looked at.
            _camera = _options.CameraPosition is { } at
                ? new FreeCamera(new Vector3d(at.X, at.Y, at.Z), _options.CameraYaw, _options.CameraPitch)
                : FreeCameraFactory.Create(scene, _options.CameraYaw, _options.CameraPitch, _options.CameraHeight);
        }

        // Now that the sun is known. The pages hold unlit albedo by design, so this is the only thing
        // that puts light into the cache -- and it runs once, because this game's sun is a fixed
        // direction per mission, which is the bargain the whole surface cache is built on.
        // The lamps this level implies but never declares, worked out once and used in three places: as
        // EMISSION on the fixture geometry itself, so a lamp looks like the thing doing the lighting; as
        // real lights in the deferred pass, so a windowless room has a source at all; and as emitters the
        // surface cache bounces off, where there is one. All three matter together -- a room lit from a
        // fixture that does not glow reads as lit by nothing, and a fixture that glows without lighting
        // reads as a decal.
        //
        // Above the surface cache and not inside it. The cache is an ultra-only structure and none of
        // this depends on it: the glow is a material and the lights are punctual, so binding the whole
        // conclusion to whether the cards were captured would have switched a level's lamps off for
        // anyone running ultra with r_surface_cache 0.
        //
        // A promoted backdrop surveyed its lamps on the preview worker and has been showing them for as
        // long as the player was reading the briefing; surveying again here was the largest single cost
        // of pressing Play, and it would have produced the same answer.
        // A cold start has a NEW world renderer that has to be handed them either way.
        if (_gi.LampsMissionId != id || !promoted)
        {
            _gi.ApplyLamps(
                LocalLightCVars.GlobalIlluminationLamps.Value != 0
                    ? EmissiveLightProposer.Propose(
                        _scene, _resources, _lampManifest, id, ActiveTitle.Id, _logger)
                    : EmissiveLightProposer.Proposal.None,
                id);
        }

        // The surfaces, now that the sun and the fixtures they are lit by are both known. Captures the
        // atlas and lights it in one step -- see GameHost.RealtimeGi.cs, which is also what the render
        // loop calls when the lighting model is switched on mid-level.
        _gi.EnsureSurfaceCache();

        // The walk has already spoken for a title that keeps its ground in the level; for the one that
        // keeps it in the mission, the terrain IS the ground.
        _mission.Ground ??= _mission.Terrain;
        _mission.ForgetPlayer();
        _titleFeatures = null;
        _titleHud?.Dispose();
        _titleHud = null;
        _playerMode = false;

        // A fresh mission is a fresh life: the last one's death, its red wash and its death camera do not
        // carry over.
        _mission.StartFreshLife();
        Viewpoint?.ResetDeathView();
        _flashlight.Reset();

        EndStage("the terrain, sky, sun, grade and camera");
    }

    /// <summary>The forest and the water, the menu's renderer and backdrop dropped, and the mission reported built.</summary>
    private void MissionAdornments(MissionBuild b)
    {
        bool promoted = b.Promoted;
        LoadedMission loaded = b.Loaded;
        LevelTask levelRoot = b.LevelRoot;

        // The things standing in the world rather than being it: planted foliage, and water. Both are
        // handed the mission's model lookup, which is the one thing the ground and the sky do not need --
        // a forest plants instances of a model, and the model lives in a pack rather than in the level's
        // own files.
        //
        // Outside the block above, which exists to keep what the backdrop already built. The backdrop
        // builds the ground and the sky and stops there, so a promoted mission that skipped these would
        // be a level with no trees and no water in it -- which is what pressing Play produced while this
        // sat with the rest.
        ScenePresentationRequest adornments = new(
            _device,
            levelRoot,
            _resources,
            name => loaded.TryGetModel(name, out MefModel? model) ? model : null,
            (name, slot) => loaded.TryGetTextureName(name, slot, out string texture) ? texture : null,
            _logger);

        _forest?.Dispose();
        _forest = _presentation.Presenters.Create<ScenePresentationRequest, IForestPresenter>(adornments);

        _water?.Dispose();
        _water = _presentation.Presenters.Create<ScenePresentationRequest, IWaterPresenter>(adornments);
        MakeTaskPresenters(levelRoot);

        // The menus are done; without closing their screens the last one would keep drawing over the world.
        _retailMenus?.CloseScreens();

        // The menu's backdrop is not needed once a real mission is up. On a cold start that releases the
        // previewed level's packs outright. On a promotion it releases nothing: the preview IS this
        // mission now, and its registry and file system are reachable through `_resources` and through
        // the renderers that were built against them — dropping this reference only says the level has
        // stopped being scenery.
        _menuBackdrop.Clear();
        _arming = null;

        // Built. What the hold is waiting on from here is the lighting, not the level.
        NotifyMissionBuilt();
        _logger.Information("Mission", $"{(promoted ? "Promoted" : "Loaded")} {loaded}.");
    }

    // The presenters the title registered for the level as a whole, then for particular task types, one for each such task.
    private readonly List<ITaskPresenter> _taskPresenters = [];

    /// <summary>Makes a presenter for every task of the level whose type has one registered.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    private void MakeTaskPresenters(LevelTask levelRoot)
    {
        ReleaseTaskPresenters();
        PresenterRegistry presenters = _presentation.Presenters;
        if (presenters.TaskTypeCount == 0 && presenters.LevelPresenterCount == 0)
        {
            return;
        }

        TaskPresenterRequest request = new(_device, _resources, levelRoot, _logger);
        _taskPresenters.AddRange(presenters.CreateForLevel(request));
        foreach (GameTask task in levelRoot.Descendants())
        {
            if (presenters.CreateForTask(task, request) is { } presenter)
            {
                _taskPresenters.Add(presenter);
            }
        }

        _logger.Information("Mission", $"{_taskPresenters.Count} task presenter(s) made.");
    }

    // Frees the presenters made for the level's tasks.
    private void ReleaseTaskPresenters()
    {
        foreach (ITaskPresenter presenter in _taskPresenters)
        {
            presenter.Dispose();
        }

        _taskPresenters.Clear();
    }
}
