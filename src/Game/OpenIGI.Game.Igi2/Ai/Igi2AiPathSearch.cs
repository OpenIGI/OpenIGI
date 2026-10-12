using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Ai;

/// <summary>One node of an A* search, <c>AIGraphAStarSearchNode_s</c>.</summary>
public sealed class Igi2AiSearchNode
{
    /// <summary>Gets or sets the graph node it stands for.</summary>
    public int NodeId { get; set; } = -1;

    /// <summary>Gets or sets the node the search reached it from, or <see langword="null"/>.</summary>
    public Igi2AiSearchNode? Parent { get; set; }

    /// <summary>Gets or sets the cost of the way to it, <c>vCostToNode</c>.</summary>
    public float CostToNode { get; set; }

    /// <summary>Gets or sets the straight distance on to the destination, <c>vEstimatedCostToDest</c>.</summary>
    public float EstimatedCostToDest { get; set; }

    /// <summary>Gets or sets the two summed, <c>vEstimatedTotalCost</c>, which orders the open list.</summary>
    public float EstimatedTotalCost { get; set; }
}

/// <summary>
/// The pool every search claims its nodes from, <c>AIGraphAStarSearch_atTakenAStarSearchNodeBuffer</c>: ten thousand, shared by
/// every soldier; a search that finds the pool spent fails and lets its nodes go.
/// </summary>
public sealed class Igi2AiSearchPool
{
    /// <summary>The pool's size.</summary>
    public const int Capacity = 10000;

    /// <summary>Gets how many nodes are taken.</summary>
    public int Taken { get; private set; }

    /// <summary>Takes a fresh node, or <see langword="null"/> when none is left (<c>AIGraphAStarSearch_ClaimSearchNode</c>).</summary>
    /// <returns>The node.</returns>
    public Igi2AiSearchNode? Claim()
    {
        if (Taken >= Capacity)
        {
            return null;
        }

        Taken++;
        return new Igi2AiSearchNode();
    }

    /// <summary>Gives a node back, <c>AIGraphAStarSearch_ReleaseSearchNode</c>.</summary>
    public void Release() => Taken--;
}

/// <summary>
/// A soldier's A* search over a graph, <c>AIGraphAStarSearchData_s</c> (<c>aigraph.c</c>): it may run over several ticks, a
/// number of nodes a call, and keeps the path it found until it is cleared.
/// </summary>
/// <remarks>
/// <para>
/// The open list is kept sorted by estimated total cost, a new node going after those that cost no more. A link's cost is its
/// length, 81920 more where it joins a vehicle node to a node that is not one, plus the weight of the node it leads to; the
/// estimate is the straight distance to the destination. A node reached again more cheaply, open or closed, is moved back into
/// the open list.
/// </para>
/// <para>
/// Called again for the same graph, source and destination, the search resumes where it stopped. Once the path is built the
/// open and closed lists are let go, so a call after that finds nothing to search and fails, as the original's does: a caller
/// asks for the path first.
/// </para>
/// </remarks>
public sealed class Igi2AiPathSearch
{
    /// <summary>The cost added for a link between a vehicle node and one that is not, twenty metres.</summary>
    public const float VehicleLinkCost = 81920f;

    private readonly Igi2AiSearchPool _pool;
    private readonly List<Igi2AiSearchNode> _open = [];
    private readonly LinkedList<Igi2AiSearchNode> _closed = new();
    private readonly List<Igi2AiSearchNode> _path = [];

    /// <summary>Initializes a new instance of the <see cref="Igi2AiPathSearch"/> class.</summary>
    /// <param name="pool">The pool shared by every search.</param>
    public Igi2AiPathSearch(Igi2AiSearchPool pool) => _pool = pool ?? throw new ArgumentNullException(nameof(pool));

    /// <summary>Gets or sets the search's flags: bit 1 means it ran out of nodes to search without reaching the destination.</summary>
    public int Flags { get; set; }

    /// <summary>Gets the graph searched, or -1.</summary>
    public int GraphId { get; private set; } = -1;

