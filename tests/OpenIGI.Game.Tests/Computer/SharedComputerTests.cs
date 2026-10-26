using OpenIGI.Core.Numerics;
using OpenIGI.Game.Computer;
using Xunit;

namespace OpenIGI.Game.Tests.Computer;

public sealed class SharedComputerTests
{
    [Fact]
    public void AHighlightNamesRangesAndKeepsThemOpenAcrossSpaces()
    {
        Assert.Equal([1, 4, 7, 8, 9], ComputerHilightIds.Parse("1 4 7-9"));
        Assert.Equal([3, 4, 5], ComputerHilightIds.Parse("3 - 5"));
        Assert.Equal([3, 5], ComputerHilightIds.Parse("3,-5"[..1] + ";5"));
    }

    [Fact]
    public void AWordThatDoesNotFitStartsTheNextLine()
    {
        List<ComputerWord> words = [];
        int under = ComputerTextWrap.Wrap(new Fonts(), 10, 20, "aaaa bbbb cc", "f", 80, words);

        // "aaaa " is 40 wide and fits; "bbbb " leaves nothing, so moves down; "cc" follows it.
        Assert.Equal([new ComputerWord("aaaa ", 10, 20), new ComputerWord("bbbb ", 10, 32), new ComputerWord("cc", 50, 32)], words);
        Assert.Equal(44, under);
    }

    [Fact]
    public void TheMapIsHeldInsideItsLimitAcross()
    {
        ComputerMapLimit limit = new(new Vector3d(0d, 0d, 0d), 100f);
        Vector3d held = limit.Clamp<X87>(new Vector3d(500000d, -500000d, 7d));

        Assert.Equal(new Vector3d(204800d, -204800d, 7d), held);
        Assert.True(limit.Contains<X87>(new Vector3d(204800d, 0d, 0d)));
        Assert.False(limit.Contains<X87>(new Vector3d(204801d, 0d, 0d)));
    }

    private sealed class Fonts : IComputerFonts
    {
        public int StringWidth(string font, string text) => text.Length * 8;

        public int LineHeight(string font) => 12;
    }
}
