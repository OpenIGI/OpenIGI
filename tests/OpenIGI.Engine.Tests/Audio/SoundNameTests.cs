using OpenIGI.Engine.Audio;
using Xunit;

namespace OpenIGI.Engine.Tests.Audio;

public sealed class SoundNameTests
{
    [Theory]
    [InlineData("LOCAL:common/sounds/ak47_1.wav", "AK47_1")]
    [InlineData("MISSION:sounds/m1_ambience.wav", "M1_AMBIENCE")]
    [InlineData("LOCAL:common/sounds/sounds.res", "SOUNDS")]
    [InlineData(@"LOCAL:common\sounds\glock_reload_1.wav", "GLOCK_RELOAD_1")]
    [InlineData("LOCAL:menu_sel.wav", "MENU_SEL")]
    public void TheKeyIsTheBareStemOfThePath(string path, string expected) =>
        Assert.Equal(expected, SoundName.ToKey(path));

    [Fact]
    public void TheKeyIsUpperCasedSoLookupIgnoresCase()
    {
        Assert.Equal(SoundName.ToKey("LOCAL:sounds/AK47_1.wav"), SoundName.ToKey("LOCAL:sounds/ak47_1.wav"));
        Assert.Equal(SoundName.Hash("AK47_1"), SoundName.Hash("ak47_1"));
        Assert.Equal(SoundName.Bucket("AK47_1"), SoundName.Bucket("ak47_1"));
    }

    [Fact]
    public void TheFirstDotEndsTheKeyRatherThanTheLast()
    {
        // 0x497390 scans FORWARD for the dot, so a name carrying two of them keeps only what precedes
        // the first. Scanning backward -- the obvious way to find an extension -- gives "a.b" here.
        Assert.Equal("A", SoundName.ToKey("LOCAL:sounds/a.b.wav"));
    }

    [Fact]
    public void ABareFilenameHasNoKeyAtAll()
    {
        // A real quirk of 0x497390 rather than an omission here: the backward scan for a separator runs
        // off the front of the string and returns without copying, leaving the key buffer empty. It
        // never bites because every path reaching it carries at least a "LOCAL:".
        Assert.Equal(string.Empty, SoundName.ToKey("ak47_1.wav"));
    }

    [Fact]
    public void APathWithNoDotHasNoKey()
    {
        Assert.Equal(string.Empty, SoundName.ToKey("LOCAL:common/sounds"));
        Assert.Equal(string.Empty, SoundName.ToKey(string.Empty));
    }

    [Fact]
    public void TheBucketIsAlwaysInsideTheTable()
    {
        foreach (string name in (string[])["A", "AK47_1", "MENU_SEL", "M1_AMBIENCE", "GAME_MUSIC", "ZZZZZZZZZZZZ"])
        {
            Assert.InRange(SoundName.Bucket(name), 0, SoundName.BucketCount - 1);
        }
    }

    [Fact]
    public void TheTwoHashesUseDifferentMultiplierSteps()
    {
        // 317 in Hash and 318 in Bucket. They are one apart and easy to read straight past, so this
        // pins them: a two-character name is the shortest input where the step shows up at all, and
        // making both 317 collapses the two functions onto the same walk.
        Assert.NotEqual(SoundName.Hash("AB") % SoundName.BucketCount, SoundName.Bucket("AB"));
    }

    [Fact]
    public void TheHashSumIsAllowedToOverflow()
    {
        // The original accumulates into a 32-bit int with no saturation, so a long name wraps. The test
        // is that a long name produces an answer at all rather than throwing in a checked context.
        string name = new('Z', SoundName.MaximumLength - 1);
        Assert.InRange(SoundName.Bucket(name), 0, SoundName.BucketCount - 1);
        SoundName.Hash(name);
    }
}
