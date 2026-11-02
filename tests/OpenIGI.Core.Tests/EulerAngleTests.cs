using OpenIGI.Core.Numerics;
using Xunit;

namespace OpenIGI.Core.Tests;

/// <summary>The engine's Euler angles survive a trip into a rotation and back.</summary>
public sealed class EulerAngleTests
{
    /// <summary>Angles away from the gimbal lock come back as they went in.</summary>
    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(0f, 0f, 3.142f)]
    [InlineData(0.3f, -0.4f, 1.2f)]
    [InlineData(-1.1f, 1.2f, -2.9f)]
    public void Angles_round_trip(float alpha, float beta, float gamma)
    {
        Vector3f back = Matrix3f.FromEngineEulerAngles(alpha, beta, gamma).ToEngineEulerAngles();
        Assert.Equal(alpha, back.X, 4);
        Assert.Equal(beta, back.Y, 4);
        Assert.Equal(MathF.IEEERemainder(gamma, MathF.Tau), MathF.IEEERemainder(back.Z, MathF.Tau), 3);
    }

    /// <summary>At the lock the angles differ but the rotation is the same.</summary>
    [Fact]
    public void The_gimbal_lock_keeps_the_rotation()
    {
        Matrix3f locked = Matrix3f.FromEngineEulerAngles(0.5f, MathF.PI / 2f, 0.25f);
        Vector3f back = locked.ToEngineEulerAngles();
        Matrix3f again = Matrix3f.FromEngineEulerAngles(back.X, back.Y, back.Z);
        Assert.Equal(locked.M11, again.M11, 4);
        Assert.Equal(locked.M12, again.M12, 4);
        Assert.Equal(locked.M23, again.M23, 4);
        Assert.Equal(locked.M32, again.M32, 4);
    }
}
