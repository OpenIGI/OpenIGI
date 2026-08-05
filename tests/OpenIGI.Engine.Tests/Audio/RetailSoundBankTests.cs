using System.Globalization;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Vfs;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Engine.Tests.Audio;

/// <summary>
/// Mounts the sound directories of a real installation, which is the only way to know the bank finds
/// what the game will ask it for.
/// </summary>
/// <remarks>Skipped when <c>IGI_GAME_PATH</c> is unset.</remarks>
public sealed class RetailSoundBankTests
{
    private readonly ITestOutputHelper _output;

    public RetailSoundBankTests(ITestOutputHelper output) => _output = output;

    [RequiresGameDataFact]
    public void TheCommonDirectoryMountsTheEffectBank()
    {
        SoundBank bank = Mount("LOCAL:common/sounds");

        Assert.True(bank.Count > 300, $"Expected the common bank, got {bank.Count} sound(s).");

        // Names the weapon tables actually carry, so this fails if the key extraction ever starts
        // producing paths instead of stems. The pair is worth noting: an automatic weapon's fire sound
        // is a LOOP plus a separate loop-end sample, which is what the two weapon-record fields are for.
        Assert.True(bank.TryGet("ak47_loop", out _));
        Assert.True(bank.TryGet("ak47_loop_e", out _));
        Assert.True(bank.TryGet("AK47_LOOP", out _), "Lookup must ignore case.");
        Assert.True(bank.TryGet("glock_reload_1", out _));

        _output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"common/sounds: {bank.Count}"));
    }

    [RequiresGameDataFact]
    public void TheMenuArchiveMountsTheMenuSounds()
    {
        SoundBank bank = new(FileSystem(), null);
        Assert.True(bank.MountArchive("LOCAL:menusystem/sound/sounds.res") > 0);

        // The two the menu manager names for moving the selection and choosing.
        Assert.True(bank.TryGet("menu_high", out _));
        Assert.True(bank.TryGet("menu_sel", out _));
    }

    [RequiresGameDataFact]
    public void AMissionsOwnSoundsMountBesideTheCommonOnes()
    {
        VirtualFileSystem fileSystem = FileSystem();
        fileSystem.SetAlias(VirtualFileSystem.MissionAlias, "LOCAL:missions/location0/level1/");

        SoundBank bank = new(fileSystem, null);
        int common = bank.MountDirectory("LOCAL:common/sounds");
        int mission = bank.MountDirectory("MISSION:sounds");

        Assert.True(mission > 0, "Mission 1 ships its own sounds.");
        Assert.True(bank.TryGet("m1_ambience", out SoundEntry ambience));
        Assert.True(bank.TryGet("game_music", out SoundEntry music));

        // Both are streamed, so the bank holds a path and no bytes -- which is what keeps a three
        // megabyte music track out of memory until something plays it.
        Assert.True(music.IsStreamed);
        Assert.True(music.Data.IsEmpty);
        Assert.True(ambience.IsStreamed);

        // A resident sound does carry its bytes.
        Assert.True(bank.TryGet("ak47_loop", out SoundEntry rifle));
        Assert.False(rifle.IsStreamed);
        Assert.False(rifle.Data.IsEmpty);

        _output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"common {common} + mission {mission} = {bank.Count}"));
    }

    [RequiresGameDataFact]
    public void NoTwoShippedSoundsCollideInTheOriginalsHashTable()
    {
        // This is what licenses the port to use a dictionary instead of reproducing the 101-bucket,
        // two-hash table at 0x4955F0. The original's lookup compares hashes, so two names sharing a
        // bucket AND an identity hash would resolve to whichever was inserted -- a real behaviour the
        // port would have to copy. Over the shipped data it never happens, so a dictionary keyed by the
        // name is exactly equivalent. If a mod ever broke this, the assertion says so.
        VirtualFileSystem fileSystem = FileSystem();
        fileSystem.SetAlias(VirtualFileSystem.MissionAlias, "LOCAL:missions/location0/level1/");

        SoundBank bank = new(fileSystem, null);
        bank.MountDirectory("LOCAL:common/sounds");
        bank.MountDirectory("MISSION:sounds");
        bank.MountArchive("LOCAL:menusystem/sound/sounds.res");

        Dictionary<(int Bucket, int Hash), string> seen = [];
        List<string> collisions = [];

        foreach (string name in bank.Names)
        {
            (int, int) key = (SoundName.Bucket(name), SoundName.Hash(name));
            if (seen.TryGetValue(key, out string? other))
            {
                collisions.Add($"{name} collides with {other}");
                continue;
            }

            seen[key] = name;
        }

        _output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{bank.Count} names, {seen.Count} distinct hashes"));
        Assert.Empty(collisions);
    }

    [RequiresGameDataFact]
    public void MountingTwiceKeepsTheFirstRatherThanTheSecond()
    {
        // "SOUND - File %s already loaded!" -- the original looks a key up before inserting and leaves
        // the existing entry alone, which is why mount order decides what a name resolves to.
        SoundBank bank = Mount("LOCAL:common/sounds");
        int first = bank.Count;

        Assert.Equal(0, bank.MountDirectory("LOCAL:common/sounds"));
        Assert.Equal(first, bank.Count);
    }

    [RequiresGameDataFact]
    public void AnAbsentDirectoryIsNotAnError()
    {
        SoundBank bank = new(FileSystem(), null);
        Assert.Equal(0, bank.MountDirectory("LOCAL:common/there-is-no-such-directory"));
        Assert.Equal(0, bank.Count);
    }

    private static VirtualFileSystem FileSystem()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        return fileSystem;
    }

    private static SoundBank Mount(string directory)
    {
        SoundBank bank = new(FileSystem(), null);
        bank.MountDirectory(directory);
        return bank;
    }
}
