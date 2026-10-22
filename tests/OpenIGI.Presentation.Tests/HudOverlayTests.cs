using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Presentation.Hud;
using OpenIGI.TestSupport;
using OpenIGI.UI.Layout;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>A mod's HUD overlays are made for each level in order, and place a world point where the view draws it.</summary>
public sealed class HudOverlayTests
{
    [Fact]
    public void Each_overlay_is_made_once_in_the_order_registered_and_one_with_nothing_to_draw_is_left_out()
    {
        PresenterRegistry registry = new PresenterRegistry()
            .ForHud(static _ => new Overlay("first"))
            .ForHud(static _ => null)
            .ForHud(static _ => new Overlay("last"));
        TaskPresenterRequest request = new(new RecordingGraphicsDevice(), new ResourceRegistry(new VirtualFileSystem()), new Root(), NullLogger.Instance);

        IReadOnlyList<IHudOverlay> made = registry.CreateHudOverlays(request);

        Assert.Equal(3, registry.HudOverlayCount);
        Assert.Equal(["first", "last"], made.Cast<Overlay>().Select(static o => o.Name));
    }

    [Fact]
    public void A_point_ahead_is_at_the_projections_centre_and_one_behind_is_nowhere()
    {
        Matrix4f projection = Matrix4f.CreateProjection(400f, 400f, 320f, 240f, 640, 480, 1f, 1000f);
        Camera3D eye = new(new Vector3d(10, 20, 30), Matrix3f.Identity);

        UiVec2? ahead = HudProjection.ToScreen(eye, projection, new Vector3d(10, 20, 130), 640f, 480f);
        UiVec2? aside = HudProjection.ToScreen(eye, projection, new Vector3d(60, 45, 130), 640f, 480f);

        Assert.NotNull(ahead);
        Assert.Equal(320f, ahead.Value.X, 3);
        Assert.Equal(240f, ahead.Value.Y, 3);

        // The original's own pixel: the centre plus the scale times the coordinate over the depth.
        Assert.NotNull(aside);
        Assert.Equal(320f + (400f * 50f / 100f), aside.Value.X, 3);
        Assert.Equal(240f + (400f * 25f / 100f), aside.Value.Y, 3);

        Assert.Null(HudProjection.ToScreen(eye, projection, new Vector3d(10, 20, -70), 640f, 480f));
    }

    private sealed class Overlay(string name) : IHudOverlay
    {
        public string Name { get; } = name;

        public void Draw(in HudOverlayFrame frame)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class Root : GameTask;
}
