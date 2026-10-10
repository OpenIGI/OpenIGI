using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Soldiers;
using OpenIGI.Game.Igi2.Weapons;
using Xunit;
using Fpu = OpenIGI.Core.Numerics.X87Single;
using static OpenIGI.Game.Igi2.Tests.Igi2PlayerFakes;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// The soldier's body over test doubles: its signal word (<c>HumanSoldier_StateSignalHandler</c>, <c>igi2 0x4E4010</c>) and its
/// standing, turning, walking, running and falling states.
/// </summary>
public sealed class Igi2HumanSoldierTests
{
    /// <summary>A soldier is made standing, walking, its upper body idle, walls not stopping it and its ground probed from 4096 up.</summary>
    [Fact]
    public void A_soldier_is_made_standing()
    {
        (Igi2HumanSoldier soldier, FakeAnimation animation, _, _) = OnFloor();

        Assert.Equal(Igi2SoldierState.Stand, soldier.Locomotion.CurrentState);
        Assert.True(soldier.IsWalk);
        Assert.False(soldier.IsObjectCollision);
        Assert.Equal((4096f, 0f, 0.33000001f), (soldier.GroundCheckZOffset, soldier.CollisionZOffset, soldier.RecoilThreshold));
        Assert.Equal(Igi2SoldierUpperBody.Idle, soldier.UpperBodyState);
        Assert.Empty(animation.Played);
    }

    /// <summary>
    /// Its first tick enters the stand: the stand row plays at once (its mind has not started), the body is still, and the idle
    /// arms start with no blend; on the floor it is grounded.
    /// </summary>
    [Fact]
    public void The_first_tick_stands_the_soldier()
    {
        (Igi2HumanSoldier soldier, FakeAnimation animation, _, ScriptedMind mind) = OnFloor();

        soldier.RunDelayed();

        Assert.Equal([(Igi2SoldierState.Stand, 0, 0f)], animation.Played);
        Assert.Equal([(Igi2HumanSoldier.UpperIdle, 0, 0f)], animation.UpperBodyPlayed);
        Assert.True(soldier.IsDisableDeltaTranslation);
        Assert.True((soldier.Signal & Igi2SoldierSignal.Grounded) != 0);
        Assert.True(soldier.Signal.HasFlag(Igi2SoldierSignal.Walk));
        Assert.True(mind.HasStarted);
        Assert.Equal(1f, soldier.MovementSpeedScale);
    }

    /// <summary>Each channel raises its own bit, read with the soldier's meanings.</summary>
    [Fact]
    public void The_channels_pack_into_the_signal()
    {
        (Igi2HumanChannel Channel, Igi2SoldierSignal Bit)[] map =
        [
            (Igi2SoldierChannel.StrafeLeft, Igi2SoldierSignal.StrafeLeft),
            (Igi2SoldierChannel.StrafeRight, Igi2SoldierSignal.StrafeRight),
            (Igi2SoldierChannel.Forward, Igi2SoldierSignal.Forward),
            (Igi2SoldierChannel.Backward, Igi2SoldierSignal.Backward),
            (Igi2SoldierChannel.Fire, Igi2SoldierSignal.Fire),
            (Igi2SoldierChannel.Reload, Igi2SoldierSignal.Reload),
            (Igi2SoldierChannel.Kneel, Igi2SoldierSignal.Kneel),
            (Igi2SoldierChannel.LieDown, Igi2SoldierSignal.LieDown),
            (Igi2SoldierChannel.Tense, Igi2SoldierSignal.Tense),
            (Igi2SoldierChannel.Aim, Igi2SoldierSignal.Aim),
            (Igi2SoldierChannel.ThrowGrenade, Igi2SoldierSignal.ThrowGrenade),
            (Igi2SoldierChannel.CloseCombat, Igi2SoldierSignal.CloseCombat),
            (Igi2SoldierChannel.Turn, Igi2SoldierSignal.Turn),
            (Igi2SoldierChannel.PressSwitch, Igi2SoldierSignal.PressSwitch),
            (Igi2SoldierChannel.RollLeft, Igi2SoldierSignal.RollLeft),
            (Igi2SoldierChannel.RollRight, Igi2SoldierSignal.RollRight),
        ];

        foreach ((Igi2HumanChannel channel, Igi2SoldierSignal bit) in map)
        {
            (Igi2HumanSoldier soldier, _, _, _) = Unborn();
            float[] frame = new float[Igi2HumanChannels.Count];
            frame[(int)channel] = 1f;
            soldier.Channels.Accept(frame);
            soldier.Locomotion.Tick(soldier);
            Assert.True((soldier.Signal & bit) != 0, $"{channel} did not raise {bit}");
        }
    }

