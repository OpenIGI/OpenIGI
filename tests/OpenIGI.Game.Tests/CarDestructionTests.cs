using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// A car blows up the way <c>TASKTYPE_CAR</c> does it, and Mission 1 can see its escape truck go.
/// </summary>
/// <remarks>
/// <para>
/// Mission 1's objectives read <c>Car_1099.isExploded</c> twice: the area gate needs it false and the
/// failure gate fires on it. Every car exports it (<c>0x42FCE0</c>), but only cars whose record named a
/// wreck were given it here, and the truck (<c>622_01_1</c>) names none — so the flag read nought for ever.
/// </para>
/// <para>
/// The rest is <c>0x42FB40</c> and <c>0x42E5A0</c>: only a LAW 80 blast starts a car dying, a round
/// never harms it, it sets one blast off per explosion point after <c>rand % 30</c> ticks, and it only
/// becomes a wreck — and raises <c>isExploded</c> — thirty ticks later.
/// </para>
/// </remarks>
public sealed class CarDestructionTests
{
    private const int EscapeTruckId = 1099;

    [RequiresGameDataFact]
    public void TheCarRecordsCarryTheirExplosionPoints()
    {
        VehiclePhysicsRegistry physics = LoadPhysics(out _);

        Assert.True(physics.TryGetCar("622_01_1", out CarPhysicsDefinition truck));
        Assert.Equal([6, 7], truck.ExplosionPoints);
        Assert.Equal(string.Empty, truck.DestroyedModel);

        Assert.True(physics.TryGetCar("601_01_1", out CarPhysicsDefinition apc));
        Assert.Equal([4, 5, 6, 7, 8], apc.ExplosionPoints);
        Assert.Equal("601_08_1", apc.DestroyedModel);

        Assert.True(physics.TryGetCar("636_01_1", out CarPhysicsDefinition buggy));
        Assert.Empty(buggy.ExplosionPoints);
    }

    [RequiresGameDataFact]
    public void MissionOneSeesItsEscapeTruckExplode()
    {
        (LevelTask root, WorldScene world, ScriptVarRegistry variables, VehiclePhysicsRegistry physics) = LoadMissionOne();
        DeclaredTask truckTask = Assert.IsType<DeclaredTask>(root.Tree.FromId(EscapeTruckId));
        DestructibleObjectRegistry destructibles = DestructibleObjectRegistry.Build(
            root, world.Placements, variables, physics, new IgiRandom());
        LevelExpressionRuntime expressions = new(variables);

        DestructibleObject truck = Assert.Single(
            destructibles.Objects,
            item => ReferenceEquals(item.Owner, truckTask) && item.Kind == DestructibleObjectKind.Vehicle);
        WorldPlacement body = Assert.IsType<WorldPlacement>(truck.Placement);
        Assert.Equal("622_01_1", body.Instance.Name);
        Assert.Equal([6, 7], truck.ExplosionPoints);
        Assert.False(expressions.EvaluateCondition("Car_1099.isExploded", emptyValue: true));

        // A round finds no handler on a car, however hard it hits.
        Assert.Null(destructibles.TryDestroy(truckTask, body, damage: 100f));
        Assert.False(truck.IsDying);

        // A grenade's blast carries no weapon record, so it does not touch the car either.
        _ = destructibles.ApplyExplosionHits(null, truck.PositionUnits, radiusUnits: 4096f);
        Assert.False(truck.IsDying);

        // Far enough out that the blast's radius and falloff fall short of the truck's own bound.
        Vector3d wide = truck.PositionUnits + new Vector3d(
            truck.ExplosionContactRadiusUnits + 10240f + 7168f + 1f, 0d, 0d);
        _ = destructibles.ApplyExplosionHits(
            null,
            wide,
            radiusUnits: 10240f,
            falloffUnits: 7168f,
            damageFactor: 5f,
            sourceWeaponId: DestructibleObjectRegistry.CarDestroyingWeaponId);
        Assert.False(truck.IsDying);

        // A LAW rocket at the truck: it dies, but it has not exploded yet. (Its windows shatter.)
        Assert.DoesNotContain(truck, destructibles.ApplyExplosionHits(
            null,
            truck.PositionUnits,
            radiusUnits: 10240f,
            falloffUnits: 7168f,
            damageFactor: 5f,
            sourceWeaponId: DestructibleObjectRegistry.CarDestroyingWeaponId));
        Assert.True(truck.IsDying);
        Assert.False(truck.IsDestroyed);

        List<VehicleBlast> blasts = [];
        int explodedOnTick = -1;
        for (int tick = 0; tick < 40 && explodedOnTick < 0; tick++)
        {
            destructibles.Tick();
            blasts.AddRange(destructibles.ConsumeVehicleBlasts());
            if (expressions.EvaluateCondition("Car_1099.isExploded", emptyValue: false))
            {
                explodedOnTick = tick;
            }
        }

        // The first tick sets the two blasts off and starts thirty more.
        Assert.Equal(DestructibleObjectRegistry.CarPointBlastTicks, explodedOnTick);
        Assert.True(truck.IsDestroyed);

        // One blast at each explosion point, not at the origin, each a 3 m Explosion0 with no source.
        Assert.Equal(2, blasts.Count);
        Assert.All(blasts, blast =>
        {
            Assert.Same(truck, blast.Source);
            Assert.Equal(12288f, blast.RadiusUnits);
            Assert.Equal(3072f, blast.FalloffUnits);
            Assert.NotEqual(truck.PositionUnits, blast.PositionUnits);
        });
        Assert.NotEqual(blasts[0].PositionUnits, blasts[1].PositionUnits);

        // No wreck model: the body and everything the truck owned leave the world with it.
        WorldPlacement[] owned = [.. world.Placements.Where(placement => IsOwnedBy(placement.Owner, truckTask))];
        Assert.All(
            owned,
            placement => Assert.True(destructibles.IsDestroyed(placement), $"{placement} outlived the truck"));

        // The host takes the marks off exactly these (the LAW's own scorch sits on the body).
        Assert.Same(truck, Assert.Single(destructibles.ConsumeExplodedVehicles()));
        WorldPlacement[] reported = [.. destructibles.PlacementsOf(truck)];
        Assert.Equal(owned.Length, reported.Length);
        Assert.All(owned, placement => Assert.Contains(placement, reported));

        // The objectives the level authors on it: the area gate closes and the failure gate opens.
        Assert.False(expressions.EvaluateCondition("!Car_1099.isExploded", emptyValue: true));
        Assert.True(expressions.EvaluateCondition(
            "HumanPlayer_0.isDead || Car_1099.isExploded || HumanSoldier_4023.isDead",
            emptyValue: false));
    }

