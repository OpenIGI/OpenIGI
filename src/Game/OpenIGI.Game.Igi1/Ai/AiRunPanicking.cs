using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>The IGI1 <c>AIAction_RunPanicking</c> destination search.</summary>
/// <remarks>
/// <para>
/// The action's first real argument is a distance expressed in metres, not a lifetime. Retail multiplies
/// it by 4096, rejects an event already farther away than that, then breadth-first walks from the graph
/// node nearest the event. The first usable node outside the radius wins. The search admits at most ten
/// newly discovered nodes (<c>0x48E270</c>, <c>0x48E300</c>, <c>0x48E430</c>).
/// </para>
/// </remarks>
public static class AiRunPanicking
{
    /// <summary>IGI1's fixed breadth-first discovery budget.</summary>
    public const int SearchBudget = 10;

    /// <summary>Engine units in one metre, as used directly by the retail action.</summary>
    public const double UnitsPerMetre = 4096.0;

    /// <summary>Finds the graph node a panicking human should run to.</summary>
    /// <param name="graph">The human's navigation graph.</param>
    /// <param name="humanPosition">The human's current feet position.</param>
    /// <param name="eventPosition">The position copied from the event that installed the action.</param>
    /// <param name="distanceMetres">The action's first argument.</param>
    /// <param name="canUseNode">The original free-position test; omitted when no occupancy service exists.</param>
    /// <returns>The first suitable node id, or <see langword="null"/>.</returns>
    public static int? FindDestination(
        GraphFile graph,
        Vector3d humanPosition,
        Vector3d eventPosition,
        double distanceMetres,
        Func<int, bool>? canUseNode = null)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (graph.MaxNodes >= AiFreePosition.NodeLimit || distanceMetres < 0.0)
        {
            return null;
        }

        double radius = distanceMetres * UnitsPerMetre;
        if (Vector3d.Distance(humanPosition, eventPosition) > radius)
        {
            return null;
        }

        GraphNode? closest = null;
        double closestSquared = double.MaxValue;
        foreach (GraphNode node in graph.Nodes)
        {
            if (node.IsExcludedFromNearestQuery)
            {
                continue;
            }

            double squared = Vector3d.DistanceSquared(node.Position, eventPosition);
            if (squared < closestSquared)
            {
                closest = node;
                closestSquared = squared;
            }
        }

        if (closest is null)
        {
            return null;
        }

        HashSet<int> visited = [closest.Id];
        Queue<int> queue = new();
        queue.Enqueue(closest.Id);
        int discovered = 0;

        while (queue.Count > 0)
        {
            int candidate = queue.Dequeue();
            GraphNode? node = graph.NodeAt(candidate);
            if (node is not null
                && Vector3d.Distance(node.Position, eventPosition) > radius
                && (canUseNode?.Invoke(candidate) ?? true))
            {
                return candidate;
            }

            foreach (GraphLink link in graph.LinksOf(candidate))
            {
                if (discovered >= SearchBudget)
                {
                    break;
                }

                int neighbour = link.Other(candidate);
                if (neighbour < 0 || !visited.Add(neighbour))
                {
                    continue;
                }

                queue.Enqueue(neighbour);
                discovered++;
            }
        }

        return null;
    }

}