    /// <summary>Gets the node the search starts from, or -1.</summary>
    public int SourceNodeId { get; private set; } = -1;

    /// <summary>Gets the node the search is for, or -1.</summary>
    public int DestNodeId { get; private set; } = -1;

    /// <summary>Gets the path's cost, <c>vPathCost</c>.</summary>
    public float PathCost { get; private set; }

    /// <summary>Gets the path found, source first, or nothing.</summary>
    public IReadOnlyList<Igi2AiSearchNode> CurrentPath => _path;

    /// <summary>Gets the path found for a search, or <see langword="null"/> when that is not the search kept (<c>AIGraphAStarSearch_GetPath</c>).</summary>
    /// <param name="graphId">The graph.</param>
    /// <param name="source">The source node.</param>
    /// <param name="dest">The destination node.</param>
    /// <returns>The path, source first.</returns>
    public IReadOnlyList<Igi2AiSearchNode>? GetPath(int graphId, int source, int dest) =>
        GraphId == graphId && SourceNodeId == source && DestNodeId == dest && _path.Count != 0 ? _path : null;

    /// <summary>Takes the first node off the path, <c>AIGraphAStarSearch_RemoveHeadFromCurrentPath</c>.</summary>
    public void RemoveHeadFromCurrentPath()
    {
        if (_path.Count != 0)
        {
            _path.RemoveAt(0);
            _pool.Release();
        }
    }

    /// <summary>Lets the search go: its path and lists emptied, its ids reset (<c>AIGraphAStarSearch_Clear</c>).</summary>
    public void Clear()
    {
        Release(_path);
        Release(_open);
        foreach (Igi2AiSearchNode _ in _closed)
        {
            _pool.Release();
        }

        _closed.Clear();
        SourceNodeId = -1;
        DestNodeId = -1;
        GraphId = -1;
    }

    /// <summary>
    /// Searches, or goes on searching, for a path, checking at most a number of nodes (<c>AIGraphAStarSearch_CreatePath</c>).
    /// </summary>
    /// <param name="graph">The graph.</param>
    /// <param name="source">The node to start from.</param>
    /// <param name="dest">The node to reach.</param>
    /// <param name="maxNodesToCheck">The most nodes checked in this call.</param>
    /// <returns>
    /// <see langword="true"/> when the path was found, or when the search must go on; <see langword="false"/> when it cannot be
    /// made (no such destination, the pool spent, or nothing left to search, which raises bit 1 of <see cref="Flags"/>).
    /// </returns>
    public bool CreatePath(Igi2AiGraph graph, int source, int dest, uint maxNodesToCheck)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (graph.NodeAt(dest) is not { } destNode)
        {
            return false;
        }

        Dictionary<int, Igi2AiSearchNode> openTable = [];
        Dictionary<int, Igi2AiSearchNode> closedTable = [];
        if (source == SourceNodeId && dest == DestNodeId && graph.Id == GraphId)
        {
            foreach (Igi2AiSearchNode node in _open)
            {
                openTable[node.NodeId] = node;
            }

            foreach (Igi2AiSearchNode node in _closed)
            {
                closedTable[node.NodeId] = node;
            }
        }
        else
        {
            Clear();
            Igi2AiGraphNode? sourceNode = graph.NodeAt(source);
            if (_pool.Claim() is not { } start || sourceNode is null)
            {
                Clear();
                return false;
            }

            start.NodeId = source;
            start.Parent = null;
            start.CostToNode = 0f;
            float sx = ((Fpu)sourceNode.Position.X - destNode.Position.X).F;
            Fpu sy = (Fpu)sourceNode.Position.Y - destNode.Position.Y;
            Fpu sz = (Fpu)sourceNode.Position.Z - destNode.Position.Z;
            Fpu estimate = Fpu.Sqrt(((sz * sz) + (sy * sy)) + ((Fpu)sx * sx));
            start.EstimatedCostToDest = estimate.F;
            start.EstimatedTotalCost = estimate.F;
            Flags = 0;
            GraphId = graph.Id;
            DestNodeId = dest;
            SourceNodeId = source;
            InsertOpen(start);
            openTable[source] = start;
        }

