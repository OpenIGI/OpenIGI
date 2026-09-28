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
using OpenIGI.Game.Weapons;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

public sealed class Issue105Tests
{
    [RequiresGameDataFact]
    public void MissionSixRetailContractSeparatesApcLimoC4AndFuelTargets()
    {
        (LevelTask root, LoadedMission mission, ScriptVarRegistry variables) = Load(6);
        DeclaredTask apc = Assert.IsType<DeclaredTask>(root.Tree.FromId(800));
        DeclaredTask fakeApc = Assert.IsType<DeclaredTask>(root.Tree.FromId(803));
        DeclaredTask limo = Assert.IsType<DeclaredTask>(root.Tree.FromId(801));

        Assert.True(apc.TryGetValue("Can fire", out object? rawCanFire));
        Assert.Equal("1", Assert.IsType<string>(rawCanFire));
        Assert.True(fakeApc.TryGetValue("Can fire", out object? rawFakeCanFire));
        Assert.Equal(string.Empty, Assert.IsType<string>(rawFakeCanFire));
        Assert.True(limo.TryGetValue("Can fire", out object? rawLimoCanFire));
        Assert.Equal(string.Empty, Assert.IsType<string>(rawLimoCanFire));

        GenericPickupRegistry pickups = GenericPickupRegistry.Build(root, variables);
        Assert.Equal([755, 756], pickups.Items
            .Where(static item => item.Task.Id is 755 or 756)
            .Select(static item => item.Task.Id)
            .Order());

        new ConditionalContainerGate(variables).Prepare(root);
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
            root,
            mission,
            DoorRegistry.Build(root),
            variables);
        Assert.Equal([1602, 1603], interactions.Items
            .Where(static item => item.Type == InteractionActivateType.PlaceExplosive)
            .Select(static item => item.Task.Id)
            .Order());
    }

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

        foreach (WorldInteraction tank in tanks)
        {
            Vector3d feet = tank.Anchor + new Vector3d(0.0, -2.0 * WorldScene.UnitsPerMetre, 0.0);
            Vector3d eye = feet + new Vector3d(0.0, 0.0, PlayerBody.StandingEyeHeight);
            Vector3d aim = tank.Anchor - eye;
            float yaw = (float)Math.Atan2(aim.X, aim.Y);
            float pitch = (float)Math.Atan2(aim.Z, Math.Sqrt((aim.X * aim.X) + (aim.Y * aim.Y)));
            Assert.Same(tank, interactions.Resolve(eye, yaw, pitch));

            Assert.True(tank.BeginUse());
            tank.CompleteUse();
            interactions.Tick();
            Assert.True(tank.IsCompleted);
            Assert.False(tank.IsAvailable);
        }
    }

    [RequiresGameDataFact]
    public void MissionSixAllFiveAuthoredCabinetsCanBeSearchedFromTheirUsePoses()
    {
        (LevelTask root, LoadedMission mission, ScriptVarRegistry variables) = Load(6);
        new ConditionalContainerGate(variables).Prepare(root);
        MissionObjectiveRuntime objectives = new(root, variables);
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
            root,
            mission,
            DoorRegistry.Build(root),
            variables);

        WorldInteraction[] cabinets = interactions.Items
            .Where(static item => item.Type == InteractionActivateType.Cabinet
                && item.Task.Id is >= 1500 and <= 1504)
            .OrderBy(static item => item.Task.Id)
            .ToArray();
        Assert.Equal([1500, 1501, 1502, 1503, 1504], cabinets.Select(static item => item.Task.Id));

        foreach (WorldInteraction cabinet in cabinets)
        {
            Vector3d feet = cabinet.BodyPose(cabinet.Anchor).Position;
            Vector3d eye = feet + new Vector3d(0.0, 0.0, PlayerBody.StandingEyeHeight);
            Vector3d aim = cabinet.Anchor - eye;
            float yaw = (float)Math.Atan2(aim.X, aim.Y);
            float pitch = (float)Math.Atan2(aim.Z, Math.Sqrt((aim.X * aim.X) + (aim.Y * aim.Y)));

            Assert.Same(cabinet, interactions.Resolve(eye, yaw, pitch));
            Assert.True(cabinet.BeginUse());
            cabinet.CompleteUse();
            interactions.Tick();
            Assert.True(cabinet.IsSearched);
        }

        objectives.Tick();
        Assert.True(variables.TryRead("StatusMessage_706.isSendt", out QvmValue completed));
        Assert.Equal(1, completed.Integer);
    }

    [RequiresGameDataFact]
    public void MissionSixC4TimerExplodesBothFuelTanksAndReleasesTheLimo()
    {
        (LevelTask root, LoadedMission mission, ScriptVarRegistry variables) = Load(6);
        new ConditionalContainerGate(variables).Prepare(root);
        WorldScene scene = WorldScene.Build(mission, root);
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
            root, mission, DoorRegistry.Build(root), variables);
        GenericPickupRegistry pickups = GenericPickupRegistry.Build(root, variables);
        DestructibleObjectRegistry destructibles = DestructibleObjectRegistry.Build(
            root, scene.Placements, variables);
        CutsceneRuntime runtime = new(root, variables, terrain: mission.Terrain);

        foreach (GenericPickup pickup in pickups.Items.Where(static item => item.Task.Id is 755 or 756))
        {
            pickups.CollectAt(pickup.Position);
        }
        interactions.Tick();
        foreach (WorldInteraction charge in interactions.Items.Where(
                     static item => item.Task.Id is 1602 or 1603))
        {
            Assert.True(charge.BeginUse());
            charge.CompleteUse();
        }

        DestructibleObject[] tanks = destructibles.Objects
            .Where(static item => item.Owner.Id is 1600 or 1601)
            .OrderBy(static item => item.Owner.Id)
            .ToArray();
        Assert.Equal([1600, 1601], tanks.Select(static item => item.Owner.Id));
        for (int tick = 1; tick < 20 * GameClock.DefaultTickRate; tick++)
        {
            runtime.Tick();
            destructibles.Tick();
            Assert.Empty(destructibles.ConsumeReadyExplosions());
        }

        // Tick 600: the Explosion expression holds, both tanks explode (isExploded rises now) and the
        // Explosion0 each creates goes off on its first update, the tick after.
        runtime.Tick();
        destructibles.Tick();
        Assert.All(tanks, static item => Assert.True(item.IsDestroyed));
        Assert.Empty(destructibles.ConsumeReadyExplosions());
        destructibles.Tick();
        Assert.Equal(tanks, destructibles.ConsumeReadyExplosions().OrderBy(static item => item.Owner.Id));

        runtime.Tick();
        GameTask limo = Assert.IsAssignableFrom<GameTask>(root.Tree.FromId(801));
        Assert.True(ConditionalContainerGate.IsTaskActive(limo));
    }

    [RequiresGameDataFact]
    public void MissionSixExplodeObjectsUseTheirAuthoredDamageScaleAsHitPoints()
    {
        (LevelTask root, LoadedMission mission, ScriptVarRegistry variables) = Load(6);
        WorldScene scene = WorldScene.Build(mission, root);
        DestructibleObjectRegistry destructibles = DestructibleObjectRegistry.Build(
            root, scene.Placements, variables);
        DestructibleObject tank = Assert.Single(
            destructibles.Objects, static item => item.Owner.Id == 1600);
        DestructibleObject fuelTruck = Assert.Single(destructibles.Objects, static item =>
            item.Owner is DeclaredTask task
            && task.TryGetValue("Model", out object? model)
            && Equals(model, "439_01_1"));

        Assert.Equal(100f, tank.ExplosionDamageThreshold);
        Assert.Equal(8f, fuelTruck.ExplosionDamageThreshold);
        for (int shot = 0; shot < 333; shot++)
        {
            Assert.Null(destructibles.TryDestroy(tank.Owner, tank.Placement, damage: 0.3f));
        }
        destructibles.Tick();
        Assert.False(tank.IsDestroyed);
        Assert.Null(destructibles.TryDestroy(tank.Owner, tank.Placement, damage: 0.3f));
        Assert.Equal(1, ExplodeObjectCountdown.RunUntilExploded(destructibles, tank));

        for (int shot = 0; shot < 26; shot++)
        {
            Assert.Null(destructibles.TryDestroy(fuelTruck.Owner, fuelTruck.Placement, damage: 0.3f));
        }
        destructibles.Tick();
        Assert.False(fuelTruck.IsDestroyed);
        Assert.Null(destructibles.TryDestroy(fuelTruck.Owner, fuelTruck.Placement, damage: 0.3f));
        Assert.Equal(1, ExplodeObjectCountdown.RunUntilExploded(destructibles, fuelTruck));
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
        (LevelTask root, LoadedMission mission, ScriptVarRegistry variables) = Load(6);
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        VehiclePhysicsRegistry physics = new();
        Assert.True(physics.Load(new ResourceRegistry(fileSystem)) > 0);
        Assert.True(physics.TryGetCar("601_01_1", out CarPhysicsDefinition apcPhysics));
        Assert.Equal("601_08_1", apcPhysics.DestroyedModel);

        new ConditionalContainerGate(variables).Prepare(root);
        WorldScene scene = WorldScene.Build(mission, root);
        GameTask internalGate = Assert.IsAssignableFrom<GameTask>(root.Tree.FromId(1114));
        bool internalGateOpen = false;
        CutsceneRuntime runtime = new(
            root,
            variables,
            vehiclePhysics: physics,
            vehicleObstacleHit: (vehicle, _, _) => vehicle.Id == 801
                ? (internalGate, default, "test internal gate")
                : null,
            vehicleObstacleReleased: obstacle => ReferenceEquals(obstacle, internalGate) && internalGateOpen,
            terrain: mission.Terrain)
        {
            CollisionIndex = ObjectCollisionIndex.Build(scene.Placements),
        };
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

        // ConditionalContainer_350's own isRun is the script-visible latch used by its alarm arm.
        // Set it through the same exported variable rather than treating the inactive object as solid.
        Assert.True(variables.TryWrite("ConditionalContainer_350.isRun", QvmValue.FromInt32(1)));
        runtime.Tick();
        Assert.False(ConditionalContainerGate.IsTaskActive(fakeApc));
        Assert.True(ConditionalContainerGate.IsTaskActive(alarmApc));
        runtime.Tick(); // SetSpeed(20) has advanced; this tick begins the graph-routed WalkTo(1).
        Assert.True(runtime.TryGetDrawTransform(alarmApc, out Vector3d movedApc, out _));
        Assert.NotEqual(new Vector3d(25047706d, 87471776d, 174684608d), movedApc * WorldScene.UnitsPerMetre);

        DestructibleObjectRegistry destructibles = DestructibleObjectRegistry.Build(
            root,
            scene.Placements,
            variables,
            physics);
        runtime.Destructibles = destructibles;
        DestructibleObject liveApc = Assert.Single(destructibles.Objects, item => ReferenceEquals(item.Owner, alarmApc));
        DestructibleObject dormantApc = Assert.Single(destructibles.Objects, item => ReferenceEquals(item.Owner, fakeApc));
        Assert.Equal(DestructibleObjectKind.Vehicle, liveApc.Kind);
        Assert.Equal("601_08_1", liveApc.DestroyedModelName);

        // A blast with no LAW behind it (a grenade's) leaves the APC alone; the LAW's starts it dying.
        _ = destructibles.ApplyExplosionHits(
            null,
            liveApc.PositionUnits,
            radiusUnits: 1f,
            canContact: item => ConditionalContainerGate.IsTaskActive(item.Owner));
        Assert.False(liveApc.IsDying);
        _ = destructibles.ApplyExplosionHits(
            null,
            liveApc.PositionUnits,
            radiusUnits: 1f,
            canContact: item => ConditionalContainerGate.IsTaskActive(item.Owner),
            sourceWeaponId: DestructibleObjectRegistry.CarDestroyingWeaponId);
        Assert.True(liveApc.IsDying);
        for (int tick = 0; tick <= DestructibleObjectRegistry.CarPointBlastTicks; tick++)
        {
            destructibles.Tick();
        }

        Assert.True(liveApc.IsDestroyed);
        Assert.False(dormantApc.IsDying);
        Assert.False(dormantApc.IsDestroyed);

        GameTask fuelTankOne = Assert.IsAssignableFrom<GameTask>(root.Tree.FromId(1600));
        GameTask fuelTankTwo = Assert.IsAssignableFrom<GameTask>(root.Tree.FromId(1601));
        GameTask limo = Assert.IsAssignableFrom<GameTask>(root.Tree.FromId(801));
        Assert.False(ConditionalContainerGate.IsTaskActive(limo));
        Assert.Null(destructibles.TryDestroy(fuelTankOne, damage: 100f));
        Assert.Null(destructibles.TryDestroy(fuelTankTwo, damage: 100f));
        destructibles.Tick(); // Both countdowns stand at 0, so both explode on this update.
        runtime.Tick();
        Assert.True(ConditionalContainerGate.IsTaskActive(limo));
        runtime.Tick(); // SetSpeed(50) has advanced; this tick begins WalkTo(1).
        Assert.True(runtime.TryGetDrawTransform(limo, out Vector3d waitingLimo, out _));
        Assert.Equal(new Vector3d(24374838d, 89438272d, 174505184d), waitingLimo * WorldScene.UnitsPerMetre);

        internalGateOpen = true;
        runtime.Tick();
        Assert.True(runtime.TryGetDrawTransform(limo, out Vector3d movedLimo, out _));
        Assert.NotEqual(new Vector3d(24374838d, 89438272d, 174505184d), movedLimo * WorldScene.UnitsPerMetre);

        // Route 607 ends at the helipad. AreaActivate_162 is vehicle-only and disables the limo's
        // conditional container as soon as the live car enters it, reproducing retail's disappearance.
        MissionObjectiveRuntime objectives = new(root, variables);
        bool reachedHelipad = false;
        for (int tick = 0; tick < 12_000 && ConditionalContainerGate.IsTaskActive(limo); tick++)
        {
            objectives.SetVehicleOccupants(runtime.VehicleOccupants());
            objectives.TickLevelState();
            if (variables.TryRead("AreaActivate_162.nActive", out QvmValue active)
                && active.ToInt32() > 0)
            {
                reachedHelipad = true;
            }
            runtime.Tick();
        }

        Assert.True(reachedHelipad, "the limo never entered retail's vehicle-only helipad volume");
        Assert.False(ConditionalContainerGate.IsTaskActive(limo));
        Assert.True(runtime.TryGetDrawTransform(limo, out Vector3d vanishedAt, out _));
        runtime.Tick();
        Assert.True(runtime.TryGetDrawTransform(limo, out Vector3d stillVanishedAt, out _));
        Assert.Equal(vanishedAt, stillVanishedAt);
    }

    [RequiresGameDataFact]
    public void MissionSixFuelTankCollisionResolvesToItsExactDestructibleOwner()
    {
        (LevelTask root, LoadedMission mission, ScriptVarRegistry variables) = Load(6);
        WorldScene scene = WorldScene.Build(mission, root);
        DeclaredTask tank = Assert.IsType<DeclaredTask>(root.Tree.FromId(1600));
        WorldPlacement placement = Assert.Single(scene.Placements, candidate => ReferenceEquals(candidate.Owner, tank));
        ObjectCollisionIndex collision = ObjectCollisionIndex.Build([placement]);
        Assert.True(collision.Count > 0, "the visible fuel tank has no bullet collision mesh");

        (Vector3d centre, double radius) = collision.Bounds(0);
        double reach = Math.Max(radius * 2d, WorldScene.UnitsPerMetre * 2d);
        Assert.True(
            collision.TryTrace(
                new WorldTraceRequest(
                    new Vector3d(centre.X - reach, centre.Y, centre.Z),
                    new Vector3d(centre.X + reach, centre.Y, centre.Z)),
                out WorldTraceHit hit),
            "a shot through the fuel-tank collision bounds did not hit the tank");
        Assert.Same(tank, hit.Owner);
        Assert.Same(placement, hit.Placement);

        DestructibleObjectRegistry destructibles = DestructibleObjectRegistry.Build(root, scene.Placements, variables);
        Assert.Null(destructibles.TryDestroy(hit.Owner, hit.Placement, damage: 0.3f));
        Assert.Equal(0.3f, Assert.Single(destructibles.Objects, item => ReferenceEquals(item.Owner, tank))
            .ExplosionDamageAccumulated, precision: 3);
        Assert.Null(destructibles.TryDestroy(hit.Owner, hit.Placement, damage: 99.7f));
        DestructibleObject destroyed = Assert.Single(destructibles.Objects, item => ReferenceEquals(item.Owner, tank));
        Assert.Equal(1, ExplodeObjectCountdown.RunUntilExploded(destructibles, destroyed));
    }

    [RequiresGameDataFact]
    public void MissionSixAlarmApcHasAnActiveRocketCollisionTarget()
    {
        (LevelTask root, LoadedMission mission, ScriptVarRegistry variables) = Load(6);
        new ConditionalContainerGate(variables).Prepare(root);
        WorldScene scene = WorldScene.Build(mission, root);
        GameTask apc = Assert.IsAssignableFrom<GameTask>(root.Tree.FromId(800));
        WorldPlacement placement = Assert.Single(scene.Placements,
            candidate => ReferenceEquals(candidate.Owner, apc));

        // The live APC is alarm-gated. Its collision begins at the authored placement, then must
        // follow the same graph-driven transform that rendering uses once the alarm releases it.
        Assert.False(ConditionalContainerGate.IsTaskActive(apc));

        ObjectCollisionIndex collision = ObjectCollisionIndex.Build([placement]);
        Assert.True(collision.Count > 0, "the live APC has no rocket collision mesh");
        (Vector3d initialCentre, double radius) = collision.Bounds(0);

        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        VehiclePhysicsRegistry physics = new();
        Assert.True(physics.Load(new ResourceRegistry(fileSystem)) > 0);
        DestructibleObjectRegistry destructibles = DestructibleObjectRegistry.Build(
            root, scene.Placements, variables, physics);
        DestructibleObject liveApc = Assert.Single(
            destructibles.Objects, item => ReferenceEquals(item.Owner, apc));
        Vector3d initialDestructiblePosition = liveApc.PositionUnits;

        CutsceneRuntime runtime = new(root, variables, terrain: mission.Terrain, vehiclePhysics: physics)
        {
            CollisionIndex = collision,
            Destructibles = destructibles,
        };
        string graphPath = Path.Combine(
            GameData.RequireRoot(), "MISSIONS", "location0", "LEVEL6", "GRAPHS", "GRAPH1.DAT");
        GraphFile graph = GraphFile.Read(File.ReadAllBytes(graphPath));
        DeclaredTask graphTask = Assert.IsType<DeclaredTask>(root.Tree.FromId(1));
        Assert.True(graphTask.TryGetValue("Graph position", out object? rawGraphPosition));
        graph.ApplyWorldOffset(Assert.IsType<Vector3d>(rawGraphPosition));
        runtime.ConfigureCarAi(new Dictionary<int, GraphFile> { [1] = graph });

        Assert.True(variables.TryWrite("ConditionalContainer_350.isRun", QvmValue.FromInt32(1)));
        // CarAI drives its car through the car's own physics: the first packet only sets the receiver
        // up, then the throttle eases in. Give it a few seconds to pull away along WalkTo(1).
        for (int tick = 0; tick < 150; tick++)
        {
            runtime.Tick();
        }

        Assert.True(ConditionalContainerGate.IsTaskActive(apc));

        (Vector3d centre, double movedRadius) = collision.Bounds(0);
        Assert.NotEqual(initialCentre, centre);
        Assert.Equal(radius, movedRadius, precision: 3);
        Assert.NotEqual(initialDestructiblePosition, liveApc.PositionUnits);
        Assert.True(runtime.TryGetDrawTransform(apc, out Vector3d drawPosition, out _));
        Vector3d drawPositionUnits = drawPosition * WorldScene.UnitsPerMetre;
        Assert.Equal(drawPositionUnits.X, liveApc.PositionUnits.X, precision: 3);
        Assert.Equal(drawPositionUnits.Y, liveApc.PositionUnits.Y, precision: 3);
        Assert.Equal(drawPositionUnits.Z, liveApc.PositionUnits.Z, precision: 3);
        double reach = Math.Max(radius * 2d, WorldScene.UnitsPerMetre * 2d);
        Assert.True(collision.TryTrace(
            new WorldTraceRequest(
                new Vector3d(centre.X - reach, centre.Y, centre.Z),
                new Vector3d(centre.X + reach, centre.Y, centre.Z)),
            out WorldTraceHit hit), "a rocket through the live APC bounds did not hit it");
        Assert.Same(apc, hit.Owner);
        Assert.Same(placement, hit.Placement);

        MissileSimulation missile = new();
        Assert.True(missile.Spawn(
            hit.Position - new Vector3d(1024d, 0d, 0d),
            hit.Position + new Vector3d(1024d, 0d, 0d),
            MissileType.MissileDirect,
            muzzleVelocityMetresPerSecond: 40f,
            sourceWeaponId: DestructibleObjectRegistry.CarDestroyingWeaponId));
        missile.TickWithMissileContext(
            (LiveMissile _, Vector3d _, Vector3d _, out WorldTraceHit directHit) =>
            {
                directHit = hit;
                return true;
            },
            humans: null);
        MissileDetonation detonation = Assert.Single(missile.Detonations);
        Assert.Same(apc, detonation.DirectHitOwner);
        Assert.Same(placement, detonation.DirectHitPlacement);
        Assert.Equal(DestructibleObjectRegistry.CarDestroyingWeaponId, detonation.SourceWeaponId);

        // The rocket's Explosion0 starts the APC dying (0x42FB40); it is not yet a wreck.
        Assert.Empty(destructibles.ApplyExplosionHits(
            null,
            detonation.Position,
            detonation.RadiusUnits,
            detonation.FalloffUnits,
            detonation.DamageFactor,
            sourceWeaponId: detonation.SourceWeaponId));
        Assert.True(liveApc.IsDying);
        Assert.False(liveApc.IsDestroyed);
        Assert.False(destructibles.ShouldExplode(liveApc));

        // Five explosion points, so thirty ticks after the tick that set them off.
        for (int tick = 0; tick <= DestructibleObjectRegistry.CarPointBlastTicks; tick++)
        {
            destructibles.Tick();
        }

        Assert.True(liveApc.IsDestroyed);

        Assert.True(runtime.TryGetDrawTransform(apc, out Vector3d wreckPosition, out _));
        runtime.ConfigureVehicleCombat(apc, new WeaponType
        {
            ScriptId = "WEAPON_ID_APC",
            MuzzleVelocity = 5f,
            RoundsPerMinute = 12,
        });
        List<VehicleCombatShot> postDestructionShots = [];
        runtime.VehicleCombatTarget = () => wreckPosition * WorldScene.UnitsPerMetre
            + new Vector3d(0d, 37.0137d * WorldScene.UnitsPerMetre, 0d);
        runtime.VehicleCombatLineOfSight = static (_, _, _) => true;
        runtime.VehicleCombatShotFired = postDestructionShots.Add;

        for (int tick = 0; tick < 8; tick++)
        {
            runtime.Tick();
        }

        Assert.True(runtime.TryGetDrawTransform(apc, out Vector3d stoppedWreckPosition, out _));
        Assert.Equal(wreckPosition.X, stoppedWreckPosition.X, precision: 6);
        Assert.Equal(wreckPosition.Y, stoppedWreckPosition.Y, precision: 6);
        Assert.Equal(wreckPosition.Z, stoppedWreckPosition.Z, precision: 6);
        Assert.Empty(postDestructionShots);
    }

    [RequiresGameDataFact]
    public void MissionSixAlarmApcAcquiresPlayerAndEmitsRetailDirectMissile()
    {
        (LevelTask root, LoadedMission mission, ScriptVarRegistry variables) = Load(6);
        new ConditionalContainerGate(variables).Prepare(root);
        DeclaredTask apc = Assert.IsType<DeclaredTask>(root.Tree.FromId(800));
        Assert.True(apc.TryGetValue("Position", out object? rawPosition));
        Vector3d playerEye = Assert.IsType<Vector3d>(rawPosition)
            + new Vector3d(0d, 37.0137d * WorldScene.UnitsPerMetre, 0d);

        CutsceneRuntime runtime = new(root, variables, terrain: mission.Terrain);
        string graphPath = Path.Combine(
            GameData.RequireRoot(), "MISSIONS", "location0", "LEVEL6", "GRAPHS", "GRAPH1.DAT");
        GraphFile graph = GraphFile.Read(File.ReadAllBytes(graphPath));
        DeclaredTask graphTask = Assert.IsType<DeclaredTask>(root.Tree.FromId(1));
        Assert.True(graphTask.TryGetValue("Graph position", out object? rawGraphPosition));
        graph.ApplyWorldOffset(Assert.IsType<Vector3d>(rawGraphPosition));
        runtime.ConfigureCarAi(new Dictionary<int, GraphFile> { [1] = graph });
        runtime.ConfigureVehicleCombat(apc, new WeaponType
        {
            ScriptId = "WEAPON_ID_APC",
            MuzzleVelocity = 5f,
            RoundsPerMinute = 12,
        });

        List<VehicleCombatShot> shots = [];
        runtime.VehicleCombatTarget = () => playerEye;
        runtime.VehicleCombatLineOfSight = static (_, _, _) => true;
        runtime.VehicleCombatShotFired = shots.Add;

        Assert.True(variables.TryWrite("ConditionalContainer_350.isRun", QvmValue.FromInt32(1)));
        runtime.Tick(); // Target becomes visible and is retained; retail does not fire in this pass.
        Assert.Empty(shots);
        runtime.Tick(); // The following CarAI/Gun_Fire pass submits the first missile.

        VehicleCombatShot shot = Assert.Single(shots);
        Assert.Equal("WEAPON_ID_APC", shot.WeaponScriptId);
        Assert.NotEqual("WEAPON_ID_RPG18", shot.WeaponScriptId);
        Assert.NotEqual("WEAPON_ID_T80", shot.WeaponScriptId);
        Assert.Equal(MissileType.MissileDirect, shot.MissileType);
        Assert.Equal(MissileFlight.FallbackLaunchMetresPerSecond, shot.MuzzleVelocityMetresPerSecond);
        Assert.Equal("110_02_1", shot.ModelName);
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
