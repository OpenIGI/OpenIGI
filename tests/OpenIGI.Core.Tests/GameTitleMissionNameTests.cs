using OpenIGI.Core.Titles;
using Xunit;

namespace OpenIGI.Core.Tests;

public sealed class GameTitleMissionNameTests
{
    [Fact]
    public void Igi1NamesEveryMissionByItsId()
    {
        Assert.Equal(GameTitle.Igi1.MissionCount, GameTitle.Igi1.MissionNames.Count);
        Assert.Equal("Trainyard", GameTitle.Igi1.MissionName(1));
        Assert.Equal("Missile Trainyard", GameTitle.Igi1.MissionName(9));
        Assert.Null(GameTitle.Igi1.MissionName(0));
        Assert.Null(GameTitle.Igi1.MissionName(15));
    }
}
