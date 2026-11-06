using OpenIGI.Editor.Core.Graphs;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Igi1;
using OpenIGI.Game.Igi2;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Vocabulary;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Desktop.Tests;

/// <summary>Every retail mission split into scopes: small graphs that between them hold all of its logic.</summary>
public sealed class GraphScopeRetailTests(ITestOutputHelper output)
{
    /// <summary>Project I.G.I.'s missions.</summary>
    [RequiresTitleDataFact("igi1")]
    public void Igi1_missions_split_into_small_graphs() => AssertScopes(new Igi1Runtime(), GameDataSource.Igi1, LevelExpressionDialect.Igi1);

    /// <summary>I.G.I. 2's missions.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Igi2_missions_split_into_small_graphs() => AssertScopes(new Igi2Runtime(), GameDataSource.Igi2, LevelExpressionDialect.Igi2);

    private void AssertScopes(IGameTitleRuntime title, GameDataSource source, LevelExpressionDialect dialect)
    {
        ScriptVocabulary vocabulary = title.CreateScriptVocabulary();
        List<int> sizes = [];
        foreach (string path in source.EnumerateFiles("objects.qvm"))
        {
            MissionDocument document = MissionDocument.Read(File.ReadAllBytes(path));
            MissionGraph graph = MissionGraph.Build(document, TaskCatalogue.Build(document.Declarations.Values, vocabulary), vocabulary, dialect);
            string level = Path.GetFileName(Path.GetDirectoryName(path))!;
            IReadOnlyList<GraphScope> scopes = GraphScopes.Build(graph);

            // Every task the mission wires is drawn somewhere, and every scope's parent is listed before it.
            HashSet<string> listed = [];
            foreach (GraphScope scope in scopes)
            {
                Assert.True(scope.Parent is null || listed.Contains(scope.Parent), $"{level}: {scope.Title} is listed before its parent");
                listed.Add(scope.Key);
                sizes.Add(scope.Members.Count + scope.References.Count);
            }

            HashSet<GraphNodeKey> wired = [.. graph.Links.SelectMany(static l => new[] { l.From.Node, l.To.Node })];
            foreach (TaskGraphNode node in graph.TaskNodes.Where(n => wired.Contains(n.Key) && !n.IsGenerated))
            {
                Assert.True(scopes.Any(s => s.Contains(node.Key)), $"{level}: {GraphScopes.Title(node)} is in no graph");
            }


            // Laid out, no two nodes of a graph overlap: every column stacks by the height each node is drawn at.
            GraphLayout layout = new();
            foreach (GraphScope scope in scopes)
            {
                GraphAutoLayout.Place(graph, layout, scope);
                List<(GraphNodeKey Key, double X, double Y, double H)> boxes = [.. graph.Nodes.Keys.Where(scope.Contains)
                    .Select(k => (k, layout.Positions[scope.LayoutKey(k)].X, layout.Positions[scope.LayoutKey(k)].Y, GraphAutoLayout.HeightOf(graph, k, scope)))];
                for (int i = 0; i < boxes.Count; i++)
                {
                    for (int j = i + 1; j < boxes.Count; j++)
                    {
                        (GraphNodeKey a, double ax, double ay, double ah) = boxes[i];
                        (GraphNodeKey b, double bx, double by, double bh) = boxes[j];
                        bool overlap = ax < bx + 220 && bx < ax + 220 && ay < by + bh && by < ay + ah;
                        Assert.False(overlap, $"{level} '{scope.Title}': {a} at ({ax},{ay}) overlaps {b} at ({bx},{by})");
                    }
                }
            }
            GraphScope biggest = scopes.MaxBy(static s => s.Members.Count + s.References.Count)!;
            output.WriteLine($"{level}: {scopes.Count} graphs; biggest '{biggest.Title}' {biggest.Members.Count} + {biggest.References.Count} references");
        }

        sizes.Sort();
        output.WriteLine($"median {sizes[sizes.Count / 2]}, 90th {sizes[sizes.Count * 9 / 10]}, max {sizes[^1]} nodes");
        Assert.True(sizes.Count > 20);
    }
}
