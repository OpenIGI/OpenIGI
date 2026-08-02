using Xunit;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Tests for <see cref="LightFixtureExtractor"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every test here builds its triangles by hand rather than loading a level, and that is the reason the
/// extractor takes a mesh and a transform instead of a <see cref="WorldPlacement"/>: a placement names a
/// <c>MefModel</c>, which only a real pack file can construct. The scene walk left over once that split
/// is made contains no arithmetic and is not tested here.
/// </para>
/// <para>
/// What is worth pinning is the pair of failures that a proposed light list can suffer without looking
/// wrong. Merge two lamps and you get one light hovering in the middle of a room, which reads perfectly
/// plausibly in a list of numbers. Split one lamp and you get a dozen coincident lights, which costs a
/// dozen times what it should and still lights the room. Neither shows up as an exception, so both are
/// asserted directly.
/// </para>
/// </remarks>
public sealed class LightFixtureExtractorTests
{
    /// <summary>Nothing to walk proposes nothing, rather than a fixture at the world origin.</summary>
    [Fact]
    public void AnEmptyInputProposesNoFixtures() =>
        Assert.Empty(LightFixtureExtractor.Propose([], ["lamp"]));

    /// <summary>Geometry that draws no emitter texture proposes nothing.</summary>
    /// <remarks>
    /// The common case by a wide margin — a level is thousands of surfaces and a handful of lamps — so
    /// a leak here would fill the list with concrete.
    /// </remarks>
    [Fact]
    public void GeometryThatDrawsNoEmitterTextureProposesNoFixtures() =>
        Assert.Empty(LightFixtureExtractor.Propose([Mesh("concrete", Quad(default, 0.5f))], ["lamp"]));

    /// <summary>An emitter set with nothing in it proposes nothing, whatever the geometry draws.</summary>
    [Fact]
    public void AnEmptyEmitterSetProposesNoFixtures() =>
        Assert.Empty(LightFixtureExtractor.Propose([Mesh("lamp", Quad(default, 0.5f))], []));

    /// <summary>The many triangles of one lamp are one fixture, not one fixture each.</summary>
    [Fact]
    public void TheManyTrianglesOfOneLampBecomeASingleFixture()
    {
        // A lamp housing, tessellated the way a modelled one is: several quads sharing a corner of
        // space, none of them more than a few centimetres from its neighbour.
        List<(Vector3f A, Vector3f B, Vector3f C)> housing =
        [
            .. Quad(new Vector3f(0f, 0f, 0f), 0.2f),
            .. Quad(new Vector3f(0.1f, 0f, 0f), 0.2f),
            .. Quad(new Vector3f(0f, 0.15f, 0f), 0.2f),
        ];

        LightFixture fixture = Assert.Single(
            LightFixtureExtractor.Propose([Mesh("lamp", housing)], ["lamp"]));

        Assert.Equal(6, fixture.TriangleCount);
        Assert.Equal("lamp", fixture.Texture);
    }

    /// <summary>Two lamps facing each other across a room stay two lights, not one between them.</summary>
    /// <remarks>
    /// The failure this exists to catch is silent: a single fixture averaged from both walls sits in
    /// mid-air with a plausible position, a plausible radius and twice the area, and nothing in the
    /// output says it is wrong.
    /// </remarks>
    [Fact]
    public void TwoLampsOnOppositeWallsDoNotMergeIntoOneLightInTheMiddle()
    {
        List<(Vector3f A, Vector3f B, Vector3f C)> walls =
        [
            .. Quad(new Vector3f(-3f, 0f, 2f), 0.25f),
            .. Quad(new Vector3f(3f, 0f, 2f), 0.25f),
        ];

        IReadOnlyList<LightFixture> fixtures =
            LightFixtureExtractor.Propose([Mesh("lamp", walls)], ["lamp"]);

        Assert.Equal(2, fixtures.Count);
        Assert.All(fixtures, f => Assert.True(
            Math.Abs(f.Position.X) > 2.5,
            $"a fixture landed at x={f.Position.X:0.##}, which is between the two lamps rather than on one."));
    }

