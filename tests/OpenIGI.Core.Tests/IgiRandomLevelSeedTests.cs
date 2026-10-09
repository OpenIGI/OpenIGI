using OpenIGI.Core.Numerics;
using Xunit;

namespace OpenIGI.Core.Tests;

/// <summary>The global stream a level starts from, as the probe read it on the original.</summary>
public sealed class IgiRandomLevelSeedTests
{
    [Fact]
    public void ALevelSeedStepsThroughTheWordsTheOriginalLogged()
    {
        // 0x416D90: C8FB1A07 / 39EA7756 / 7FF13CCD mixed with 0x19111911 gives D1EA0316 / C672F756 / 5DBF1ACD, the
        // words before mission 4's first draw; the accumulators before its next two draws were 6C583CB3 and 330283E2.
        IgiRandom stream = new();
        stream.SeedForLevel();

        Assert.Equal(0x6C583CB3u, stream.NextRaw());
        Assert.Equal(0x330283E2u, stream.NextRaw());
    }

    [Fact]
    public void ReseedingForALevelStartsTheSameSequenceAgain()
    {
        IgiRandom stream = new(7u);
        stream.NextRaw();
        stream.SeedForLevel();
        uint first = stream.NextRaw();

        IgiRandom fresh = new();
        fresh.SeedForLevel();

        Assert.Equal(fresh.NextRaw(), first);
    }
}
