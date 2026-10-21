using Xunit;

namespace OpenIGI.Game.Igi1.Tests;

public sealed class Igi1QuirksTests
{
    [Fact]
    public void MissionSixsDestroyedTerminalIsDrawnFromTheArchivesSpelling()
    {
        Igi1Quirks quirks = new();

        Assert.Equal("252_01_2", quirks.DestroyedModelArchiveName(6, "252_02_1"));
        Assert.Equal("252_01_2", quirks.DestroyedModelArchiveName(6, "252_02_1".ToUpperInvariant()));
        Assert.Null(quirks.DestroyedModelArchiveName(5, "252_02_1"));
        Assert.Null(quirks.DestroyedModelArchiveName(6, "252_02_2"));
    }

    [Fact]
    public void AModCanAddASpelling()
    {
        Igi1Quirks quirks = new();
        quirks.DestroyedModelArchiveNames[(3, "400_02_1")] = "400_01_2";

        Assert.Equal("400_01_2", quirks.DestroyedModelArchiveName(3, "400_02_1"));
    }
}