    /// <summary>A fixture longer than the joining distance is still one fixture.</summary>
    /// <remarks>
    /// Joining is transitive, which is what makes the threshold a statement about the gaps inside a
    /// fixture rather than about how big a fixture may be. A five-metre strip light with a quad every
    /// metre would otherwise come out as five lights in a row.
    /// </remarks>
    [Fact]
    public void AFixtureLongerThanTheJoiningDistanceStaysWhole()
    {
        List<(Vector3f A, Vector3f B, Vector3f C)> strip = [];

        for (int step = 0; step <= 5; step++)
        {
            strip.AddRange(Quad(new Vector3f(step, 0f, 0f), 0.1f));
        }

        LightFixture fixture = Assert.Single(
            LightFixtureExtractor.Propose([Mesh("lamp", strip)], ["lamp"]));

        Assert.Equal(12, fixture.TriangleCount);
        Assert.Equal(2.5, fixture.Position.X, 1e-6);
    }

    /// <summary>The fixture stands where the object stands, not where the model was authored.</summary>
    [Fact]
    public void AFixtureSitsAtTheCentroidOfItsTrianglesInWorldSpace()
    {
        LightFixture fixture = Assert.Single(LightFixtureExtractor.Propose(
            [Mesh("lamp", Quad(default, 0.3f), new Vector3d(10.0, -20.0, 30.0))],
            ["lamp"]));

        Assert.Equal(10.0, fixture.Position.X, 1e-6);
        Assert.Equal(-20.0, fixture.Position.Y, 1e-6);
        Assert.Equal(30.0, fixture.Position.Z, 1e-6);
    }

    /// <summary>A turned object's lamp turns with it.</summary>
    /// <remarks>
    /// The transform has to be the one the renderer draws with, or the proposed light sits somewhere the
    /// player can see the lamp is not. A quarter turn about the vertical is the case where getting the
    /// transpose backwards is visible rather than symmetric: a lamp a metre along the model's X axis
    /// ends up a metre along world Y, and the wrong composition puts it a metre along −Y.
    /// </remarks>
    [Fact]
    public void APlacementsRotationIsAppliedToTheFixturesPosition()
    {
        LightFixture fixture = Assert.Single(LightFixtureExtractor.Propose(
            [
                Mesh(
                    "lamp",
                    Quad(new Vector3f(1f, 0f, 0f), 0.2f),
                    default,
                    Matrix3f.RotationZ(MathF.PI / 2f)),
            ],
            ["lamp"]));

        Assert.Equal(0.0, fixture.Position.X, 1e-5);
        Assert.Equal(1.0, fixture.Position.Y, 1e-5);
        Assert.Equal(0.0, fixture.Position.Z, 1e-5);
    }

    /// <summary>A large triangle pulls the centroid more than a sliver does.</summary>
    /// <remarks>
    /// Lamps are tessellated unevenly, and an unweighted mean of triangle centroids would let a rim of
    /// slivers outvote the panel they surround. Here the areas are 0.5 and 2, so the weighted centroid
    /// sits four fifths of the way towards the larger triangle at <c>x = 4.8</c>; an unweighted one
    /// would sit halfway, at 3.
    /// </remarks>
    [Fact]
    public void TheCentroidIsWeightedByAreaSoASliverDoesNotOutvoteAPanel()
    {
        List<(Vector3f A, Vector3f B, Vector3f C)> uneven =
        [
            (new Vector3f(0f, 0f, 0f), new Vector3f(0f, 1f, 0f), new Vector3f(0f, 0f, 1f)),
            (new Vector3f(6f, 0f, 0f), new Vector3f(6f, 2f, 0f), new Vector3f(6f, 0f, 2f)),
        ];

        LightFixture fixture = Assert.Single(LightFixtureExtractor.Propose(
            [Mesh("lamp", uneven)],
            ["lamp"],
            clusterDistance: 10f));

        Assert.Equal(4.8, fixture.Position.X, 1e-6);
        Assert.Equal(2.5f, fixture.Area, 1e-5f);
    }

