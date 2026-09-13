using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.World;
using OpenIGI.Game.Ai;
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

    [RequiresGameDataFact]
    public void MissionSixAlarmAppliesTheAuthoredApcSwapAndVehicleWreckBinding()
    {
        (LevelTask root, _, ScriptVarRegistry variables) = Load(6);
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        VehiclePhysicsRegistry physics = new();
        Assert.True(physics.Load(new ResourceRegistry(fileSystem)) > 0);
        Assert.True(physics.TryGetCar("601_01_1", out CarPhysicsDefinition apcPhysics));
        Assert.Equal("601_08_1", apcPhysics.DestroyedModel);

        new ConditionalContainerGate(variables).Prepare(root);
        CutsceneRuntime runtime = new(root, variables, vehiclePhysics: physics);
        string graphPath = Path.Combine(GameData.RequireRoot(), "MISSIONS", "location0", "LEVEL6", "GRAPHS", "GRAPH1.DAT");
        GraphFile graph = GraphFile.Read(File.ReadAllBytes(graphPath));
        DeclaredTask graphTask = Assert.IsType<DeclaredTask>(root.Tree.FromId(1));
        Assert.True(graphTask.TryGetValue("Graph position", out object? rawGraphPosition));
        graph.ApplyWorldOffset(Assert.IsType<Vector3d>(rawGraphPosition));
        string limoGraphPath = Path.Combine(GameData.RequireRoot(), "MISSIONS", "location0", "LEVEL6", "GRAPHS", "GRAPH11.DAT");
        GraphFile limoGraph = GraphFile.Read(File.ReadAllBytes(limoGraphPath));
        DeclaredTask limoGraphTask = Assert.IsType<DeclaredTask>(root.Tree.FromId(11));
        Assert.True(limoGraphTask.TryGetValue("Graph position", out object? rawLimoGraphPosition));
        limoGraph.ApplyWorldOffset(Assert.IsType<Vector3d>(rawLimoGraphPosition));
        runtime.ConfigureCarAi(new Dictionary<int, GraphFile> { [1] = graph, [11] = limoGraph });
        GameTask fakeApc = Assert.IsAssignableFrom<GameTask>(root.Tree.FromId(803));
        GameTask alarmApc = Assert.IsAssignableFrom<GameTask>(root.Tree.FromId(800));
        Assert.True(ConditionalContainerGate.IsTaskActive(fakeApc));
        Assert.False(ConditionalContainerGate.IsTaskActive(alarmApc));

        AiAlarmControlRegistry alarms = AiAlarmControlRegistry.Build(root, variables);
        Assert.True(alarms.TryGet(98, out AiAlarmControlState? alarm));
        alarm!.Update(tick: 1, disabled: false, enabled: true, triggerCondition: true, onCondition: true);
        runtime.Tick();
        Assert.False(ConditionalContainerGate.IsTaskActive(fakeApc));
        Assert.True(ConditionalContainerGate.IsTaskActive(alarmApc));
        runtime.Tick(); // SetSpeed(20) has advanced; this tick begins the graph-routed WalkTo(1).
        Assert.True(runtime.TryGetDrawTransform(alarmApc, out Vector3d movedApc, out _));
        Assert.NotEqual(new Vector3d(25047706d, 87471776d, 174684608d), movedApc * WorldScene.UnitsPerMetre);

        DestructibleObjectRegistry destructibles = DestructibleObjectRegistry.Build(root, [], variables, physics);
        DestructibleObject liveApc = Assert.Single(destructibles.Objects, item => ReferenceEquals(item.Owner, alarmApc));
        DestructibleObject dormantApc = Assert.Single(destructibles.Objects, item => ReferenceEquals(item.Owner, fakeApc));
        Assert.Equal(DestructibleObjectKind.Vehicle, liveApc.Kind);
        Assert.Equal("601_08_1", liveApc.DestroyedModelName);

        IReadOnlyList<DestructibleObject> hit = destructibles.ApplyExplosionHits(
            null,
            liveApc.PositionUnits,
            radiusUnits: 1f,
            canContact: item => ConditionalContainerGate.IsTaskActive(item.Owner));
        Assert.Contains(liveApc, hit);
        Assert.True(liveApc.IsDestroyed);
        Assert.False(dormantApc.IsDestroyed);

        GameTask fuelTankOne = Assert.IsAssignableFrom<GameTask>(root.Tree.FromId(1600));
        GameTask fuelTankTwo = Assert.IsAssignableFrom<GameTask>(root.Tree.FromId(1601));
        GameTask limo = Assert.IsAssignableFrom<GameTask>(root.Tree.FromId(801));
        Assert.False(ConditionalContainerGate.IsTaskActive(limo));
        Assert.NotNull(destructibles.TryDestroy(fuelTankOne));
        Assert.NotNull(destructibles.TryDestroy(fuelTankTwo));
        runtime.Tick();
        Assert.True(ConditionalContainerGate.IsTaskActive(limo));
        runtime.Tick(); // SetSpeed(50) has advanced; this tick begins WalkTo(1).
        Assert.True(runtime.TryGetDrawTransform(limo, out Vector3d movedLimo, out _));
        Assert.NotEqual(new Vector3d(24374838d, 89438272d, 174505184d), movedLimo * WorldScene.UnitsPerMetre);
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
