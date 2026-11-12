using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>A route table worked out from a graph's links, for a graph that ships none.</summary>
public sealed class GraphRouteFromLinksTests
{
    // Four nodes linked round their edge, 0-1-2-3-0, and across from 1 to 3, the short way to 2 being through 1; and 5 on its own.
    private static readonly GraphNode[] Nodes =
    [
        Node(0, 0, 0), Node(1, 10, 0), Node(2, 10, 10), Node(3, 0, 12), Node(5, 50, 50),
    ];

    private static readonly GraphLink[] Links =
    [
        new(0, 1, 1), new(1, 2, 1), new(2, 3, 1), new(0, 3, 1), new(1, 3, 1),
    ];

    [Fact]
    public void Each_step_is_a_neighbour_on_the_shortest_way_and_the_cost_falls_along_it()
    {
        GraphRouteTable routes = GraphRouteTable.FromLinks(8, Nodes, Links);

        Assert.True(routes.TryGetStep(0, 2, out int next, out float cost));
        Assert.Equal(1, next);
        Assert.Equal(20f, cost, 3);
        Assert.Equal([1, 2], routes.EnumerateRoute(0, 2));
        Assert.True(routes.TryGetStep(1, 2, out _, out float rest));
        Assert.True(rest < cost);
        Assert.Equal([3], routes.EnumerateRoute(0, 3));
    }

    [Fact]
    public void A_node_has_no_route_to_itself_nor_to_one_no_link_reaches()
    {
        GraphRouteTable routes = GraphRouteTable.FromLinks(8, Nodes, Links);

        Assert.Equal(GraphRouteTable.Sentinel, routes[2, 2]);
        Assert.Equal(GraphRouteTable.Sentinel, routes[0, 5]);
        Assert.Equal(GraphRouteTable.Sentinel, routes[0, 7]);
        Assert.Empty(routes.EnumerateRoute(5, 0));
    }

    private static GraphNode Node(int id, double x, double y) =>
        new(id, new Vector3d(x, y, 0), 0f, 1f, 0, string.Empty, GraphNodeCriteria.None);
}
