using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Every level's graphs, read as the first game reads a graph file, stand where this game placed them.</summary>
public sealed class RetailIgi2GraphFileTests(ITestOutputHelper output)
{
    [RequiresTitleDataFact("igi2")]
    public void Every_levels_graphs_read_as_the_first_games_have_every_node_where_this_game_placed_it()
    {
        int levels = 0;
        int nodes = 0;
        foreach (string script in RetailIgi2LevelLoadTests.LevelScripts())
        {
            string directory = Path.GetDirectoryName(script)!;
            RetailIgi2LevelLoadTests.Harness harness = new(script, loadTables: true);
            harness.Loader.LoadScript(RetailIgi2LevelLoadTests.Harness.ScriptName);
            VirtualFileSystem files = new();
            files.RegisterDevice(new LocalFileDevice(GameDataSource.Igi2.RequireRoot()));
            string relative = Path.GetRelativePath(GameDataSource.Igi2.RequireRoot(), directory).Replace('\\', '/');
            files.SetAlias(VirtualFileSystem.MissionAlias, $"LOCAL:{relative}/");
            ResourceRegistry resources = new(files);
            Igi2AiGraphs graphs = Igi2AiGraphs.Build(
                harness.LevelRoot, resources, harness.Loop.Natives, harness.Session.Services.Require<Igi2ScriptFunctions>());

            IReadOnlyDictionary<int, GraphFile> read = graphs.ToGraphFiles(resources);

            Assert.Equal(graphs.Graphs.Keys.Order(), read.Keys.Order());
            foreach ((int id, GraphFile file) in read)
            {
                Igi2AiGraph graph = graphs.Graph(id)!;
                Assert.Empty(file.DiscardedNodeIds);
                Assert.Equal(graph.Nodes.Count, file.Nodes.Count);
                foreach (GraphNode node in file.Nodes)
                {
                    Vector3d placed = graph.Node(node.Id)!.Position;
                    Assert.Equal(placed, node.Position);
                }

                nodes += file.Nodes.Count;
            }

            levels++;
        }

        output.WriteLine($"{levels} level(s), {nodes} node(s).");
        Assert.True(levels > 0);
    }
}
