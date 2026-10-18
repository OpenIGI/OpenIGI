using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Paths;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// Mission 1's six spline paths and the guides that move its cutscene props along them (<c>SplinePathGuideQTask_RunHandler</c>,
/// <c>igi2 0x5E67F0</c>; <c>SplinePathDynCubeObj_InitPath</c>, <c>0x548DA0</c>).
/// </summary>
public sealed class RetailIgi2SplinePathTests
{
    /// <summary>The intro's plane path, <c>SplinePathDynCubeObj_3055</c>, three nodes at 600, 600 and 700 km/h.</summary>
    private const int PlanePath = 3055;

    /// <summary>The guide that flies the plane, <c>SplinePathGuideQTask_3051</c>.</summary>
    private const int PlaneGuide = 3051;

    /// <summary>The plane, <c>EditRigidObj_3061</c> (<c>700_03_1</c>).</summary>
    private const int Plane = 3061;

    /// <summary>
    /// Each path is measured as the editor measured it: every node's time from the start, worked out again from the segments'
    /// lengths and the speeds, is the time the level was saved with.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_paths_are_measured_as_the_editor_saved_them()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Igi2SplinePathTask[] paths = [.. m.Tree.OfType<Igi2SplinePathTask>()];
        Assert.Equal([3, 3, 3, 3, 4, 3], paths.Select(static p => p.Nodes.Count));
        Assert.Equal(6, m.Tree.OfType<Igi2SplinePathGuideTask>().Count());

        // Node id and the "Time from start (Sec)" objects.qvm holds for it.
        (int Node, float Saved)[] saved =
        [
            (3053, 5.714244f), (3052, 8.578773f), (3105, 13.900069f), (3104, 31.868818f), (3000, 2.858109f), (2996, 7.885553f),
            (3084, 23.913446f), (3083, 36.677937f), (3020, 5.90451f), (3019, 10.523278f), (3018, 18.389338f), (2939, 7.939974f),
            (2938, 30.813921f),
        ];
        Dictionary<int, Igi2SplinePathNodeTask> nodes = paths.SelectMany(static p => p.Nodes).ToDictionary(static n => n.Id);
        foreach ((int node, float time) in saved)
        {
            Assert.Equal(time, nodes[node].TimeFromStart, 5);
        }

