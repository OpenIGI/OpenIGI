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
/// Proves the cutscene camera matches the authored objects.qvm EditCamera data for every
/// CutScene in campaign missions 10-12: at each shot's start the production interpolation
/// (<see cref="CutsceneRuntime.EvaluateSceneFrame"/>) must pass exactly through the authored
/// position, orientation, FOV and filter of that shot, because the retail Hermite basis
/// interpolates knot values exactly. Within each static shot's window the sampler must stay on
/// that shot carrying its authored filter.
/// </summary>
public sealed class CutsceneFidelityLevels10to12Tests
{
    [RequiresGameDataFact]
    public void MissionTenStaticCameraShotsMatchTheAuthoredEditCameraData() =>
        AssertMissionCameraFidelity(10);

    [RequiresGameDataFact]
    public void MissionElevenStaticCameraShotsMatchTheAuthoredEditCameraData() =>
        AssertMissionCameraFidelity(11);

    [RequiresGameDataFact]
    public void MissionTwelveStaticCameraShotsMatchTheAuthoredEditCameraData() =>
        AssertMissionCameraFidelity(12);

    private static void AssertMissionCameraFidelity(int mission)
    {
        VirtualFileSystem fs = new();
        fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs);
        GameLoop loop = new(new ManualTimeSource());
        RegisterCutsceneTypes(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fs, resources, loop.Natives);
        _ = new MissionLoader(missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(mission);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime runtime = new(root, loop.Variables);

        List<string> problems = [];
        int staticChecks = 0;
        int excludedShots = 0;
        int scenesChecked = 0;

        foreach (CutSceneTask scene in Walk(root).OfType<CutSceneTask>())
        {
            List<(DeclaredTask Shot, float Duration)> shots = [];
            foreach (GameTask task in Walk(scene))
            {
                if (task is DeclaredTask declared
                    && string.Equals(task.Type.Name, "EditCamera", StringComparison.Ordinal))
                {
                    shots.Add((declared, Real(declared, "Duration")));
                }
            }

            Assert.NotEmpty(shots);
            scenesChecked++;

            // Accumulate shot start times EXACTLY like SceneState.Timed (double += float duration)
            // so every asserted knot time matches the runtime's schedule bit-for-bit.
            double totalSeconds = 0d;
            foreach ((_, float duration) in shots)
            {
                totalSeconds += Math.Max(0f, duration);
            }

            Assert.True(totalSeconds > 0f, $"mission {mission} scene {scene.Id} has zero total duration");
            Assert.NotNull(runtime.EvaluateSceneFrame(scene, totalSeconds));

            double elapsed = 0d;
            for (int index = 0; index < shots.Count; index++)
            {
                (DeclaredTask shot, _) = shots[index];
                int link = Int(shot, "Link task ID", -1);
                int target = Int(shot, "Target task ID", -1);
                float shake = Real(shot, "Camera shake");
                float duration = Math.Max(0f, shots[index].Duration);

                // Authored zero-duration shots are terminal Hermite knots sharing their predecessor's
                // end time. At such a tie the retail current-shot scan selects the LAST group member,
                // so an instantaneous knot cannot be sampled distinctly and is excluded.
                if (duration <= 0f)
                {
                    excludedShots++;
                    continue;
                }
                if (link != -1 || target != -1 || shake != 0f)
                {
                    excludedShots++;
                    elapsed += duration;
                    continue;
                }

                string label = $"mission {mission} scene {scene.Id} shot #{index} (t={elapsed:R}s)";

                // Window check: just inside the shot the sampler must sit on THIS shot and carry its
                // authored filter (active.Filter is constant within a shot in retail).
                CutsceneFrame? windowFrame = runtime.EvaluateSceneFrame(scene, elapsed + (1.0 / CutsceneRuntime.Frequency));
                Assert.NotNull(windowFrame);
                if (windowFrame is not null)
                {
                    if (windowFrame.ShotIndex != index)
                    {
                        problems.Add($"{label}: window shot index {windowFrame.ShotIndex} != {index}");
                    }

                    string windowFilter = Text(shot, "FILTER");
                    if (windowFrame.Filter != windowFilter)
                    {
                        problems.Add($"{label}: window filter '{windowFrame.Filter}' != authored '{windowFilter}'");
                    }
                }

                // Knot check: the Hermite basis interpolates the authored knot values exactly.
                CutsceneFrame? frame = runtime.EvaluateSceneFrame(scene, elapsed);
                Assert.NotNull(frame);
                if (frame is null)
                {
                    elapsed += duration;
                    continue;
                }

                staticChecks++;

                Vector3d expectedPosition = Vector(shot, "Position") / WorldScene.UnitsPerMetre;
                // Float32 knot arithmetic leaves ~1/1024 m residue on kilometre-scale coordinates;
                // 5 mm is far below anything visible while still catching real interpolation errors.
                double positionDelta = (frame.Camera.Position - expectedPosition).Length;
                if (positionDelta > 1e-3)
                {
                    problems.Add(
                        $"{label}: position {frame.Camera.Position} != authored {expectedPosition} (delta {positionDelta:R})");
                }

                float expectedFov = Math.Clamp(Real(shot, "FOV", 1f), 0.001f, 3f);
                if (Math.Abs(frame.FieldOfView - expectedFov) > 1e-4)
                {
                    problems.Add($"{label}: fov {frame.FieldOfView} != authored {expectedFov}");
                }

                string filter = Text(shot, "FILTER");
                if (frame.Filter != filter)
                {
                    problems.Add($"{label}: filter '{frame.Filter}' != authored '{filter}'");
                }

                if (frame.ShotIndex != index)
                {
                    problems.Add($"{label}: shot index {frame.ShotIndex} != {index}");
                }

                // Retail EditCamera stores an engine rotation (determinant +1); the render boundary
                // transposes it into Camera3D's row-axis view basis and flips the up row sign.
                Matrix3f axes = Matrix3f.FromEngineEulerAngles(
                    Real(shot, "Alpha"), Real(shot, "Beta"), Real(shot, "Gamma")).Transposed();
                Matrix3f expectedOrientation = Matrix3f.FromRows(axes.Row1, -axes.Row2, axes.Row3);
                Matrix3f actual = frame.Camera.Orientation;
                float orientationDelta = 0f;
                for (int r = 0; r < 3; r++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        orientationDelta = Math.Max(
                            orientationDelta,
                            Math.Abs(actual[r, c] - expectedOrientation[r, c]));
                    }
                }

                if (orientationDelta > 1e-4)
                {
                    problems.Add(
                        $"{label}: orientation delta {orientationDelta} — authored eulers ({Real(shot, "Alpha")}, {Real(shot, "Beta")}, {Real(shot, "Gamma")})");
                }

                elapsed += duration;
            }
        }

