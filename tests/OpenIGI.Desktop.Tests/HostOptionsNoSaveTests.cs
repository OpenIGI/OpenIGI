using OpenIGI.Host.Configuration;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary><c>-nosave</c>: a run that writes nothing of the player's own, profiles or settings.</summary>
public sealed class HostOptionsNoSaveTests
{
    [Fact]
    public void An_ordinary_run_saves_the_players_files()
    {
        HostOptions options = HostOptions.Parse(["-mission", "11"]);
        Assert.False(options.NoSave);
        Assert.True(options.SavesUserFiles);
    }

    [Fact]
    public void Nosave_keeps_the_players_files_as_they_were()
    {
        HostOptions options = HostOptions.Parse(["-mission", "11", "-lockstep", "-nosave"]);
        Assert.True(options.NoSave);
        Assert.False(options.SavesUserFiles);
    }

    [Fact]
    public void A_lockstep_run_neither_reads_nor_writes_the_players_profiles()
    {
        HostOptions options = HostOptions.Parse(["-mission", "1", "-lockstep"]);
        Assert.False(options.ReadsUserProfiles);
        Assert.False(options.SavesUserFiles);
        Assert.True(HostOptions.Parse(["-mission", "1"]).ReadsUserProfiles);
    }

    [Fact]
    public void A_benchmark_saves_nothing_either()
    {
        HostOptions options = HostOptions.Parse(["-benchmark", Path.Combine(Path.GetTempPath(), "openigi-nosave-bench.json")]);
        Assert.False(options.NoSave);
        Assert.False(options.SavesUserFiles);
    }
}
