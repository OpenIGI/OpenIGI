using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Presentation.Rendering;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>
/// The sky dome's lattice and its shading, which both games build the same way.
/// </summary>
/// <remarks>
/// Worth pinning at the shared level rather than per title, because the claim these tests stand on is
/// precisely that there is one dome: the arithmetic below closes on a byte count taken from I.G.I. 2's
/// binary, and it has to hold for I.G.I. 1's radius too or the two were never the same lattice.
/// </remarks>
public sealed class SkyDomeGeometryTests
{
    /// <summary>The lattice is the size the original locks a buffer for.</summary>
    /// <remarks>
    /// <c>SkyDome_InitialiseVertexBuffer</c> locks <b>5,136 bytes</b> at sixteen bytes a vertex — a
    /// position and a packed colour — which is 321 vertices: an apex, fifteen rings of twenty, and a
    /// skirt of twenty. Arithmetic that closes on the byte, so a lattice of any other size is wrong.
    /// </remarks>
    [Fact]
    public void The_lattice_is_the_size_the_original_allocates()
    {
        Assert.Equal(321, SkyDomeGeometry.VertexCount);
        Assert.Equal(5136, SkyDomeGeometry.VertexCount * 16);

        // Fifteen rings six degrees apart start six degrees below the apex and land on the horizon.
        Assert.Equal(MathF.PI / 2f, SkyDomeGeometry.RingCount * SkyDomeGeometry.RingStep, 5);

        // Twenty vertices eighteen degrees apart close the circle.
        Assert.Equal(MathF.Tau, SkyDomeGeometry.RingVertexCount * SkyDomeGeometry.VertexStep, 5);
    }

    /// <summary>The dome reaches its radius at the apex and the horizon, and hangs below it.</summary>
    /// <remarks>
    /// Run at both games' radii. I.G.I. 1 sizes the dome to <c>0.8 ×</c> a far plane in metres and
    /// I.G.I. 2 fixes it at 8,000 engine units — two numbers three orders of magnitude apart, which is
    /// the point: the lattice is a direction field, and the radius only names the units.
    /// </remarks>
    [Theory]
    [InlineData(8000f)]
    [InlineData(160f)]
    [InlineData(1f)]
    public void The_dome_spans_its_radius_and_hangs_below_the_horizon(float radius)
    {
        Vector3f grey = new(0.5f, 0.5f, 0.5f);
        (Vector3f[] positions, Bgra32[] colours) = SkyDomeGeometry.Build(
            radius,
            30f * MathF.PI / 180f,
            [grey, grey, grey, grey, grey, grey]);

        Assert.Equal(SkyDomeGeometry.VertexCount, positions.Length);
        Assert.Equal(SkyDomeGeometry.VertexCount, colours.Length);

        // The apex is straight up at the radius.
        Assert.Equal(new Vector3f(0f, 0f, radius), positions[0]);

        // The last ring sits on the horizon, at the radius, all the way round.
        for (int i = 0; i < SkyDomeGeometry.RingVertexCount; i++)
        {
            Vector3f vertex = positions[SkyDomeGeometry.HorizonRingStart + i];
            Assert.Equal(0f, vertex.Z, 3);
            Assert.Equal(radius, MathF.Sqrt((vertex.X * vertex.X) + (vertex.Y * vertex.Y)), 2);
        }

        // And the skirt hangs straight down from it, at half the radius.
        for (int i = 0; i < SkyDomeGeometry.RingVertexCount; i++)
        {
            Vector3f skirt = positions[SkyDomeGeometry.SkirtStart + i];
            Assert.Equal(radius * SkyDomeGeometry.SkirtFraction, skirt.Z, 3);
            Assert.Equal(positions[SkyDomeGeometry.HorizonRingStart + i].X, skirt.X, 3);
            Assert.Equal(positions[SkyDomeGeometry.HorizonRingStart + i].Y, skirt.Y, 3);
        }
    }

