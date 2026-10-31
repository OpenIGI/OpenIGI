using OpenIGI.Core.Numerics;
using OpenIGI.Game.Multiplayer;
using Xunit;

namespace OpenIGI.Game.Tests.Multiplayer;

/// <summary>The eye eased over a server's correction of the client's own player.</summary>
public sealed class PredictionErrorSmoothingTests
{
    [Fact]
    public void The_eye_stays_where_it_was_and_fades_to_the_body_over_the_smoothing_ticks()
    {
        PredictionErrorSmoothing smoothing = new() { SmoothTicks = 3 };
        Assert.Equal(default, smoothing.Offset(0f));

        // Moved 30 units along X: at once the eye is still 30 behind, then two thirds, a third, nothing.
        smoothing.Note(new Vector3d(30, 0, 0));
        Assert.Equal(-30, smoothing.Offset(0f).X, 6);
        smoothing.Tick();
        Assert.Equal(-20, smoothing.Offset(0f).X, 6);
        Assert.Equal(-15, smoothing.Offset(0.5f).X, 6);
        smoothing.Tick();
        smoothing.Tick();
        Assert.Equal(default, smoothing.Offset(0f));
    }

    [Fact]
    public void A_second_correction_adds_to_what_is_left_of_the_first()
    {
        PredictionErrorSmoothing smoothing = new() { SmoothTicks = 2 };
        smoothing.Note(new Vector3d(0, 10, 0));
        smoothing.Tick();
        smoothing.Note(new Vector3d(0, 10, 0));
        Assert.Equal(-15, smoothing.Offset(0f).Y, 6);
    }
}
