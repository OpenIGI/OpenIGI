using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Qvm;
using OpenIGI.TestSupport;
using Xunit;
using static OpenIGI.Scripting.Qvm.QvmExpression;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// The level's sound definitions, as <c>Game_CreateHandler</c> (<c>igi2 0x4070E0</c>) reads them and <c>SoundSys_NewSoundFn</c>
/// (<c>0x42F6F0</c>) finds them, and the banks they play from.
/// </summary>
public sealed class RetailIgi2SoundDefinitionsTests
{
    private static readonly Lazy<Mission1Sounds> Mission1 = new(static () =>
    {
        VirtualFileSystem files = new();
        files.RegisterDevice(new LocalFileDevice(GameDataSource.Igi2.RequireRoot()));
        ResourceRegistry resources = new(files);
        GameLoop loop = new(new ManualTimeSource());
        Igi2MissionCatalogue catalogue = new(NullLogger.Instance);
        catalogue.Register(loop.Natives);
        catalogue.Scan(files, resources, loop.Natives);
        MissionDescriptor mission = catalogue.Missions.Single(static m => m.Id == 11);
        files.SetAlias(VirtualFileSystem.MissionAlias, mission.MissionAliasTarget);

        Igi2SoundDefinitions definitions = new();
        definitions.Load(resources, Igi2SoundDefinitions.Directories(mission));
        SoundBank bank = new(files);
        foreach (string directory in Igi2SoundDefinitions.BankDirectories(mission))
        {
            bank.MountDirectory(directory);
        }

        return new Mission1Sounds(mission, definitions, bank);
    });

    /// <summary>
    /// Mission 1 reads the game's definitions, location 1's and its own; its banks are each of those and its English
    /// folder.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Mission1_reads_the_game_the_location_and_the_mission()
    {
        MissionDescriptor mission = Mission1.Value.Mission;
        Assert.Equal(["LOCAL:common/sounds", "LOCAL:missions/location1/common/sounds", "MISSION:sounds"], Igi2SoundDefinitions.Directories(mission));
        Assert.Equal(
            [
                "LOCAL:common/sounds", "LOCAL:common/sounds/ENGLISH",
                "LOCAL:missions/location1/common/sounds", "LOCAL:missions/location1/common/sounds/ENGLISH",
                "MISSION:sounds", "MISSION:sounds/ENGLISH",
            ],
            Igi2SoundDefinitions.BankDirectories(mission));
    }

    /// <summary>
    /// The three scripts define 964 names, every one in one of 26 groups, none so long a 32-byte field would cut it; a
    /// name defined twice answers with its first definition.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Mission1_defines_964_names_in_26_groups()
    {
        Igi2SoundDefinitions definitions = Mission1.Value.Definitions;
        Assert.Equal(964, definitions.Count);
        Assert.Equal(26, definitions.Roots.Count);
        Assert.All(definitions.Roots, static root => Assert.Equal(SoundDefinitionKind.Group, root.Kind));
        Assert.Equal("ALARMSYSTEM", definitions.Roots[0].Name);
        Assert.Contains(definitions.Roots, static root => root.Name == "AI GUARDS");

        List<SoundDefinition> all = [.. All(definitions.Roots)];
        Assert.Equal(967, all.Count);
        Assert.All(all, static d => Assert.True(d.Name.Length < 32, d.Name));

        Assert.True(definitions.TryResolve("BULLETS", out SoundDefinition? group));
        Assert.Equal(SoundDefinitionKind.Group, group!.Kind);
        Assert.True(definitions.TryResolve("weaponpickup_1", out SoundDefinition? pickup));
        Assert.Same(all.First(static d => d.Name == "weaponpickup_1"), pickup);
    }

    /// <summary>
    /// Sounds the player makes are there with the distances, volume and loop the scripts give them; a name is matched
    /// with its case, so the blast's pains, which mission 1 defines only in capitals and only up to the second, make no
    /// sound.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Mission1_defines_the_players_sounds()
    {
        Igi2SoundDefinitions definitions = Mission1.Value.Definitions;

        Assert.True(definitions.TryResolve("alarm_10", out SoundDefinition? alarm));
        Assert.Equal(("ALARM_2", 1f, 10f, 1000f, true), (alarm!.BankKey, alarm.Volume, alarm.MinimumDistanceMetres, alarm.MaximumDistanceMetres, alarm.Loop));
        Assert.True(definitions.TryResolve("grenade_bnc_1", out SoundDefinition? bounce));
        Assert.Equal(("GRENADE_BNC_1", 5f, 200f, false), (bounce!.BankKey, bounce.MinimumDistanceMetres, bounce.MaximumDistanceMetres, bounce.Loop));
        Assert.True(definitions.TryResolve("crawl_loop", out SoundDefinition? crawl));
        Assert.True(crawl!.Loop);
        Assert.True(definitions.TryResolve("jump_1", out _));
        Assert.True(definitions.TryResolve("changeweapon_01", out _));

        Assert.True(definitions.TryResolve("GV_69_01", out _));
        Assert.All(Player.Igi2HumanPlayer.ExplosionHitSounds, name => Assert.False(definitions.TryResolve(name, out _), name));
        Assert.False(definitions.TryResolve(string.Empty, out _));
    }

