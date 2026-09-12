using OpenIGI.Core.Numerics;
using OpenIGI.Desktop;
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

namespace OpenIGI.Desktop.Tests;

/// <summary>Exercises the live animated-eye binding against retail soldiers from every campaign level.</summary>
public sealed class AiPerceptionRetailIntegrationTests
{
    private const double Metre = 4096.0;

    [RequiresGameDataFact]
    public void ARepresentativeLiveEyeInEveryLevelSeesAStandingTargetAlongTheSoldiersForwardAxis()
    {
        List<string> failures = [];
        for (int level = 1; level <= 14; level++)
        {
            using Fixture fixture = Fixture.Load(level);
            AiSoldier? soldier = fixture.Renderer.BrainsForTesting.FirstOrDefault(brain => brain.PlayerIsEnemy);
            if (soldier is null)
            {
                failures.Add($"Level {level} has no hostile live brain");
                continue;
            }

            // AiEyeSnapshot converts world deltas into local eye space with delta * orientation.
            // Therefore a local +Y target is transformed back to world space with the transpose.
            Vector3d forward = soldier.Orientation.Transposed().Transform(new Vector3d(0.0, 5.0 * Metre, 0.0));
            Vector3d mark = new(
                soldier.Position.X + forward.X,
                soldier.Position.Y + forward.Y,
                soldier.Position.Z + AiSoldier.DetectionRaiseStandingInUnits);
            (double distance, bool inCone, _, _, _) = soldier.ProbeDetection(mark);
            if (!inCone)
            {
                failures.Add(
                    $"Level {level}'s live bone-5 eye rejected a body mark {distance / Metre:0.00} m along body +Y");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
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
                renderer.Animate(new Dictionary<int, GraphFile>(), levelRoot: root) > 0,
                $"Level {level} created no AI brains");
            return new Fixture(renderer);
        }

        public void Dispose() => Renderer.Dispose();
    }
}
