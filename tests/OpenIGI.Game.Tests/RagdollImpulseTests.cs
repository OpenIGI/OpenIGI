using OpenIGI.Core.Numerics;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Weapons;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Covers what a round does to the body it kills — that the throw follows the shot rather than a table
/// of canned falls.
/// </summary>
public sealed class RagdollImpulseTests
{
    /// <summary>The body is thrown the way the round was travelling, not the way it was facing.</summary>
    [Theory]
    [InlineData(1.0, 0.0, 0.0)]
    [InlineData(-1.0, 0.0, 0.0)]
    [InlineData(0.0, 1.0, 0.0)]
    [InlineData(0.0, 0.0, -1.0)]
    public void ABodyIsThrownAlongTheRound(double x, double y, double z)
    {
        RagdollSkeleton rig = RagdollRig.Rig();
        RagdollBody body = RagdollRig.Standing(rig);
        Vector3d start = body.PositionOf(RagdollRig.Chest);
        Vector3d travel = new(x, y, z);

        RagdollImpulse.Apply(
            body, RagdollRig.Chest, travel, RagdollImpulse.MomentumOf(rig, WeaponCategory.Gun, 1.0));
        body.Step(RagdollWorld.Empty, substeps: 8);

        Vector3d moved = body.PositionOf(RagdollRig.Chest) - start;
        Assert.True(
            Vector3d.Dot(moved.Normalized(), travel.Normalized()) > 0.5,
            $"The body went {moved} for a round travelling {travel}.");
    }

    /// <summary>A more powerful weapon throws a body further, in the order the real momenta run.</summary>
    /// <remarks>
    /// This is the part of the model that is genuinely physical: the relative magnitudes are exterior
    /// ballistics, so a shotgun beats a rifle beats a pistol beats a knife without any of it being
    /// authored.
    /// </remarks>
    [Fact]
    public void AMorePowerfulWeaponThrowsABodyFurther()
    {
        RagdollSkeleton rig = RagdollRig.Rig();

        double ThrowDistance(WeaponCategory category)
        {
            RagdollBody body = RagdollRig.Standing(rig);
            Vector3d start = body.PositionOf(RagdollRig.Chest);
            RagdollImpulse.Apply(
                body,
                RagdollRig.Chest,
                new Vector3d(0.0, 1.0, 0.0),
                RagdollImpulse.MomentumOf(rig, category, 1.0));
            body.Step(RagdollWorld.Empty, substeps: 8);
            return Vector3d.Distance(start, body.PositionOf(RagdollRig.Chest));
        }

        double knife = ThrowDistance(WeaponCategory.Knife);
        double pistol = ThrowDistance(WeaponCategory.Pistol);
        double rifle = ThrowDistance(WeaponCategory.Gun);
        double shotgun = ThrowDistance(WeaponCategory.Shotgun);

        Assert.True(knife < pistol, $"knife {knife:F0} vs pistol {pistol:F0}");
        Assert.True(pistol < rifle, $"pistol {pistol:F0} vs rifle {rifle:F0}");
        Assert.True(rifle < shotgun, $"rifle {rifle:F0} vs shotgun {shotgun:F0}");
    }

    /// <summary>The player's own multiplier scales the whole thing, and zero means the body simply falls.</summary>
    [Fact]
    public void TheForceMultiplierScalesTheThrowAndZeroRemovesIt()
    {
        RagdollSkeleton rig = RagdollRig.Rig();

        Assert.Equal(0.0, RagdollImpulse.MomentumOf(rig, WeaponCategory.Gun, 0.0));
        Assert.Equal(
            RagdollImpulse.MomentumOf(rig, WeaponCategory.Gun, 1.0) * 2.0,
            RagdollImpulse.MomentumOf(rig, WeaponCategory.Gun, 2.0),
            1e-6);
    }