        Assert.True(scenesChecked > 0, $"mission {mission} has no authored CutScene tasks");
        Assert.True(
            staticChecks > 0,
            $"mission {mission}: no static shots checked; {excludedShots} shots excluded (link/target/shake/zero-duration)");
        Assert.True(
            problems.Count == 0,
            $"mission {mission}: {problems.Count} fidelity mismatches over {staticChecks} static shots "
            + $"({excludedShots} excluded link/target/shake/zero-duration shots):\n" + string.Join("\n", problems));
    }

    private static void RegisterCutsceneTypes(TaskTypeRegistry types)
    {
        PlayerTaskTypes.RegisterAll(types);
        types.Register<CutSceneTask>(CutSceneTask.TypeName);
        types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
    }

    private static IEnumerable<GameTask> Walk(GameTask root)
    {
        foreach (GameTask child in root.Children)
        {
            yield return child;
            foreach (GameTask nested in Walk(child))
            {
                yield return nested;
            }
        }
    }

    private static float Real(DeclaredTask task, string name, float fallback = 0f) =>
        task.TryGetValue(name, out object? value) && value is float number ? number : fallback;

    private static int Int(DeclaredTask task, string name, int fallback) =>
        task.TryGetValue(name, out object? value) && value is int number ? number : fallback;

    private static string Text(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is string text ? text : string.Empty;

    private static Vector3d Vector(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is Vector3d vector ? vector : default;
}
