using Xunit;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Tests for <see cref="MeshCardBaker"/> and <see cref="MeshCard"/>.
/// </summary>
/// <remarks>
/// <para>
/// Nothing downstream of a card can be judged by eye. A card that is too small crops the capture, and
/// the missing surface does not appear as a visible error — it appears as slightly less bounced light
/// from a wall nobody was looking at. A card whose basis is transposed or mirrored produces a page that
/// looks entirely plausible, because most of this game's surfaces are tiling textures that read the
/// same either way round.
/// </para>
/// <para>
/// So the shapes here are ones whose right answer is arithmetic rather than aesthetic: a single quad,
/// whose two faces are the only two directions that describe it; a cube, whose six faces are equal by
/// construction; and an asymmetric pair of quads, where a card fitted to the triangles facing its way
/// would be measurably smaller than the model.
/// </para>
/// </remarks>
public sealed class MeshCardBakerTests
{
    /// <summary>A model with no triangles has nothing to capture, and says so rather than throwing.</summary>
    [Fact]
    public void GeometryWithNoTrianglesProducesNoCards() =>
        Assert.Empty(MeshCardBaker.Bake([], []));

    /// <summary>A flat panel is described by a card above it and one below, and by nothing on its edges.</summary>
    /// <remarks>
    /// The defining case. Its four edges have no area at all, so a card on any of them would be an
    /// atlas page of empty space; its underside has no triangles either, but is a surface light lands on
    /// — this game's floors are single quads with no back — and a ray arriving from below must find
    /// something there.
    /// </remarks>
    [Fact]
    public void AFlatPanelGetsCardsAboveAndBelowItAndNoneOnItsEdges()
    {
        (Vertex3D[] vertices, ushort[] indices) = Quad(
            new Vector3f(0f, 0f, 0f),
            new Vector3f(2f, 0f, 0f),
            new Vector3f(2f, 2f, 0f),
            new Vector3f(0f, 2f, 0f));

        IReadOnlyList<MeshCard> cards = MeshCardBaker.Bake(vertices, indices);
        MeshCardAxis[] expected = [MeshCardAxis.PositiveZ, MeshCardAxis.NegativeZ];

        Assert.Equal(expected, Axes(cards));
        Assert.All(cards, card => Assert.Equal(0.5f, card.AreaShare, 0.001f));
    }

    /// <summary>A cube is described from all six directions, each carrying a sixth of its surface.</summary>
    [Fact]
    public void ACubeGetsAllSixCardsWithEqualShares()
    {
        (Vertex3D[] vertices, ushort[] indices) = Cube(2f);

        IReadOnlyList<MeshCard> cards = MeshCardBaker.Bake(vertices, indices);

        Assert.Equal(6, cards.Count);
        Assert.All(cards, card => Assert.Equal(1f / 6f, card.AreaShare, 0.001f));
    }

    /// <summary>The six shares of a model sum to one, so a caller can read them as a distribution.</summary>
    [Fact]
    public void TheSharesOfAModelSumToOne()
    {
        (Vertex3D[] vertices, ushort[] indices) = Cube(1.5f);

        float sum = MeshCardBaker.Bake(vertices, indices).Sum(card => card.AreaShare);

        Assert.Equal(1f, sum, 0.001f);
    }