    /// <summary>A bigger fixture is proposed to light further.</summary>
    [Fact]
    public void TheRadiusGrowsWithTheClustersExtent()
    {
        LightFixture small = Assert.Single(
            LightFixtureExtractor.Propose([Mesh("lamp", Quad(default, 0.25f))], ["lamp"]));
        LightFixture large = Assert.Single(
            LightFixtureExtractor.Propose([Mesh("lamp", Quad(default, 0.6f))], ["lamp"]));

        Assert.True(large.Extent > small.Extent, "the larger quad did not measure as larger.");
        Assert.True(
            large.Radius > small.Radius * 2f,
            $"a quad six times the area reaches {large.Radius:0.##}m against {small.Radius:0.##}m.");
    }

    /// <summary>However small the geometry, a proposed light still lights something.</summary>
    /// <remarks>
    /// A lamp modelled as one small quad would otherwise be given a radius smaller than its own housing,
    /// and a reviewer reading the list would conclude the extractor had missed it.
    /// </remarks>
    [Fact]
    public void ATinyFixtureStillGetsTheMinimumRadius()
    {
        LightFixture fixture = Assert.Single(
            LightFixtureExtractor.Propose([Mesh("lamp", Quad(default, 0.01f))], ["lamp"]));

        Assert.Equal(LightFixtureExtractor.MinimumRadius, fixture.Radius);
    }

    /// <summary>A dimmer emitter is proposed to reach less far, by the square root of its luminance.</summary>
    /// <remarks>
    /// Cutting an inverse-square falloff off at a fixed threshold puts the cut-off distance at the root
    /// of the emitted power, so a quarter of the brightness is half the reach. Asserted because it is
    /// the one part of the radius formula that follows from something rather than being chosen.
    /// </remarks>
    [Fact]
    public void ADimmerColourReachesLessFar()
    {
        LightFixture bright = Assert.Single(LightFixtureExtractor.Propose(
            [Mesh("lamp", Quad(default, 0.5f))], ["lamp"], _ => new Vector3f(1f, 1f, 1f)));
        LightFixture dim = Assert.Single(LightFixtureExtractor.Propose(
            [Mesh("lamp", Quad(default, 0.5f))], ["lamp"], _ => new Vector3f(0.25f, 0.25f, 0.25f)));

        Assert.Equal(bright.Radius / 2f, dim.Radius, 1e-3f);
    }

    /// <summary>A fixture carries the emitter's colour and says which texture produced it.</summary>
    [Fact]
    public void AFixtureCarriesItsEmittersColourAndName()
    {
        LightFixture fixture = Assert.Single(LightFixtureExtractor.Propose(
            [Mesh("236_01_1", Quad(default, 0.3f))],
            ["236_01_1"],
            texture => texture == "236_01_1" ? new Vector3f(1f, 0.8f, 0.5f) : new Vector3f(1f, 0f, 1f)));

        Assert.Equal("236_01_1", fixture.Texture);
        Assert.Equal(1f, fixture.Colour.X);
        Assert.Equal(0.8f, fixture.Colour.Y);
        Assert.Equal(0.5f, fixture.Colour.Z);
    }

    /// <summary>Emitter names are matched however the pack happens to have cased them.</summary>
    [Fact]
    public void EmitterNamesAreMatchedWithoutRegardToCase() =>
        Assert.Single(LightFixtureExtractor.Propose([Mesh("Lamp_01", Quad(default, 0.3f))], ["lamp_01"]));

