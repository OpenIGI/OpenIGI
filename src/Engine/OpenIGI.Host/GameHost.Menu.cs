using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.World;
using OpenIGI.Host.Menu;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Presentation.Lighting;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    /// <summary>
    /// Makes a preview built on a worker the live backdrop, doing on the render thread only what has to
    /// happen there.
    /// </summary>
    /// <param name="preview">The built preview, from <see cref="MissionPreviewLoader"/>.</param>
    /// <remarks>
    /// <para>
    /// Everything a backdrop is made of — the packs, the task tree, the scene, the collision index, the
    /// terrain surface, the level's sun, its baked probe field — arrived already built. What is left is the
    /// part that cannot leave this thread: a GL texture, buffer and framebuffer all belong to the context
    /// that made them, so the three renderers are constructed here. That is a handful of milliseconds
    /// against the several hundred the whole load used to cost the frame.
    /// </para>
    /// <para>
    /// The renderers are built against the preview's OWN resource registry and file system, because a
    /// <see cref="IWorldPresenter"/> resolves models and textures through the registry it was given for as
    /// long as it lives, and the sky reads through the file system whose <c>MISSION:</c> alias points at
    /// this level. Holding the preview keeps both alive; dropping it drops the whole context.
    /// </para>
    /// <para>
    /// A backdrop is looked at, never played, so nothing a played mission needs is set up: no nav graphs,
    /// no player, no weapons, no HUD. Drawn lite (<see cref="BackdropLite"/>, the default) it has no
    /// soldiers, surface cache or distance field either -- a render pass ending in a device wait and an
    /// upload that landed as the swap's stall -- and Play builds them behind the going-in sequence.
    /// </para>
    /// </remarks>
    private void AdoptPreview(MissionPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var handover = System.Diagnostics.Stopwatch.StartNew();

        // Where the handover's time went, stage by stage, for the line logged at the end. The total alone
        // said a swap was slow without saying what to do about it.
        var stages = new System.Text.StringBuilder();
        long lapStart = 0;
        void Lap(string stage)
        {
            long now = handover.ElapsedMilliseconds;
            stages.Append(System.Globalization.CultureInfo.InvariantCulture, $"{(stages.Length == 0 ? "" : ", ")}{stage} {now - lapStart}");
            lapStart = now;
        }

        _scene = preview.Scene;
        _mission.Solids = preview.Solids;
        _mission.Spins = preview.Spins;
        _timeOfDay.Sun = preview.Sun;

        _mission.Terrain = preview.Mission.Terrain is { IsUsable: true } standable ? standable : null;

        // A title whose ground is inside the level hands it over with the walk; one whose ground came
        // with the mission stands on the terrain itself.
        _mission.Ground = preview.Ground ?? _mission.Terrain;

        // Nobody stands in a backdrop, and the previous mission's player must not be left holding a scene
        // that has just been replaced.
        _mission.ForgetPlayer();
        _titleFeatures = null;
        _playerMode = false;

        // The outgoing backdrop's renderer owns a level's worth of GL textures, and a backdrop is now
        // replaced every time the player settles on a different mission rather than once per game. Dropping
        // the reference without disposing would leak a level's textures per row of the list.
        _world?.Dispose();
        _world = _presentation.Presenters.Create<WorldPresentationRequest, IWorldPresenter>(new WorldPresentationRequest(_device, preview.Resources, _logger));
        if (_world is not null)
        {
            _world.Materials = _materials;
            _world.DetailMaps = _detailMaps;

            // The backdrop turns its fans too, and a promoted mission keeps this very renderer, so
            // setting it here is what makes the mission behave the same whichever way it was entered.
            _world.Spins = _mission.Spins;
        }

        // Decoded on the pool while this backdrop was being built, so the capture below uploads rather
        // than decodes. Null on a preview built before there was a cache to fill, which the renderer
        // handles by decoding as it always did.
        if (_world is not null)
        {
            _world.Decoded = preview.Decoded;
        }

        // Held because the renderers read through the preview's own resource registry and file system for
        // as long as they live; letting it go would pull the packs out from under them.
        _menuBackdrop.Show(preview);

        // The level is a backdrop from here, so the lite profile decides what below is built at all.
        UpdateBackdropProfile();
        Lap("world");

        // Both factories, and in the mission path's own order. A backdrop that asked only for the second
        // gave a title whose ground it does not describe no ground at all -- and because a promoted
        // backdrop IS the mission, keeping its terrain rather than rebuilding it, the mission then had
        // none either. That is why I.G.I. 2 drew its ground from -mission and not from the menu: the two
        // routes were asking different questions of the same title.
        _terrain?.Dispose();
        _terrain = _presentation.Presenters.Create<GroundPresentationRequest, ITerrainPresenter>(
            new GroundPresentationRequest(_device, preview.LevelRoot, preview.Resources, _logger));

        _terrain ??= preview.TerrainSurface is { } surface
            ? _presentation.Presenters.Create<TerrainPresentationRequest, ITerrainPresenter>(new TerrainPresentationRequest(
                _device, surface, _logger, _detailMaps, preview.GroundDetail))
            : null;

        // Uploaded with the tiles; the preview held a full-size map per tile for as long as it lived.
        _menuBackdrop.Show(preview with { GroundDetail = [] });
        Lap("terrain");

        // The mission's soldiers, built while the player is still reading the briefing. They are not drawn
        // -- a backdrop shows a place, not the guards standing in it -- but they ARE given to the shadow
        // pass, and that is the point.
        //
        // Measured: the first frame that shows a skinned mesh to a device which has spent the menu drawing
        // nothing but static geometry costs about three hundred milliseconds, almost all of it inside the
        // shadow pass. It is not the cost of the soldiers -- a cold `-mission 1` start, where they are
        // there from the first frame, peaks at three milliseconds. It is the driver re-specialising a
        // pipeline it had already settled on for one vertex layout when a second layout arrives late.
        // Nothing about loading them earlier avoids that; only DRAWING them earlier does. The shadow map
        // is an offscreen target that is overwritten every frame, so a cast nobody can see pays the cost
        // where nobody can feel it.
        //
        // Not for a lite backdrop, which draws no shadow to give them to: there they were only a level's
        // worth of soldier meshes and textures decoded and uploaded on this thread, every swap. Play then
        // loads them the way a cold start does, and the pipeline's first sight of a skinned mesh falls
        // under the warm-up's hold instead.
        _menuBackdrop.ReleaseSoldiers();
        if (!BackdropLite)
        {
            ISoldierBodies? soldiers = _presentation.Presenters.Create<SoldierPresentationRequest, ISoldierBodies>(
                new SoldierPresentationRequest(_device, preview.Resources, _logger, _mission.MagicObjects, _detailMaps));
            _menuBackdrop.ArmSoldiers(soldiers);
            soldiers?.Load(
                preview.Mission,
                preview.LevelRoot,
                preview.Mission.Terrain is { IsUsable: true } standing ? standing : null,
                preview.Solids.Caster());
        }

        Lap("soldiers");

        // And the level's own residency, walked a little at a time on the frames that follow. The orbit
        // resolves only what it happens to look at, so without this the frame that moves the camera to the
        // player's spawn uploads everything else at once.
        _arming = _world is null
            ? null
            : new MissionArming(preview.MissionId, preview.Scene, _world, _logger);

        // The preview's OWN registry, not the session's. The two halves of this request have to describe
        // the same installation: preview.FileSystem carries the MISSION alias the backdrop's level bound,
        // and the session registry is reading through a file system that has no such alias while the menu
        // is up. Project I.G.I.'s sky never noticed because it reads its layers through the file system
        // directly; the sequel's resolves them as resources, so it asked for MISSION:... and got a fatal
        // "device not present" -- which took the process down the first time a backdrop was built for it.
        _sky?.Dispose();
        _sky = _presentation.Presenters.Create<SkyPresentationRequest, ISkyPresenter>(
            new SkyPresentationRequest(
                _device, preview.LevelRoot, preview.FileSystem, preview.Resources, FarPlane, _logger));
        _globalLight = GlobalLightSettings.Read(preview.LevelRoot);
        _shadowPass ??= new ShadowCasterPass(_device);
        Lap("sky");

        // The level's air and its object shading, which the backdrop draws under and the promoted mission
        // keeps. BringMissionUp reads them only on its cold path; a level that comes up this way must
        // read them here or play without them.
        ApplyLevelConstants(preview.LevelRoot);

        // And the day/night binding those constants decide, which is why it cannot happen earlier: what
        // the cycle is reset WITH depends on what the level said about its own light, and that is read
        // immediately above. See DayNightBinding.
        //
        // Reset with the RAW sun -- which is what happened here -- a backdrop of I.G.I. 2's is lit by the
        // near-black colour its night missions author AND by the Dirlight's back colour, which nineteen
        // of its twenty-four levels leave at black. The ground then has no light at all reaching it, and
        // the first mission's backdrop was a black hillside under a sky that drew correctly.
        _timeOfDay.BoundSun = preview.Sun is { } previewSun
            ? DayNightBinding.Bind(previewSun, _deferredLighting)
            : null;
        _timeOfDay.DayNight.ResetForLevel(_timeOfDay.BoundSun, ExposureCVars.TimeOfDay.Value);

        // After the reset, which clears it -- the same order the mission path keeps.
        if (preview.Sun is { } nightSun && _deferredLighting is { Night: { } boundNight } authoredNight)
        {
            _timeOfDay.DayNight.SetNightLighting(
                boundNight.SunIsMoon ? nightSun.Colour : null,
                authoredNight.Ambient);
        }

        // A mission with no shipped bake previews on sun and sky alone. Tracing one is seconds of CPU for a
        // backdrop, and the trace belongs to the mission's real load, which caches it for good.
        _baked.AdoptPreviewField(preview.ProbeField);
        Lap("constants");

        // The outgoing level's surface cache and distance fields go first: the cards were captured from
        // the level that has just been replaced, and the clipmaps were traced against it.
        _gi.DiscardSurfaceCache();
        _gi.DiscardDistanceFields();

        // Then this level's own. A backdrop used to be a picture lit by the artists' bakes, with no lamps,
        // no cards and no field, while the mission it became was lit by the real-time model -- so the
        // picture changed the instant Play was pressed, and every one of those structures was then built
        // on the render thread in front of the player. The preview worker has surveyed the lamps and read
        // (or is baking) the field; what is left here is the upload and the capture, a few tens of
        // milliseconds under the held frame the dissolve is about to fade off.
        //
        // The lamps first, and always: a model name means different art in different levels, so carrying
        // one mission's lamps into the backdrop of the next glows whatever shares a name. The switches
        // index the fixture list and nothing else, so they are rebuilt with it.
        _gi.ApplyLamps(preview.Lamps, preview.MissionId);

        _gi.AdoptBackdropField(preview.DistanceFieldResolution, preview.DistanceField, preview.PendingDistanceField);

        Lap("field");

        // The capture is a render pass over every distinct model and has to run here. It reads the
        // lamps' emission off the geometry, so it comes after them. A lite backdrop wants no cache, and
        // this returns at once.
        _gi.EnsureSurfaceCache();
        Lap("cards");

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

        ApplyColourLookup(preview.MissionId);

        _camera = FreeCameraFactory.Create(
            _scene, _options.CameraYaw, _options.CameraPitch, _options.CameraHeight);
        ApplyShowcaseCamera(preview.MissionId, preview.PointsOfInterest);
        Lap("grade");

        // The one number that says whether this stayed off the frame: everything else was built on a
        // worker, so this is the whole of what a backdrop swap costs the render thread.
        _logger.Information(
            "Preview",
            FormattableString.Invariant(
                $"The backdrop is now mission {preview.MissionId}; the handover took {handover.ElapsedMilliseconds} ms ({stages} ms), {SinceProcessStart():0} ms after the process started."));
    }

    /// <summary>Builds every mission and writes out the places its menu backdrop should circle.</summary>
    /// <param name="path">The file to write, in <see cref="ShowcasePointFile"/>'s format.</param>
    /// <param name="strings">The language table, so each entry can carry the mission's real name.</param>
    /// <remarks>
    /// <c>-poidump</c>'s body. It builds each mission the same way the menu does — the whole point being
    /// that the places written are exactly the ones the game would have found — and walks them in campaign
    /// order so the file reads down the campaign. A mission that will not build is skipped with a warning
    /// rather than taking the run down; its backdrop then finds its places at load, as it would anyway.
    /// </remarks>
    private void DumpShowcasePoints(string path, MenuStringTable strings)
    {
        MenuCameraLibrary cameras = MenuCameraLibrary.Load(
            Path.Combine(AppContext.BaseDirectory, "Assets", "menu-cameras.json"), _logger);

        using var loader = new MissionPreviewLoader(
            _gamePath, _missions, _mission.MagicObjects, _baked.ProbeCache, cameras, _previewContext, _logger, _work);
        var entries = new List<ShowcasePointFile.Entry>();

        foreach (MissionDescriptor descriptor in _missions.InCampaignOrder())
        {
            try
            {
                MissionPreview preview = loader.BuildForTooling(descriptor.Id);

                // A tool wants the places, not a field; a bake started for a level nobody will look at
                // would run to completion on a thread-pool thread for nothing.
                preview.PendingDistanceField?.Cancel();
                entries.Add(new ShowcasePointFile.Entry(
                    descriptor.Id,
                    strings.Get(descriptor.Name),
                    cameras.For(descriptor.Id, _titleRuntime.Title.Id),
                    preview.PointsOfInterest));

                _logger.Information(
                    "Preview",
                    $"Mission {descriptor.Id}: {preview.PointsOfInterest.Count} place(s).");
            }
            catch (Exception ex) when (ex is EngineFatalException or IOException or InvalidDataException)
            {
                _logger.Warning("Preview", $"Mission {descriptor.Id} would not build: {ex.Message}");
            }
        }

        ShowcasePointFile.Write(path, entries, _logger);
    }

    /// <summary>Points the showcase camera at a mission's places, or at the level as a whole when it has none.</summary>
    /// <param name="missionId">The mission being shown, for its authored orbit style.</param>
    /// <param name="points">
    /// The places to circle in turn. Empty falls back to the old behaviour — one orbit around the median of
    /// every placement — which is what a level with too little in it to cluster gets.
    /// </param>
    private void ApplyShowcaseCamera(int missionId, IReadOnlyList<ShowcasePoint> points)
    {
        Vector3d centre = default;
        if (_scene is { Placements.Count: > 0 } showcaseScene)
        {
            double[] centreXs = new double[showcaseScene.Placements.Count];
            double[] centreYs = new double[showcaseScene.Placements.Count];
            for (int i = 0; i < showcaseScene.Placements.Count; i++)
            {
                centreXs[i] = showcaseScene.Placements[i].Position.X;
                centreYs[i] = showcaseScene.Placements[i].Position.Y;
            }

            Array.Sort(centreXs);
            Array.Sort(centreYs);
            centre = new Vector3d(centreXs[centreXs.Length / 2], centreYs[centreYs.Length / 2], showcaseScene.GroundHeight + 15.0);
        }

        _showcaseCamera = new MenuShowcaseCamera(
            points, centre, _menuCameras.For(missionId, _titleRuntime.Title.Id));
    }
}
