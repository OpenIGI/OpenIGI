using System.Buffers.Binary;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.Ai;

/// <summary>
/// The precomputed all-pairs route table an <c>AIGraph</c> carries. <b>There is no runtime A*</b> — a walk
/// is a table lookup per step.
/// </summary>
/// <remarks>
/// <para>
/// The table is <c>nMaxNodes²</c> entries of eight bytes, <c>{i32 nextNode, f32 cost}</c>, allocated and
/// filled with the sentinel by <c>0x5092E0</c>/<c>0x509340</c> and then overwritten wholesale from the
/// file's <c>RouteTable</c> blob.
/// </para>
/// <para>
/// <b>The indexing is <c>from + to × maxNodes</c></b> — column-major, the destination being the major axis
/// (<c>0x509390</c>). Getting this backwards is not a crash: the table stays in range and reads plausibly,
/// it just returns a node that is nowhere near the walker. With the right order the entry's node is a
/// <em>direct neighbour</em> of the node being walked from in <b>1088822 of 1088822</b> reachable retail
/// pairs; with the transpose it is one in 5.8% of them, which is what caught the mistake.
/// </para>
/// <para>
/// <see cref="Sentinel"/> is <c>{-1, -1.0f}</c> and means "no route". It fills the diagonal (a node has no
/// route to itself — all 8317 of them), every slot whose endpoints are not both occupied (all 3237161 of
/// them), and 5700 genuinely unreachable live pairs.
/// </para>
/// <para>
/// The cost is in engine units and <b>strictly decreases along a route</b> — verified for all 1039956
/// interior steps in the corpus, which is what guarantees <see cref="EnumerateRoute"/> terminates without
/// needing a visited set.
/// </para>
/// </remarks>
public sealed class GraphRouteTable
{
    /// <summary>The size of one entry in bytes.</summary>
    public const int EntryStride = 8;

    /// <summary>The node id an entry carries when there is no route.</summary>
    public const int NoRoute = -1;

    private readonly ReadOnlyMemory<byte> _data;

    // A table worked out from a graph's links rather than read: each destination's column, made as it is first asked for.
    private readonly RouteColumns? _columns;

    /// <summary>Initializes a new instance of the <see cref="GraphRouteTable"/> class.</summary>
    /// <param name="data">The table bytes, exactly <c>8 × maxNodes²</c> of them.</param>
    /// <param name="maxNodes">The slot count the file declares.</param>
    /// <exception cref="GraphFormatException"><paramref name="data"/> is not the right length.</exception>
    public GraphRouteTable(ReadOnlyMemory<byte> data, int maxNodes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxNodes);

        int expected = EntryStride * maxNodes * maxNodes;
        if (data.Length != expected)
        {
            throw new GraphFormatException(
                $"A route table for {maxNodes} slots must be {expected} bytes, not {data.Length}.");
        }

