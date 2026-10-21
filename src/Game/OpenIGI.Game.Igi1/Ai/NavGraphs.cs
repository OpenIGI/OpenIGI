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
    /// <param name="placements">
    /// The level's placements, which place a graph on a train where its car stands; without them such a graph stays in
    /// its car's frame.
    /// </param>
    /// <returns>The graphs by their task's id, in world space.</returns>
    /// <exception cref="IOException">The directory or a graph could not be read.</exception>
    public static Dictionary<int, GraphFile> Read(
        string directory,
        LevelTask levelRoot,
        IOpenIgiLogger logger,
        IReadOnlyList<WorldPlacement>? placements = null)
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
                Place(graph, graphId, levelRoot, placements);
                graphs[graphId] = graph;
            }
            catch (GraphFormatException ex)
            {
                logger.Warning("Ai", $"Nav graph '{stem}' did not parse: {ex.Message}");
            }
        }

        return graphs;
    }

    /// <summary>
    /// Gets the task whose frame a graph's nodes are left in by <see cref="Read"/>: a model the level does not place where it
    /// stands, a train set on its railroad, which carries the graph as it moves. <see langword="null"/> for a graph placed in
    /// the world.
    /// </summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="graphId">The graph's task id.</param>
    /// <returns>The carrier, or <see langword="null"/>.</returns>
    /// <remarks>
    /// The original adds the carrier's frame as it stands every time a node is read (<c>0x4F9770</c>), so a graph on a train
    /// rides it; one under a model with a place of its own is placed once, as a building's.
    /// </remarks>
    public static GameTask? CarrierOf(LevelTask levelRoot, int graphId)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        return levelRoot.Tree.FromId(graphId)?.Parent is DeclaredTask parent
            && parent.TryGetValue(WorldScene.ModelParameter, out object? model)
            && model is string { Length: > 0 }
            && !TryGetPlace(parent, out _, out _)
                ? parent
                : null;
    }

    // A model's own place in the level: its position and its orientation angles.
    private static bool TryGetPlace(DeclaredTask model, out Vector3d position, out Vector3f angles)
    {
        if (model.TryGetValue("Position", out object? place) && place is Vector3d at
            && model.TryGetValue("Orientation", out object? turn) && turn is Vector3f by)
        {
            position = at;
            angles = by;
            return true;
        }

        position = default;
        angles = default;
        return false;
    }

    // The file stores node positions in the graph's own frame; the world position is node + graphTask.Position, what
    // 0x4F9770 adds every time.
    private static void Place(GraphFile graph, int graphId, LevelTask levelRoot, IReadOnlyList<WorldPlacement>? placements)
    {
        Vector3d graphPos = TaskGeometry.FindPosition(levelRoot, graphId);

        // A graph under a placed model is in that model's frame (0x4F9770's other branch). Mission 9's intro office is a
        // Building whose graphs sit at a few metres: added to nothing, they put Anya's feet 42 km under the level.
        // A graph on a model with no place of its own (CarrierOf) is in the carrier's frame.
        if (levelRoot.Tree.FromId(graphId)?.Parent is DeclaredTask graphParent
            && graphParent.TryGetValue(WorldScene.ModelParameter, out object? parentModel)
            && parentModel is string { Length: > 0 }
            && TryGetPlace(graphParent, out Vector3d parentPosition, out Vector3f parentAngles))
        {
            graph.ApplyWorldOffset(
                graphPos,
                parentPosition,
                RetailOrientation.Euler(parentAngles.X, parentAngles.Y, parentAngles.Z));
        }
        else if (levelRoot.Tree.FromId(graphId)?.Parent is DeclaredTask { Type.Name: "Train" } graphTrain
            && placements?.FirstOrDefault(placement => ReferenceEquals(placement.Owner, graphTrain)) is { } trainPlacement)
        {
            // A Train has no point position -- its "Position" is a scalar along its rail -- so the branch above never saw
            // one, and the wagon's graph landed at the world origin. The car stands where its consist puts it (0x42A7C0);
            // its graph is in that frame like any other, and a rider is held relative to it (GraphFile.ToParentFrame).
            Matrix3f m = trainPlacement.Orientation;
            graph.ApplyWorldOffset(
                graphPos,
                trainPlacement.Position * WorldScene.UnitsPerMetre,
                [m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33]);
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