    /// <summary>An activation's channel holds the task to activate; it is taken once and the channel cleared.</summary>
    [Fact]
    public void An_activation_names_its_task()
    {
        (Igi2HumanSoldier soldier, _, _, _) = Unborn();
        float[] frame = new float[Igi2HumanChannels.Count];
        frame[27] = 160f;
        soldier.Channels.Accept(frame);

        soldier.Locomotion.Tick(soldier);

        Assert.Equal(160, soldier.ActivateTaskId);
        Assert.Equal(0f, soldier.Channels[(Igi2HumanChannel)27]);
        Assert.True((soldier.Signal & Igi2SoldierSignal.Activate) != 0);
    }

    /// <summary>
    /// The forward channel walks it: row 7 plays at once and from the next tick the body steps 204.8 a tick along its
    /// heading (the step <c>Human_Move</c> takes and clears).
    /// </summary>
    [Fact]
    public void Forward_walks_the_soldier()
    {
        (Igi2HumanSoldier soldier, FakeAnimation animation, _, ScriptedMind mind) = OnFloor();
        soldier.RunDelayed();
        mind.Frame[(int)Igi2SoldierChannel.Forward] = 1f;

        soldier.RunDelayed();
        Assert.Equal(Igi2SoldierState.WalkForward, soldier.Locomotion.CurrentState);
        Assert.Equal((Igi2HumanSoldier.WalkRow, 0, 0f), animation.Played[^1]);
        Assert.False(soldier.IsDisableDeltaTranslation);
        double y = soldier.Position.Y;

        soldier.RunDelayed();
        Assert.Equal(204.8, soldier.Position.Y - y, 0.01);
        Assert.Equal(0.0, soldier.Position.X, 0.0001);
        Assert.False(soldier.IsRunning);
    }

    /// <summary>The walk toggle makes it run: 458.752 a tick, and running.</summary>
    [Fact]
    public void Toggling_walk_runs_the_soldier()
    {
        (Igi2HumanSoldier soldier, FakeAnimation animation, _, ScriptedMind mind) = OnFloor();
        soldier.RunDelayed();
        mind.Frame[(int)Igi2SoldierChannel.ToggleWalk] = 1f;
        soldier.RunDelayed();
        mind.Frame[(int)Igi2SoldierChannel.ToggleWalk] = 0f;
        Assert.False(soldier.IsWalk);

        mind.Frame[(int)Igi2SoldierChannel.Forward] = 1f;
        soldier.RunDelayed();
        double y = soldier.Position.Y;
        soldier.RunDelayed();

        Assert.Equal(Igi2SoldierState.RunForward, soldier.Locomotion.CurrentState);
        Assert.True(soldier.IsRunning);
        Assert.Equal(458.752, soldier.Position.Y - y, 0.01);

        mind.Frame[(int)Igi2SoldierChannel.Forward] = 0f;
        soldier.RunDelayed();
        Assert.Equal(Igi2SoldierState.Stand, soldier.Locomotion.CurrentState);
        Assert.Equal((Igi2SoldierState.Stand, 0, 0.5f), animation.Played[^1]);
    }

    /// <summary>The turn channel turns it on the spot, blended over half a second, and it stands again when the turn ends.</summary>
    [Fact]
    public void Turning_holds_while_the_channel_does()
    {
        (Igi2HumanSoldier soldier, FakeAnimation animation, _, ScriptedMind mind) = OnFloor();
        soldier.RunDelayed();
        mind.Frame[(int)Igi2SoldierChannel.Turn] = 1f;

        soldier.RunDelayed();
        soldier.RunDelayed();
        Assert.Equal(Igi2SoldierState.StandTurn, soldier.Locomotion.CurrentState);
        Assert.Equal((Igi2SoldierState.StandTurn, 0, 0.5f), animation.Played[^1]);

        mind.Frame[(int)Igi2SoldierChannel.Turn] = 0f;
        soldier.RunDelayed();
        Assert.Equal(Igi2SoldierState.Stand, soldier.Locomotion.CurrentState);
    }

