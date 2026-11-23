using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Minefields, smokes and figures, as minefield.c, smoke.c and editboneobj.c work them.</summary>
public sealed class Igi2WorldEffectTests
{
    /// <summary>A field whose expression holds explodes, then waits its delay between explosions in ticks before it may again.</summary>
    [Fact]
    public void A_minefield_waits_its_delay_between_explosions()
    {
        Igi2MineFieldTask field = new() { ExplodeExpression = "1", DelayBetween = 2f };
        Flow.Igi2LevelRun level = Level();
        field.Run(level);
        Assert.Equal(60, field.Pause);
        for (int tick = 0; tick < 60; tick++)
        {
            field.Run(level);
        }

        Assert.Equal(0, field.Pause);
        field.Run(level);
        Assert.Equal(60, field.Pause);
    }

    /// <summary>A delay of -1 makes the pause -30, which counts down past -1 and never comes back to nought: the field explodes once.</summary>
    [Fact]
    public void A_minefield_told_to_explode_once_never_explodes_again()
    {
        Igi2MineFieldTask field = new() { ExplodeExpression = "1", DelayBetween = -1f };
        Flow.Igi2LevelRun level = Level();
        field.Run(level);
        Assert.Equal(-30, field.Pause);
        for (int tick = 0; tick < 100; tick++)
        {
            field.Run(level);
        }

        Assert.Equal(-130, field.Pause);
    }

    /// <summary>A fireball smoke that is not told to burn burns at half a point a second within a metre.</summary>
    [Fact]
    public void A_fireball_burns_by_default()
    {
        Igi2SmokeTask fire = new() { Sprite = 0 };
        fire.Prepare();
        Assert.True(fire.DoDamage);
        Assert.Equal(0.5f, fire.DamagePerSecond);
        Assert.Equal(1f, fire.DamageRadius);

        Igi2SmokeTask smoke = new() { Sprite = 3 };
        smoke.Prepare();
        Assert.False(smoke.DoDamage);
    }

    /// <summary>
    /// A model named for a kind of face becomes one of three in turn, the counter stepped by every human whatever its model; others
    /// are worn as named.
    /// </summary>
    [Fact]
    public void Faces_are_given_in_turn()
    {
        Igi2HumanFaces faces = new();
        Assert.Equal("rsg1_1", faces.Make("rsg_1"));
        Assert.Equal("anya_1", faces.Make("anya_1"));
        Assert.Equal("rsg3_1", faces.Make("ch_rsg_1"));
        Assert.Equal("chn1_1", faces.Make("chn_1"));
    }

    /// <summary>A figure's clip list splits on commas, spaces, tabs and new lines.</summary>
    [Fact]
    public void A_figures_clip_list_splits_as_the_original_splits_it()
    {
        Igi2EditBoneObjTask figure = new() { AnimationList = "idle01, talk02\twave\n  sit" };
        Assert.Equal(["idle01", "talk02", "wave", "sit"], figure.Clips);
    }

