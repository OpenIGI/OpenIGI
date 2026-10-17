using OpenIGI.Core.Numerics;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Engine.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

internal readonly record struct AuthoredLevelObjectTransform(
    string Id,
    Vector3d PositionUnits,
    Matrix3f Orientation,
    bool HasDeclaredOrientation);

/// <summary>Verifies retail level object placement and orientation across every campaign mission.</summary>
public sealed class LevelPositionVerificationTests
{
    [Fact]
    public void AuthoredPositionAndOrientationPredicatesMatchRetailVerifierSemantics()
    {
        Vector3d authored = new(1000, 2000, 3000);
        Vector3d runtime = authored / WorldScene.UnitsPerMetre;

        Assert.True(LevelPositionValidator.PosMatch(authored, runtime));
        Assert.False(LevelPositionValidator.PosMatch(new Vector3d(authored.X + 1, authored.Y, authored.Z), runtime));
        Assert.True(LevelPositionValidator.PosMatch(new Vector3d(authored.X + 1, authored.Y, authored.Z), runtime, 1));
        Assert.False(LevelPositionValidator.PosMatch(new Vector3d(authored.X + 2, authored.Y, authored.Z), runtime, 1));

        Matrix3f authoredOrientation = Matrix3f.RotationZ(1.5708f);
        Assert.True(LevelPositionValidator.OriMatch(authoredOrientation, authoredOrientation));
        Assert.False(
            LevelPositionValidator.OriMatch(
                authoredOrientation,
                Matrix3f.RotationZ(1.5708f + (LevelPositionValidator.OrientationTolerance * 2f))));
    }

    [RequiresGameDataFact]
    public void EveryCampaignMissionHasFiniteRuntimeObjectTransformsMatchingAuthoredDirectTransforms()
    {
        int checkedObjects = 0;
        for (int missionId = 1; missionId <= 14; missionId++)
        {
            WorldScene scene = Build(missionId);
            foreach (WorldPlacement placement in scene.Placements)
            {
                AssertFinite(placement.Position, $"mission {missionId} {placement}");
                AssertOrthonormal(placement.Orientation, $"mission {missionId} {placement}", placement.IsAttached);

                // Attached children inherit their parent's transform plus their authored attachment
                // offset, and spline segments are generated between waypoint tasks. In both cases the
                // Owner's direct Position is not the placement's expected world position. They are still
                // covered by the finite/orthonormal checks above.
                if (placement.IsAttached || placement.TaskName.StartsWith("spline:", StringComparison.Ordinal))
                {
                    continue;
                }

                if (placement.Owner is not DeclaredTask declared
                    || !TryReadAuthored(declared, out AuthoredLevelObjectTransform authored))
                {
                    continue;
                }

                if (declared.Type.Name == WorldScene.SecurityCameraType)
                {
                    AssertCameraTransform(scene, placement, declared, authored.PositionUnits);
                    checkedObjects++;
                    continue;
                }

                Assert.True(
                    LevelPositionValidator.PosMatch(authored.PositionUnits, placement.Position),
                    $"mission {missionId} {placement.Instance.Name} position differs from authored {authored.PositionUnits}");
                if (authored.HasDeclaredOrientation)
                {
                    Assert.True(
                        LevelPositionValidator.OriMatch(authored.Orientation, placement.Orientation),
                        $"mission {missionId} {placement.Instance.Name} orientation differs from authored data");
                }
                checkedObjects++;
            }
        }

        Assert.True(checkedObjects > 100, $"expected authored transforms across the campaign, checked {checkedObjects}");
    }

    [RequiresGameDataFact]
    public void EveryCampaignMissionEmitsReferenceLevelLoaderRecordsWithoutPlacementMismatches()
    {
        // Large enough that no mission's placement records push an earlier mission's out.
        MemorySink logger = new(capacity: 1 << 20);

        for (int missionId = 1; missionId <= 14; missionId++)
        {
            _ = Build(missionId, logger);
        }

        IReadOnlyList<LogRecord> records = logger.Snapshot();
        string[] loaded = records
            .Where(record => record.Category == "World.Loader"
                && record.Message.StartsWith("Object Loaded:", StringComparison.Ordinal))
            .Select(record => record.Message)
            .ToArray();

        Assert.True(loaded.Length > 1000, $"expected reference placement records, got {loaded.Length}");
        string[] mismatches = records
            .Select(record => record.Message)
            .Where(line => line.Contains("Verification=MISMATCH", StringComparison.Ordinal))
            .ToArray();
        Assert.True(mismatches.Length == 0, string.Join(Environment.NewLine, mismatches.Take(10)));
        Assert.All(
            loaded,
            line =>
            {
                Assert.Contains("ModelID=", line, StringComparison.Ordinal);
                Assert.Contains("Pos=(", line, StringComparison.Ordinal);
                Assert.Contains("Ori=(", line, StringComparison.Ordinal);
                Assert.Contains("Tex=", line, StringComparison.Ordinal);
                Assert.Contains("Model=", line, StringComparison.Ordinal);
                Assert.Contains("Source=LOCAL:missions/", line, StringComparison.Ordinal);
            });
    }

