using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Scripting.Runtime;
using System.Globalization;

namespace OpenIGI.Game.Igi2.Ai;

/// <summary>A node of an AI graph, I.G.I. 2's <c>AIGraphNode_s</c>.</summary>
public sealed partial class Igi2AiGraphNode
{
    /// <summary>The most links a node keeps, its <c>aptGraphLinks[64]</c>.</summary>
    public const int MaxLinks = 64;

    private readonly List<Igi2AiGraphLink> _links = [];

    private readonly IIgi2AttachParent? _parent;

    internal Igi2AiGraphNode(
        int id,
        Vector3d position,
        float gamma,
        float radius,
        int material,
        float light,
        int terrain,
        string criteriaText,
        int criteria,
        IIgi2AttachParent? parent = null)
    {
        Id = id;
        LocalPosition = position;
        _parent = parent;
        Gamma = gamma;
        Radius = radius;
        Diameter = ((Fpu)radius + radius).F;
        Material = material;
        LightValue = light;
        Terrain = terrain;
        CriteriaText = criteriaText;
        Criteria = criteria;
    }

    /// <summary>Gets its id, its slot in the graph.</summary>
    public int Id { get; }

    /// <summary>Gets where it is as the graph holds it: in its parent's frame when the graph hangs from one, else in the world.</summary>
    public Vector3d LocalPosition { get; }

    /// <summary>
    /// Gets where it is in the world now, <c>Graph_GetNodePos</c> (<c>igi2 0x59A2A0</c>): under a parent its held position turned by
    /// the parent's frame as it stands (each axis summing z, then y, then x against the frame's rows) and the parent's position
    /// added, so the node rides with the parent; otherwise as held.
    /// </summary>
    public Vector3d Position
    {
        get
        {
            if (_parent is not { } parent)
            {
                return LocalPosition;
            }

            Vector3d p = LocalPosition;
            IReadOnlyList<float> m = parent.AttachOrientation;
            Vector3d at = parent.AttachPosition;
            double x = (((Fpu)m[2] * p.Z) + ((Fpu)m[1] * p.Y) + ((Fpu)m[0] * p.X)).Value;
            double y = (((Fpu)m[5] * p.Z) + ((Fpu)m[4] * p.Y) + ((Fpu)m[3] * p.X)).Value;
            double z = (((Fpu)m[8] * p.Z) + ((Fpu)m[7] * p.Y) + ((Fpu)m[6] * p.X)).Value;
            return new Vector3d(((Fpu)at.X + x).Value, ((Fpu)at.Y + y).Value, ((Fpu)at.Z + z).Value);
        }
    }

    /// <summary>Gets the heading a soldier standing on it faces.</summary>
    public float Gamma { get; }

    /// <summary>Gets its radius, in world units.</summary>
    public float Radius { get; }

    /// <summary>Gets twice its radius, kept beside it.</summary>
    public float Diameter { get; }

    /// <summary>Gets the ground material under it, an index into the world's, or -1 for none.</summary>
    public int Material { get; }

    /// <summary>Gets how lit it is, nought or more.</summary>
    public float LightValue { get; }

    /// <summary>Gets a value other than nought when it lies on the terrain rather than in a building.</summary>
    public int Terrain { get; }

    /// <summary>Gets its criteria as the editor wrote them, a script expression.</summary>
    public string CriteriaText { get; }

    /// <summary>Gets its criteria, the <c>NODECRITERIA_*</c> bits the text evaluates to.</summary>
    public int Criteria { get; }

    /// <summary>Gets its links, at most <see cref="MaxLinks"/>, in the order they were made.</summary>
    public IReadOnlyList<Igi2AiGraphLink> Links => _links;

    internal void Attach(Igi2AiGraphLink link)
    {
        if (_links.Count < MaxLinks)
        {
            _links.Add(link);
        }
    }
}

/// <summary>A link between two nodes of an AI graph, I.G.I. 2's <c>GraphLink_s</c>.</summary>
/// <param name="NodeA">The first node's id.</param>
/// <param name="NodeB">The second node's id.</param>
/// <param name="Type">Its type, as the editor wrote it.</param>
/// <param name="Length">The distance between the nodes, worked out when the link is made.</param>
public sealed record Igi2AiGraphLink(int NodeA, int NodeB, int Type, float Length)
{
    /// <summary>The node at the other end from one.</summary>
    /// <param name="id">One end's id.</param>
    /// <returns>The other end's id.</returns>
    public int Other(int id) => id == NodeA ? NodeB : NodeA;
}

