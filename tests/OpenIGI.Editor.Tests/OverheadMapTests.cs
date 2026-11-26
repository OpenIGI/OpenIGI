using OpenIGI.Core.Graphics;
using OpenIGI.Editor.Map;
using OpenIGI.Host.Editor;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>
/// The map from above cut at a storey: a 40 m square of ground with a 10 m building in its middle, slabs at 0.2 m, 3.4 m and a
/// roof at 6.6 m, each a colour of its own; the floor under a point and the picture at each cut.
/// </summary>
public sealed class OverheadMapTests
{
    private static readonly Bgra32 Blue = Bgra32.FromRgb(0, 0, 255);
    private static readonly Bgra32 Red = Bgra32.FromRgb(255, 0, 0);
    private static readonly Bgra32 Green = Bgra32.FromRgb(0, 255, 0);
    private static readonly Bgra32 Yellow = Bgra32.FromRgb(255, 255, 0);

    /// <summary>The floor under the building's middle is the highest slab the cut keeps; outside, the ground.</summary>
    [Fact]
    public void The_floor_under_a_point_is_the_highest_the_cut_keeps()
    {
        FloorPlan plan = new(Scene());
        Assert.Equal(6.6, plan.Surface(1020, 2020, double.PositiveInfinity), 3);
        Assert.Equal(3.4, plan.Surface(1020, 2020, FloorPlan.Floors[2].Cut), 3);
        Assert.Equal(0.2, plan.Surface(1020, 2020, FloorPlan.Floors[1].Cut), 3);
        Assert.Equal(0.0, plan.Surface(1005, 2005, double.PositiveInfinity), 3);
        Assert.Equal(3.4, plan.FloorUnder(1020, 2020, 3.5), 3);
        Assert.True(FloorPlan.IsOn(FloorPlan.Floors[2], 3.4));
        Assert.False(FloorPlan.IsOn(FloorPlan.Floors[2], 0.2));
    }

    /// <summary>Each cut shows the storey under it: the roof whole, then floor 1's slab, then the ground floor's.</summary>
    [Fact]
    public void Each_cut_shows_the_storey_under_it()
    {
        OverheadScene scene = Scene();
        OverheadRaster raster = new(scene, new FloorPlan(scene), OpenIGI.Core.Threading.InlineWorkScheduler.Instance);
        Bgra32 Middle(int floor, bool walls = false) => raster.Render(View(floor, walls))[(20 * 40) + 20];
        Assert.Equal(Red, Middle(0));
        Assert.Equal(Green, Middle(2));
        Assert.Equal(Yellow, Middle(1));

        // Off the building, the ground at every cut.
        Bgra32[] ground = raster.Render(View(2, walls: false));
        Assert.Equal(Blue, ground[(5 * 40) + 5]);
    }

    /// <summary>A wall the cut passes through is drawn thick and black along its foot.</summary>
    [Fact]
    public void A_cut_wall_is_drawn_black()
    {
        OverheadScene scene = Scene();
        OverheadRaster raster = new(scene, new FloorPlan(scene), OpenIGI.Core.Threading.InlineWorkScheduler.Instance);
        Bgra32[] picture = raster.Render(View(2, walls: true));

        // The building's west wall runs north-south at x = 15 m: the pixel on it is near black, the one inside is floor 1's.
        Bgra32 wall = picture[(20 * 40) + 15];
        Assert.True(wall.R < 30 && wall.G < 30 && wall.B < 30, $"wall pixel {wall}");
        Assert.Equal(Green, picture[(20 * 40) + 20]);
    }

    // One metre a pixel over the scene's 40 m square, cut at a storey.
    private static OverheadView View(int floor, bool walls) => new(1000, 2040, 1.0, 40, 40, FloorPlan.Floors[floor], walls);

    // The ground square at nought, the building from 15 to 25 m both ways: its slabs as model faces and as collision, and
    // its four walls as collision from the ground to the roof.
    private static OverheadScene Scene()
    {
        OverheadTexture Plain(Bgra32 colour) => new(1, 1, [colour], Repeat: true);
        float[] Square(float from, float to, float z, float under) =>
        [
            from, from, z, 0, 0, 1, under, to, from, z, 0, 0, 1, under, to, to, z, 0, 0, 1, under,
            from, from, z, 0, 0, 1, under, to, to, z, 0, 0, 1, under, from, to, z, 0, 0, 1, under,
        ];

        List<float> collision = [];
        void Slab(float z)
        {
            collision.AddRange([15, 15, z, 25, 15, z, 25, 25, z, 0]);
            collision.AddRange([15, 15, z, 25, 25, z, 15, 25, z, 0]);
        }

        void Wall(float ax, float ay, float bx, float by)
        {
            collision.AddRange([ax, ay, 0, bx, by, 0, bx, by, 6.6f, 0]);
            collision.AddRange([ax, ay, 0, bx, by, 6.6f, ax, ay, 6.6f, 0]);
        }

        Slab(0.2f);
        Slab(3.4f);
        Slab(6.6f);
        Wall(15, 15, 15, 25);
        Wall(25, 15, 25, 25);
        Wall(15, 15, 25, 15);
        Wall(15, 25, 25, 25);
        return new OverheadScene(
            1000,
            2000,
            1040,
            2040,
            [Plain(Blue), Plain(Yellow), Plain(Green), Plain(Red)],
            [
                new OverheadGroup(OverheadPass.GroundFirst, 0, Square(0, 40, 0, 0)),
                new OverheadGroup(OverheadPass.Solid, 1, Square(15, 25, 0.2f, 0)),
                new OverheadGroup(OverheadPass.Solid, 2, Square(15, 25, 3.4f, 0)),
                new OverheadGroup(OverheadPass.Solid, 3, Square(15, 25, 6.6f, 0)),
            ])
        {
            Collision = [.. collision],
        };
    }
}