    /// <summary>
    /// The bone the round went through takes the most of it, and the rest of the body less the further
    /// it is — so a head shot snaps the head and a leg shot drops the body onto that leg.
    /// </summary>
    [Fact]
    public void TheStruckBoneTakesTheMostOfIt()
    {
        RagdollSkeleton rig = RagdollRig.Rig();
        RagdollBody body = RagdollRig.Standing(rig);

        Vector3d headStart = body.PositionOf(RagdollRig.Head);
        Vector3d footStart = body.PositionOf(RagdollRig.LeftFoot);

        RagdollImpulse.Apply(
            body,
            RagdollRig.Head,
            new Vector3d(0.0, 1.0, 0.0),
            RagdollImpulse.MomentumOf(rig, WeaponCategory.Gun, 1.0));
        body.Step(RagdollWorld.Empty, substeps: 8);

        double head = Vector3d.Distance(headStart, body.PositionOf(RagdollRig.Head));
        double foot = Vector3d.Distance(footStart, body.PositionOf(RagdollRig.LeftFoot));

        Assert.True(head > foot, $"the head moved {head:F0} and a foot {foot:F0}");
    }

    /// <summary>A body shot while standing is carried off its feet, not dropped where it stood.</summary>
    /// <remarks>
    /// <b>The case the other tests here all miss</b>, because they throw a body that is already in the
    /// air. A guard is standing <em>on</em> the floor when he is killed, so he is in contact at the very
    /// instant the round arrives — and anything that damps a body on contact will take the knock straight
    /// back off him. That is exactly what the settling ramp did until it was taught to leave a body alone
    /// while it is still travelling.
    /// </remarks>
    [Fact]
    public void ABodyShotWhileStandingIsCarriedOffItsFeet()
    {
        RagdollSkeleton rig = RagdollRig.Rig();
        RagdollWorld plane = RagdollWorld.Plane(0.0);

        // Feet on the floor, as a standing guard's are.
        RagdollBody body = RagdollRig.Standing(rig, new Vector3d(0.0, 0.0, 2_400.0));
        Vector3d start = body.Centre;

        RagdollImpulse.Apply(
            body,
            RagdollRig.Chest,
            new Vector3d(0.0, 1.0, 0.0),
            RagdollImpulse.MomentumOf(rig, WeaponCategory.Gun, 1.5));

        for (int tick = 0; tick < 200 && !body.IsAtRest; tick++)
        {
            body.Step(plane, substeps: 8, 0.7);
        }

        Vector3d moved = body.Centre - start;
        double carried = Math.Sqrt((moved.X * moved.X) + (moved.Y * moved.Y));

        // A metre at least, and along the round. Measured at about 1.6.
        Assert.True(carried > 4096.0, $"The body only travelled {carried / 4096.0:F2} m.");
        Assert.True(moved.Y > 0.0, "The body did not go the way the round did.");
    }

    /// <summary>A round with no direction at all does nothing rather than throwing the body somewhere.</summary>
    [Fact]
    public void ARoundWithNoDirectionDoesNothing()
    {
        RagdollSkeleton rig = RagdollRig.Rig();
        RagdollBody body = RagdollRig.Standing(rig);
        Vector3d start = body.PositionOf(RagdollRig.Chest);

        RagdollImpulse.Apply(
            body, RagdollRig.Chest, Vector3d.Zero, RagdollImpulse.MomentumOf(rig, WeaponCategory.Gun, 1.0));

        Assert.Equal(start, body.PositionOf(RagdollRig.Chest));
    }

    /// <summary>An unnamed bone pushes the body as a whole, rather than throwing or refusing.</summary>
    /// <remarks>
    /// <c>HumanHit.Bone</c> is <c>-1</c> when the body was not posed and no bone could be named, which is
    /// ordinary rather than exceptional.
    /// </remarks>
    [Fact]
    public void AnUnnamedBonePushesTheBodyAsAWhole()
    {
        RagdollSkeleton rig = RagdollRig.Rig();
        RagdollBody body = RagdollRig.Standing(rig);
        Vector3d start = body.PositionOf(RagdollRig.Pelvis);

        RagdollImpulse.Apply(
            body, -1, new Vector3d(0.0, 1.0, 0.0), RagdollImpulse.MomentumOf(rig, WeaponCategory.Gun, 1.0));
        body.Step(RagdollWorld.Empty, substeps: 8);

        Assert.True(body.PositionOf(RagdollRig.Pelvis).Y > start.Y);
    }
}
