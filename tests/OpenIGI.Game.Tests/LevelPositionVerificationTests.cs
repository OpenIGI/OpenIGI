using OpenIGI.Core.Numerics;
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

namespace OpenIGI.Game.Tests;

internal readonly record struct AuthoredLevelObjectTransform(
    string Id,
    Vector3d PositionUnits,
    Matrix3f Orientation);

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
                AssertOrthonormal(placement.Orientation, $"mission {missionId} {placement}");

                if (placement.Owner is not DeclaredTask declared
                    || !TryReadAuthored(declared, out AuthoredLevelObjectTransform authored))
                {
                    continue;
                }

                Assert.True(
                    LevelPositionValidator.PosMatch(authored.PositionUnits, placement.Position),
                    $"mission {missionId} {placement.Instance.Name} position differs from authored {authored.PositionUnits}");
                Assert.True(
                    LevelPositionValidator.OriMatch(authored.Orientation, placement.Orientation),
                    $"mission {missionId} {placement.Instance.Name} orientation differs from authored data");
                checkedObjects++;
            }
        }

        Assert.True(checkedObjects > 100, $"expected authored transforms across the campaign, checked {checkedObjects}");
    }

    private static WorldScene Build(int missionId)
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
        LevelLoader levelLoader = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
        LoadedMission mission = new MissionLoader(registry, fileSystem, resources, levelLoader).Load(missionId);
        new ConditionalContainerGate(loop.Variables).Prepare(levelRoot);
        return WorldScene.Build(mission, levelRoot);
    }

    private static bool TryReadAuthored(DeclaredTask task, out AuthoredLevelObjectTransform transform)
    {
        if (!task.TryGetValue("Position", out object? positionValue) || positionValue is not Vector3d position)
        {
            transform = default;
            return false;
        }

        Matrix3f orientation = task.TryGetValue("Orientation", out object? orientationValue)
            ? orientationValue switch
            {
                Vector3f angles => WorldScene.ToOrientation(angles),
                Matrix3f matrix => matrix,
                _ => Matrix3f.Identity,
            }
            : task.TryGetValue("Gamma", out object? gammaValue) && gammaValue is float gamma
                ? Matrix3f.RotationZ(gamma)
                : Matrix3f.Identity;

        transform = new(task.Name, position, orientation);
        return true;
    }

    private static void AssertFinite(Vector3d value, string context)
    {
        Assert.True(double.IsFinite(value.X), $"{context}: X is not finite");
        Assert.True(double.IsFinite(value.Y), $"{context}: Y is not finite");
        Assert.True(double.IsFinite(value.Z), $"{context}: Z is not finite");
    }

    private static void AssertOrthonormal(Matrix3f value, string context)
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
        Assert.InRange(value.Determinant, 0.999f, 1.001f);
    }

    private static float Dot(Vector3f left, Vector3f right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
}