    /// <summary>The strongest proposal comes first, because the list is read top-down.</summary>
    [Fact]
    public void FixturesAreOrderedWithTheStrongestFirst()
    {
        List<(Vector3f A, Vector3f B, Vector3f C)> pair =
        [
            .. Quad(new Vector3f(-10f, 0f, 0f), 0.2f),
            .. Quad(new Vector3f(10f, 0f, 0f), 1.5f),
        ];

        IReadOnlyList<LightFixture> fixtures =
            LightFixtureExtractor.Propose([Mesh("lamp", pair)], ["lamp"]);

        Assert.Equal(2, fixtures.Count);
        Assert.True(fixtures[0].Intensity > fixtures[1].Intensity, "the weaker fixture was listed first.");
        Assert.Equal(10.0, fixtures[0].Position.X, 1e-6);
    }

    /// <summary>Triangles from separately placed objects still cluster by where they ended up.</summary>
    /// <remarks>
    /// Two instances of the same lamp model stand in different places, so the grouping has to happen
    /// after the transform rather than per mesh — otherwise every placement of a model is one fixture
    /// however far apart the placements are.
    /// </remarks>
    [Fact]
    public void ClusteringHappensAfterPlacementRatherThanPerMesh()
    {
        IReadOnlyList<LightFixture> fixtures = LightFixtureExtractor.Propose(
            [
                Mesh("lamp", Quad(default, 0.2f), new Vector3d(0.0, 0.0, 0.0)),
                Mesh("lamp", Quad(default, 0.2f), new Vector3d(0.4, 0.0, 0.0)),
                Mesh("lamp", Quad(default, 0.2f), new Vector3d(40.0, 0.0, 0.0)),
            ],
            ["lamp"]);

        // The two placements 0.4m apart are one lamp; the one forty metres away is another.
        Assert.Equal(2, fixtures.Count);
        Assert.Contains(fixtures, f => f.TriangleCount == 4);
        Assert.Contains(fixtures, f => f.TriangleCount == 2);
    }

    /// <summary>A missing argument is refused rather than treated as an empty level.</summary>
    [Fact]
    public void MissingArgumentsAreRefused()
    {
        Assert.Throws<ArgumentNullException>(() => LightFixtureExtractor.Propose(null!, ["lamp"]));
        Assert.Throws<ArgumentNullException>(
            () => LightFixtureExtractor.Propose([], null!));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LightFixtureExtractor.Propose([], ["lamp"], clusterDistance: 0f));
    }

    /// <summary>Builds one placed mesh whose every triangle draws with the same texture.</summary>
    private static LightFixtureExtractor.PlacedMesh Mesh(
        string texture,
        IReadOnlyList<(Vector3f A, Vector3f B, Vector3f C)> triangles,
        Vector3d position = default,
        Matrix3f? orientation = null)
    {
        List<Vertex3D> vertices = [];
        List<ushort> indices = [];

        foreach ((Vector3f a, Vector3f b, Vector3f c) in triangles)
        {
            // Unshared vertices: the extractor reads positions only, and a flat buffer keeps the
            // expected centroid something a reader can work out from the test's own numbers.
            Vector3f[] corners = [a, b, c];

            foreach (Vector3f corner in corners)
            {
                indices.Add((ushort)vertices.Count);
                vertices.Add(new Vertex3D(corner, new Vector3f(0f, 0f, 1f), 0f, 0f));
            }
        }

        MeshGeometry geometry = new(
            [.. vertices],
            [.. indices],
            [new MeshSubset(0, indices.Count, 0, 0)]);

        return new LightFixtureExtractor.PlacedMesh(
            geometry,
            position,
            orientation ?? Matrix3f.Identity,
            _ => texture);
    }

    /// <summary>Two triangles forming a square in the XY plane, centred on a point.</summary>
    private static (Vector3f A, Vector3f B, Vector3f C)[] Quad(Vector3f centre, float half) =>
    [
        (new Vector3f(centre.X - half, centre.Y - half, centre.Z),
         new Vector3f(centre.X + half, centre.Y - half, centre.Z),
         new Vector3f(centre.X + half, centre.Y + half, centre.Z)),
        (new Vector3f(centre.X - half, centre.Y - half, centre.Z),
         new Vector3f(centre.X + half, centre.Y + half, centre.Z),
         new Vector3f(centre.X - half, centre.Y + half, centre.Z)),
    ];
}
