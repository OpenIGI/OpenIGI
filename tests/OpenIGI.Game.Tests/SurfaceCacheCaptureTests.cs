using Xunit;
using OpenIGI.TestSupport;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Tests for <see cref="SurfaceCacheCapture"/>, which renders every model into its cards' pages.
/// </summary>
/// <remarks>
/// These check what the capture ASKS the backend to do, which is the half that can be verified without
/// a GL context. Whether the resulting pixels are right needs a real device and is checked by looking
/// at the atlas; whether every card was given its own page, in the right order, under a capture that
/// was opened and closed, is checked here.
/// </remarks>
public sealed class SurfaceCacheCaptureTests
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

    private static MeshGeometry Quad()
    {
        Vertex3D[] vertices =
        [
            new(new Vector3f(-1f, -1f, 0f), Vector3f.UnitZ, default, default, default),
            new(new Vector3f(1f, -1f, 0f), Vector3f.UnitZ, default, default, default),
            new(new Vector3f(1f, 1f, 0f), Vector3f.UnitZ, default, default, default),
            new(new Vector3f(-1f, 1f, 0f), Vector3f.UnitZ, default, default, default),
        ];

        return new MeshGeometry(vertices, [0, 1, 2, 0, 2, 3], [new MeshSubset(0, 6, 0, 0)]);
    }

    private static SurfaceCacheCapture.Subject Subject(params MeshCard[] cards) => new(
        Quad(),
        cards,
        _ => (TextureHandle.None, TextureHandle.None, SurfaceMaterial.Default));

    /// <summary>A capture with no cards does nothing rather than opening an empty atlas.</summary>
    [Fact]
    public void NothingToCaptureOpensNoAtlas()
    {
        RecordingGraphicsDevice device = new();

        Assert.Null(SurfaceCacheCapture.Capture(device, [], []));
        Assert.Empty(device.CardCaptures);
    }

    /// <summary>The capture is opened once and closed again, whatever happens in between.</summary>
    /// <remarks>
    /// Leaving it open would leave the backend rendering into the atlas for the rest of the frame, and
    /// the level would draw itself into its own surface cache.
    /// </remarks>
    [Fact]
    public void CaptureIsOpenedOnceAndAlwaysClosed()
    {
        RecordingGraphicsDevice device = new();
        MeshHandle mesh = device.CreateMesh(Quad().Vertices, Quad().Indices);

        SurfaceCacheCapture.Capture(
            device, [Subject(Card(MeshCardAxis.PositiveZ, 32))], [mesh]);

        Assert.Single(device.CardCaptures);
        Assert.False(device.IsCapturingCards, "the card capture was left open.");
    }

    /// <summary>Every card gets its own page, and no page is visited twice.</summary>
    [Fact]
    public void EveryCardIsDrawnIntoItsOwnPage()
    {
        RecordingGraphicsDevice device = new();
        MeshHandle mesh = device.CreateMesh(Quad().Vertices, Quad().Indices);

        MeshCard[] cards =
        [
            Card(MeshCardAxis.PositiveX, 32),
            Card(MeshCardAxis.NegativeX, 32),
            Card(MeshCardAxis.PositiveZ, 64),
            Card(MeshCardAxis.NegativeZ, 64),
        ];

        SurfaceCacheCapture.Result? result =
            SurfaceCacheCapture.Capture(device, [Subject(cards)], [mesh]);

        Assert.NotNull(result);
        Assert.Equal(4, device.CardPages.Count);
        Assert.Equal(4, device.CardPages.Distinct().Count());
    }

    /// <summary>The pages drawn into are the ones the layout assigned.</summary>
    /// <remarks>
    /// The capture and the tracing side agree on where a card lives only because both read the same
    /// layout. Drawing into a rectangle the layout did not assign puts the surface somewhere no trace
    /// will look, and the atlas would still look perfectly reasonable.
    /// </remarks>
    [Fact]
    public void PagesDrawnIntoAreTheOnesTheLayoutAssigned()
    {
        RecordingGraphicsDevice device = new();
        MeshHandle mesh = device.CreateMesh(Quad().Vertices, Quad().Indices);

        MeshCard[] cards = [Card(MeshCardAxis.PositiveZ, 32), Card(MeshCardAxis.NegativeZ, 16)];

        SurfaceCacheCapture.Result? result =
            SurfaceCacheCapture.Capture(device, [Subject(cards)], [mesh]);

        Assert.NotNull(result);

        foreach (SurfaceCacheLayout.Page page in result.Value.Layout.Pages)
        {
            Assert.Contains((page.X, page.Y, page.Width, page.Height), device.CardPages);
        }
    }

    /// <summary>Each card records which model it belongs to.</summary>
    /// <remarks>
    /// A trace finds a card and needs to know whose transform to apply to reach it. Getting this wrong
    /// reads one model's surface through another's placement.
    /// </remarks>
    [Fact]
    public void CardsRecordWhichModelTheyBelongTo()
    {
        RecordingGraphicsDevice device = new();
        MeshHandle mesh = device.CreateMesh(Quad().Vertices, Quad().Indices);

        SurfaceCacheCapture.Result? result = SurfaceCacheCapture.Capture(
            device,
            [
                Subject(Card(MeshCardAxis.PositiveZ, 32)),
                Subject(Card(MeshCardAxis.PositiveX, 32), Card(MeshCardAxis.NegativeX, 32)),
            ],
            [mesh, mesh]);

        Assert.NotNull(result);
        Assert.Equal([0, 1, 1], result.Value.Owners);
    }

    /// <summary>A model with no uploaded mesh is skipped rather than drawn with a null handle.</summary>
    [Fact]
    public void ModelsWithNoMeshAreSkipped()
    {
        RecordingGraphicsDevice device = new();

        SurfaceCacheCapture.Result? result = SurfaceCacheCapture.Capture(
            device, [Subject(Card(MeshCardAxis.PositiveZ, 32))], [MeshHandle.None]);

        Assert.NotNull(result);
        Assert.Empty(device.MeshDraws);
        Assert.False(device.IsCapturingCards, "the capture was left open after skipping everything.");
    }
}
