using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Presentation.Igi1;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Desktop.Tests;

/// <summary>Exercises the live animated-eye binding against retail soldiers from every campaign level.</summary>
public sealed class AiPerceptionRetailIntegrationTests
{
    private const double Metre = 4096.0;

    [RequiresGameDataFact]
    public void ARepresentativeLiveBrainInEveryLevelDetectsAStandingTargetAlongItsForwardAxis()
    {
        List<string> failures = [];
        for (int level = 1; level <= 14; level++)
        {
            using Fixture fixture = Fixture.Load(level);
            Igi1HumanAi? soldier = fixture.Renderer.Simulation.BrainsForTesting.FirstOrDefault(brain => brain.PlayerIsEnemy);
            if (soldier is null)
            {
                failures.Add($"Level {level} has no hostile live brain");
                continue;
            }

            // The eye follows the body only while the soldier is drawn close up (see the next test).
            fixture.Renderer.Simulation.ObserveAllForTesting();

            // AiEyeSnapshot converts world deltas into local eye space with delta * orientation.
            // Therefore a local +Y target is transformed back to world space with the transpose.
            Vector3d forward = soldier.Orientation.Transposed().Transform(new Vector3d(0.0, 5.0 * Metre, 0.0));
            Vector3d mark = new(
                soldier.Position.X + forward.X,
                soldier.Position.Y + forward.Y,
                soldier.Position.Z + Igi1HumanAi.DetectionMarkAboveFeet(crouching: false));
            (double distance, bool inCone, _, _, _) = soldier.ProbeDetection(mark);
            if (!inCone)
            {
                failures.Add(
                    $"Level {level}'s live bone-5 eye rejected a body mark {distance / Metre:0.00} m along body +Y");
                continue;
            }

            bool detected = false;
            for (int tick = 1; tick <= 60; tick++)
            {
                soldier.Tick(tick, mark, rootMotionThisTick: 0.0);
                detected |= soldier.SeesEnemy;
            }

            if (!detected)
            {
                failures.Add(
                    $"Level {level}'s live brain kept an in-cone target out of the detection schedule");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// A guard the player has never drawn looks along its eye bone's rest orientation, whatever its body
    /// faces -- measured on the retail game, where seventeen of mission 1's twenty-one guards shared that
    /// exact matrix until the view turned onto them.
    /// </summary>
    [RequiresGameDataFact]
    public void AnUndrawnGuardLooksAlongTheRestPoseNotItsBody()
    {
        using Fixture fixture = Fixture.Load(1);
        Igi1HumanAi soldier = fixture.Renderer.Simulation.BrainsForTesting.First(brain =>
            brain.PlayerIsEnemy && Math.Abs(Math.Sin(brain.Facing)) > 0.9);

        // Five metres along the body's own forward: to the side of the rest-pose eye, so out of its cone.
        Vector3d forward = soldier.Orientation.Transposed().Transform(new Vector3d(0.0, 5.0 * Metre, 0.0));
        Vector3d bodyAhead = new(
            soldier.Position.X + forward.X,
            soldier.Position.Y + forward.Y,
            soldier.Position.Z + Igi1HumanAi.DetectionMarkAboveFeet(crouching: false));
        Assert.False(soldier.ProbeDetection(bodyAhead).InCone, "an undrawn guard's eye followed its body");

        // Five metres along world +Y: where the rest-pose eye looks.
        Vector3d northAhead = new(
            soldier.Position.X,
            soldier.Position.Y + (5.0 * Metre),
            soldier.Position.Z + Igi1HumanAi.DetectionMarkAboveFeet(crouching: false));
        Assert.True(soldier.ProbeDetection(northAhead).InCone, "an undrawn guard's eye missed its rest direction");

        // Once drawn close up, the eye follows the body.
        fixture.Renderer.Simulation.ObserveAllForTesting();
        Assert.True(soldier.ProbeDetection(bodyAhead).InCone, "a drawn guard's eye did not follow its body");
    }

    private sealed class Fixture(SoldierRenderer renderer) : IDisposable
    {
        public SoldierRenderer Renderer { get; } = renderer;

        public static Fixture Load(int level)
        {
            VirtualFileSystem fileSystem = new();
            fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
            ResourceRegistry resources = new(fileSystem);
            GameLoop loop = new(new ManualTimeSource());
            PlayerTaskTypes.RegisterAll(loop.Types);

            MissionRegistry registry = new();
            registry.Register(loop.Natives);
            registry.Scan(fileSystem, resources, loop.Natives);

            LevelTask root = (LevelTask)loop.Tasks.New(
                loop.Tasks.Root,
                loop.Types.Get(EngineTaskTypes.Level));
            LoadedMission mission = new MissionLoader(
                registry,
                fileSystem,
                resources,
                new LevelLoader(root, loop.TaskCommands, loop.Natives, resources))
                .Load(level);

            SoldierRenderer renderer = new(new RecordingGraphicsDevice(), resources);
            Assert.True(renderer.Load(mission, root) > 0, $"Level {level} loaded no soldiers");
            Assert.True(
                renderer.Simulation.Animate(new Dictionary<int, GraphFile>(), levelRoot: root) > 0,
                $"Level {level} created no AI brains");
            return new Fixture(renderer);
        }

        public void Dispose() => Renderer.Dispose();
    }
}
