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

/// <summary>Protects the real Level 4 village-gunner sight line reported in issue 203.</summary>
public sealed class LevelFourWindowDetectionTests
{
    private const double Metre = WorldScene.UnitsPerMetre;
    private static readonly Vector3d GunnerFeet = new(112864784.0, -54876064.0, 175482656.0);
    private static readonly Vector3d OutsideWindow = new(
        GunnerFeet.X,
        GunnerFeet.Y + (8.0 * Metre),
        175462208.0 + AiSoldier.DetectionRaiseStandingInUnits);

    /// <summary>
    /// Soldier 432 looks from the upper floor of Building 459 toward the approaching player. The authored
    /// pane is Glass (material 14), and retail visibility continues through it on every difficulty.
    /// Difficulty selects the combat row after detection; it does not turn glass into opaque cover.
    /// </summary>
    [RequiresGameDataTheory]
    [InlineData(AiDifficulty.Easy, "GD_1")]
    [InlineData(AiDifficulty.Normal, "GD_2")]
    [InlineData(AiDifficulty.Hard, "GD_3")]
    public void LevelFourVillageGunnerDetectsPlayerOutsideGlassWindow(
        AiDifficulty difficulty,
        string difficultyName)
    {
        using Fixture fixture = Fixture.Load(difficulty);
        Vector3d liveEye = fixture.Gunner.EyePosition();

        Assert.True(
            fixture.Solids.TryTrace(
                new WorldTraceRequest(liveEye, OutsideWindow, WorldTraceMask.World),
                out WorldTraceHit pane),
            "the retail Level 4 window ray did not touch its authored pane");
        Assert.True(
            pane.Material == 14 && pane.Label.Contains("GUNNER_BUILDING_WINDOW", StringComparison.Ordinal),
            $"expected Level 4 glass pane, got material {pane.Material} '{pane.Label}'");

        (double _, bool inCone, bool clear, _, _) = fixture.Gunner.ProbeDetection(OutsideWindow);
        Assert.True(inCone, "the player outside the window is not inside the gunner's authored view cone");
        Assert.True(clear, $"transparent Level 4 pane '{pane.Label}' blocked AI sight");
        Assert.Equal(difficultyName, fixture.Gunner.CombatParameters?.Source.DifficultyName);

        bool detected = false;
        for (int tick = 1; tick <= 90; tick++)
        {
            fixture.Gunner.Tick(tick, OutsideWindow, rootMotionThisTick: 0.0);
            detected |= fixture.Gunner.SeesEnemy;
        }

        Assert.True(detected, $"Level 4 gunner never detected the visible player on {difficulty}");
        Assert.Equal(60.0 * Metre, fixture.Gunner.Vision.Primary.Length);
    }

    private sealed class Fixture(
        SoldierRenderer renderer,
        ObjectCollisionIndex solids,
        AiSoldier gunner) : IDisposable
    {
        public ObjectCollisionIndex Solids { get; } = solids;

        public AiSoldier Gunner { get; } = gunner;

        public static Fixture Load(AiDifficulty difficulty)
        {
            VirtualFileSystem fileSystem = new();
            fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
            ResourceRegistry resources = new(fileSystem);
            GameLoop loop = new(new ManualTimeSource());
            PlayerTaskTypes.RegisterAll(loop.Types);
            AiTaskTypes.RegisterAll(loop.Types);

            MissionRegistry missions = new();
            missions.Register(loop.Natives);
            missions.Scan(fileSystem, resources, loop.Natives);

            HumanAiConfigRegistry config = new();
            config.Load(resources, loop.Natives, loop.Tasks);

            MagicObjectRegistry magic = new();
            magic.Register(loop.Natives);
            magic.Load(resources, loop.Natives);

            LevelTask root = (LevelTask)loop.Tasks.New(
                loop.Tasks.Root,
                loop.Types.Get(EngineTaskTypes.Level));
            LoadedMission mission = new MissionLoader(
                missions,
                fileSystem,
                resources,
                new LevelLoader(root, loop.TaskCommands, loop.Natives, resources),
                terrainLoader: new TerrainLoader(resources, loop.Natives))
                .Load(4);
            WorldScene scene = WorldScene.Build(mission, root, magic);
            ObjectCollisionIndex solids = ObjectCollisionIndex.Build(scene.Placements);
            WorldTraceCast world = mission.Terrain is { } terrain
                ? WorldTrace.Nearest(solids.Tracer(), TerrainRayTrace.Caster(terrain))
                : solids.Tracer();

            SoldierRenderer renderer = new(new RecordingGraphicsDevice(), resources, magic: magic);
            Assert.True(renderer.Load(mission, root) > 0);
            Assert.True(renderer.Animate(
                new Dictionary<int, GraphFile>(),
                sight: SoldierRenderer.AiSightCast(world),
                scripts: resources,
                natives: loop.Natives,
                levelRoot: root,
                config: config,
                difficulty: difficulty,
                variables: loop.Variables) > 0);

            AiSoldier gunner = Assert.Single(renderer.BrainsForTesting, brain =>
                Math.Abs(brain.Position.X - GunnerFeet.X) < Metre
                && Math.Abs(brain.Position.Y - GunnerFeet.Y) < Metre);
            return new Fixture(renderer, solids, gunner);
        }

        public void Dispose() => renderer.Dispose();
    }
}
