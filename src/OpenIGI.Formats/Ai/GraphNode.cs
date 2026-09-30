using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.Ai;

/// <summary>One nav-graph node, as <c>0x4F9510</c> builds it.</summary>
/// <remarks>
/// <para>
/// The node lives in a flat array indexed by <see cref="Id"/>, so the array is as long as the file's
/// <c>nMaxNodes</c> and mostly empty — level 1's first graph declares 100 slots and fills one. The
/// engine's record is 576 bytes, of which the fields here occupy the first 52 plus the criteria string;
/// the rest is runtime state (a 256-byte scratch area, the owning graph, the resolved material).
/// </para>
/// <para>
/// Three guards on creation, all reproduced by <see cref="GraphFile"/>: an id of <b>1000 or more is
/// silently dropped</b>, an id past the current capacity grows the array, and a <b>duplicate id is
/// fatal</b> (<c>"GRAPHNODE ID ALREADY USED"</c> followed by a hang). The largest id in the retail corpus
/// is 380, so the cap is never approached.
/// </para>
/// <para>
/// <b>Ids start at 1, not 0.</b> Slot 0 is never occupied in any of the 300 retail graphs, and the
/// nearest-node query walks slots from 0 and simply finds nothing there.
/// </para>
/// </remarks>
public sealed class GraphNode
{
    /// <summary>The id at or above which a node is silently discarded (<c>0x4F9510</c>).</summary>
    public const int IdLimit = 1000;

    /// <summary>Engine units per metre, which is what <see cref="Radius"/> has to be scaled by.</summary>
    public const double MetreInUnits = 4096.0;

    /// <summary>Initializes a new instance of the <see cref="GraphNode"/> class.</summary>
    /// <param name="id">The node id, which is also its slot.</param>
    /// <param name="position">The node's world position.</param>
    /// <param name="gamma">The node's <c>vNodeGamma</c>.</param>
    /// <param name="radius">The node's radius in metres.</param>
    /// <param name="materialId">The material id the node names, or <c>-1</c> for none.</param>
    /// <param name="criteriaName">The criteria constant's name, empty for an ordinary node.</param>
    /// <param name="criteria">The bit the name resolves to.</param>
    public GraphNode(
        int id,
        Vector3d position,
        float gamma,
        float radius,
        int materialId,
        string criteriaName,
        GraphNodeCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(criteriaName);

        Id = id;
        Position = position;
        Gamma = gamma;
        Radius = radius;
        MaterialId = materialId;
        CriteriaName = criteriaName;
        Criteria = criteria;
    }

    /// <summary>Gets the node's id, which is also its index in the graph's node array.</summary>
    public int Id { get; }

    /// <summary>Gets the node's world position, in engine units.</summary>
    /// <remarks>
    /// The file stores positions in the graph's own frame. <see cref="GraphFile.ApplyWorldOffset(Vector3d)"/>
    /// transforms them into world space by adding the graph task's declared position — which is what
    /// <c>0x4F9770</c> does every time a caller asks for a node's position.
    /// </remarks>
    public Vector3d Position { get; internal set; }

    /// <summary>Gets the node's <c>vNodeGamma</c>.</summary>
    /// <remarks>
    /// Read as a <see cref="float"/> and stored at <c>node+32</c> without conversion. Nothing on the paths
    /// read so far consumes it; the name suggests a facing angle. Carried, not interpreted.
    /// </remarks>
    public float Gamma { get; }

    /// <summary>Gets the node's radius, <b>in metres</b>.</summary>
    /// <remarks>
    /// The nearest-node query multiplies it by <c>4096.0</c> before subtracting it from a distance
    /// (<c>0x5096F0</c>), which is what fixes the unit: this is metres while the position is engine units.
    /// The engine also caches <c>radius + radius</c> at <c>node+40</c>.
    /// </remarks>
    public float Radius { get; }

    /// <summary>Gets the material id the node names, or <c>-1</c> when it names none.</summary>
    /// <remarks>
    /// Resolved at load time through the same material table the world uses. <c>-1</c> stores a null
    /// pointer on the hashed path; the positional path handles it differently — see
    /// <see cref="GraphFile"/>.
    /// </remarks>
    public int MaterialId { get; }

    /// <summary>Gets the criteria constant's name as the file spells it, or the empty string.</summary>
    public string CriteriaName { get; }

    /// <summary>Gets the criteria bit <see cref="CriteriaName"/> resolves to.</summary>
    public GraphNodeCriteria Criteria { get; }

    /// <summary>Gets a value indicating whether the nearest-node query will skip this node.</summary>
    /// <remarks>The query masks against the <see cref="GraphNodeCriteria.View"/> bit alone (<c>0x509870</c>).</remarks>
    public bool IsExcludedFromNearestQuery => (Criteria & GraphNodeCriteria.View) != 0;

    /// <summary>Resolves a criteria name to its bit.</summary>
    /// <param name="name">The name as stored, or the empty string.</param>
    /// <returns>The bit, or <see cref="GraphNodeCriteria.None"/> for an empty or unregistered name.</returns>
    /// <remarks>
    /// An unregistered name resolves to zero rather than being an error: the engine looks the string up in
    /// the same global name table the script constants live in and a miss yields nothing. Every name in the
    /// retail corpus is one of the three registered ones.
    /// </remarks>
    public static GraphNodeCriteria ResolveCriteria(string name) => name switch
    {
        "NODECRITERIA_DOOR" => GraphNodeCriteria.Door,
        "NODECRITERIA_VIEW" => GraphNodeCriteria.View,
        "NODECRITERIA_STAIR" => GraphNodeCriteria.Stair,
        _ => GraphNodeCriteria.None,
    };

    /// <inheritdoc/>
    public override string ToString() =>
        $"node {Id} at {Position} r={Radius}m" + (Criteria == GraphNodeCriteria.None ? string.Empty : $" [{Criteria}]");
}