    /// <summary>
    /// With nothing underfoot it falls, slowed by the air and pulled down by gravity a tick, and lands standing with the ground's
    /// own landing sound.
    /// </summary>
    [Fact]
    public void Off_the_ground_it_falls_and_lands()
    {
        (Igi2HumanSoldier soldier, FakeAnimation animation, FakeSurroundings surroundings, _) = Unborn();
        soldier.Mind = new ScriptedMind();
        soldier.Position = new Vector3d(0, 0, 40000);
        surroundings.World.Surfaces.Add(new FakeCollider(0.0, material: 3));
        surroundings.LandingSounds[3] = "land_snow";

        soldier.RunDelayed();
        soldier.RunDelayed();
        Assert.Equal(Igi2SoldierState.Fall, soldier.Locomotion.CurrentState);
        Assert.Equal(default, soldier.Speed);
        soldier.RunDelayed();
        Assert.Equal((((Fpu)0f - Igi2HumanSoldier.FallGravity) * Igi2HumanSoldier.FallDamping).F, soldier.Speed.Z);

        for (int tick = 0; tick < 200 && soldier.Locomotion.CurrentState == Igi2SoldierState.Fall; tick++)
        {
            soldier.RunDelayed();
        }

        Assert.Equal(Igi2SoldierState.Stand, soldier.Locomotion.CurrentState);
        Assert.Contains("land_snow", surroundings.Sounds);
        Assert.Equal(default, soldier.Speed);
        Assert.Contains(animation.Played, static p => p.State == Igi2SoldierState.Fall);
    }

    /// <summary>
    /// The lie channel lays it down (<c>HumanSoldier_InitStateStandToLieDown</c>, <c>igi2 0x4E6B00</c>), it lies once that clip ends,
    /// and when the channel drops it gets up at twice the clip's speed (<c>RunStateLieDownToStand</c>, <c>0x4E6C90</c>) and stands.
    /// </summary>
    [Fact]
    public void It_lies_down_and_gets_up_again()
    {
        (Igi2HumanSoldier soldier, FakeAnimation animation, _, ScriptedMind mind) = OnFloor();
        soldier.RunDelayed();
        mind.Frame[(int)Igi2SoldierChannel.LieDown] = 1f;

        soldier.RunDelayed();
        Assert.Equal(Igi2SoldierState.StandToLieDown, soldier.Locomotion.CurrentState);
        Assert.Equal(Igi2HumanPose.LyingFlat, soldier.Pose);
        Assert.Equal((Igi2SoldierState.StandToLieDown, 0, 0f), animation.Played[^1]);

        animation.MainEnded = true;
        soldier.RunDelayed();
        Assert.Equal(Igi2SoldierState.LieDown, soldier.Locomotion.CurrentState);
        Assert.Equal((Igi2SoldierState.LieDown, 0, 0f), animation.Played[^1]);

        animation.MainEnded = false;
        mind.Frame[(int)Igi2SoldierChannel.LieDown] = 0f;
        soldier.RunDelayed();
        Assert.Equal(Igi2SoldierState.LieDownToStand, soldier.Locomotion.CurrentState);
        soldier.RunDelayed();
        Assert.Equal(2 * Animation.Igi2AnimController.TicksPerFrame, animation.MainTimeStep);

        animation.MainEnded = true;
        soldier.RunDelayed();
        Assert.Equal(Igi2SoldierState.Stand, soldier.Locomotion.CurrentState);
    }

