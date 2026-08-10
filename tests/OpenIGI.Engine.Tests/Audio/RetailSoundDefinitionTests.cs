using OpenIGI.Audio;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Engine.Tests.Audio;

/// <summary>
/// Reads the retail sound scripts, which is the only way to check the argument map recovered from
/// <c>DefineSound</c>'s stack frame against what the shipped data actually says.
/// </summary>
/// <remarks>Skipped when <c>IGI_GAME_PATH</c> is unset.</remarks>
public sealed class RetailSoundDefinitionTests
{
    [RequiresGameDataFact]
    public void EveryArgumentLandsWhereTheStackFrameSaysItDoes()
    {
        SoundDefinitionRegistry registry = Load();

        // DefineSound("ak47_loop", "AK47_LOOP", 0, 0, 0, 1, 5.0, 65.0, -1, -1, -1, -1, TRUE)
        Assert.True(registry.TryResolve("ak47_loop", out SoundDefinition? rifle));
        Assert.NotNull(rifle);
        Assert.Equal("AK47_LOOP", rifle.BankKey);
        Assert.Equal(SoundDefinitionKind.Sound, rifle.Kind);
        Assert.Equal(1f, rifle.Volume);
        Assert.Equal(5f, rifle.MinimumDistanceMetres);
        Assert.Equal(65f, rifle.MaximumDistanceMetres);
        Assert.Equal(-1, rifle.VolumeParameterIndex);
        Assert.Equal(-1, rifle.PitchParameterIndex);
        Assert.Equal(-1, rifle.BankKeyParameterIndex);

        // The whole point of the loop flag: an automatic weapon's fire is one looping sample.
        Assert.True(rifle.Loop);
    }

    [RequiresGameDataFact]
    public void TheDefinitionNameAndTheBankKeyAreDifferentNames()
    {
        // This is the fact the whole layer exists for, and the one that dissolves the "guards die
        // silently" reading: 0x4638A0 formats a plain %d, so it builds the single-digit name -- and
        // the single-digit name is a DEFINITION, which maps to the padded bank key.
        SoundDefinitionRegistry registry = Load();

        for (int variant = 1; variant <= 4; variant++)
        {
            Assert.True(registry.TryResolve($"ai_death_{variant}", out SoundDefinition? death));
            Assert.NotNull(death);
            Assert.Equal($"AI_DEATH_0{variant}", death.BankKey);
        }

        // The player's set needs no such mapping -- its files are unpadded -- which is exactly why the
        // padding looked like a bug rather than a convention.
        Assert.True(registry.TryResolve("player_death_1", out SoundDefinition? player));
        Assert.NotNull(player);
        Assert.Equal("PLAYER_DEATH_1", player.BankKey);
    }

    [RequiresGameDataFact]
    public void TheVariantHelperDrawsOneNameThatWasNeverDefined()
    {
        // 0x4B47C0 is four instructions ending in an unsigned `div`, so the draw is [0, count-1] while
        // the definitions start at 1. Pinning it here because it is the difference between reproducing
        // the original's silence and quietly "fixing" it.
        SoundDefinitionRegistry registry = Load();

        Assert.False(registry.TryResolve("ai_death_0", out _));
        Assert.False(registry.TryResolve("player_death_0", out _));

        // And the top variant of each set can never be drawn, so it ships unused.
        Assert.True(registry.TryResolve("ai_death_4", out _));
        Assert.True(registry.TryResolve("player_death_4", out _));
    }

    [RequiresGameDataFact]
    public void GroupsNestTheirDefinitionsRatherThanFlatteningThem()
    {
        SoundDefinitionRegistry registry = Load();

        SoundDefinition guns = Assert.Single(registry.Roots, root => root.Name == "GUN SHOTS");
        Assert.Equal(SoundDefinitionKind.Group, guns.Kind);
        Assert.Equal(27, guns.Children.Count);
        Assert.Contains(guns.Children, child => child.Name == "ak47_loop");

        // A group is found by name too, but cannot be played: 0x4E7200 refuses everything but a
        // plain sound.
        Assert.False(guns.IsPlayable);
    }

