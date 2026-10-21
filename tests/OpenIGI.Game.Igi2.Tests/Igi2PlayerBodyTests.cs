using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The collision body fitted to the posture, <c>HumanPlayer_MaintainCollisionZOffset</c> (<c>igi2 0x4B89A0</c>).</summary>
public sealed class Igi2PlayerBodyTests
{
    /// <summary>Standing and crouching look for footing from a metre up, with the body not lifted.</summary>
    [Theory]
    [InlineData(Igi2HumanPose.Standing)]
    [InlineData(Igi2HumanPose.Crouching)]
    public void An_upright_body_checks_from_a_metre(Igi2HumanPose pose)
    {
        (Igi2HumanPlayer player, _, _) = Igi2PlayerFakes.Player();
        player.Pose = pose;
        player.CollisionZOffset = 99f;

        player.MaintainCollisionZOffset();

        Assert.Equal(4096f, player.GroundCheckZOffset);
        Assert.Equal(0f, player.CollisionZOffset);
    }

    /// <summary>A jump with crouch held tucks the legs: the body lifts half a metre and checks from a metre and a half.</summary>
    [Fact]
    public void A_crouched_jump_tucks_the_legs()
    {
        (Igi2HumanPlayer player, _, _) = Igi2PlayerFakes.Player();
        player.Locomotion.TransitionTo(Igi2PlayerState.Jump);
        player.Signal |= Igi2PlayerSignal.Crouch;

        player.MaintainCollisionZOffset();

        Assert.Equal(6144f, player.GroundCheckZOffset);
        Assert.Equal(2048f, player.CollisionZOffset);
    }

    /// <summary>Lying flat checks from 0.3 metres, or from a metre on a train.</summary>
    [Theory]
    [InlineData(false, 1228.8f)]
    [InlineData(true, 4096f)]
    public void A_prone_body_checks_low_except_on_a_train(bool onTrain, float expected)
    {
        (Igi2HumanPlayer player, _, Igi2PlayerFakes.FakeSurroundings surroundings) = Igi2PlayerFakes.Player();
        object owner = new Igi2CollisionOwner(onTrain ? Igi2HumanPlayer.TrainType : "EditRigidObj", 1, "deck", 0);
        surroundings.World.Surfaces.Add(new Igi2PlayerFakes.FakeCollider(0.0, owner: owner));
        Vector3d at = new(0.0, 0.0, 100.0);
        player.GroundCheckZOffset = 4096f;
        player.GroundCollision(at, ref at, 819.2f);
        Assert.NotNull(player.GroundCollider);

        player.Pose = Igi2HumanPose.LyingFlat;
        player.MaintainCollisionZOffset();

        Assert.Equal(expected, player.GroundCheckZOffset);
        Assert.Equal(0f, player.CollisionZOffset);
    }
}