    /// <summary>Mission 1's figures and smokes load as their classes.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Mission_1s_figures_and_smokes_load()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Assert.NotEmpty(m.Tree.OfType<Igi2EditBoneObjTask>());
        Assert.NotEmpty(m.Tree.OfType<Igi2SmokeTask>());
        Assert.Contains(m.Tree.OfType<Igi2EditBoneObjTask>(), static f => f.Clips.Count > 0 && f.Model.Length > 0);
    }

    /// <summary>
    /// A figure's controllers step themselves, as every new <c>AnimController</c> does (<c>AnimController_CreateHandler</c>): its clip plays,
    /// and as one ends the next of its list plays (<c>EditBoneObj_RunHandler</c>); a save read back starts its list again.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_figure_plays_its_clips_in_turn()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        (List<Game.World.LoadedPack> packs, Engine.Resources.ResourceRegistry resources) = Igi2LevelScene.Packs(m.Level);
        Animation.Igi2AnimationLibrary clips = Animation.Igi2AnimationLibrary.Load(packs, resources);
        Igi2EditBoneObjTask? figure = null;
        foreach (Igi2EditBoneObjTask candidate in m.Tree.OfType<Igi2EditBoneObjTask>())
        {
            if (candidate.Clips.Count < 2 || candidate.Clips[0] == candidate.Clips[1]
                || m.Level.ModelFor(candidate.Model)?.BoneHierarchy is not { } rig
                || candidate.Clips.Take(2).Any(c => clips.Find(rig, c) is null))
            {
                continue;
            }

            figure = candidate;
            break;
        }

        Assert.NotNull(figure);
        figure.Prepare(clips, m.Level.ModelFor, figure.Model, new Core.Numerics.IgiRandom());
        Formats.Animation.Igi2AnimationClip first = figure.Clip!;
        Assert.Equal(figure.Clips[0], first.Name);

        figure.Run(m.Run);
        Assert.True(figure.ClipTime > 0, "The figure's clip did not move.");

        int ticks = 0;
        while (ReferenceEquals(figure.Clip, first) && ticks++ < 100_000)
        {
            figure.Run(m.Run);
        }

        Assert.Equal(figure.Clips[1], figure.Clip!.Name);

        // EditBoneObj_DeSerializeDoneHandler: once a save is read, the figure's list plays again from its first clip.
        ((Game.Saves.ISavedState)figure).LoadDone();
        Assert.Equal(figure.Clips[0], figure.Clip!.Name);
        Assert.Equal(0, figure.ClipTime);
    }

    /// <summary>A spline's waypoint is in the world only while its spline displays its waypoints (<c>SplineObjWaypoint_UpdateInternalDataHandler</c>).</summary>
    [Fact]
    public void A_waypoint_is_drawn_only_when_its_spline_displays_waypoints()
    {
        foreach (bool display in (bool[])[false, true])
        {
            Engine.Tasks.TaskTypeRegistry types = new();
            Engine.Tasks.TaskTree tree = new(types);
            Engine.Tasks.GameTask root = tree.New(null, types.Register<Engine.Tasks.ContainerTask>("Container"));
            Igi2SplineObjTask spline = (Igi2SplineObjTask)tree.New(root, types.Register<Igi2SplineObjTask>(Igi2SplineObjTask.TypeName));
            spline.DisplayWaypoints = display;
            Igi2SplineObjWaypointTask waypoint = (Igi2SplineObjWaypointTask)tree.New(spline, types.Register<Igi2SplineObjWaypointTask>(Igi2SplineObjWaypointTask.TypeName));
            waypoint.WaypointModel = "400_01_1";

            Igi2WorldScene scene = Igi2WorldScene.Build(root);
            Assert.Equal(display, scene.Placements.Any(static p => p.TypeName == Igi2SplineObjWaypointTask.TypeName));
        }
    }

    /// <summary>An object excluded from clipping against portals says so on its placement, and only it.</summary>
    [Fact]
    public void An_object_excluded_from_portal_clipping_says_so()
    {
        Engine.Tasks.TaskTypeRegistry types = new();
        Engine.Tasks.TaskTree tree = new(types);
        Engine.Tasks.GameTask root = tree.New(null, types.Register<Engine.Tasks.ContainerTask>("Container"));
        Engine.Tasks.TaskType rigid = types.Register<Interaction.Igi2EditRigidObjTask>(Interaction.Igi2EditRigidObjTask.TypeName);
        Interaction.Igi2EditRigidObjTask excluded = (Interaction.Igi2EditRigidObjTask)tree.New(root, rigid);
        excluded.Model = "400_01_1";
        excluded.IsExcludedFromPortalClipping = true;
        Interaction.Igi2EditRigidObjTask room = (Interaction.Igi2EditRigidObjTask)tree.New(root, rigid);
        room.Model = "400_02_1";

        Igi2WorldScene scene = Igi2WorldScene.Build(root);
        Assert.True(scene.Placements.Single(static p => p.Model == "400_01_1").IsExcludedFromPortalClipping);
        Assert.False(scene.Placements.Single(static p => p.Model == "400_02_1").IsExcludedFromPortalClipping);
    }

    /// <summary>
    /// A container whose condition fails takes a document out of the world and off the pickups' list; once it runs again the document
    /// comes back, unless it was taken (<c>GenericPickup_RemoveHandler</c>, <c>GenericPickup_RegisterHandler</c>). A container inside it
    /// goes with it and comes back only while it runs itself.
    /// </summary>
    [Fact]
    public void A_containers_children_leave_the_world_and_come_back_with_it()
    {
        Engine.Tasks.TaskTypeRegistry types = new();
        Engine.Tasks.TaskTree tree = new(types);
        Engine.Tasks.GameTask root = tree.New(null, types.Register<Engine.Tasks.ContainerTask>("Container"));
        Engine.Tasks.TaskType conditional = types.Register<Flow.Igi2ConditionalContainerTask>(Flow.Igi2ConditionalContainerTask.TypeName);
        Engine.Tasks.TaskType pickups = types.Register<Pickups.Igi2GenericPickupTask>(Pickups.Igi2GenericPickupTask.TypeName);
        Flow.Igi2ConditionalContainerTask outer = (Flow.Igi2ConditionalContainerTask)tree.New(root, conditional);
        Pickups.Igi2GenericPickupTask document = (Pickups.Igi2GenericPickupTask)tree.New(outer, pickups);
        Flow.Igi2ConditionalContainerTask inner = (Flow.Igi2ConditionalContainerTask)tree.New(outer, conditional);
        Pickups.Igi2GenericPickupTask key = (Pickups.Igi2GenericPickupTask)tree.New(inner, pickups);

        Flow.Igi2LevelRun.RemoveFromWorld(outer);
        Assert.False(document.IsInWorld);
        Assert.False(key.IsInWorld);
        Assert.False(inner.IsActive);

        // The inner container does not run (its run byte is nought until its condition holds), so what it holds stays out.
        Flow.Igi2LevelRun.PutInWorld(outer);
        Assert.True(document.IsInWorld);
        Assert.False(inner.IsActive);
        Assert.False(key.IsInWorld);
    }

    /// <summary>
    /// A countdown is shown from the run its start expression holds until it runs out; taken out of the world its display goes, and the
    /// next run while it counts puts it back (<c>MissionTimer_RunHandler</c>, <c>MissionTimer_RemoveHandler</c>).
    /// </summary>
    [Fact]
    public void A_countdown_is_shown_while_it_counts_and_in_the_world()
    {
        Flow.Igi2MissionTimerTask timer = new() { StartExpression = "1", TimeInSeconds = 0.1f };
        timer.Prepare();
        Flow.Igi2LevelRun level = Level();
        Assert.False(timer.IsShown);

        timer.Run(level);
        Assert.True(timer.IsShown);
        Assert.Equal("00 : 00", timer.DisplayText);

        timer.RemoveFromWorld();
        Assert.False(timer.IsShown);
        timer.Run(level);
        Assert.True(timer.IsShown);

        for (int tick = 0; tick < 10; tick++)
        {
            timer.Run(level);
        }

        Assert.Equal(Flow.Igi2MissionTimerStatus.Finished, timer.Status);
        Assert.False(timer.IsShown);
    }

    /// <summary>An overlay up when its holder leaves the world comes down, and the infrared view with it (<c>OverlayHolder_RemoveHandler</c>).</summary>
    [Fact]
    public void An_overlay_comes_down_as_its_holder_leaves_the_world()
    {
        Flow.Igi2ScreenOverlay screen = new();
        Flow.Igi2OverlayHolderTask holder = new() { VisibleExpression = "1", InfraredExpression = "1", OverlayType = 1, Screen = screen };
        holder.Run(Level());
        Assert.Equal(Flow.Igi2OverlayType.Zoom, screen.Overlay);
        Assert.True(screen.IsInfrared);

        holder.RemoveFromWorld();
        Assert.Null(screen.Overlay);
        Assert.False(screen.IsInfrared);
    }

    /// <summary>
    /// A flock's birds are drawn as their <c>PolyListDynCubeObj</c> draws them: four triangles a bird, flat, both sides, tested against the
    /// world's depth and written into none.
    /// </summary>
    [Fact]
    public void A_flocks_birds_are_drawn_flat_and_double_sided()
    {
        World.Igi2BirdFlockTask flock = new() { Individuals = 10 };
        flock.Prepare(new World.Igi2FlockRandom());
        flock.Run(RainLevel());

        Rendering.Headless.RecordingGraphicsDevice device = new();
        using Presentation.Rendering.FlatPolygonDrawer drawer = new(device);
        drawer.Draw([new Game.Player.TitleWorldPolygons(flock.Position, flock.Wings())], default, Core.Numerics.Matrix4f.Identity);

        Rendering.Headless.RecordingGraphicsDevice.MeshDrawCall draw = Assert.Single(device.MeshDraws);
        Assert.Equal(10 * 4, draw.TriangleCount);
        Assert.Equal(Rendering.BlendMode.Opaque, draw.Blend);
        Assert.Equal(Rendering.DepthState.TestOnly, draw.DepthState);
        Assert.Equal(Rendering.CullMode.None, draw.CullMode);
    }

    /// <summary>A building's lightmap info hands it its indoors ambient light as the level loads.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_buildings_lightmap_info_sets_its_inside_ambient()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Igi2LightmapInfoTask[] infos = [.. m.Tree.OfType<Igi2LightmapInfoTask>()];
        Assert.NotEmpty(infos);
        foreach (Igi2LightmapInfoTask info in infos)
        {
            if (info.Parent is Igi2BuildingTask building)
            {
                Assert.Equal(info.IndoorsAmbient, building.InsideAmbient);
            }
        }
    }

    private static Flow.Igi2LevelRun Level()
    {
        Engine.Tasks.TaskTypeRegistry types = new();
        Engine.Tasks.TaskTree tree = new(types);
        Engine.Tasks.GameTask root = tree.New(null, types.Register<Engine.Tasks.ContainerTask>("Container"));
        return new(root, new OpenIGI.Scripting.Runtime.NativeRegistry(), new Scripting.Igi2ScriptFunctions());
    }
    /// <summary>A rain effect falls without an active expression, and as its expression says with one; its density is held in range.</summary>
    [Fact]
    public void Rain_falls_as_its_active_expression_says()
    {
        World.Igi2RainEffectTask always = new() { Density = 40 };
        always.Run(RainLevel());
        Assert.True(always.IsActive);
        Assert.Equal(World.Igi2RainEffectTask.MaximumDensity, always.Layers);
        Assert.Equal(World.Igi2RainEffectTask.RainTexture, always.Texture);

        World.Igi2RainEffectTask stopped = new() { ActiveExpression = "0", IsRain = false, Density = 0 };
        stopped.Run(RainLevel());
        Assert.False(stopped.IsActive);
        Assert.Equal(1, stopped.Layers);
        Assert.Equal(World.Igi2RainEffectTask.SnowTexture, stopped.Texture);
    }

    private static Flow.Igi2LevelRun RainLevel()
    {
        Engine.Tasks.TaskTypeRegistry types = new();
        Engine.Tasks.TaskTree tree = new(types);
        Engine.Tasks.GameTask root = tree.New(null, types.Register<Engine.Tasks.ContainerTask>("Container"));
        return new(root, new OpenIGI.Scripting.Runtime.NativeRegistry(), new Scripting.Igi2ScriptFunctions());
    }
    /// <summary>
    /// A flock holds its birds to a hundred and starts their swings at forty to 1240 ticks; flying, each bird stays within twice the
    /// radius across and ten metres up or down, and is drawn as four triangles; out of sight it stands still.
    /// </summary>
    [Fact]
    public void A_flock_wheels_while_seen()
    {
        World.Igi2BirdFlockTask flock = new() { Individuals = 150, Radius = 50f, CutoffDistance = 100f };
        flock.Prepare(new World.Igi2FlockRandom());
        Assert.Equal(World.Igi2BirdFlockTask.MaximumBirds, flock.Birds.Count);
        Assert.All(flock.Birds, static bird => Assert.All(bird.Periods, static period => Assert.InRange(period, 40, 1240)));

        Flow.Igi2LevelRun level = RainLevel();
        for (int tick = 0; tick < 200; tick++)
        {
            flock.Run(level);
        }

        float reach = 2f * 50f * 4096f;
        Assert.All(flock.Birds, bird =>
        {
            Assert.InRange(Math.Abs(bird.Position.X), 0f, reach);
            Assert.InRange(Math.Abs(bird.Position.Y), 0f, reach);
            Assert.InRange(Math.Abs(bird.Position.Z), 0f, 2f * 40960f);
        });
        Assert.Equal(flock.Birds.Count * 12, flock.Wings().Count);

        flock.Project(101f * 4096f);
        Assert.False(flock.IsSeen);
        Core.Numerics.Vector3f before = flock.Birds[0].Position;
        flock.Run(level);
        Assert.Equal(before, flock.Birds[0].Position);
    }
}
