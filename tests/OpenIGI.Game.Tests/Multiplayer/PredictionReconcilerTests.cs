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

    [Fact]
    public void A_prediction_made_in_a_lift_is_weighed_as_a_place_in_the_car()
    {
        // The client stood 64 over its car's floor; the server ran the same command two ticks further into the car's
        // travel, so its player and its car are both 300 higher. In the world that is an error; in the car it is none.
        PredictionReconciler reconciler = new();
        reconciler.Record(10, new Vector3d(0, 0, 1064), new PlatformFrame(3, new Vector3d(0, 0, 1000)));
        Assert.Null(reconciler.Check(10, new Vector3d(0, 0, 1364), index => index == 3 ? new Vector3d(0, 0, 1300) : null));
        Assert.Equal(0, reconciler.Corrections);

        // Off its floor in the server's car -- a jump the server never made -- it is put back by as much.
        reconciler.Record(11, new Vector3d(0, 0, 2064), new PlatformFrame(3, new Vector3d(0, 0, 1000)));
        Assert.Equal(new Vector3d(0, 0, -1000), reconciler.Check(11, new Vector3d(0, 0, 1364), index => new Vector3d(0, 0, 1300)));
    }
}
