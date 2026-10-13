using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Vehicles;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;
using Fpu = OpenIGI.Core.Numerics.X87Single;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The trains that ride a spline, and the couplings that space them along it.</summary>
/// <remarks>
/// <para>
/// A train is the one thing in an I.G.I. 2 level placed entirely by arithmetic. It carries a distance
/// and a spline, its carriages carry nothing at all, and where each one ends up comes out of walking the
/// track. Nothing in the data says where a carriage is, so there is no authored position to compare
/// against — what can be checked is the relationship the walk is supposed to produce.
/// </para>
/// <para>
/// The relationship is this: consecutive carriages stand exactly one carriage-length apart in a STRAIGHT
/// line, not one carriage-length along the track. That is the whole reason
/// <c>SplineObj_GetPosAndMatrix</c> (<c>igi2ed 0x63FA10</c>) solves a triangle for each one instead of
/// subtracting arc length, and it is the thing that goes wrong invisibly if the solve is mistaken: the
/// carriages still sit on the rails, just gradually too close together or too far apart.
/// </para>
/// </remarks>
public sealed class Igi2TrainTests
{
    /// <summary>A carriage faces the coupling in front of it, whatever the track does in between.</summary>
    /// <remarks>
    /// <c>Train_MoveCarriage</c> (<c>igi2ed 0x47FB20</c>) builds the rotation from the two coupling
    /// points and nothing else, putting the direction of travel in the middle column and starting the
    /// side axis horizontal. A carriage is rigid: it does not bend to the rail under it.
    /// </remarks>
    [Fact]
    public void A_carriage_faces_the_coupling_in_front_of_it()
    {
        Matrix3f facing = Igi2Train.Facing(new Vector3d(4096.0, 0.0, 0.0), new Vector3d(0.0, 0.0, 0.0));

        // Travelling east, so the middle column is east and the side axis is the horizontal across it.
        Assert.Equal(1f, facing.Column2.X, 4);
        Assert.Equal(0f, facing.Column2.Y, 4);
        Assert.Equal(0f, facing.Column2.Z, 4);
        Assert.Equal(0f, facing.Column1.Z, 4);
        Assert.Equal(1f, facing.Determinant, 4);

        // Two couplings in the same place leave it unturned rather than undefined.
        Assert.Equal(Matrix3f.Identity, Igi2Train.Facing(default, default));
    }

    /// <summary>On straight track a coupling walk is exactly the steps it was given.</summary>
    /// <remarks>
    /// The degenerate case of the triangle solve, and the one with an answer that can be written down:
    /// along a straight run the chord and the arc are the same thing, so each coupling lands its own step
    /// back from the one before.
    /// </remarks>
    [Fact]
    public void On_straight_track_the_couplings_are_the_steps()
    {
        Igi2SplineSweep run = Igi2SplineSweep.Create(
            new Vector3d(0.0, 0.0, 0.0),
            new Vector3d(400_000.0, 0.0, 0.0),
            Matrix3f.Identity,
            Matrix3f.Identity,
            areas: 20,
            linear: false,
            flipped: false);
        Igi2SplinePath path = new(1, [run]);

        Assert.True(path.TryWalk(200_000.0, [-40_000f, -40_000f], out Vector3d[] points));
        Assert.Equal(3, points.Length);
        Assert.Equal(200_000.0, points[0].X, 0);
        Assert.Equal(160_000.0, points[1].X, 0);
        Assert.Equal(120_000.0, points[2].X, 0);
        Assert.Equal(0.0, points[2].Y, 1);
    }

    /// <summary>A walk that runs off the end of the track stops at the end of it.</summary>
    /// <remarks>
    /// The original clamps to the segment's own last or first frame depending on which way the step went,
    /// rather than reporting a failure. A rake longer than its track piles up at the buffers.
    /// </remarks>
    [Fact]
    public void A_walk_off_the_end_of_the_track_stops_there()
    {
        Igi2SplineSweep run = Igi2SplineSweep.Create(
            new Vector3d(0.0, 0.0, 0.0),
            new Vector3d(40_000.0, 0.0, 0.0),
            Matrix3f.Identity,
            Matrix3f.Identity,
            areas: 20,
            linear: false,
            flipped: false);
        Igi2SplinePath path = new(1, [run]);

        Assert.True(path.TryWalk(1_000.0, [-40_000f], out Vector3d[] points));
        Assert.Equal(0.0, points[1].X, 0);
    }

