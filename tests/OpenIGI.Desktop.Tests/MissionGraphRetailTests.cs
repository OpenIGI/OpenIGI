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

/// <summary>Every retail mission as a graph: every wire lands on a node and a pin that exist.</summary>
public sealed class MissionGraphRetailTests(ITestOutputHelper output)
{
    /// <summary>Project I.G.I.'s missions.</summary>
    [RequiresTitleDataFact("igi1")]
    public void Igi1_missions_build_sound_graphs() => AssertGraphs(new Igi1Runtime(), GameDataSource.Igi1, LevelExpressionDialect.Igi1);

    /// <summary>I.G.I. 2's missions.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Igi2_missions_build_sound_graphs() => AssertGraphs(new Igi2Runtime(), GameDataSource.Igi2, LevelExpressionDialect.Igi2);

    private void AssertGraphs(IGameTitleRuntime title, GameDataSource source, LevelExpressionDialect dialect)
    {
        ScriptVocabulary vocabulary = title.CreateScriptVocabulary();
        SortedSet<string> problems = new(StringComparer.Ordinal);
        int missions = 0;
        SortedSet<string> unknownNames = new(StringComparer.Ordinal);
        foreach (string path in source.EnumerateFiles("objects.qvm"))
        {
            MissionDocument document = MissionDocument.Read(File.ReadAllBytes(path));
            MissionGraph graph = MissionGraph.Build(document, TaskCatalogue.Build(document.Declarations.Values, vocabulary), vocabulary, dialect);
            string level = Path.GetFileName(Path.GetDirectoryName(path))!;
            missions++;

            foreach (GraphLink link in graph.Links)
            {
                if (!graph.Nodes.TryGetValue(link.From.Node, out GraphNode? from) || !graph.Nodes.ContainsKey(link.To.Node))
                {
                    problems.Add($"{level}: a wire's end is missing ({link})");
                }
                else if (from is TaskGraphNode task && link.From.Pin != MissionGraph.Self
                    && task.Definition.Outputs.All(o => o.Name != link.From.Pin))
                {
                    problems.Add($"{level}: {task.Task.TypeName}.{link.From.Pin} is read but not exported");
                }
            }

            int tasks = graph.TaskNodes.Count();
            int shown = graph.TaskNodes.Count(static n => n.IsShown);
            int expressions = graph.Nodes.Values.OfType<ExpressionGraphNode>().Count();
            int unresolved = graph.Nodes.Values.OfType<ExpressionGraphNode>().Count(static n => n.Kind is ExpressionNodeKind.UnresolvedField);
            int unknown = graph.Nodes.Values.OfType<ExpressionGraphNode>().Count(static n => n.Kind is ExpressionNodeKind.UnknownName);
            output.WriteLine($"{level}: {tasks} tasks, {shown} shown, {expressions} expression nodes, {graph.Links.Count} wires, {unresolved} unresolved, {unknown} unknown");
            unknownNames.UnionWith(graph.Nodes.Values.OfType<ExpressionGraphNode>()
                .Where(static n => n.Kind is ExpressionNodeKind.UnknownName)
                .Select(n => $"{n.Text} ({((TaskGraphNode)graph.Nodes[n.Owner.Task]).Task.TypeName}.{n.Owner.Parameter})"));
        }

        output.WriteLine("UNKNOWN " + string.Join(" | ", unknownNames));

        Assert.True(missions > 10, $"only {missions} missions");
        Assert.True(problems.Count == 0, $"{problems.Count} problems:\n" + string.Join('\n', problems.Take(60)));
    }
}