    /// <summary>
    /// Every bullet's strike on a material makes a sound but the placeholder row's, so the gunshot's once-a-tick
    /// ricochet draw is taken at the first face it strikes.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_bullet_strikes_every_material_aloud()
    {
        Igi2SoundDefinitions definitions = Mission1.Value.Definitions;
        Igi2GameMaterials materials = Igi2Session.Start().Require<Igi2GameMaterials>();
        List<string> silent = [];
        int aloud = 0;
        for (int row = 0; row < Igi2GameMaterials.Rows; row++)
        {
            foreach (string hit in (materials[row]?.BulletHitSounds ?? []).Where(static h => h.Length > 0))
            {
                if (definitions.TryResolve(hit, out _))
                {
                    aloud++;
                }
                else
                {
                    silent.Add($"{row}:{hit}");
                }
            }
        }

        Assert.Equal(153, aloud);
        Assert.Equal(["0:Hit0"], silent);
    }

    /// <summary>Every definition plays a banked sample, but four whose samples retail never shipped.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Mission1_banks_every_sample_but_four()
    {
        Mission1Sounds m = Mission1.Value;
        string[] missing =
        [
            .. All(m.Definitions.Roots)
                .Where(static d => d.Kind != SoundDefinitionKind.Group)
                .Where(d => !m.Bank.TryGet(d.BankKey, out _))
                .Select(static d => d.BankKey),
        ];

        Assert.Equal(["ALARM_1", "PLAYER_DEATH_4", "PLAYER_XPLHIT_2", "PLAYER_XPLHIT_3"], missing);
    }

    /// <summary>
    /// The natives as the editor parses them: a group answers to its own name and holds what its arguments define; the
    /// first script's definition of a name is the one found; a sound with no nearest distance hears in full to a fiftieth
    /// of its farthest, chopped as the FPU chops it; a directory without a script defines nothing.
    /// </summary>
    [Fact]
    public void The_natives_define_as_the_editor_parses()
    {
        MemoryFileDevice device = new();
        device.Add("a/sounds.qvm", BuildProgram(
            Call(Igi2SoundDefinitions.DefineGroupNative, Text("G"), Sound("near", "NEAR", 0f, 50f, loop: 1), Sound("twice", "FIRST", 2f, 8f, loop: 0x100))));
        device.Add("b/sounds.qvm", BuildProgram(
            Sound("twice", "SECOND", 2f, 8f, loop: 0),
            Call(
                Igi2SoundDefinitions.DefineTriggerOnceNative,
                [Text("trigger"), Text("TRIG"), Real(0f), Real(0f), Real(0f), Real(1f), Real(0f), Real(30f), Real(2.5f), Int(3), Int(-1), Int(-1), Int(-1), Int(1), Int(1)])));
        VirtualFileSystem files = new();
        files.RegisterDevice(device);

        Igi2SoundDefinitions definitions = new();
        Assert.Equal(4, definitions.Load(new ResourceRegistry(files), ["LOCAL:a", "LOCAL:b", "LOCAL:c"]));

        Assert.Equal(["G", "twice", "trigger"], definitions.Roots.Select(static d => d.Name));
        Assert.True(definitions.TryResolve("G", out SoundDefinition? group));
        Assert.Equal(["near", "twice"], group!.Children.Select(static d => d.Name));

        Assert.True(definitions.TryResolve("near", out SoundDefinition? near));
        Assert.Equal(BitConverter.Int32BitsToSingle(0x3F7FFFFF), near!.MinimumDistanceMetres);
        Assert.Equal(50f, near.MaximumDistanceMetres);
        Assert.True(near.Loop);

        Assert.True(definitions.TryResolve("twice", out SoundDefinition? twice));
        Assert.Equal("FIRST", twice!.BankKey);
        Assert.False(twice.Loop);
        Assert.Equal(2f, twice.MinimumDistanceMetres);

        Assert.True(definitions.TryResolve("trigger", out SoundDefinition? trigger));
        Assert.Equal((SoundDefinitionKind.TriggerOnce, 0f, 2.5f, 3, true, true), (trigger!.Kind, trigger.MinimumDistanceMetres, trigger.DurationSeconds, trigger.TriggerParameterIndex, trigger.Loop, trigger.Invert));
    }

    private static QvmExpression Sound(string name, string bank, float nearest, float farthest, int loop) => Call(
        Igi2SoundDefinitions.DefineSoundNative,
        [Text(name), Text(bank), Real(0f), Real(0f), Real(0f), Real(1f), Real(nearest), Real(farthest), Int(-1), Int(-1), Int(-1), Real(-1f), Int(loop)]);

    private static IEnumerable<SoundDefinition> All(IEnumerable<SoundDefinition> level)
    {
        foreach (SoundDefinition definition in level)
        {
            yield return definition;
            foreach (SoundDefinition child in All(definition.Children))
            {
                yield return child;
            }
        }
    }

    private sealed record Mission1Sounds(MissionDescriptor Mission, Igi2SoundDefinitions Definitions, SoundBank Bank);
}
