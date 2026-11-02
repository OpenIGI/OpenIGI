using System.Buffers.Binary;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.Ai;

/// <summary>Builds the all-pairs route table an I.G.I. graph file carries, as the original tools did.</summary>
/// <remarks>
/// <para>
/// The game only reads the table (<see cref="GraphRouteTable"/>); it was baked by the original editor. Measured against the
/// retail graphs, the bake is shortest paths over the links, both ways, each costing the straight-line distance between
/// its nodes in engine units. A search from each source node, relaxing links in file order and only on a strictly
/// shorter cost, gives the retail next node for every reachable pair of I.G.I.'s level 3 (325,788 of them), equal-cost
/// ties included, and the same set of unreachable pairs. Costs agree to float rounding: the original's summation order
/// is not known, so about half come out one unit in the last place apart.
/// </para>
/// <para>
/// Where a retail table disagrees by more, its graph's nodes were moved after the bake (level 1's graph 4019 has about
/// ten such links), and a rebuild brings it up to date.
/// </para>
/// </remarks>
public static class GraphRouteBuilder
{
    /// <summary>Builds a route table.</summary>
    /// <param name="maxNodes">The slot count the table is square over.</param>
    /// <param name="nodes">The nodes: id and position. Ids outside <c>[0, maxNodes)</c> are ignored.</param>
    /// <param name="links">The links as node-id pairs, in file order.</param>
    /// <returns>The table, <c>8 × maxNodes²</c> bytes, indexed <c>from + to × maxNodes</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxNodes"/> is negative.</exception>
    public static byte[] Build(int maxNodes, IReadOnlyList<(int Id, Vector3d Position)> nodes, IReadOnlyList<(int From, int To)> links)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxNodes);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(links);

        byte[] table = new byte[GraphRouteTable.EntryStride * maxNodes * maxNodes];
        (int sentinelNode, float sentinelCost) = GraphRouteTable.Sentinel;
        for (int at = 0; at < table.Length; at += GraphRouteTable.EntryStride)
        {
            BinaryPrimitives.WriteInt32LittleEndian(table.AsSpan(at), sentinelNode);
            BinaryPrimitives.WriteSingleLittleEndian(table.AsSpan(at + 4), sentinelCost);
        }

        Vector3d?[] positions = new Vector3d?[maxNodes];
        foreach ((int id, Vector3d position) in nodes)
        {
            if ((uint)id < (uint)maxNodes)
            {
                positions[id] = position;
            }
        }

        List<(int To, double Length)>[] adjacent = new List<(int, double)>[maxNodes];
        foreach ((int from, int to) in links)
        {
            if ((uint)from < (uint)maxNodes && (uint)to < (uint)maxNodes && positions[from] is { } a && positions[to] is { } b)
            {
                double length = (a - b).Length;
                (adjacent[from] ??= []).Add((to, length));
                (adjacent[to] ??= []).Add((from, length));
            }
        }

        double[] distance = new double[maxNodes];
        int[] firstStep = new int[maxNodes];
        PriorityQueue<int, double> open = new();
        for (int source = 0; source < maxNodes; source++)
        {
            if (positions[source] is null || adjacent[source] is null)
            {
                continue;
            }

            Array.Fill(distance, double.PositiveInfinity);
            Array.Fill(firstStep, -1);
            distance[source] = 0;
            open.Enqueue(source, 0);
            while (open.TryDequeue(out int node, out double reached))
            {
                if (reached > distance[node] || adjacent[node] is not { } edges)
                {
                    continue;
                }

                foreach ((int next, double length) in edges)
                {
                    double through = reached + length;
                    if (through < distance[next])
                    {
                        distance[next] = through;
                        firstStep[next] = node == source ? next : firstStep[node];
                        open.Enqueue(next, through);
                    }
                }
            }

            for (int target = 0; target < maxNodes; target++)
            {
                if (target != source && firstStep[target] >= 0)
                {
                    int at = GraphRouteTable.EntryStride * (source + (target * maxNodes));
                    BinaryPrimitives.WriteInt32LittleEndian(table.AsSpan(at), firstStep[target]);
                    BinaryPrimitives.WriteSingleLittleEndian(table.AsSpan(at + 4), (float)distance[target]);
                }
            }
        }

        return table;
    }
}
