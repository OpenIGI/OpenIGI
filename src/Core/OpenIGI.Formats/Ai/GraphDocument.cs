using System.Buffers.Binary;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.Ai;

/// <summary>A node of a graph document: its id, where it is, and which record holds the position.</summary>
/// <param name="Id">The node id.</param>
/// <param name="Position">The position, in the graph's own frame and world units.</param>
/// <param name="PositionRecord">The index of the record that holds the position, or -1 when the node has none.</param>
public readonly record struct GraphDocumentNode(int Id, Vector3d Position, int PositionRecord);

/// <summary>
/// A named graph file (<c>graph&lt;n&gt;.dat</c>) as its records, kept exactly as written, so it can be edited and saved
/// back without disturbing anything it does not change.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="GraphFile"/> interprets a graph for the game; this keeps one for an editor. Each record is the field's hash,
/// its declared type and its value, each tagged, and every record's bytes are kept verbatim: a
/// document saved unchanged is the file it was read from, and moving a node rewrites the 24 bytes of its position
/// and nothing else.
/// </para>
/// <para>
/// Only the named variant is editable. The positional variant (seven files, all in I.G.I.'s level 8) has no record
/// boundaries to edit within and is refused.
/// </para>
/// <para>
/// The route table the file carries is not rebuilt when a node moves: routes still connect the same nodes, though one
/// may no longer be the shortest.
/// </para>
/// </remarks>
public sealed class GraphDocument
{
    private static readonly uint NodeIdHash = GraphFieldHash.Of("nNodeID");
    private static readonly uint PositionHash = GraphFieldHash.Of("tNodePosition");
    private static readonly uint LinkFromHash = GraphFieldHash.Of("nLinkID1");
    private static readonly uint LinkToHash = GraphFieldHash.Of("nLinkID2");
    private static readonly uint RouteTableHash = GraphFieldHash.Of("RouteTable");

    // Every record is the field hash as a tagged UInt32 (5 bytes), the declared type as a tagged byte (2), then the
    // tagged value, whose payload therefore starts 8 bytes in.
    private const int ValueOffset = 5 + 2 + 1;

    private readonly List<byte[]> _records = [];
    private readonly List<uint> _hashes = [];

    private GraphDocument()
    {
    }

    /// <summary>Gets the nodes, in file order.</summary>
    public IReadOnlyList<GraphDocumentNode> Nodes { get; private set; } = [];

    /// <summary>Gets the links as node-id pairs, in file order.</summary>
    public IReadOnlyList<(int From, int To)> Links { get; private set; } = [];

    /// <summary>Gets the number of records.</summary>
    public int RecordCount => _records.Count;

    /// <summary>Gets whether bytes are a named graph file, the variant this document reads.</summary>
    /// <param name="data">The bytes.</param>
    /// <returns><see langword="true"/> when they open with the named variant's magic.</returns>
    public static bool IsNamed(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(data) == GraphFile.Magic;

    /// <summary>Reads a named graph file.</summary>
    /// <param name="data">The file's bytes.</param>
    /// <returns>The document.</returns>
    /// <exception cref="GraphFormatException">The file is positional, or a record runs past the end.</exception>
    public static GraphDocument Read(ReadOnlyMemory<byte> data)
    {
        if (!IsNamed(data.Span))
        {
            throw new GraphFormatException("Only named graph files can be edited; this one is positional.");
        }

        GraphDocument document = new();
        GraphStreamReader reader = new(data);
        reader.ReadRawUInt32();
        while (!reader.IsAtEnd)
        {
            int start = reader.Position;
            uint hash = reader.ReadUInt32();
            reader.SkipValue();
            if (hash == RouteTableHash)
            {
                // Not a tagged value: an element-type byte and a length, each tagged, then that many raw bytes
                // (0x4FA9D0, as GraphFile reads it).
                reader.ReadBlob(out _, out int declared, 0);
                reader.ReadBlobPayload(declared);
            }
            else
            {
                reader.SkipValue();
            }

            document._hashes.Add(hash);
            document._records.Add(data.Slice(start, reader.Position - start).ToArray());
        }

        document.Index();
        return document;
    }

    /// <summary>Writes the document back to a graph file.</summary>
    /// <returns>The bytes.</returns>
    public byte[] Write()
    {
        byte[] bytes = new byte[4 + _records.Sum(static r => r.Length)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, GraphFile.Magic);
        int at = 4;
        foreach (byte[] record in _records)
        {
            record.CopyTo(bytes, at);
            at += record.Length;
        }

        return bytes;
    }

    /// <summary>Moves a node.</summary>
    /// <param name="id">The node id.</param>
    /// <param name="position">The new position, in the graph's frame and world units.</param>
    /// <returns><see langword="false"/> when no node has that id, or it has no position record.</returns>
    public bool SetNodePosition(int id, Vector3d position)
    {
        int index = -1;
        for (int i = 0; i < Nodes.Count; i++)
        {
            if (Nodes[i].Id == id && Nodes[i].PositionRecord >= 0)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return false;
        }

        Span<byte> value = _records[Nodes[index].PositionRecord].AsSpan(ValueOffset, 24);
        BinaryPrimitives.WriteDoubleLittleEndian(value, position.X);
        BinaryPrimitives.WriteDoubleLittleEndian(value[8..], position.Y);
        BinaryPrimitives.WriteDoubleLittleEndian(value[16..], position.Z);
        Index();
        return true;
    }

    private void Index()
    {
        List<GraphDocumentNode> nodes = [];
        List<(int, int)> links = [];
        int node = -1;
        int from = -1;
        for (int i = 0; i < _records.Count; i++)
        {
            uint hash = _hashes[i];
            if (hash == NodeIdHash)
            {
                node = Int32Value(_records[i]);
                nodes.Add(new GraphDocumentNode(node, default, -1));
            }
            else if (hash == PositionHash && nodes.Count > 0 && nodes[^1].Id == node && _records[i].Length >= ValueOffset + 24)
            {
                ReadOnlySpan<byte> value = _records[i].AsSpan(ValueOffset, 24);
                Vector3d position = new(
                    BinaryPrimitives.ReadDoubleLittleEndian(value),
                    BinaryPrimitives.ReadDoubleLittleEndian(value[8..]),
                    BinaryPrimitives.ReadDoubleLittleEndian(value[16..]));
                nodes[^1] = nodes[^1] with { Position = position, PositionRecord = i };
            }
            else if (hash == LinkFromHash)
            {
                from = Int32Value(_records[i]);
            }
            else if (hash == LinkToHash)
            {
                links.Add((from, Int32Value(_records[i])));
            }
        }

        Nodes = nodes;
        Links = links;
    }

    private static int Int32Value(byte[] record) =>
        record.Length >= ValueOffset + 4 ? BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(ValueOffset, 4)) : -1;
}