    /// <summary>Every card spans the model's whole bounding box on its plane, not just what faces it.</summary>
    /// <remarks>
    /// The property the whole design turns on, and the one an optimisation would break. The model here
    /// is a small horizontal plate spanning a quarter of the model in x, plus a tall wall spanning all
    /// of it: only the plate faces upwards, so a card fitted to the contributing triangles would be a
    /// quarter as wide and the wall's top edge would fall outside the capture entirely.
    /// </remarks>
    [Fact]
    public void ACardCoversTheModelsFullBoundingBoxOnItsPlane()
    {
        List<Vertex3D> vertices = [];
        List<ushort> indices = [];

        // A horizontal plate, facing +Z, occupying x in [0, 1].
        AddQuad(
            vertices,
            indices,
            new Vector3f(0f, 0f, 0f),
            new Vector3f(1f, 0f, 0f),
            new Vector3f(1f, 1f, 0f),
            new Vector3f(0f, 1f, 0f));

        // A vertical wall, facing -Y, running the model's whole width and taking the box to z = 2.
        AddQuad(
            vertices,
            indices,
            new Vector3f(0f, 0f, 0f),
            new Vector3f(4f, 0f, 0f),
            new Vector3f(4f, 0f, 2f),
            new Vector3f(0f, 0f, 2f));

        IReadOnlyList<MeshCard> cards = MeshCardBaker.Bake([.. vertices], [.. indices]);
        MeshCard above = cards.Single(card => card.Axis == MeshCardAxis.PositiveZ);

        // Right is +X for a +Z card, Up is +Y.
        Assert.Equal(0f, above.Centre.X - above.HalfWidth, 0.001f);
        Assert.Equal(4f, above.Centre.X + above.HalfWidth, 0.001f);
        Assert.Equal(0f, above.Centre.Y - above.HalfHeight, 0.001f);
        Assert.Equal(1f, above.Centre.Y + above.HalfHeight, 0.001f);

        // And the card itself sits on the face of the box, with the whole model behind it.
        Assert.Equal(2f, above.Centre.Z, 0.001f);
        Assert.Equal(2f, above.Depth, 0.001f);
        Assert.Equal(0f, above.FarCentre.Z, 0.001f);
    }

    /// <summary>Each card's near plane is the box face it faces, and its far plane the opposite one.</summary>
    [Fact]
    public void EachCardSitsOnTheBoxFaceItFacesWithTheModelBehindIt()
    {
        (Vertex3D[] vertices, ushort[] indices) = Cube(3f);

        foreach (MeshCard card in MeshCardBaker.Bake(vertices, indices))
        {
            // The cube runs from 0 to 3, so the face a card sits on is at 3 along its own axis for a
            // positive direction and at 0 for a negative one, and the far plane is the other.
            float near = Dot(card.Centre, card.Direction);
            float far = Dot(card.FarCentre, card.Direction);

            Assert.Equal(3f, near - far, 0.001f);
            Assert.Equal(3f, card.Depth, 0.001f);
            Assert.True(near > far, $"the {card.Axis} card's far plane is in front of it.");
        }
    }

    /// <summary>A card with no thickness along its axis still gets a usable orthographic range.</summary>
    /// <remarks>
    /// A single quad has exactly zero extent along the direction it faces. An orthographic volume whose
    /// near and far planes coincide has nothing to rasterise into, so the depth is floored — the failure
    /// it prevents is an empty atlas page for every flat panel in the level.
    /// </remarks>
    [Fact]
    public void AFlatPanelStillGetsANonDegenerateDepth()
    {
        (Vertex3D[] vertices, ushort[] indices) = Quad(
            new Vector3f(0f, 0f, 5f),
            new Vector3f(1f, 0f, 5f),
            new Vector3f(1f, 1f, 5f),
            new Vector3f(0f, 1f, 5f));

        Assert.All(MeshCardBaker.Bake(vertices, indices), card => Assert.True(
            card.Depth > 0f, $"the {card.Axis} card has a degenerate orthographic range."));
    }

    /// <summary>A card's texel count follows its world size on each axis independently.</summary>
    [Fact]
    public void ResolutionFollowsTheCardsWorldSizeOnEachAxis()
    {
        // Four units wide, one tall, so at the default eight texels per unit the card is 32 by 8.
        (Vertex3D[] vertices, ushort[] indices) = Quad(
            new Vector3f(0f, 0f, 0f),
            new Vector3f(4f, 0f, 0f),
            new Vector3f(4f, 1f, 0f),
            new Vector3f(0f, 1f, 0f));

        MeshCard above = MeshCardBaker.Bake(vertices, indices)
            .Single(card => card.Axis == MeshCardAxis.PositiveZ);

        Assert.Equal(32, above.ResolutionU);
        Assert.Equal(8, above.ResolutionV);
        Assert.Equal(0.125f, above.TexelSizeU, 0.001f);
    }

