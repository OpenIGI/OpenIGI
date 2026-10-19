using OpenIGI.Core.Graphics;
using OpenIGI.Presentation.Lighting;
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
    /// <summary>Every card's camera sits on its own face and looks in at the model.</summary>
    /// <remarks>
    /// The rotation and the translation of the card's view have to agree: the model goes through the
    /// rotation, so the eye must too, rows dotted with the vector as the scene camera does it. Multiplied
    /// by the columns instead, the eye lands where the transpose puts it, and the two Z cards hide the
    /// mistake -- their frame is its own transpose -- while every X and Y card shifts the model off its
    /// page. Three quarters of a level's side pages came back empty that way, every wall was then resolved
    /// through its roof card's silhouette, and a storage tank read as a fan of stripes in a window.
    /// </remarks>
    [Theory]
    [InlineData(MeshCardAxis.PositiveX)]
    [InlineData(MeshCardAxis.NegativeX)]
    [InlineData(MeshCardAxis.PositiveY)]
    [InlineData(MeshCardAxis.NegativeY)]
    [InlineData(MeshCardAxis.PositiveZ)]
    [InlineData(MeshCardAxis.NegativeZ)]
    public void EveryCardLooksInwardFromItsOwnFace(MeshCardAxis axis)
    {
        RecordingGraphicsDevice device = new();
        MeshHandle mesh = device.CreateMesh(Quad().Vertices, Quad().Indices);

        // Off the origin, and a different size every way, so that a transposed frame or a swapped axis
        // cannot land on the right answer by symmetry.
        MeshCard card = new(
            axis,
            new Vector3f(3f, -2f, 5f),
            HalfWidth: 1.5f,
            HalfHeight: 2.5f,
            Depth: 4f,
            ResolutionU: 16,
            ResolutionV: 16,
            AreaShare: 1f / 6f);

        SurfaceCacheCapture.Capture(device, [Subject(card)], [mesh]);

        RecordingGraphicsDevice.MeshDrawCall draw = Assert.Single(device.MeshDraws);
        Matrix4f modelView = draw.ModelView;

        // The card's own centre is the eye.
        AssertClose(Vector3f.Zero, modelView.TransformPoint(card.Centre));

        // A point across the card by (a, b) and c behind its plane is seen at (a, -b, c): right is
        // right, up runs down the page as the vertex stage then flips it back, and forward is INTO the
        // model, which is away from the face the card looks from.
        Vector3f behind = card.Centre + (card.Right * 0.7f) + (card.Up * -0.4f) - (card.Direction * 1.3f);
        AssertClose(new Vector3f(0.7f, 0.4f, 1.3f), modelView.TransformPoint(behind));
    }

    private static void AssertClose(Vector3f expected, Vector3f actual)
    {
        Assert.Equal(expected.X, actual.X, 1e-4f);
        Assert.Equal(expected.Y, actual.Y, 1e-4f);
        Assert.Equal(expected.Z, actual.Z, 1e-4f);
    }

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
