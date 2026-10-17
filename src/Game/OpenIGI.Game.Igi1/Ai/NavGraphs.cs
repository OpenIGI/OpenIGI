using System.Globalization;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>Reads a level's nav graphs, one per <c>AIGraph</c> task, placed in the world.</summary>
/// <remarks>
/// A level ships one graph per <c>AIGraph</c> task, named by that task's id (ai.md 1.1), and every HumanAI names the one it
/// walks through its own "Graph ID" parameter (ai.md 7.6). Level 1 ships eleven of them: giving every soldier the single
/// largest one routed each over node ids from a graph that was not its own, off the edge of the level.
/// </remarks>
public static class NavGraphs
{
    /// <summary>Reads every <c>graph&lt;id&gt;.dat</c> under a mission's graph directory.</summary>
    /// <param name="directory">The mission's <c>graphs</c> directory; a missing one has no graphs.</param>
    /// <param name="levelRoot">The level's task tree, which places each graph.</param>
    /// <param name="logger">Told about a graph that does not parse, which is then left out.</param>
    /// <returns>The graphs by their task's id, in world space.</returns>
    /// <exception cref="IOException">The directory or a graph could not be read.</exception>
    public static Dictionary<int, GraphFile> Read(string directory, LevelTask levelRoot, IOpenIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(logger);
        Dictionary<int, GraphFile> graphs = [];
        if (!Directory.Exists(directory))
        {
            return graphs;
        }

        // Retail installs commonly preserve uppercase pack names (GRAPHS/GRAPH9.DAT). The VFS accepts those names, but this
        // direct filesystem walk must do so explicitly on a case-sensitive volume as well.
        foreach (string file in Directory.EnumerateFiles(directory)
            .Where(file => Path.GetFileNameWithoutExtension(file).StartsWith("graph", StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetExtension(file), ".dat", StringComparison.OrdinalIgnoreCase)))
        {
            string stem = Path.GetFileNameWithoutExtension(file);
            if (!int.TryParse(stem.AsSpan("graph".Length), out int graphId))
            {
                continue;
            }

            try
            {
                GraphFile graph = GraphFile.Read(File.ReadAllBytes(file));
                Place(graph, graphId, levelRoot);
                graphs[graphId] = graph;
            }
            catch (GraphFormatException ex)
            {
                logger.Warning("Ai", $"Nav graph '{stem}' did not parse: {ex.Message}");
            }
        }

        return graphs;
    }

    // The file stores node positions in the graph's own frame; the world position is node + graphTask.Position, what
    // 0x4F9770 adds every time.
    private static void Place(GraphFile graph, int graphId, LevelTask levelRoot)
    {
        Vector3d graphPos = TaskGeometry.FindPosition(levelRoot, graphId);

        // A graph under a placed model is in that model's frame (0x4F9770's other branch). Mission 9's intro office is a
        // Building whose graphs sit at a few metres: added to nothing, they put Anya's feet 42 km under the level.
        if (levelRoot.Tree.FromId(graphId)?.Parent is DeclaredTask graphParent
            && graphParent.TryGetValue(WorldScene.ModelParameter, out object? parentModel)
            && parentModel is string { Length: > 0 }
            && graphParent.TryGetValue("Position", out object? parentPlace)
            && parentPlace is Vector3d parentPosition
            && graphParent.TryGetValue("Orientation", out object? parentTurn)
            && parentTurn is Vector3f parentAngles)
        {
            graph.ApplyWorldOffset(
                graphPos,
                parentPosition,
                RetailOrientation.Euler(parentAngles.X, parentAngles.Y, parentAngles.Z));
        }
        else
        {
            graph.ApplyWorldOffset(graphPos);
        }

        // The graph task's own "Node cover midoffset" -- where the can-shoot ray starts.
        if (TaskGeometry.TryFindValue(levelRoot, graphId, "Node cover midoffset", out object? midOffset)
            && midOffset is IConvertible convertible)
        {
            graph.CoverMidOffsetMetres = convertible.ToDouble(CultureInfo.InvariantCulture);
        }
    }
}
