using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Proves the cutscene runtime camera reproduces the authored <c>objects.qvm</c> EditCamera data
/// for every CutScene of campaign missions 1–3 (IGI1 data only):
/// <list type="bullet">
/// <item>At every tick-aligned shot knot the evaluated frame sits exactly on the authored
/// position/orientation/FOV/filter — the retail Hermite sampler passes through knots.</item>
/// <item>Across every static shot's whole span the sampler stays on that shot (index) and its
/// authored filter.</item>
/// </list>
/// Knots that fall between 30 Hz ticks cannot be sampled exactly by any tick-driven consumer
/// (retail included); those are covered by the span checks instead.
/// </summary>
public sealed class CutsceneFidelityLevels1to3Tests
{
    private const float PositionToleranceMetres = 1e-3f;
    private const float ElementTolerance = 2e-3f;

    [RequiresGameDataFact]
    public void MissionOneCameraMatchesAuthoredEditCameraShots() =>
        AssertMissionCameraMatchesAuthoredShots(1);

    [RequiresGameDataFact]
    public void MissionTwoCameraMatchesAuthoredEditCameraShots() =>
        AssertMissionCameraMatchesAuthoredShots(2);

    [RequiresGameDataFact]
    public void MissionThreeCameraMatchesAuthoredEditCameraShots() =>
        AssertMissionCameraMatchesAuthoredShots(3);

    private static void AssertMissionCameraMatchesAuthoredShots(int mission)
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

        List<string> failures = [];
        int scenesChecked = 0;
        int staticShotsChecked = 0;
        int knotsExactChecked = 0;
        int excludedShots = 0;

