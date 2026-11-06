using OpenIGI.Editor.Core.Graphs;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Igi1;
using OpenIGI.Game.Igi2;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Vocabulary;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// Every level expression every retail mission holds, read into a graph and written back from it, means the same to the game;
/// and a graph nobody edited compiles to no edits at all.
/// </summary>
/// <remarks>
/// "The same" is <see cref="ExpressionCanonical"/>'s documented normalisation: the game's own preparation and parser, then the
/// trees compared with brackets dropped, numbers by value, and field reads by task id and field. Text the game cannot parse
/// must come back verbatim.
/// </remarks>
public sealed class MissionExpressionRoundTripTests
{
    /// <summary>Project I.G.I.'s missions.</summary>
    [RequiresTitleDataFact("igi1")]
    public void Igi1_expressions_survive_the_graph() => AssertRoundTrip(new Igi1Runtime(), GameDataSource.Igi1, LevelExpressionDialect.Igi1);

    /// <summary>I.G.I. 2's missions.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Igi2_expressions_survive_the_graph() => AssertRoundTrip(new Igi2Runtime(), GameDataSource.Igi2, LevelExpressionDialect.Igi2);

    private static void AssertRoundTrip(IGameTitleRuntime title, GameDataSource source, LevelExpressionDialect dialect)
    {
        ScriptVocabulary vocabulary = title.CreateScriptVocabulary();
        List<string> failures = [];
        int checkedCount = 0;
        int rewritten = 0;
        foreach (string path in source.EnumerateFiles("objects.qvm"))
        {
            MissionDocument document = MissionDocument.Read(File.ReadAllBytes(path));
            MissionGraph graph = MissionGraph.Build(document, TaskCatalogue.Build(document.Declarations.Values, vocabulary), vocabulary, dialect);
            string level = Path.GetFileName(Path.GetDirectoryName(path))!;
            foreach (((GraphNodeKey task, string parameter), GraphPinRef _) in graph.ExpressionRoots)
            {
                TaskGraphNode node = (TaskGraphNode)graph.Nodes[task];
                string original = ((QscStringLiteral)node.Task.FindParameter(parameter)!.Arguments[0]).Value;
                string written = ExpressionGraphWriter.Write(graph, task, parameter);
                string owner = $"{node.Task.TypeName}_{node.Task.Id}";
                checkedCount++;
                rewritten += original == written ? 0 : 1;
                if (!ExpressionCanonical.Same(original, written, owner, dialect))
                {
                    failures.Add($"{level} {owner}.{parameter}:\n    was   {Flat(original)}\n    wrote {Flat(written)}");
                }
            }

            // Nothing was edited, so nothing compiles: the mission saves as level data wrote it.
            IReadOnlyList<ExpressionChange> changes = MissionGraphCompiler.Changes(graph, dialect);
            failures.AddRange(changes.Select(c => $"{level}: unedited graph wants to rewrite {c.Task}.{c.Parameter}"));
        }

        Assert.True(checkedCount > 1000, $"only {checkedCount} expressions");
        Assert.True(rewritten > 100, $"only {rewritten} expressions were spelled differently, so the comparison proves little");
        Assert.True(failures.Count == 0, $"{failures.Count} of {checkedCount} changed:\n" + string.Join('\n', failures.Take(30)));
    }

    private static string Flat(string text) => text.Replace("\n", "\\n", StringComparison.Ordinal);
}
