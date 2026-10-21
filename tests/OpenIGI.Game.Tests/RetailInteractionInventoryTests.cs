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
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

public sealed class RetailInteractionInventoryTests
{
    private static readonly HashSet<string> InteractiveTypes = new(StringComparer.Ordinal)
    {
        "Wire", "Fence", "Ladder", "Terminal", "Cabinet", "Switch", "Door",
        "GenericTBA", "PlaceExplosiveTBA", "StationaryGun",

        // A Terminal subtype (0x440C20 registers it under word_53C680): hacked and switched like any terminal.
        "SCameraControl",
    };

    /// <summary>
    /// Mission 14 streams its building sections in through AreaActivate containers. A task in one that is not running
    /// still has its variables -- retail binds them as it creates the task -- and is simply not in the world until the
    /// container's ACTIVATE (0x487A70) puts it there.
    /// </summary>
    [RequiresGameDataFact]
    public void A_door_in_a_dormant_section_keeps_its_exports_and_comes_into_the_world_with_it()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        _ = loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        LevelLoader level = new(root, loop.TaskCommands, loop.Natives, resources);
        LoadedMission mission = new MissionLoader(missions, fileSystem, resources, level).Load(14);
        ScriptVarRegistry variables = loop.Variables;
        ConditionalContainerTask section = root.Descendants().OfType<ConditionalContainerTask>()
            .First(static container => container.Descendants().Any(static task => task.Type.Name == "Door"));
        section.IsActive = false;
        DeclaredTask door = section.Descendants().OfType<DeclaredTask>().First(static task => task.Type.Name == "Door");

        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(root, mission, DoorRegistry.Build(root), variables);

        Assert.True(variables.TryRead($"{door.Name}.isClosed", out _), $"{door.Name}.isClosed is not exported.");
        Assert.True(variables.TryRead($"{door.Name}.nDoorOpenTicks", out _), $"{door.Name}.nDoorOpenTicks is not exported.");
        Assert.True(interactions.TryGet(door, out WorldInteraction? interaction));
        Assert.False(interaction!.IsInWorld);

