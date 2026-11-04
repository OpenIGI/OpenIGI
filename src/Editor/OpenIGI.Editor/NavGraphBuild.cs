using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.World;

namespace OpenIGI.Editor;

/// <summary>A navigation graph built for a mission: the <c>AIGraph</c> task to add and the graph file it names.</summary>
/// <param name="Task">The new AIGraph task, unattached, its id the file's number.</param>
/// <param name="File">The <c>graphs/graph&lt;id&gt;.dat</c> it reads.</param>
/// <param name="Nodes">How many nodes the graph has.</param>
/// <param name="Links">How many links.</param>
internal sealed record NavGraphBuilt(MissionTask Task, byte[] File, int Nodes, int Links);

/// <summary>
/// Builds a navigation graph over a mission's ground for Project I.G.I., as the original's editor left one: an
/// <c>AIGraph</c> task at the graph's origin and a named graph file of nodes on the ground, linked where a soldier can walk
/// between them, with its routes baked.
/// </summary>
/// <remarks>
/// <para>
/// A soldier's <c>HumanAI</c> names the graph it walks by the AIGraph task's id, and the level reads that graph from
/// <c>MISSION:graphs/graph&lt;id&gt;.dat</c>, its node positions relative to the task's "Graph position".
/// </para>
/// <para>
/// The task's other values are the shipped levels' own: "Graphdata" is the file's node count, slot count and link count,
/// as level 1's graph 13 (4 nodes, 100 slots, 4 links) has it, and the link settings are the ones every shipped AIGraph
/// keeps. Nodes carry no material (-1): the reader at <c>0x4FAFE0</c> leaves a node's material empty for -1, as 215 of the
/// shipped nodes have it.
/// </para>
/// </remarks>
internal static class NavGraphBuild
{
    /// <summary>The slot counts are whole hundreds, as the shipped graphs' are (100 and 300).</summary>
    private const int SlotStep = 100;

    /// <summary>Builds a graph over a square of ground.</summary>
    /// <param name="document">The mission, which must declare AIGraph.</param>
    /// <param name="terrain">The ground.</param>
    /// <param name="request">The square and its limits.</param>
    /// <returns>The task and its file, or null when the mission does not declare AIGraph, has no free id or no ground there.</returns>
    public static NavGraphBuilt? Build(MissionDocument document, IEditableTerrain terrain, NavGraphRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(terrain);
        if (!document.Declarations.ContainsKey("AIGraph"))
        {
            return null;
        }

        NavGraphLayout layout = NavGraphLayout.Build(request, (x, y) => terrain.TryGetHeight(x, y, out double z) ? z : null);
        if (layout.Nodes.Count < 2)
        {
            return null;
        }

        HashSet<int> used = [.. document.EnumerateTasks().Select(static t => t.Id)];
        int id = Enumerable.Range(1, MissionDocument.MaximumTaskId - 1).FirstOrDefault(i => !used.Contains(i), -1);
        if (id < 0)
        {
            return null;
        }

        // The origin on the ground under the first node; every node is stored from it, in world units.
        Vector3d origin = layout.Nodes[0].PositionMetres * WorldUnits.PerMetre;
        int slots = ((layout.Nodes.Count / SlotStep) + 1) * SlotStep;
        GraphWriteNode[] nodes = [.. layout.Nodes.Select(n => new GraphWriteNode(n.Id, (n.PositionMetres * WorldUnits.PerMetre) - origin, Material: -1))];
        GraphLink[] links = [.. layout.Links.Select(static l => new GraphLink(l.A, l.B, GraphFileWriter.WalkLink))];
        byte[] file = GraphFileWriter.Write(slots, nodes, links);

        MissionTask task = new("AIGraph", "Navigation", id);
        document.ApplyDeclaration(task);
        Set(task, "Graph position", origin.X, origin.Y, origin.Z);
        Set(task, "Graphdata", nodes.Length, slots, links.Length);
        Set(task, "Node cover midoffset", 1);
        Set(task, "Node cover topoffset", 2);
        Set(task, "Max height difference between linked nodes", 3);
        Set(task, "Width of node links", 0.3);
        Set(task, "Link maximum distance to ground", request.MaximumGroundGapMetres);
        Set(task, "Precise link method step value", 0.05);
        return new NavGraphBuilt(task, file, nodes.Length, links.Length);
    }

    private static void Set(MissionTask task, string parameter, params double[] values)
    {
        if (task.FindParameter(parameter) is { } found && found.Arguments.Count == values.Length)
        {
            found.SetNumbers(values);
        }
    }
}
