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
/// Proves the runtime cutscene camera reproduces the authored objects.qvm EditCamera data for
/// every CutScene of campaign missions 4, 5 and 6. Every authored parameter (positions, Euler
/// angles, FOV, durations, smooth flags, filters) is fed through an independently written
/// evaluator of the documented retail spline (Catmull-Rom Hermite over non-uniform knots with the
/// smooth-flag endpoint fallbacks) and compared against the runtime frame at matched times.
/// Shots whose start lands exactly on an integer tick additionally assert knot-exact equality
/// with the raw authored values.
/// </summary>
public sealed class CutsceneFidelityLevels4to6Tests
{
    private const double TickEpsilon = 1e-9;

    [RequiresGameDataFact]
    public void MissionFourCameraMatchesAuthoredEditCameraData()
    {
        RunCameraFidelity(4);
    }

    [RequiresGameDataFact]
    public void MissionFiveCameraMatchesAuthoredEditCameraData()
    {
        RunCameraFidelity(5);
    }

    [RequiresGameDataFact]
    public void MissionSixCameraMatchesAuthoredEditCameraData()
    {
        RunCameraFidelity(6);
    }

    private static void RunCameraFidelity(int mission)
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
        int excludedShots = 0;

        foreach (CutSceneTask scene in Walk(root).OfType<CutSceneTask>())
        {
            List<DeclaredTask> authored = Walk(scene)
                .Where(static t => t is DeclaredTask d && string.Equals(t.Type.Name, "EditCamera", StringComparison.Ordinal))
                .Cast<DeclaredTask>()
                .ToList();
            Assert.NotEmpty(authored);

            List<AuthoredShot> shots = [];
            foreach (DeclaredTask camera in authored)
            {
                shots.Add(new AuthoredShot(
                    Position: TryVector(camera, "Position"),
                    Alpha: Real(camera, "Alpha"),
                    Beta: Real(camera, "Beta"),
                    Gamma: Real(camera, "Gamma"),
                    Fov: Math.Clamp(Real(camera, "FOV", 1f), 0.001f, 3f),
                    Duration: Math.Max(0f, Real(camera, "Duration")),
                    Link: Int(camera, "Link task ID", -1),
                    Target: Int(camera, "Target task ID", -1),
                    Shake: Real(camera, "Camera shake"),
                    Filter: Text(camera, "FILTER"),
                    Smooth: Bool(camera, "Smooth to next")));
            }

            double total = shots.Sum(s => s.Duration);
            Assert.True(total > 0f, $"mission {mission} scene {scene.Id}: no authored shot duration");

            // The clock clamp happens upstream in Tick; the seam must still serve a frame past the
            // authored end so the host-side clamp is the only place the scene stops.
            Assert.NotNull(runtime.EvaluateSceneFrame(scene, total + 1.0 / CutsceneRuntime.Frequency));

            double[] starts = new double[shots.Count];
            double acc = 0;
            for (int i = 0; i < shots.Count; i++)
            {
                starts[i] = acc;
                acc += shots[i].Duration;
            }
            int finalTick = (int)Math.Ceiling(total * 30);

            for (int i = 0; i < shots.Count; i++)
            {
                AuthoredShot shot = shots[i];
                if (shot.Link != -1 || shot.Target != -1 || shot.Shake != 0f)
                {
                    excludedShots++;
                    continue;
                }

                // Sample just inside this shot's window on the continuous scene clock.
                double time = starts[i] + 1.0 / CutsceneRuntime.Frequency;
                double finalTime = total + 1.0 / CutsceneRuntime.Frequency;
                if (time > finalTime)
                {
                    time = finalTime;
                }

                CutsceneFrame? frame = runtime.EvaluateSceneFrame(scene, time);
                Assert.NotNull(frame);
                string prefix = $"mission {mission} scene {scene.Id} shot {i} (t {time:R})";

                // Retail knot selection: the LAST entry whose start is not past the time wins, so
                // zero-duration marker knots sharing a start are skipped in favour of their successor.
                int expectedIndex = SelectIndex(starts, time);
                if (frame!.ShotIndex != expectedIndex)
                {
                    failures.Add($"{prefix}: ShotIndex {frame.ShotIndex} != expected {expectedIndex}");
                    continue;
                }

                staticShotsChecked++;

                Vector3d expectedPosition = EvaluateVector(shots, starts, expectedIndex, time) / WorldScene.UnitsPerMetre;
                // Authored knots are float32; at 27 km their own representation quantum is ~2 mm.
                // Agreement is therefore required to the source-data precision, not to double eps:
                // a few float32 ulps of the coordinate magnitude plus a 1 mm floor.
                double positionTolerance = 1e-3 + (4e-7 * expectedPosition.Length);
                double positionDelta = (frame.Camera.Position - expectedPosition).Length;
                if (positionDelta > positionTolerance)
                {
                    failures.Add(
                        $"{prefix}: position {frame.Camera.Position} != evaluated {expectedPosition} (delta {positionDelta:R})");
                }

                float expectedFov = (float)EvaluateComponent(shots, starts, expectedIndex, time, s => s.Fov);
                if (Math.Abs(frame.FieldOfView - expectedFov) > 1e-4)
                {
                    failures.Add($"{prefix}: fov {frame.FieldOfView} != evaluated {expectedFov}");
                }

                string expectedFilter = shots[expectedIndex].Filter;
                if (!string.Equals(frame.Filter, expectedFilter, StringComparison.Ordinal))
                {
                    failures.Add($"{prefix}: filter '{frame.Filter}' != authored '{expectedFilter}'");
                }

                double alpha = EvaluateComponent(shots, starts, expectedIndex, time, s => s.Alpha);
                double beta = EvaluateComponent(shots, starts, expectedIndex, time, s => s.Beta);
                double gamma = EvaluateComponent(shots, starts, expectedIndex, time, s => s.Gamma);
                Matrix3f axes = Matrix3f.FromEngineEulerAngles((float)alpha, (float)beta, (float)gamma).Transposed();
                Matrix3f expectedOrientation = Matrix3f.FromRows(axes.Row1, -axes.Row2, axes.Row3);
                float orientationDelta = OrientationDelta(frame.Camera.Orientation, expectedOrientation);
                if (orientationDelta > 1e-3f)
                {
                    failures.Add(
                        $"{prefix}: orientation delta {orientationDelta:R} exceeds 1e-3");
                }

                // Where the authored boundary lands exactly on a logic tick the sample time equals
                // the knot, so the spline must pass exactly through the authored values.
                if (Math.Abs(time - starts[i]) < TickEpsilon && expectedIndex == i)
                {
                    Vector3d knotPosition = shot.Position / WorldScene.UnitsPerMetre;
                    double knotDelta = (frame.Camera.Position - knotPosition).Length;
                    if (knotDelta > 1e-4)
                    {
                        failures.Add(
                            $"{prefix}: knot position {frame.Camera.Position} != authored {knotPosition} (delta {knotDelta:R})");
                    }
                    if (Math.Abs(frame.FieldOfView - shot.Fov) > 1e-4)
                    {
                        failures.Add($"{prefix}: knot fov {frame.FieldOfView} != authored {shot.Fov}");
                    }
                }
            }

            scenesChecked++;
        }

