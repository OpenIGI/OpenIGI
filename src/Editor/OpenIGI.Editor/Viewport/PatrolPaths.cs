using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Formats.Ai;

namespace OpenIGI.Editor.Viewport;

/// <summary>One stretch of a patrol as the soldier walks it, in world metres.</summary>
/// <param name="From">Where it starts.</param>
/// <param name="To">Where it ends.</param>
/// <param name="Run">Whether the soldier runs it.</param>
/// <param name="IsWayBack">Whether it is the way round again from the last waypoint to the first.</param>
internal readonly record struct PatrolLeg(Vector3d From, Vector3d To, bool Run, bool IsWayBack);

/// <summary>One waypoint of a patrol, where it is.</summary>
/// <param name="Index">Its place in the route, from nought.</param>
/// <param name="At">Where, in world metres.</param>
/// <param name="Stop">The waypoint.</param>
internal readonly record struct PatrolStop(int Index, Vector3d At, PatrolWaypoint Stop);

/// <summary>A soldier's patrol laid out in the world: the stretches walked along the AI graph's routes, and its waypoints.</summary>
/// <param name="Legs">The stretches, in the order walked.</param>
/// <param name="Stops">The waypoints that stand on a node of the graph.</param>
/// <param name="Looks">Each look at a node, from the waypoint to what is looked at.</param>
internal sealed record PatrolWalk(IReadOnlyList<PatrolLeg> Legs, IReadOnlyList<PatrolStop> Stops, IReadOnlyList<(Vector3d From, Vector3d To)> Looks);

/// <summary>
/// Where soldiers' patrols go: each read from its <c>PatrolPath</c>, its waypoints the nodes of the AI graph the soldier's
/// <c>HumanAI</c> names, the way between them the graph's own route table, as the game walks it.
/// </summary>
/// <param name="workspace">The open mission, its documents and graphs.</param>
internal sealed class PatrolPaths(MissionWorkspace workspace)
{
    private (GraphDocument Graph, GraphFile File)? _routes;

    /// <summary>Whether a task is a soldier with AI, which may have a patrol.</summary>
    /// <param name="task">The task.</param>
    /// <returns>Whether it is.</returns>
    public static bool IsSoldier(MissionTask? task) =>
        task is not null && task.TypeName.StartsWith("HumanSoldier", StringComparison.Ordinal) && task.Children.Any(static c => c.TypeName == "HumanAI");

    /// <summary>A soldier's mind.</summary>
    /// <param name="soldier">The soldier.</param>
    /// <returns>Its <c>HumanAI</c>, or null.</returns>
    public static MissionTask? Ai(MissionTask soldier) => soldier.Children.FirstOrDefault(static c => c.TypeName == "HumanAI");

    /// <summary>A soldier's patrol task.</summary>
    /// <param name="soldier">The soldier.</param>
    /// <returns>Its <c>PatrolPath</c>, or null.</returns>
    public static MissionTask? Path(MissionTask soldier) => soldier.Children.FirstOrDefault(static c => c.TypeName == PatrolRoute.PathType);

    /// <summary>The AI graph task a soldier's mind names.</summary>
    /// <param name="soldier">The soldier.</param>
    /// <returns>The <c>AIGraph</c> task, or null.</returns>
    public MissionTask? GraphTask(MissionTask soldier)
    {
        if (Ai(soldier)?.FindParameter("Graph ID") is not { } parameter || !parameter.TryGetNumbers(out double[] value) || workspace.Document is not { } document)
        {
            return null;
        }

        int id = (int)value[0];
        return document.EnumerateTasks().FirstOrDefault(t => MissionGraphs.IsGraph(t) && t.Id == id);
    }

    /// <summary>The graph a soldier walks.</summary>
    /// <param name="soldier">The soldier.</param>
    /// <returns>The graph, or null.</returns>
    public GraphDocument? Graph(MissionTask soldier) =>
        GraphTask(soldier) is { } task && workspace.Mission is { } mission ? workspace.Graphs.Get(mission, task) : null;

