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
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Igi1;
using OpenIGI.TestSupport;
using System.Reflection;
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
    /// Issue #179: a wall short of the body stops the round, and the body behind it is untouched. Retail's
    /// trace hands the gunshot handler only its nearest contact (<c>0x47BA54</c>), so the wall takes it --
    /// but the near-miss handler reads the round's whole range before the march (<c>0x47B690</c> ->
    /// <c>0x47B290</c>), so the soldier still hears it go by.
    /// </summary>
    [RequiresGameDataFact]
    public void ABodyBehindTheWallIsUnhurtButHearsTheRound()
    {
        using PublisherFixture fixture = PublisherFixture.Create();

        Assert.False(fixture.Renderer.TryShoot(
            fixture.BulletOrigin,
            fixture.BulletEnd,
            damageFactor: 0.1f,
            WeaponCategory.Gun,
            stopDistance: 5 * Metre,
            alreadyHeard: false,
            out _));

        Assert.Equal(0, fixture.StruckBrain.Events.PendingCount(AiEvent.TakingDamage));
        Assert.Equal(1, fixture.StruckBrain.Events.PendingCount(AiEvent.GunshotMiss));
        Assert.All(fixture.Health.Values, health => Assert.Equal(0f, health.Damage));
    }

    /// <summary>The control for the wall: one beyond the body leaves the hit exactly as it was.</summary>
    [RequiresGameDataFact]
    public void AWallBeyondTheBodyLeavesTheHit()
    {
        using PublisherFixture fixture = PublisherFixture.Create();

        Assert.True(fixture.Renderer.TryShoot(
            fixture.BulletOrigin,
            fixture.BulletEnd,
            damageFactor: 0.1f,
            WeaponCategory.Gun,
            stopDistance: 15 * Metre,
            alreadyHeard: false,
            out _));

        Assert.Equal(1, fixture.StruckBrain.Events.PendingCount(AiEvent.TakingDamage));
        Assert.Contains(fixture.Health.Values, health => health.Damage > 0f);
    }

    /// <summary>
    /// <c>KnifeStab</c> (<c>0x46DFB0</c>) stabs a soldier with no fight on (<c>sub_4528F0</c>: no combat record
    /// at <c>HumanAI+15016</c>) at a damage factor of 1000 -- the silent kill -- whatever the blade's own.
    /// </summary>
    [RequiresGameDataFact]
    public void AKnifeKillsASoldierWithNoFightOnOutright()
    {
        using PublisherFixture fixture = PublisherFixture.Create();
        Assert.False(fixture.StruckBrain.HasCombatRecord);

        Assert.True(fixture.Renderer.TryShoot(
            fixture.BulletOrigin,
            fixture.BulletEnd,
            damageFactor: 0.1f,
            WeaponCategory.Knife,
            out _));

        Assert.Single(fixture.Health.Values, health => health.IsDead);
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
            Igi1HumanAi struckBrain,
            Igi1HumanAi recipientBrain,
            Vector3d struckPosition)
        {
            Renderer = renderer;
            StruckBrain = struckBrain;
            RecipientBrain = recipientBrain;
            BulletOrigin = new(struckPosition.X - (10 * Metre), struckPosition.Y, struckPosition.Z + Metre);
            BulletEnd = new(struckPosition.X + (10 * Metre), struckPosition.Y, struckPosition.Z + Metre);
        }

        public SoldierRenderer Renderer { get; }

        public Igi1HumanAi StruckBrain { get; }

        public Igi1HumanAi RecipientBrain { get; }

        public Vector3d BulletOrigin { get; }

        public Vector3d BulletEnd { get; }

        public IReadOnlyDictionary<SoldierView, HumanHealth> Health => Renderer.Simulation.Health;

        public static PublisherFixture Create(bool playerOnTheSoldiersSide = false, int? team = null)
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
            if (team is { } side)
            {
                struck.Team = side;
                recipient.Team = side;
            }

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

            Igi1HumanAi[] brains = [.. renderer.Simulation.BrainsForTesting.OrderBy(brain => brain.RuntimeId)];
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
