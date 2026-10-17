using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// The per-node occupancy claims the free-position search makes, and the sub-slot positions it hands
/// out — the global record list at <c>0x57D768</c>, records built by <c>sub_452C60</c>, sub-slots
/// claimed by <c>0x48B780</c>.
/// </summary>
/// <remarks>
/// <para>
/// A node is divided into a grid of <b>rows = cols = trunc(radius × 0.5)</b> cells (the radius is the
/// node's authored figure in metres), each cell <b>two metres</b> (8192 units) square and holding up to
/// <see cref="CellCapacity"/> claims. A node whose record says <c>occupants ≥ rows × cols</c> is full and
/// the acceptance test (<c>0x48B260</c>) rejects it outright — which means a node with a radius under
/// four metres holds exactly <b>one</b> soldier: its first claimant takes the centre cell through the
/// no-record arm, and the zero-capacity record then turns everyone else away.
/// </para>
/// <para>
/// The scan over the grid starts from a different corner each time, driven by a shared round-robin
/// counter (<c>dword_5C8BB8</c>) that increments <b>per acceptance test</b> — it is what fans a squad out
/// over a large node instead of stacking everyone on the same cell.
/// </para>
/// <para>
/// The claimed cell's world position — cell offset from the grid centre, clamped to ±radius, rotated by
/// the node's authored heading — is what retail writes to <c>HumanAI+16048</c>, and what Follow
/// (<c>0x48C860</c>) and RePosition (<c>0x48C760</c>) actually walk to via <c>sub_4522A0</c>.
/// </para>
/// </remarks>
public sealed class AiNodeOccupancy
{
    /// <summary>How many claims one grid cell holds — <c>0x48B780</c>'s literal 25.</summary>
    public const int CellCapacity = 25;

    /// <summary>The grid cell pitch — two metres, <c>0x48B780</c>'s literal 8192.</summary>
    public const double CellSizeInUnits = 8192.0;

    private sealed class Record
    {
        public required (GraphFile Graph, int NodeId) Key { get; init; }

        public required int Rows { get; init; }

        public required int Cols { get; init; }

        public required int[] Cells { get; init; }

        public int Occupants { get; set; }
    }

    private sealed record Claim(Record Record, int Row, int Col);

    private readonly Dictionary<(GraphFile Graph, int NodeId), Record> _records = [];
    private readonly Dictionary<object, Claim> _claims = [];
    private int _pattern;

