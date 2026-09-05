using OpenIGI.Core.Numerics;
using OpenIGI.Desktop;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests;

public sealed class AiHearingTests
{
    private const double Metre = 4096.0;

    [RequiresGameDataFact]
    public void BulletDamagePublishesOnlyAcceptedDamageToANearbySquadmate()
    {
        using PublisherFixture fixture = PublisherFixture.Create();
        AiSoldier struck = fixture.StruckBrain;
        AiSoldier recipient = fixture.RecipientBrain;

        Assert.True(fixture.Renderer.TryShoot(
            fixture.BulletOrigin,
            fixture.BulletEnd,
            damageFactor: 0.1f,
            WeaponCategory.Gun,
            out _));
        Assert.Equal(1, struck.Events.PendingCount(AiEvent.TakingDamage));
        Assert.Equal(1, recipient.Events.PendingCount(AiEvent.Alert));
        Assert.False(recipient.Alerted);
        recipient.Tick(0, default, rootMotionThisTick: 0.0);
        Assert.True(recipient.Alerted);

        using PublisherFixture rejected = PublisherFixture.Create();
        rejected.StruckBrain.Events.SetEnabled(AiEvent.TakingDamage, false);

        Assert.True(rejected.Renderer.TryShoot(
            rejected.BulletOrigin,
            rejected.BulletEnd,
            damageFactor: 0.1f,
            WeaponCategory.Gun,
            out _));
        Assert.Equal(0, rejected.StruckBrain.Events.PendingCount(AiEvent.TakingDamage));
        Assert.Equal(0, rejected.RecipientBrain.Events.PendingCount(AiEvent.Alert));
        rejected.RecipientBrain.Tick(0, default, rootMotionThisTick: 0.0);
        Assert.False(rejected.RecipientBrain.Alerted);
    }

    [RequiresGameDataFact]
    public void ExplosionDamagePublishesOnlyAcceptedDamageToANearbySquadmate()
    {
        using PublisherFixture fixture = PublisherFixture.Create();
        AiSoldier struck = fixture.StruckBrain;
        AiSoldier recipient = fixture.RecipientBrain;

        fixture.Renderer.ApplyExplosion(
            fixture.StruckBrain.Position,
            radiusUnits: 5 * (float)Metre,
            falloffUnits: 0f,
            damageFactor: 0.1f);
        Assert.Equal(1, struck.Events.PendingCount(AiEvent.TakingDamage));
        Assert.Equal(1, recipient.Events.PendingCount(AiEvent.Alert));
        Assert.False(recipient.Alerted);
        recipient.Tick(0, default, rootMotionThisTick: 0.0);
        Assert.True(recipient.Alerted);

        using PublisherFixture rejected = PublisherFixture.Create();
        rejected.StruckBrain.Events.SetEnabled(AiEvent.TakingDamage, false);

        rejected.Renderer.ApplyExplosion(
            rejected.StruckBrain.Position,
            radiusUnits: 5 * (float)Metre,
            falloffUnits: 0f,
            damageFactor: 0.1f);
        Assert.Equal(0, rejected.StruckBrain.Events.PendingCount(AiEvent.TakingDamage));
        Assert.Equal(0, rejected.RecipientBrain.Events.PendingCount(AiEvent.Alert));
        rejected.RecipientBrain.Tick(0, default, rootMotionThisTick: 0.0);
        Assert.False(rejected.RecipientBrain.Alerted);
    }

    private sealed class PublisherFixture : IDisposable
    {
        private PublisherFixture(
            SoldierRenderer renderer,
            AiSoldier struckBrain,
            AiSoldier recipientBrain,
            Vector3d struckPosition)
        {
            Renderer = renderer;
            StruckBrain = struckBrain;
            RecipientBrain = recipientBrain;
            BulletOrigin = new(struckPosition.X - (10 * Metre), struckPosition.Y, struckPosition.Z + Metre);
            BulletEnd = new(struckPosition.X + (10 * Metre), struckPosition.Y, struckPosition.Z + Metre);
        }

        public SoldierRenderer Renderer { get; }

        public AiSoldier StruckBrain { get; }

        public AiSoldier RecipientBrain { get; }

        public Vector3d BulletOrigin { get; }

        public Vector3d BulletEnd { get; }

        public static PublisherFixture Create()
        {
            VirtualFileSystem fileSystem = new();
            fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
            ResourceRegistry resources = new(fileSystem);
            GameLoop loop = new(new ManualTimeSource());
            PlayerTaskTypes.RegisterAll(loop.Types);

            MissionRegistry registry = new();
            registry.Register(loop.Natives);
            registry.Scan(fileSystem, resources, loop.Natives);

            LevelTask sourceRoot = (LevelTask)loop.Tasks.New(
                loop.Tasks.Root,
                loop.Types.Get(EngineTaskTypes.Level));
            LoadedMission mission = new MissionLoader(
                registry,
                fileSystem,
                resources,
                new LevelLoader(sourceRoot, loop.TaskCommands, loop.Natives, resources))
                .Load(1);
            HumanSoldierTask template = FindAll<HumanSoldierTask>(sourceRoot)
                .First(soldier => mission.TryGetAnimationSet(soldier.BoneHierarchy, out _)
                    && mission.TryGetModel(soldier.Model, out MefModel? model)
                    && model is not null
                    && MefSkinner.IsSkinned(model));

            TaskTree testTree = new(loop.Types);
            HumanSoldierTask struck = NewSoldier(
                testTree,
                loop.Types.Get(PlayerTaskTypes.HumanSoldier),
                template,
                default);
            HumanSoldierTask recipient = NewSoldier(
                testTree,
                loop.Types.Get(PlayerTaskTypes.HumanSoldier),
                template,
                new Vector3d(6 * Metre, 0, 0));

            SoldierRenderer renderer = new(new RecordingGraphicsDevice(), resources);
            Assert.Equal(2, renderer.Load(mission, testTree.Root));
            Assert.Equal(2, renderer.Animate(new Dictionary<int, GraphFile>()));

            AiSoldier[] brains = [.. renderer.BrainsForTesting.OrderBy(brain => brain.RuntimeId)];
            return new PublisherFixture(renderer, brains[0], brains[1], struck.Position);
        }

        public void Dispose() => Renderer.Dispose();

        private static HumanSoldierTask NewSoldier(
            TaskTree tree,
            TaskType type,
            HumanSoldierTask template,
            Vector3d position)
        {
            HumanSoldierTask soldier = (HumanSoldierTask)tree.New(tree.Root, type);
            soldier.Position = position;
            soldier.Gamma = template.Gamma;
            soldier.Model = template.Model;
            soldier.Team = template.Team;
            soldier.BoneHierarchy = template.BoneHierarchy;
            soldier.StandAnimation = template.StandAnimation;
            return soldier;
        }

        private static IEnumerable<T> FindAll<T>(GameTask task)
            where T : GameTask
        {
            foreach (GameTask child in task.Children)
            {
                if (child is T match)
                {
                    yield return match;
                }

                foreach (T deeper in FindAll<T>(child))
                {
                    yield return deeper;
                }
            }
        }
    }
}