    /// <summary>Every carriage in the corpus stands one carriage-length from the next.</summary>
    /// <remarks>
    /// <para>
    /// Nine carriages over two trains, both in <c>location1/level2</c>'s mine. The spacing is the claim:
    /// each gap is the length of the carriage in FRONT of it, because that is the step the walk took to
    /// find the coupling behind it.
    /// </para>
    /// <para>
    /// The tolerance is for the displacement. <c>Train_MoveCarriage</c> shifts each carriage off the rail
    /// by its authored <c>Displacement X</c> and <c>Z</c>, turned through that carriage's own rotation —
    /// and on a curve two neighbours are turned differently, which moves them a few centimetres relative
    /// to each other. The six-car rake is on a curve and shows exactly that.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_carriage_stands_one_carriage_length_from_the_next()
    {
        int carriages = 0;
        int trains = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2Placement[] rake =
                [.. level.Scene.Placements.Where(p => p.TypeName == Igi2Train.TrainType)];
            if (rake.Length == 0)
            {
                continue;
            }

            carriages += rake.Length;
            for (int i = 0; i < rake.Length; i++)
            {
                Matrix3f facing = Assert.IsType<Matrix3f>(rake[i].Frame);
                Assert.Equal(1f, facing.Determinant, 3);

                MefModel model = Assert.IsType<MefModel>(level.ModelFor(rake[i].Model));
                Assert.True(model.TryGetMagicVertex(0, out Vector3f head));
                Assert.True(model.TryGetMagicVertex(1, out Vector3f tail));
                double length = Math.Sqrt(
                    ((head.X - tail.X) * (head.X - tail.X))
                    + ((head.Y - tail.Y) * (head.Y - tail.Y))
                    + ((head.Z - tail.Z) * (head.Z - tail.Z))) / 4096.0;

                // The last carriage of a rake has nothing behind it to measure against. A rake ends where
                // the next one's locomotive starts, and that gap is not a coupling.
                if (i + 1 >= rake.Length)
                {
                    trains++;
                    continue;
                }

                double gap = (rake[i + 1].Position - rake[i].Position).Length / 4096.0;
                if (gap > length + 1.0)
                {
                    trains++;
                    continue;
                }

                Assert.InRange(gap, length - 0.3, length + 0.3);
            }
        }

        Assert.Equal(9, carriages);
        Assert.Equal(2, trains);
    }

    /// <summary>
    /// Mission 12's running AnimTask drives its train: set back at load to where the level starts it, at its original thrust's speed;
    /// then each tick its thrust closes on the programme's, and it moves its speed along the spline, its carriages a coupling apart.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void An_anim_task_drives_its_train_along_the_spline()
    {
        Igi2Level level = Igi2Level.Find("location1", "level2");
        Igi2AnimTask animation = All(level.Root).OfType<Igi2AnimTask>().Single(t => t.Id == 4039);
        Igi2TrainTask train = All(level.Root).OfType<Igi2TrainTask>().Single(t => t.Id == animation.TargetId);
        GameTask rails = All(level.Root).Single(t => t.Id == train.RailroadId);
        train.Prepare(level.ModelFor, Igi2SplineObject.Read(rails).Path);
        Assert.Equal(train.Rake[0].CurrentPosition, train.Rake[0].Built.Position);

        animation.Begin();
        Assert.Same(train, animation.Target);
        Assert.Equal(0, animation.Tick);
        Assert.Equal(train.OriginalPosition, train.Distance);
        Assert.Equal(train.OriginalThrust, train.Thrust);

        // Its run expression follows the other train's AnimTask, which waits on a mission variable; without it, it runs as authored.
        animation.RunExpression = string.Empty;
        Igi2LevelRun run = new(level.Root, new OpenIGI.Scripting.Runtime.NativeRegistry(), new Scripting.Igi2ScriptFunctions());
        for (int tick = 0; tick < 300; tick++)
        {
            float before = train.Distance;
            animation.Run(run);
            if (animation.Tick >= 2)
            {
                Assert.Equal(((Fpu)(train.FlipDirection ? -train.Speed : train.Speed) + before).F, train.Distance);
            }
        }

        Assert.Equal(300, animation.Tick);
        for (int i = 1; i < train.Rake.Count; i++)
        {
            double gap = (train.Rake[i].CurrentPosition - train.Rake[i - 1].CurrentPosition).Length;
            Assert.InRange(gap, train.Rake[i - 1].Length - 1500.0, train.Rake[i - 1].Length + 1500.0);
        }
    }

    private static IEnumerable<GameTask> All(GameTask task)
    {
        yield return task;
        foreach (GameTask child in task.Children)
        {
            foreach (GameTask inner in All(child))
            {
                yield return inner;
            }
        }
    }
}
