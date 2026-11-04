using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.Ai;

/// <summary>Adding and taking away a graph's nodes: the file written again as the original lays one out, routes baked.</summary>
/// <remarks>
/// A node changes the file's shape (its records, and the route table's size when the slots run out), so rather than splice
/// records the graph is read for what it holds and written again by <see cref="GraphFileWriter"/>, which writes every
/// shipped graph back byte for byte: what the edit does not touch comes out as it was.
/// </remarks>
public static class GraphDocumentEdits
{
    /// <summary>How many slots a full graph grows by: the shipped graphs' slot counts are whole hundreds.</summary>
    public const int SlotStep = 100;

    /// <summary>A copy of a graph with one more node, linked to the nodes given.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="position">The node's position, in the graph's frame and world units.</param>
    /// <param name="linkTo">The nodes to link it to.</param>
    /// <param name="id">Receives the new node's id: one past the highest.</param>
    /// <returns>The new graph.</returns>
    public static GraphDocument WithNode(this GraphDocument graph, Vector3d position, IReadOnlyList<int> linkTo, out int id)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(linkTo);
        GraphFile file = GraphFile.Read(graph.Write());
        id = file.Nodes.Count == 0 ? 1 : file.Nodes.Max(static n => n.Id) + 1;
        int slots = Math.Max(file.MaxNodes, ((id / SlotStep) + 1) * SlotStep);
        List<GraphWriteNode> nodes = [.. file.Nodes.Select(static n => new GraphWriteNode(n.Id, n.Position, n.Gamma, n.Radius, n.MaterialId, n.CriteriaName))];
        nodes.Add(new GraphWriteNode(id, position, Material: -1));
        int added = id;
        List<GraphLink> links = [.. file.Links, .. linkTo.Where(n => n != added && file.Nodes.Any(f => f.Id == n)).Distinct().Select(n => new GraphLink(n, added, GraphFileWriter.WalkLink))];
        return GraphDocument.Read(GraphFileWriter.Write(slots, nodes, links));
    }

    /// <summary>A copy of a graph without a node and its links.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="id">The node.</param>
    /// <returns>The new graph; the same one when it has no such node.</returns>
    public static GraphDocument WithoutNode(this GraphDocument graph, int id)
    {
        ArgumentNullException.ThrowIfNull(graph);
        GraphFile file = GraphFile.Read(graph.Write());
        if (file.Nodes.All(n => n.Id != id))
        {
            return graph;
        }

        GraphWriteNode[] nodes = [.. file.Nodes.Where(n => n.Id != id).Select(static n => new GraphWriteNode(n.Id, n.Position, n.Gamma, n.Radius, n.MaterialId, n.CriteriaName))];
        GraphLink[] links = [.. file.Links.Where(l => l.NodeA != id && l.NodeB != id)];
        return GraphDocument.Read(GraphFileWriter.Write(file.MaxNodes, nodes, links));
    }
}
