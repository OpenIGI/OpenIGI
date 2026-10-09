using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.TestSupport;
using Xunit;
using static OpenIGI.TestSupport.QvmExpression;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// Mission 1's lifts (<c>Lift_RunHandler</c>, <c>igi2 0x46E420</c>): standing at their first floor, moving to the next when their
/// script says, carrying the doors they hold, and sounding as they go.
/// </summary>
public sealed class RetailIgi2LiftTests
{
    /// <summary>The upper building's hydraulic lift, <c>Lift_3597</c>, between floors 3460 and 3451.</summary>
    private const int Hydraulic = 3597;

    /// <summary>The mine shaft's lift, <c>Lift_3593</c>, between floors 3428 and 3449; <c>EditVariable_271</c> at one sends it up.</summary>
    private const int MineShaft = 3593;

    /// <summary>
    /// The two lifts stand at their first floors once those are made, at the far end of a way that begins and ends there.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Mission1_lifts_stand_at_their_first_floors()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Igi2LiftTask[] lifts = [.. m.Tree.OfType<Igi2LiftTask>()];
        Assert.Equal(2, lifts.Length);
        Igi2LiftTask hydraulic = Lift(m, Hydraulic);
        Assert.Equal(3460, hydraulic.ActiveFloor?.Id);
        Assert.Equal((0, 1f, false), (hydraulic.CurrentFloor, hydraulic.Along, hydraulic.IsMoving));
        Assert.Equal(hydraulic.ActiveFloor!.Position, hydraulic.CurrentPosition);
        Assert.Equal(3428, Lift(m, MineShaft).ActiveFloor?.Id);
    }

    /// <summary>
    /// Told to go up, the mine shaft's lift sets off with its start sound, moves at its top speed a share of the way a tick and
    /// slows in the last tenth, arrives at its next floor with its stop sound, and counts that floor; its solid moves with it.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_mine_lift_goes_up()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        foreach (Igi2LiftTask each in m.Tree.OfType<Igi2LiftTask>())
        {
            each.Models = m.Level.ModelFor;
            each.Sounds = m.Sounds;
            each.Bind(m.Collision!.SolidsOf(Igi2LiftTask.TypeName, each.Id));
        }

        Igi2LiftTask lift = Lift(m, MineShaft);
        Igi2FloorTask up = m.Tree.OfType<Igi2FloorTask>().Single(static f => f.Id == 3449);
        m.Run.Tick();
        Assert.False(lift.IsMoving);

        Igi2EditVariableTask variable = m.Tree.OfType<Igi2EditVariableTask>().Single(static v => v.Id == 271);
        variable.Value = 1;
        m.Run.Tick();
        Assert.Same(up, lift.ActiveFloor);
        Assert.True(lift.IsMoving);
        Assert.Contains(m.Sounds.Playing, static s => s.Name == "11_lift_start");

        int ticks = 1;
        while (lift.IsMoving && ticks < 5000)
        {
            m.Run.Tick();
            ticks++;
        }

        Assert.False(lift.IsMoving);
        Assert.Equal((1f, 1), (lift.Along, lift.CurrentFloor));
        Assert.Equal(up.Position.Z, lift.CurrentPosition.Z, 0.5);
        Assert.Contains(m.Sounds.Playing, static s => s.Name == "11_lift_stop");

        Igi2RigidObject solid = m.Collision!.SolidsOf(Igi2LiftTask.TypeName, MineShaft)[0];
        Assert.Equal(lift.CurrentPosition, solid.Position);
    }

    /// <summary>
    /// A door inside a lift holds itself against it (<c>Door_AttachToParent</c>, <c>igi2 0x536520</c>) and rides it from floor to
    /// floor (<c>Door_UpdateAttachmentHandler</c>, <c>0x5382C0</c>). Mission 1 has none, so a level of one lift with two floors
    /// and a door is built for it; the lift's floors are made after it, and stand it at the first before the door is made.
    /// </summary>
    [Fact]
    public void A_door_in_a_lift_rides_it()
    {
        string script = Path.Combine(Path.GetTempPath(), $"igi2-lift-{Guid.NewGuid():N}.qvm");
        File.WriteAllBytes(script, BuildProgram(
            Declare("Lift", ("Model", "String16"), ("Start floor task ID", "Int32"), ("Max speed", "Real32"), ("Speed inertia", "Real32"),
                ("Floor down", "VarString"), ("Floor up", "VarString"), ("Can start", "VarString"), ("Start sound", "String32"),
                ("Stop sound", "String32"), ("Move sound", "String32"), ("Start Position", "ObjectPos"), ("Start Orientation", "Real32x9")),
            Declare("Floor", ("Position", "ObjectPos"), ("Orientation", "Real32x9")),
            Declare("Door", ("Position start", "ObjectPos"), ("Position stop X", "Real32"), ("Position stop Y", "Real32"),
                ("Position slider", "Real32"), ("Orientation", "Real32x9"), ("Model", "String16"), ("Max angle", "Real32"),
                ("Open time", "Real32"), ("Pickable", "bool8"), ("Open Both Ways", "bool8"), ("Pick lock time (s)", "Real32"),
                ("Locked expression", "VarString"), ("Open door expression", "VarString"), ("Close door expression", "VarString"),
                ("StopOnCollision", "bool8"), ("Link up to a portal", "bool8"), ("Delta scale factor", "Real32"),
                ("Open sound", "String16"), ("Close sound", "String16"), ("Move sound", "String16"), ("Begin open sound", "String16"),
                ("Begin close sound", "String16"), ("Activate Sound", "String32")),
            Call("Task_New", [
                Int(1), Text("Lift"), Text("lift"), Text(""), Int(10), Real(1f), Real(0.99f), Text(""), Text("1"), Text("1"),
                Text(""), Text(""), Text(""), Real(0f), Real(0f), Real(0f), Real(0f), Real(0f), Real(0f),
                Floor(10, 0f), Floor(11, 8192f),
                Call("Task_New", [
                    Int(12), Text("Door"), Text("door"), Real(100f), Real(0f), Real(0f), Real(0f), Real(0f), Real(0f),
                    Real(0f), Real(0f), Real(0f), Text(""), Real(90f), Real(0.5f), Int(0), Int(0), Real(4f), Text(""), Text(""),
                    Text(""), Int(1), Int(1), Real(1f), Text(""), Text(""), Text(""), Text(""), Text(""), Text("")]),
            ])));

        try
        {
            RetailIgi2LevelLoadTests.Harness harness = new(script);
            harness.Loader.LoadScript(RetailIgi2LevelLoadTests.Harness.ScriptName);
            Igi2LevelRun run = new(harness.LevelRoot, harness.Loop.Natives, harness.Session.Services.Require<Igi2ScriptFunctions>());
            Igi2LiftTask lift = RetailIgi2Mission1.All(harness.LevelRoot).OfType<Igi2LiftTask>().Single();
            Igi2DoorTask door = RetailIgi2Mission1.All(harness.LevelRoot).OfType<Igi2DoorTask>().Single();
            Assert.Same(lift, door.Parent);
            Assert.Equal(new Vector3d(100.0, 0.0, 0.0), door.CurrentPosition);

            int ticks = 0;
            do
            {
                run.Tick();
                ticks++;
                Assert.Equal(lift.CurrentPosition.Z, door.CurrentPosition.Z);
            }
            while (lift.IsMoving && ticks < 1000);

            Assert.Equal((1, new Vector3d(100.0, 0.0, 8192.0)), (lift.CurrentFloor, door.CurrentPosition));
        }
        finally
        {
            File.Delete(script);
        }
    }

    private static QvmExpression Declare(string type, params (string Name, string Type)[] parameters) =>
        Call("Task_DeclareParameters", [Text(type), .. parameters.SelectMany(static p => new[] { Text(p.Name), Text(p.Type) })]);

    private static QvmExpression Floor(int id, float z) =>
        Call("Task_New", [Int(id), Text("Floor"), Text(""), Real(0f), Real(0f), Real(z), Real(0f), Real(0f), Real(0f)]);

    private static Igi2LiftTask Lift(RetailIgi2Mission1 m, int id) => m.Tree.OfType<Igi2LiftTask>().Single(l => l.Id == id);
}
