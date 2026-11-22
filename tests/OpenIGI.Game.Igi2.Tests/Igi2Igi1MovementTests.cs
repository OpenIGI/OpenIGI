using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using Xunit;

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
            reference.Body.Yaw = player.Gamma;
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
        player.Igi1Feet = new Igi1FootMotion(new Igi1FootWorld(Floor, null, null, null));
        Tick(player, Igi2PlayerAction.None);

        // The reference stands where the player first stood, at rest.
        PlayerBody body = new() { Walls = Floor };
        body.ComeToRest();
        Reference reference = new(body, new HumanLocomotionAnimator(HumanLocomotionRootMotion.Retail));
        body.Yaw = player.Gamma;
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
