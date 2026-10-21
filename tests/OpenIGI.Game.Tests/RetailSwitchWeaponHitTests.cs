using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

/// <summary>A round that strikes a switch: <c>Switch</c>'s <c>WeaponHit</c> handler (<c>0x445840</c>).</summary>
public sealed class RetailSwitchWeaponHitTests
{
    [RequiresGameDataFact]
    public void A_round_breaks_a_destructable_switch_for_good()
    {
        // Mission 8's Switch_253 is one of the three the level marks Destructable.
        (WorldInteractionRegistry interactions, ScriptVarRegistry variables) = Load(8);
        WorldInteraction button = Assert.Single(interactions.Items, item => item.Task.Name == "Switch_253");
        Assert.True(button.IsDestructable);
        Assert.True(button.IsAvailable);

        Assert.True(interactions.WeaponHit(button.Task));

        // Switch::GetActivate (0x445730) offers a broken switch to nobody, and the switch exports what happened (0x445860).
        Assert.True(button.IsDestroyed);
        Assert.False(button.IsAvailable);
        Assert.True(variables.TryRead("Switch_253.isDestroyed", out QvmValue destroyed));
        Assert.Equal(1, destroyed.Integer);
        Assert.True(variables.TryRead("Switch_253.isLastDestroyed", out QvmValue last));
        Assert.Equal(0, last.Integer);
        Assert.False(interactions.WeaponHit(button.Task));
    }

    [RequiresGameDataFact]
    public void A_round_leaves_a_switch_that_is_not_destructable_working()
    {
        // Mission 1's alarm pads are not Destructable: a round stops on them and nothing changes.
        (WorldInteractionRegistry interactions, ScriptVarRegistry variables) = Load(1);
        WorldInteraction pad = Assert.Single(interactions.Items, item => item.Task.Name == "Switch_94");

        Assert.False(interactions.WeaponHit(pad.Task));
        Assert.False(pad.IsDestroyed);
        Assert.True(pad.IsAvailable);
        Assert.True(variables.TryRead("Switch_94.isDestroyed", out QvmValue destroyed));
        Assert.Equal(0, destroyed.Integer);
    }

    private static (WorldInteractionRegistry Interactions, ScriptVarRegistry Variables) Load(int missionId)
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
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(root, mission, DoorRegistry.Build(root), loop.Variables);
        return (interactions, loop.Variables);
    }
}
