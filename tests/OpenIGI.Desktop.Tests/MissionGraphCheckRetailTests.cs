using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Editor.Core.Projects;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Igi1;
using OpenIGI.Game.Igi2;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Vocabulary;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Desktop.Tests;

/// <summary>The graph checks over every retail mission with the title's vocabulary: warnings only, each a known retail mistake.</summary>
public sealed class MissionGraphCheckRetailTests(ITestOutputHelper output)
{
    /// <summary>Project I.G.I.'s missions.</summary>
    [RequiresTitleDataFact("igi1")]
    public void Igi1_retail_has_no_graph_errors() => AssertClean(new Igi1Runtime(), GameDataSource.Igi1, LevelExpressionDialect.Igi1, references: 9, logic: 1);

    /// <summary>I.G.I. 2's missions.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Igi2_retail_has_no_graph_errors() => AssertClean(new Igi2Runtime(), GameDataSource.Igi2, LevelExpressionDialect.Igi2, references: 12, logic: 5);

    private void AssertClean(IGameTitleRuntime title, GameDataSource source, LevelExpressionDialect dialect, int references, int logic)
    {
        VocabularyFacts facts = new(title.CreateScriptVocabulary(), dialect);
        Dictionary<string, List<string>> byCheck = new(StringComparer.Ordinal);
        foreach (string path in source.EnumerateFiles("objects.qvm"))
        {
            MissionDocument document = MissionDocument.Read(File.ReadAllBytes(path));
            foreach (MissionIssue issue in MissionValidator.Validate(document, facts).Where(static i => i.Check is "References" or "Logic"))
            {
                Assert.Equal(IssueSeverity.Warning, issue.Severity);
                if (!byCheck.TryGetValue(issue.Check, out List<string>? list))
                {
                    byCheck[issue.Check] = list = [];
                }

                list.Add($"{Path.GetFileName(Path.GetDirectoryName(path))} {issue.Task}: {issue.Message}");
            }
        }

        foreach ((string check, List<string> list) in byCheck)
        {
            output.WriteLine($"{check}: {list.Count}");
            foreach (string line in list.Take(40))
            {
                output.WriteLine("  " + line);
            }
        }

        // The retail mistakes, counted: unreadable text, a stray animation name, a call made on a field, and reads and
        // references of tasks a level does not have. A new warning here is a regression in the graph or the vocabulary.
        Assert.Equal(references, byCheck.GetValueOrDefault("References")?.Count ?? 0);
        Assert.Equal(logic, byCheck.GetValueOrDefault("Logic")?.Count ?? 0);
    }

    private sealed class VocabularyFacts(ScriptVocabulary vocabulary, LevelExpressionDialect dialect) : IMissionFacts
    {
        public MissionTarget Target => MissionTarget.Retail;

        public int MountainAreas => 0;

        public ScriptVocabulary? Vocabulary => vocabulary;

        public LevelExpressionDialect Dialect => dialect;

        public bool? HasModel(string model) => null;

        public double? GroundUnder(Vector3d pointMetres) => null;

        public IEditorCommand? MakeOpenIgiOnly() => null;
    }
}