/// <summary>
/// An AI graph's nodes and links as <c>Graph_ParseGraphStream</c> (<c>igi2 0x59B4B0</c>) reads them from a level's
/// <c>graphs/graph&lt;id&gt;.dat</c>.
/// </summary>
/// <remarks>
/// <para>
/// The file is the named stream I.G.I. 1 also writes (<see cref="GraphFile.Magic"/>, each value a field hash, a type and the
/// value), read by a callback per field into one staged node. I.G.I. 2 adds two node fields, <c>nNodeOnTerrain</c> and
/// <c>vNodeLightValue</c>, has no route table (its soldiers search the graph), and keeps up to 2000 nodes where I.G.I. 1 kept
/// 1000.
/// </para>
/// <para>
/// <c>nNodeID</c> clears the staged node (<c>Graph_NodeIDCallback</c>, <c>0x59AA20</c>); <c>nNodeCriteria</c> makes it
/// (<c>Graph_NodeCriteriaCallback</c>, <c>0x59AC50</c>, into <c>Graph_CreateNode</c>, <c>0x59A040</c>), its criteria evaluated
/// as a one-line script. <c>eLinkType</c> makes a link from the last two ids read (<c>Graph_CreateLink</c>, <c>0x59A610</c>).
/// </para>
/// </remarks>
public sealed partial class Igi2AiGraph
{
    /// <summary>The most nodes a graph holds; a node with a higher id is dropped (<c>Graph_CreateNode</c>, <c>igi2 0x59A040</c>).</summary>
    public const int IdLimit = 2000;

    private static readonly Dictionary<uint, string> Fields = new string[]
    {
        "nMaxNodes", "nNodeID", "tNodePosition", "vNodeGamma", "vNodeRadius", "nNodeMaterial", "nNodeOnTerrain",
        "vNodeLightValue", "nNodeCriteria", "nLinkID1", "nLinkID2", "eLinkType",
    }.ToDictionary(GraphFieldHash.Of);

    private readonly Dictionary<int, Igi2AiGraphNode> _nodes = [];
    private readonly List<Igi2AiGraphLink> _links = [];

    private Igi2AiGraph(int maxNodes) => MaxNodes = maxNodes;

    /// <summary>Gets the object the graph hangs from, whose frame its nodes ride in, or <see langword="null"/>.</summary>
    public IIgi2AttachParent? Parent { get; private init; }

    /// <summary>Gets how many node slots the graph has, <c>nMaxNodes</c>.</summary>
    public int MaxNodes { get; private set; }

    /// <summary>Gets its nodes, by id.</summary>
    public IReadOnlyDictionary<int, Igi2AiGraphNode> Nodes => _nodes;

    /// <summary>Gets its links, in the order they were made.</summary>
    public IReadOnlyList<Igi2AiGraphLink> Links => _links;

    /// <summary>Gets the field hashes the stream carried that the original has no callback for, which it skips.</summary>
    public IReadOnlyList<uint> SkippedFields { get; private set; } = [];

    /// <summary>Finds a node by id.</summary>
    /// <param name="id">The id.</param>
    /// <returns>The node, or <see langword="null"/>.</returns>
    public Igi2AiGraphNode? Node(int id) => _nodes.GetValueOrDefault(id);

    /// <summary>Reads a graph file.</summary>
    /// <param name="data">The file.</param>
    /// <param name="placement">Where the graph is, which places its nodes.</param>
    /// <param name="criteria">Evaluates a node's criteria text.</param>
    /// <returns>The graph.</returns>
    /// <exception cref="GraphFormatException">The file is not the named stream, or is malformed.</exception>
    public static Igi2AiGraph Read(ReadOnlyMemory<byte> data, Igi2AiGraphPlacement placement, Func<string, int> criteria)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(criteria);
        GraphStreamReader reader = new(data);
        if (data.Length < 4 || reader.ReadRawUInt32() != GraphFile.Magic)
        {
            // Graph_LoadOldGraph (0x59AD90) reads the older positional form, which no I.G.I. 2 level ships.
            throw new GraphFormatException("An I.G.I. 2 graph is the named stream.");
        }

