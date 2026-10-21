using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

public sealed class Igi2QuirksTests
{
    [Fact]
    public void OnlyMissionSeventeenHasTheBrokenComputer()
    {
        Igi2Quirks quirks = new();

        Assert.True(quirks.HasBrokenComputer(17));
        Assert.All([1, 2, 16, 18, 19], mission => Assert.False(quirks.HasBrokenComputer(mission)));
    }
}
