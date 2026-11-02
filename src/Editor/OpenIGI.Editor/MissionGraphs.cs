using OpenIGI.Core.Diagnostics;
using System.Globalization;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.World;
using OpenIGI.Host.Editor;

namespace OpenIGI.Editor;

/// <summary>
/// The open mission's AI graphs, read when first shown and kept until the mission changes: each AIGraph task's
/// <c>graphs/graph&lt;id&gt;.dat</c> as an editable document, with where its nodes stand in the world. Moved nodes are
/// saved into the project with the mission, the graph's route table rebuilt to follow them.
/// </summary>
internal sealed class MissionGraphs
{
    private const string RelativeParameter = "Relative";

    private readonly IEditorHost _host;
    private readonly Dictionary<int, Entry?> _graphs = [];

    public MissionGraphs(IEditorHost host)
    {
        _host = host;
    }

    /// <summary>Gets whether any graph has an edit not yet saved.</summary>
    public bool IsDirty => _graphs.Values.Any(static g => g?.Dirty == true);

    /// <summary>Gets whether a task is an AI graph.</summary>
    /// <param name="task">The task.</param>
    /// <returns><see langword="true"/> for an AIGraph.</returns>
    public static bool IsGraph(MissionTask task) => task.TypeName.Equals("AIGraph", StringComparison.OrdinalIgnoreCase) && task.Id >= 0;

    /// <summary>Forgets every graph, when the mission is closed or reloaded.</summary>
    public void Clear() => _graphs.Clear();

    /// <summary>Gets a graph task's document, reading it on first use.</summary>
    /// <param name="mission">The mission.</param>
    /// <param name="task">The AIGraph task.</param>
    /// <returns>The document, or <see langword="null"/> when the file is missing or not editable.</returns>
    public GraphDocument? Get(MissionDescriptor mission, MissionTask task)
    {
        if (!IsGraph(task))
        {
            return null;
        }

        if (!_graphs.TryGetValue(task.Id, out Entry? entry))
        {
            entry = Load(mission, task.Id);
            _graphs[task.Id] = entry;
        }

        return entry?.Document;
    }

    /// <summary>Gets where a graph's frame puts a node, in world metres.</summary>
    /// <param name="task">The AIGraph task.</param>
    /// <param name="local">The node's position as stored, in the graph's frame and world units.</param>
    /// <returns>The world position in metres.</returns>
    /// <remarks>
    /// The node plus the graph's position; under a placed model, that sum turned by the model's orientation and
    /// moved to its position, as the game places it.
    /// </remarks>
    public static Vector3d ToWorldMetres(MissionTask task, Vector3d local)
    {
        (Vector3d offset, (Vector3d Position, Matrix3f Orientation)? frame) = Frame(task);
        Vector3d sum = offset + local;
        if (frame is { } parent)
        {
            sum = parent.Position + parent.Orientation.Transform(sum);
        }

        return sum / WorldUnits.PerMetre;
    }

    /// <summary>The inverse of <see cref="ToWorldMetres"/>.</summary>
    /// <param name="task">The AIGraph task.</param>
    /// <param name="worldMetres">A world position in metres.</param>
    /// <returns>The position in the graph's frame, in world units.</returns>
    public static Vector3d ToLocalUnits(MissionTask task, Vector3d worldMetres)
    {
        (Vector3d offset, (Vector3d Position, Matrix3f Orientation)? frame) = Frame(task);
        Vector3d sum = worldMetres * WorldUnits.PerMetre;
        if (frame is { } parent)
        {
            sum = parent.Orientation.Transposed().Transform(sum - parent.Position);
        }

        return sum - offset;
    }

    /// <summary>Gets where a graph's own origin stands, in world metres: where its gizmo goes.</summary>
    /// <param name="task">The AIGraph task.</param>
    /// <returns>The position.</returns>
    public static Vector3d OriginMetres(MissionTask task) => ToWorldMetres(task, Vector3d.Zero);

    /// <summary>Gets the "Graph position" that puts a graph's origin at a world point, through its parent's frame.</summary>
    /// <param name="task">The AIGraph task.</param>
    /// <param name="worldMetres">Where the origin should stand, in world metres.</param>
    /// <returns>The parameter's value, in world units.</returns>
    public static Vector3d OffsetForOrigin(MissionTask task, Vector3d worldMetres) =>
        ToLocalUnits(task, worldMetres) + Frame(task).Offset;

