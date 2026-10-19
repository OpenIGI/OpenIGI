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
using OpenIGI.Presentation.Igi1;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Desktop.Tests;

public sealed class AiHearingTests
{
    private const double Metre = 4096.0;

    /// <summary>
    /// A hit is the struck soldier's own news: <c>0x4553E0</c> posts TAKINGDAMAGE to its own AI only, and the
    /// squad hears of a fight only through the engaged combat tick's ping (<c>sub_48AB10</c>). A round that
    /// lands wakes nobody beside the soldier it hit.
    /// </summary>
    [RequiresGameDataFact]
    public void ABulletHitTellsOnlyTheSoldierItHit()
    {
        using PublisherFixture fixture = PublisherFixture.Create();

        Assert.True(fixture.Renderer.TryShoot(
            fixture.BulletOrigin,
            fixture.BulletEnd,
            damageFactor: 0.1f,
            WeaponCategory.Gun,
            out _));

        Assert.Equal(1, fixture.StruckBrain.Events.PendingCount(AiEvent.TakingDamage));
        Assert.Equal(0, fixture.RecipientBrain.Events.PendingCount(AiEvent.Alert));
    }

    /// <summary>
    /// An explosion's damage is news to nobody's brain: <c>HumanSoldier</c>'s ExplosionHit (<c>0x45A3D0</c>) posts no AI
    /// event -- only the WeaponHit and close-combat handlers call <c>0x450D50</c> -- and a soldier hears a blast through the
    /// explosion's own noise event instead.
    /// </summary>
    [RequiresGameDataFact]
    public void AnExplosionHitTellsNoBrain()
    {
        using PublisherFixture fixture = PublisherFixture.Create();

        fixture.Renderer.Simulation.ApplyExplosion(
            fixture.StruckBrain.Position,
            radiusUnits: 5 * (float)Metre,
            falloffUnits: 0f,
            damageFactor: 0.1f);

        Assert.Equal(0, fixture.StruckBrain.Events.PendingCount(AiEvent.TakingDamage));
        Assert.Equal(0, fixture.RecipientBrain.Events.PendingCount(AiEvent.Alert));
    }

    /// <summary>
    /// The player's round passes through a soldier on the player's side: <c>Human</c>'s WeaponCollisionEvaluate
    /// (<c>0x45E3E0</c>) lets a body stop a round only when the shooter is of another team, and nobody hears of it.
    /// </summary>
    [RequiresGameDataFact]
    public void APlayersRoundPassesThroughSoldiersOnThePlayersSide()
    {
        using PublisherFixture fixture = PublisherFixture.Create(playerOnTheSoldiersSide: true);

        Assert.False(fixture.Renderer.TryShoot(
            fixture.BulletOrigin,
            fixture.BulletEnd,
            damageFactor: 0.1f,
            WeaponCategory.Gun,
            out _));

        Assert.Equal(0, fixture.StruckBrain.Events.PendingCount(AiEvent.TakingDamage));
    }

    /// <summary>
    /// An invulnerable soldier's brain still hears the round: <c>0x45D3C0</c> skips the damage, then posts TAKINGDAMAGE
    /// (<c>0x45D577</c>) whatever the round did.
    /// </summary>
    [RequiresGameDataFact]
    public void AnInvulnerableSoldierStillHearsTheRound()
    {
        using PublisherFixture fixture = PublisherFixture.Create();
        fixture.StruckBrain.IsInvulnerable = true;

        Assert.True(fixture.Renderer.TryShoot(
            fixture.BulletOrigin,
            fixture.BulletEnd,
            damageFactor: 0.1f,
            WeaponCategory.Gun,
            out _));

        Assert.Equal(1, fixture.StruckBrain.Events.PendingCount(AiEvent.TakingDamage));
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

        public static PublisherFixture Create(bool playerOnTheSoldiersSide = false)
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

            if (playerOnTheSoldiersSide)
            {
                HumanPlayerTask player = (HumanPlayerTask)testTree.New(testTree.Root, loop.Types.Get(PlayerTaskTypes.HumanPlayer));
                player.Team = template.Team;
            }

            SoldierRenderer renderer = new(new RecordingGraphicsDevice(), resources);
            Assert.Equal(2, renderer.Load(mission, testTree.Root));
            Assert.Equal(2, renderer.Simulation.Animate(
                new Dictionary<int, GraphFile>(),
                levelRoot: playerOnTheSoldiersSide ? testTree.Root : null));

            AiSoldier[] brains = [.. renderer.Simulation.BrainsForTesting.OrderBy(brain => brain.RuntimeId)];
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
