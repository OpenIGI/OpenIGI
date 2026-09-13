using Xunit;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Tests for <see cref="SurfaceCacheInstances"/>, which gives every placement its own lighting pages.
/// </summary>
/// <remarks>
/// The property that matters is the one the type exists for: two placements of the same model must not
/// share a lighting page. Sharing would light every crate in the level identically, which looks like a
/// plausible scene — badly lit, but plausible — and would never be reported as a fault.
/// </remarks>
public sealed class SurfaceCacheInstancesTests
{
    private static MeshCard Card(MeshCardAxis axis, int resolution) => new(
        axis,
        Vector3f.Zero,
        HalfWidth: 1f,
        HalfHeight: 1f,
        Depth: 2f,
        ResolutionU: resolution,
        ResolutionV: resolution,
        AreaShare: 1f / 6f);

    /// <summary>A capture of two models: the first with two cards, the second with three.</summary>
    private static SurfaceCacheCapture.Result Capture() => new(
        SurfaceCacheLayout.Pack(
        [
            Card(MeshCardAxis.PositiveZ, 32),
            Card(MeshCardAxis.NegativeZ, 32),
            Card(MeshCardAxis.PositiveX, 16),
            Card(MeshCardAxis.NegativeX, 16),
            Card(MeshCardAxis.PositiveY, 16),
        ]),
        [
            Card(MeshCardAxis.PositiveZ, 32),
            Card(MeshCardAxis.NegativeZ, 32),
            Card(MeshCardAxis.PositiveX, 16),
            Card(MeshCardAxis.NegativeX, 16),
            Card(MeshCardAxis.PositiveY, 16),
        ],
        [0, 0, 1, 1, 1]);

    private static WorldPlacement Placement(double x) =>
        new(new Vector3d(x, 0, 0), Matrix3f.Identity, default!, null!, null!, $"at{x}", null!);

    /// <summary>A level whose placements use no captured model has nothing to light.</summary>
    [Fact]
    public void PlacementsWithNoModelProduceNothing()
    {
        Assert.Null(SurfaceCacheInstances.Build(Capture(), [Placement(0)], _ => -1));
    }

    /// <summary>Each placement gets one page per card of its own model.</summary>
    [Fact]
    public void EachPlacementGetsAPagePerCardOfItsModel()
    {
        SurfaceCacheInstances instances = SurfaceCacheInstances.Build(
            Capture(),
            [Placement(0), Placement(10), Placement(20)],
            p => p.TaskName == "at20" ? 1 : 0)!;

        Assert.Equal(3, instances.Instances.Length);

        // Two placements of model 0 (two cards each) and one of model 1 (three cards).
        Assert.Equal(2, instances.Instances[0].PageCount);
        Assert.Equal(2, instances.Instances[1].PageCount);
        Assert.Equal(3, instances.Instances[2].PageCount);
        Assert.Equal(7, instances.Layout.Pages.Length);
    }

    /// <summary>Two placements of one model must not share a lighting page.</summary>
    /// <remarks>
    /// <b>The whole reason this type exists.</b> One crate stands in sunlight and another in a hangar's
    /// shadow; a shared page gives them the same light, and the level looks plausible while being
    /// uniformly wrong.
    /// </remarks>
    [Fact]
    public void TwoPlacementsOfOneModelDoNotShareAPage()
    {
        SurfaceCacheInstances instances = SurfaceCacheInstances.Build(
            Capture(), [Placement(0), Placement(10)], _ => 0)!;

        SurfaceCacheInstances.Placed first = instances.Instances[0];
        SurfaceCacheInstances.Placed second = instances.Instances[1];

        Assert.NotEqual(first.FirstPage, second.FirstPage);

        for (int a = 0; a < first.PageCount; a++)
        {
            for (int b = 0; b < second.PageCount; b++)
            {
                Assert.False(
                    instances.Layout.Pages[first.FirstPage + a]
                        .Overlaps(instances.Layout.Pages[second.FirstPage + b]),
                    "two placements of the same model were given overlapping lighting pages.");
            }
        }
    }

    /// <summary>A lighting page must be the same size as the card it lights.</summary>
    /// <remarks>
    /// A lighting texel reads its albedo at the same position within the card, in the captured atlas.
    /// Pages of different sizes would need a rescale between the two, and getting that slightly wrong
    /// shifts the lighting off the surface it belongs to by a texel — which reads as a soft edge rather
    /// than as a bug.
    /// </remarks>
    [Fact]
    public void LightingPagesMatchTheirCardsInSize()
    {
        SurfaceCacheCapture.Result capture = Capture();

        SurfaceCacheInstances instances = SurfaceCacheInstances.Build(
            capture, [Placement(0), Placement(10)], _ => 0)!;

        for (int page = 0; page < instances.Layout.Pages.Length; page++)
        {
            MeshCard card = capture.Cards[instances.CardForPage[page]];

            Assert.Equal(card.ResolutionU, instances.Layout.Pages[page].Width);
            Assert.Equal(card.ResolutionV, instances.Layout.Pages[page].Height);
        }
    }

    /// <summary>Each placement records where it stands, so the lighting can be placed in the world.</summary>
    [Fact]
    public void PlacementsCarryTheirWorldTransform()
    {
        SurfaceCacheInstances instances = SurfaceCacheInstances.Build(
            Capture(), [Placement(0), Placement(42)], _ => 0)!;

        Assert.Equal(0d, instances.Instances[0].Position.X);
        Assert.Equal(42d, instances.Instances[1].Position.X);
    }

    /// <summary>Every page names the card it lights.</summary>
    [Fact]
    public void EveryPageNamesItsCard()
    {
        SurfaceCacheCapture.Result capture = Capture();

        SurfaceCacheInstances instances = SurfaceCacheInstances.Build(
            capture, [Placement(0)], _ => 1)!;

        // Model 1 owns cards 2, 3 and 4 in the capture above.
        Assert.Equal([2, 3, 4], instances.CardForPage);
    }
}
