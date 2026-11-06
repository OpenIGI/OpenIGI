using System.Buffers.Binary;
using System.Text;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.Ai;

/// <summary>One node to write: what a named graph file keeps for it.</summary>
/// <param name="Id">The node id, from 1, below the slot count.</param>
/// <param name="Position">The position in the graph's own frame (relative to its <c>AIGraph</c> task), in world units.</param>
/// <param name="Gamma">The facing, in radians; the shipped graphs leave almost every node at 0.</param>
/// <param name="Radius">The radius as stored; 0.5 is the shipped graphs' commonest.</param>
/// <param name="Material">The ground material under the node, or -1 for none.</param>
/// <param name="Criteria">The criteria name, such as <c>NODECRITERIA_DOOR</c>, or empty.</param>
public sealed record GraphWriteNode(int Id, Vector3d Position, float Gamma = 0f, float Radius = 0.5f, int Material = -1, string Criteria = "");

/// <summary>
/// Writes a named I.G.I. graph file (<c>graph&lt;n&gt;.dat</c>) from nodes and links, as the original tools laid one out.
/// </summary>
/// <remarks>
/// <para>
/// The layout is the shipped files': the magic, <c>nMaxNodes</c>, the <c>RouteTable</c>, then each node's six fields
/// (<c>nNodeID</c>, <c>tNodePosition</c>, <c>vNodeGamma</c>, <c>vNodeRadius</c>, <c>nNodeMaterial</c>,
/// <c>nNodeCriteria</c>) and each link's three (<c>nLinkID1</c>, <c>nLinkID2</c>, <c>eLinkType</c>). A record is its field's
/// hash as a tagged UInt32, its declared type as a tagged byte, and its tagged value, the encoding
/// <see cref="GraphStreamReader"/> reads; a string is a length byte and its bytes with the terminating NUL.
/// </para>
/// <para>
/// Written from what <see cref="GraphFile"/> reads out of a shipped graph and that graph's own route table, every named
/// graph of I.G.I. comes back byte for byte. A new graph's routes are baked by <see cref="GraphRouteBuilder"/>.
/// </para>
/// </remarks>
public static class GraphFileWriter
{
    /// <summary>The link type every link of the shipped graphs has.</summary>
    public const int WalkLink = 1;

    /// <summary>Writes a graph file.</summary>
    /// <param name="maxNodes">The slot count: every node id is below it, and the route table is square over it.</param>
    /// <param name="nodes">The nodes, in file order.</param>
    /// <param name="links">The links, in file order.</param>
    /// <param name="routes">The route table's bytes, <c>8 × maxNodes²</c>; baked from the nodes and links when null.</param>
    /// <returns>The file's bytes.</returns>
    /// <exception cref="ArgumentException">A node id is outside <c>[1, maxNodes)</c> or repeated, or the route table is the wrong size.</exception>
    public static byte[] Write(int maxNodes, IReadOnlyList<GraphWriteNode> nodes, IReadOnlyList<GraphLink> links, ReadOnlyMemory<byte>? routes = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxNodes);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(links);
        HashSet<int> ids = [];
        foreach (GraphWriteNode node in nodes)
        {
            if (node.Id < 1 || node.Id >= maxNodes || !ids.Add(node.Id))
            {
                throw new ArgumentException($"Node id {node.Id} is repeated or outside 1..{maxNodes - 1}.", nameof(nodes));
            }
        }

        ReadOnlyMemory<byte> table = routes ?? GraphRouteBuilder.Build(
            maxNodes,
            [.. nodes.Select(static n => (n.Id, n.Position))],
            [.. links.Select(static l => (l.NodeA, l.NodeB))]);
        if (table.Length != GraphRouteTable.EntryStride * maxNodes * maxNodes)
        {
            throw new ArgumentException($"A route table over {maxNodes} slots is {GraphRouteTable.EntryStride * maxNodes * maxNodes} bytes, not {table.Length}.", nameof(routes));
        }

        using MemoryStream stream = new();
        Span<byte> magic = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(magic, GraphFile.Magic);
        stream.Write(magic);
        Int32(stream, "nMaxNodes", maxNodes);

        // The route table: its element type and length as tagged values, then the raw entries.
        Head(stream, "RouteTable", GraphValueType.Blob);
        stream.WriteByte((byte)GraphValueType.Bool8);
        stream.WriteByte((byte)GraphValueType.Blob);
        Tagged(stream, GraphValueType.Int32, table.Length);
        stream.Write(table.Span);

        Span<byte> position = stackalloc byte[24];
        foreach (GraphWriteNode node in nodes)
        {
            Int32(stream, "nNodeID", node.Id);
            Head(stream, "tNodePosition", GraphValueType.Real64x3);
            BinaryPrimitives.WriteDoubleLittleEndian(position, node.Position.X);
            BinaryPrimitives.WriteDoubleLittleEndian(position[8..], node.Position.Y);
            BinaryPrimitives.WriteDoubleLittleEndian(position[16..], node.Position.Z);
            stream.WriteByte((byte)GraphValueType.Real64x3);
            stream.Write(position);
            Real32(stream, "vNodeGamma", node.Gamma);
            Real32(stream, "vNodeRadius", node.Radius);
            Int32(stream, "nNodeMaterial", node.Material);
            Head(stream, "nNodeCriteria", GraphValueType.String8);
            byte[] text = Encoding.Latin1.GetBytes(node.Criteria + "\0");
            stream.WriteByte((byte)GraphValueType.String8);
            stream.WriteByte(checked((byte)text.Length));
            stream.Write(text);
        }

        foreach (GraphLink link in links)
        {
            Int32(stream, "nLinkID1", link.NodeA);
            Int32(stream, "nLinkID2", link.NodeB);
            Int32(stream, "eLinkType", link.LinkType);
        }

        return stream.ToArray();
    }

    // A record's opening: the field's hash as a tagged UInt32, then its declared type as a tagged byte.
    private static void Head(Stream stream, string field, GraphValueType type)
    {
        Tagged(stream, GraphValueType.UInt32, unchecked((int)GraphFieldHash.Of(field)));
        stream.WriteByte((byte)GraphValueType.Bool8);
        stream.WriteByte((byte)type);
    }

    private static void Int32(Stream stream, string field, int value)
    {
        Head(stream, field, GraphValueType.Int32);
        Tagged(stream, GraphValueType.Int32, value);
    }

    private static void Real32(Stream stream, string field, float value)
    {
        Head(stream, field, GraphValueType.Real32);
        Tagged(stream, GraphValueType.Real32, BitConverter.SingleToInt32Bits(value));
    }

    private static void Tagged(Stream stream, GraphValueType tag, int bits)
    {
        Span<byte> value = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(value, bits);
        stream.WriteByte((byte)tag);
        stream.Write(value);
    }
}