        Igi2AiGraph graph = new(placement.MaxNodes) { Parent = placement.Parent };
        List<uint> skipped = [];
        int id = 0;
        Vector3d position = default;
        float gamma = 0f;
        float radius = 0f;
        int material = -1;
        float light = 0f;
        int terrain = 0;
        int linkA = -1;
        int linkB = -1;
        while (!reader.IsAtEnd)
        {
            uint hash = reader.ReadUInt32();
            if (!Fields.TryGetValue(hash, out string? field))
            {
                skipped.Add(hash);
                reader.SkipValue();
                reader.SkipValue();
                continue;
            }

            reader.ReadByte();
            switch (field)
            {
                case "nMaxNodes":
                    // Graph_MaxNodesCallback (0x59A920) frees the node list and makes a new, empty one of that size.
                    graph.MaxNodes = reader.ReadInt32();
                    graph._nodes.Clear();
                    break;
                case "nNodeID":
                    id = reader.ReadInt32();
                    position = default;
                    gamma = 0f;
                    radius = 0f;
                    material = -1;
                    light = 0f;
                    terrain = 0;
                    break;
                case "tNodePosition":
                    position = placement.Place(reader.ReadVector3d());
                    break;
                case "vNodeGamma":
                    gamma = reader.ReadSingle();
                    break;
                case "vNodeRadius":
                    radius = reader.ReadSingle();
                    break;
                case "nNodeMaterial":
                    material = reader.ReadInt32();
                    break;
                case "nNodeOnTerrain":
                    // Graph_NodeTerrainCallback (0x59AC20): -1 reads as nought.
                    int onTerrain = reader.ReadInt32();
                    terrain = onTerrain == -1 ? 0 : onTerrain;
                    break;
                case "vNodeLightValue":
                    // Graph_NodeLightValueCallback (0x59ABE0): nought or less reads as nought.
                    float lit = reader.ReadSingle();
                    light = lit <= 0f ? 0f : lit;
                    break;
                case "nNodeCriteria":
                    string text = reader.ReadString();
                    graph.Add(id, position, gamma, radius, material, light, terrain, text, criteria);
                    break;
                case "nLinkID1":
                    linkA = reader.ReadInt32();
                    linkB = -1;
                    break;
                case "nLinkID2":
                    linkB = reader.ReadInt32();
                    break;
                default:
                    graph.Link(linkA, linkB, reader.ReadInt32());
                    break;
            }
        }

        graph.SkippedFields = skipped;
        return graph;
    }

    private void Add(int id, Vector3d position, float gamma, float radius, int material, float light, int terrain, string text, Func<string, int> criteria)
    {
        // A node past the limit is dropped; a negative id would be written outside the list, and is dropped here too.
        if ((uint)id >= IdLimit)
        {
            return;
        }

        if (id >= MaxNodes)
        {
            MaxNodes = id + 1;
        }

        if (_nodes.ContainsKey(id))
        {
            throw new GraphFormatException(string.Create(CultureInfo.InvariantCulture, $"Graph node {id} is made twice (GRAPHNODE ID ALREADY USED)."));
        }

        _nodes[id] = new Igi2AiGraphNode(id, position, gamma, radius, material, light, terrain, text, criteria(text), Parent);
    }

    private void Link(int a, int b, int type)
    {
        if (Node(a) is not { } first || Node(b) is not { } second)
        {
            throw new GraphFormatException(string.Create(CultureInfo.InvariantCulture, $"Graph link {a}-{b} names a node the graph lacks."));
        }

        // The length is the two nodes' distance in the world as they stand when the link is made: x and y stored as floats, z kept
        // on the stack, summed in that order.
        float dx = ((Fpu)first.Position.X - second.Position.X).F;
        float dy = ((Fpu)first.Position.Y - second.Position.Y).F;
        Fpu dz = (Fpu)first.Position.Z - second.Position.Z;
        float length = Fpu.Sqrt(((Fpu)dx * dx) + ((Fpu)dy * dy) + (dz * dz)).F;
        Igi2AiGraphLink link = new(a, b, type, length);
        _links.Add(link);
        first.Attach(link);
        second.Attach(link);
    }
}

