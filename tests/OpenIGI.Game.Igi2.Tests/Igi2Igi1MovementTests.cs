using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using Xunit;
using Fpu = OpenIGI.Core.Numerics.X87Single;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// I.G.I. 2's player on Project I.G.I.'s movement (<see cref="Igi2PlayerMovement.Igi1"/>): it goes exactly where that game's own
/// body goes for the same keys, and its states play what the body does.
/// </summary>
public sealed class Igi2Igi1MovementTests
{
    /// <summary>
    /// Running and walking in every direction, crouching and crouch-walking, and turning at a run, the player stands where a bare
    /// Project I.G.I. body fed the same controls stands, every tick.
    /// </summary>
    [Fact]
    public void The_player_goes_where_Project_IGIs_body_goes()
    {
        (Igi2HumanPlayer player, Reference reference) = Stand();
        Igi2PlayerAction[] directions =
        [
            Igi2PlayerAction.MoveForwards, Igi2PlayerAction.MoveForwards | Igi2PlayerAction.MoveRight, Igi2PlayerAction.MoveRight,
            Igi2PlayerAction.MoveBackwards | Igi2PlayerAction.MoveRight, Igi2PlayerAction.MoveBackwards,
            Igi2PlayerAction.MoveBackwards | Igi2PlayerAction.MoveLeft, Igi2PlayerAction.MoveLeft,
            Igi2PlayerAction.MoveForwards | Igi2PlayerAction.MoveLeft,
        ];

        int tick = 0;
        foreach (Igi2PlayerAction pace in new[] { Igi2PlayerAction.None, Igi2PlayerAction.ModifyWalkRun, Igi2PlayerAction.Crouch })
        {
            foreach (Igi2PlayerAction direction in directions)
            {
                tick = Hold(player, reference, direction | pace, 25, tick);
                tick = Hold(player, reference, pace, 8, tick);
            }
        }

        tick = Hold(player, reference, Igi2PlayerAction.MoveForwards, 30, tick, mouseX: 0.05f);

        // Some seventy metres walked, run and crouch-walked over the script.
        Assert.True(reference.Travelled > 200000d, $"The player only covered {reference.Travelled} units in {tick} ticks.");
    }

    /// <summary>
    /// The jump is the body's: it leaves the ground on the press with the first game's speed, the state plays the jump the tick
    /// after, and it comes down where the body comes down, into the gait the keys hold.
    /// </summary>
    [Fact]
    public void The_jump_is_Project_IGIs_and_the_state_follows_it()
    {
        (Igi2HumanPlayer player, Reference reference) = Stand();
        int tick = Hold(player, reference, Igi2PlayerAction.MoveForwards, 20, 0);
        tick = Hold(player, reference, Igi2PlayerAction.MoveForwards | Igi2PlayerAction.Jump, 1, tick);
        Assert.False(reference.Body.IsGrounded);

        tick = Hold(player, reference, Igi2PlayerAction.MoveForwards, 1, tick);
        Assert.Equal(Igi2PlayerState.Jump, player.Locomotion.CurrentState);

        double highest = 0d;
        for (int i = 0; i < 60 && !(reference.Body.IsGrounded && player.Locomotion.CurrentState != Igi2PlayerState.Jump); i++)
        {
            tick = Hold(player, reference, Igi2PlayerAction.MoveForwards, 1, tick);
            highest = Math.Max(highest, player.Position.Z);
        }

        // The first game's jump: 1024 units a tick up, under its gravity.
        Assert.InRange(highest, 5000d, 8000d);
        Assert.Equal(Igi2PlayerState.RunForward, player.Locomotion.CurrentState);
        Assert.Equal(0f, player.Damage.Damage);
    }

    /// <summary>
    /// Turned off zero, each key moves the player along I.G.I. 2's own axes for its heading: forward along its forward axis, back
    /// against it, right along its right axis and left against it.
    /// </summary>
    /// <param name="degrees">The heading the player is turned to.</param>
    [Theory]
    [InlineData(0f)]
    [InlineData(37f)]
    [InlineData(90f)]
    [InlineData(-120f)]
    public void Each_key_moves_the_player_along_its_own_axes(float degrees)
    {
        (Igi2PlayerAction Key, int Axis, float Sign)[] keys =
        [
            (Igi2PlayerAction.MoveForwards, 1, 1f), (Igi2PlayerAction.MoveBackwards, 1, -1f),
            (Igi2PlayerAction.MoveRight, 0, 1f), (Igi2PlayerAction.MoveLeft, 0, -1f),
        ];
        foreach ((Igi2PlayerAction key, int axis, float sign) in keys)
        {
            (Igi2HumanPlayer player, _, _) = Igi2PlayerFakes.OnFloor();
            player.Movement = Igi2PlayerMovement.Igi1;
            player.Igi1Feet = new Igi1FootMotion(new Igi1FootWorld(Floor, null, null, null), player);
            float gamma = degrees * MathF.PI / 180f;
            player.Gamma = gamma;
            player.IdealGamma = gamma;
            Tick(player, Igi2PlayerAction.None);

            Vector3d start = player.Position;
            for (int i = 0; i < 20; i++)
            {
                Tick(player, key);
            }

            Matrix3f o = player.Orientation;
            Vector3f along = axis == 1 ? new Vector3f(o.M12, o.M22, 0f) : new Vector3f(o.M11, o.M21, 0f);
            Vector3d moved = player.Position - start;
            double ahead = ((moved.X * along.X) + (moved.Y * along.Y)) * sign;
            double astray = Math.Abs((moved.X * along.Y) - (moved.Y * along.X));
            Assert.True(ahead > 1000d, $"{key} at {degrees} degrees moved {moved}, {ahead} along its axis.");
            Assert.True(astray < ahead * 0.05, $"{key} at {degrees} degrees strayed {astray} off its axis, moving {moved}.");
        }
    }

