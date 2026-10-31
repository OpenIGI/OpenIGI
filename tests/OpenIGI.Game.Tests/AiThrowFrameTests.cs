using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Ai;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary><c>AI_AimThrow</c>'s grenade frame (<c>0x452930</c>): the thrower's frame tipped about x by the target's pitch.</summary>
public sealed class AiThrowFrameTests
{
    [Fact]
    public void ALevelTargetKeepsTheThrowersFrame()
    {
        Matrix3f facing = Matrix3f.Identity;
        Matrix3f frame = SoldierWeaponFire.AimThrowFrame(facing, new Vector3d(0, 4096, 0));

        Assert.Equal(facing, frame);
    }

    [Fact]
    public void ATargetAboveTipsTheForwardColumnUpByItsPitch()
    {
        // 45 degrees up, straight ahead of an unturned thrower: the frame's second column points at it.
        Matrix3f frame = SoldierWeaponFire.AimThrowFrame(Matrix3f.Identity, new Vector3d(0, 4096, 4096));

        float half = MathF.Sqrt(0.5f);
        Assert.Equal(0f, frame.M12, 5);
        Assert.Equal(half, frame.M22, 5);
        Assert.Equal(half, frame.M32, 5);
        Assert.Equal(1f, frame.M11, 5);
    }

    [Fact]
    public void ATargetBelowTipsItDown()
    {
        Matrix3f frame = SoldierWeaponFire.AimThrowFrame(Matrix3f.Identity, new Vector3d(0, 4096, -4096));

        Assert.True(frame.M32 < 0f);
    }
}
