using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Presentation.Igi1;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using System.Collections;
using System.Reflection;
using Xunit;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// Covers the renderer callback that <see cref="PlayerCamera"/> gives a player's retail hit-scan round.
/// It deliberately tests only the renderer seam: scope state and the input-to-fire transition belong to
/// the camera's integration coverage, while this callback owns the body collision work.
/// </summary>
public sealed class SoldierRendererHitscanTests
{
    private const double Metre = WorldScene.UnitsPerMetre;

    private static readonly FieldInfo GroupsField = typeof(SoldierRenderer)
        .GetField("_groups", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private const double PickCullRadiusUnits = SoldierSimulation.PickCullRadiusUnits;
    private const double PickChestUnits = SoldierSimulation.PickChestUnits;

    private const double PresentationOffsetMetres = 8.0;

    [RequiresGameDataFact]
    public void MissionThreeDisembarkRootTrackConsumesOnlyItsAuthoredDisplacement()
    {
        using Scenario scenario = CreateScenario(missionNumber: 3);
        SoldierView jones = Assert.Single(RendererSoldiers(scenario.Renderer),
            view => view.Soldier.Id == 3341);
        Assert.True(jones.PlayStateAnimation(244, loops: false));
        BoneClip clip = jones.Bones.SourceOf(0)!;
        Vector3f start = clip.SampleRoot();
        Vector3d accumulated = default;
        for (int tick = 1; tick <= 270; tick++)
        {
            jones.Tick();
            Vector3f delta = jones.RootMotion();
            accumulated += new Vector3d(delta.X, delta.Y, delta.Z);
            Vector3f sample = clip.SampleRoot();
            Vector3d expected = new(sample.X - start.X, sample.Y - start.Y, sample.Z - start.Z);
            Assert.True((accumulated - expected).Length < 0.1,
                $"tick={tick} sum={accumulated} sampleDelta={expected}");
            if (tick <= 181)
            {
                // Interpolating equal float keys can leave sub-unit roundoff.
                Assert.InRange(accumulated.Length, 0d, 0.001d);
            }
        }
    }

    [RequiresGameDataFact]
    public void MissionThreeDisembarkActorUsesAuthoredStagingDespiteMisspelledContainerLabel()
    {
        using Scenario scenario = CreateScenario(prepareContainers: true, missionNumber: 3);
        HumanSoldierTask jones = Assert.IsType<HumanSoldierTask>(scenario.Root.Tree.FromId(3341));
        Assert.True(CinematicActor.IsStaged(jones),
            "Scene-702 Jones is script-gated cinematic content even though retail labels his ancestor 'Intro cutscnee'.");
        Assert.False(scenario.Cutscenes!.TryGetAttachedDrawTransform(jones, out _, out _),
            "A dormant actor cannot capture the parked vehicle's pose before its collision is registered.");
    }

    [RequiresGameDataFact]
    public void StagedAnyaKeepsHerAuthoredModelOrigin()
    {
        using Scenario scenario = CreateScenario(prepareContainers: true);
        SoldierView anya = Assert.Single(RendererSoldiers(scenario.Renderer),
            view => view.Soldier.Model == "015_01_1");
        object member = MemberFor(scenario.Renderer, anya);
        Assert.True(Get<bool>(member, "IsCutsceneActor"));
        Assert.Equal(6, anya.Soldier.BoneHierarchy);
        Assert.Equal(5, anya.StandAnimation);

        MethodInfo placement = typeof(SoldierRenderer).GetMethod(
            "TickPlacementOf", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var actual = ((Vector3d Where, Matrix3f Orientation))placement.Invoke(
            scenario.Renderer, [member])!;
        Vector3d expected = anya.Soldier.Position / Metre;
        Assert.True((actual.Where - expected).Length < 1e-6,
            $"staged Anya authored={expected}, rendered={actual.Where}, root={anya.RootSample}; "
            + "the model origin must not receive the absolute root height twice");
    }

    [RequiresGameDataFact]
    public void DefaultPlayerHitscanPathRegistersTheMeshItAimsAt()
    {
        // Tactical ballistics is opt-in, so a normal player shot resolves synchronously through this
        // renderer-owned callback rather than spawning an in-flight projectile.
        Assert.False(GameplayCVars.Ballistics.DefaultValue);

        using Scenario scenario = CreateScenario();
        MeshHitRay ray = FindAnyMeshHitRay(scenario.Renderer);

        Assert.True(scenario.Renderer.TryShoot(ray.Origin, ray.End, damageFactor: 0.5f, WeaponCategory.Gun, out HumanHit hit));
        Assert.True(hit.Distance > 0.0);
        Assert.InRange(hit.Zone, 0, HumanHitZoneTable.Soldier.Count - 1);
    }

    [RequiresGameDataFact]
    public void LethalDragunovHitKeepsTheLevelFourCorpseDrawable()
    {
        using Scenario scenario = CreateScenario();
        MeshHitRay ray = FindAnyMeshHitRay(
            scenario.Renderer,
            view => view.Soldier.Team != scenario.Renderer.Simulation.PlayerTeam
                && !CinematicActor.IsStaged(view.Soldier));
        object targetMember = MemberFor(scenario.Renderer, ray.View);
        MeshHandle bodyMesh = Get<MeshHandle>(targetMember, "Mesh");
        KeepOnlyTarget(scenario.Renderer, targetMember);

        int maxHitsToKill = HumanHitZoneTable.HitsToKill(
            damageFactor: 0.5f,
            HumanHitZoneTable.Soldier.Min(zone => zone.Multiplier));
        for (int shot = 0; shot < maxHitsToKill && !ray.View.IsDead; shot++)
        {
            ray = FindAnyMeshHitRay(
                scenario.Renderer,
                view => ReferenceEquals(view, ray.View));
            Assert.True(scenario.Renderer.TryShoot(
                ray.Origin,
                ray.End,
                damageFactor: 0.5f,
                WeaponCategory.Gun,
                out _));
        }

        Assert.True(ray.View.IsDead);
        Assert.Contains(
            scenario.Renderer.Simulation.Occupants(),
            occupant => occupant.IsDead && occupant.Position == ray.View.Position);

        for (int tick = 0; tick < 180; tick++)
        {
            scenario.Renderer.Tick(enemyAlive: false);
        }

        scenario.Device.MeshDraws.Clear();
        scenario.Renderer.BeginFrame(tickFraction: 1f, smooth: false);
        scenario.Renderer.Draw(
            new Camera3D(ray.Origin, Camera3D.LookRotation(0f, 0f)),
            Matrix4f.Identity);

        Assert.Contains(scenario.Device.MeshDraws, draw => draw.Mesh == bodyMesh);
    }

    [RequiresGameDataTheory]
    [InlineData(417)]
    [InlineData(418)]
    [InlineData(419)]
    public void DragunovKillKeepsAnActivatedBehindWarehouseGuardAboveItsFloor(int taskId)
    {
        using RagdollScope ragdoll = new(enabled: false);
        using Scenario scenario = CreateScenario(prepareContainers: true);
        SoldierView target = Assert.Single(
            RendererSoldiers(scenario.Renderer),
            view => view.Soldier.Id == taskId);
        Assert.False(ConditionalContainerGate.IsTaskActive(target.Soldier));

        Assert.True(scenario.Loop.Variables.TryWrite(
            "EditVariable_752.nValue",
            OpenIGI.Scripting.Runtime.QvmValue.FromInt32(1)));
        Assert.True(scenario.Loop.Variables.TryWrite(
            "EditVariable_4000.nValue",
            OpenIGI.Scripting.Runtime.QvmValue.FromInt32(1)));
        Assert.True(scenario.Loop.Variables.TryRead(
            "EditVariable_752.nValue",
            out OpenIGI.Scripting.Runtime.QvmValue activation));
        Assert.Equal(1, activation.ToInt32());
        WriteInt(scenario, "ConditionalContainer_3900.isRun", 0);
        WriteInt(scenario, "ConditionalContainer_3901.isRun", 1);
        WriteInt(scenario, "ConditionalContainer_3902.isRun", 0);
        scenario.Cutscenes!.Tick();
        ConditionalContainerTask behindWarehouse = Assert.IsType<ConditionalContainerTask>(target.Soldier.Parent);
        Assert.True(behindWarehouse.IsRun, $"Container {behindWarehouse.Id} did not run: {behindWarehouse.Condition}");
        Assert.True(
            ConditionalContainerGate.IsTaskActive(target.Soldier),
            DescribeAncestry(target.Soldier));

        object targetMember = MemberFor(scenario.Renderer, target);
        Assert.Equal("WEAPON_ID_AK47", Get<WeaponType>(targetMember, "WeaponType").ScriptId);
        Assert.NotNull(targetMember.GetType().GetProperty("Weapon")!.GetValue(targetMember));

        scenario.Renderer.Tick(enemyAlive: false);
        scenario.Renderer.BeginFrame(tickFraction: 1f, smooth: false);
        Assert.True(Get<bool>(targetMember, "HasHandState"));
        Assert.True(Get<bool>(targetMember, "HasWeaponFrameState"));

        MeshHitRay ray = FindAnyMeshHitRay(
            scenario.Renderer,
            view => ReferenceEquals(view, target));
        MeshHandle bodyMesh = Get<MeshHandle>(targetMember, "Mesh");
        Vector3d spawnMetres = target.Position / WorldScene.UnitsPerMetre;

        int maxHitsToKill = HumanHitZoneTable.HitsToKill(
            damageFactor: 0.5f,
            HumanHitZoneTable.Soldier.Min(zone => zone.Multiplier));
        for (int shot = 0; shot < maxHitsToKill && !target.IsDead; shot++)
        {
            ray = FindAnyMeshHitRay(
                scenario.Renderer,
                view => ReferenceEquals(view, target));
            Assert.True(scenario.Renderer.TryShoot(
                ray.Origin,
                ray.End,
                damageFactor: 0.5f,
                WeaponCategory.Gun,
                out _));

            scenario.Cutscenes.Tick();
            scenario.Renderer.Tick(enemy: ray.Origin, enemyAlive: true);
            scenario.Renderer.BeginFrame(tickFraction: 1f, smooth: false);
            Assert.True(ConditionalContainerGate.IsTaskActive(target.Soldier));
            AssertBodyRemainsDrawableNearSpawn(scenario, targetMember, bodyMesh, spawnMetres, shot + 1);
        }

        Assert.True(target.IsDead);
        for (int tick = 0; tick < 180; tick++)
        {
            scenario.Cutscenes.Tick();
            scenario.Renderer.Tick(enemyAlive: false);
        }

        Assert.True(ConditionalContainerGate.IsTaskActive(target.Soldier));
        scenario.Renderer.BeginFrame(tickFraction: 1f, smooth: false);
        AssertBodyRemainsDrawableNearSpawn(scenario, targetMember, bodyMesh, spawnMetres, maxHitsToKill + 180);
    }

    [RequiresGameDataTheory]
    [InlineData(429)]
    [InlineData(430)]
    [InlineData(431)]
    public void PostRescueGeneratedGuardLeavesItsBodyAndRespawnsArmed(int taskId)
    {
        using RagdollScope ragdoll = new(enabled: false);
        using Scenario scenario = CreateScenario(prepareContainers: true);
        SoldierView firstLife = Assert.Single(
            RendererSoldiers(scenario.Renderer),
            view => view.Soldier.Id == taskId);
        Assert.False(ConditionalContainerGate.IsTaskActive(firstLife.Soldier));

        scenario.Cutscenes!.Tick();
        Assert.True(scenario.Loop.Variables.TryWrite(
            "StatusMessage_977.isSendt",
            OpenIGI.Scripting.Runtime.QvmValue.FromInt32(1)));
        ForceContainer(scenario, 307, run: true);
        ForceContainer(scenario, 3901, run: true);
        scenario.Renderer.Tick(enemyAlive: false);
        Assert.True(
            ConditionalContainerGate.IsTaskActive(firstLife.Soldier),
            DescribeAncestry(firstLife.Soldier));

        object firstMember = MemberFor(scenario.Renderer, firstLife);
        Assert.Equal("WEAPON_ID_AK47", Get<WeaponType>(firstMember, "WeaponType").ScriptId);
        Assert.NotNull(firstMember.GetType().GetProperty("Weapon")!.GetValue(firstMember));

        MeshHitRay ray = FindAnyMeshHitRay(
            scenario.Renderer,
            view => ReferenceEquals(view, firstLife));
        int maxHitsToKill = HumanHitZoneTable.HitsToKill(
            damageFactor: 0.5f,
            HumanHitZoneTable.Soldier.Min(zone => zone.Multiplier));
        for (int shot = 0; shot < maxHitsToKill && !firstLife.IsDead; shot++)
        {
            ray = FindAnyMeshHitRay(
                scenario.Renderer,
                view => ReferenceEquals(view, firstLife));
            Assert.True(scenario.Renderer.TryShoot(
                ray.Origin,
                ray.End,
                damageFactor: 0.5f,
                WeaponCategory.Gun,
                out _));
        }
        Assert.True(firstLife.IsDead);
        MeshHandle corpseMesh = Get<MeshHandle>(firstMember, "Mesh");

        // Audio state and alert replies are keyed by runtime identity, not the authored task ID.
        // A retained corpse must not advance its replacement's sound loop on the same tick.
        List<int> soundEmitters = [];
        scenario.Renderer.Simulation.SetWeaponSound((id, _, _, _) => soundEmitters.Add(id));

        // Retail GuardGenerator clones another live subtree. It does not revive and erase the corpse.
        scenario.Renderer.Tick(enemyAlive: false);
        AssertDistinctRuntimeLifetimes(scenario.Renderer, soundEmitters);
        SoldierView[] lives = RendererSoldiers(scenario.Renderer)
            .Where(view => view.Soldier.Id == taskId)
            .ToArray();
        Assert.Equal(2, lives.Length);
        Assert.True(firstLife.IsDead);
        SoldierView replacement = Assert.Single(lives, view => !view.IsDead);

        object replacementMember = MemberFor(scenario.Renderer, replacement);
        Assert.Equal("WEAPON_ID_AK47", Get<WeaponType>(replacementMember, "WeaponType").ScriptId);
        object? replacementWeaponValue = replacementMember.GetType().GetProperty("Weapon")!.GetValue(replacementMember);
        Assert.NotNull(replacementWeaponValue);
        object replacementWeapon = replacementWeaponValue;
        MeshHandle replacementWeaponMesh = Get<MeshHandle>(replacementWeapon, "Mesh");

        scenario.Renderer.BeginFrame(tickFraction: 1f, smooth: false);
        Assert.True(Get<bool>(replacementMember, "HasHandState"));
        Assert.True(Get<bool>(replacementMember, "HasWeaponFrameState"));
        Assert.Contains(scenario.Renderer.Simulation.MapContacts(), position => position == replacement.Position);
        Camera3D drawCamera = new(ray.Origin, Camera3D.LookRotation(0f, 0f));
        Matrix4f replacementWeaponTransform = drawCamera.CreateModelView(
            Get<Vector3d>(replacementMember, "DrawnWeaponOrigin"),
            Get<Matrix3f>(replacementMember, "DrawnWeaponRotation"));
        scenario.Device.MeshDraws.Clear();
        scenario.Renderer.Draw(
            drawCamera,
            Matrix4f.Identity);
        Assert.Contains(scenario.Device.MeshDraws, draw => draw.Mesh == corpseMesh);
        Assert.Contains(
            scenario.Device.MeshDraws,
            draw => draw.Mesh == replacementWeaponMesh && draw.ModelView == replacementWeaponTransform);

        // Shoot the generated life too. This is the user-visible failure mode: the replacement used
        // to be the original view reset in place, so its first hit made the body appear to evaporate.
        for (int shot = 0; shot < maxHitsToKill && !replacement.IsDead; shot++)
        {
            MeshHitRay replacementRay = FindAnyMeshHitRay(
                scenario.Renderer,
                view => ReferenceEquals(view, replacement));
            Assert.True(scenario.Renderer.TryShoot(
                replacementRay.Origin,
                replacementRay.End,
                damageFactor: 0.5f,
                WeaponCategory.Gun,
                out _));
        }
        Assert.True(replacement.IsDead);
        MeshHandle replacementCorpseMesh = Get<MeshHandle>(replacementMember, "Mesh");

        soundEmitters.Clear();
        scenario.Renderer.Tick(enemyAlive: false);
        AssertDistinctRuntimeLifetimes(scenario.Renderer, soundEmitters);
        SoldierView[] nextGeneration = RendererSoldiers(scenario.Renderer)
            .Where(view => view.Soldier.Id == taskId)
            .ToArray();
        Assert.Equal(3, nextGeneration.Length);
        Assert.Equal(2, nextGeneration.Count(view => view.IsDead));
        SoldierView nextLive = Assert.Single(nextGeneration, view => !view.IsDead);
        object nextMember = MemberFor(scenario.Renderer, nextLive);
        Assert.Equal("WEAPON_ID_AK47", Get<WeaponType>(nextMember, "WeaponType").ScriptId);
        Assert.NotNull(nextMember.GetType().GetProperty("Weapon")!.GetValue(nextMember));

        scenario.Renderer.BeginFrame(tickFraction: 1f, smooth: false);
        scenario.Device.MeshDraws.Clear();
        scenario.Renderer.Draw(
            new Camera3D(ray.Origin, Camera3D.LookRotation(0f, 0f)),
            Matrix4f.Identity);
        Assert.Contains(scenario.Device.MeshDraws, draw => draw.Mesh == corpseMesh);
        Assert.Contains(scenario.Device.MeshDraws, draw => draw.Mesh == replacementCorpseMesh);
    }

    [RequiresGameDataFact]
    public void FullLevelRendererSendsHarrisonModelToJosipBeforeRescueCompletes()
    {
        using Scenario scenario = CreateScenario(prepareContainers: true);
        CutsceneRuntime cutscenes = Assert.IsType<CutsceneRuntime>(scenario.Cutscenes);
        MissionObjectiveRuntime objectives = Assert.IsType<MissionObjectiveRuntime>(scenario.Objectives);
        Vector3d playerOutside = new(112865840.0, -54000000.0, 176238000.0);
        HumanSoldierTask harrisonTask = Assert.IsType<HumanSoldierTask>(scenario.Root.Tree.FromId(400));
        Assert.Equal("022_01_1", harrisonTask.SoldierModel);
        Assert.Equal(500, Assert.Single(harrisonTask.Children.OfType<HumanAiTask>()).Id);

        SoldierView harrison = Assert.Single(RendererSoldiers(scenario.Renderer), view => view.Soldier.Id == 400);
        SoldierView escortOne = Assert.Single(RendererSoldiers(scenario.Renderer), view => view.Soldier.Id == 401);
        SoldierView escortTwo = Assert.Single(RendererSoldiers(scenario.Renderer), view => view.Soldier.Id == 402);
        SoldierView[] squad = RendererSoldiers(scenario.Renderer)
            .Where(view => view.Soldier.Id is >= 400 and <= 405)
            .OrderBy(view => view.Soldier.Id)
            .ToArray();
        Assert.Equal(6, squad.Length);

        // Reproduce the post-gate gameplay state without running a synthetic one-soldier brain: the real
        // level renderer owns all six friendlies, their authored models, graph-1 routes and collision.
        cutscenes.RequestBreak();
        for (int tick = 0; tick < 10 && cutscenes.ActiveScene?.Id == 1000; tick++)
        {
            objectives.TickLevelState(playerOutside, playerTeam: 0);
            cutscenes.Tick();
            objectives.TickAfterLevelState();
            cutscenes.FinishExpressionTick();
        }
        Assert.NotEqual(1000, cutscenes.ActiveScene?.Id);
        ForceContainer(scenario, 3900, run: false);
        ForceContainer(scenario, 3901, run: true);
        ForceContainer(scenario, 3902, run: false);
        ForceContainer(scenario, 300, run: true);
        WriteInt(scenario, "AnimTask_850.nTick", (66 * 30) + 1);
        WriteInt(scenario, "EditVariable_752.nValue", 2);

        Dictionary<SoldierView, AiSoldier> brains = RendererBrains(scenario.Renderer);
        SoldierView distantGuard = Assert.Single(brains.Keys, view => view.Soldier.Id == 420);
        Vector3d distantPosition = new(playerOutside.X, playerOutside.Y, playerOutside.Z);
        brains[distantGuard].SeparateTo(distantPosition);
        distantGuard.PlaceAt(distantPosition, distantGuard.Orientation);
        foreach (SoldierView view in RendererSoldiers(scenario.Renderer))
        {
            if (view != distantGuard && view.Soldier.Team != 0 && ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                view.Kill(-1);
            }
        }

        scenario.Renderer.Tick(enemyAlive: false);
        Assert.Equal(725, brains[harrison].Route?.Path.Id);
        Assert.Equal(726, brains[escortOne].Route?.Path.Id);
        Assert.Equal(727, brains[escortTwo].Route?.Path.Id);

        // A killed local target must be retired even when another guard remains alive elsewhere.
        // Reproduce an interrupted patrol for each of the three members of the rescue team.
        // Each is fighting the dead guard: a dead human is never registered, so the combat record's slot is
        // gone and the fight ends (0x44F410 releases the record, and the arm ends without a slot).
        SoldierView deadTarget = Assert.Single(brains.Keys, view => view.Soldier.Id == 417);
        foreach (SoldierView friendly in new[] { harrison, escortOne, escortTwo })
        {
            typeof(AiSoldier).GetMethod("EnterCombat", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(brains[friendly], null);
            typeof(AiSoldier).GetField("_targetId", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(brains[friendly], deadTarget.Soldier.Id);
        }
        scenario.Renderer.Tick(enemyAlive: false);
        foreach (SoldierView friendly in new[] { harrison, escortOne, escortTwo })
        {
            Assert.NotEqual(AiSoldierActivity.Engaging, brains[friendly].Activity);
        }
        Assert.False(distantGuard.IsDead);

        DeclaredTask area154 = Assert.IsType<DeclaredTask>(scenario.Root.Tree.FromId(154));
        Bounds rescue = BoundsOf(area154);
        int? firstInside = null;
        int firstInsideTick = -1;
        int harrisonInsideTick = -1;
        HashSet<int> rescueArrivals = [];
        for (int tick = 0; tick < 5000 && rescueArrivals.Count < 3; tick++)
        {
            scenario.Renderer.Tick(enemyAlive: false);
            foreach (SoldierView view in squad)
            {
                if (rescue.Contains(view.Position))
                {
                    if (firstInside is null)
                    {
                        firstInside = view.Soldier.Id;
                        firstInsideTick = tick;
                    }
                    if (view.Soldier.Id is 400 or 401 or 402)
                    {
                        rescueArrivals.Add(view.Soldier.Id);
                    }
                    if (view.Soldier.Id == 400 && harrisonInsideTick < 0)
                    {
                        harrisonInsideTick = tick;
                    }
                }
            }
        }

        Assert.True(
            firstInside is not null,
            $"No friendly reached area 154; harrison=({harrison.Position.X:0},{harrison.Position.Y:0},{harrison.Position.Z:0}) "
                + $"routes={brains[harrison].Route?.Path.Id},{brains[escortOne].Route?.Path.Id},{brains[escortTwo].Route?.Path.Id} "
                + DescribeAncestry(harrison.Soldier));
        Assert.True(
            harrisonInsideTick >= firstInsideTick
                && harrisonInsideTick <= firstInsideTick + (14 * 30),
            $"first={firstInside} tick={firstInsideTick} harrisonTick={harrisonInsideTick} "
                + $"harrison=({harrison.Position.X:0},{harrison.Position.Y:0},{harrison.Position.Z:0}) "
                + $"escort401=({escortOne.Position.X:0},{escortOne.Position.Y:0},{escortOne.Position.Z:0}) "
                + $"escort402=({escortTwo.Position.X:0},{escortTwo.Position.Y:0},{escortTwo.Position.Z:0}) "
                + $"nodes={brains[harrison].CurrentNode}->{brains[harrison].DestinationNode},"
                + $"{brains[escortOne].CurrentNode}->{brains[escortOne].DestinationNode},"
                + $"{brains[escortTwo].CurrentNode}->{brains[escortTwo].DestinationNode} "
                + $"waypoints={brains[harrison].DebugWaypoint},{brains[escortOne].DebugWaypoint},{brains[escortTwo].DebugWaypoint} "
                + $"models={harrison.Soldier.SoldierModel},{escortOne.Soldier.SoldierModel},{escortTwo.Soldier.SoldierModel} "
                + $"area={rescue}");
        // Each is walking to its authored node or already on it: arrival clears the goal (0x4518E0 sets
        // ai+15920 to -1 when the queue runs out) and leaves the node as the current one.
        Assert.True(HeadingForOrOn(brains[harrison], 113));
        Assert.Equal(3, rescueArrivals.Count);
        Assert.True(HeadingForOrOn(brains[escortOne], 112));
        Assert.True(HeadingForOrOn(brains[escortTwo], 114));

        // Feed all six live friendlies through the real area/timer/message runtimes while Harrison is
        // physically inside area 154. The authored rescue cannot advance team one's stage before the
        // 14-second LevelTimer_761 finishes and sends StatusMessage_977.
        int rescueSentTick = -1;
        for (int tick = 0; tick < (15 * 30); tick++)
        {
            objectives.SetSoldierOccupants(scenario.Renderer.Simulation.Occupants());
            objectives.TickLevelState(playerOutside, playerTeam: 0);
            cutscenes.Tick();
            cutscenes.FinishExpressionTick();
            objectives.TickAfterLevelState();

            if (ReadInt(scenario, "StatusMessage_977.isSendt") != 0)
            {
                rescueSentTick = tick;
                break;
            }
            Assert.Equal(2, ReadInt(scenario, "EditVariable_752.nValue"));
        }
        Assert.InRange(rescueSentTick, (14 * 30) - 1, (15 * 30) - 1);

        // The next host tick consumes StatusMessage_977, advances team one's stage and registers the
        // three authored GuardGenerators beside Josip's building.
        cutscenes.Tick();
        scenario.Renderer.Tick(enemyAlive: false);
        objectives.SetSoldierOccupants(scenario.Renderer.Simulation.Occupants());
        objectives.TickLevelState(playerOutside, playerTeam: 0);
        cutscenes.FinishExpressionTick();
        objectives.TickAfterLevelState();
        Assert.Equal(3, ReadInt(scenario, "EditVariable_752.nValue"));
        Assert.True(ConditionalContainerGate.IsTaskActive(scenario.Root.Tree.FromId(429)!));
        Assert.True(ConditionalContainerGate.IsTaskActive(scenario.Root.Tree.FromId(430)!));
        Assert.True(ConditionalContainerGate.IsTaskActive(scenario.Root.Tree.FromId(431)!));
    }

    private static void WriteInt(Scenario scenario, string name, int value) =>
        Assert.True(scenario.Loop.Variables.TryWrite(
            name,
            OpenIGI.Scripting.Runtime.QvmValue.FromInt32(value)));

    // Opens or closes a container the way the mission eventually does: its isRun, and the registration the level
    // walk's edge brings with it (0x4874E0).
    private static void ForceContainer(Scenario scenario, int id, bool run)
    {
        WriteInt(scenario, $"ConditionalContainer_{id}.isRun", run ? 1 : 0);
        Assert.IsType<ConditionalContainerTask>(scenario.Root.Tree.FromId(id)).IsActive = run;
    }

    private static int ReadInt(Scenario scenario, string name)
    {
        Assert.True(scenario.Loop.Variables.TryRead(name, out OpenIGI.Scripting.Runtime.QvmValue value));
        return value.ToInt32();
    }

    private static Bounds BoundsOf(DeclaredTask task)
    {
        Assert.True(task.TryGetValue("Position", out object? position));
        Assert.True(task.TryGetValue("Orientation", out object? orientation));
        Assert.True(task.TryGetValue("Dimensions", out object? dimensions));
        Vector3d centre = Assert.IsType<Vector3d>(position);
        Vector3f angles = Assert.IsType<Vector3f>(orientation);
        Matrix3f rotation = Matrix3f.FromEngineEulerAngles(angles.X, angles.Y, angles.Z);
        Vector3f half = Assert.IsType<Vector3f>(dimensions) * 0.5f;
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3f local = new(
                (corner & 1) == 0 ? half.X : -half.X,
                (corner & 2) == 0 ? half.Y : -half.Y,
                (corner & 4) == 0 ? half.Z : -half.Z);
            Vector3f rotated = rotation.Transposed().Transform(local);
            Vector3d point = centre + new Vector3d(rotated.X, rotated.Y, rotated.Z);
            minX = Math.Min(minX, point.X); minY = Math.Min(minY, point.Y); minZ = Math.Min(minZ, point.Z);
            maxX = Math.Max(maxX, point.X); maxY = Math.Max(maxY, point.Y); maxZ = Math.Max(maxZ, point.Z);
        }
        return new Bounds(new Vector3d(minX, minY, minZ), new Vector3d(maxX, maxY, maxZ));
    }

    private static void AssertBodyRemainsDrawableNearSpawn(
        Scenario scenario,
        object targetMember,
        MeshHandle bodyMesh,
        Vector3d spawnMetres,
        int tick)
    {
        Vector3d drawn = Get<Vector3d>(targetMember, "DrawnWhere");
        double dx = spawnMetres.X - drawn.X;
        double dy = spawnMetres.Y - drawn.Y;
        double dz = spawnMetres.Z - drawn.Z;
        double distance = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        Assert.True(distance < 20.0, $"tick {tick}: guard moved {distance:0.000}m from {spawnMetres} to {drawn}");

        (Vertex3D[] vertices, ushort[] indices) = scenario.Device.Meshes[bodyMesh.Id];
        float maximumLocalZ = float.MinValue;
        Assert.All(indices.Distinct(), index =>
        {
            Vertex3D vertex = vertices[index];
            Assert.True(float.IsFinite(vertex.Position.X));
            Assert.True(float.IsFinite(vertex.Position.Y));
            Assert.True(float.IsFinite(vertex.Position.Z));
            Assert.InRange(vertex.Position.X, -20f, 20f);
            Assert.InRange(vertex.Position.Y, -20f, 20f);
            Assert.InRange(vertex.Position.Z, -20f, 20f);
            maximumLocalZ = Math.Max(maximumLocalZ, vertex.Position.Z);
        });
        Assert.True(
            drawn.Z + maximumLocalZ > spawnMetres.Z,
            $"tick {tick}: corpse is entirely below its authored floor; root={drawn.Z:0.000}, localMax={maximumLocalZ:0.000}, floor={spawnMetres.Z:0.000}");

        scenario.Device.MeshDraws.Clear();
        scenario.Renderer.Draw(
            new Camera3D(spawnMetres + new Vector3d(0.0, -10.0, 2.0), Camera3D.LookRotation(0f, 0f)),
            Matrix4f.Identity);
        Assert.Contains(scenario.Device.MeshDraws, draw => draw.Mesh == bodyMesh);
    }

    [RequiresGameDataFact]
    public void MissionLoadPrimesSoldierCollisionMeshesBeforeTheFirstShot()
    {
        // Collision chunks used to be decoded by TryShoot's first candidate. That made the first
        // scoped round pay an avoidable synchronous allocation/parse on the render thread. Loading the
        // mission is the stable cache boundary, so every drawable group must already have attempted its
        // one-time read before a shot can be offered.
        using Scenario scenario = CreateScenario();
        int groups = 0;
        int prepared = 0;
        foreach (object group in Assert.IsAssignableFrom<IEnumerable>(GroupsField.GetValue(scenario.Renderer)))
        {
            groups++;
            bool wasPrepared = Get<bool>(group, "CollisionMeshPrepared");
            prepared += wasPrepared ? 1 : 0;
        }

        Assert.True(groups > 0, "Mission four must load at least one soldier group.");
        Assert.Equal(groups, prepared);

        if (GameplayCVars.Ragdoll.Value)
        {
            int rigs = scenario.Renderer.Simulation.RagdollRigs.Count;
            Assert.True(rigs > 0, "Ragdoll rigs must be prepared before the first lethal shot.");
        }
    }

    [RequiresGameDataFact]
    public void SmoothedPlayerHitscanRegistersTheMeshAtItsDisplayedPosition()
    {
        // A shot is resolved on the input tick, but the target under the crosshair is the renderer's last
        // r_smooth frame. Make those two positions materially different, then aim through the exact mesh
        // the frame presents. This exercises the real player-shot seam rather than a private collision helper.
        using Scenario scenario = CreateScenario();
        MeshBackedSoldier target = FirstMeshBackedSoldier(scenario.Renderer);
        KeepOnlyTarget(scenario.Renderer, target.Member);
        MovePresentationAwayFromSimulation(scenario.Renderer, target.Member, smooth: true);

        Vector3d displayedRoot = Get<Vector3d>(target.Member, "DrawnWhere") * Metre;
        Matrix3f displayedOrientation = Get<Matrix3f>(target.Member, "DrawnOrientation");
        Vector3f displayedFacing = new(
            displayedOrientation.M12,
            displayedOrientation.M22,
            displayedOrientation.M32);
        Vector3d displayedFeet = new(
            displayedRoot.X,
            displayedRoot.Y,
            displayedRoot.Z - target.View.RootSample.Z);
        Matrix3f simulationOrientation = target.View.Orientation;
        MeshHitRay ray;
        try
        {
            // The mesh helper takes SoldierView's faced pose. Recreate the interpolation heading here to
            // independently find the mesh the display shows, then restore the simulation state before
            // invoking the public player-shot seam.
            target.View.PlaceAt(target.View.Position, displayedOrientation);
            ray = FindMeshHitRay(target.View, target.Mesh, displayedFeet, displayedFacing);
        }
        finally
        {
            target.View.PlaceAt(target.View.Position, simulationOrientation);
        }

        Assert.NotEqual(target.View.Position.X, displayedFeet.X);

        Assert.True(scenario.Renderer.TryShoot(ray.Origin, ray.End, damageFactor: 0.5f, WeaponCategory.Gun, out _));
    }

    [RequiresGameDataFact]
    public void SmoothedPlayerHitscanUsesTheRenderedBlendedCollisionVertices()
    {
        // The renderer interpolates the two already-skinned GPU streams. This ray crosses only the
        // halfway triangle: it misses both source poses, so it catches an implementation that merely
        // follows the displayed body transform while still tracing the current limb pose.
        using Scenario scenario = CreateScenario();
        MeshBackedSoldier target = FirstMeshBackedSoldier(scenario.Renderer);
        KeepOnlyTarget(scenario.Renderer, target.Member);
        MovePresentationAwayFromSimulation(scenario.Renderer, target.Member, smooth: true);

        Vector3d root = Get<Vector3d>(target.Member, "DrawnWhere") * Metre;
        Matrix3f orientation = Get<Matrix3f>(target.Member, "DrawnOrientation");
        BlendedOnlyRay ray = FindBlendedOnlyRay(target.View.Bones.Skeleton, target.Mesh, root, orientation);

        // These are the exact two model-space pose snapshots the player collision path receives from
        // the animated mesh. The test fixture injects a deliberately separated pair so its visible
        // midpoint cannot accidentally be hit through either endpoint pose.
        Set(target.Member, "SkinnedPose", ray.Current);
        Set(target.Member, "PreviousSkinnedPose", ray.Previous);
        Set(target.Member, "PoseBlend", 0.5f);

        Assert.True(scenario.Renderer.TryShoot(ray.Origin, ray.End, damageFactor: 0.5f, WeaponCategory.Gun, out _));
    }

    [RequiresGameDataFact]
    public void UnsmoothedPlayerHitscanUsesTheCurrentSimulationPosition()
    {
        // r_smooth is optional. Even when the old tick pair differs, the off branch must retain the
        // original current-tick collision point rather than reading stale presentation state.
        using Scenario scenario = CreateScenario();
        MeshBackedSoldier target = FirstMeshBackedSoldier(scenario.Renderer);
        KeepOnlyTarget(scenario.Renderer, target.Member);
        MovePresentationAwayFromSimulation(scenario.Renderer, target.Member, smooth: false);

        Vector3d currentFeet = FeetOnGround(scenario.Renderer, target.View);
        Matrix3f orientation = target.View.Orientation;
        MeshHitRay ray = FindMeshHitRay(
            target.View,
            target.Mesh,
            currentFeet,
            new Vector3f(orientation.M12, orientation.M22, orientation.M32));

        Assert.True(scenario.Renderer.TryShoot(ray.Origin, ray.End, damageFactor: 0.5f, WeaponCategory.Gun, out _));
    }

    [Fact]
    public void MeshHitBroadPhaseRejectsABodyBesideTheFiniteShotSegment()
    {
        // This is the guard before HumanHitTest.TryCastMesh skins a collision mesh. A body may be ahead
        // along the ray but still be too far to the side for this round to touch it.
        double radius = PickCullRadiusUnits;
        (bool eligible, _) = HitSegmentMayReachSphere(
            origin: new Vector3d(0.0, 0.0, 0.0),
            direction: new Vector3d(1.0, 0.0, 0.0),
            centre: new Vector3d(radius * 4.0, radius * 2.0, 0.0),
            radius,
            reach: radius * 8.0);

        Assert.False(eligible);
    }

    [Fact]
    public void MeshHitBroadPhaseRejectsABodyBeyondTheFiniteShotSegment()
    {
        // The broad phase accepts a *segment*, not an infinite crosshair ray. This avoids skinning every
        // guard that happens to stand further downrange than the round's actual end point.
        double radius = PickCullRadiusUnits;
        (bool eligible, _) = HitSegmentMayReachSphere(
            origin: new Vector3d(0.0, 0.0, 0.0),
            direction: new Vector3d(1.0, 0.0, 0.0),
            centre: new Vector3d(radius * 3.0, 0.0, 0.0),
            radius,
            reach: radius);

        Assert.False(eligible);
    }

    [RequiresGameDataFact]
    public void PlayerRoundStartingInsideTheBodySphereStillReachesItsMesh()
    {
        using Scenario scenario = CreateScenario();
        MeshHitRay longRay = FindAnyMeshHitRay(scenario.Renderer);
        Vector3d direction = Unit(longRay.End - longRay.Origin);
        Vector3d origin = Offset(longRay.MeshHit.Position, direction, -0.5 * Metre);
        Vector3d end = Offset(longRay.MeshHit.Position, direction, 0.5 * Metre);

        Assert.True(IsInsideBodySphere(origin, longRay.BodySphereCentre));
        Assert.True(HumanHitTest.TryCastMesh(
            origin,
            end,
            longRay.Feet,
            longRay.Facing,
            longRay.Mesh,
            longRay.View.Pose(),
            longRay.View.RootSample.Z,
            out _));

        Assert.True(scenario.Renderer.TryShoot(origin, end, damageFactor: 0.5f, WeaponCategory.Gun, out _));
    }

    /// <summary>
    /// A headshot is decided by where the round STRUCK: 0x489C60 promotes a front hit to zone 15 only
    /// when the hit record's point is within 15 cm of bone 4. Measuring the ray's closest approach
    /// instead made a round that struck a forearm raised in front of the face (every aiming guard) a
    /// headshot, because its line ran on through the head -- guards died to the second arm hit.
    /// </summary>
    [RequiresGameDataFact]
    public void AHeadshotIsScoredWhereTheRoundStruckNotWhereItsLineRan()
    {
        using Scenario scenario = CreateScenario();
        int heads = 0;
        foreach ((SoldierView soldier, MefCollisionMesh mesh) in MeshBackedSoldiers(scenario.Renderer))
        {
            Vector3d feet = FeetOnGround(scenario.Renderer, soldier);
            Matrix3f orientation = soldier.Orientation;
            Vector3f facing = new(orientation.M12, orientation.M22, orientation.M32);
            BonePose pose = soldier.Pose();
            Vector3f bone = pose.Positions[HumanDamage.ProximityBoneIndex];
            Vector3d head = new(feet.X + bone.X, feet.Y + bone.Y, feet.Z + soldier.RootSample.Z + bone.Z);

            for (double height = 1.1; height <= 1.9; height += 0.05)
            {
                for (double across = -0.4; across <= 0.4; across += 0.05)
                {
                    Vector3d aim = new(
                        feet.X + (facing.Y * across * Metre),
                        feet.Y - (facing.X * across * Metre),
                        feet.Z + (height * Metre));
                    Vector3d origin = new(
                        aim.X + (facing.X * 12.0 * Metre), aim.Y + (facing.Y * 12.0 * Metre), aim.Z);
                    Vector3d end = new(
                        aim.X - (facing.X * 2.0 * Metre), aim.Y - (facing.Y * 2.0 * Metre), aim.Z);
                    if (!HumanHitTest.TryCastMesh(
                            origin, end, feet, facing, mesh, pose, soldier.RootSample.Z, out HumanHit hit)
                        || hit.Zone != HumanDamage.ProximityZone)
                    {
                        continue;
                    }

                    heads++;
                    Assert.True(
                        (hit.Position - head).Length < HumanDamage.HeadProximityUnits,
                        $"{soldier.Soldier.Name}: a head hit struck bone {hit.Bone} "
                            + $"{(hit.Position - head).Length / Metre:0.00} m from the head");
                }
            }
        }

        Assert.True(heads > 0, "no ray reached any soldier's head, so the rule was never exercised");
    }

    [RequiresGameDataFact]
    public void HitscanRayPointingAwayFromEverySoldierDoesNotRegister()
    {
        using Scenario scenario = CreateScenario();
        SoldierView[] soldiers = RendererSoldiers(scenario.Renderer).ToArray();
        double westOfEverySoldier = soldiers.Min(soldier => soldier.Position.X) - (100 * Metre);
        Vector3d origin = new(westOfEverySoldier, 0.0, 0.0);
        Vector3d end = new(westOfEverySoldier - (100 * Metre), 0.0, 0.0);

        Assert.False(scenario.Renderer.TryShoot(origin, end, damageFactor: 0.5f, WeaponCategory.Gun, out _));
    }

    private static MeshHitRay FindAnyMeshHitRay(
        SoldierRenderer renderer,
        Func<SoldierView, bool>? accepts = null)
    {
        // Select against the skinned mesh itself, not TryPick: the test must not use the renderer's other
        // consumer of the broad-phase gate to decide which round counts as a hit.
        foreach ((SoldierView soldier, MefCollisionMesh mesh) in MeshBackedSoldiers(renderer))
        {
            if (accepts is not null && !accepts(soldier))
            {
                continue;
            }

            Vector3d feet = FeetOnGround(renderer, soldier);
            Matrix3f orientation = soldier.Orientation;
            Vector3f facing = new(orientation.M12, orientation.M22, orientation.M32);
            Vector3d bodySphereCentre = new(feet.X, feet.Y, feet.Z + PickChestUnits);

            foreach (double height in new[] { 0.5, 0.9, 1.3, 1.7 })
            {
                Vector3d aim = new(feet.X, feet.Y, feet.Z + (height * Metre));
                foreach ((double X, double Y) offset in new[]
                {
                    (-12.0, 0.0),
                    (12.0, 0.0),
                    (0.0, -12.0),
                    (0.0, 12.0),
                })
                {
                    Vector3d origin = new(aim.X + (offset.X * Metre), aim.Y + (offset.Y * Metre), aim.Z);
                    if (HumanHitTest.TryCastMesh(
                        origin,
                        aim,
                        feet,
                        facing,
                        mesh,
                        soldier.Pose(),
                        soldier.RootSample.Z,
                        out HumanHit hit))
                    {
                        return new MeshHitRay(origin, aim, hit, soldier, mesh, feet, facing, bodySphereCentre);
                    }
                }
            }
        }

        throw new Xunit.Sdk.XunitException("Mission four did not provide a ray to any loaded soldier collision mesh.");
    }

    private static MeshHitRay FindMeshHitRay(
        SoldierView soldier,
        MefCollisionMesh mesh,
        Vector3d feet,
        Vector3f facing)
    {
        Vector3d bodySphereCentre = new(feet.X, feet.Y, feet.Z + PickChestUnits);
        foreach (double height in new[] { 0.5, 0.9, 1.3, 1.7 })
        {
            Vector3d aim = new(feet.X, feet.Y, feet.Z + (height * Metre));
            foreach ((double X, double Y) offset in new[]
            {
                (-12.0, 0.0),
                (12.0, 0.0),
                (0.0, -12.0),
                (0.0, 12.0),
            })
            {
                Vector3d origin = new(aim.X + (offset.X * Metre), aim.Y + (offset.Y * Metre), aim.Z);
                if (HumanHitTest.TryCastMesh(
                    origin,
                    aim,
                    feet,
                    facing,
                    mesh,
                    soldier.Pose(),
                    soldier.RootSample.Z,
                    out HumanHit hit))
                {
                    return new MeshHitRay(origin, aim, hit, soldier, mesh, feet, facing, bodySphereCentre);
                }
            }
        }

        throw new Xunit.Sdk.XunitException($"Soldier '{soldier.Soldier.Name}' did not provide a ray to its collision mesh.");
    }

    private static BlendedOnlyRay FindBlendedOnlyRay(
        BoneSkeleton skeleton,
        MefCollisionMesh mesh,
        Vector3d root,
        Matrix3f orientation)
    {
        Quaternion32[] identity = Enumerable.Repeat(Quaternion32.Identity, skeleton.BoneCount).ToArray();
        BonePose baseline = BonePose.Evaluate(skeleton, identity);

        foreach (MefCollisionFace face in mesh.Faces)
        {
            Vector3d a = SkinVertex(mesh, face.I0, baseline);
            Vector3d b = SkinVertex(mesh, face.I1, baseline);
            Vector3d c = SkinVertex(mesh, face.I2, baseline);
            Vector3d normal = Vector3d.Cross(b - a, c - a);
            if (normal.LengthSquared <= 1.0)
            {
                continue;
            }

            normal = Unit(normal);
            Vector3d tangent = Vector3d.Cross(normal, Vector3d.UnitZ);
            if (tangent.LengthSquared <= 1e-12)
            {
                tangent = Vector3d.Cross(normal, Vector3d.UnitY);
            }
            tangent = Unit(tangent) * (1.5 * Metre);

            BonePose current = BonePose.Evaluate(
                skeleton,
                identity,
                new Vector3f((float)tangent.X, (float)tangent.Y, (float)tangent.Z));
            BonePose previous = BonePose.Evaluate(
                skeleton,
                identity,
                new Vector3f((float)-tangent.X, (float)-tangent.Y, (float)-tangent.Z));
            Vector3d centre = (a + b + c) / 3.0;
            Vector3d localOrigin = centre + (normal * (6.0 * Metre));
            Vector3d localEnd = centre - (normal * (6.0 * Metre));

            if (HumanHitTest.TryCastMesh(localOrigin, localEnd, Vector3d.Zero, Vector3f.UnitY, mesh, current, 0.0, out _)
                || HumanHitTest.TryCastMesh(localOrigin, localEnd, Vector3d.Zero, Vector3f.UnitY, mesh, previous, 0.0, out _)
                || !HumanHitTest.TryCastMeshBlended(
                    localOrigin,
                    localEnd,
                    Vector3d.Zero,
                    Vector3f.UnitY,
                    mesh,
                    current,
                    previous,
                    Matrix3f.Identity,
                    previousShare: 0.5f,
                    out _))
            {
                continue;
            }

            return new BlendedOnlyRay(
                ToWorld(localOrigin, root, orientation),
                ToWorld(localEnd, root, orientation),
                current,
                previous);
        }

        throw new Xunit.Sdk.XunitException("Retail human mesh did not provide a triangle unique to its blended pose.");
    }

    private static Vector3d SkinVertex(MefCollisionMesh mesh, int vertex, BonePose pose)
    {
        int bone = mesh.VertexBones[vertex];
        if ((uint)bone >= (uint)pose.BoneCount)
        {
            bone = 0;
        }

        Vector3f point = mesh.Vertices[vertex];
        Matrix3f rotation = pose.Rotations[bone];
        Vector3f translation = pose.Positions[bone];
        return new Vector3d(
            (rotation.M11 * point.X) + (rotation.M12 * point.Y) + (rotation.M13 * point.Z) + translation.X,
            (rotation.M21 * point.X) + (rotation.M22 * point.Y) + (rotation.M23 * point.Z) + translation.Y,
            (rotation.M31 * point.X) + (rotation.M32 * point.Y) + (rotation.M33 * point.Z) + translation.Z);
    }

    private static Vector3d ToWorld(Vector3d local, Vector3d root, Matrix3f orientation)
    {
        Vector3f turned = orientation.Transposed().Transform(new Vector3f(
            (float)local.X,
            (float)local.Y,
            (float)local.Z));
        return new Vector3d(root.X + turned.X, root.Y + turned.Y, root.Z + turned.Z);
    }

    private static MeshBackedSoldier FirstMeshBackedSoldier(SoldierRenderer renderer)
    {
        foreach (object group in Assert.IsAssignableFrom<IEnumerable>(GroupsField.GetValue(renderer)))
        {
            (object owner, PropertyInfo collision) = Find(group, "CollisionMesh");
            object? candidate = collision.GetValue(owner);
            if (candidate is not MefCollisionMesh mesh)
            {
                continue;
            }

            foreach (object member in Assert.IsAssignableFrom<IEnumerable>(
                group.GetType().GetProperty("Soldiers", BindingFlags.Instance | BindingFlags.Public)!.GetValue(group)))
            {
                SoldierView view = Assert.IsType<SoldierView>(
                    Get<SoldierView>(member, "View"));
                return new MeshBackedSoldier(view, mesh, member);
            }
        }

        throw new Xunit.Sdk.XunitException("Mission four did not provide a loaded soldier collision mesh.");
    }

    private static void KeepOnlyTarget(SoldierRenderer renderer, object target)
    {
        foreach (object group in Assert.IsAssignableFrom<IEnumerable>(GroupsField.GetValue(renderer)))
        {
            IList members = Assert.IsAssignableFrom<IList>(
                group.GetType().GetProperty("Soldiers", BindingFlags.Instance | BindingFlags.Public)!.GetValue(group));
            for (int i = members.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(members[i], target))
                {
                    members.RemoveAt(i);
                }
            }
        }
    }

    private static object MemberFor(SoldierRenderer renderer, SoldierView target)
    {
        foreach (object group in Assert.IsAssignableFrom<IEnumerable>(GroupsField.GetValue(renderer)))
        {
            foreach (object member in Assert.IsAssignableFrom<IEnumerable>(
                group.GetType().GetProperty("Soldiers", BindingFlags.Instance | BindingFlags.Public)!.GetValue(group)))
            {
                if (ReferenceEquals(Get<SoldierView>(member, "View"), target))
                {
                    return member;
                }
            }
        }

        throw new Xunit.Sdk.XunitException($"No renderer member owns soldier '{target.Soldier.Name}'.");
    }

    private static void MovePresentationAwayFromSimulation(SoldierRenderer renderer, object member, bool smooth)
    {
        Vector3d simulatedRoot = Get<Vector3d>(member, "TickWhere");
        Set(member, "PrevTickWhere", new Vector3d(
            simulatedRoot.X + PresentationOffsetMetres,
            simulatedRoot.Y,
            simulatedRoot.Z));
        if (smooth)
        {
            // Interpolate a turn too: the collision pose must follow the same DrawnOrientation as the
            // displayed mesh, not merely the displayed translation.
            Set(member, "PrevTickFacing", Get<float>(member, "TickFacing") + (MathF.PI * 0.5f));
        }
        Set(member, "HasTickState", true);
        renderer.BeginFrame(tickFraction: 0.5f, smooth);
    }

    private static T Get<T>(object instance, string property)
    {
        (object owner, PropertyInfo info) = Find(instance, property);
        return Assert.IsType<T>(info.GetValue(owner));
    }

    private static void Set<T>(object instance, string property, T value)
    {
        (object owner, PropertyInfo info) = Find(instance, property);
        info.SetValue(owner, value);
    }

    // A renderer's soldier holds its drawing state itself and its simulation state on its Agent; a group holds its
    // model's CPU data on its Body.
    private static (object Owner, PropertyInfo Property) Find(object instance, string property)
    {
        const BindingFlags Public = BindingFlags.Instance | BindingFlags.Public;
        if (instance.GetType().GetProperty(property, Public) is { } own)
        {
            return (instance, own);
        }

        foreach (string part in (string[])["Agent", "Body"])
        {
            if (instance.GetType().GetProperty(part, Public)?.GetValue(instance) is { } inner
                && inner.GetType().GetProperty(property, Public) is { } found)
            {
                return (inner, found);
            }
        }

        throw new InvalidOperationException($"{instance.GetType().Name} has no property {property}.");
    }

    private static IEnumerable<(SoldierView View, MefCollisionMesh Mesh)> MeshBackedSoldiers(SoldierRenderer renderer)
    {
        foreach (object group in Assert.IsAssignableFrom<IEnumerable>(GroupsField.GetValue(renderer)))
        {
            (object owner, PropertyInfo collision) = Find(group, "CollisionMesh");
            object? candidate = collision.GetValue(owner);
            if (candidate is not MefCollisionMesh mesh)
            {
                continue;
            }

            IEnumerable members = Assert.IsAssignableFrom<IEnumerable>(
                group.GetType().GetProperty("Soldiers", BindingFlags.Instance | BindingFlags.Public)!.GetValue(group));
            foreach (object member in members)
            {
                SoldierView view = Assert.IsType<SoldierView>(
                    Get<SoldierView>(member, "View"));
                yield return (view, mesh);
            }
        }
    }

    private static Vector3d FeetOnGround(SoldierRenderer renderer, SoldierView soldier) => new(
        soldier.Position.X,
        soldier.Position.Y,
        renderer.Simulation.SnapToGround(soldier.Position.X, soldier.Position.Y, soldier.Position.Z));

    private static (bool Eligible, double EntryDistance) HitSegmentMayReachSphere(
        Vector3d origin,
        Vector3d direction,
        Vector3d centre,
        double radius,
        double reach)
    {
        bool eligible = SoldierSimulation.RaySphere(origin, direction, centre, radius, reach, out double entry);
        return (eligible, entry);
    }

    private static bool IsInsideBodySphere(Vector3d point, Vector3d centre)
    {
        double dx = point.X - centre.X;
        double dy = point.Y - centre.Y;
        double dz = point.Z - centre.Z;
        return (dx * dx) + (dy * dy) + (dz * dz) <= PickCullRadiusUnits * PickCullRadiusUnits;
    }

    private static Vector3d Unit(Vector3d vector)
    {
        double length = Math.Sqrt((vector.X * vector.X) + (vector.Y * vector.Y) + (vector.Z * vector.Z));
        Assert.True(length > 0.0);
        return new Vector3d(vector.X / length, vector.Y / length, vector.Z / length);
    }

    private static Vector3d Offset(Vector3d point, Vector3d direction, double distance) => new(
        point.X + (direction.X * distance),
        point.Y + (direction.Y * distance),
        point.Z + (direction.Z * distance));

    private static void AssertDistinctRuntimeLifetimes(SoldierRenderer renderer, List<int> soundEmitters)
    {
        Assert.NotEmpty(soundEmitters);
        Assert.Equal(soundEmitters.Count, soundEmitters.Distinct().Count());
        Dictionary<SoldierView, AiSoldier> brains = RendererBrains(renderer);
        Assert.Equal(brains.Count, brains.Values.Select(brain => brain.RuntimeId).Distinct().Count());
        Dictionary<int, AiSoldier> recipients = renderer.Simulation.BrainsByRuntimeId;
        foreach (AiSoldier brain in brains.Values)
        {
            Assert.Same(brain, recipients[brain.RuntimeId]);
        }
    }

    private static Dictionary<SoldierView, AiSoldier>.KeyCollection RendererSoldiers(SoldierRenderer renderer) =>
        RendererBrains(renderer).Keys;

    private static Dictionary<SoldierView, AiSoldier> RendererBrains(SoldierRenderer renderer) => renderer.Simulation.Brains;

    private static string DescribeAncestry(GameTask task)
    {
        List<string> ancestry = [];
        for (GameTask? current = task; current is not null; current = current.Parent)
        {
            string state = current is ConditionalContainerTask container
                ? $" run={container.IsRun} condition='{container.Condition}'"
                : string.Empty;
            ancestry.Add($"{current.Type.Name}_{current.Id}{state}");
        }

        return string.Join(" <- ", ancestry);
    }

    private readonly record struct MeshHitRay(
        Vector3d Origin,
        Vector3d End,
        HumanHit MeshHit,
        SoldierView View,
        MefCollisionMesh Mesh,
        Vector3d Feet,
        Vector3f Facing,
        Vector3d BodySphereCentre);

    private readonly record struct MeshBackedSoldier(SoldierView View, MefCollisionMesh Mesh, object Member);

    private readonly record struct BlendedOnlyRay(
        Vector3d Origin,
        Vector3d End,
        BonePose Current,
        BonePose Previous);

    private readonly record struct Bounds(Vector3d Minimum, Vector3d Maximum)
    {
        public bool Contains(Vector3d point) =>
            point.X >= Minimum.X && point.X <= Maximum.X
            && point.Y >= Minimum.Y && point.Y <= Maximum.Y
            && point.Z >= Minimum.Z && point.Z <= Maximum.Z;
    }

    private static Scenario CreateScenario(bool prepareContainers = false, int missionNumber = 4)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        AiTaskTypes.RegisterAll(loop.Types);
        loop.Types.Register<CutSceneTask>(CutSceneTask.TypeName);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);

        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        LoadedMission mission = new MissionLoader(
            missions,
            fileSystem,
            resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(missionNumber);

        CutsceneRuntime? cutscenes = null;
        MissionObjectiveRuntime? objectives = null;
        if (prepareContainers)
        {
            new ConditionalContainerGate(loop.Variables).Prepare(root);
            cutscenes = new CutsceneRuntime(root, loop.Variables);
            objectives = new MissionObjectiveRuntime(root, loop.Variables, native: cutscenes.TryInvokeNative);
        }

        RecordingGraphicsDevice device = new();
        SoldierRenderer renderer = new(device, resources);
        if (!prepareContainers)
        {
            Assert.True(renderer.Load(mission, root) > 0);
            Assert.True(renderer.Simulation.Animate(new Dictionary<int, GraphFile>(), levelRoot: root) > 0);
            return new Scenario(renderer, device, loop, root, cutscenes, objectives);
        }

        WorldScene scene = WorldScene.Build(mission, root);
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(scene.Placements);
        Assert.True(renderer.Load(
            mission,
            root,
            mission.Terrain is { IsUsable: true } terrain ? terrain : null,
            solids.Caster()) > 0);
        Dictionary<int, GraphFile> graphs = NavGraphs.Read(
            Path.Combine(GameData.RequireRoot(), "missions", "location0", $"level{missionNumber}", "graphs"),
            root,
            NullLogger.Instance);
        Assert.True(renderer.Simulation.Animate(
            graphs,
            solids.Tracer(),
            resources,
            loop.Natives,
            root,
            variables: loop.Variables) > 0);
        WeaponTypeRegistry weapons = new();
        weapons.Register(loop.Natives);
        Assert.True(weapons.Load(fileSystem, resources, loop.Natives) > 0);
        Assert.True(renderer.Arm(mission, weapons) > 0);
        return new Scenario(renderer, device, loop, root, cutscenes, objectives);
    }

    private sealed class Scenario(
        SoldierRenderer renderer,
        RecordingGraphicsDevice device,
        GameLoop loop,
        LevelTask root,
        CutsceneRuntime? cutscenes,
        MissionObjectiveRuntime? objectives) : IDisposable
    {
        public SoldierRenderer Renderer { get; } = renderer;

        public RecordingGraphicsDevice Device { get; } = device;

        public GameLoop Loop { get; } = loop;

        public LevelTask Root { get; } = root;

        public CutsceneRuntime? Cutscenes { get; } = cutscenes;

        public MissionObjectiveRuntime? Objectives { get; } = objectives;

        public void Dispose() => Renderer.Dispose();
    }

    private sealed class RagdollScope : IDisposable
    {
        private readonly bool _previous = GameplayCVars.Ragdoll.Value;

        public RagdollScope(bool enabled) => GameplayCVars.Ragdoll.Value = enabled;

        public void Dispose() => GameplayCVars.Ragdoll.Value = _previous;
    }

    private static bool HeadingForOrOn(AiSoldier brain, int node) =>
        brain.DestinationNode == node || (brain.DestinationNode == -1 && brain.CurrentNode == node);
}
