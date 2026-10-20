using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>The sounds Project I.G.I.'s world tasks make as they update.</summary>
public sealed class InteractionSoundTests
{
    [RequiresGameDataFact]
    public void A_generator_holds_its_run_sound_while_on_and_shuts_down_with_its_stop_sound()
    {
        (LevelTask root, ScriptVarRegistry variables, LoadedMission mission) = Load(8);
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(root, mission, DoorRegistry.Build(root), variables);
        List<InteractionSoundEvent> heard = [];
        interactions.Sounds = heard.Add;

        // Mission 8's generator runs while EditVariable_703.nValue > 0, with no backup; the objectives bind the variable in a
        // played level.
        int power = 0;
        variables.BindInt(root.Descendants().First(static task => task.Name == "EditVariable_703"), "nValue", () => power, value => power = value);
        Assert.True(variables.TryWrite("EditVariable_703.nValue", QvmValue.FromInt32(1)));
        interactions.Tick();
        interactions.Tick();
        InteractionSoundEvent started = Assert.Single(heard);
        Assert.Equal(InteractionSound.LoopStarted, started.What);
        Assert.Equal("generator_run", started.Sound);
        Assert.Equal("Generator_99", started.Task.Name);

        heard.Clear();
        Assert.True(variables.TryWrite("EditVariable_703.nValue", QvmValue.FromInt32(0)));
        interactions.Tick();
        interactions.Tick();
        Assert.Equal([InteractionSound.LoopStopped, InteractionSound.Played], heard.Select(static sound => sound.What));
        Assert.Equal("generator_stop", heard[1].Sound);
    }

    [RequiresGameDataFact]
    public void A_switch_clicks_as_it_comes_to_rest_on_and_wears_its_pressed_model_meanwhile()
    {
        (LevelTask root, ScriptVarRegistry variables, LoadedMission mission) = Load(1);
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(root, mission, DoorRegistry.Build(root), variables);
        List<InteractionSoundEvent> heard = [];
        interactions.Sounds = heard.Add;
        WorldInteraction button = interactions.Items.First(static item => item.Type == InteractionActivateType.Switch && item.IsOn);

        interactions.Tick();
        heard.Clear();
        button.Press();
        interactions.Tick();
        Assert.Equal(1, button.ModelSlot);
        Assert.Empty(heard);

        // Released once two ticks have passed the press: the rest model, and its click where the switch stands.
        for (int tick = 0; tick < 3; tick++)
        {
            interactions.Tick();
        }

        Assert.Equal(0, button.ModelSlot);
        InteractionSoundEvent click = Assert.Single(heard);
        Assert.Equal(WorldInteraction.SwitchSound, click.Sound);
        Assert.Equal(button.TaskPosition, click.Position);
    }

    [RequiresGameDataFact]
    public void A_powered_fence_buzzes_in_a_box_along_its_span_until_the_power_goes()
    {
        (LevelTask root, ScriptVarRegistry variables, LoadedMission mission) = Load(6);
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(root, mission, DoorRegistry.Build(root), variables);
        List<InteractionSoundEvent> heard = [];
        interactions.Sounds = heard.Add;
        int fences = interactions.Items.Count(static item => item.Type == InteractionActivateType.Fence && item.FenceSpan is not null
            && item.Task.TryGetValue("Electric Expression", out object? electric) && electric is string { Length: > 0 });
        Assert.True(fences > 0);

        // The fences are powered while ConditionalContainer_360 runs; the container's gate binds its isRun in a played level.
        int running = 0;
        variables.BindInt(root.Descendants().First(static task => task.Name == "ConditionalContainer_360"), "isRun", () => running, value => running = value);
        Assert.True(variables.TryWrite("ConditionalContainer_360.isRun", QvmValue.FromInt32(1)));
        interactions.Tick();
        Assert.Equal(fences, heard.Count(static sound => sound.What == InteractionSound.AreaStarted));
        InteractionSoundEvent area = heard.First(static sound => sound.What == InteractionSound.AreaStarted);
        Assert.Equal(WorldInteraction.FenceSound, area.Sound);
        Assert.Equal(1.75, area.HalfExtentsMetres.Y);
        Assert.Equal(2.0, area.HalfExtentsMetres.Z);
        Assert.True(area.HalfExtentsMetres.X > 1.5);

        // The areas a powered fence makes join the level's ambience, and leave it with the power.
        AmbientAreaRegistry ambience = new();
        PlacedAmbientArea placed = ambience.Add(area);
        Assert.Contains(placed, ambience.Areas);

        heard.Clear();
        Assert.True(variables.TryWrite("ConditionalContainer_360.isRun", QvmValue.FromInt32(0)));
        interactions.Tick();
        Assert.Equal(fences, heard.Count(static sound => sound.What == InteractionSound.AreaStopped));
        placed.IsSounding = true;
        AmbientAreaRegistry.Retire(placed);
        ambience.Tick(default);
        Assert.True(placed.WantsStop);
        placed.IsSounding = false;
        ambience.Tick(default);
        Assert.DoesNotContain(placed, ambience.Areas);
    }

    private static (LevelTask Root, ScriptVarRegistry Variables, LoadedMission Mission) Load(int missionId)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        LevelLoader level = new(root, loop.TaskCommands, loop.Natives, resources);
        LoadedMission mission = new MissionLoader(missions, fileSystem, resources, level).Load(missionId);
        return (root, loop.Variables, mission);
    }
}
