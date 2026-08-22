using OpenIGI.Engine.Flow;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

public sealed class RetailInteractionInventoryTests
{
    private static readonly HashSet<string> InteractiveTypes = new(StringComparer.Ordinal)
    {
        "Wire", "Fence", "Ladder", "Terminal", "Cabinet", "Switch", "Door",
        "GenericTBA", "PlaceExplosiveTBA", "StationaryGun",
    };

    [Fact]
    public void MedipackPickupFallbackProvidesAUsableChargeCount()
    {
        Assert.Equal(50, AmmoPickupCatalog.DefaultAmount(PlayerWeapons.MedipackAmmoId));
    }

    [RequiresGameDataFact]
    public void LevelTwoGenericExplosiveChargesPublishTheirAuthoredBlastExactlyOnce()
    {
        (LevelTask root, MagicObjectRegistry magic, LoadedMission mission, ScriptVarRegistry variables) = Load(2);
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
            root,
            mission,
            DoorRegistry.Build(root),
            variables);

        List<WorldInteraction> charges = interactions.Items
            .Where(static item => item.Type == InteractionActivateType.Generic && item.Explosion is not null)
            .ToList();
        Assert.Equal(3, charges.Count);

        WorldInteraction charge = Assert.Single(charges, static item => item.Task.Id == 282);
        GenericTbaExplosionParameters payload = Assert.IsType<GenericTbaExplosionParameters>(charge.Explosion);
        Assert.False(payload.IsExplodable);
        Assert.Equal(1f, payload.DamageScale);
        Assert.Equal(0.5f, payload.ExplosionRadiusMetres);
        Assert.Equal(0.5f, payload.ExplosionFalloffMetres);
        Assert.Equal(1f, payload.ExplosionDamageScale);
        Assert.Equal(0f, payload.ExplosionDelaySeconds);
        Assert.Equal(5, payload.FragmentCount);
        Assert.Equal(4, payload.FireballCount);
        Assert.Equal(string.Empty, payload.DestroyedModelName);
        Assert.Equal(string.Empty, payload.ExplosionExpression);
        Assert.Equal("explo_02_m", payload.ExplosionSound);

        charge.BeginUse();
        charge.CompleteUse();

        Assert.True(charge.IsCompleted);
        Assert.False(charge.IsDestroyed);
        Assert.True(variables.TryRead("GenericTBA_282.isFinished", out var finished));
        Assert.Equal(1, finished.Integer);
        Assert.True(variables.TryRead("GenericTBA_282.isFinishedThisTick", out var finishedThisTick));
        Assert.Equal(1, finishedThisTick.Integer);
        (WorldInteraction emitted, GenericTbaExplosionParameters emittedPayload) = Assert.Single(
            interactions.ConsumeExplosions());
        Assert.Same(charge, emitted);
        Assert.Equal(payload, emittedPayload);
        Assert.Empty(interactions.ConsumeExplosions());

        charge.MarkExplosionPresented();
        Assert.True(charge.IsDestroyed);

