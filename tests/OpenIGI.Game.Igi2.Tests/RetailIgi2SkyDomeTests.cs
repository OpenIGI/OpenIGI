using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The sky dome's lattice and its shading.</summary>
public sealed class RetailIgi2SkyDomeTests
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
        Assert.Equal(321, Igi2SkyDome.VertexCount);
        Assert.Equal(5136, Igi2SkyDome.VertexCount * 16);

        // Fifteen rings six degrees apart start six degrees below the apex and land on the horizon.
        Assert.Equal(MathF.PI / 2f, Igi2SkyDome.RingCount * Igi2SkyDome.RingStep, 5);

        // Twenty vertices eighteen degrees apart close the circle.
        Assert.Equal(MathF.Tau, Igi2SkyDome.RingVertexCount * Igi2SkyDome.VertexStep, 5);
    }

    /// <summary>The dome reaches its radius at the apex and the horizon, and hangs below it.</summary>
    [Fact]
    public void The_dome_spans_its_radius_and_hangs_below_the_horizon()
    {
        Vector3f grey = new(0.5f, 0.5f, 0.5f);
        (Vector3f[] positions, Bgra32[] colours) = Igi2SkyDome.Build(
            [grey, grey, grey, grey, grey, grey],
            Igi2SkyDome.HalfAngleOf(30f));

        Assert.Equal(Igi2SkyDome.VertexCount, positions.Length);
        Assert.Equal(Igi2SkyDome.VertexCount, colours.Length);

        // The apex is straight up at the radius.
        Assert.Equal(new Vector3f(0f, 0f, Igi2SkyDome.Radius), positions[0]);

        // The last ring sits on the horizon, at the radius, all the way round.
        int lastRing = 1 + ((Igi2SkyDome.RingCount - 1) * Igi2SkyDome.RingVertexCount);
        for (int i = 0; i < Igi2SkyDome.RingVertexCount; i++)
        {
            Vector3f vertex = positions[lastRing + i];
            Assert.Equal(0f, vertex.Z, 2);
            Assert.Equal(Igi2SkyDome.Radius, MathF.Sqrt((vertex.X * vertex.X) + (vertex.Y * vertex.Y)), 1);
        }

        // And the skirt hangs straight down from it.
        int skirt = Igi2SkyDome.VertexCount - Igi2SkyDome.RingVertexCount;
        for (int i = 0; i < Igi2SkyDome.RingVertexCount; i++)
        {
            Assert.Equal(Igi2SkyDome.SkirtDepth, positions[skirt + i].Z);
            Assert.Equal(positions[lastRing + i].X, positions[skirt + i].X, 2);
        }
    }

    /// <summary>Every level's sky shades without a channel leaving the byte range.</summary>
    /// <remarks>
    /// <para>
    /// The shading clamps, and this is where that matters: the corpus authors a colour of 1.0385, which
    /// the parameter carries unchanged and <c>SkyDome_InterpolateVertexColour</c> flattens to white at
    /// vertex time. A reader that clamped on the way in would agree with the original by accident; one
    /// that clamped nowhere would overflow a byte here.
    /// </para>
    /// <para>
    /// Built for every shipped level, because the band selection depends on each one's own angle and a
    /// level whose angle sat outside the rings would divide by nothing.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_levels_sky_shades_without_overflowing()
    {
        int skies = 0;
        int snapped = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            foreach (GameTask task in Walk(level.Root))
            {
                if (task is not FlatSkyTask sky)
                {
                    continue;
                }

                skies++;
                snapped += sky.SnapColours ? 1 : 0;

                Vector3f[] table = Igi2SkyDome.ColourTableFor(sky);
                Assert.Equal(6, table.Length);

                // The top is duplicated, which is what makes the apex a flat colour.
                Assert.Equal(sky.TopColour, table[0]);
                Assert.Equal(sky.TopColour, table[1]);

                (Vector3f[] positions, Bgra32[] colours) = Igi2SkyDome.Build(
                    table,
                    Igi2SkyDome.HalfAngleOf(sky.DomeAngle));

                Assert.Equal(Igi2SkyDome.VertexCount, positions.Length);
                Assert.All(colours, colour => Assert.Equal(255, colour.A));

                // The apex is the top colour, clamped.
                Assert.Equal(Clamp(sky.TopColour.X), colours[0].R);
                Assert.Equal(Clamp(sky.TopColour.Y), colours[0].G);
                Assert.Equal(Clamp(sky.TopColour.Z), colours[0].B);
            }
        }

        Assert.Equal(24, skies);

        // Recorded rather than asserted either way: snapping is what makes a pair one colour, and how
        // many levels want a horizon that does not vary with heading is a fact about the corpus.
        Assert.InRange(snapped, 0, 24);
    }

    /// <summary>Snapping the colours removes the blend round the compass.</summary>
    /// <remarks>
    /// The handler copies Middle 2 over Middle 1 and Bottom 2 over Bottom 1 before the table is built,
    /// so each pair collapses. A dome built from a snapped sky is the same colour at every heading.
    /// </remarks>
    [Fact]
    public void Snapping_makes_each_pair_one_colour()
    {
        FlatSkyTask sky = new()
        {
            TopColour = new Vector3f(1f, 1f, 1f),
            MiddleColour1 = new Vector3f(1f, 0f, 0f),
            MiddleColour2 = new Vector3f(0f, 0f, 1f),
            BottomColour1 = new Vector3f(0f, 1f, 0f),
            BottomColour2 = new Vector3f(0f, 1f, 1f),
            DomeAngle = 30f,
            SnapColours = true,
        };

        Vector3f[] table = Igi2SkyDome.ColourTableFor(sky);
        Assert.Equal(sky.MiddleColour2, table[2]);
        Assert.Equal(sky.MiddleColour2, table[3]);
        Assert.Equal(sky.BottomColour2, table[4]);
        Assert.Equal(sky.BottomColour2, table[5]);

        // Which makes a ring flat: the triangle wave round the compass has nothing to blend between.
        (_, Bgra32[] colours) = Igi2SkyDome.Build(table, Igi2SkyDome.HalfAngleOf(sky.DomeAngle));
        int ring = 1 + (Igi2SkyDome.RingCount - 1) * Igi2SkyDome.RingVertexCount;
        for (int i = 1; i < Igi2SkyDome.RingVertexCount; i++)
        {
            Assert.Equal(colours[ring], colours[ring + i]);
        }
    }

    /// <summary>Without snapping, a ring does vary with heading.</summary>
    /// <remarks>
    /// The other half of the same claim: the two middle and two bottom colours are a north and a south,
    /// and a dome that ignored one would be uniform. This is what says the pair is used.
    /// </remarks>
    [Fact]
    public void Without_snapping_a_ring_varies_with_heading()
    {
        FlatSkyTask sky = new()
        {
            TopColour = new Vector3f(1f, 1f, 1f),
            MiddleColour1 = new Vector3f(1f, 0f, 0f),
            MiddleColour2 = new Vector3f(0f, 0f, 1f),
            BottomColour1 = new Vector3f(0f, 1f, 0f),
            BottomColour2 = new Vector3f(0f, 1f, 1f),
            DomeAngle = 30f,
            SnapColours = false,
        };

        (_, Bgra32[] colours) = Igi2SkyDome.Build(
            Igi2SkyDome.ColourTableFor(sky),
            Igi2SkyDome.HalfAngleOf(sky.DomeAngle));

        int ring = 1 + (Igi2SkyDome.RingCount - 1) * Igi2SkyDome.RingVertexCount;
        Bgra32 north = colours[ring];
        Bgra32 south = colours[ring + (Igi2SkyDome.RingVertexCount / 2)];

        Assert.NotEqual(north, south);
    }

    private static byte Clamp(float value) =>
        (byte)(value <= 0f ? 0 : value > 1f ? 255 : (int)(value * 255f));

    private static IEnumerable<GameTask> Walk(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            yield return child;

            foreach (GameTask descendant in Walk(child))
            {
                yield return descendant;
            }
        }
    }
}
