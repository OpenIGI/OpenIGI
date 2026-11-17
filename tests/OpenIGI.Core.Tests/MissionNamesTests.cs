using OpenIGI.Core.Titles;
using Xunit;

namespace OpenIGI.Core.Tests;

/// <summary>The missions' names the game writes down for the launcher: read back by id, kept per title, rewritten only when changed.</summary>
public sealed class MissionNamesTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("openigi-missions-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Names_written_by_the_game_are_read_by_id_for_each_title()
    {
        Assert.Empty(MissionNames.Load("igi1", _folder));
        MissionNames.Save("igi1", [new MissionName(1, 1, "Trainyard"), new MissionName(2, 2, "Get Priboi")], _folder);
        MissionNames.Save("igi2", [new MissionName(101, 1, "Ice Breaker")], _folder);

        IReadOnlyDictionary<int, MissionName> first = MissionNames.Load("igi1", _folder);
        Assert.Equal("Get Priboi", first[2].Name);
        Assert.Equal(2, first.Count);
        Assert.Equal(1, MissionNames.Load("igi2", _folder)[101].Number);
    }

    [Fact]
    public void The_same_names_are_not_written_again()
    {
        MissionName[] missions = [new MissionName(1, 1, "Trainyard")];
        MissionNames.Save("igi1", missions, _folder);
        string path = Path.Combine(_folder, "missions-igi1.json");
        DateTime written = File.GetLastWriteTimeUtc(path);
        File.SetLastWriteTimeUtc(path, written.AddMinutes(-5));
        MissionNames.Save("igi1", missions, _folder);
        Assert.Equal(written.AddMinutes(-5), File.GetLastWriteTimeUtc(path));

        File.WriteAllText(path, "not json");
        Assert.Empty(MissionNames.Load("igi1", _folder));
    }
}
