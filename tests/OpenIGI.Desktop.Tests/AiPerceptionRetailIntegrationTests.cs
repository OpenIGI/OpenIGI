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

    /// <summary>
    /// A drawn guard looks along its eye bone as the clip poses it -- the body's matrix times bone 5, what the camera
    /// pass rebuilds the palette with (probed on mission 4's running 404) -- and a standing target ahead of that eye is
    /// detected. A guard whose clip holds its head down looks at the floor, as mission 14's 1111 does in clip 87, so
    /// the test takes, in every level, the first hostile guard whose drawn eye is roughly level, and puts the target
    /// along that eye's own heading.
    /// </summary>
    [RequiresGameDataFact]
    public void ARepresentativeLiveBrainInEveryLevelDetectsAStandingTargetAlongItsEye()
    {
        List<string> failures = [];
        for (int level = 1; level <= 14; level++)
        {
            using Fixture fixture = Fixture.Load(level);
            fixture.Renderer.Simulation.ObserveAllForTesting();
            Igi1HumanAi? soldier = fixture.Renderer.Simulation.BrainsForTesting.FirstOrDefault(brain =>
                brain.PlayerIsEnemy && Math.Abs(brain.CurrentEyeOrientation.M32) < 0.5f);
            if (soldier is null)
            {
                failures.Add($"Level {level} has no hostile live brain with a level eye");
                continue;
            }

            // The eye's forward is its matrix's second column (AiEyeSnapshot: local = delta * M); level it.
            Matrix3f eye = soldier.CurrentEyeOrientation;
            double length = Math.Sqrt((eye.M12 * eye.M12) + (eye.M22 * eye.M22));
            Vector3d mark = new(
                soldier.Position.X + (eye.M12 / length * 5.0 * Metre),
                soldier.Position.Y + (eye.M22 / length * 5.0 * Metre),
                soldier.Position.Z + Igi1HumanAi.DetectionMarkAboveFeet(crouching: false));
            (double distance, bool inCone, _, _, _) = soldier.ProbeDetection(mark);
            if (!inCone)
            {
                failures.Add($"Level {level}'s drawn eye rejected a mark {distance / Metre:0.00} m along its own heading");
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
    /// A guard nothing has drawn looks along the rest eye turned by the heading it was placed with, B * R, however
    /// it turns afterwards. Probed on mission 4: thirteen undrawn soldiers placed at 0, 24, -54, -78, 96 and 108
    /// degrees, each B * R to 1e-4. Mission 1's level-start guards are all placed at 0, which is why the first
    /// measurement there saw R itself on every one of them.
    /// </summary>
    [RequiresGameDataFact]
    public void AnUndrawnGuardLooksAlongItsPlacedHeadingNotWorldNorth()
    {
        using Fixture fixture = Fixture.Load(1);
        Igi1HumanAi soldier = fixture.Renderer.Simulation.BrainsForTesting.First(brain =>
            brain.PlayerIsEnemy && Math.Abs(Math.Sin(brain.Facing)) > 0.9);

        // Five metres along the heading it was placed with: where the placed rest eye looks.
        Vector3d forward = soldier.Orientation.Transposed().Transform(new Vector3d(0.0, 5.0 * Metre, 0.0));
        Vector3d bodyAhead = new(
            soldier.Position.X + forward.X,
            soldier.Position.Y + forward.Y,
            soldier.Position.Z + Igi1HumanAi.DetectionMarkAboveFeet(crouching: false));
        Assert.True(soldier.ProbeDetection(bodyAhead).InCone, "an undrawn guard's eye missed its placed heading");

        // Five metres along world +Y, a quarter turn off that heading: outside the cone.
        Vector3d northAhead = new(
            soldier.Position.X,
            soldier.Position.Y + (5.0 * Metre),
            soldier.Position.Z + Igi1HumanAi.DetectionMarkAboveFeet(crouching: false));
        Assert.False(soldier.ProbeDetection(northAhead).InCone, "an undrawn guard's eye ignored its placed heading");

        // Once drawn close up, the eye follows the body.
        fixture.Renderer.Simulation.ObserveAllForTesting();
        Assert.True(soldier.ProbeDetection(bodyAhead).InCone, "a drawn guard's eye did not follow its body");
    }

    /// <summary>
    /// A brain is given the level's counter as the original reads it during a tick -- 0 on the level's first, where
    /// the counter is incremented after the tick's handlers -- so a level-start soldier creates on counter 0, as
    /// mission 4's patrols did on the probe, and its sweep phase (counter mod 4) lines up with the original's.
    /// </summary>
    [RequiresGameDataFact]
    public void ALevelsSecondTickReachesItsSoldiersAsCounterOne()
    {
        using Fixture fixture = Fixture.Load(4);

        fixture.Renderer.Tick();
        fixture.Renderer.Tick();

        IReadOnlyList<Igi1HumanAi> brains = fixture.Renderer.Simulation.BrainsForTesting;
        Assert.Contains(brains, brain => brain.LastTick == 1);
        Assert.All(brains, brain => Assert.InRange(brain.LastTick, 0, 1));
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
