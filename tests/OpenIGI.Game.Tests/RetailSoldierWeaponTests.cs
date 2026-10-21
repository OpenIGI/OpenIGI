using System.Globalization;
using System.Text;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

/// <summary>
/// What arms a soldier, measured over every retail level: the <c>Gun</c> child tasks its own subtree
/// declares, and whether each resolves to a weapon with a model.
/// </summary>
/// <remarks>
/// <para>
/// <b>The correction this pins.</b> A soldier's weapon is <em>not</em> resolved from its
/// <see cref="AiType"/>. It is an authored child task exactly like the player's loadout, and the census
/// below shows the two disagreeing routinely: level 1 gives soldiers whose type is
/// <c>AITYPE_PATROL_AK</c> an MP5SD as often as an AK. The weapon in the type's name belongs to the
/// <c>settings.qsc</c> key <c>(AIType, Difficulty)</c>, not to any model lookup.
/// </para>
/// </remarks>
public sealed class RetailSoldierWeaponTests
{
    /// <summary>
    /// Every weapon a soldier carries anywhere in the game resolves in the weapon table and names a
    /// model to draw it with.
    /// </summary>
    [RequiresGameDataFact]
    public void EverySoldierWeaponResolvesToAModel()
    {
        WeaponTypeRegistry roster = LoadRoster();

        var report = new StringBuilder();
        int soldiers = 0;
        int armed = 0;
        Dictionary<string, int> byWeapon = [];
        Dictionary<string, int> byModel = [];

        for (int id = 1; id <= 14; id++)
        {
            int levelSoldiers = 0;
            int levelArmed = 0;

            foreach (HumanSoldierTask soldier in Soldiers(id))
            {
                soldiers++;
                levelSoldiers++;

                IReadOnlyList<LoadoutWeapon> carried = PlayerLoadout.WeaponsOf(soldier);
                if (carried.Count == 0)
                {
                    continue;
                }

                // Slot 0 of the weapon list -- the FIRST gun child, which is what every consumer of
                // sub_4F2060(human + 1220, 0) takes.
                string weaponId = carried[0].WeaponId;
                byWeapon[weaponId] = byWeapon.GetValueOrDefault(weaponId) + 1;

                Assert.True(
                    roster.TryGetByScriptId(weaponId, out WeaponType weapon),
                    $"level {id}: '{soldier.Name}' carries {weaponId}, which is not in the weapon table.");
                Assert.False(
                    string.IsNullOrEmpty(weapon.GunModel),
                    $"level {id}: {weaponId} names no model to draw.");

                byModel[weapon.GunModel] = byModel.GetValueOrDefault(weapon.GunModel) + 1;
                armed++;
                levelArmed++;
            }

            report.AppendLine(
                CultureInfo.InvariantCulture,
                $"level {id,2}: {levelSoldiers,4} soldier(s), {levelArmed,4} armed");
        }

        report.AppendLine();
        foreach ((string weaponId, int count) in byWeapon.OrderByDescending(p => p.Value))
        {
            roster.TryGetByScriptId(weaponId, out WeaponType weapon);
            report.AppendLine(CultureInfo.InvariantCulture, $"{count,5}  {weaponId,-28} {weapon.GunModel}");
        }

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "openigi-soldier-weapon-census.txt"), report.ToString());

        // The measured corpus, over all fourteen levels, counting Level 10's RPG soldier and Level 14's
        // five female soldiers. Not every soldier is armed -- a civilian carries nothing and a few scripted
        // soldiers are props -- but 651 of 698 are, which is what makes this a mechanism rather than a
        // special case.
        Assert.Equal(698, soldiers);
        Assert.Equal(651, armed);

        // Fourteen distinct weapons and fourteen distinct models, one apiece: nothing shares a model and
        // nothing resolves to an empty one. The RPG soldier's launcher and a female soldier's Colt are two.
        Assert.Equal(14, byWeapon.Count);
        Assert.Equal(14, byModel.Count);
        Assert.Equal(1, byWeapon["WEAPON_ID_RPG18"]);

        // The AK is what most of the game's guards actually hold.
        Assert.Equal(445, byWeapon["WEAPON_ID_AK47"]);
        Assert.Equal("100_01_1", roster.TryGetByScriptId("WEAPON_ID_AK47", out WeaponType ak) ? ak.GunModel : null);

        // One soldier in the game carries a pair of binoculars as its first item, which is a reminder
        // that "slot 0 of the weapon list" really is a list order and not a weapon-ness test.
        Assert.Equal(1, byWeapon["WEAPON_ID_BINOCULARS"]);
    }

    /// <summary>
    /// The weapon in an <c>AITYPE_*</c> name does not decide what the soldier holds — measured, not
    /// argued.
    /// </summary>
    /// <remarks>
    /// Level 1 alone has soldiers declaring <c>AITYPE_PATROL_AK</c> and carrying <c>WEAPON_ID_MP5SD</c>.
    /// If the type picked the model there could be no such pair.
    /// </remarks>
    [RequiresGameDataFact]
    public void TheAiTypesWeaponNameDisagreesWithWhatTheSoldierCarries()
    {
        WeaponTypeRegistry roster = LoadRoster();
        int disagreements = 0;

        for (int id = 1; id <= 14; id++)
        {
            foreach (HumanSoldierTask soldier in Soldiers(id))
            {
                HumanAiTask? brain = soldier.Children.OfType<HumanAiTask>().FirstOrDefault();
                if (brain is null)
                {
                    continue;
                }

                IReadOnlyList<LoadoutWeapon> carried = PlayerLoadout.WeaponsOf(soldier);
                if (carried.Count == 0 || !roster.TryGetByScriptId(carried[0].WeaponId, out WeaponType weapon))
                {
                    continue;
                }

                string type = brain.AiTypeName;
                if (type.EndsWith("_AK", StringComparison.Ordinal)
                    && !weapon.ScriptId.Contains("AK", StringComparison.Ordinal))
                {
                    disagreements++;
                }
            }
        }

        Assert.True(
            disagreements > 0,
            "no AITYPE_*_AK soldier carries anything but an AK, so this test proves nothing");
    }

    private static WeaponTypeRegistry LoadRoster()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());

        WeaponTypeRegistry roster = new();
        roster.Register(loop.Natives);
        roster.Load(fileSystem, resources, loop.Natives);
        return roster;
    }

    private static List<HumanSoldierTask> Soldiers(int missionId)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);

        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        AiTaskTypes.RegisterAll(loop.Types);

        LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));

        MissionRegistry registry = new();
        registry.Register(loop.Natives);
        registry.Scan(fileSystem, resources, loop.Natives);

        LevelLoader levelLoader = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
        MissionLoader loader = new(registry, fileSystem, resources, levelLoader);
        loader.Load(missionId);

        List<HumanSoldierTask> found = [];
        Collect(levelRoot, found);
        return found;
    }

    private static void Collect(GameTask task, List<HumanSoldierTask> into)
    {
        if (task is HumanSoldierTask soldier)
        {
            into.Add(soldier);
        }

        foreach (GameTask child in task.Children)
        {
            Collect(child, into);
        }
    }
}