/// <summary>Where a graph's nodes are read into: its own position, and the frame of the object it hangs from.</summary>
/// <param name="MaxNodes">The node slots it starts with, its <c>Graphdata</c>'s second value.</param>
/// <param name="IsRelative">Whether node positions are already in the graph's frame.</param>
/// <param name="Position">The graph's position, added to a node's when it is not relative.</param>
/// <param name="Parent">
/// The object it hangs from when that object answers the attach event (<c>Graph_CreateHandler</c>'s <c>Attach_eAttachEvent</c>; a
/// model object does, a container does not), or <see langword="null"/>.
/// </param>
public sealed record Igi2AiGraphPlacement(int MaxNodes, bool IsRelative, Vector3d Position, IIgi2AttachParent? Parent = null)
{
    /// <summary>Places a node read from the file, <c>Graph_NodePositionCallback</c> (<c>igi2 0x59AA50</c>).</summary>
    /// <param name="read">The position as read.</param>
    /// <returns>The node's position in the graph.</returns>
    /// <remarks>
    /// A relative graph keeps it. Otherwise the graph's position is added, and under a parent the parent's position is taken
    /// off and the parent's frame undone: each axis sums z, then y, then x against the frame's columns.
    /// </remarks>
    public Vector3d Place(Vector3d read)
    {
        if (IsRelative)
        {
            return read;
        }

        double x = ((Fpu)read.X + Position.X).Value;
        double y = ((Fpu)read.Y + Position.Y).Value;
        double z = ((Fpu)read.Z + Position.Z).Value;
        if (Parent is not { } owner)
        {
            return new Vector3d(x, y, z);
        }

        Vector3d parent = owner.AttachPosition;
        IReadOnlyList<float> m = owner.AttachOrientation;
        x = ((Fpu)x - parent.X).Value;
        y = ((Fpu)y - parent.Y).Value;
        z = ((Fpu)z - parent.Z).Value;
        return new Vector3d(
            (((Fpu)m[6] * z) + ((Fpu)m[3] * y) + ((Fpu)m[0] * x)).Value,
            (((Fpu)m[7] * z) + ((Fpu)m[4] * y) + ((Fpu)m[1] * x)).Value,
            (((Fpu)m[8] * z) + ((Fpu)m[5] * y) + ((Fpu)m[2] * x)).Value);
    }
}

/// <summary>An AI graph as level data places it, I.G.I. 2's <c>AIGraph</c>: the nodes and links soldiers walk.</summary>
public sealed partial class Igi2AiGraphTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "AIGraph";

    private double _midCoverOffset;
    private double _topCoverOffset;

    /// <summary>Gets or sets the graph's position.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Graph position")]
    public Vector3d GraphPosition { get; set; }

    /// <summary>Gets or sets a value indicating whether the file's positions are already in the graph's frame.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Relative")]
    public bool IsRelative { get; set; }

    /// <summary>Gets or sets the editor's update button.</summary>
    [ScriptParam(ScriptParamType.PushButton, "Update")]
    public bool IsUpdate { get; set; }

    /// <summary>Gets or sets the graph's sizes; the second is the node slots it starts with.</summary>
    [ScriptParam(ScriptParamType.Graph, "Graphdata")]
    public float[] GraphData { get; set; } = [0f, 0f, 0f];

    /// <summary>Gets or sets the height of a node's middle cover test, in metres, <c>vMidCoverOffset</c>.</summary>
    /// <remarks>A value over a hundred is world units, and reads as metres (<c>AIGraph_UpdateInternalDataHandler</c>, <c>igi2 0x59D0F0</c>).</remarks>
    [ScriptParam(ScriptParamType.Real64, "Node cover midoffset")]
    public double MidCoverOffset { get => _midCoverOffset; set => _midCoverOffset = value > 100.0 ? value * 0.000244140625 : value; }

    /// <summary>Gets or sets the height of a node's top cover test, in metres, <c>vTopCoverOffset</c>.</summary>
    [ScriptParam(ScriptParamType.Real64, "Node cover topoffset")]
    public double TopCoverOffset { get => _topCoverOffset; set => _topCoverOffset = value > 100.0 ? value * 0.000244140625 : value; }

    /// <summary>Gets or sets the editor's limit on the climb between linked nodes.</summary>
    [ScriptParam(ScriptParamType.Real64, "Max height difference between linked nodes")]
    public double MaxHeightDifference { get; set; }

    /// <summary>Gets or sets the editor's link width.</summary>
    [ScriptParam(ScriptParamType.Real64, "Width of node links")]
    public double LinkWidth { get; set; }

    /// <summary>Gets or sets the editor's limit on a link's height over the ground.</summary>
    [ScriptParam(ScriptParamType.Real64, "Link maximum distance to ground")]
    public double LinkMaxGroundDistance { get; set; }

    /// <summary>Gets or sets the editor's limit on a link's length.</summary>
    [ScriptParam(ScriptParamType.Real64, "Max Link-length")]
    public double MaxLinkLength { get; set; }

    /// <summary>Gets or sets whether the editor links precisely.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Use precise link method (SLOW!)")]
    public bool IsPreciseLink { get; set; }

    /// <summary>Gets or sets the editor's precise link step.</summary>
    [ScriptParam(ScriptParamType.Real64, "Precise link method step value")]
    public double PreciseLinkStep { get; set; }

    /// <summary>Gets or sets whether the editor recomputes and shows the cover.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Update & Display CoverInfo")]
    public bool IsUpdateCover { get; set; }

    /// <summary>Gets where its nodes are read into.</summary>
    public Igi2AiGraphPlacement Placement => new(GraphData.Length > 1 ? (int)GraphData[1] : 0, IsRelative, GraphPosition, Parent as IIgi2AttachParent);

    /// <summary>The file a graph is read from, <c>Graph_ParseGraphHandler</c> (<c>igi2 0x599110</c>), by its task id.</summary>
    /// <param name="taskId">The graph's level id.</param>
    /// <returns>The resource name.</returns>
    public static string ResourceNameFor(int taskId) =>
        string.Create(CultureInfo.InvariantCulture, $"MISSION:graphs/graph{taskId}.dat");
}

