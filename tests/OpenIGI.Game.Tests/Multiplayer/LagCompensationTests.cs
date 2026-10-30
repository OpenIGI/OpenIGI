using OpenIGI.Core.Numerics;
using OpenIGI.Game.Multiplayer;
using Xunit;

namespace OpenIGI.Game.Tests.Multiplayer;

/// <summary>A shot traced against the world as its shooter's client drew it.</summary>
public sealed class LagCompensationTests
{
    [Fact]
    public void AShotIsAimedAtTheMomentTheShooterSaw()
    {
        LagCompensation lag = new(new SnapshotInterpolator(), 30) { InterpolationDelay = 0.1 };
        Assert.Equal(97, lag.AimedAt(100));
    }

    [Fact]
    public void NoShotIsRewoundFurtherThanTheLimit()
    {
        LagCompensation lag = new(new SnapshotInterpolator(), 30) { InterpolationDelay = 5, MaxUnlag = 0.5 };
        Assert.Equal(85, lag.AimedAt(100));
    }

    [Fact]
    public void ATargetIsFoundWhereItStoodThen()
    {
        SnapshotInterpolator history = new();
        for (long tick = 0; tick < 20; tick++)
        {
            history.Add(new Snapshot(tick, [new ReplicatedHuman(4, new Int3((int)tick * 300, 0, 0), default, 0, 0, 0, 0, 0, 255, 0, ReplicatedHumanCondition.Alive)]));
        }

        LagCompensation lag = new(history, 30) { InterpolationDelay = 0.1 };
        Assert.True(lag.TargetAt(15, 4, out ReplicatedHuman then));
        Assert.Equal(12 * 300, then.Position.X);
        Assert.False(lag.TargetAt(15, 5, out _));
    }
}
