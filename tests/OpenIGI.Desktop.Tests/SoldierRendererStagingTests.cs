using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Igi1;
using OpenIGI.TestSupport;
using Xunit;
using static OpenIGI.Desktop.Tests.MeshHitRays;
using static OpenIGI.Desktop.Tests.SoldierRendererProbe;
using static OpenIGI.Desktop.Tests.SoldierRendererScenario;
using Scenario = OpenIGI.Desktop.Tests.SoldierRendererScenario;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// Covers the soldier renderer keeping a level's authored staging: a disembark's root track, a staged
/// actor's model origin, and the scripted rescue squad's routes and models.
/// </summary>
[Collection(SoldierRendererScenario.CollectionName)]
public sealed class SoldierRendererStagingTests
{
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
        SoldierMember member = MemberFor(scenario.Renderer, anya);
        Assert.True(member.Agent.IsCutsceneActor);
        Assert.Equal(6, anya.Soldier.BoneHierarchy);
        Assert.Equal(5, anya.StandAnimation);

        (Vector3d Where, Matrix3f Orientation) actual = scenario.Renderer.TickPlacementOfForTesting(member);
        Vector3d expected = anya.Soldier.Position / Metre;
        Assert.True((actual.Where - expected).Length < 1e-6,
            $"staged Anya authored={expected}, rendered={actual.Where}, root={anya.RootSample}; "
            + "the model origin must not receive the absolute root height twice");
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

        Dictionary<SoldierView, Igi1HumanAi> brains = RendererBrains(scenario.Renderer);
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

        // The squad's first tick is its creation (0x44F410's first pass); IDLE, and with it the patrol, is the next.
        scenario.Renderer.Tick(enemyAlive: false);
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
            brains[friendly].EnterCombatForTesting(deadTarget.Soldier.Id);
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

    private readonly record struct Bounds(Vector3d Minimum, Vector3d Maximum)
    {
        public bool Contains(Vector3d point) =>
            point.X >= Minimum.X && point.X <= Maximum.X
            && point.Y >= Minimum.Y && point.Y <= Maximum.Y
            && point.Z >= Minimum.Z && point.Z <= Maximum.Z;
    }

    private static bool HeadingForOrOn(Igi1HumanAi brain, int node) =>
        brain.DestinationNode == node || (brain.DestinationNode == -1 && brain.CurrentNode == node);
}