    /// <summary>Every triangle addresses a vertex that exists, and there are as many as declared.</summary>
    /// <remarks>
    /// The lattice is fixed, so the index list is a constant — and a constant that overran the vertex
    /// array would take the device down rather than draw wrongly, which is the kind of failure a fact
    /// this cheap should never let through.
    /// </remarks>
    [Fact]
    public void The_indices_cover_the_lattice_without_leaving_it()
    {
        ushort[] indices = SkyDomeGeometry.BuildIndices();

        Assert.Equal(SkyDomeGeometry.IndexCount, indices.Length);
        Assert.Equal(0, indices.Length % 3);
        Assert.All(indices, index => Assert.InRange(index, 0, SkyDomeGeometry.VertexCount - 1));

        // The apex fan, then a band per ring: every vertex but the apex belongs to two triangles of its
        // own band, so nothing in the lattice goes undrawn.
        Assert.Equal(SkyDomeGeometry.VertexCount, indices.Distinct().Count());
    }

    /// <summary>The shading clamps rather than wrapping.</summary>
    /// <remarks>
    /// Where an over-bright authored colour is dealt with. I.G.I. 2's corpus reaches 1.0385 and the
    /// engine carries it unchanged through the parameter, flattening it at vertex time — so a blend that
    /// clamped nowhere would overflow a byte and a bright sky would come out dark.
    /// </remarks>
    [Fact]
    public void An_over_bright_colour_clamps_to_white()
    {
        Vector3f bright = new(1.0385f, 1.0385f, 1.0385f);
        Bgra32 shade = SkyDomeGeometry.Shade(bright, bright, bright, bright, 0.5f, 0.5f);

        Assert.Equal(255, shade.R);
        Assert.Equal(255, shade.G);
        Assert.Equal(255, shade.B);
        Assert.Equal(255, shade.A);

        Vector3f negative = new(-1f, -1f, -1f);
        Bgra32 dark = SkyDomeGeometry.Shade(negative, negative, negative, negative, 0.5f, 0.5f);
        Assert.Equal(0, dark.R);
        Assert.Equal(0, dark.G);
        Assert.Equal(0, dark.B);
    }

    /// <summary>Each pair is a north and a south, and the blend runs between them round the compass.</summary>
    /// <remarks>
    /// The half of the claim that says the second colour of each pair is used at all. A dome that read
    /// only one would be uniform at every heading and no test of its size or its shape would notice.
    /// </remarks>
    [Fact]
    public void A_ring_varies_with_heading_when_its_pair_differs()
    {
        Vector3f white = new(1f, 1f, 1f);
        Vector3f red = new(1f, 0f, 0f);
        Vector3f blue = new(0f, 0f, 1f);
        Vector3f green = new(0f, 1f, 0f);
        Vector3f cyan = new(0f, 1f, 1f);

        (_, Bgra32[] varied) = SkyDomeGeometry.Build(
            8000f,
            30f * MathF.PI / 180f,
            [white, white, blue, red, cyan, green]);

        Bgra32 north = varied[SkyDomeGeometry.HorizonRingStart];
        Bgra32 south = varied[SkyDomeGeometry.HorizonRingStart + (SkyDomeGeometry.RingVertexCount / 2)];
        Assert.NotEqual(north, south);

        // Collapse each pair — which is what a level's Snap Colours does — and the ring goes flat.
        (_, Bgra32[] snapped) = SkyDomeGeometry.Build(
            8000f,
            30f * MathF.PI / 180f,
            [white, white, blue, blue, cyan, cyan]);

        for (int i = 1; i < SkyDomeGeometry.RingVertexCount; i++)
        {
            Assert.Equal(
                snapped[SkyDomeGeometry.HorizonRingStart],
                snapped[SkyDomeGeometry.HorizonRingStart + i]);
        }
    }

    /// <summary>A colour table of the wrong size is refused rather than read past.</summary>
    [Fact]
    public void A_short_colour_table_is_refused()
    {
        Vector3f grey = new(0.5f, 0.5f, 0.5f);
        Assert.Throws<ArgumentException>(() => SkyDomeGeometry.Build(8000f, 0.5f, [grey, grey, grey]));
    }
}
