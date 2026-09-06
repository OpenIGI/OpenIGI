using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.Game.Player;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

public sealed class Issue105Tests
{
    [RequiresGameDataFact]
    public void MissionSixFuelTanksOfferC4AtTheirAuthoredRetailRadius()
    {
        (LevelTask root, LoadedMission mission, ScriptVarRegistry variables) = Load(6);
        new ConditionalContainerGate(variables).Prepare(root);
        _ = new MissionObjectiveRuntime(root, variables);
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
            root,
            mission,
            DoorRegistry.Build(root),
            variables);
        GenericPickupRegistry pickups = GenericPickupRegistry.Build(root, variables);

        GenericPickup[] c4 = pickups.Items
            .Where(static pickup => pickup.Task.Id is 755 or 756)
            .ToArray();
        Assert.Equal(2, c4.Length);

        foreach (GenericPickup pickup in c4)
        {
            pickups.CollectAt(pickup.Position);
        }

        interactions.Tick();
        WorldInteraction[] tanks = interactions.Items
            .Where(static item => item.Type == InteractionActivateType.PlaceExplosive)
            .ToArray();
        Assert.Equal(2, tanks.Length);
        Assert.All(tanks, static tank => Assert.True(tank.IsAvailable));

        WorldInteraction tank = tanks[0];
        Vector3d eye = tank.Anchor + new Vector3d(0.0, -20000.0, 0.0);
        Assert.Same(tank, interactions.Resolve(eye, yaw: 0f));

        tank.BeginUse();
        tank.CompleteUse();
        interactions.Tick();
        Assert.True(tank.IsCompleted);
        Assert.False(tank.IsAvailable);
    }

    [RequiresGameDataFact]
    public void MissionSixFenceElectricExpressionTracksGeneratorStateAndContact()
    {
        (LevelTask root, LoadedMission mission, ScriptVarRegistry variables) = Load(6);
        new ConditionalContainerGate(variables).Prepare(root);
        _ = new MissionObjectiveRuntime(root, variables);
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
            root,
            mission,
            DoorRegistry.Build(root),
            variables);

        Assert.True(variables.TryRead("ConditionalContainer_360.isRun", out QvmValue power));
        Assert.Equal(1, power.Integer);

        WorldInteraction[] fences = interactions.Items
            .Where(static item => item.Type == InteractionActivateType.Fence)
            .ToArray();
        Assert.Equal(41, fences.Length);
        Assert.All(fences, static item => Assert.True(item.IsElectrified));
        WorldInteraction fence = fences[0];

        Vector3d localCentre = new(
            (fence.LocalBoundsMinimum.X + fence.LocalBoundsMaximum.X) * 0.5,
            (fence.LocalBoundsMinimum.Y + fence.LocalBoundsMaximum.Y) * 0.5,
            (fence.LocalBoundsMinimum.Z + fence.LocalBoundsMaximum.Z) * 0.5);
        Vector3d localFrom = new(localCentre.X, fence.LocalBoundsMinimum.Y - 2048.0, localCentre.Z);
        Vector3d localTo = new(localCentre.X, fence.LocalBoundsMaximum.Y + 2048.0, localCentre.Z);
        Vector3d from = fence.Anchor + fence.Orientation.Transposed().Transform(localFrom);
        Vector3d to = fence.Anchor + fence.Orientation.Transposed().Transform(localTo);

        Assert.Same(fence, interactions.FindElectrifiedFenceContact(from, to));

        // The body centre is stopped one human radius short of the thin fence by collision. A
        // stationary contact must still be a shock contact even when the centre never enters the mesh.
        Vector3d nearFenceLocal = new(
            localCentre.X,
            fence.LocalBoundsMinimum.Y - HumanSeparation.RadiusInUnits + 64.0,
            localCentre.Z);
        Vector3d nearFence = fence.Anchor + fence.Orientation.Transposed().Transform(nearFenceLocal);
        Assert.Same(fence, interactions.FindElectrifiedFenceContact(nearFence, nearFence));
        Assert.Same(fence, interactions.FindElectrifiedFenceNear(nearFence));

        Assert.True(variables.TryWrite("ConditionalContainer_360.isRun", QvmValue.Zero));
        interactions.Tick();
        Assert.False(fence.IsElectrified);
        Assert.Null(interactions.FindElectrifiedFenceContact(from, to));

        Assert.True(variables.TryWrite("ConditionalContainer_360.isRun", QvmValue.FromInt32(1)));
        interactions.Tick();
        Assert.True(fence.IsElectrified);
        Assert.Same(fence, interactions.FindElectrifiedFenceContact(from, to));
    }

    private static (LevelTask Root, LoadedMission Mission, ScriptVarRegistry Variables) Load(int missionId)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        LevelLoader loader = new(root, loop.TaskCommands, loop.Natives, resources, null);
        LoadedMission mission = new MissionLoader(missions, fileSystem, resources, loader, null, null).Load(missionId);
        return (root, mission, loop.Variables);
    }
}
