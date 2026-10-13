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
}