    /// <summary>A bigger model gets more texels than a smaller one at the same texel density.</summary>
    [Theory]
    [InlineData(1.5f, 3f)]
    [InlineData(3f, 8f)]
    [InlineData(4f, 12f)]
    public void ABiggerCardGetsMoreTexelsThanASmallerOne(float small, float large)
    {
        Assert.True(
            SquareCardResolution(small) < SquareCardResolution(large),
            $"a {large}-unit card got no more texels than a {small}-unit one.");
    }

    /// <summary>However large or small the model, the resolution stays within its bounds.</summary>
    /// <remarks>
    /// Both ends matter and for different reasons. A one-texel card describes a surface's average colour
    /// rather than the surface; a terrain-sized card at this density would ask for thousands of texels
    /// on an axis, which is an atlas allocation that fails at load rather than a page that looks poor.
    /// </remarks>
    [Theory]
    [InlineData(0.001f)]
    [InlineData(0.5f)]
    [InlineData(4f)]
    [InlineData(200f)]
    [InlineData(20000f)]
    public void ResolutionStaysWithinItsBounds(float size)
    {
        int resolution = SquareCardResolution(size);

        Assert.InRange(resolution, MeshCardBaker.MinimumResolution, MeshCardBaker.MaximumResolution);
    }

    /// <summary>The texel density is the caller's, and doubling it doubles the resolution.</summary>
    [Fact]
    public void TheTexelDensityScalesTheResolution()
    {
        (Vertex3D[] vertices, ushort[] indices) = Quad(
            new Vector3f(0f, 0f, 0f),
            new Vector3f(2f, 0f, 0f),
            new Vector3f(2f, 2f, 0f),
            new Vector3f(0f, 2f, 0f));

        int sparse = MeshCardBaker.Bake(vertices, indices, texelsPerUnit: 4f)[0].ResolutionU;
        int dense = MeshCardBaker.Bake(vertices, indices, texelsPerUnit: 8f)[0].ResolutionU;

        Assert.Equal(8, sparse);
        Assert.Equal(16, dense);
    }

    /// <summary>A model whose triangles are all degenerate still gets a card, so it is not invisible.</summary>
    /// <remarks>
    /// This game's models contain collapsed quads left behind by the original's tooling, and a model
    /// built entirely of them still occupies space. No card at all means no entry in the surface cache,
    /// which at trace time is indistinguishable from the object not being there.
    /// </remarks>
    [Fact]
    public void AModelOfDegenerateTrianglesStillGetsOneCard()
    {
        Vertex3D[] vertices =
        [
            Vertex(new Vector3f(0f, 0f, 0f)),
            Vertex(new Vector3f(1f, 0f, 0f)),
            Vertex(new Vector3f(2f, 0f, 0f)),
        ];

        // Collinear, so the cross product is zero and no direction accumulates any area at all.
        IReadOnlyList<MeshCard> cards = MeshCardBaker.Bake(vertices, [0, 1, 2]);

        MeshCard only = Assert.Single(cards);

        Assert.True(only.AreaShare > 0f, "the fallback card reports describing nothing, so it is droppable.");
        Assert.True(only.Depth > 0f, "the fallback card has a degenerate orthographic range.");
    }

