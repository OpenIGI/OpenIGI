using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.Ai;

/// <summary>How a nearest-node query ended.</summary>
/// <remarks>
/// <c>0x5096F0</c> returns three distinct values, and the two success codes are not interchangeable: the
/// caller can tell "the nearest node is over there" from "you are standing in it".
/// </remarks>
public enum GraphNearestNodeResult
{
    /// <summary>No node qualified — the graph is empty or every node is excluded.</summary>
    None = 0,

    /// <summary>A node was found and the target is <b>outside</b> its radius.</summary>
    Outside = 1,

    /// <summary>A node was found and the target is <b>inside</b> its radius.</summary>
    Inside = 2,
}

/// <summary>Finds the graph node nearest a world position — <c>0x5096F0</c>.</summary>
/// <remarks>
/// <para>
/// This is how a guard turns a position into somewhere on the graph. It is a <b>linear scan of every slot</b>
/// with no spatial index, which is affordable because a graph holds at most a few hundred nodes and the query
/// is not per-tick.
/// </para>
/// <para>
/// Three details that are not obvious:
/// </para>
/// <list type="bullet">
/// <item>
/// the score is <c>distance − radius × 4096</c>, so a node's radius <b>pulls it closer</b> in the ranking. A
/// large sloppy node beats a small precise one that is slightly nearer — which is what fixes the radius's
/// unit as metres;
/// </item>
/// <item>
/// <b>nodes carrying <see cref="GraphNodeCriteria.View"/> are skipped entirely</b>, so an observation post can
/// never be the answer;
/// </item>
/// <item>
/// the engine keeps the <b>best three</b> in an insertion-sorted array and then returns only the first. The
/// other two are computed and discarded. They are exposed here because a discarded computation that precise
/// is a lead, not noise.
/// </item>
/// </list>
/// </remarks>
public static class GraphNearestNode
{
    /// <summary>How many candidates the engine ranks before returning the best.</summary>
    public const int CandidateCount = 3;

    /// <summary>The score every candidate slot starts at — <c>2^26</c> units, about 16 km.</summary>
    public const double NoCandidateScore = 67108864.0;

    /// <summary>Finds the node nearest a position.</summary>
    /// <param name="graph">The graph to search.</param>
    /// <param name="target">The world position, in engine units.</param>
    /// <param name="best">Receives the nearest node, or <see langword="null"/> when none qualifies.</param>
    /// <param name="ranked">
    /// Receives the best three in order, shortest score first, padded with <see langword="null"/>. The engine
    /// computes these and uses only the first.
    /// </param>
    /// <returns>Which of the three outcomes applied.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
    public static GraphNearestNodeResult Find(
        GraphFile graph,
        Vector3d target,
        out GraphNode? best,
        out IReadOnlyList<GraphNode?> ranked)
    {
        ArgumentNullException.ThrowIfNull(graph);

        GraphNode?[] nodes = new GraphNode?[CandidateCount];
        double[] scores = [NoCandidateScore, NoCandidateScore, NoCandidateScore];

        for (int id = 0; id < graph.MaxNodes; id++)
        {
            GraphNode? node = graph.NodeAt(id);
            if (node is null || node.IsExcludedFromNearestQuery)
            {
                continue;
            }

            // 0x4B32B0 sums z first; the whole score is x87 arithmetic rounded toward zero.
            X87 dx = (X87)node.Position.X - target.X;
            X87 dy = (X87)node.Position.Y - target.Y;
            X87 dz = (X87)node.Position.Z - target.Z;
            double score = (X87.Sqrt(dz * dz + dy * dy + dx * dx) - ((X87)node.Radius * 4096.0f)).Value;

            Insert(nodes, scores, node, score);
        }

        ranked = nodes;
        best = nodes[0];
        if (best is null)
        {
            return GraphNearestNodeResult.None;
        }

        // The sign of the best score is the difference between "near this node" and "at it".
        return scores[0] >= 0.0 ? GraphNearestNodeResult.Outside : GraphNearestNodeResult.Inside;
    }

    private static void Insert(GraphNode?[] nodes, double[] scores, GraphNode node, double score)
    {
        for (int slot = 0; slot < CandidateCount; slot++)
        {
            if (nodes[slot] is null)
            {
                nodes[slot] = node;
                scores[slot] = score;
                return;
            }

            if (score < scores[slot])
            {
                for (int shift = CandidateCount - 1; shift > slot; shift--)
                {
                    nodes[shift] = nodes[shift - 1];
                    scores[shift] = scores[shift - 1];
                }

                nodes[slot] = node;
                scores[slot] = score;
                return;
            }
        }

        // All three are full and this beats none of them: dropped.
    }
}