    /// <summary>
    /// Tense and strafing left it runs aside at 267.6 a tick (<c>HumanSoldier_RunStateTenseRunLeft</c>, <c>igi2 0x4E54E0</c>); asked to
    /// roll it rolls left crouched (<c>InitStateStandRollLeft</c>, <c>0x4E61D0</c>) and kneels when the roll's clip ends.
    /// </summary>
    [Fact]
    public void Tense_it_strafes_and_rolls_into_a_kneel()
    {
        (Igi2HumanSoldier soldier, FakeAnimation animation, _, ScriptedMind mind) = OnFloor();
        soldier.RunDelayed();
        mind.Frame[(int)Igi2SoldierChannel.Tense] = 1f;
        mind.Frame[(int)Igi2SoldierChannel.StrafeLeft] = 1f;

        soldier.RunDelayed();
        Assert.Equal(Igi2SoldierState.TenseRunLeft, soldier.Locomotion.CurrentState);
        Assert.True(soldier.IsRunning);
        Vector3d before = soldier.Position;
        soldier.RunDelayed();
        double moved = Math.Sqrt(Math.Pow(soldier.Position.X - before.X, 2) + Math.Pow(soldier.Position.Y - before.Y, 2));
        Assert.Equal(Igi2HumanSoldier.TenseStrafeSpeed, moved, 0.01);

        mind.Frame[(int)Igi2SoldierChannel.RollLeft] = 1f;
        soldier.RunDelayed();
        Assert.Equal(Igi2SoldierState.StandRollLeft, soldier.Locomotion.CurrentState);
        Assert.Equal(Igi2HumanPose.Crouching, soldier.Pose);
        Assert.Equal(1, soldier.UpperBodyFlags & 1);

        mind.Frame[(int)Igi2SoldierChannel.RollLeft] = 0f;
        mind.Frame[(int)Igi2SoldierChannel.StrafeLeft] = 0f;
        mind.Frame[(int)Igi2SoldierChannel.Kneel] = 1f;
        animation.MainEnded = true;
        soldier.RunDelayed();
        Assert.Equal(Igi2SoldierState.Kneel, soldier.Locomotion.CurrentState);
        Assert.Equal(0, soldier.UpperBodyFlags & 1);
    }

    /// <summary>
    /// A blow (<c>HumanSoldier_InitStateStandCloseCombat</c>, <c>igi2 0x4E6750</c>) marks its damage dealt once the clip strikes, and
    /// the clip's end stands it again (<c>RunStateStandCloseCombat</c>, <c>0x4E67A0</c>).
    /// </summary>
    [Fact]
    public void A_blow_marks_itself_dealt_and_ends_standing()
    {
        (Igi2HumanSoldier soldier, FakeAnimation animation, _, ScriptedMind mind) = OnFloor();
        soldier.RunDelayed();
        mind.Frame[(int)Igi2SoldierChannel.CloseCombat] = 1f;
        soldier.RunDelayed();
        mind.Frame[(int)Igi2SoldierChannel.CloseCombat] = 0f;
        Assert.Equal(Igi2SoldierState.StandCloseCombat, soldier.Locomotion.CurrentState);
        Assert.False(soldier.IsDamageDealt);

        soldier.Signal |= Igi2SoldierSignal.BlowLanded;
        soldier.RunDelayed();
        Assert.True(soldier.IsDamageDealt);
        Assert.Equal(Igi2SoldierSignal.None, soldier.Signal & Igi2SoldierSignal.BlowLanded);

        animation.MainEnded = true;
        soldier.RunDelayed();
        Assert.Equal(Igi2SoldierState.Stand, soldier.Locomotion.CurrentState);
    }

    /// <summary>
    /// Pressing a switch keeps the upper body off and moves by the clip (<c>HumanSoldier_InitStateStandPressSwitch</c>,
    /// <c>igi2 0x4E9550</c>); the clip's end stands it with the upper body free.
    /// </summary>
    [Fact]
    public void Pressing_a_switch_stands_it_once_the_clip_ends()
    {
        (Igi2HumanSoldier soldier, FakeAnimation animation, _, ScriptedMind mind) = OnFloor();
        soldier.RunDelayed();
        mind.Frame[(int)Igi2SoldierChannel.PressSwitch] = 1f;
        soldier.RunDelayed();
        mind.Frame[(int)Igi2SoldierChannel.PressSwitch] = 0f;
        Assert.Equal(Igi2SoldierState.StandPressSwitch, soldier.Locomotion.CurrentState);
        Assert.Equal(1, soldier.UpperBodyFlags & 1);
        Assert.False(soldier.IsDisableDeltaTranslation);

        animation.MainEnded = true;
        soldier.RunDelayed();
        Assert.Equal(Igi2SoldierState.Stand, soldier.Locomotion.CurrentState);
        Assert.Equal(0, soldier.UpperBodyFlags & 1);
    }