    /// <summary>
    /// With weapon weight on, the weapons carried slow the first game's walk by I.G.I. 2's own share, a half at most; off, they do
    /// not slow it at all.
    /// </summary>
    /// <param name="slowDown">The weapons' summed slow-down.</param>
    /// <param name="expected">The share of the walk left.</param>
    [Theory]
    [InlineData(0.3f, 0.7)]
    [InlineData(0.8f, 0.5)]
    public void Weapon_weight_slows_the_walk_as_IGI2_slows_its_own(float slowDown, double expected)
    {
        double free = RunDistance(weight: false, slowDown);
        double laden = RunDistance(weight: true, slowDown);

        Assert.Equal(RunDistance(weight: false, slowDown: 0f), free, 6);
        Assert.InRange(laden / free, expected - 0.001, expected + 0.001);
    }

    /// <summary>
    /// The original's look eases a small turn as Project I.G.I.'s body does (<see cref="HumanBodyTurn"/>), where the linear look
    /// and I.G.I. 2's own turn it whole; a large turn is whole in both.
    /// </summary>
    /// <param name="input">The tick's horizontal look input.</param>
    [Theory]
    [InlineData(0.02f)]
    [InlineData(-0.045f)]
    [InlineData(0.3f)]
    public void The_original_look_eases_a_small_turn_as_IGI1s_body_does(float input)
    {
        (Igi2HumanPlayer retail, _, _) = Igi2PlayerFakes.OnFloor();
        retail.Movement = Igi2PlayerMovement.Igi1;
        retail.Igi1Look = Igi1Look.Retail;
        (Igi2HumanPlayer linear, _, _) = Igi2PlayerFakes.OnFloor();
        linear.Movement = Igi2PlayerMovement.Igi1;

        float retailBefore = retail.IdealGamma, linearBefore = linear.IdealGamma;
        Tick(retail, Igi2PlayerAction.None, input);
        Tick(linear, Igi2PlayerAction.None, input);

        float fov = retail.View.FieldOfView;
        float eased = (-(Fpu)new HumanBodyTurn(averages: false).Step(input) * fov).F;
        float whole = ((-((Fpu)input * 3.1415927f)) * fov).F;
        Assert.Equal(((Fpu)eased + retailBefore).F, retail.IdealGamma);
        Assert.Equal(((Fpu)whole + linearBefore).F, linear.IdealGamma);
        Assert.Equal(Math.Abs(input) < HumanBodyTurn.SmallInput, Math.Abs(eased) < Math.Abs(whole));
    }

    /// <summary>The session's comparison stands in for the chosen movement and look; off leaves the settings' own.</summary>
    [Fact]
    public void The_comparison_stands_in_for_the_settings()
    {
        Assert.Equal(
            new Igi2MovementChoice(Igi2PlayerMovement.Igi1, Igi1Look.Retail, true),
            Igi2MovementChoice.From(Igi2PlayerMovement.Igi1, Igi1Look.Retail, true, Igi2MovementCompare.Off));
        Assert.Equal(Igi2PlayerMovement.Igi2, Igi2MovementChoice.From(Igi2PlayerMovement.Igi1, Igi1Look.Retail, false, Igi2MovementCompare.Igi2).Movement);
        Assert.True(Igi2MovementChoice.From(Igi2PlayerMovement.Igi2, Igi1Look.Retail, false, Igi2MovementCompare.Igi1Linear).LinearIgi1Look);
        Assert.True(Igi2MovementChoice.From(Igi2PlayerMovement.Igi2, Igi1Look.Linear, false, Igi2MovementCompare.Igi1Retail).RetailIgi1Look);
    }

    // How far the player runs in forty ticks on the first game's body, carrying weapons that slow by a share.
    private static double RunDistance(bool weight, float slowDown)
    {
        (Igi2HumanPlayer player, _, _) = Igi2PlayerFakes.OnFloor(new Igi2PlayerFakes.RecordingWeapon { SummedMovementSlowDown = slowDown });
        player.Movement = Igi2PlayerMovement.Igi1;
        player.Igi1Weight = weight;
        player.Igi1Feet = new Igi1FootMotion(new Igi1FootWorld(Floor, null, null, null), player);
        Tick(player, Igi2PlayerAction.None);
        Vector3d start = player.Position;
        for (int i = 0; i < 40; i++)
        {
            Tick(player, Igi2PlayerAction.MoveForwards);
        }

        return Across(player.Position - start);
    }