        Assert.True(scenesChecked > 0, $"mission {mission}: no authored CutScene tasks found");
        Assert.True(
            failures.Count == 0,
            $"mission {mission}: {failures.Count} camera mismatches across {scenesChecked} scenes "
            + $"({staticShotsChecked} static shots asserted, {excludedShots} linked/shaken shots excluded)\n"
            + string.Join("\n", failures.Take(20)));
    }

    /// <summary>Retail selection: the last knot whose start is not past the time.</summary>
    private static int SelectIndex(double[] starts, double time)
    {
        int index = 0;
        for (int i = 1; i < starts.Length; i++)
        {
            if (starts[i] > time)
            {
                break;
            }
            index = i;
        }
        return index;
    }

    /// <summary>
    /// Independently written evaluation of the documented retail spline: cubic Hermite over the
    /// four-knot neighbourhood with the smooth-flag endpoint fallbacks
    /// (!p0.SmoothToNext → p0:=p1, !p1.SmoothToNext → p2:=p1, !p2.SmoothToNext → p3:=p2).
    /// </summary>
    private static double EvaluateComponent(
        List<AuthoredShot> shots, double[] starts, int current, double time, Func<AuthoredShot, double> value)
    {
        int i0 = Math.Max(0, current - 1);
        int i1 = current;
        int i2 = Math.Min(shots.Count - 1, current + 1);
        int i3 = Math.Min(shots.Count - 1, current + 2);

        int p0 = i0, p1 = i1, p2 = i2, p3 = i3;
        if (!shots[p0].Smooth)
        {
            p0 = p1;
        }
        if (!shots[p1].Smooth)
        {
            p2 = p1;
        }
        if (!shots[p2].Smooth)
        {
            p3 = p2;
        }

        return Hermite(
            time, starts[p0], starts[p1], starts[p2], starts[p3],
            value(shots[p0]), value(shots[p1]), value(shots[p2]), value(shots[p3]));
    }

    private static Vector3d EvaluateVector(
        List<AuthoredShot> shots, double[] starts, int current, double time) => new(
        EvaluateComponent(shots, starts, current, time, s => s.Position.X),
        EvaluateComponent(shots, starts, current, time, s => s.Position.Y),
        EvaluateComponent(shots, starts, current, time, s => s.Position.Z));

    /// <summary>Non-uniform Catmull-Rom Hermite, transcribed independently from the retail math.</summary>
    private static double Hermite(
        double time, double t0, double t1, double t2, double t3,
        double v0, double v1, double v2, double v3)
    {
        double s1 = t2 == t0 ? 0.0 : 2.0 * (t1 - t0) / (t2 - t0) * ((v2 - v0) * 0.5);
        double s2 = t3 == t1 ? 0.0 : 2.0 * (t2 - t1) / (t3 - t1) * ((v3 - v1) * 0.5);
        double u = t2 == t1 ? 0.0 : (time - t1) / (t2 - t1);
        double a = (2.0 * v1) - (2.0 * v2) + s2 + s1;
        double b = (3.0 * v2) - (3.0 * v1) - (2.0 * s1) - s2;
        return v1 + (u * u * u * a) + (u * u * b) + (u * s1);
    }

    private static float OrientationDelta(Matrix3f actual, Matrix3f expected)
    {
        float delta = 0f;
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                delta = MathF.Max(delta, MathF.Abs(actual[row, column] - expected[row, column]));
            }
        }
        return delta;
    }

    private static Vector3d TryVector(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is Vector3d vector ? vector : default;

    private static float Real(DeclaredTask task, string name, float fallback = 0f) =>
        task.TryGetValue(name, out object? value) && value is float number ? number : fallback;

    private static int Int(DeclaredTask task, string name, int fallback = 0) =>
        task.TryGetValue(name, out object? value) && value is int number ? number : fallback;

    private static bool Bool(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is bool flag && flag;

    private static string Text(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is string text ? text : string.Empty;

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

    private sealed record AuthoredShot(
        Vector3d Position,
        float Alpha,
        float Beta,
        float Gamma,
        float Fov,
        float Duration,
        int Link,
        int Target,
        float Shake,
        string Filter,
        bool Smooth);

    private static void RegisterCutsceneTypes(TaskTypeRegistry types)
    {
        PlayerTaskTypes.RegisterAll(types);
        types.Register<CutSceneTask>(CutSceneTask.TypeName);
        types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
    }
}