/// <summary>A level's AI graphs, read when the level is.</summary>
public sealed class Igi2AiGraphs
{
    private const string LogCategory = "AI";

    private readonly Dictionary<int, Igi2AiGraph> _graphs;

    private Igi2AiGraphs(Dictionary<int, Igi2AiGraph> graphs) => _graphs = graphs;

    /// <summary>Gets the graphs, by their task id.</summary>
    public IReadOnlyDictionary<int, Igi2AiGraph> Graphs => _graphs;

    /// <summary>Finds a graph by its task id, a soldier's <c>Graph ID</c>.</summary>
    /// <param name="id">The id.</param>
    /// <returns>The graph, or <see langword="null"/>.</returns>
    public Igi2AiGraph? Graph(int id) => _graphs.GetValueOrDefault(id);

    /// <summary>Reads every <c>AIGraph</c> a level places.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="resources">Where the graph files are read from.</param>
    /// <param name="natives">The script namespace, with the <c>NODECRITERIA_*</c> constants.</param>
    /// <param name="functions">The natives criteria text may call.</param>
    /// <param name="logger">Where load diagnostics go.</param>
    /// <returns>The graphs.</returns>
    /// <remarks>Retail aborts on a missing file (<c>Graph_ParseGraphStream</c>, <c>igi2 0x59B4B0</c>); here the graph is left out and logged.</remarks>
    public static Igi2AiGraphs Build(GameTask levelRoot, ResourceRegistry resources, NativeRegistry natives, Igi2ScriptFunctions functions, IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(functions);
        IOpenIgiLogger log = logger ?? NullLogger.Instance;
        Dictionary<int, Igi2AiGraph> graphs = [];
        foreach (GameTask task in levelRoot.Descendants())
        {
            if (task is not Igi2AiGraphTask graph || task.Id == GameTask.NoId)
            {
                continue;
            }

            string resource = Igi2AiGraphTask.ResourceNameFor(task.Id);
            if (!resources.TryLoad(resource, out ReadOnlyMemory<byte> bytes))
            {
                log.Warning(LogCategory, $"AIGraph {task.Id} has no '{resource}'.");
                continue;
            }

            Igi2AiGraph read = Igi2AiGraph.Read(bytes, graph.Placement, text => Igi2LineScript.Evaluate(text, natives, functions));
            read.Id = task.Id;

            // AIGraph_UpdateInternalDataAfterLoadHandler reads the graph's cover, when the level ships it.
            if (resources.TryLoad(Igi2AiGraph.CoverResourceNameFor(task.Id), out ReadOnlyMemory<byte> cover))
            {
                read.LoadCover(cover);
            }

            graphs[task.Id] = read;
        }

        return new Igi2AiGraphs(graphs);
    }
}