        _data = data;
        MaxNodes = maxNodes;
    }

    private GraphRouteTable(RouteColumns columns, int maxNodes)
    {
        _columns = columns;
        MaxNodes = maxNodes;
    }

    /// <summary>
    /// Works a graph's routes out from its links, for a graph that ships none -- another game's, which searches its routes as it
    /// walks them rather than looking them up.
    /// </summary>
    /// <param name="maxNodes">The slot count the graph declares.</param>
    /// <param name="nodes">Its nodes, where they stand.</param>
    /// <param name="links">Its links, each walked both ways.</param>
    /// <returns>The table.</returns>
    /// <remarks>
    /// A route is the shortest along the links, a link costing the distance between its nodes, so the cost strictly decreases
    /// along a route as a shipped table's does, and a node has no route to itself or to a node no link reaches. A destination's
    /// routes are worked out together (one search out from it) the first time any is asked for, so a table costs what its
    /// walkers' destinations need rather than the whole square.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxNodes"/> is negative.</exception>
    public static GraphRouteTable FromLinks(int maxNodes, IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphLink> links)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxNodes);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(links);
        return new GraphRouteTable(new RouteColumns(maxNodes, nodes, links), maxNodes);
    }

    /// <summary>Gets the slot count the table is square over.</summary>
    public int MaxNodes { get; }

    /// <summary>Gets the entry that means "no route".</summary>
    public static (int NextNode, float Cost) Sentinel => (NoRoute, -1.0f);

    /// <summary>Reads one entry.</summary>
    /// <param name="from">The node being walked from.</param>
    /// <param name="to">The destination node.</param>
    /// <returns>The next node to step to and the remaining cost, or <see cref="Sentinel"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either id is outside <c>[0, MaxNodes)</c>.</exception>
    public (int NextNode, float Cost) this[int from, int to]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(from);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(from, MaxNodes);
            ArgumentOutOfRangeException.ThrowIfNegative(to);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(to, MaxNodes);

            if (_columns is { } columns)
            {
                return columns.Entry(from, to);
            }

            // 0x509390: routeTable + 8 * (from + to * maxNodes).
            ReadOnlySpan<byte> entry = _data.Span.Slice(EntryStride * (from + (to * MaxNodes)), EntryStride);
            return (
                BinaryPrimitives.ReadInt32LittleEndian(entry),
                BinaryPrimitives.ReadSingleLittleEndian(entry[4..]));
        }
    }

    /// <summary>Gets the next node on the way from one node to another.</summary>
    /// <param name="from">The node being walked from.</param>
    /// <param name="to">The destination node.</param>
    /// <param name="next">Receives the next node's id.</param>
    /// <param name="cost">Receives the remaining cost in engine units.</param>
    /// <returns><see langword="true"/> when a route exists.</returns>
    /// <remarks>
    /// This is <c>0x5096C0</c>: it tests the node field against <c>-1</c> and nothing else, so the cost is
    /// whatever the table holds. The caller that fails this test is the one that prints
    /// <c>"CarAI: Error in graph %d. No link between nodes %d and %d."</c>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Either id is outside <c>[0, MaxNodes)</c>.</exception>
    public bool TryGetStep(int from, int to, out int next, out float cost)
    {
        (next, cost) = this[from, to];
        return next != NoRoute;
    }

    /// <summary>Walks the table from one node to another, yielding every node along the way.</summary>
    /// <param name="from">The starting node.</param>
    /// <param name="to">The destination node.</param>
    /// <returns>
    /// The nodes stepped to, <paramref name="to"/> last. Empty when no route exists. <paramref name="from"/>
    /// is not included.
    /// </returns>
    /// <remarks>
    /// The engine never materialises a whole route — a walker asks for one step per decision. This exists
    /// so a route can be tested and drawn. It is bounded by <see cref="MaxNodes"/> steps regardless of the
    /// table's contents, so a malformed table cannot hang the caller the way the original would.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Either id is outside <c>[0, MaxNodes)</c>.</exception>
    public IReadOnlyList<int> EnumerateRoute(int from, int to)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(from);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(from, MaxNodes);
        ArgumentOutOfRangeException.ThrowIfNegative(to);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(to, MaxNodes);

        List<int> route = [];
        int at = from;
        for (int guard = 0; guard <= MaxNodes; guard++)
        {
            if (at == to)
            {
                return route;
            }

            if (!TryGetStep(at, to, out int next, out _))
            {
                return [];
            }

            route.Add(next);
            at = next;
        }

        return [];
    }

    /// <inheritdoc/>
    public override string ToString() =>
        _columns is null ? $"route table over {MaxNodes} slots, {_data.Length} bytes" : $"route table over {MaxNodes} slots, from links";

    // Each destination's column of a table worked out from links: the next node toward it from every node, and what is left.
    private sealed class RouteColumns
    {
        // The least a link costs, so that the cost still falls along a route through nodes that stand on one another.
        private const double ShortestLink = 1e-3;

        private readonly int _maxNodes;
        private readonly Vector3d[] _positions;
        private readonly List<(int Node, double Length)>[] _neighbours;
        private readonly Dictionary<int, (int[] Next, float[] Cost)> _columns = [];

        public RouteColumns(int maxNodes, IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphLink> links)
        {
            _maxNodes = maxNodes;
            _positions = new Vector3d[maxNodes];
            bool[] present = new bool[maxNodes];
            _neighbours = new List<(int, double)>[maxNodes];
            foreach (GraphNode node in nodes)
            {
                if ((uint)node.Id < (uint)maxNodes)
                {
                    present[node.Id] = true;
                    _positions[node.Id] = node.Position;
                }
            }

            foreach (GraphLink link in links)
            {
                if ((uint)link.NodeA < (uint)maxNodes && (uint)link.NodeB < (uint)maxNodes && present[link.NodeA] && present[link.NodeB]
                    && link.NodeA != link.NodeB)
                {
                    double length = Math.Max(Vector3d.Distance(_positions[link.NodeA], _positions[link.NodeB]), ShortestLink);
                    (_neighbours[link.NodeA] ??= []).Add((link.NodeB, length));
                    (_neighbours[link.NodeB] ??= []).Add((link.NodeA, length));
                }
            }
        }

        public (int NextNode, float Cost) Entry(int from, int to)
        {
            if (!_columns.TryGetValue(to, out (int[] Next, float[] Cost) column))
            {
                column = Search(to);
                _columns[to] = column;
            }

            int next = column.Next[from];
            return next == NoRoute ? Sentinel : (next, column.Cost[from]);
        }

        // One search out from the destination: each node reached steps to the node it was reached from.
        private (int[] Next, float[] Cost) Search(int to)
        {
            int[] next = new int[_maxNodes];
            double[] distance = new double[_maxNodes];
            Array.Fill(next, NoRoute);
            Array.Fill(distance, double.PositiveInfinity);
            distance[to] = 0.0;
            PriorityQueue<int, double> open = new();
            open.Enqueue(to, 0.0);
            while (open.TryDequeue(out int at, out double reached))
            {
                if (reached > distance[at] || _neighbours[at] is not { } around)
                {
                    continue;
                }

                foreach ((int node, double length) in around)
                {
                    double through = reached + length;
                    if (through < distance[node])
                    {
                        distance[node] = through;
                        next[node] = at;
                        open.Enqueue(node, through);
                    }
                }
            }

            float[] cost = new float[_maxNodes];
            for (int node = 0; node < _maxNodes; node++)
            {
                cost[node] = next[node] == NoRoute ? -1.0f : (float)distance[node];
            }

            return (next, cost);
        }
    }
}
