using OpenIGI.Core.Numerics;
using OpenIGI.Game.Multiplayer;
using Xunit;

namespace OpenIGI.Game.Tests.Multiplayer;

/// <summary>The client's own player kept where the server has it.</summary>
public sealed class PredictionReconcilerTests
{
    [Fact]
    public void AnAgreeingPredictionIsLeftAlone()
    {
        PredictionReconciler reconciler = new();
        reconciler.Record(10, new Vector3d(1000, 0, 0));
        Assert.Null(reconciler.Check(10, new Vector3d(1010, 0, 0)));
        Assert.Equal(0, reconciler.Corrections);
    }

    [Fact]
    public void AnErrorIsCorrectedOnceAndNotCountedAgain()
    {
        PredictionReconciler reconciler = new();
        for (long tick = 1; tick <= 5; tick++)
        {
            reconciler.Record(tick, new Vector3d(tick * 100, 0, 0));
        }

        // The server had the player 500 further on after tick 2: so were ticks 3 to 5, and they are not corrected again.
        Assert.Equal(new Vector3d(500, 0, 0), reconciler.Check(2, new Vector3d(700, 0, 0)));
        Assert.Null(reconciler.Check(3, new Vector3d(800, 0, 0)));
        Assert.Null(reconciler.Check(2, new Vector3d(9999, 0, 0)));
        Assert.Equal(1, reconciler.Corrections);
    }

    [Fact]
    public void ATickNeverPredictedIsNotJudged()
    {
        PredictionReconciler reconciler = new();
        Assert.Null(reconciler.Check(42, new Vector3d(1, 2, 3)));
    }
}
