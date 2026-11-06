using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Missions;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>Laying a navigation graph over ground: a grid on the ground, linked where a soldier can walk.</summary>
public sealed class NavGraphLayoutTests
{
    /// <summary>Flat ground is a full grid, every node linked to its eight neighbours, within the node budget.</summary>
    [Fact]
    public void Flat_ground_is_a_fully_linked_grid()
    {
        NavGraphLayout layout = NavGraphLayout.Build(new NavGraphRequest(new Vector3d(100, 100, 0), SideMetres: 90, MaximumNodes: 100), static (_, _) => 5.0);

        Assert.Equal(100, layout.Nodes.Count); // 10 × 10 at 10 m
        Assert.Equal(10, layout.SpacingMetres);
        Assert.All(layout.Nodes, static n => Assert.Equal(5.0, n.PositionMetres.Z));
        Assert.Equal([.. Enumerable.Range(1, 100)], layout.Nodes.Select(static n => n.Id));

        // Across, up and both diagonals between every neighbouring pair: 2 × 10 × 9 straight, 2 × 9 × 9 diagonal.
        Assert.Equal(180 + 162, layout.Links.Count);
    }

    /// <summary>A cliff is not walked over, and ground cut off from the rest is left out.</summary>
    [Fact]
    public void A_cliff_cuts_links_and_an_island_is_dropped()
    {
        // A 30 m wall along x = 150 splits the square; the smaller side goes.
        NavGraphLayout layout = NavGraphLayout.Build(
            new NavGraphRequest(new Vector3d(100, 100, 0), SideMetres: 90, MaximumNodes: 100),
            static (x, _) => x > 125 ? 30.0 : 0.0);

        Assert.All(layout.Nodes, static n => Assert.Equal(0.0, n.PositionMetres.Z));
        Assert.Equal(80, layout.Nodes.Count); // the 8 columns below the wall
    }

    /// <summary>A link whose line leaves the ground, over a ditch, is not walked: only the links beside it are.</summary>
    [Fact]
    public void A_ditch_between_nodes_cuts_the_link()
    {
        NavGraphLayout layout = NavGraphLayout.Build(
            new NavGraphRequest(new Vector3d(5, 0, 0), SideMetres: 10, MaximumNodes: 4),
            static (x, _) => Math.Abs(x - 5) < 1.5 ? -4.0 : 0.0);

        // Four corners 10 m apart, the ditch down the middle: what is left links along one side only.
        Assert.Single(layout.Links);
        Assert.Single(layout.Nodes.Select(static n => n.PositionMetres.X).Distinct());
    }

    /// <summary>A large square widens the spacing to keep within the node budget.</summary>
    [Fact]
    public void The_budget_widens_the_spacing()
    {
        NavGraphLayout layout = NavGraphLayout.Build(new NavGraphRequest(Vector3d.Zero, SideMetres: 512, MaximumNodes: 256), static (_, _) => 0.0);

        Assert.True(layout.Nodes.Count <= 256, $"{layout.Nodes.Count} nodes");
        Assert.True(layout.SpacingMetres > 30, $"{layout.SpacingMetres} m");
    }
}
