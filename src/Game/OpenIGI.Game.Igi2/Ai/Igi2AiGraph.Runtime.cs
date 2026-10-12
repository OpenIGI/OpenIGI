using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ilff;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Ai;

/// <summary>Where a position lies against the nearest node, <c>AIGraphGetClosestNode_e</c>.</summary>
public enum Igi2AiClosestNode
{
    /// <summary>The graph has no node a soldier may stand on.</summary>
    NoNode,

    /// <summary>The nearest node is found, and the position is inside its radius.</summary>
    InsideNode,

    /// <summary>The nearest node is found, but the position is outside its radius.</summary>
    OutsideNode,
}

/// <content>A node's state while the level runs: how soldiers weigh it, who holds it.</content>
public sealed partial class Igi2AiGraphNode
{
    /// <summary>Gets or sets what the node adds to a path's cost, <c>vNodeWeight</c>: raised while soldiers stand on it.</summary>
    public float Weight { get; set; }

    /// <summary>Gets or sets the id of whoever holds the node, <c>nOccupiedByID</c>, or -1.</summary>
    public int OccupiedById { get; set; } = -1;

    /// <summary>Gets or sets the indoor sector the node lies in, <c>ptSectorQTask</c>, or <see langword="null"/> outdoors.</summary>
    public object? Sector { get; set; }
}

/// <summary>A node's cover: from which of 32 directions it is sheltered, <c>AIGraphNodeCoverItem_s</c>.</summary>
public sealed class Igi2AiCoverItem
{
    /// <summary>Gets the node it belongs to.</summary>
    public required int NodeId { get; init; }

    /// <summary>Gets, for each of 32 directions around the node, how it is sheltered from that way: nought not at all; bit 4 standing only.</summary>
    public required int[] CoverCircle { get; init; }

    /// <summary>Gets or sets its flags: bit 1 is taken (<c>AIGraph_LockCoverNode</c>), bit 2 a soldier stands on it.</summary>
    public int Flags { get; set; }
}

/// <content>The queries soldiers ask a graph as the level runs (<c>aigraph.c</c>, <c>graph.c</c>).</content>
public sealed partial class Igi2AiGraph
{
    /// <summary>The <c>NODECRITERIA_VEHICLE</c> bit, <c>AIGraph_bVehicleNodeCriteria</c>.</summary>
    public const int VehicleCriteria = 1;

    /// <summary>The <c>NODECRITERIA_DOOR</c> bit, <c>AIGraph_bDoorNodeCriteria</c>.</summary>
    public const int DoorCriteria = 2;

    /// <summary>The <c>NODECRITERIA_VIEW</c> bit, <c>AIGraph_bViewNodeCriteria</c>: a point to look at, which nobody stands on.</summary>
    public const int ViewCriteria = 4;

    /// <summary>The <c>NODECRITERIA_STAIR</c> bit, <c>AIGraph_bStairNodeCriteria</c>.</summary>
    public const int StairCriteria = 8;

    private readonly List<Igi2AiCoverItem> _cover = [];

    /// <summary>Gets or sets the graph's task id, <c>nGraphID</c>.</summary>
    public int Id { get; set; } = -1;

    /// <summary>Gets the nodes' cover, in the order the cover file lists it, <c>tNodeCoverItemList</c>.</summary>
    public IReadOnlyList<Igi2AiCoverItem> Cover => _cover;

    /// <summary>Gets the node in a slot, or <see langword="null"/> when the slot is empty or out of range (<c>Graph_GetNode</c>).</summary>
    /// <param name="id">The slot.</param>
    /// <returns>The node.</returns>
    public Igi2AiGraphNode? NodeAt(int id) => id >= 0 && id < MaxNodes ? Node(id) : null;

    /// <summary>
    /// Finds the node nearest a position, by straight distance, skipping view nodes and, unless allowed, vehicle nodes
    /// (<c>AIGraph_GetClosestNode</c>).
    /// </summary>
    /// <param name="position">The position.</param>
    /// <param name="allowVehicleNodes">Whether a vehicle node may be the answer.</param>
    /// <param name="node">Receives the node, or <see langword="null"/>.</param>
    /// <returns>Whether there was one, and whether the position is inside it.</returns>
    /// <remarks>The slots are searched in order and the first of equally near nodes is kept.</remarks>
    public Igi2AiClosestNode ClosestNode(Vector3d position, bool allowVehicleNodes, out Igi2AiGraphNode? node)
    {
        node = null;
        double best = 0.0;
        for (int i = 0; i < MaxNodes; i++)
        {
            if (Node(i) is not { } candidate
                || (candidate.Criteria & ViewCriteria) != 0
                || (!allowVehicleNodes && (candidate.Criteria & VehicleCriteria) != 0))
            {
                continue;
            }

            Vector3d at = candidate.Position;
            Fpu dz = (Fpu)at.Z - position.Z, dy = (Fpu)at.Y - position.Y, dx = (Fpu)at.X - position.X;
            double squared = (((dz * dz) + (dy * dy)) + (dx * dx)).Value;
            if (node is null || squared < best)
            {
                best = squared;
                node = candidate;
            }
        }

        if (node is null)
        {
            return Igi2AiClosestNode.NoNode;
        }

        return ((Fpu)node.Radius * 4096.0).Value <= ((Fpu)best * 0.000244140625).Value
            ? Igi2AiClosestNode.OutsideNode
            : Igi2AiClosestNode.InsideNode;
    }