        interactions.Tick();
        Assert.Empty(interactions.ConsumeExplosions());
    }

    [RequiresGameDataFact]
    public void LevelTwoGenericExplosiveChargeStopsBlockingWorldTracesAfterPresentation()
    {
        (LevelTask root, MagicObjectRegistry magic, LoadedMission mission, ScriptVarRegistry variables) = Load(2);
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
            root,
            mission,
            DoorRegistry.Build(root),
            variables);
        WorldInteraction charge = Assert.Single(
            interactions.Items,
            static item => item.Type == InteractionActivateType.Generic && item.Task.Id == 282);
        WorldScene scene = WorldScene.Build(mission, root, magic);
        WorldPlacement placement = Assert.Single(
            scene.Placements,
            candidate => ReferenceEquals(candidate.Owner, charge.Task)
                && string.Equals(candidate.Instance.Name, "202_01_1", StringComparison.OrdinalIgnoreCase));
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(scene.Placements);

        WorldTraceRequest request = TraceThrough(placement.Position);
        Assert.True(
            HitsOwner(solids, request, charge.Task),
            "the authored charge has no collision trace");

        charge.BeginUse();
        charge.CompleteUse();
        Assert.False(charge.IsDestroyed);

        charge.MarkExplosionPresented(solids);

        Assert.True(charge.IsDestroyed);
        Assert.False(
            HitsOwner(solids, request, charge.Task),
            "the destroyed charge remained solid after detonation");

        static WorldTraceRequest TraceThrough(Vector3d positionMetres)
        {
            Vector3d centre = new(
                positionMetres.X * WorldScene.UnitsPerMetre,
                positionMetres.Y * WorldScene.UnitsPerMetre,
                positionMetres.Z * WorldScene.UnitsPerMetre);
            double span = WorldScene.UnitsPerMetre * 2.0;
            return new WorldTraceRequest(
                centre - new Vector3d(span, 0.0, 0.0),
                centre + new Vector3d(span, 0.0, 0.0),
                WorldTraceMask.World);
        }

        static bool HitsOwner(ObjectCollisionIndex solids, WorldTraceRequest request, GameTask owner)
        {
            if (!solids.TryTrace(request, out WorldTraceHit hit))
            {
                return false;
            }

            if (ReferenceEquals(hit.Owner, owner))
            {
                return true;
            }

            return hit.Owner is { } blocker
                && solids.TryTrace(request with { IgnoredOwner = blocker }, out WorldTraceHit next)
                && ReferenceEquals(next.Owner, owner);
        }
    }

    [RequiresGameDataFact]
    public void MissionThreeAmmoDoorCanOnlyBeOpenedFromItsPoweredTerminal()
    {
        (LevelTask root, MagicObjectRegistry magic, LoadedMission mission, ScriptVarRegistry variables) =
            Load(3, loadTerrain: true);
        DoorRegistry doors = DoorRegistry.Build(root);
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(root, mission, doors, variables);

        Assert.True(variables.TryRead("Generator_99.isOn", out var generatorPower));
        Assert.Equal(1, generatorPower.Integer);

        WorldInteraction terminal = Assert.Single(
            interactions.Items,
            static item => item.Type == InteractionActivateType.Terminal && item.Task.Id == 552);
        WorldInteraction door = Assert.Single(
            interactions.Items,
            static item => item.Type == InteractionActivateType.Door
                && item.Task.TryGetValue("Open door expression", out object? expression)
                && string.Equals(expression as string, "Terminal_552.isHackedThisTick", StringComparison.Ordinal));

        interactions.Tick();
        Assert.True(terminal.IsAvailable);

        // F12 telemetry reports the body root rather than the HumanView eye used by the retail scan.
        // It still gives us the exact side from which this keypad must offer itself. Keep that side,
        // move inside the verified 8192-unit Human_UpdateActivate radius, and aim at magic vertex 1.
        Vector3d reportedPlayerPosition = new(23802971.0, -55285329.0, 174418631.0);
        Assert.True(terminal.CanOfferFrom(reportedPlayerPosition));
        (Vector3d terminalPose, _) = terminal.BodyPose(reportedPlayerPosition);
        Vector3d terminalPoseLocal = terminal.Orientation.Transform(terminalPose - terminal.Anchor);
        Assert.Equal(0.0, terminalPoseLocal.X, 3);
        Assert.Equal(2764.8, terminalPoseLocal.Y, 3);
        Assert.Equal(0.0, terminalPoseLocal.Z, 3);
        WorldScene scene = WorldScene.Build(mission, root, magic);
        PlayerBody interactionBody = new()
        {
            Position = terminalPose,
            Walls = ObjectCollisionIndex.Build(scene.Placements).PlayerCaster(),
        };
        Assert.True(interactionBody.SnapInteractionToGround(Assert.IsType<TerrainData>(mission.Terrain)));
        Assert.True(interactionBody.IsGrounded);
        Assert.True(interactionBody.Position.Z < terminalPose.Z);
        Vector3d retailEye = terminal.Anchor
            + ((reportedPlayerPosition - terminal.Anchor).Normalized()
                * (WorldInteractionRegistry.ActivateRadius * 0.75));
        Vector3d towardTerminal = terminal.Anchor - retailEye;
        float terminalYaw = (float)Math.Atan2(towardTerminal.X, towardTerminal.Y);
        float terminalPitch = (float)Math.Atan2(
            towardTerminal.Z,
            Math.Sqrt((towardTerminal.X * towardTerminal.X) + (towardTerminal.Y * towardTerminal.Y)));
        Assert.Same(
            terminal,
            interactions.Resolve(retailEye, terminalYaw, terminalPitch));
        Assert.True(door.IsLocked);
        Assert.False(door.IsPickable);
        Assert.False(door.IsAvailable);

        terminal.BeginUse();
        terminal.CompleteUse();
        interactions.Tick();
        doors.Tick();

        Assert.False(door.Door!.IsFullyClosed);
    }

    [RequiresGameDataFact]
    public void EveryRetailDirectInteractionHasARegisteredActivateType()
    {
        Dictionary<string, int> totals = InteractiveTypes.ToDictionary(static name => name, static _ => 0);
        AmmoTypeRegistry ammoTypes = LoadAmmoTypes();
        int genericPickupTotal = 0;
        for (int missionId = 1; missionId <= 14; missionId++)
        {
            (LevelTask root, MagicObjectRegistry magic, LoadedMission mission, ScriptVarRegistry variables) = Load(missionId);
            Dictionary<string, List<DeclaredTask>> found = Walk(root)
                .OfType<DeclaredTask>()
                .Where(static task => InteractiveTypes.Contains(task.Type.Name))
                .GroupBy(static task => task.Type.Name, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.ToList(), StringComparer.Ordinal);

            foreach ((string name, List<DeclaredTask> tasks) in found.OrderBy(static pair => pair.Key))
            {
                totals[name] += tasks.Count;
            }

            DoorRegistry doors = DoorRegistry.Build(root);
            WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
                root, mission, doors, variables);
            List<DeclaredTask> pickupTasks = Walk(root)
                .OfType<DeclaredTask>()
                .Where(static task => string.Equals(task.Type.Name, "GenericPickup", StringComparison.Ordinal)
                    || string.Equals(task.Type.Name, "AmmoPickup", StringComparison.Ordinal))
                .ToList();
            GenericPickupRegistry genericPickups = GenericPickupRegistry.Build(root, variables);
            genericPickupTotal += pickupTasks.Count;
            Assert.Equal(pickupTasks.Count, genericPickups.Items.Count);
            foreach (GenericPickup pickup in genericPickups.Items)
            {
                Assert.Contains(pickup.Task, pickupTasks);
                Assert.True(variables.TryRead($"{pickup.Task.Name}.isPickedUp", out var pickedUp));
                Assert.Equal(0, pickedUp.Integer);

                if (string.Equals(pickup.Task.Type.Name, "AmmoPickup", StringComparison.Ordinal))
                {
                    Assert.True(AmmoPickupCatalog.TryResolve(pickup, ammoTypes, out AmmoType ammo));
                    Assert.Equal(ammo.Model, pickup.ModelName);
                    Assert.True(pickup.Task.TryGetValue("Ammo", out object? rawQuantity));
                    int quantity = rawQuantity switch
                    {
                        short value => value,
                        int value => value,
                        _ => -1,
                    };
                    Assert.True(quantity > 0);
                    Assert.Equal(quantity, AmmoPickupCatalog.Amount(pickup, ammo.AmmoId));
                }
            }
            WorldScene scene = WorldScene.Build(mission, root, magic);
            foreach (GenericPickup pickup in genericPickups.Items.Where(
                         static item => string.Equals(item.Task.Type.Name, "AmmoPickup", StringComparison.Ordinal)))
            {
                // Inventory resolution alone is not enough: an AmmoPickup must also survive the
                // scene walk as a drawable placement, otherwise the player can never see or collect
                // the authored object in-game.
                WorldPlacement placement = Assert.Single(
                    scene.Placements,
                    candidate => ReferenceEquals(candidate.Owner, pickup.Task)
                        && string.Equals(candidate.Instance.Name, pickup.ModelName, StringComparison.OrdinalIgnoreCase));
                Assert.Equal(pickup.Position / WorldScene.UnitsPerMetre, placement.Position);
                Assert.NotNull(MefMeshBuilder.Build(placement.Model));
            }
            Assert.Equal(found.Values.Sum(static tasks => tasks.Count), interactions.Items.Count);
            foreach (WorldInteraction interaction in interactions.Items.Where(static item => item.Type is
                         InteractionActivateType.Door or InteractionActivateType.Cabinet
                         or InteractionActivateType.Terminal or InteractionActivateType.Generic
                         or InteractionActivateType.PlaceExplosive))
            {
                string durationParameter = interaction.Type switch
                {
                    InteractionActivateType.Door => "Pick lock time (s)",
                    InteractionActivateType.Cabinet => "Search Time (s)",
                    InteractionActivateType.Terminal => "Hack Time (s)",
                    _ => "Activate Time (s)",
                };
                Assert.Equal(
                    Math.Max(1, (int)(ReadReal(interaction.Task, durationParameter, 4f) * 30f)),
                    interaction.DurationTicks);
                if (interaction.Type is InteractionActivateType.Terminal or InteractionActivateType.Generic)
                {
                    (Vector3d pose, _) = interaction.BodyPose(interaction.Anchor);
                    Vector3d localPose = interaction.Orientation.Transform(pose - interaction.Anchor);
                    double expectedForward = interaction.Type == InteractionActivateType.Terminal
                        ? 2764.8
                        : 3481.6;
                    Assert.Equal(0.0, localPose.X, 3);
                    Assert.Equal(expectedForward, localPose.Y, 3);
                    Assert.Equal(0.0, localPose.Z, 3);
                }
            }
            foreach (WorldInteraction door in interactions.Items.Where(
                static item => item.Type == InteractionActivateType.Door))
            {
                Assert.NotNull(door.Door);
                PlacedDoor placed = Assert.Single(
                    doors.Doors,
                    candidate => ReferenceEquals(candidate.Task, door.Task));
                Assert.Same(placed.State, door.Door);

                Vector3d normal = door.Orientation.Transposed().Transform(new Vector3d(0.0, 2048.0, 0.0));
                Vector3d standing = door.Anchor + normal;
                Assert.True(door.CanOfferFrom(standing));
                Assert.Equal(door.Anchor, door.CandidatePoint(standing));

                Vector3d outside = door.Anchor
                    + door.Orientation.Transposed().Transform(new Vector3d(2048.0, 0.0, 0.0));
                Assert.False(door.CanOfferFrom(outside));
            }
            foreach (PlacedDoor door in doors.Doors)
            {
                WorldPlacement placement = Assert.Single(
                    scene.Placements,
                    candidate => ReferenceEquals(candidate.Owner, door.Task));
                Assert.Equal(placement.Position, door.DrawPosition);
                Assert.Equal(placement.Orientation, door.DrawOrientation);

                door.State.Toggle();
                DeclaredTask doorTask = Assert.IsType<DeclaredTask>(door.Task);
                int motionTicks = Math.Max(1, (int)Math.Ceiling(ReadReal(doorTask, "Open time", 1f) * 30f));
                for (int tick = 0; tick <= motionTicks; tick++)
                {
                    door.State.Tick();
                }

                Assert.True(door.State.IsFullyOpen, $"{door.Task.Name} did not finish opening");
                bool hasMotion = ReadReal(doorTask, "Position stop X", 0f) != 0f
                    || ReadReal(doorTask, "Position stop Y", 0f) != 0f
                    || ReadReal(doorTask, "Position slider", 0f) != 0f
                    || ReadReal(doorTask, "Max angle", 0f) != 0f;
                if (hasMotion)
                {
                    Assert.True(
                        door.DrawPosition != placement.Position || door.DrawOrientation != placement.Orientation,
                        $"{door.Task.Name} became passable without changing its visible transform");
                }

                door.State.Toggle();
                for (int tick = 0; tick <= motionTicks; tick++)
                {
                    door.State.Tick();
                }

                Assert.True(door.State.IsFullyClosed, $"{door.Task.Name} did not close on a second use");
                Assert.Equal(placement.Position, door.DrawPosition);
                Assert.Equal(placement.Orientation, door.DrawOrientation);
            }
            foreach (WorldInteraction wire in interactions.Items.Where(
                static item => item.Type == InteractionActivateType.Wire))
            {
                WorldPlacement placement = Assert.Single(
                    scene.Placements,
                    candidate => ReferenceEquals(candidate.Owner, wire.Task));
                Assert.NotNull(placement.BentGeometry);
                Assert.True(WireTransform.TryCreate(wire.Task, placement.Model, out WireTransform transform));
                Assert.Equal(transform.PositionMetres, placement.Position);
                Assert.Equal(transform.Orientation, placement.Orientation);
                Assert.Equal(transform.CableStart, wire.Anchor);
                Assert.Equal(transform.CableEnd, wire.End);

                Vector3d mount = wire.WireMountPoint(wire.Anchor - ((wire.End - wire.Anchor) * 10.0));
                double expected = Math.Min(3072.0, (wire.End - wire.Anchor).Length * 0.49);
                Assert.Equal(expected, Vector3d.Distance(wire.Anchor, mount), precision: 3);

                Vector3d body = wire.WireBodyPoint(
                    wire.Anchor - ((wire.End - wire.Anchor) * 10.0));
                Vector3d attachment = mount - body;
                Assert.Equal(3686.4, attachment.Length, precision: 3);
                Assert.Equal(
                    0.0,
                    Vector3d.Dot(attachment, (wire.End - wire.Anchor).Normalized()),
                    precision: 3);
            }
            foreach (WorldInteraction fence in interactions.Items.Where(
                         static item => item.Type == InteractionActivateType.Fence))
            {
                WorldPlacement placement = Assert.Single(
                    scene.Placements,
                    candidate => ReferenceEquals(candidate.Owner, fence.Task));
                Assert.NotNull(MefMeshBuilder.Build(placement.Model));
                Assert.Equal(fence.Orientation, placement.Orientation);
                Assert.True(fence.HasLocalBounds);
                Assert.Equal(WorldInteraction.FenceTraversalTicks, fence.DurationTicks);

                double x = fence.LocalBoundsMaximum.X - 1.0;
                double z = (fence.LocalBoundsMinimum.Z + fence.LocalBoundsMaximum.Z) * 0.5;
                double outsideY = fence.LocalBoundsMaximum.Y + 2048.0;
                Vector3d positiveSide = fence.Anchor + fence.Orientation.Transposed().Transform(
                    new Vector3d(x, outsideY, z));
                Vector3d negativeSide = fence.Anchor + fence.Orientation.Transposed().Transform(
                    new Vector3d(x, fence.LocalBoundsMinimum.Y - 2048.0, z));

                Assert.True(fence.CanOfferFrom(positiveSide));
                Assert.True(fence.CanOfferFrom(negativeSide));
                Vector3d offeredLocal = fence.Orientation.Transform(fence.CandidatePoint(positiveSide) - fence.Anchor);
                Assert.InRange(Math.Abs(offeredLocal.X - x), 0.0, 0.01);
                Assert.InRange(Math.Abs(offeredLocal.Y - fence.LocalBoundsMaximum.Y), 0.0, 0.01);

                (Vector3d mount, _) = fence.BodyPose(positiveSide);
                Vector3d mountLocal = fence.Orientation.Transform(mount - fence.Anchor);
                Assert.Equal(2457.6, mountLocal.Y, precision: 3);
                Assert.Equal(3891.2, mountLocal.Z, precision: 3);
                Vector3d exitLocal = fence.Orientation.Transform(fence.FenceExitPoint(mount) - fence.Anchor);
                Assert.Equal(-2457.6, exitLocal.Y, precision: 3);
            }
            foreach (WorldInteraction interaction in interactions.Items.Where(
                static item => item.Type == InteractionActivateType.StationaryGun))
            {
                DeclaredTask task = interaction.Task;
                AiStationaryGun gun = Assert.IsType<AiStationaryGun>(interaction.StationaryGun);
                Assert.Equal(task.Id, gun.TaskId);
                Assert.Equal(ReadReal(task, "Max up angle", 25f), gun.MaximumAlphaDegrees);
                Assert.Equal(ReadReal(task, "Max down angle", -25f), gun.MinimumAlphaDegrees);
                Assert.Equal(ReadReal(task, "Max sideways angle", 90f), gun.MaximumGammaDegrees);
                Assert.Equal(
                    task.TryGetValue("Ammo", out object? rawAmmo) && rawAmmo is int ammo ? ammo : -1,
                    gun.Ammunition);
            }
        }

        // Ladder is model-attached; every direct family appears in at least one shipped mission.
        foreach (string type in InteractiveTypes.Where(static name => name != "Ladder"))
        {
            Assert.True(totals[type] > 0, $"No shipped {type} task was loaded.");
        }
        Assert.True(genericPickupTotal > 0, "No shipped GenericPickup task was loaded.");
    }

    private static (LevelTask Root, MagicObjectRegistry Magic, LoadedMission Mission, ScriptVarRegistry Variables) Load(
        int missionId,
        bool loadTerrain = false)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        LevelLoader level = new(root, loop.TaskCommands, loop.Natives, resources);
        TerrainLoader? terrain = loadTerrain ? new TerrainLoader(resources, loop.Natives) : null;
        LoadedMission mission = new MissionLoader(missions, fileSystem, resources, level, null, terrain).Load(missionId);
        return (root, magic, mission, loop.Variables);
    }

    private static AmmoTypeRegistry LoadAmmoTypes()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        AmmoTypeRegistry ammo = new();
        ammo.Register(loop.Natives);
        ammo.Load(fileSystem, resources, loop.Natives);
        return ammo;
    }

    private static IEnumerable<GameTask> Walk(GameTask root)
    {
        foreach (GameTask child in root.Children)
        {
            yield return child;
            foreach (GameTask descendant in Walk(child))
            {
                yield return descendant;
            }
        }
    }

    private static float ReadReal(DeclaredTask task, string name, float fallback) =>
        task.TryGetValue(name, out object? value) && value is float real ? real : fallback;
}