        uint checkedNodes = 0;
        while (_open.Count != 0)
        {
            if (checkedNodes >= maxNodesToCheck)
            {
                return true;
            }

            Igi2AiSearchNode current = _open[0];
            if (current.NodeId == DestNodeId)
            {
                BuildPath(current);
                return true;
            }

            Igi2AiGraphNode here = graph.NodeAt(current.NodeId)!;
            foreach (Igi2AiGraphLink link in here.Links)
            {
                int next = link.Other(current.NodeId);
                Igi2AiGraphNode neighbour = graph.NodeAt(next)!;
                Fpu cost = (Fpu)link.Length + current.CostToNode;
                if ((Igi2AiGraph.VehicleCriteria & here.Criteria) != (Igi2AiGraph.VehicleCriteria & neighbour.Criteria))
                {
                    cost += VehicleLinkCost;
                }

                float costToNode = (cost + neighbour.Weight).F;
                float tx = ((Fpu)destNode.Position.X - neighbour.Position.X).F;
                float ty = ((Fpu)destNode.Position.Y - neighbour.Position.Y).F;
                Fpu tz = (Fpu)destNode.Position.Z - neighbour.Position.Z;
                float costToDest = Fpu.Sqrt((((Fpu)tx * tx) + ((Fpu)ty * ty)) + (tz * tz)).F;
                float total = ((Fpu)costToDest + costToNode).F;

                Igi2AiSearchNode? found;
                if (openTable.TryGetValue(next, out Igi2AiSearchNode? open))
                {
                    if (!(total <= open.EstimatedTotalCost))
                    {
                        continue;
                    }

                    _open.Remove(open);
                    openTable.Remove(next);
                    found = open;
                }
                else if (closedTable.TryGetValue(next, out Igi2AiSearchNode? closed))
                {
                    if (!(total <= closed.EstimatedTotalCost))
                    {
                        continue;
                    }

                    _closed.Remove(closed);
                    closedTable.Remove(next);
                    found = closed;
                }
                else if (_pool.Claim() is { } claimed)
                {
                    found = claimed;
                }
                else
                {
                    Clear();
                    return false;
                }

                found.NodeId = next;
                found.Parent = current;
                found.CostToNode = costToNode;
                found.EstimatedTotalCost = total;
                found.EstimatedCostToDest = costToDest;
                InsertOpen(found);
                openTable[next] = found;
            }

            _open.Remove(current);
            _closed.AddFirst(current);
            closedTable[current.NodeId] = current;
            checkedNodes++;
        }

        Flags |= 1;
        Clear();
        return false;
    }

    private void InsertOpen(Igi2AiSearchNode node)
    {
        int at = _open.FindIndex(o => !(o.EstimatedTotalCost <= node.EstimatedTotalCost));
        if (at < 0)
        {
            _open.Add(node);
        }
        else
        {
            _open.Insert(at, node);
        }
    }

    /// <summary>Copies the way back from the destination into the path, then lets the lists go (<c>AIGraphAStarSearch_BuildPathFromClosedList</c>).</summary>
    private void BuildPath(Igi2AiSearchNode destination)
    {
        PathCost = destination.CostToNode;
        for (Igi2AiSearchNode? step = destination; step is not null; step = step.Parent)
        {
            if (_pool.Claim() is not { } copy)
            {
                Clear();
                return;
            }

            copy.NodeId = step.NodeId;
            copy.CostToNode = step.CostToNode;
            copy.EstimatedCostToDest = step.EstimatedCostToDest;
            copy.EstimatedTotalCost = step.EstimatedTotalCost;
            _path.Insert(0, copy);
        }

        Release(_open);
        foreach (Igi2AiSearchNode _ in _closed)
        {
            _pool.Release();
        }

        _closed.Clear();
    }

    private void Release(List<Igi2AiSearchNode> nodes)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            _pool.Release();
        }

        nodes.Clear();
    }
}