    [RequiresGameDataFact]
    public void ADestroyedTankLeavesItsWreckWhereItStoodAndTheWreckBlocks()
    {
        // Mission 7's T80 Car_354. Measured in retail (probe vehicle hooks): thirty ticks after the LAW's blast
        // the countdown's last tick creates 614_08_1 at the tank's own position (0x4F3390), deletes the tank's
        // children (0x401780) and takes the tank out of the world (message 0x4CEA20); the wreck's initializer
        // (0x4F2B90) commits it to the world like any placed object, so it collides.
        (LevelTask root, WorldScene world, ScriptVarRegistry variables, VehiclePhysicsRegistry physics) = LoadMission(7);
        DeclaredTask tankTask = Assert.IsType<DeclaredTask>(root.Tree.FromId(354));
        DestructibleObjectRegistry destructibles = DestructibleObjectRegistry.Build(
            root, world.Placements, variables, physics, new IgiRandom());
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(world.Placements);
        solids.Destructibles = destructibles;

        DestructibleObject tank = Assert.Single(
            destructibles.Objects,
            item => ReferenceEquals(item.Owner, tankTask) && item.Kind == DestructibleObjectKind.Vehicle);
        WorldPlacement body = Assert.IsType<WorldPlacement>(tank.Placement);
        Assert.Equal("614_01_1", body.Instance.Name);
        Assert.Equal("614_08_1", Assert.IsType<WorldModelBinding>(body.DestroyedReplacement).Instance.Name);

        Vector3d above = tank.PositionUnits + new Vector3d(0d, 0d, 5d * 4096d);
        Vector3d below = tank.PositionUnits - new Vector3d(0d, 0d, 3d * 4096d);
        Assert.True(solids.TryIdentify(above, below, out string intact, out _));
        Assert.DoesNotContain("destroyed", intact, StringComparison.Ordinal);

        _ = destructibles.ApplyExplosionHits(
            null,
            tank.PositionUnits,
            radiusUnits: 10240f,
            falloffUnits: 7168f,
            damageFactor: 5f,
            sourceWeaponId: DestructibleObjectRegistry.CarDestroyingWeaponId);
        for (int tick = 0; tick <= DestructibleObjectRegistry.CarPointBlastTicks && !tank.IsDestroyed; tick++)
        {
            destructibles.Tick();
            _ = destructibles.ConsumeVehicleBlasts();
        }

        Assert.True(tank.IsDestroyed);

        // The hull stands in for its wreck rather than leaving the world with its turret, guns and tracks.
        Assert.True(destructibles.IsDestroyed(body));
        Assert.All(
            world.Placements.Where(placement => ReferenceEquals(placement.Owner, tankTask) && !ReferenceEquals(placement, body)),
            part => Assert.True(destructibles.IsDestroyed(part), $"{part.Instance.Name} outlived the tank"));

        // And it is the wreck that a ray now meets where the tank stood.
        Assert.True(solids.TryIdentify(above, below, out string wreck, out _));
        Assert.Contains("destroyed: 614_08_1", wreck, StringComparison.Ordinal);
    }

    private static bool IsOwnedBy(GameTask? task, GameTask ancestor)
    {
        for (GameTask? current = task; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static VehiclePhysicsRegistry LoadPhysics(out ResourceRegistry resources)
    {
        VirtualFileSystem fs = new();
        fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        resources = new ResourceRegistry(fs);
        VehiclePhysicsRegistry physics = new();
        Assert.True(physics.Load(resources) > 0);
        return physics;
    }

    private static (LevelTask Root, WorldScene World, ScriptVarRegistry Variables, VehiclePhysicsRegistry Physics) LoadMissionOne() =>
        LoadMission(1);

    private static (LevelTask Root, WorldScene World, ScriptVarRegistry Variables, VehiclePhysicsRegistry Physics) LoadMission(int missionId)
    {
        VirtualFileSystem fs = new();
        fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs);
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        loop.Types.Register<CutSceneTask>(CutSceneTask.TypeName);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fs, resources, loop.Natives);
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        VehiclePhysicsRegistry physics = new();
        physics.Load(resources);
        LoadedMission mission = new MissionLoader(
            missions,
            fs,
            resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources),
            terrainLoader: new TerrainLoader(resources, loop.Natives)).Load(missionId);
        WorldScene world = WorldScene.Build(mission, root, magic, vehiclePhysics: physics);
        return (root, world, loop.Variables, physics);
    }
}