    /// <summary>Raising the threshold drops the directions that describe least, in that order.</summary>
    /// <remarks>
    /// A wall ten units long, three high and a fifth thick. Its two broad faces carry almost all the
    /// area; its top and bottom edges carry two square units each, which is small but real; its two ends
    /// carry well under one. Raising the bar past each in turn is what shows the share is measuring
    /// something rather than merely being present.
    /// </remarks>
    [Fact]
    public void RaisingTheThresholdDropsTheLeastRepresentedDirectionsFirst()
    {
        (Vertex3D[] vertices, ushort[] indices) = Box(new Vector3f(10f, 0.2f, 3f));

        MeshCardAxis[] generous = Axes(MeshCardBaker.Bake(vertices, indices, minimumAreaShare: 0.001f));
        MeshCardAxis[] standard = Axes(MeshCardBaker.Bake(vertices, indices));
        MeshCardAxis[] strict = Axes(MeshCardBaker.Bake(vertices, indices, minimumAreaShare: 0.2f));

        Assert.Equal(6, generous.Length);

        // The ends of the wall go first: they are a fifth of a unit by three.
        MeshCardAxis[] withoutEnds =
        [
            MeshCardAxis.PositiveY,
            MeshCardAxis.NegativeY,
            MeshCardAxis.PositiveZ,
            MeshCardAxis.NegativeZ,
        ];

        Assert.Equal(withoutEnds, standard);

        // Then its top and bottom, leaving only the two faces anybody sees.
        MeshCardAxis[] broadFacesOnly = [MeshCardAxis.PositiveY, MeshCardAxis.NegativeY];

        Assert.Equal(broadFacesOnly, strict);
    }

    /// <summary>Each card's basis is right-handed about the direction it faces.</summary>
    /// <remarks>
    /// The invariant a capture depends on and no picture reveals: with <c>Right × Up</c> pointing away
    /// from the card instead of towards the viewer, the page comes out mirrored. The surfaces this game
    /// captures are tiling textures and lettering is rare, so a mirrored atlas would survive inspection
    /// indefinitely and show up only as light arriving from the wrong side of a room.
    /// </remarks>
    [Theory]
    [InlineData(MeshCardAxis.PositiveX)]
    [InlineData(MeshCardAxis.NegativeX)]
    [InlineData(MeshCardAxis.PositiveY)]
    [InlineData(MeshCardAxis.NegativeY)]
    [InlineData(MeshCardAxis.PositiveZ)]
    [InlineData(MeshCardAxis.NegativeZ)]
    public void TheCardBasisIsRightHanded(MeshCardAxis axis)
    {
        Vector3f right = MeshCard.RightOf(axis);
        Vector3f up = MeshCard.UpOf(axis);
        Vector3f cross = Vector3f.Cross(right, up);

        Assert.Equal(MeshCard.DirectionOf(axis), cross);
        Assert.Equal(0f, Dot(right, up), 0.001f);
    }

    /// <summary>Opposite directions are opposite, which the enum's numbering is arranged to make true.</summary>
    [Theory]
    [InlineData(MeshCardAxis.PositiveX, MeshCardAxis.NegativeX)]
    [InlineData(MeshCardAxis.PositiveY, MeshCardAxis.NegativeY)]
    [InlineData(MeshCardAxis.PositiveZ, MeshCardAxis.NegativeZ)]
    public void OppositeAxesFaceOppositeWays(MeshCardAxis axis, MeshCardAxis opposite)
    {
        Assert.Equal(opposite, MeshCard.Opposite(axis));
        Assert.Equal(axis, MeshCard.Opposite(opposite));
        Assert.Equal(-1f, Dot(MeshCard.DirectionOf(axis), MeshCard.DirectionOf(opposite)), 0.001f);
    }

