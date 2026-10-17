using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Host.Menu;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.UI.Layout;
using OpenIGI.UI;
using OpenIGI.Game.Igi1.Ai;

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
        _worldSolids = preview.Solids;
        _spins = preview.Spins;
        _sun = preview.Sun;

        _playerTerrain = preview.Mission.Terrain is { IsUsable: true } standable ? standable : null;

        // A title whose ground is inside the level hands it over with the walk; one whose ground came
        // with the mission stands on the terrain itself.
        _ground = preview.Ground ?? _playerTerrain;

        // Nobody stands in a backdrop, and the previous mission's player must not be left holding a scene
        // that has just been replaced.
        _player = null;
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
            _world.Spins = _spins;
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
        _backdrop = preview;

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
        _backdrop = preview with { GroundDetail = [] };
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
        _backdropSoldiers?.Dispose();
        _backdropSoldiers = null;
        if (!BackdropLite)
        {
            _backdropSoldiers = _presentation.Presenters.Create<SoldierPresentationRequest, ISoldierPresenter>(
                new SoldierPresentationRequest(_device, preview.Resources, _logger, _magic, _detailMaps));
            _backdropSoldiers?.Load(
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
        _cycleSun = preview.Sun is { } previewSun
            ? DayNightBinding.Bind(previewSun, _deferredLighting)
            : null;
        _dayNight.ResetForLevel(_cycleSun, RenderCVars.TimeOfDay.Value);

        // After the reset, which clears it -- the same order the mission path keeps.
        if (preview.Sun is { } nightSun && _deferredLighting is { Night: { } boundNight } authoredNight)
        {
            _dayNight.SetNightLighting(
                boundNight.SunIsMoon ? nightSun.Colour : null,
                authoredNight.Ambient);
        }

        // A mission with no shipped bake previews on sun and sky alone. Tracing one is seconds of CPU for a
        // backdrop, and the trace belongs to the mission's real load, which caches it for good.
        _probeField = preview.ProbeField;
        _device.SetIrradianceVolume(_probeField);
        Lap("constants");

        // The outgoing level's surface cache and distance fields go first: the cards were captured from
        // the level that has just been replaced, and the clipmaps were traced against it.
        DiscardSurfaceCache();
        DiscardDistanceFields();

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
        ApplyLamps(preview.Lamps, preview.MissionId);

        if (RenderCVars.DistanceFields.Value && _device.Capabilities.SupportsCompute)
        {
            _attemptedFieldResolution = preview.DistanceFieldResolution;
            _builtFieldResolution = preview.DistanceFieldResolution;

            if (preview.DistanceField is { } readField)
            {
                AdoptDistanceField(readField, DistanceFieldAtlas.Build(readField.Fields), fromCache: true);
            }
            else if (preview.PendingDistanceField is { } baking)
            {
                // Handed to the same per-frame collect a played mission's bake uses, which adopts it and
                // writes the cache when it lands -- under a held frame, so the menu does not show the
                // cross-over either.
                _sdfBake = baking;
            }
        }
        else
        {
            // Started before the lite profile was in force -- the very first backdrop is built before
            // there is one -- and wanted by nobody now. Play bakes its own if it needs one.
            preview.PendingDistanceField?.Cancel();
        }

        Lap("field");

        // The capture is a render pass over every distinct model and has to run here. It reads the
        // lamps' emission off the geometry, so it comes after them. A lite backdrop wants no cache, and
        // this returns at once.
        EnsureSurfaceCache();
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
    private void DumpShowcasePoints(string path, OpenIGI.Game.Igi1.Menus.MenuStringTable strings)
    {
        MenuCameraLibrary cameras = MenuCameraLibrary.Load(
            Path.Combine(AppContext.BaseDirectory, "Assets", "menu-cameras.json"), _logger);

        using var loader = new MissionPreviewLoader(
            _gamePath, _missions, _magic, _giCache, cameras, _previewContext, _logger, _work);
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

    /// <summary>Grabs the frame just rendered into the dissolve texture, sizing it to the window first.</summary>
    /// <remarks>
    /// Called after the 3D pass and BEFORE the menu is drawn over it, so what is held is the outgoing level
    /// alone. Catching it after the menu would put a second copy of the menu in the texture and ghost it
    /// against the live one for the length of the dissolve.
    /// </remarks>
    private void HoldFrameForDissolve()
    {
        int width = _window.Width;
        int height = _window.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (_dissolveSize != (width, height))
        {
            if (_dissolveTexture.IsValid)
            {
                _device.DestroyTexture(_dissolveTexture);
            }

            _dissolveTexture = _device.CreateTexture(
                new TextureDescription(width, height, 1, TextureFilter.Linear, TextureAddressMode.Clamp),
                new Bgra32[width * height]);
            _dissolveSize = (width, height);
        }

        _device.CopyFrameToTexture(_dissolveTexture, width, height);
    }

    /// <summary>Draws the held frame over the live one, so the two cross-dissolve.</summary>
    /// <param name="painter">The shell's painter, which draws over the resolved scene and under the menu.</param>
    /// <param name="amount">How much of the held frame is still showing, 1 all of it to 0 none.</param>
    /// <remarks>
    /// The renderer keeps one live world, so two levels can never be on screen at once. What can be is the
    /// last <em>frame</em> of the outgoing one: it is held as a texture and faded off over the incoming
    /// level, which is a true dissolve rather than a dip through black. The camera's orbit is slow enough
    /// that a still is indistinguishable from the live picture over a third of a second.
    /// </remarks>
    private void DrawDissolve(UiPainter painter, float amount)
    {
        if (amount <= 0.002f || !_dissolveTexture.IsValid)
        {
            return;
        }

        painter.DrawSprite(
            _dissolveTexture,
            new UiRect(0f, 0f, _window.Width, _window.Height),

            RectF.Unit,
            Bgra32.White.WithAlpha((byte)(255f * Math.Clamp(amount, 0f, 1f))));
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
