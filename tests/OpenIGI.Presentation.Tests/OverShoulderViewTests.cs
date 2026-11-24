using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Presentation.Cameras;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>The move over the shoulder as either game's frame drives it: timed on the wall clock, held by a pause, ended by a cut.</summary>
public sealed class OverShoulderViewTests
{
    private static readonly Camera3D Eye = new(new Vector3d(10.0, 20.0, 1.6), Camera3D.LookRotation(0f, 0f));

    // Out all the way at the default blend: a tenth of a second at a time, as the move takes no more a frame.
    private static OverShoulderView Out(out int now)
    {
        OverShoulderView view = new();
        now = 1000;
        view.Reset(now);
        for (int i = 0; i < 10; i++)
        {
            now += 100;
            view.Compose(true, true, Eye, now, paused: false, walls: null);
        }

        return view;
    }

    [Fact]
    public void At_the_eye_it_gives_the_eye_back_and_no_ray()
    {
        OverShoulderView view = new();
        view.Reset(0);

        Camera3D drawn = view.Compose(true, false, Eye, 16, paused: false, walls: null);

        Assert.Equal(Eye.Position, drawn.Position);
        Assert.False(view.IsEngaged);
        Assert.Null(view.AimRay);
        Assert.True(view.ShowsViewModel);
        Assert.False(view.ShowsBody);
    }

    [Fact]
    public void Out_over_the_shoulder_the_body_shows_and_the_crosshair_has_the_cameras_ray()
    {
        OverShoulderView view = Out(out int now);

        Camera3D drawn = view.Compose(true, true, Eye, now + 16, paused: false, walls: null);

        Assert.True(view.ShowsBody);
        Assert.False(view.ShowsViewModel);
        Assert.NotNull(view.AimRay);
        Assert.Equal(drawn.Position.X * 4096.0, view.AimRay!.Value.OriginUnits.X, 3);
        Assert.Equal(drawn.Orientation.Row3, view.AimRay.Value.Direction);
    }

    [Fact]
    public void A_pause_holds_the_camera_where_it_is()
    {
        OverShoulderView view = new();
        view.Reset(0);
        Camera3D moving = view.Compose(true, true, Eye, 100, paused: false, walls: null);

        Camera3D held = view.Compose(true, false, Eye, 200, paused: true, walls: null);

        Assert.Equal(moving.Position, held.Position);
    }

    [Fact]
    public void A_view_taken_elsewhere_cuts_back_to_the_eye()
    {
        OverShoulderView view = Out(out int now);

        Camera3D drawn = view.Compose(false, true, Eye, now + 16, paused: false, walls: null);

        Assert.Equal(Eye.Position, drawn.Position);
        Assert.False(view.IsEngaged);
        Assert.Null(view.AimRay);
    }
}
