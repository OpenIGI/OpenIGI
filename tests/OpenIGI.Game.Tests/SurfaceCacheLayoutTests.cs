using Xunit;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Tests for <see cref="SurfaceCacheLayout"/>, which assigns every card its rectangle of atlas.
/// </summary>
/// <remarks>
/// Two properties decide whether anything downstream can be trusted. Pages must not overlap, because a
/// trace reading one surface would otherwise get another model's — and both would look like ordinary
/// surfaces, so nothing would report it. And a card that cannot be placed must keep its entry, because
/// pages are addressed by index and dropping one renumbers every page after it.
/// </remarks>
public sealed class SurfaceCacheLayoutTests
{
    /// <summary>A card of the given texel size, with the rest of its geometry irrelevant here.</summary>
    private static MeshCard Card(int resolutionU, int resolutionV) => new(
        MeshCardAxis.PositiveZ,
        Vector3f.Zero,
        HalfWidth: 1f,
        HalfHeight: 1f,
        Depth: 1f,
        ResolutionU: resolutionU,
        ResolutionV: resolutionV,
        AreaShare: 1f);

    /// <summary>No cards means no atlas, rather than a degenerate one.</summary>
    [Fact]
    public void NoCardsProduceNoAtlas()
    {
        SurfaceCacheLayout layout = SurfaceCacheLayout.Pack([]);

        Assert.Empty(layout.Pages);
        Assert.Equal(0, layout.Width);
    }

    /// <summary>Every card gets a page, in the order it was given.</summary>
    /// <remarks>
    /// The packer sorts internally to lay tall pages first, and the result must not come back sorted:
    /// a trace addresses card three of model seven by index, so a reordered result silently points
    /// every lookup at a different surface.
    /// </remarks>
    [Fact]
    public void PagesComeBackInTheOrderTheCardsWereGiven()
    {
        MeshCard[] cards = [Card(16, 16), Card(64, 64), Card(8, 8), Card(32, 32)];

        SurfaceCacheLayout layout = SurfaceCacheLayout.Pack(cards);

        Assert.Equal(4, layout.Pages.Length);
        Assert.Equal(16, layout.Pages[0].Width);
        Assert.Equal(64, layout.Pages[1].Width);
        Assert.Equal(8, layout.Pages[2].Width);
        Assert.Equal(32, layout.Pages[3].Width);
    }

    /// <summary>No two pages may share a texel.</summary>
    /// <remarks>
    /// <b>The test the file exists for.</b> Two overlapping pages both contain a plausible surface, so
    /// the fault is invisible in the atlas and invisible in the frame — it shows only as one model
    /// occasionally reflecting another's colour, which reads as a lighting bug.
    /// </remarks>
    [Fact]
    public void NoTwoPagesOverlap()
    {
        List<MeshCard> cards = [];

        for (int i = 0; i < 200; i++)
        {
            // A spread of sizes, deliberately not all equal: pages of one size tile perfectly and would
            // hide an off-by-one that only appears where a short page follows a tall one.
            cards.Add(Card(8 << (i % 4), 8 << ((i + 2) % 4)));
        }

        SurfaceCacheLayout layout = SurfaceCacheLayout.Pack(cards);

        for (int a = 0; a < layout.Pages.Length; a++)
        {
            for (int b = a + 1; b < layout.Pages.Length; b++)
            {
                Assert.False(
                    layout.Pages[a].Overlaps(layout.Pages[b]),
                    $"page {a} at {layout.Pages[a]} overlaps page {b} at {layout.Pages[b]}.");
            }
        }
    }

    /// <summary>Every page must lie wholly inside the atlas.</summary>
    [Fact]
    public void EveryPageFitsInsideTheAtlas()
    {
        List<MeshCard> cards = [];

        for (int i = 0; i < 120; i++)
        {
            cards.Add(Card(16 + (i % 5 * 16), 16 + (i % 3 * 16)));
        }

        SurfaceCacheLayout layout = SurfaceCacheLayout.Pack(cards);

        foreach (SurfaceCacheLayout.Page page in layout.Pages)
        {
            Assert.True(page.IsAllocated, "a page that should have fitted was left unallocated.");
            Assert.True(page.Right <= layout.Width, $"page {page} runs past the atlas width.");
            Assert.True(page.Bottom <= layout.Height, $"page {page} runs past the atlas height.");
        }
    }

    /// <summary>Pages are separated, so a linear fetch at an edge cannot reach its neighbour.</summary>
    /// <remarks>
    /// Without the gutter, sampling a page's edge blends in whatever was packed beside it — a different
    /// model's surface, with no geometric relationship to this one.
    /// </remarks>
    [Fact]
    public void PagesAreSeparatedByAGutter()
    {
        MeshCard[] cards = [Card(32, 32), Card(32, 32), Card(32, 32)];

        SurfaceCacheLayout layout = SurfaceCacheLayout.Pack(cards);

        for (int a = 0; a < layout.Pages.Length; a++)
        {
            for (int b = a + 1; b < layout.Pages.Length; b++)
            {
                SurfaceCacheLayout.Page first = layout.Pages[a];
                SurfaceCacheLayout.Page second = layout.Pages[b];

                bool apart =
                    first.Right + SurfaceCacheLayout.Gutter <= second.X
                    || second.Right + SurfaceCacheLayout.Gutter <= first.X
                    || first.Bottom + SurfaceCacheLayout.Gutter <= second.Y
                    || second.Bottom + SurfaceCacheLayout.Gutter <= first.Y;

                Assert.True(apart, $"pages {first} and {second} are not a gutter apart.");
            }
        }
    }

    /// <summary>A small set of cards must not allocate an enormous atlas.</summary>
    [Fact]
    public void AtlasIsSizedToWhatIsActuallyPacked()
    {
        SurfaceCacheLayout layout = SurfaceCacheLayout.Pack([Card(16, 16), Card(16, 16)]);

        Assert.True(layout.Width <= 128, $"two small cards allocated a {layout.Width}-square atlas.");
    }

    /// <summary>A card too large for the atlas keeps its entry rather than being dropped.</summary>
    /// <remarks>
    /// Pages are addressed by index, so dropping one renumbers every page after it and points every
    /// later lookup at the wrong surface. An unallocated page is wrong in one place instead.
    /// </remarks>
    [Fact]
    public void CardsThatCannotFitKeepTheirEntry()
    {
        MeshCard[] cards = [Card(32, 32), Card(4096, 4096), Card(32, 32)];

        SurfaceCacheLayout layout = SurfaceCacheLayout.Pack(cards, maximumSize: 128);

        Assert.Equal(3, layout.Pages.Length);
        Assert.True(layout.Pages[0].IsAllocated);
        Assert.False(layout.Pages[1].IsAllocated, "an oversized card was given atlas space.");
        Assert.True(layout.Pages[2].IsAllocated, "a card after the oversized one lost its page.");
    }

    /// <summary>The packing must not waste most of the atlas.</summary>
    /// <remarks>
    /// A shelf packer trades some space for simplicity, and this pins how much. Falling well below
    /// this means rows are mostly air and the sort is not doing its job.
    /// </remarks>
    [Fact]
    public void PackingIsReasonablyTight()
    {
        List<MeshCard> cards = [];

        for (int i = 0; i < 300; i++)
        {
            cards.Add(Card(16 << (i % 3), 16 << ((i + 1) % 3)));
        }

        SurfaceCacheLayout layout = SurfaceCacheLayout.Pack(cards);

        Assert.True(
            layout.Occupancy > 0.45f,
            $"the atlas is only {layout.Occupancy * 100f:0.#}% occupied, which is mostly waste.");
    }
}
