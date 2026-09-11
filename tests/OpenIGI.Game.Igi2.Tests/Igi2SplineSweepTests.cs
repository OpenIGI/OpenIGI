using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// The curve a spline segment bends its model along, and the shape that comes out of it.
/// </summary>
/// <remarks>
/// <para>
/// A spline segment is the only geometry in an I.G.I. 2 level that no task declares: the waypoints are
/// declared and the segments between them are made at load. So there is nothing to compare a segment
/// against except the curve's own definition, which is what these check — that it starts and ends where
/// the waypoints are, faces the way they face, and produces a model of a believable size.
/// </para>
/// <para>
/// That last one is not a formality. The first working version of the sweep divided a chord in engine
/// units by an extent in metres and produced roads four thousand times too long, which on screen is not
/// a misplaced road but a solid wall across the camera.
/// </para>
/// </remarks>
public sealed class Igi2SplineSweepTests
{
    /// <summary>The curve passes through the two waypoints it joins.</summary>
    /// <remarks>
    /// A cubic Hermite is defined to, and <c>SplineObjSegment_Update</c> (<c>igi2ed 0x611FB0</c>) stores
    /// every frame relative to the two waypoints' midpoint, so the first and last frames are the
    /// waypoints themselves seen from there.
    /// </remarks>
    [Fact]
    public void The_curve_ends_at_the_waypoints_it_joins()
    {
        Vector3d start = new(1000.0, 2000.0, 300.0);
        Vector3d stop = new(5000.0, 2400.0, 700.0);
        Igi2SplineSweep sweep = Igi2SplineSweep.Create(
            start,
            stop,
            Matrix3f.Identity,
            Matrix3f.Identity,
            areas: 20,
            linear: false,
            flipped: false);

        Assert.Equal(21, sweep.Frames.Count);
        Assert.Equal(3000.0, sweep.Midpoint.X, 3);
        Assert.Equal(2200.0, sweep.Midpoint.Y, 3);
        Assert.Equal(500.0, sweep.Midpoint.Z, 3);

        Igi2SplineFrame first = sweep.Frames[0];
        Assert.Equal(start.X - sweep.Midpoint.X, first.Position.X, 1);
        Assert.Equal(start.Y - sweep.Midpoint.Y, first.Position.Y, 1);
        Assert.Equal(start.Z - sweep.Midpoint.Z, first.Position.Z, 1);

        Igi2SplineFrame last = sweep.Frames[^1];
        Assert.Equal(stop.X - sweep.Midpoint.X, last.Position.X, 1);
        Assert.Equal(stop.Y - sweep.Midpoint.Y, last.Position.Y, 1);
        Assert.Equal(stop.Z - sweep.Midpoint.Z, last.Position.Z, 1);
    }

    /// <summary>The ends wear the waypoints' own facings rather than the curve's.</summary>
    /// <remarks>
    /// <c>SplineObjSegment_Update</c> copies the two orientations in whole at index zero and index
    /// <c>areas</c>, and only builds a frame from the tangent for the samples in between. Which is what
    /// makes two segments meeting at a waypoint line up: both take that waypoint's matrix verbatim.
    /// </remarks>
    [Fact]
    public void The_ends_wear_the_waypoints_own_facings()
    {
        Matrix3f entering = Matrix3f.FromEngineEulerAngles(0.25f, 0.1f, 0.75f);
        Matrix3f leaving = Matrix3f.FromEngineEulerAngles(-0.3f, 0.2f, 1.25f);
        Igi2SplineSweep sweep = Igi2SplineSweep.Create(
            new Vector3d(0.0, 0.0, 0.0),
            new Vector3d(4096.0, 0.0, 0.0),
            entering,
            leaving,
            areas: 8,
            linear: false,
            flipped: false);

        Assert.Equal(entering, sweep.Frames[0].Orientation);
        Assert.Equal(leaving, sweep.Frames[^1].Orientation);
        Assert.Equal(9, sweep.Frames.Count);
    }

    /// <summary>A straight segment is sampled once, however many areas it asked for.</summary>
    /// <remarks><c>SplineObjSegment_Update</c> overwrites the count with one as its first act.</remarks>
    [Fact]
    public void A_straight_segment_is_sampled_once()
    {
        Igi2SplineSweep sweep = Igi2SplineSweep.Create(
            new Vector3d(0.0, 0.0, 0.0),
            new Vector3d(4096.0, 4096.0, 0.0),
            Matrix3f.Identity,
            Matrix3f.Identity,
            areas: 20,
            linear: true,
            flipped: false);

        Assert.True(sweep.IsLinear);
        Assert.Equal(2, sweep.Frames.Count);
    }