    /// <summary>Under I.G.I. 2's own movement the body is never stepped: the player moves as the sequel moves it.</summary>
    [Fact]
    public void Igi2s_own_movement_leaves_the_body_alone()
    {
        (Igi2HumanPlayer player, _) = Stand();
        player.Movement = Igi2PlayerMovement.Igi2;
        for (int i = 0; i < 10; i++)
        {
            Tick(player, Igi2PlayerAction.MoveForwards);
        }

        Assert.False(player.Igi1Feet!.IsSeated);
        Assert.False(player.StandsOnIgi1Feet);
    }

    private static int Hold(Igi2HumanPlayer player, Reference reference, Igi2PlayerAction held, int ticks, int tick, float mouseX = 0f)
    {
        for (int i = 0; i < ticks; i++)
        {
            bool jump = (held & Igi2PlayerAction.Jump) != 0 && !reference.JumpHeld;
            reference.JumpHeld = (held & Igi2PlayerAction.Jump) != 0;
            Tick(player, held, mouseX);
            tick++;

            // What the player's body read this tick: its keys, the pace and stance the states chose, and the player's heading.
            float forward = Axis(held, Igi2PlayerAction.MoveForwards, Igi2PlayerAction.MoveBackwards);
            float strafe = Axis(held, Igi2PlayerAction.MoveRight, Igi2PlayerAction.MoveLeft);
            PlayerCommand command = new(
                forward, strafe, 0f, 0f,
                Run: !player.Signal.HasFlag(Igi2PlayerSignal.Walking),
                Crouch: player.Pose == Igi2HumanPose.Crouching,
                Jump: jump);
            Vector3d before = reference.Body.Position;
            reference.Body.Yaw = Igi1FootMotion.YawOf(player.Gamma);
            reference.Body.TickAnimated(command, terrain: null, reference.Animator);
            reference.Travelled += Across(reference.Body.Position - before);

            Assert.True(
                reference.Body.Position == player.Position,
                $"Tick {tick}: the player stands at {player.Position}, the body at {reference.Body.Position}.");
            Assert.Equal(reference.Body.IsGrounded, player.Signal.HasFlag(Igi2PlayerSignal.Grounded));
        }

        return tick;
    }

    private static float Axis(Igi2PlayerAction held, Igi2PlayerAction positive, Igi2PlayerAction negative) =>
        ((held & positive) != 0 ? 1f : 0f) - ((held & negative) != 0 ? 1f : 0f);

    private static double Across(Vector3d at) => Math.Sqrt((at.X * at.X) + (at.Y * at.Y));

    private static (Igi2HumanPlayer Player, Reference Reference) Stand()
    {
        (Igi2HumanPlayer player, _, _) = Igi2PlayerFakes.OnFloor();
        player.Movement = Igi2PlayerMovement.Igi1;
        player.Igi1Feet = new Igi1FootMotion(new Igi1FootWorld(Floor, null, null, null), player);
        Tick(player, Igi2PlayerAction.None);

        // The reference stands where the player first stood, at rest.
        PlayerBody body = new() { Walls = Floor };
        body.ComeToRest();
        Reference reference = new(body, new HumanLocomotionAnimator(HumanLocomotionRootMotion.Retail));
        body.Yaw = Igi1FootMotion.YawOf(player.Gamma);
        body.TickAnimated(new PlayerCommand(0f, 0f, 0f, 0f, Run: true, Crouch: false, Jump: false), terrain: null, reference.Animator);
        Assert.True(body.Position == player.Position, $"{body.Position} against {player.Position}.");
        return (player, reference);
    }

    private static void Tick(Igi2HumanPlayer player, Igi2PlayerAction held, float mouseX = 0f)
    {
        float[] frame = new float[Igi2HumanChannels.Count];
        new Igi2PlayerInputReader().Read(new Igi2PlayerInputState(mouseX, 0f, held, 0), frame);
        player.Update(frame);
    }

    // Level ground at zero everywhere.
    private static bool Floor(Vector3d origin, Vector3d end, out WorldRayHit hit)
    {
        hit = default;
        if (end.Z >= origin.Z || origin.Z < 0d || end.Z > 0d)
        {
            return false;
        }

        hit = new WorldRayHit(new Vector3d(origin.X, origin.Y, 0d), new Vector3f(0f, 0f, 1f), origin.Z);
        return true;
    }

    private sealed class Reference(PlayerBody body, HumanLocomotionAnimator animator)
    {
        public PlayerBody Body { get; } = body;

        public HumanLocomotionAnimator Animator { get; } = animator;

        public bool JumpHeld { get; set; }

        public double Travelled { get; set; }
    }
}
