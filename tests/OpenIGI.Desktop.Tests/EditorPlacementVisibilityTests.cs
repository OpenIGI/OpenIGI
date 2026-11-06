using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Host.Editor;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>The editor's layer of hidden and isolated objects over a title's view culling.</summary>
public sealed class EditorPlacementVisibilityTests
{
    /// <summary>A hidden object is not drawn; showing it again draws it; others are untouched.</summary>
    [Fact]
    public void Hiding_leaves_out_only_what_was_hidden()
    {
        (WorldPlacement a, WorldPlacement b) = (Placement(1), Placement(2));
        EditorPlacementVisibility visibility = new();
        Assert.True(visibility.IsEmpty);

        visibility.SetHidden(PlacementKey.Of(a), hidden: true);
        Assert.False(visibility.IsVisible(a));
        Assert.True(visibility.IsVisible(b));

        visibility.SetHidden(PlacementKey.Of(a), hidden: false);
        Assert.True(visibility.IsVisible(a));
    }

    /// <summary>Isolation draws only what is in the set; ending it draws everything; clearing forgets all.</summary>
    [Fact]
    public void Isolation_draws_only_its_set()
    {
        (WorldPlacement a, WorldPlacement b) = (Placement(1), Placement(2));
        EditorPlacementVisibility visibility = new();
        visibility.SetIsolated([PlacementKey.Of(b)]);
        Assert.False(visibility.IsVisible(a));
        Assert.True(visibility.IsVisible(b, Vector3d.Zero));

        visibility.SetIsolated(null);
        visibility.SetHidden(PlacementKey.Of(b), hidden: true);
        visibility.Clear();
        Assert.True(visibility.IsVisible(a) && visibility.IsVisible(b));
    }

    /// <summary>What the editor leaves alone is decided by the title's culling underneath.</summary>
    [Fact]
    public void Everything_else_is_the_titles_call()
    {
        WorldPlacement a = Placement(1);
        EditorPlacementVisibility visibility = new() { Inner = new HidesAll() };
        Assert.False(visibility.IsVisible(a));
        Assert.Equal("nothing", visibility.DescribeView());
    }

    private static WorldPlacement Placement(int id) => new(
        Position: default,
        Orientation: Matrix3f.Identity,
        Instance: default,
        Pack: null!,
        Model: null!,
        TaskName: "Crate",
        Owner: null!)
    {
        LevelId = id,
    };

    private sealed class HidesAll : IPlacementVisibility
    {
        public bool IsOutsideVisible => false;

        public void BeginView(in Camera3D eye, float tangentHalfFovX, float tangentHalfFovY)
        {
        }

        public bool IsVisible(WorldPlacement placement) => false;

        public string DescribeView() => "nothing";
    }
}