    /// <summary>The node nearest a position, or <see langword="null"/>, as <see cref="ClosestNode"/> finds it.</summary>
    /// <param name="position">The position.</param>
    /// <param name="allowVehicleNodes">Whether a vehicle node may be the answer.</param>
    /// <returns>The node.</returns>
    public Igi2AiGraphNode? Nearest(Vector3d position, bool allowVehicleNodes)
    {
        ClosestNode(position, allowVehicleNodes, out Igi2AiGraphNode? node);
        return node;
    }

    /// <summary>
    /// Walks from a node toward a position, each step to the nearest neighbour while that comes nearer, at most a number of steps
    /// (<c>AIGraph_GetClosestNodeFromStartingNode</c>).
    /// </summary>
    /// <param name="start">The node to start from.</param>
    /// <param name="position">The position.</param>
    /// <param name="maxSteps">The most steps taken.</param>
    /// <returns>The node the walk ended on, or <paramref name="start"/> when no neighbour came nearer.</returns>
    /// <remarks>A neighbour that is a vehicle node is only stepped onto from a vehicle node.</remarks>
    public Igi2AiGraphNode ClosestNodeFromStartingNode(Igi2AiGraphNode start, Vector3d position, int maxSteps)
    {
        ArgumentNullException.ThrowIfNull(start);
        Igi2AiGraphNode? closest = null;
        float closestDistance = 0f;
        Igi2AiGraphNode node = start;
        while (maxSteps != 0)
        {
            Igi2AiGraphNode? nearest = null;
            float nearestDistance = 0f;
            foreach (Igi2AiGraphLink link in node.Links)
            {
                if (NodeAt(link.Other(node.Id)) is not { } neighbour
                    || ((node.Criteria & VehicleCriteria) == 0 && (neighbour.Criteria & VehicleCriteria) != 0))
                {
                    continue;
                }

                Vector3d at = neighbour.Position;
                float dx = ((Fpu)at.X - position.X).F, dy = ((Fpu)at.Y - position.Y).F;
                Fpu dz = (Fpu)at.Z - position.Z;
                Fpu distance = Fpu.Sqrt((((Fpu)dx * dx) + ((Fpu)dy * dy)) + (dz * dz));
                if (nearest is null || distance.Value < nearestDistance)
                {
                    nearestDistance = distance.F;
                    nearest = neighbour;
                }
            }

            if (nearest is null || (closest is not null && nearestDistance >= closestDistance))
            {
                break;
            }

            closestDistance = nearestDistance;
            closest = nearest;
            node = nearest;
            maxSteps--;
        }

        return closest ?? start;
    }

    /// <summary>Adds to a node's weight, never below nothing (<c>Graph_AddWeightToNode</c>).</summary>
    /// <param name="id">The node.</param>
    /// <param name="weight">What to add.</param>
    public void AddWeight(int id, float weight)
    {
        if (NodeAt(id) is { } node)
        {
            Fpu sum = (Fpu)weight + node.Weight;
            node.Weight = sum.Value <= 0.0 ? 0f : sum.F;
        }
    }

    /// <summary>
    /// Whether a position lies within a node's radius plus a margin, measured along the node's own axes on the ground, and, when
    /// asked, within a metre of its height (<c>Graph_IsPositionOnNode</c>).
    /// </summary>
    /// <param name="position">The position.</param>
    /// <param name="node">The node.</param>
    /// <param name="deviance">The margin, in metres.</param>
    /// <param name="checkHeight">Whether the height must be within a metre too.</param>
    /// <returns><see langword="true"/> when on it. A position straight over the centre is not, as in the original.</returns>
    public static bool IsPositionOnNode(Vector3d position, Igi2AiGraphNode node, double deviance, bool checkHeight)
    {
        ArgumentNullException.ThrowIfNull(node);
        Vector3d at = node.Position;
        Fpu rx = (Fpu)position.X - at.X, ry = (Fpu)position.Y - at.Y;
        Fpu rz = (Fpu)position.Z - at.Z;
        if (checkHeight && Fpu.Abs(rz).Value > 4096.0)
        {
            return false;
        }

        if (rx.Value == 0.0 && ry.Value == 0.0)
        {
            return false;
        }

        // An unattached graph's node axes are the world's.
        Fpu across = ((Fpu)0.0 * ry) + ((Fpu)1.0 * rx) + ((Fpu)0.0 * 0.0);
        Fpu along = ((Fpu)1.0 * ry) + ((Fpu)0.0 * rx) + ((Fpu)0.0 * 0.0);
        Fpu reach = (Fpu)node.Radius + deviance;
        double low = (-4096.0 * reach).Value, high = (reach * 4096.0).Value;
        return !(across.Value < low) && across.Value <= high && along.Value >= low && along.Value <= high;
    }