        foreach (CutSceneTask scene in Walk(root).OfType<CutSceneTask>())
        {
            List<DeclaredTask> shotTasks = Walk(scene)
                .Where(static t => t is DeclaredTask d && string.Equals(t.Type.Name, "EditCamera", StringComparison.Ordinal))
                .Cast<DeclaredTask>()
                .ToList();
            Assert.NotEmpty(shotTasks);

            // Mirror the runtime's Timed accumulation bit-for-bit: double += float, walk order.
            double[] starts = new double[shotTasks.Count];
            double acc = 0;
            for (int i = 0; i < shotTasks.Count; i++)
            {
                starts[i] = acc;
                acc += Real(shotTasks[i], "Duration");
            }

            double total = acc;
            Assert.True(total > 0f, $"mission {mission} scene {scene.Id}: no authored shot duration");
            scenesChecked++;

            Assert.NotNull(runtime.EvaluateSceneFrame(scene, 0));
            Assert.NotNull(runtime.EvaluateSceneFrame(scene, total + 1.0 / CutsceneRuntime.Frequency));

            for (int k = 0; k < shotTasks.Count; k++)
            {
                DeclaredTask shot = shotTasks[k];
                bool linked = Int(shot, "Link task ID", -1) != -1 || Int(shot, "Target task ID", -1) != -1;
                bool shaking = Real(shot, "Camera shake") != 0f;
                if (linked || shaking || Real(shot, "Duration") == 0f)
                {
                    excludedShots++;
                    continue;
                }

                string label = $"mission {mission} scene {scene.Id} shot {k}";
                staticShotsChecked++;

                // Span check: on every interior tick the sampler must sit on THIS shot and carry its
                // authored filter (active.Filter is constant within a shot in retail).
                double spanStartTime = starts[k] + 1.0 / CutsceneRuntime.Frequency;
                double spanEnd = starts[k] + Real(shot, "Duration");
                double spanEndTime = Math.Max(spanStartTime, spanEnd - 1.0 / CutsceneRuntime.Frequency);
                foreach (double sampleTime in new[] { spanStartTime, (spanStartTime + spanEndTime) / 2, spanEndTime })
                {
                    CutsceneFrame spanFrame = runtime.EvaluateSceneFrame(scene, sampleTime)
                        ?? throw new Xunit.Sdk.XunitException($"{label}: no frame at {sampleTime:F3}s");
                    if (spanFrame.ShotIndex != k)
                    {
                        failures.Add($"{label} @t {sampleTime:F3}s: shot index {spanFrame.ShotIndex} != {k}");
                    }

                    string authoredFilter = Text(shot, "FILTER");
                    if (!string.Equals(spanFrame.Filter, authoredFilter, StringComparison.Ordinal))
                    {
                        failures.Add($"{label} @t {sampleTime:F3}s: filter '{spanFrame.Filter}' != authored '{authoredFilter}'");
                    }
                }

                // Knot-exactness check, only where the authored knot lands on a 30 Hz tick.
                double knotSeconds = starts[k];
                double knotTicks = knotSeconds * CutsceneRuntime.Frequency;
                long rounded = (long)Math.Round(knotTicks);
                if (Math.Abs(knotTicks - rounded) > 1e-3)
                {
                    continue;
                }

                // Resolve ties exactly like the production sampler: the LAST entry whose float start
                // is <= the sample time.
                double time = knotSeconds;
                int selected = 0;
                for (int i = 1; i < shotTasks.Count; i++)
                {
                    if (!(starts[i] > time))
                    {
                        selected = i;
                    }
                }

                DeclaredTask expectedShot = shotTasks[selected];
                if (Int(expectedShot, "Link task ID", -1) != -1
                    || Int(expectedShot, "Target task ID", -1) != -1
                    || Real(expectedShot, "Camera shake") != 0f)
                {
                    excludedShots++;
                    continue;
                }

                knotsExactChecked++;
                CutsceneFrame frame = runtime.EvaluateSceneFrame(scene, knotSeconds)
                    ?? throw new Xunit.Sdk.XunitException($"{label}: no frame at knot");

                string knotLabel = $"{label} (selected {selected}) at {knotSeconds:F2}s";

                Vector3d authoredPosition = Position(expectedShot);
                double delta = (frame.Camera.Position - authoredPosition / WorldScene.UnitsPerMetre).Length;
                if (delta > PositionToleranceMetres)
                {
                    failures.Add($"{knotLabel}: position {frame.Camera.Position} != authored "
                        + $"{authoredPosition / WorldScene.UnitsPerMetre} (delta={delta:R})");
                }

                float expectedFov = Math.Clamp(Real(expectedShot, "FOV", 1f), 0.001f, 3f);
                if (Math.Abs(frame.FieldOfView - expectedFov) > ElementTolerance)
                {
                    failures.Add($"{knotLabel}: fov {frame.FieldOfView} != authored {expectedFov}");
                }

                if (frame.ShotIndex != selected)
                {
                    failures.Add($"{knotLabel}: shot index {frame.ShotIndex} != expected {selected}");
                }

                Matrix3f axes = Matrix3f.FromEngineEulerAngles(
                    Real(expectedShot, "Alpha"), Real(expectedShot, "Beta"), Real(expectedShot, "Gamma")).Transposed();
                Matrix3f expectedOrientation = Matrix3f.FromRows(axes.Row1, -axes.Row2, axes.Row3);
                float[] expectedElements =
                [
                    expectedOrientation.M11, expectedOrientation.M12, expectedOrientation.M13,
                    expectedOrientation.M21, expectedOrientation.M22, expectedOrientation.M23,
                    expectedOrientation.M31, expectedOrientation.M32, expectedOrientation.M33,
                ];
                Matrix3f actualOrientation = frame.Camera.Orientation;
                float[] actualElements =
                [
                    actualOrientation.M11, actualOrientation.M12, actualOrientation.M13,
                    actualOrientation.M21, actualOrientation.M22, actualOrientation.M23,
                    actualOrientation.M31, actualOrientation.M32, actualOrientation.M33,
                ];
                for (int e = 0; e < expectedElements.Length; e++)
                {
                    if (Math.Abs(expectedElements[e] - actualElements[e]) > ElementTolerance)
                    {
                        failures.Add($"{knotLabel}: orientation element {e} {actualElements[e]} != authored {expectedElements[e]}");
                        break;
                    }
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            $"mission {mission}: {failures.Count} authored-camera mismatches across {scenesChecked} scenes "
            + $"({staticShotsChecked} static shots span-checked, {knotsExactChecked} knots exactly checked, "
            + $"{excludedShots} linked/shaking/zero-duration shots excluded):\n"
            + string.Join("\n", failures.Take(20)));
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

    private static Vector3d Position(DeclaredTask task) =>
        task.TryGetValue("Position", out object? value) && value is Vector3d vector ? vector : default;

    private static float Real(DeclaredTask task, string name, float fallback = 0f) =>
        task.TryGetValue(name, out object? value) && value is float number ? number : fallback;

    private static int Int(DeclaredTask task, string name, int fallback) =>
        task.TryGetValue(name, out object? value) && value is int number ? number : fallback;

    private static string Text(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is string text ? text : string.Empty;

    private static void RegisterCutsceneTypes(TaskTypeRegistry types)
    {
        PlayerTaskTypes.RegisterAll(types);
        types.Register<CutSceneTask>(CutSceneTask.TypeName);
        types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
    }
}