    /// <summary>Gets the middle of a graph's nodes and the radius around it that holds them, in world metres.</summary>
    /// <param name="task">The AIGraph task.</param>
    /// <param name="graph">Its document.</param>
    /// <returns>The centre and radius, or <see langword="null"/> for a graph with no placed node.</returns>
    public static (Vector3d Centre, double Radius)? BoundsMetres(MissionTask task, GraphDocument graph)
    {
        Vector3d min = new(double.MaxValue, double.MaxValue, double.MaxValue);
        Vector3d max = new(double.MinValue, double.MinValue, double.MinValue);
        bool any = false;
        foreach (GraphDocumentNode node in graph.Nodes)
        {
            if (node.PositionRecord < 0)
            {
                continue;
            }

            Vector3d p = ToWorldMetres(task, node.Position);
            min = new Vector3d(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
            max = new Vector3d(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
            any = true;
        }

        return any ? ((min + max) * 0.5, Math.Max(1.0, (max - min).Length * 0.5)) : null;
    }

    /// <summary>A command that moves a graph node, merging with the next move of the same node.</summary>
    /// <param name="task">The AIGraph task.</param>
    /// <param name="nodeId">The node id.</param>
    /// <param name="local">The new position, in the graph's frame and world units.</param>
    /// <returns>The command, or <see langword="null"/> when the graph is not loaded or has no such node.</returns>
    public IEditorCommand? MoveNode(MissionTask task, int nodeId, Vector3d local)
    {
        if (!_graphs.TryGetValue(task.Id, out Entry? entry) || entry is null
            || entry.Document.Nodes.FirstOrDefault(n => n.Id == nodeId) is not { PositionRecord: >= 0 } node)
        {
            return null;
        }

        return new MoveNodeCommand(entry, nodeId, node.Position, local);
    }

    /// <summary>Writes every edited graph into the project.</summary>
    /// <param name="workspace">The workspace whose project receives them.</param>
    /// <returns>How many graphs were written.</returns>
    /// <exception cref="IOException">The project cannot be written.</exception>
    public int Save(MissionWorkspace workspace)
    {
        int written = 0;
        foreach (Entry? entry in _graphs.Values)
        {
            if (entry is { Dirty: true })
            {
                // Moved nodes change which way is shortest; an I.G.I. graph's table is baked again to follow them.
                entry.Document.RebuildRoutes();
                workspace.WriteToProject(entry.GamePath, entry.Document.Write());
                entry.Dirty = false;
                written++;
            }
        }

        return written;
    }

    /// <summary>Gets the game path of an AI graph task's file, relative to the installation.</summary>
    /// <param name="mission">The mission.</param>
    /// <param name="graphId">The AIGraph task's id.</param>
    /// <returns>The path.</returns>
    public static string GamePath(MissionDescriptor mission, int graphId) =>
        string.Create(CultureInfo.InvariantCulture, $"{mission.MissionDirectory.TrimEnd('/', '\\')}/graphs/graph{graphId}.dat");

    // A node stands at parent(offset + stored), in world units, where the two games differ in what offset and parent are.
    // I.G.I.: the offset is the graph's position, and a graph under a placed model is in the model's frame
    // (GraphFile.ApplyWorldOffset's second form). I.G.I. 2 (its graphs carry "Relative"): a relative graph rides its
    // parent and ignores its own position; any other graph is stored plus its position, in the world even under a parent
    // (Igi2AiGraphPlacement.Place puts the parent's frame on and takes it off again).
    private static (Vector3d Offset, (Vector3d Position, Matrix3f Orientation)? Parent) Frame(MissionTask task)
    {
        Vector3d offset = TaskPresentation.PositionMetres(task) is { } metres ? metres * WorldUnits.PerMetre : Vector3d.Zero;
        (Vector3d, Matrix3f)? parent = task.Parent is { } owner
            && owner.FindParameter(WorldScene.ModelParameter) is not null
            && TaskPresentation.PositionMetres(owner) is { } at
            && TaskPresentation.Orientation(owner) is not null
                ? (at * WorldUnits.PerMetre, TaskPresentation.OrientationMatrix(owner))
                : null;
        if (task.FindParameter(RelativeParameter) is not { } relative)
        {
            return (offset, parent);
        }

        return relative.Arguments is [var flag] && ScriptValues.TryGetBool(flag, out bool isRelative) && isRelative
            ? (Vector3d.Zero, parent)
            : (offset, null);
    }

    private Entry? Load(MissionDescriptor mission, int graphId)
    {
        string path = GamePath(mission, graphId);
        try
        {
            if (!_host.TryReadFile("LOCAL:" + path, out ReadOnlyMemory<byte> bytes))
            {
                return null;
            }

            if (!GraphDocument.IsNamed(bytes.Span))
            {
                _host.Logger.Information("Editor", $"{path} is a positional graph; it is shown in the game but cannot be edited.");
                return null;
            }

            return new Entry(path, GraphDocument.Read(bytes));
        }
        catch (Exception error) when (error is IOException or GraphFormatException)
        {
            _host.Logger.Warning("Editor", $"Could not read {path}: {error.Message}");
            return null;
        }
    }

    private sealed class Entry(string gamePath, GraphDocument document)
    {
        public string GamePath { get; } = gamePath;

        public GraphDocument Document { get; } = document;

        public bool Dirty { get; set; }
    }

    private sealed class MoveNodeCommand(Entry entry, int nodeId, Vector3d from, Vector3d to) : IEditorCommand
    {
        private Vector3d _to = to;

        public string Name => "Move Graph Node";

        public void Apply()
        {
            entry.Document.SetNodePosition(nodeId, _to);
            entry.Dirty = true;
        }

        public void Undo()
        {
            entry.Document.SetNodePosition(nodeId, from);
            entry.Dirty = true;
        }

        public bool TryMerge(IEditorCommand following)
        {
            if (!CanMerge(following))
            {
                return false;
            }

            _to = ((MoveNodeCommand)following).To;
            return true;
        }

        public bool CanMerge(IEditorCommand following) =>
            following is MoveNodeCommand next && ReferenceEquals(next.Entry, entry) && next.NodeId == nodeId;

        private Entry Entry => entry;

        private int NodeId => nodeId;

        private Vector3d To => _to;
    }
}