    /// <summary>Which of 32 directions around a node a position lies in (<c>AIGraph_GetCoverSectorFromPosition</c>).</summary>
    /// <param name="node">The node's position.</param>
    /// <param name="position">The position.</param>
    /// <returns>The direction, 0 to 31; 0 when the position is on the node.</returns>
    public static int CoverSector(Vector3d node, Vector3d position)
    {
        Fpu dx = (Fpu)position.X - node.X, dy = (Fpu)position.Y - node.Y, dz = (Fpu)position.Z - node.Z;
        Fpu length = Fpu.Sqrt(((dx * dx) + (dy * dy)) + (dz * dz));
        if (length.Value == 0.0)
        {
            return 0;
        }

        float cos = (((Fpu)1.0 / length) * dx).F;
        float sin = (((Fpu)1.0 / length) * dy).F;
        Fpu angle = Igi2Pose.Acos(cos);
        if (sin < 0f)
        {
            angle = (Fpu)6.2831854820251465 - angle;
        }

        return Fpu.ToInt32((Fpu)5.092957973480225 * angle);
    }

    /// <summary>Whether a node's cover shelters it from a position (<c>AIGraph_CoverNodeProvidesCoverFromPosition</c>).</summary>
    /// <param name="item">The node's cover.</param>
    /// <param name="position">The position.</param>
    /// <returns><see langword="true"/> when sheltered.</returns>
    public bool ProvidesCover(Igi2AiCoverItem item, Vector3d position)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (NodeAt(item.NodeId) is not { } node)
        {
            return false;
        }

        // A position exactly due east, its angle a full turn, is sector 32: the original reads past the circle into the node id.
        int sector = CoverSector(node.Position, position);
        return sector < 32 ? item.CoverCircle[sector] != 0 : item.NodeId != 0;
    }

    /// <summary>Frees the cover a node took, <c>AIGraph_UnlockCoverNode</c>: the first item listed for it.</summary>
    /// <param name="nodeId">The node.</param>
    public void UnlockCover(int nodeId)
    {
        foreach (Igi2AiCoverItem item in _cover)
        {
            if (item.NodeId == nodeId)
            {
                item.Flags &= ~1;
                return;
            }
        }
    }

    /// <summary>Gets the first cover item listed for a node, or <see langword="null"/>.</summary>
    /// <param name="nodeId">The node.</param>
    /// <returns>The item.</returns>
    public Igi2AiCoverItem? CoverOf(int nodeId) => _cover.FirstOrDefault(item => item.NodeId == nodeId);

    /// <summary>
    /// Reads the graph's cover, <c>MISSION:graphs/graphcover&lt;id&gt;.dat</c> (<c>AIGraph_LoadNodeCoverData</c>): an <c>AICC</c> file
    /// of version 1 whose items are <c>AICN</c> chunks, each the 32 sheltered directions and the node's id.
    /// </summary>
    /// <param name="data">The file.</param>
    /// <remarks>A file of another version leaves the graph without cover.</remarks>
    public void LoadCover(ReadOnlyMemory<byte> data)
    {
        _cover.Clear();
        if (!IlffContainer.IsIlff(data.Span))
        {
            return;
        }

        IlffContainer file = IlffContainer.Read(data);
        if (file.Chunks.Count == 0 || file.Chunks[0].Payload.Length < 8
            || BitConverter.ToInt32(file.Chunks[0].Payload.Span[4..8]) != 1)
        {
            return;
        }

        foreach (IlffChunk chunk in file.Chunks.Skip(1))
        {
            ReadOnlySpan<byte> item = chunk.Payload.Span;
            if (item.Length < 144)
            {
                continue;
            }

            int[] circle = new int[32];
            for (int i = 0; i < 32; i++)
            {
                circle[i] = BitConverter.ToInt32(item[(12 + (4 * i))..]);
            }

            _cover.Add(new Igi2AiCoverItem { NodeId = BitConverter.ToInt32(item[140..]), CoverCircle = circle });
        }
    }

    /// <summary>The resource a graph's cover is read from, <c>MISSION:graphs/graphcover%d.dat</c>.</summary>
    /// <param name="taskId">The graph's task id.</param>
    /// <returns>The resource name.</returns>
    public static string CoverResourceNameFor(int taskId) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"MISSION:graphs/graphcover{taskId}.dat");
}
