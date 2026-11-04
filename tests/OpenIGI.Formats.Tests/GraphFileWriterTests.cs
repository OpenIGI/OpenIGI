using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>Writing named graph files: the shipped ones come back byte for byte, and a new one reads back as written.</summary>
public sealed class GraphFileWriterTests
{
    /// <summary>Every named graph of I.G.I., written again from what it reads as and its own route table, is the same file.</summary>
    [RequiresGameDataFact]
    public void Shipped_graphs_write_back_byte_for_byte()
    {
        int written = 0;
        foreach (string file in Directory.EnumerateFiles(Path.Combine(GameData.RequireRoot(), "missions"), "graph*.dat", SearchOption.AllDirectories))
        {
            byte[] bytes = File.ReadAllBytes(file);
            if (!GraphDocument.IsNamed(bytes) || Path.GetFileName(file).StartsWith("graphcover", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            GraphFile graph = GraphFile.Read(bytes);
            byte[] again = GraphFileWriter.Write(
                graph.MaxNodes,
                [.. graph.Nodes.Select(static n => new GraphWriteNode(n.Id, n.Position, n.Gamma, n.Radius, n.MaterialId, n.CriteriaName))],
                graph.Links,
                RouteBytes(bytes));
            Assert.True(bytes.AsSpan().SequenceEqual(again), $"{file} differs");
            written++;
        }

        Assert.True(written > 250, $"only {written} graphs");
    }

    /// <summary>A new graph reads back as written, its routes baked over its links.</summary>
    [Fact]
    public void A_new_graph_reads_back_with_its_routes()
    {
        GraphWriteNode[] nodes =
        [
            new(1, new Vector3d(0, 0, 0), Material: 6),
            new(2, new Vector3d(8 * 4096, 0, 0), Material: 6),
            new(3, new Vector3d(16 * 4096, 0, 0), Criteria: "NODECRITERIA_VIEW"),
        ];
        GraphLink[] links = [new(1, 2, GraphFileWriter.WalkLink), new(2, 3, GraphFileWriter.WalkLink)];

        GraphFile graph = GraphFile.Read(GraphFileWriter.Write(10, nodes, links));

        Assert.Equal(10, graph.MaxNodes);
        Assert.Equal([1, 2, 3], graph.Nodes.Select(static n => n.Id));
        Assert.Equal(6, graph.Nodes[0].MaterialId);
        Assert.Equal("NODECRITERIA_VIEW", graph.Nodes[2].CriteriaName);
        Assert.Equal(2, graph.Links.Count);
        Assert.Equal([2, 3], graph.RouteTable!.EnumerateRoute(1, 3)); // the steps after the start
    }

    /// <summary>A node added is linked and routed to; taken away, its links go too; a full graph grows a hundred slots.</summary>
    [Fact]
    public void Nodes_are_added_and_taken_away()
    {
        GraphWriteNode[] nodes = [new(1, new Vector3d(0, 0, 0)), new(2, new Vector3d(8 * 4096, 0, 0))];
        GraphDocument graph = GraphDocument.Read(GraphFileWriter.Write(3, nodes, [new GraphLink(1, 2, GraphFileWriter.WalkLink)]));

        GraphDocument grown = graph.WithNode(new Vector3d(16 * 4096, 0, 0), [2], out int id);
        Assert.Equal(3, id);
        Assert.Equal(GraphDocumentEdits.SlotStep, grown.MaxNodes);
        Assert.True(grown.AreLinked(2, 3));
        Assert.Equal([2, 3], GraphFile.Read(grown.Write()).RouteTable!.EnumerateRoute(1, 3));

        GraphDocument cut = grown.WithoutNode(2);
        Assert.Equal([1, 3], cut.Nodes.Select(static n => n.Id));
        Assert.Empty(cut.Links);
        Assert.Same(cut, cut.WithoutNode(42));
    }

    // A shipped file's route table, as GraphDocument finds it: the bytes after the record's 14-byte opening.
    private static byte[] RouteBytes(byte[] file)
    {
        GraphStreamReader reader = new(file);
        reader.ReadRawUInt32();
        while (!reader.IsAtEnd)
        {
            uint hash = reader.ReadUInt32();
            reader.SkipValue();
            if (hash == GraphFieldHash.Of("RouteTable"))
            {
                reader.ReadBlob(out _, out int declared, 0);
                return reader.ReadBlobPayload(declared).ToArray();
            }

            reader.SkipValue();
        }

        return [];
    }
}