    /// <summary>Whether a node's record says it is full — the acceptance test's first occupancy gate.</summary>
    /// <param name="graph">The node's graph.</param>
    /// <param name="nodeId">The node.</param>
    /// <returns><see langword="true"/> when a record exists and <c>occupants ≥ rows × cols</c>.</returns>
    /// <remarks>A node nobody has claimed yet has no record and is never full.</remarks>
    public bool IsFull(GraphFile graph, int nodeId)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return _records.TryGetValue((graph, nodeId), out Record? record)
            && record.Occupants >= record.Rows * record.Cols;
    }

    /// <summary>
    /// Claims the cell of a node nearest a point — <c>sub_452D20</c>, which a walk planned with its claim flag
    /// set (<c>0x4518E0</c>'s fourth argument) makes for its destination, releasing the soldier's old claim.
    /// </summary>
    /// <param name="graph">The node's graph.</param>
    /// <param name="node">The destination node.</param>
    /// <param name="claimant">The soldier claiming.</param>
    /// <param name="at">The point the walk ends at — the node's own position when it names none.</param>
    /// <returns>The claimed cell's position, which retail keeps at <c>HumanAI+16048</c>.</returns>
    /// <remarks>
    /// Unlike the free-position search this never refuses: the cell is the point's, clamped into the grid,
    /// however many already stand there. The patrol walker, WalkToNode, RunToNode and MoveToEvent all plan
    /// this way, so a patrolling guard always holds a claim on where it is heading -- which is where a fight
    /// that finds it off its node sends it back to (RePosition, <c>0x48C760</c>).
    /// </remarks>
    public Vector3d ClaimAt(GraphFile graph, GraphNode node, object claimant, Vector3d at)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(claimant);

        Release(claimant);
        if (!_records.TryGetValue((graph, node.Id), out Record? record))
        {
            record = CreateRecord(graph, node);
        }

        // The point in the node's own frame, in two-metre cells from the grid's corner, clamped into it.
        double dx = at.X - node.Position.X;
        double dy = at.Y - node.Position.Y;
        double sin = Math.Sin(node.Gamma);
        double cos = Math.Cos(node.Gamma);
        double localX = (cos * dx) + (sin * dy);
        double localY = (-sin * dx) + (cos * dy);
        int row = (int)Math.Clamp((record.Rows * 0.5) + (localX / CellSizeInUnits), 0.0, record.Rows);
        int col = (int)Math.Clamp((record.Cols * 0.5) + (localY / CellSizeInUnits), 0.0, record.Cols);
        Take(claimant, record, row, col);

        double offsetX = (row - (record.Rows * 0.5)) * CellSizeInUnits;
        double offsetY = (col - (record.Cols * 0.5)) * CellSizeInUnits;
        return new Vector3d(
            node.Position.X + ((cos * offsetX) - (sin * offsetY)),
            node.Position.Y + ((sin * offsetX) + (cos * offsetY)),
            node.Position.Z);
    }

    /// <summary>The node a soldier's claim is on — the record at <c>HumanAI+16032</c>'s <c>+12</c>.</summary>
    /// <param name="claimant">The soldier.</param>
    /// <returns>The node id, or <see langword="null"/> when it holds no claim.</returns>
    public int? ClaimedNode(object claimant)
    {
        ArgumentNullException.ThrowIfNull(claimant);
        return _claims.TryGetValue(claimant, out Claim? claim) ? claim.Record.Key.NodeId : null;
    }

    /// <summary>Whether a soldier's own claim is on this node — <c>0x48B260</c>'s first occupancy test.</summary>
    /// <param name="graph">The node's graph.</param>
    /// <param name="nodeId">The node.</param>
    /// <param name="claimant">The soldier asking.</param>
    /// <returns><see langword="true"/> when the node's record is the one the soldier holds (<c>HumanAI+16032</c>).</returns>
    public bool IsClaimedBy(GraphFile graph, int nodeId, object claimant)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return _claims.TryGetValue(claimant, out Claim? claim) && claim.Record.Key == (graph, nodeId);
    }

    /// <summary>
    /// Claims a sub-slot on a node for a soldier, releasing whatever it held before, and returns the
    /// slot's world position — the search's accepted outcome.
    /// </summary>
    /// <param name="graph">The node's graph.</param>
    /// <param name="node">The node being claimed.</param>
    /// <param name="claimant">The soldier claiming. One claim per claimant; a new claim replaces it.</param>
    /// <param name="position">Receives the claimed position inside the node, in world units.</param>
    /// <returns><see langword="false"/> when every reachable cell is at capacity.</returns>
    public bool TryClaim(GraphFile graph, GraphNode node, object claimant, out Vector3d position)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(claimant);
        position = default;

        if (!_records.TryGetValue((graph, node.Id), out Record? record))
        {
            // 0x48B260's no-record arm: the offset is the grid's (0,0) cell — minus half the grid each
            // way — and the record is created by the claim itself (sub_452C60 with row = col = 0).
            record = CreateRecord(graph, node);
            position = SlotPosition(node, record, 0, 0);
            Take(claimant, record, 0, 0);
            return true;
        }

        // 0x48B260's grid walk: the shared counter picks which corner the scan starts from, and the
        // first cell under capacity is taken. The counter advances once per test, full or not.
        _pattern = (_pattern + 1) % 4;
        foreach ((int row, int col) in ScanOrder(_pattern, record.Rows, record.Cols))
        {
            if (record.Cells[(record.Cols * row) + col] >= CellCapacity)
            {
                continue;
            }

            position = SlotPosition(node, record, row, col);
            Take(claimant, record, row, col);
            return true;
        }

        return false;
    }

    /// <summary>Releases a soldier's claim, as the combat action's end and a replacement claim do.</summary>
    /// <param name="claimant">The soldier.</param>
    /// <remarks>
    /// The last release <b>frees the record</b> (<c>sub_452C10</c> unlinks it into the pool at zero
    /// occupants), which is what lets a small node — whose zero-capacity grid would otherwise reject
    /// everyone for ever — be claimed again through the no-record arm.
    /// </remarks>
    public void Release(object claimant)
    {
        ArgumentNullException.ThrowIfNull(claimant);
        if (!_claims.Remove(claimant, out Claim? claim))
        {
            return;
        }

        int index = (claim.Record.Cols * claim.Row) + claim.Col;
        if (index < claim.Record.Cells.Length)
        {
            claim.Record.Cells[index]--;
        }

        if (--claim.Record.Occupants <= 0)
        {
            _records.Remove(claim.Record.Key);
        }
    }

    private static IEnumerable<(int Row, int Col)> ScanOrder(int pattern, int rows, int cols)
    {
        // The four corner-start orders of 0x48B260's switch, in the order ++dword_5C8BB8 visits them.
        switch (pattern)
        {
            case 1:
                for (int col = 0; col < cols; col++)
                {
                    for (int row = rows - 1; row >= 0; row--)
                    {
                        yield return (row, col);
                    }
                }

                break;

            case 2:
                for (int row = 0; row < rows; row++)
                {
                    for (int col = cols - 1; col >= 0; col--)
                    {
                        yield return (row, col);
                    }
                }

                break;

            case 3:
                for (int col = cols - 1; col >= 0; col--)
                {
                    for (int row = rows - 1; row >= 0; row--)
                    {
                        yield return (row, col);
                    }
                }

                break;

            default:
                for (int row = 0; row < rows; row++)
                {
                    for (int col = 0; col < cols; col++)
                    {
                        yield return (row, col);
                    }
                }

                break;
        }
    }

    private static Vector3d SlotPosition(GraphNode node, Record record, int row, int col)
    {
        // 0x48B780: cell offset from the grid centre in two-metre steps, each axis clamped to the
        // node's ±radius, rotated into the node's authored frame and added to its position.
        double limit = node.Radius * GraphNode.MetreInUnits;
        double localX = Math.Clamp((row - (record.Rows / 2)) * CellSizeInUnits, -limit, limit);
        double localY = Math.Clamp((col - (record.Cols / 2)) * CellSizeInUnits, -limit, limit);
        double sin = Math.Sin(node.Gamma);
        double cos = Math.Cos(node.Gamma);
        return new Vector3d(
            node.Position.X + ((cos * localX) - (sin * localY)),
            node.Position.Y + ((sin * localX) + (cos * localY)),
            node.Position.Z);
    }

    private Record CreateRecord(GraphFile graph, GraphNode node)
    {
        // sub_452C60: rows = cols = trunc(radius * 0.5) — the radius is metres, so one grid cell per
        // two metres of radius, and a small node gets a zero-capacity grid whose record still counts
        // its single centre-cell occupant.
        int side = Math.Max(0, (int)(node.Radius * 0.5f));
        Record record = new()
        {
            Key = (graph, node.Id),
            Rows = side,
            Cols = side,
            Cells = new int[Math.Max(1, side * side)],
        };
        _records[(graph, node.Id)] = record;
        return record;
    }

    private void Take(object claimant, Record record, int row, int col)
    {
        Release(claimant);
        int index = record.Cols * row + col;
        if (index < record.Cells.Length)
        {
            record.Cells[index]++;
        }

        record.Occupants++;
        _claims[claimant] = new Claim(record, row, col);
    }
}