    /// <summary>A direction with no eastward component keeps the world's own facing.</summary>
    /// <remarks>
    /// <c>Real32x3_GetAngles</c> (<c>igi2ed 0x604FA0</c>) guards its <c>atan2</c> on the X component
    /// alone and reports a gamma of zero when it is exactly zero — not the quarter turn the arctangent
    /// would give. A segment rising straight up therefore keeps the facing it had rather than snapping
    /// to one, and reproducing that is the difference between a ladder standing and a ladder rolled.
    /// </remarks>
    [Fact]
    public void A_direction_with_no_eastward_component_keeps_the_world_s_facing()
    {
        (float beta, float gamma) = Igi2SplineSweep.DirectionAngles(new Vector3f(0f, 0f, 1f));
        Assert.Equal(0f, gamma);
        Assert.Equal(-MathF.PI / 2f, beta, 4);

        (float _, float east) = Igi2SplineSweep.DirectionAngles(new Vector3f(1f, 1f, 0f));
        Assert.Equal(MathF.PI / 4f, east, 4);
    }

    /// <summary>The roll is reported the way the engine reports it: never negative.</summary>
    /// <remarks>
    /// <c>Real32x9_GetAngles</c> (<c>igi2ed 0x606260</c>) adds a full turn to any angle that comes out
    /// below zero, which is why a segment's two ends have to be unwrapped against each other before
    /// their rolls are interpolated — otherwise a roll crossing zero takes the long way round.
    /// </remarks>
    [Fact]
    public void The_roll_is_reported_the_way_the_engine_reports_it()
    {
        Assert.Equal(0f, Igi2SplineSweep.RollOf(Matrix3f.Identity), 5);
        Assert.Equal(0.5f, Igi2SplineSweep.RollOf(Matrix3f.FromEngineEulerAngles(0.5f, 0f, 0f)), 4);
        Assert.Equal(MathF.Tau - 0.5f, Igi2SplineSweep.RollOf(Matrix3f.FromEngineEulerAngles(-0.5f, 0f, 0f)), 4);
    }

    /// <summary>Every segment in the corpus bends a model of a size the level could hold.</summary>
    /// <remarks>
    /// <para>
    /// The one claim that catches a sweep whose arithmetic is off by a scale factor rather than by a
    /// little. A segment's geometry is the model carried along the curve, so it spans the curve and not
    /// much else: the curve is at most a little longer than the straight line between its waypoints, and
    /// the model's own cross-section is what it adds to that.
    /// </para>
    /// <para>
    /// Checked against the chord rather than against a fixed number of metres, because the corpus's
    /// segments run from two metres to four hundred and seventy.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_segment_bends_a_model_of_a_size_the_level_could_hold()
    {
        int segments = 0;
        int levels = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            bool any = false;
            foreach (Igi2Placement placement in level.Scene.Placements)
            {
                if (placement.Geometry is not Igi2SplineSweep sweep)
                {
                    continue;
                }

                if (!level.Table.TryResolveMesh(placement.Model, out string? mesh)
                    || !level.TryReadModel(mesh, out MefModel? model))
                {
                    continue;
                }

                MeshGeometry? geometry = sweep.Build(model);
                if (geometry is null)
                {
                    continue;
                }

                segments++;
                any = true;

                // The straight line between the two waypoints, which the frames are measured from.
                Vector3f first = sweep.Frames[0].Position;
                Vector3f last = sweep.Frames[^1].Position;
                double chord = Math.Sqrt(
                    ((last.X - first.X) * (last.X - first.X))
                    + ((last.Y - first.Y) * (last.Y - first.Y))
                    + ((last.Z - first.Z) * (last.Z - first.Z))) / WorldScene.UnitsPerMetre;

                double reach = 0.0;
                foreach (Vertex3D vertex in geometry.Vertices)
                {
                    reach = Math.Max(
                        reach,
                        Math.Sqrt(
                            (vertex.Position.X * vertex.Position.X)
                            + (vertex.Position.Y * vertex.Position.Y)
                            + (vertex.Position.Z * vertex.Position.Z)));
                }

                // Half the chord reaches each end, and the curve's bulge and the model's own width are
                // what is left. Twenty metres of slack covers the widest section the corpus ships.
                Assert.True(
                    reach <= (chord * 1.5) + 20.0,
                    $"A '{placement.Model}' segment reaches {reach:F0}m from its middle across a {chord:F0}m span.");
            }

            levels += any ? 1 : 0;
        }

        Assert.Equal(16, levels);
        Assert.Equal(633, segments);
    }
}
