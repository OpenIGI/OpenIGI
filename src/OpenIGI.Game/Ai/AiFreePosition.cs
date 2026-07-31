using OpenIGI.Formats.Ai;

namespace OpenIGI.Game.Ai;

/// <summary>
/// <c>FindFreePosition</c> — <c>sub_48B050</c> and its acceptance test <c>sub_48B260</c>.
/// </summary>
/// <remarks>
/// <para>
/// It answers <i>"where should I stand to deal with this target"</i>, and it is where the "guards never
/// take cover" complaint is really answered: the original has <b>no cover concept at all</b>, only
/// <i>can I shoot from there</i>.
/// </para>
/// <para>
/// The search is a breadth-first walk outward from <b>the target's</b> nearest node, expanding at most
/// <see cref="DefaultBudget"/> new nodes, and it runs no pathfinding: the direction test is three
/// lookups in §1's precomputed route table.
/// </para>
/// </remarks>
public static class AiFreePosition
{
    /// <summary>How many new nodes the search may expand — the original always passes five.</summary>
    public const int DefaultBudget = 5;

    /// <summary>
    /// The node-count at which the original refuses the search rather than overflowing its queue.
    /// </summary>
    /// <remarks>
    /// The queue is a 1024-entry array with a 1024-byte visited map, and a graph declaring 1024 or more
    /// slots <b>aborts with a message</b>. No retail graph comes close — the largest declares 400.
    /// </remarks>
    public const int NodeLimit = 1024;

    /// <summary>Looks for somewhere to stand.</summary>
    /// <param name="graph">The level's navigation graph.</param>
    /// <param name="myNodeId">The soldier's own node — <c>HumanAI+15916</c>.</param>
    /// <param name="targetNodeId">The node nearest the target — <c>0x453200</c>.</param>
    /// <param name="towardTarget">
    /// Whether to apply the direction test. <b>This one flag is the whole difference between advancing
    /// and searching</b>: the chooser passes <see langword="true"/> while it can see the target and
    /// <see langword="false"/> while it cannot, so a patrol that has lost you takes <i>any</i> free node
    /// near where you were, and spreads out.
    /// </param>
    /// <param name="canShootFrom">
    /// The line-of-fire test, <c>sub_48B6E0</c>: a ray from the node's position to the target's aim
    /// point, accepted when clear or when the blocker sits at ≥ 0.79 of the way. Pass
    /// <see langword="null"/> to skip it — <b>a recorded divergence</b>, because without it a soldier
    /// will advance to somewhere it cannot actually shoot from.
    /// </param>
    /// <param name="budget">How many new nodes may be expanded.</param>
    /// <returns>The node to stand on, or <see langword="null"/> when the search finds nothing.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
    public static int? Find(
        GraphFile graph,
        int myNodeId,
        int targetNodeId,
        bool towardTarget,
        Func<int, bool>? canShootFrom = null,
        int budget = DefaultBudget)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (graph.MaxNodes >= NodeLimit || targetNodeId < 0 || myNodeId < 0)
        {
            return null;
        }

        if (Accept(graph, targetNodeId, myNodeId, targetNodeId, towardTarget, canShootFrom))
        {
            return targetNodeId;
        }

        HashSet<int> visited = [targetNodeId];
        Queue<int> queue = new();
        queue.Enqueue(targetNodeId);
        int expanded = 0;

        while (queue.Count > 0 && expanded < budget)
        {
            int node = queue.Dequeue();
            expanded++;

            foreach (GraphLink link in graph.LinksOf(node))
            {
                int neighbour = link.Other(node);
                if (neighbour < 0 || !visited.Add(neighbour))
                {
                    continue;
                }

                if (Accept(graph, neighbour, myNodeId, targetNodeId, towardTarget, canShootFrom))
                {
                    return neighbour;
                }

                queue.Enqueue(neighbour);
            }
        }

        return null;
    }

    /// <summary>
    /// The direction test — a triangle inequality over the precomputed route table.
    /// </summary>
    /// <param name="graph">The level's navigation graph.</param>
    /// <param name="candidateId">The node being considered.</param>
    /// <param name="myNodeId">The soldier's own node.</param>
    /// <param name="targetNodeId">The node nearest the target.</param>
    /// <returns>
    /// <see langword="true"/> when the candidate lies on, or shortens, the route between the soldier and
    /// the target.
    /// </returns>
    /// <remarks>
    /// <c>cost(c → me) + cost(c → target) &gt; cost(me → target)</c> rejects. No pathfinding runs; all
    /// three figures are table lookups.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
    public static bool ShortensTheRoute(GraphFile graph, int candidateId, int myNodeId, int targetNodeId)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (graph.RouteTable is null)
        {
            return true;
        }

        float toMe = graph.RouteTable[candidateId, myNodeId].Cost;
        float toTarget = graph.RouteTable[candidateId, targetNodeId].Cost;
        float direct = graph.RouteTable[myNodeId, targetNodeId].Cost;

        if (toMe < 0.0f || toTarget < 0.0f || direct < 0.0f)
        {
            // A sentinel row: unreachable either way, so the candidate cannot be on the route.
            return false;
        }

        return toMe + toTarget <= direct;
    }

    private static bool Accept(
        GraphFile graph,
        int candidateId,
        int myNodeId,
        int targetNodeId,
        bool towardTarget,
        Func<int, bool>? canShootFrom)
    {
        if (graph.NodeAt(candidateId) is null)
        {
            return false;
        }

        if (towardTarget && !ShortensTheRoute(graph, candidateId, myNodeId, targetNodeId))
        {
            return false;
        }

        return canShootFrom?.Invoke(candidateId) ?? true;
    }
}
