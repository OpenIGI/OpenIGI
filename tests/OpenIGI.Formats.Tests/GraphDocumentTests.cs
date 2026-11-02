using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>Graph files edited as records: unchanged they save byte for byte, and a moved node is read back where it went.</summary>
public sealed class GraphDocumentTests
{
    /// <summary>Every named retail graph of both games saves back unchanged and agrees with the game's own reader.</summary>
    [RequiresTitleDataFact("igi1")]
    public void Igi1_graphs_round_trip() => AssertRoundTrips(GameDataSource.Igi1);

    /// <summary>The same for I.G.I. 2.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Igi2_graphs_round_trip() => AssertRoundTrips(GameDataSource.Igi2);

    /// <summary>A moved node changes 24 bytes and nothing else, and the game's reader sees it moved.</summary>
    [RequiresTitleDataFact("igi1")]
    public void A_moved_node_is_where_it_was_put()
    {
        string path = GameDataSource.Igi1.EnumerateFiles("graph*.dat").First(p => GraphDocument.IsNamed(File.ReadAllBytes(p)) && GraphDocument.Read(File.ReadAllBytes(p)).Nodes.Count > 2);
        byte[] original = File.ReadAllBytes(path);
        GraphDocument document = GraphDocument.Read(original);
        GraphDocumentNode node = document.Nodes[1];
        Vector3d moved = new(node.Position.X + 4096, node.Position.Y, node.Position.Z - 2048);

        Assert.True(document.SetNodePosition(node.Id, moved));
        byte[] saved = document.Write();
        Assert.Equal(original.Length, saved.Length);
        Assert.InRange(original.Zip(saved).Count(static p => p.First != p.Second), 1, 24);

        GraphFile game = GraphFile.Read(saved);
        Assert.Equal(moved, game.Nodes.Single(n => n.Id == node.Id).Position);
    }

    private static void AssertRoundTrips(GameDataSource source)
    {
        int named = 0;
        foreach (string path in source.EnumerateFiles("graph*.dat"))
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (!GraphDocument.IsNamed(bytes))
            {
                continue;
            }

            named++;
            GraphDocument document = GraphDocument.Read(bytes);
            Assert.True(bytes.AsSpan().SequenceEqual(document.Write()), $"{path} changed on a save with no edit.");

            GraphFile game = GraphFile.Read(bytes);
            Assert.Equal(game.Links.Count, document.Links.Count);
            // The document lists every node record; the game keeps some and discards the rest (I.G.I. 2 drops many).
            Assert.Equal(game.Nodes.Count + game.DiscardedNodeIds.Count, document.Nodes.Count);
        }

        Assert.True(named > 0, "No named graph file was found.");
    }
}