    /// <summary>A texel density that cannot produce a card is refused rather than silently corrected.</summary>
    [Fact]
    public void ANonPositiveTexelDensityIsRefused()
    {
        (Vertex3D[] vertices, ushort[] indices) = Cube(1f);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => MeshCardBaker.Bake(vertices, indices, texelsPerUnit: 0f));
    }

    /// <summary>The overload taking a whole model agrees with the one taking its buffers.</summary>
    [Fact]
    public void BakingFromGeometryMatchesBakingFromBuffers()
    {
        (Vertex3D[] vertices, ushort[] indices) = Cube(2f);
        MeshGeometry geometry = new(vertices, indices, []);

        Assert.Equal(MeshCardBaker.Bake(vertices, indices), MeshCardBaker.Bake(geometry));
    }

    /// <summary>The axes of a bake, in the order it emitted them.</summary>
    private static MeshCardAxis[] Axes(IReadOnlyList<MeshCard> cards) =>
        [.. cards.Select(card => card.Axis)];

    /// <summary>The resolution a square panel of a given size is given on both of its in-plane axes.</summary>
    private static int SquareCardResolution(float size)
    {
        (Vertex3D[] vertices, ushort[] indices) = Quad(
            new Vector3f(0f, 0f, 0f),
            new Vector3f(size, 0f, 0f),
            new Vector3f(size, size, 0f),
            new Vector3f(0f, size, 0f));

        return MeshCardBaker.Bake(vertices, indices)[0].ResolutionU;
    }

    /// <summary>A single quad, wound so that its normal follows the right-hand rule about a, b, c.</summary>
    private static (Vertex3D[] Vertices, ushort[] Indices) Quad(
        Vector3f a, Vector3f b, Vector3f c, Vector3f d)
    {
        List<Vertex3D> vertices = [];
        List<ushort> indices = [];

        AddQuad(vertices, indices, a, b, c, d);

        return ([.. vertices], [.. indices]);
    }

    /// <summary>An axis-aligned box from the origin, wound outwards.</summary>
    private static (Vertex3D[] Vertices, ushort[] Indices) Box(Vector3f size)
    {
        List<Vertex3D> vertices = [];
        List<ushort> indices = [];

        Vector3f p000 = new(0f, 0f, 0f);
        Vector3f p100 = new(size.X, 0f, 0f);
        Vector3f p110 = new(size.X, size.Y, 0f);
        Vector3f p010 = new(0f, size.Y, 0f);
        Vector3f p001 = new(0f, 0f, size.Z);
        Vector3f p101 = new(size.X, 0f, size.Z);
        Vector3f p111 = new(size.X, size.Y, size.Z);
        Vector3f p011 = new(0f, size.Y, size.Z);

        AddQuad(vertices, indices, p001, p101, p111, p011);
        AddQuad(vertices, indices, p000, p010, p110, p100);
        AddQuad(vertices, indices, p100, p110, p111, p101);
        AddQuad(vertices, indices, p000, p001, p011, p010);
        AddQuad(vertices, indices, p010, p011, p111, p110);
        AddQuad(vertices, indices, p000, p100, p101, p001);

        return ([.. vertices], [.. indices]);
    }

    /// <summary>A cube of a given side, from the origin.</summary>
    private static (Vertex3D[] Vertices, ushort[] Indices) Cube(float side) =>
        Box(new Vector3f(side, side, side));

    /// <summary>Appends a quad as two triangles.</summary>
    private static void AddQuad(
        List<Vertex3D> vertices, List<ushort> indices, Vector3f a, Vector3f b, Vector3f c, Vector3f d)
    {
        ushort first = (ushort)vertices.Count;

        // The geometric normal, given to every vertex. The baker does not read it -- it takes the
        // winding -- but a mesh whose stored normals contradicted its winding would be a poor witness
        // for anything downstream.
        Vector3f normal = Vector3f.Cross(b - a, c - a).Normalized();

        vertices.Add(Vertex(a, normal));
        vertices.Add(Vertex(b, normal));
        vertices.Add(Vertex(c, normal));
        vertices.Add(Vertex(d, normal));

        indices.AddRange([first, (ushort)(first + 1), (ushort)(first + 2)]);
        indices.AddRange([first, (ushort)(first + 2), (ushort)(first + 3)]);
    }

    /// <summary>A vertex at a position, with everything the baker ignores left at zero.</summary>
    private static Vertex3D Vertex(Vector3f position, Vector3f normal = default) =>
        new(position, normal, 0f, 0f);

    /// <summary>The dot product of two vectors.</summary>
    private static float Dot(Vector3f a, Vector3f b) => Vector3f.Dot(a, b);
}