    /// <summary>
    /// A soldier fires from its mind's fire position toward its aim point (<c>HumanSoldier_WeaponGetParentParametersHandler</c>,
    /// <c>igi2 0x4E9B60</c>): forward along the aim, the side the aim crossed with straight up, unnormalised (shorter the steeper the
    /// aim), up normalised; three run-stream draws go first; an aim straight up takes the mind's own orientation.
    /// </summary>
    [Fact]
    public void A_soldier_fires_along_its_minds_aim()
    {
        (Igi2HumanSoldier soldier, _, _, _) = OnFloor();
        AimingMind mind = new() { Fire = new Vector3d(100, 200, 300), Aim = new Vector3d(4196, 4296, 300) };
        soldier.Mind = mind;
        IgiRandom expected = new();
        expected.NextRaw();
        expected.NextRaw();
        expected.NextRaw();
        Assert.True(soldier.HasFireFrame);

        Igi2FireFrame level = soldier.GetFireFrame(addInaccuracy: true);
        Assert.Equal(mind.Fire, level.Position);
        Assert.Equal(expected.NextRaw(), soldier.Random.NextRaw());
        float[] o = level.Orientation;
        Assert.Equal(0.70710677f, o[1], 5);
        Assert.Equal(0.70710677f, o[4], 5);
        Assert.Equal(0.70710677f, o[0], 5);
        Assert.Equal(-0.70710677f, o[3], 5);
        Assert.Equal(1f, o[8], 5);

        mind.Aim = new Vector3d(100, 4296, 4396);
        float[] steep = soldier.GetFireFrame(addInaccuracy: false).Orientation;
        Assert.Equal(0.70710677f, steep[0], 5);
        Assert.Equal(-0.70710677f, steep[5], 5);
        Assert.Equal(0.70710677f, steep[8], 5);

        mind.Aim = new Vector3d(100, 200, 9000);
        Assert.Equal(mind.FireOrientation, soldier.GetFireFrame(addInaccuracy: false).Orientation);
    }

    /// <summary>A mind with a fire position and an aim point, and a turned orientation of its own.</summary>
    private sealed class AimingMind : IIgi2SoldierMind
    {
        public Vector3d Fire { get; init; }

        public Vector3d Aim { get; set; }

        public bool HasStarted => true;

        public Vector3d FirePosition => Fire;

        public Vector3d AimTarget => Aim;

        public IReadOnlyList<float> FireOrientation { get; } = [0f, 1f, 0f, -1f, 0f, 0f, 0f, 0f, 1f];

        public void Run(Igi2HumanSoldier soldier)
        {
        }
    }

    private static (Igi2HumanSoldier, FakeAnimation, FakeSurroundings, ScriptedMind) Unborn()
    {
        FakeAnimation animation = new();
        FakeSurroundings surroundings = new();
        Igi2HumanSoldier soldier = new(Igi2NoWeaponControl.Instance, animation, surroundings);
        soldier.Create();
        soldier.Damage.MaxDamage = 100f;
        return (soldier, animation, surroundings, new ScriptedMind());
    }

    private static (Igi2HumanSoldier, FakeAnimation, FakeSurroundings, ScriptedMind) OnFloor()
    {
        (Igi2HumanSoldier soldier, FakeAnimation animation, FakeSurroundings surroundings, ScriptedMind mind) = Unborn();
        surroundings.World.Surfaces.Add(new FakeCollider(0.0));
        soldier.Mind = mind;
        return (soldier, animation, surroundings, mind);
    }

    /// <summary>A mind that sends a fixed frame each tick.</summary>
    private sealed class ScriptedMind : IIgi2SoldierMind
    {
        public float[] Frame { get; } = new float[Igi2HumanChannels.Count];

        public bool HasStarted { get; private set; }

        public void Run(Igi2HumanSoldier soldier)
        {
            soldier.Update(Frame);
            HasStarted = true;
        }
    }
}
