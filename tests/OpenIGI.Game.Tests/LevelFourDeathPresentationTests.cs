using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Game.Weapons;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

/// <summary>Death-presentation contract for Mission 4's enemy models.</summary>
/// <remarks>
/// Level 4 fields two enemy models (<c>003_01_1</c> guards, hierarchy 1; <c>013_01_1</c> gunners,
/// hierarchy 1). A Dragunov kill must always resolve to a death clip the soldier's own animation set
/// carries: an unresolvable clip leaves the corpse frozen in its standing pose instead of falling.
/// </remarks>
public sealed class LevelFourDeathPresentationTests
{
    private readonly ITestOutputHelper _output;

    public LevelFourDeathPresentationTests(ITestOutputHelper output) => _output = output;

    [RequiresGameDataFact]
    public void DragunovKillsResolveDrawableDeathClipsForLevelFourModels()
    {
        BoneAnimationSet soldierSet = BoneAnimationSet.Read(
            File.ReadAllBytes(Path.Combine(GameData.RequireRoot(), "common", "anims", "001.iff")));

        WeaponType dragunov = LoadWeapons().Weapons.First(x => x.ScriptId == "WEAPON_ID_DRAGUNOV");
        _output.WriteLine($"dragunov factor={dragunov.DamageFactor} category={dragunov.Category}");
        SoldierDeathMode mode = SoldierDeath.ModeFor(dragunov.Category);

        // Every zone a Dragunov round can strike, both coin flips, no script override.
        for (int zone = 0; zone < HumanHitZoneTable.Soldier.Count; zone++)
        {
            for (int flip = 0; flip < 2; flip++)
            {
                int clip = SoldierDeath.Choose(
                    SoldierDeathClips.Soldier,
                    mode,
                    zone,
                    HumanHitZoneTable.Soldier,
                    shotFromTheFront: true,
                    coinFlip: flip);
                Assert.True(clip >= 0, $"zone {zone} flip {flip} must name a death clip");
                Assert.True(
                    soldierSet.TryGet(clip, out _),
                    $"zone {zone} flip {flip} names clip {clip}, missing from soldier set 001");
            }
        }

        // The Dragunov's roster damage must actually kill: two torso rounds, one head round.
        HumanHealth torso = new(HumanHitZoneTable.Soldier);
        torso.Apply(HumanDamage.DefaultZone, dragunov.DamageFactor);
        Assert.False(torso.IsDead);
        torso.Apply(HumanDamage.DefaultZone, dragunov.DamageFactor);
        Assert.True(torso.IsDead);

        HumanHealth head = new(HumanHitZoneTable.Soldier);
        head.Apply(SoldierDeath.HeadZone, dragunov.DamageFactor);
        Assert.True(head.IsDead);
    }

    [RequiresGameDataFact]
    public void LevelFourEnemiesShareOneAnimationSetWithDeathClips()
    {
        (_, LevelTask root, _, _) = LoadLevel();
        foreach (int id in new[] { 406, 413, 416, 417, 420, 429, 433 })
        {
            HumanSoldierTask soldier = Assert.IsType<HumanSoldierTask>(root.Tree.FromId(id));
            Assert.Equal(1, soldier.BoneHierarchy);
        }

        BoneAnimationSet soldierSet = BoneAnimationSet.Read(
            File.ReadAllBytes(Path.Combine(GameData.RequireRoot(), "common", "anims", "001.iff")));
        foreach (int clip in SoldierDeath.CrumpleAnimations)
        {
            Assert.True(soldierSet.TryGet(clip, out _), $"crumple clip {clip} must exist in set 001");
        }
    }

    private static (GameLoop Loop, LevelTask Root, object? Objectives, object? Runtime)
        LoadLevel()
    {
        VirtualFileSystem fs = new();
        fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs);
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        AiTaskTypes.RegisterAll(loop.Types);
        loop.Types.Register<CutSceneTask>(CutSceneTask.TypeName);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fs, resources, loop.Natives);
        _ = new MissionLoader(missions, fs, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(4);
        return (loop, root, null, null);
    }

    private static WeaponTypeRegistry LoadWeapons()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        WeaponTypeRegistry weapons = new();
        weapons.Register(loop.Natives);
        weapons.Load(fileSystem, resources, loop.Natives);
        return weapons;
    }
}
