using OpenIGI.Core.Numerics;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Ai.Runtime;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Soldiers;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>A soldier of mission one another game claims is given no body, and the level finds the other game's in its stead.</summary>
public sealed class RetailIgi2HostedHumanTests
{
    [RequiresTitleDataFact("igi2")]
    public void A_claimed_soldier_has_no_body_and_the_level_finds_and_strikes_its_stand_in()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        Igi2HumanSoldierTask[] tasks = [.. m.Tree.OfType<Igi2HumanSoldierTask>()];
        Igi2HumanSoldierTask claimed = tasks[0];
        StandIn standIn = new(claimed.Position, claimed.Team);
        claimed.Driver = GameTitle.Igi1;
        claimed.StandIn = standIn;

        (List<LoadedPack> packs, ResourceRegistry packResources) = Igi2LevelScene.Packs(m.Level);
        Igi2AnimationLibrary clips = Igi2AnimationLibrary.Load(packs, packResources);
        IgiRandom random = new();
        Igi2Soldiers soldiers = Igi2Gameplay.SpawnSoldiers(
            m.Tree[0], m.Services!, m.Collision!, clips, m.Level.ModelFor, m.Services!.Require<Igi2PlayerTuning>(), m.Sounds, random,
            AiDifficulty.Normal, natives: m.Run.Natives, functions: m.Run.Functions);

        Assert.Null(claimed.Body);
        Assert.Equal(tasks.Count(static t => t.Driver is null), soldiers.Bodies.Count);

        Assert.True(PlayerSpawn.TryFindGameplay(m.Level.Root, out PlayerSpawnPoint spawn));
        MefModel model = m.Level.ModelFor(spawn.Model)!;
        Igi2TitlePlayer player = Igi2TitlePlayer.Spawn(
            m.Collision!, Igi2Session.Start().Require<Igi2PlayerTuning>().General, spawn.Position, spawn.Gamma, AiDifficulty.Normal, null,
            model.BoneHierarchy);
        VirtualFileSystem files = new();
        files.RegisterDevice(new LocalFileDevice(GameDataSource.Igi2.RequireRoot()));
        files.SetAlias(VirtualFileSystem.MissionAlias, "LOCAL:missions/location1/level1/");
        Igi2AiWorld ai = Igi2LevelAi.Build(new Igi2LevelAiSetup(
            m.Tree[0], new ResourceRegistry(files), m.Run.Natives, m.Run.Functions, player.Human, [player.Human, .. soldiers.Bodies], random,
            () => m.Run.LevelFlow?.Tick ?? 0)
        {
            FireTaskTypes = m.Services!.Require<Igi2HumanWeapons>().FireTaskTypes,
            Config = m.Services!.TryGet(out Igi2HumanAiConfig? config) ? config : null,
            Difficulty = 1,
            World = m.Services!.Require<Igi2AiWorld>(),
        });

        Assert.Same(standIn, ai.TaskById(claimed.Id));
        claimed.WeaponHit(default(WeaponHit));
        claimed.ExplosionHit(default(ExplosionHit));
        Assert.Equal(1, standIn.WeaponHits);
        Assert.Equal(1, standIn.ExplosionHits);
    }

    [RequiresTitleDataFact("igi2")]
    public void A_blast_strikes_a_human_joined_to_the_level_as_it_strikes_the_worlds_own_targets()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        Vector3d at = m.Tree.OfType<Igi2HumanSoldierTask>().First().Position;
        StandIn own = new(at, 0);
        StandIn joined = new(at, 0);
        LevelJoiners joiners = new();
        joiners.JoinExplosions(joined);
        DynamicTasks dynamic = new();
        Igi2ExplosionWorld blasts = new(new IgiRandom(0x1234567u), m.Collision!, m.Materials, dynamic) { Joined = joiners };
        blasts.AddTarget(own);

        blasts.Explode(new Igi2GrenadeExplosion(at, new OpenIGI.Game.Physics.WorldIntersection(), 1f, 4096f, 8192f, null));
        for (int tick = 0; tick < 60 && own.ExplosionHits == 0; tick++)
        {
            dynamic.Run();
        }

        Assert.True(own.ExplosionHits > 0);
        Assert.Equal(own.ExplosionHits, joined.ExplosionHits);
    }

    private sealed class StandIn(Vector3d position, int team) : IAiHuman, IWeaponHitTarget, IExplosionHitTarget
    {
        public Vector3d Position => position;

        public int Team => team;

        public bool IsDead => false;

        public Vector3d OriginalViewPosition => position;

        public int WeaponHits { get; private set; }

        public int ExplosionHits { get; private set; }

        public void WeaponHit(in WeaponHit hit) => WeaponHits++;

        public void ExplosionHit(in ExplosionHit hit) => ExplosionHits++;
    }
}
