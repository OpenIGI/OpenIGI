using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using Xunit;
using Fpu = OpenIGI.Core.Numerics.X87Single;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// Mission 1's doors (<c>Door_RunHandler</c>, <c>igi2 0x5377B0</c>): moving between shut and open, found by a view
/// (<c>Door_EvaluateActivateHandler</c>, <c>0x537600</c>), opened by the player (<c>HumanPlayer_InitActivate</c>, <c>0x4A06A0</c>) and
/// held by a human in the way (<c>Door_TraceLinesHandler</c>, <c>0x536FE0</c>).
/// </summary>
public sealed class RetailIgi2DoorTests
{
    /// <summary>The outer front gate's left half, <c>Door_3956</c>: locked while shut, opened by blowing <c>GenericTBA_260</c>.</summary>
    private const int FrontGate = 3956;

    /// <summary>A blockhouse door, <c>Door_3882</c>: opened by <c>Switch_170</c>, closed five seconds after it stands open.</summary>
    private const int Blockhouse = 3882;

    /// <summary>The cargo blockhouse's switch, <c>Switch_170</c>.</summary>
    private const int BlockhouseSwitch = 170;

    /// <summary>A door of the large building, <c>Door_3697</c>: never locked, though its lock could be picked, and it swings −90°.</summary>
    private const int LargeBuilding = 3697;

    /// <summary>
    /// The 32 doors of mission 1 are doors: each starts shut where its start puts it, and is still in the world scene. The front
    /// gate is locked by its script once the level runs; the large building's door is not.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Mission1_has_its_doors()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Assert.Equal(32, m.Doors.Count);
        Igi2DoorTask gate = m.Door(FrontGate);
        Assert.Equal((90f, 2f, "303_08_1", false), (gate.MaxAngle, gate.OpenTime, gate.Model, gate.Pickable));
        Assert.Equal(("door_fgate_end", "door_fgate_loop", "tba_picklock"), (gate.OpenSound, gate.MoveSound, gate.ActivateSound));
        Assert.Equal((true, false, 1, gate.Position), (gate.IsClosed, gate.IsOpen, gate.Status, gate.CurrentPosition));
        Assert.Contains(m.Level.Scene.Placements, p => p.LevelId == FrontGate && p.Model == "303_08_1");

