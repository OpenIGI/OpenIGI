using OpenIGI.Core.Graphics;
using OpenIGI.Game.World.Sectors;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Covers the sector cull's screen: how a view's angles become its rectangles, and its rectangles a part of the view.</summary>
public sealed class SectorScreenProjectionTests
{
    /// <summary>A view's projection puts the view's edges on the screen's edges and its axis at the centre.</summary>
    [Fact]
    public void A_view_spans_its_screen()
    {
        SectorScreenProjection projection = SectorScreenProjection.ForView(1.5f, 1f);
        Assert.Equal(new SectorScreenRect(0f, 0f, 1536f, 1024f), projection.DisplayArea);
        Assert.Equal(768f, projection.OriginX);
        Assert.Equal(512f, projection.OriginY);
        Assert.Equal(projection.ScreenFovX, projection.ScreenFovY);
    }

    /// <summary>
    /// The screen counts up and a view clip counts down, so a rectangle along the bottom of the screen is the bottom of the view.
    /// </summary>
    [Fact]
    public void A_rectangle_at_the_bottom_of_the_screen_is_the_bottom_of_the_view()
    {
        SectorScreenProjection projection = SectorScreenProjection.ForView(1f, 1f);
        ViewClip clip = projection.ToViewClip(new SectorScreenRect(0f, 0f, 512f, 256f));
        Assert.Equal(new ViewClip(0f, 0.75f, 0.5f, 1f), clip);
        Assert.Equal(ViewClip.Full, projection.ToViewClip(projection.DisplayArea));
    }

    /// <summary>A sphere straight ahead is in the whole view; one behind the eye, or beside a narrow window, is not.</summary>
    [Fact]
    public void A_sphere_is_kept_only_by_a_window_it_falls_in()
    {
        Camera3D eye = new(default, Camera3D.LookRotation(0f, 0f));
        SectorScreenProjection projection = SectorScreenProjection.ForView(1f, 1f);
        SectorScreenRect whole = projection.DisplayArea;
        SectorScreenRect left = new(0f, 0f, 100f, 1024f);

        Assert.True(SectorPortalProjection.SphereInAnyWindow(eye, projection, new(0, 1000, 0), 10f, [whole]));
        Assert.False(SectorPortalProjection.SphereInAnyWindow(eye, projection, new(0, -1000, 0), 10f, [whole]));
        Assert.False(SectorPortalProjection.SphereInAnyWindow(eye, projection, new(0, 1000, 0), 10f, [left]));
        Assert.True(SectorPortalProjection.SphereInAnyWindow(eye, projection, new(-900, 1000, 0), 10f, [left]));
    }
}