        section.IsActive = true;
        Assert.True(interaction.IsInWorld);
    }

    [RequiresGameDataTheory]
    [InlineData(400, 500, "this.isClosed && Switch_500.isLastPressed", "this.isOpen && Switch_500.isLastPressed")]
    [InlineData(
        403,
        501,
        "this.isClosed &&\n(Switch_501.isLastPressed || Switch_502.isLastPressed)",
        "this.isOpen &&\n(Switch_501.isLastPressed || Switch_502.isLastPressed)")]
    public void MissionFiveSelfGatedDoorsOpenAndCloseFromTheirRetailSwitches(
        int doorId,
        int switchId,
        string expectedOpen,
        string expectedClose)
    {
        (LevelTask root, _, LoadedMission mission, ScriptVarRegistry variables) = Load(5);
        DoorRegistry doors = DoorRegistry.Build(root);
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(root, mission, doors, variables);
        WorldInteraction door = Assert.Single(interactions.Items, item => item.Task.Id == doorId);
        WorldInteraction doorSwitch = Assert.Single(interactions.Items, item => item.Task.Id == switchId);

        Assert.Equal(expectedOpen, ReadString(door.Task, "Open door expression"));
        Assert.Equal(expectedClose, ReadString(door.Task, "Close door expression"));
        Assert.True(door.Door!.IsFullyClosed);

        doorSwitch.Press();
        for (int tick = 0; tick < 4; tick++)
        {
            interactions.Tick();
            doors.Tick();
        }
        Assert.False(door.Door.IsFullyClosed);

        for (int tick = 0; tick <= 60; tick++)
        {
            interactions.Tick();
            doors.Tick();
        }
        Assert.True(door.Door.IsFullyOpen);

        doorSwitch.Press();
        for (int tick = 0; tick < 4; tick++)
        {
            interactions.Tick();
            doors.Tick();
        }
        Assert.False(door.Door.IsFullyOpen);
    }

    [RequiresGameDataFact]
    public void MissionThreeDoor472AutoClosesAfterItsRetailSelfTimer()
    {
        (LevelTask root, _, LoadedMission mission, ScriptVarRegistry variables) = Load(3);
        DoorRegistry doors = DoorRegistry.Build(root);
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(root, mission, doors, variables);
        WorldInteraction door = Assert.Single(interactions.Items, static item => item.Task.Id == 472);

        Assert.Equal(
            "this.nDoorOpenTicks > 10*GAME_FREQUENCY",
            ReadString(door.Task, "Close door expression"));

        door.Door!.CommandOpen();
        for (int tick = 0; tick <= 60; tick++)
        {
            interactions.Tick();
            doors.Tick();
        }
        Assert.True(door.Door.IsFullyOpen);

        for (int tick = 0; tick <= 300; tick++)
        {
            interactions.Tick();
            doors.Tick();
        }

        Assert.False(door.Door.IsFullyOpen);
    }

    [Fact]
    public void MedipackPickupFallbackProvidesAUsableChargeCount()
    {
        Assert.Equal(50, AmmoPickupCatalog.DefaultAmount(PlayerWeapons.MedipackAmmoId));
    }

    [RequiresGameDataFact]
    public void LevelTwoChargesOnlyFinishWhenPlanted()
    {
        // Retail (probed, 0x443EA0): planting GenericTBA_282 sets isFinished and isFinishedThisTick and nothing
        // else. Explodable is FALSE on all three charges, so the task never runs its ExplodeObject update: no
        // blast, no Explosion0, the tank keeps its model and its collision. The outro's own ExplodeObjects
        // are what blow up.
        (LevelTask root, MagicObjectRegistry magic, LoadedMission mission, ScriptVarRegistry variables) = Load(2);
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
            root, mission, DoorRegistry.Build(root), variables);
        WorldScene scene = WorldScene.Build(mission, root, magic);
        DestructibleObjectRegistry destructibles = DestructibleObjectRegistry.Build(root, scene.Placements, variables);
        List<WorldInteraction> charges = interactions.Items
            .Where(static item => item.Type == InteractionActivateType.Generic)
            .ToList();
        Assert.Equal([282, 284, 286], charges.Select(static item => item.Task.Id).Order());
        Assert.All(charges, static charge => Assert.False(Assert.IsType<bool>(
            charge.Task.TryGetValue("Explodable", out object? explodable) ? explodable : null)));
        Assert.All(charges, static charge => Assert.Equal(245, charge.ActivateAnimation));
        Assert.DoesNotContain(destructibles.Objects, item => charges.Any(charge => ReferenceEquals(charge.Task, item.Owner)));

        WorldInteraction charge = Assert.Single(charges, static item => item.Task.Id == 282);
        WorldPlacement placement = Assert.Single(
            scene.Placements,
            candidate => ReferenceEquals(candidate.Owner, charge.Task)
                && string.Equals(candidate.Instance.Name, "202_01_1", StringComparison.OrdinalIgnoreCase));
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(scene.Placements);
        Vector3d centre = placement.Position * WorldScene.UnitsPerMetre;
        double span = WorldScene.UnitsPerMetre * 2.0;
        WorldTraceRequest request = new(
            centre - new Vector3d(span, 0.0, 0.0),
            centre + new Vector3d(span, 0.0, 0.0),
            WorldTraceMask.World);
        Assert.True(HitsOwner(solids, request, charge.Task));

        Assert.True(charge.BeginUse());
        charge.CompleteUse();

        Assert.True(charge.IsCompleted);
        Assert.True(variables.TryRead("GenericTBA_282.isFinished", out QvmValue finished));
        Assert.Equal(1, finished.Integer);
        Assert.True(variables.TryRead("GenericTBA_282.isFinishedThisTick", out QvmValue finishedThisTick));
        Assert.Equal(1, finishedThisTick.Integer);
        interactions.Tick();
        destructibles.Tick();
        Assert.Empty(destructibles.ConsumeReadyExplosions());
        Assert.True(HitsOwner(solids, request, charge.Task));

        static bool HitsOwner(ObjectCollisionIndex collision, WorldTraceRequest trace, GameTask owner)
        {
            if (!collision.TryTrace(trace, out WorldTraceHit hit))
            {
                return false;
            }

            if (ReferenceEquals(hit.Owner, owner))
            {
                return true;
            }

            return hit.Owner is { } blocker
                && collision.TryTrace(trace with { IgnoredOwner = blocker }, out WorldTraceHit next)
                && ReferenceEquals(next.Owner, owner);
        }
    }

    [RequiresGameDataFact]
    public void LevelTwoAiActivationOnlyUsesAChargeAndThePlanterFinishesIt()
    {
        (LevelTask root, _, LoadedMission mission, ScriptVarRegistry variables) = Load(2);
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
            root, mission, DoorRegistry.Build(root), variables);
        WorldInteraction charge = Assert.Single(
            interactions.Items,
            static item => item.Type == InteractionActivateType.Generic && item.Task.Id == 282);

        // An AI's activate (0x454DA0) only uses what it holds: the charge is in use, not finished.
        interactions.Tick();
        Assert.True(interactions.ActivateFromAi(charge.Task.Id));
        Assert.True(charge.InUse.Value);
        Assert.False(charge.IsCompleted);
        Assert.False(charge.CompletedThisTick);

        // The human who plants it finishes it (0x443EA0), and nothing else happens.
        charge.BeginUse();
        charge.CompleteUse();
        Assert.True(charge.IsCompleted);
        Assert.True(charge.CompletedThisTick);
        Assert.True(variables.TryRead("GenericTBA_282.isFinished", out QvmValue finished));
        Assert.Equal(1, finished.Integer);
    }

    [RequiresGameDataFact]
    public void LevelTwoCrateC4IsPickedUpFromTheFloorAndItsTransmissionSounds()
    {
        // Probed in retail (IGI_PROBE_HOOKS=pickup): the C4 lies on a 1.4 m crate in the explosives room. Standing
        // on the floor beside it, the Human's origin at (126045409, -37933259, 175425546), 0x46C630 saw it 1477.8
        // units away and 2389.1 above, took it at once, and the transmission it set off played "message" from
        // 0x486650. Its feet are 6266 units below the item, so a feet test only passed mid-jump.
        (LevelTask root, MagicObjectRegistry magic, LoadedMission mission, ScriptVarRegistry variables) = Load(2);
        new ConditionalContainerGate(variables).Prepare(root);
        WorldScene scene = WorldScene.Build(mission, root, magic);
        GenericPickupRegistry pickups = GenericPickupRegistry.Build(root, variables, scene.Placements);
        MissionObjectiveRuntime objectives = new(root, variables);
        List<string> sounds = [];
        objectives.MessageSound = sounds.Add;
        GenericPickup c4 = Assert.Single(pickups.Items, static item => item.Task.Id == 200);
        Assert.Equal(PickupReach.Radius, c4.ReachRadius);

        PlayerBody body = new() { Position = new Vector3d(126045409.0, -37933259.0, 175421668.8) };
        Assert.Equal(175425546.0, body.Origin.Z, 1.0);
        Assert.Empty(pickups.CollectAt(body.Position));
        Assert.Same(c4, Assert.Single(pickups.CollectAt(body.Origin)));
        Assert.True(variables.TryRead("GenericPickup_200.isPickedUp", out QvmValue picked));
        Assert.Equal(1, picked.Integer);

        for (int tick = 0; tick < 5 && sounds.Count == 0; tick++)
        {
            objectives.Tick();
        }

        Assert.Equal(["message"], sounds);
    }

    [RequiresGameDataFact]
    public void SwitchPressPulseConsumesOneRegistryTickAtATime()
    {
        (LevelTask root, _, LoadedMission mission, ScriptVarRegistry variables) = Load(1);
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
            root, mission, DoorRegistry.Build(root), variables);
        WorldInteraction button = interactions.Items.First(
            static item => item.Type == InteractionActivateType.Switch);

        button.Press();
        interactions.Tick();
        Assert.True(button.Pressed.Value);
        interactions.Tick();
        Assert.True(button.Pressed.Value);
        interactions.Tick();
        Assert.False(button.Pressed.Value);
        Assert.True(button.Pressed.IsFallingEdge);
    }

    [RequiresGameDataFact]
    public void PlayerStationaryGunUseKeepsTheGunOccupiedByTheInteraction()
    {
        for (int missionId = 1; missionId <= 14; missionId++)
        {
            (LevelTask root, _, LoadedMission mission, ScriptVarRegistry variables) = Load(missionId);
            WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
                root, mission, DoorRegistry.Build(root), variables);
            WorldInteraction? gun = interactions.Items.FirstOrDefault(
                static item => item.Type == InteractionActivateType.StationaryGun);
            if (gun is null)
            {
                continue;
            }

            gun.BeginUse();

            Assert.True(gun.StationaryGun!.IsOccupied);
            Assert.True(gun.IsPlayerUsingStationaryGun);

            gun.CancelUse();

            Assert.False(gun.StationaryGun.IsOccupied);
            Assert.False(gun.IsPlayerUsingStationaryGun);
            return;
        }

        Assert.Fail("No shipped StationaryGun task was loaded.");
    }

    [Fact]
    public void AiOccupiedStationaryGunRejectsPlayerUseWithoutSettingInUse()
    {
        AiStationaryGun stationaryGun = new(
            taskId: 17,
            position: default,
            orientation: Matrix3f.Identity);
        WorldInteraction interaction = new(
            new DeclaredTask(),
            InteractionActivateType.StationaryGun,
            anchor: default,
            end: default,
            orientation: Matrix3f.Identity,
            facing: 0f,
            durationTicks: 0,
            actionSprite: "stationary.spr",
            door: null,
            isPickable: false,
            stationaryGun: stationaryGun,
            localBoundsMinimum: default,
            localBoundsMaximum: default,
            hasLocalBounds: false,
            markerPoint: default,
            markerLocal: default);
        object aiOwner = new();
        Assert.True(stationaryGun.TryMount(aiOwner));

        Assert.False(interaction.BeginUse());
        Assert.False(interaction.InUse.Value);
        Assert.False(interaction.IsPlayerUsingStationaryGun);

        stationaryGun.Dismount(aiOwner);
    }

    /// <summary>
    /// Issue #125: mission 7's Radio_3366 is an ExplodeObject (<c>0x43AD80</c> registers it under
    /// <c>word_54097C</c>). Shooting it explodes it on the family's countdown, swaps in 204_02_1, raises
    /// <c>isExploded</c> -- which AnimTask 306's Run expression negates -- and its own update
    /// (<c>0x43AF90</c>) turns <c>isOn</c> off.
    /// </summary>
    [RequiresGameDataFact]
    public void MissionSevenRadioFallsSilentWhenItIsShot()
    {
        (LevelTask root, MagicObjectRegistry magic, LoadedMission mission, ScriptVarRegistry variables) = Load(7);
        WorldScene scene = WorldScene.Build(mission, root, magic);
        DestructibleObjectRegistry destructibles = DestructibleObjectRegistry.Build(root, scene.Placements, variables);
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
            root, mission, DoorRegistry.Build(root), variables);
        LevelExpressionRuntime expressions = new(variables);

        DestructibleObject radio = Assert.Single(destructibles.Objects, item => item.Owner.Id == 3366);
        Assert.Equal("Radio", radio.TypeName);
        Assert.True(radio.ExplodesOnCountdown);
        Assert.Equal("204_02_1", radio.DestroyedModelName);
        Assert.Equal(3366, Assert.Single(interactions.Radios).Task.Id);

        // Off from its constructor (0x43AF20) until its first update reads On expression "1".
        Assert.True(variables.TryRead("Radio_3366.isOn", out QvmValue before));
        Assert.Equal(0, before.ToInt32());
        interactions.TickRadios(null);
        Assert.True(variables.TryRead("Radio_3366.isOn", out QvmValue lit));
        Assert.Equal(1, lit.ToInt32());
        Assert.True(expressions.EvaluateCondition("!Radio_3366.isExploded", emptyValue: false));

        WorldPlacement model = Assert.Single(scene.Placements, placement => ReferenceEquals(placement.Owner, radio.Owner));
        Assert.Null(destructibles.TryDestroy(radio.Owner, model));
        Assert.False(radio.IsDestroyed);
        Assert.Equal(1, ExplodeObjectCountdown.RunUntilExploded(destructibles, radio));
        interactions.TickRadios(null);

        Assert.True(destructibles.IsDestroyed(model));
        Assert.True(variables.TryRead("Radio_3366.isOn", out QvmValue after));
        Assert.Equal(0, after.ToInt32());
        Assert.False(expressions.EvaluateCondition(
            "!Radio_3366.isExploded && AlarmControl_97.isAlarm && EditVariable_4001.nValue",
            emptyValue: false));
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
    public void MissionThreeControlTowerTerminalTypingPoseStaysOnTheFloor()
    {
        (LevelTask root, MagicObjectRegistry magic, LoadedMission mission, ScriptVarRegistry variables) =
            Load(3, loadTerrain: true);
        WorldScene scene = WorldScene.Build(mission, root, magic);
        ObjectCollisionIndex collisions = ObjectCollisionIndex.Build(scene.Placements);
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
            root,
            mission,
            DoorRegistry.Build(root),
            variables);
        WorldInteraction terminal = Assert.Single(
            interactions.Items,
            static item => item.Type == InteractionActivateType.Terminal && item.Task.Id == 333);
        (Vector3d authoredPose, _) = terminal.BodyPose(terminal.Anchor);
        Vector3d clearFloorSample = terminal.Anchor + terminal.Orientation.Transposed().Transform(
            new Vector3d(0.0, 4096.0, 0.0));
        Assert.True(collisions.PlayerCaster()(
            clearFloorSample + new Vector3d(0.0, 0.0, 16384.0),
            clearFloorSample - new Vector3d(0.0, 0.0, 65536.0),
            out WorldRayHit floor));
        PlayerBody body = new()
        {
            // A player close enough to activate this terminal is standing on the same control-room
            // floor sampled just beyond the terminal's collision footprint.
            Position = new Vector3d(authoredPose.X, authoredPose.Y, floor.Position.Z),
            Walls = collisions.PlayerCaster(),
        };

        Assert.True(body.SnapInteractionToGround(Assert.IsType<TerrainData>(mission.Terrain)));
        Assert.Equal(floor.Position.Z, body.Position.Z, precision: 3);
        Assert.True(body.Position.Z < terminal.Anchor.Z);
        Assert.True(body.IsGrounded);
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
                // A round trip through the original's chop-rounded frame closes to an ulp, not exactly.
                Assert.InRange(Vector3d.Distance(door.Anchor, door.CandidatePoint(standing)), 0.0, 0.001);

                Vector3d outside = door.Anchor
                    + door.Orientation.Transposed().Transform(new Vector3d(2048.0, 0.0, 0.0));
                Assert.False(door.CanOfferFrom(outside));
            }
            foreach (PlacedDoor door in doors.Doors)
            {
                WorldPlacement placement = Assert.Single(
                    scene.Placements,
                    candidate => ReferenceEquals(candidate.Owner, door.Task));
                // The placement is the authored start; the door draws where its Position slider slid it from there.
                Assert.Equal(placement.Position, door.ClosedPosition);
                Assert.Equal(placement.Orientation, door.DrawOrientation);

                door.State.Toggle();
                DeclaredTask doorTask = Assert.IsType<DeclaredTask>(door.Task);
                int motionTicks = Math.Max(1, (int)Math.Ceiling(ReadReal(doorTask, "Open time", 1f) * 30f));
                for (int tick = 0; tick <= motionTicks; tick++)
                {
                    door.State.Tick();
                }

                Assert.True(door.State.IsFullyOpen, $"{door.Task.Name} did not finish opening");
                // Position slider is where the slide starts (+312), not a travel: a door authored fully slid is open
                // before it is used, and only a stop or a swing moves it (0x446D10).
                bool slides = ReadReal(doorTask, "Position stop X", 0f) != 0f
                    || ReadReal(doorTask, "Position stop Y", 0f) != 0f;
                bool hasMotion = (slides && ReadReal(doorTask, "Position slider", 0f) != 1f)
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

                // Closed is the slide at zero: its start, wherever its Position slider first put it.
                Assert.Equal(door.ClosedPosition, door.DrawPosition);
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
                // The fence's frame is the original's chop-rounded rotation, a few ulps from orthonormal, so a round
                // trip through it closes to a few hundredths of a unit (micrometres), not exactly.
                Vector3d offeredLocal = fence.Orientation.Transform(fence.CandidatePoint(positiveSide) - fence.Anchor);
                Assert.InRange(Math.Abs(offeredLocal.X - x), 0.0, 0.05);
                Assert.InRange(Math.Abs(offeredLocal.Y - fence.LocalBoundsMaximum.Y), 0.0, 0.05);

                (Vector3d mount, _) = fence.BodyPose(positiveSide);
                Vector3d mountLocal = fence.Orientation.Transform(mount - fence.Anchor);
                Assert.Equal(2457.6, mountLocal.Y, precision: 3);
                Assert.Equal(3891.2, mountLocal.Z, precision: 3);
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

    [RequiresGameDataFact]
    public void EveryRetailStationaryGunHasItsDrawableHolderPlacement()
    {
        int stationaryGunCount = 0;

        for (int missionId = 1; missionId <= 14; missionId++)
        {
            (LevelTask root, MagicObjectRegistry magic, LoadedMission mission, ScriptVarRegistry variables) =
                Load(missionId);
            DoorRegistry doors = DoorRegistry.Build(root);
            WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(root, mission, doors, variables);
            WorldScene scene = WorldScene.Build(mission, root, magic);

            foreach (WorldInteraction gun in interactions.Items.Where(
                         static item => item.Type == InteractionActivateType.StationaryGun))
            {
                stationaryGunCount++;
                DeclaredTask declared = Assert.IsType<DeclaredTask>(gun.Task);
                string parameters = string.Join(
                    "; ",
                    declared.Type.Parameters.Select(
                        (parameter, index) => $"{parameter.Name}={declared.GetValue(index)}"));
                Assert.True(
                    scene.Placements.Any(candidate => ReferenceEquals(candidate.Owner, gun.Task)),
                    $"Mission {missionId} task {declared.Name} produced no placement; "
                    + $"models=[{string.Join(",", WorldScene.PlacedModelNames(declared))}], "
                    + $"parameters=[{parameters}], unresolved={scene.UnresolvedCount}");
                WorldPlacement placement = Assert.Single(
                    scene.Placements,
                    candidate => ReferenceEquals(candidate.Owner, gun.Task));

                string expectedModel = declared.TryGetValue("Holder model", out object? rawHolderModel)
                    && rawHolderModel is string holderModel
                    && holderModel.Length > 0
                    ? holderModel
                    : "456_13_1";
                Assert.Equal(expectedModel, placement.Instance.Name);
                Assert.Equal(
                    new Vector3d(
                        gun.Anchor.X / WorldScene.UnitsPerMetre,
                        gun.Anchor.Y / WorldScene.UnitsPerMetre,
                        gun.Anchor.Z / WorldScene.UnitsPerMetre),
                    placement.Position);
                Assert.Equal(gun.Orientation, placement.Orientation);
                Assert.False(placement.Model.IsEmpty);
            }
        }

        Assert.True(stationaryGunCount > 0, "The campaign did not expose any StationaryGun tasks.");
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

    private static string ReadString(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is string text ? text : string.Empty;
}