    /// <summary>A soldier's patrol laid out in the world, or null when it has none or no graph to walk it on.</summary>
    /// <param name="soldier">The soldier.</param>
    /// <returns>The walk.</returns>
    public PatrolWalk? Walk(MissionTask soldier)
    {
        ArgumentNullException.ThrowIfNull(soldier);
        return Path(soldier) is { } path && GraphTask(soldier) is { } graphTask ? Walk(TaskPresentation.PositionMetres(soldier), path, graphTask) : null;
    }

    /// <summary>
    /// A route laid out in the world: a <c>PatrolPath</c> walked on an AI graph from a start, as a soldier walks his patrol
    /// and a <c>CarAI</c> drives its car along its route.
    /// </summary>
    /// <param name="start">Where the walker stands at first, or null.</param>
    /// <param name="path">The <c>PatrolPath</c> task.</param>
    /// <param name="graphTask">The <c>AIGraph</c> task its nodes are on.</param>
    /// <returns>The walk, or null when the route has no waypoints or the graph cannot be read.</returns>
    public PatrolWalk? Walk(Vector3d? start, MissionTask path, MissionTask graphTask)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(graphTask);
        if (workspace.Mission is not { } mission || workspace.Graphs.Get(mission, graphTask) is not { } graph)
        {
            return null;
        }

        PatrolRoute route = PatrolRoute.Read(path);
        if (route.Waypoints.Count == 0)
        {
            return null;
        }

        Dictionary<int, Vector3d> nodes = graph.Nodes.Where(static n => n.PositionRecord >= 0).ToDictionary(static n => n.Id, n => MissionGraphs.ToWorldMetres(graphTask, n.Position));
        List<PatrolLeg> legs = [];
        List<PatrolStop> stops = [];
        List<(Vector3d, Vector3d)> looks = [];

        // From the start to the first waypoint, then each leg along the graph's routes.
        Vector3d? last = start;
        int? lastNode = null;
        for (int i = 0; i < route.Waypoints.Count; i++)
        {
            PatrolWaypoint stop = route.Waypoints[i];
            if (!nodes.TryGetValue(stop.Node, out Vector3d at))
            {
                continue;
            }

            foreach (Vector3d step in lastNode is { } from ? Leg(graph, nodes, from, stop.Node) : [at])
            {
                if (last is { } previous)
                {
                    legs.Add(new PatrolLeg(previous, step, stop.Run, false));
                }

                last = step;
            }

            lastNode = stop.Node;
            stops.Add(new PatrolStop(i, at, stop));
            foreach (PatrolAction look in stop.Actions.Where(static a => a.Kind == PatrolPause.LookAt))
            {
                if (nodes.TryGetValue((int)look.Value, out Vector3d target))
                {
                    looks.Add((at, target));
                }
            }
        }

        // Round again to the first waypoint, unless the route stops at its end.
        if (!route.Stops && route.Waypoints.Count > 1 && lastNode is { } end && nodes.ContainsKey(route.Waypoints[0].Node))
        {
            Vector3d? back = null;
            foreach (Vector3d step in Leg(graph, nodes, end, route.Waypoints[0].Node))
            {
                if (back is { } previous)
                {
                    legs.Add(new PatrolLeg(previous, step, false, true));
                }

                back = step;
            }
        }

        return new PatrolWalk(legs, stops, looks);
    }

    /// <summary>Drops the route table read from a graph, after the graph changed in place (a node added).</summary>
    public void Forget() => _routes = null;

    // The graph's route between two nodes, as world points from the first to the second; a straight hop without one.
    private IEnumerable<Vector3d> Leg(GraphDocument graph, Dictionary<int, Vector3d> nodes, int from, int to)
    {
        if (_routes is not { } cached || !ReferenceEquals(cached.Graph, graph))
        {
            _routes = (graph, GraphFile.Read(graph.Write()));
        }

        IReadOnlyList<int> steps = _routes.Value.File.RouteTable is { } table && table.MaxNodes > Math.Max(from, to) ? table.EnumerateRoute(from, to) : [];
        if (steps.Count == 0)
        {
            steps = [to];
        }

        yield return nodes[from];
        foreach (int step in steps)
        {
            if (nodes.TryGetValue(step, out Vector3d at))
            {
                yield return at;
            }
        }
    }
}