    private static void AssertCameraTransform(
        WorldScene scene,
        WorldPlacement placement,
        DeclaredTask task,
        Vector3d authoredPosition)
    {
        string holderName = task.TryGetValue("Holder Model", out object? model)
            && model is string { Length: > 0 } name ? name : "313_01_1";
        WorldPlacement holder = Assert.Single(scene.Placements,
            candidate => ReferenceEquals(candidate.Owner, task)
                && !candidate.IsAttached && candidate.Instance.Name == holderName);
        float gamma = task.TryGetValue("Holder Gamma", out object? value) && value is float angle ? angle : 0f;
        Matrix3f holderOrientation = Matrix3f.RotationZ(gamma);
        if (ReferenceEquals(placement, holder))
        {
            Assert.True(LevelPositionValidator.PosMatch(authoredPosition, holder.Position));
            Assert.True(LevelPositionValidator.OriMatch(holderOrientation, holder.Orientation));
            return;
        }

        // The head mounts at the holder's magic vertex, not at the task's direct position.
        // Calculate M*v explicitly to keep this check independent of the runtime transform helper.
        Assert.True(holder.Model.TryGetMagicVertex(0, out Vector3f mount));
        Vector3d offset = new(
            (holderOrientation.M11 * mount.X) + (holderOrientation.M12 * mount.Y) + (holderOrientation.M13 * mount.Z),
            (holderOrientation.M21 * mount.X) + (holderOrientation.M22 * mount.Y) + (holderOrientation.M23 * mount.Z),
            (holderOrientation.M31 * mount.X) + (holderOrientation.M32 * mount.Y) + (holderOrientation.M33 * mount.Z));
        Assert.True(LevelPositionValidator.PosMatch(authoredPosition + offset, placement.Position));
        float alpha = task.TryGetValue("Camera Alpha", out object? a) && a is float av ? av : 0f;
        float headGamma = task.TryGetValue("Camera Gamma", out object? g) && g is float gv ? gv : 0f;
        Assert.True(LevelPositionValidator.OriMatch(
            Matrix3f.FromEngineEulerAngles(alpha, 0f, gamma + headGamma), placement.Orientation));
    }

    private static WorldScene Build(int missionId, IOpenIgiLogger? logger = null)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));

        MissionRegistry registry = new();
        registry.Register(loop.Natives);
        registry.Scan(fileSystem, resources, loop.Natives);

        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);

        TerrainLoader terrainLoader = new(resources, loop.Natives);
        LevelLoader levelLoader = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
        LoadedMission mission = new MissionLoader(
            registry, fileSystem, resources, levelLoader, logger, terrainLoader).Load(missionId);
        new ConditionalContainerGate(loop.Variables).Prepare(levelRoot);
        return WorldScene.Build(mission, levelRoot, magic, logger);
    }

    private static bool TryReadAuthored(DeclaredTask task, out AuthoredLevelObjectTransform transform)
    {
        if (!TryReadAuthoredPosition(task, out Vector3d position))
        {
            transform = default;
            return false;
        }

        bool hasOrientation = task.TryGetValue("Orientation", out object? orientationValue);
        bool hasGamma = task.TryGetValue("Gamma", out object? gammaValue) && gammaValue is float;
        Matrix3f orientation = hasOrientation
            ? orientationValue switch
            {
                Vector3f angles => WorldScene.ToOrientation(angles),
                Matrix3f matrix => matrix,
                float orientationGamma => Matrix3f.RotationZ(orientationGamma),
                _ => Matrix3f.Identity,
            }
            : hasGamma && gammaValue is float gamma
                ? Matrix3f.RotationZ(gamma)
                : Matrix3f.Identity;

        transform = new(task.Name, position, orientation, hasOrientation || hasGamma);
        return true;
    }

    private static bool TryReadAuthoredPosition(DeclaredTask task, out Vector3d position)
    {
        foreach (string name in WorldScene.PositionParameters)
        {
            if (!task.TryGetValue(name, out object? value) || value is not Vector3d vector)
            {
                continue;
            }

            // Trains store a rail scalar under "Position"; only ObjectPos vectors count.
            position = vector;
            return true;
        }

        position = default;
        return false;
    }

    private static void AssertFinite(Vector3d value, string context)
    {
        Assert.True(double.IsFinite(value.X), $"{context}: X is not finite");
        Assert.True(double.IsFinite(value.Y), $"{context}: Y is not finite");
        Assert.True(double.IsFinite(value.Z), $"{context}: Z is not finite");
    }

    private static void AssertOrthonormal(Matrix3f value, string context, bool allowReflection = false)
    {
        Assert.True(float.IsFinite(value.M11) && float.IsFinite(value.M12) && float.IsFinite(value.M13), context);
        Assert.True(float.IsFinite(value.M21) && float.IsFinite(value.M22) && float.IsFinite(value.M23), context);
        Assert.True(float.IsFinite(value.M31) && float.IsFinite(value.M32) && float.IsFinite(value.M33), context);
        Assert.InRange(Math.Abs(Dot(value.Row1, value.Row1) - 1f), 0f, 0.001f);
        Assert.InRange(Math.Abs(Dot(value.Row2, value.Row2) - 1f), 0f, 0.001f);
        Assert.InRange(Math.Abs(Dot(value.Row3, value.Row3) - 1f), 0f, 0.001f);
        Assert.InRange(Math.Abs(Dot(value.Row1, value.Row2)), 0f, 0.001f);
        Assert.InRange(Math.Abs(Dot(value.Row1, value.Row3)), 0f, 0.001f);
        Assert.InRange(Math.Abs(Dot(value.Row2, value.Row3)), 0f, 0.001f);
        // Authored attachment matrices can reflect a mesh while remaining orthonormal.
        float determinant = allowReflection ? Math.Abs(value.Determinant) : value.Determinant;
        Assert.True(determinant >= 0.999f && determinant <= 1.001f, $"{context}: determinant={value.Determinant}");
    }

    private static float Dot(Vector3f left, Vector3f right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
}