    [RequiresGameDataFact]
    public void TheFlatIndexAnswersExactlyAsAFirstMatchTreeWalkWould()
    {
        // Names are NOT unique -- the corpus has five collisions, and they are deliberate: a group
        // wrapping a same-named graph (`helicopter_1`, `machinery_01`, `weaponpickup_1`), a group name
        // a mission redeclares (`CUTSCENE SFX`), and three anonymous definitions sharing the empty
        // name. So traversal order IS observable, and the flat index is only legitimate if it returns
        // what the original's stop-at-first-match walk would.
        SoundDefinitionRegistry registry = Load();

        List<SoundDefinition> walked = [];
        Walk(registry.Roots, walked);
        Assert.Equal(431, walked.Count);

        foreach (SoundDefinition first in walked
            .GroupBy(definition => definition.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .Where(definition => definition.Name.Length > 0))
        {
            Assert.True(registry.TryResolve(first.Name, out SoundDefinition? resolved));
            Assert.Same(first, resolved);
        }
    }

    [RequiresGameDataFact]
    public void AGroupWrappingASameNamedGraphIsFoundBeforeTheGraph()
    {
        // The original creates a group's task before forcing the arguments that build its children, so
        // the group meets the search first. That matters: the wrapper is the spawnable unit, and a
        // group is deliberately not playable down the direct path.
        SoundDefinitionRegistry registry = Load();

        Assert.True(registry.TryResolve("helicopter_1", out SoundDefinition? helicopter));
        Assert.NotNull(helicopter);
        Assert.Equal(SoundDefinitionKind.Group, helicopter.Kind);
        Assert.Equal(SoundDefinitionKind.Graph, Assert.Single(helicopter.Children).Kind);
    }

    [RequiresGameDataFact]
    public void AnEmptyNameResolvesToNothingAtAll()
    {
        // Three definitions are declared with no name. The original never finds them either --
        // 0x4E6B00 returns before it searches when the wanted name is empty.
        SoundDefinitionRegistry registry = Load();
        Assert.False(registry.TryResolve(string.Empty, out _));
    }

    [RequiresGameDataFact]
    public void AMissionAddsItsOwnDefinitionsOnTopOfTheSharedOnes()
    {
        SoundDefinitionRegistry registry = Load();

        // From missions/location0/level1/sounds/sounds.qvm, not the common script.
        Assert.True(registry.TryResolve("m1_ambience", out SoundDefinition? ambience));
        Assert.NotNull(ambience);
        Assert.Equal("M1_AMBIENCE", ambience.BankKey);
    }

    [RequiresGameDataFact]
    public void MissionOneCutsceneDialogueResolvesToItsGlobalVoiceTrack()
    {
        SoundDefinitionRegistry registry = Load();

        Assert.True(registry.TryResolve("_cut01_01", out SoundDefinition? dialogue));
        Assert.NotNull(dialogue);
        Assert.Equal(SoundDefinitionKind.Sound, dialogue.Kind);
        Assert.True(dialogue.Loop);

        VirtualFileSystem fileSystem = BuildFileSystem();
        SoundBank bank = new(fileSystem, null);
        bank.MountDirectory("LOCAL:common/sounds");
        bank.MountDirectory("MISSION:sounds");
        Assert.True(bank.TryGet(dialogue.BankKey, out _),
            $"Definition '{dialogue.Name}' points at absent bank key '{dialogue.BankKey}'.");

        RecordingAudioDevice device = new();
        SoundSystem sounds = new(device, bank, fileSystem, null) { Definitions = registry };
        Assert.NotEqual(AudioVoiceHandle.None,
            sounds.PlayDefinition(dialogue.Name, priority: 320, looping: false));
        Assert.False(Assert.Single(device.Played).Parameters.Looping);
    }

    [RequiresGameDataFact]
    public void PlayingByDefinitionNameReachesTheBankKeyWithTheAuthoredDistances()
    {
        VirtualFileSystem fileSystem = BuildFileSystem();
        SoundBank bank = new(fileSystem, null);
        bank.MountDirectory("LOCAL:common/sounds");

        RecordingAudioDevice device = new();
        SoundSystem sounds = new(device, bank, fileSystem, null) { Definitions = Load() };

        Assert.NotEqual(AudioVoiceHandle.None, sounds.PlayDefinition("ai_death_1", new(0, 0, 0)));

        VoiceParameters played = Assert.Single(device.Played).Parameters;
        Assert.True(played.IsPositional);
        Assert.Equal(1f, played.Gain);
        Assert.False(played.Looping);

        // DefineSound("ai_death_1", "AI_DEATH_01", 0, 0, 0, 1, 0, 50.0, -1, -1, -1, -1, FALSE) --
        // the distances are the definition's own, in metres, not something the call site invented.
        Assert.Equal(0f, played.MinimumDistance);
        Assert.Equal(50f, played.MaximumDistance);
    }

    private static void Walk(IReadOnlyList<SoundDefinition> definitions, List<SoundDefinition> into)
    {
        foreach (SoundDefinition definition in definitions)
        {
            into.Add(definition);
            Walk(definition.Children, into);
        }
    }

    private static VirtualFileSystem BuildFileSystem()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        fileSystem.SetAlias(VirtualFileSystem.MissionAlias, "LOCAL:missions/location0/level1/");
        return fileSystem;
    }

    private static SoundDefinitionRegistry Load()
    {
        VirtualFileSystem fileSystem = BuildFileSystem();
        ResourceRegistry resources = new(fileSystem);

        GameLoop loop = new(new ManualTimeSource());
        SoundDefinitionRegistry registry = new();
        registry.Register(loop.Natives);
        registry.LoadForMission(resources, loop.Natives);
        return registry;
    }
}