        Assert.All(paths, static p => Assert.Equal(0f, p.Nodes[0].TimeFromStart));
        Assert.All(paths, static p => Assert.Equal(0f, p.Nodes[^1].DistanceToNext));
    }

    /// <summary>
    /// A segment's curve starts and ends exactly on its nodes; a node that turns with the curve faces along its tangent, and a
    /// free one keeps the frame it was placed with.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_segment_runs_from_node_to_node_and_frames_follow_the_tangent()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        foreach (Igi2SplinePathTask path in m.Tree.OfType<Igi2SplinePathTask>())
        {
            for (int segment = 0; segment < path.Nodes.Count - 1; segment++)
            {
                Assert.Equal(path.Nodes[segment].Position, path.Position(segment, 0f));
                Assert.Equal(path.Nodes[segment + 1].Position, path.Position(segment, 1f));
            }
        }

        Igi2SplinePathNodeTask first = m.Tree.OfType<Igi2SplinePathTask>().Single(static p => p.Id == PlanePath).Nodes[0];
        Assert.False(first.IsFreeOrientation);
        Vector3d tangent = first.Tangent;
        double length = Math.Sqrt((tangent.X * tangent.X) + (tangent.Y * tangent.Y) + (tangent.Z * tangent.Z));
        Assert.Equal(tangent.X / length, first.Frame[1], 5);
        Assert.Equal(tangent.Y / length, first.Frame[4], 5);
        Assert.Equal(0f, first.Frame[8] - 1f, 5);

        Igi2SplinePathNodeTask free = m.Tree.OfType<Igi2SplinePathNodeTask>().Single(static n => n.Id == 3001);
        Assert.True(free.IsFreeOrientation);
        Assert.Equal([1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f], free.Frame.Select(static v => Math.Abs(v)));
    }

    /// <summary>
    /// The intro's sequence starts the plane's guide, which flies the plane along its path: the plane stands where the curve is,
    /// its step is the guide's velocity, about 600 km/h a tick, its solids go with it, and <c>vPosition</c> says how far along it is.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_intro_flies_the_plane_along_its_path()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        Igi2SplinePathGuideTask guide = m.Tree.OfType<Igi2SplinePathGuideTask>().Single(static g => g.Id == PlaneGuide);
        Igi2SplinePathTask path = m.Tree.OfType<Igi2SplinePathTask>().Single(static p => p.Id == PlanePath);
        Igi2EditRigidObjTask plane = m.Tree.OfType<Igi2EditRigidObjTask>().Single(static p => p.Id == Plane);
        Assert.Equal((PlanePath, Plane, false), (guide.PathId, guide.ModelId, guide.IsRunning));
        guide.Connect();
        Assert.Same(guide, plane.Guide);
        plane.Models = m.Level.ModelFor;
        Igi2RigidObject solid = m.Collision!.SolidsOf(Igi2EditRigidObjTask.TypeName, Plane)[0];
        plane.Bind(m.Collision.SolidsOf(Igi2EditRigidObjTask.TypeName, Plane));

        int started = -1;
        for (int tick = 0; tick < 200 && started < 0; tick++)
        {
            m.Run.Tick();
            if (guide.IsRunning)
            {
                started = tick;
            }
        }

        Assert.True(started >= 0, "the intro never started the plane");
        for (int tick = 0; tick < 30; tick++)
        {
            Vector3d before = plane.CurrentPosition;
            m.Run.Tick();
            Assert.Equal(path.Position(guide.Segment, guide.T), plane.CurrentPosition);
            Assert.Equal(path.Orientation(guide.Segment, guide.T), plane.CurrentOrientation);
            Vector3f step = guide.Velocity;
            Assert.Equal((float)(plane.CurrentPosition.Y - before.Y), step.Y, 0);
        }

        // 600 km/h is 22755 units a tick at 30 a second; the tenths' estimate of the curve's length runs a little short.
        double speed = Math.Sqrt((guide.Velocity.X * (double)guide.Velocity.X) + (guide.Velocity.Y * (double)guide.Velocity.Y));
        Assert.InRange(speed, 21000.0, 24000.0);
        Assert.Equal(plane.CurrentPosition, solid.Position);
        Assert.Equal(guide.PathPosition, m.Run.Evaluate(Igi2LevelExpressions.Create($"SplinePathGuideQTask_{PlaneGuide}.vPosition", "test", m.Run.Natives)));
    }

    /// <summary>
    /// A guide that is not looping stops at its path's end, with its model on the last node; one that loops starts the path again.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_guide_stops_at_the_end_or_loops()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Igi2SplinePathGuideTask guide = m.Tree.OfType<Igi2SplinePathGuideTask>().Single(static g => g.Id == PlaneGuide);
        Igi2SplinePathTask path = m.Tree.OfType<Igi2SplinePathTask>().Single(static p => p.Id == PlanePath);
        Igi2EditRigidObjTask plane = m.Tree.OfType<Igi2EditRigidObjTask>().Single(static p => p.Id == Plane);
        guide.SetRunning(true);
        int ticks = 0;
        while (guide.IsRunning && ticks < 2000)
        {
            guide.Run(m.Run);
            ticks++;
        }

        // About 8.6 seconds of flight, as the last node's time says.
        Assert.InRange(ticks, 240, 275);
        Assert.Equal((1, 1f), (guide.Segment, guide.T));
        Assert.Equal(path.Nodes[^1].Position, plane.CurrentPosition);

        guide.IsLooping = true;
        guide.SetRunning(true);
        int segment = guide.Segment;
        for (int tick = 0; tick < 10 && guide.Segment == segment; tick++)
        {
            guide.Run(m.Run);
        }

        Assert.Equal((0, true), (guide.Segment, guide.IsRunning));
    }
}