        m.Run.Tick();
        Assert.True(gate.IsLocked);
        Assert.False(m.Door(LargeBuilding).IsLocked);
    }

    /// <summary>
    /// Told to open, a door says so, turns a thirtieth of its open time a tick until it stands at its full angle, then counts the
    /// ticks it stands open; the blockhouse's script closes it at five seconds, and it says so again and sounds shut.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_door_opens_turns_and_closes_on_its_scripts()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        Igi2DoorTask door = m.Door(Blockhouse);
        m.Run.Tick();
        Assert.False(door.IsOpenDoor);

        m.Switch(BlockhouseSwitch).Press(0);
        int wait = 0;
        while (!door.IsOpenDoor && wait++ < 5)
        {
            m.Run.Tick();
        }

        Assert.True(door.IsOpenDoor);
        Assert.Contains(m.Sounds.Playing, static s => s.Name == "door_ext_1");

        // A fifteenth of the way a tick: chopped steps leave the fifteenth short of open, and the next stops at it.
        float step = ((Fpu)1f / ((Fpu)0.5f * 30f)).F;
        Assert.Equal(step, door.Slider);
        int ticks = 1;
        while (!door.IsOpen)
        {
            m.Run.Tick();
            ticks++;
        }

        Assert.Equal(16, ticks);
        Assert.Equal(1f, door.Slider);
        float[] turned = Igi2Pose.RotateGamma(Igi2Pose.Rotation(door.Orientation.X, door.Orientation.Y, door.Orientation.Z), ((Fpu)(-90f) * 1f * 0.017453292f).F);
        Assert.Equal(turned, door.CurrentOrientation);
        Igi2RigidObject solid = m.Collision!.SolidsOf(Igi2DoorTask.TypeName, Blockhouse)[0];
        Assert.Equal(turned, solid.Orientation);

        int open = 0;
        while (door.IsOpenDoor)
        {
            m.Run.Tick();
            open++;
        }

        Assert.Equal(5 * 30, door.OpenTicks);
        Assert.Equal(5 * 30, open);
        Assert.Contains(m.Sounds.Playing, static s => s.Name == "door_ext_2");
        while (!door.IsClosed)
        {
            m.Run.Tick();
        }

        Assert.Equal(door.Position, door.CurrentPosition);
        Assert.Contains(m.Sounds.Playing, static s => s.Name == "door_ext_3");
    }

    /// <summary>
    /// A door that opens both ways swings away from the side it is opened from; one that does not keeps its level's angle.
    /// Mission 1 has none that opens both ways, so the large building's door is given the parameter.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_door_that_opens_both_ways_swings_away_from_its_opener()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Assert.DoesNotContain(m.Doors, static d => d.OpenBothWays);
        Igi2DoorTask door = m.Door(LargeBuilding);
        door.ToggleOpen(door.Position);
        Assert.Equal(-90f, door.MaxAngle);
        door.ToggleOpen(door.Position);
        door.OpenBothWays = true;
        float[] f = Igi2Pose.Rotation(door.Orientation.X, door.Orientation.Y, door.Orientation.Z);
        Vector3d ahead = new(f[1], f[4], f[7]);

        door.ToggleOpen(door.Position - (ahead * 2000.0));
        Assert.Equal(90f, door.MaxAngle);
        door.ToggleOpen(door.Position);
        Assert.False(door.IsOpenDoor);
        door.ToggleOpen(door.Position + (ahead * 2000.0));
        Assert.Equal(-90f, door.MaxAngle);
    }

    /// <summary>
    /// A view finds an unlocked door from either side, within its box across and up, but not far off to one side; a locked door
    /// that cannot be picked is not found at all.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_view_finds_an_unlocked_door_from_either_side()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        m.Run.Tick();
        Igi2DoorTask door = m.Door(LargeBuilding);
        Vector3d vertex = Vertex(m, door);
        float[] f = Igi2Pose.Rotation(door.Orientation.X, door.Orientation.Y, door.Orientation.Z);
        Vector3d across = new(f[0], f[3], f[6]);
        Vector3d ahead = new(f[1], f[4], f[7]);
        float[] facing = [-f[0], -f[1], f[2], -f[3], -f[4], f[5], -f[6], -f[7], f[8]];

        Assert.Same(door, Find(door, vertex + (ahead * 2000.0), facing));
        Assert.Same(door, Find(door, vertex - (ahead * 2000.0), f));
        Assert.Null(Find(door, vertex + (ahead * 2000.0) + (across * 2000.0), facing));

        Igi2DoorTask gate = m.Door(FrontGate);
        Assert.True(gate.IsLocked);
        float[] g = Igi2Pose.Rotation(gate.Orientation.X, gate.Orientation.Y, gate.Orientation.Z);
        Vector3d gateAhead = new(g[1], g[4], g[7]);
        Assert.Null(Find(gate, Vertex(m, gate) - (gateAhead * 2000.0), g));
    }

    /// <summary>
    /// The player facing the door finds it and opens it on the tick the activate key goes down; holding the key on does not close
    /// it again.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_player_opens_the_door_it_faces()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        m.Run.Tick();
        Igi2DoorTask door = m.Door(LargeBuilding);
        float[] f = Igi2Pose.Rotation(door.Orientation.X, door.Orientation.Y, door.Orientation.Z);
        Vector3d ahead = new(f[1], f[4], f[7]);
        Vector3d at = Vertex(m, door) - (ahead * 2400.0);
        float facing = MathF.Atan2(-(float)ahead.X, (float)ahead.Y);

        Igi2TitlePlayer player = RetailIgi2Mission1.Stand(m.Collision!, m.Level, door, new Vector3d(at.X, at.Y, door.Position.Z), facing);
        Assert.Same(door, player.Human.EvaluateTarget);
        player.Tick(RetailIgi2Mission1.Idle with { Held = PlayerAction.Activate, Pressed = PlayerAction.Activate });
        Assert.True(door.IsOpenDoor);
        Assert.Equal(-90f, door.MaxAngle);
        for (int tick = 0; tick < 3; tick++)
        {
            player.Tick(RetailIgi2Mission1.Idle with { Held = PlayerAction.Activate });
        }

        Assert.True(door.IsOpenDoor);
        Assert.False(player.Human.IsActivating);
    }

    /// <summary>
    /// A human's lines that strike a door from the side it swings toward hold it for fifteen ticks; the same lines from the side it
    /// swings away from, or a trace run for no human, do not.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_human_in_the_way_holds_a_moving_door()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        m.Run.Tick();
        Igi2DoorTask door = m.Door(LargeBuilding);
        float[] f = Igi2Pose.Rotation(door.Orientation.X, door.Orientation.Y, door.Orientation.Z);
        Vector3d ahead = new(f[1], f[4], f[7]);
        door.ToggleOpen(door.Position + (ahead * 2000.0));
        Assert.Equal(-90f, door.MaxAngle);

        Igi2TitlePlayer player = RetailIgi2Mission1.Stand(m.Collision!, m.Level, door, door.Position + (ahead * 20000.0), 0f);
        Igi2LineTrace trace = Through(m.Collision!, door, f, fromAhead: true);
        trace.Evaluator = null;
        m.Collision!.TraceLines(trace, ~Igi2CollisionWorld.TerrainGroup);
        Assert.Equal(0, door.HoldTimer);

        trace.Evaluator = player.Human;
        m.Collision!.TraceLines(trace, ~Igi2CollisionWorld.TerrainGroup);
        Assert.Equal(0, door.HoldTimer);

        Igi2LineTrace behind = Through(m.Collision!, door, f, fromAhead: false);
        behind.Evaluator = player.Human;
        m.Collision!.TraceLines(behind, ~Igi2CollisionWorld.TerrainGroup);
        Assert.Equal(Igi2DoorTask.HoldTicks, door.HoldTimer);

        m.Run.Tick();
        Assert.Equal((0f, Igi2DoorTask.HoldTicks - 1), (door.Slider, door.HoldTimer));
    }

    private static Vector3d Vertex(RetailIgi2Mission1 m, Igi2DoorTask door)
    {
        Vector3f v = m.Level.ModelFor(door.Model)!.TryGetMagicVertex(0, out Vector3f vertex) ? vertex : default;
        IReadOnlyList<float> r = door.CurrentOrientation;
        Vector3d p = door.CurrentPosition;
        return new Vector3d(
            p.X + (r[0] * v.X) + (r[1] * v.Y) + (r[2] * v.Z),
            p.Y + (r[3] * v.X) + (r[4] * v.Y) + (r[5] * v.Z),
            p.Z + (r[6] * v.X) + (r[7] * v.Y) + (r[8] * v.Z));
    }

    private static IIgi2Activatable? Find(Igi2DoorTask target, Vector3d view, float[] orientation)
    {
        Igi2EvaluateActivate context = new(view, orientation, isPlayer: true);
        target.EvaluateActivate(context);
        return context.Best;
    }

    // A line along the door's own Y that strikes its solid, found by sweeping its face; from its front or from behind.
    private static Igi2LineTrace Through(Igi2CollisionWorld world, Igi2DoorTask door, float[] f, bool fromAhead)
    {
        Igi2RigidObject solid = world.SolidsOf(Igi2DoorTask.TypeName, door.Id)[0];
        Vector3d across = new(f[0], f[3], f[6]), ahead = new(f[1], f[4], f[7]), up = new(f[2], f[5], f[8]);
        double sign = fromAhead ? 1.0 : -1.0;
        for (int mesh = 0; mesh < 2; mesh++)
        {
            for (double x = -4096.0; x <= 4096.0; x += 256.0)
            {
                for (double z = 0.0; z <= 8192.0; z += 512.0)
                {
                    Vector3d face = door.CurrentPosition + (across * x) + (up * z);
                    Igi2LineTrace trace = new(1) { Mesh = mesh };
                    trace.Starts[0] = face + (ahead * (1024.0 * sign));
                    trace.Ends[0] = face - (ahead * (1024.0 * sign));
                    world.TraceLines(trace, ~Igi2CollisionWorld.TerrainGroup);
                    if (trace.Intersections[0].IsHit && ReferenceEquals(trace.Intersections[0].HitObject, solid))
                    {
                        return trace;
                    }
                }
            }
        }

        throw new InvalidOperationException($"No line along its face strikes Door_{door.Id}.");
    }
}
