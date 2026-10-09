using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Soldiers;
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
