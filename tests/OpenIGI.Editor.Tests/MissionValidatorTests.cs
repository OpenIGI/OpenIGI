using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Editor.Core.Projects;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Qvm;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>The retail game's silent rules, caught in the editor; and the export a retail game is given.</summary>
public sealed class MissionValidatorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("openigi-export").FullName;

    /// <summary>A duplicate id and an id past the table are errors; a clean mission has none.</summary>
    [Fact]
    public void Ids_the_game_would_drop_are_errors()
    {
        MissionDocument document = MissionDocument.Read(QscParser.ParseScript("""
            Task_New(-1, "Container", "Root",
                Task_New(7, "A", "first"),
                Task_New(7, "A", "second"),
                Task_New(5000, "A", "far"),
                Task_New(8, "A", "fine"));
            """, QvmProgram.IgiVersion));

        IReadOnlyList<MissionIssue> issues = MissionValidator.Validate(document);
        Assert.Equal(["far", "second"], issues.Select(static i => i.Task!.Label).Order());
        Assert.All(issues, static i => Assert.Equal(IssueSeverity.Error, i.Severity));
    }

    /// <summary>"Fix all" gives every clashing task its own free id, as one undoable step.</summary>
    [Fact]
    public void Id_fixes_take_distinct_free_ids()
    {
        MissionDocument document = MissionDocument.Read(QscParser.ParseScript("""
            Task_New(-1, "Container", "Root",
                Task_New(1, "A", "first"),
                Task_New(1, "A", "second"),
                Task_New(1, "A", "third"),
                Task_New(2, "A", "two"));
            """, QvmProgram.IgiVersion));

        IEditorCommand[] steps = [.. MissionValidator.Validate(document).Select(static i => i.Fixes[0].Make()!)];
        CompositeCommand all = new("Fix", steps);
        all.Apply();
        Assert.Empty(MissionValidator.Validate(document));
        Assert.Equal([1, 2, 3, 4], document.EnumerateTasks().Select(static t => t.Id).Where(static id => id >= 0).Order());
        all.Undo();
        Assert.Equal(2, MissionValidator.Validate(document).Count);
    }

    /// <summary>A condition testing a task the mission does not have, and a target naming none, are found.</summary>
    [Fact]
    public void Missing_references_are_found()
    {
        MissionDocument document = MissionDocument.Read(QscParser.ParseScript("""
            Task_DeclareParameters("ConditionalContainer", "Condition", "VarString");
            Task_DeclareParameters("Pointer", "TargetID", "Int32");
            Task_New(-1, "Container", "Root",
                Task_New(10, "Terminal", "t"),
                Task_New(11, "ConditionalContainer", "ok", "Terminal_10.isHacked"),
                Task_New(12, "ConditionalContainer", "bad", "Terminal_10.isHacked && Terminal_99.isHacked"),
                Task_New(13, "Pointer", "aims", 10),
                Task_New(14, "Pointer", "lost", 77));
            """, QvmProgram.IgiVersion));

        IReadOnlyList<MissionIssue> issues = MissionValidator.Validate(document);
        Assert.True(issues.Count == 2, string.Join(" / ", issues.Select(static i => i.Task!.Label + ": " + i.Message)));
        Assert.Equal(["bad", "lost"], issues.Select(static i => i.Task!.Label).Order());
        Assert.Contains("Terminal_99", issues.Single(static i => i.Task!.Label == "bad").Message, StringComparison.Ordinal);
    }

    /// <summary>A soldier well off the ground is found and dropped onto it; one standing on it is not; a retail mission with mountains is an error.</summary>
    [Fact]
    public void Live_checks_use_the_levels_facts()
    {
        MissionDocument document = MissionDocument.Read(QscParser.ParseScript("""
            Task_DeclareParameters("HumanSoldier", "Position", "ObjectPos");
            Task_New(-1, "Container", "Root",
                Task_New(1, "HumanSoldier", "floating", 0, 0, 81920),
                Task_New(2, "HumanSoldier", "standing", 4096, 0, 40960));
            """, QvmProgram.IgiVersion));
        Facts facts = new(groundMetres: 10.0);

        IReadOnlyList<MissionIssue> issues = MissionValidator.Validate(document, facts);
        MissionIssue floating = Assert.Single(issues, static i => i.Task?.Label == "floating");
        Assert.DoesNotContain(issues, static i => i.Task?.Label == "standing");

        floating.Fixes[0].Make()!.Apply();
        Assert.Empty(MissionValidator.Validate(document, facts));
    }

    /// <summary>No retail mission has an error by these rules.</summary>
    [RequiresTitleDataFact("igi1")]
    public void Retail_missions_are_clean()
    {
        foreach (string path in GameDataSource.Igi1.EnumerateFiles("objects.qvm"))
        {
            MissionDocument document = MissionDocument.Read(File.ReadAllBytes(path));
            Assert.DoesNotContain(MissionValidator.Validate(document), static i => i.Severity == IssueSeverity.Error);
        }
    }

    /// <summary>More of a kind than the game's list for it holds is an error; nearly full is a warning; with room, nothing.</summary>
    [Fact]
    public void A_full_task_list_is_an_error()
    {
        string doors = string.Join(",\n", Enumerable.Range(1, 5).Select(static i => $"Task_New({i}, \"Door\", \"d{i}\")"));
        MissionDocument document = MissionDocument.Read(QscParser.ParseScript($"Task_New(-1, \"Container\", \"Root\",\n{doors});", QvmProgram.IgiVersion));

        MissionIssue over = Assert.Single(MissionValidator.Validate(document, new Facts(0.0) { Lists = [new("doors", 6, ["Door"]) { Reserve = 2 }] }));
        Assert.Equal(IssueSeverity.Error, over.Severity);
        Assert.Contains("5 doors", over.Message, StringComparison.Ordinal);

        MissionIssue near = Assert.Single(MissionValidator.Validate(document, new Facts(0.0) { Lists = [new("doors", 5, ["Door"])] }));
        Assert.Equal(IssueSeverity.Warning, near.Severity);
        Assert.Empty(MissionValidator.Validate(document, new Facts(0.0) { Lists = [new("doors", 64, ["Door"])] }));
    }

    /// <summary>A level with no player, and a guard generator that is not one soldier, are found.</summary>
    [Fact]
    public void A_level_the_game_cannot_read_is_found()
    {
        MissionDocument document = MissionDocument.Read(QscParser.ParseScript("""
            Task_DeclareParameters("HumanPlayer", "Position", "ObjectPos");
            Task_New(-1, "Container", "Root",
                Task_New(1, "GuardGenerator", "empty"),
                Task_New(2, "GuardGenerator", "path first",
                    Task_New(3, "PatrolPath", "p"),
                    Task_New(4, "HumanSoldier", "s")),
                Task_New(5, "GuardGenerator", "fine",
                    Task_New(6, "HumanSoldier", "s")));
            """, QvmProgram.IgiVersion));

        IReadOnlyList<MissionIssue> issues = MissionValidator.Validate(document);
        Assert.Contains(issues, static i => i.Task is null && i.Severity == IssueSeverity.Error && i.Message.Contains("no player", StringComparison.Ordinal));
        Assert.Equal(["empty", "path first"], issues.Where(static i => i is { Severity: IssueSeverity.Error, Task: not null }).Select(static i => i.Task!.Label).Order());
        Assert.DoesNotContain(issues, static i => i.Task?.Label == "fine");
    }

    /// <summary>A soldier's mind with no script of its id in the mission's folder is found; one with its script is not.</summary>
    [Fact]
    public void A_mind_without_its_script_is_found()
    {
        MissionDocument document = MissionDocument.Read(QscParser.ParseScript("""
            Task_New(-1, "Container", "Root",
                Task_New(10, "HumanSoldier", "a", Task_New(11, "HumanAI", "scripted")),
                Task_New(12, "HumanSoldier", "b", Task_New(13, "HumanAI", "bare")));
            """, QvmProgram.IgiVersion));

        MissionIssue issue = Assert.Single(MissionValidator.Validate(document, new Facts(0.0) { Files = ["ai/11.qvm"] }));
        Assert.Equal("bare", issue.Task!.Label);
        Assert.Contains("ai/13.qvm", issue.Message, StringComparison.Ordinal);
    }

    /// <summary>Every retail level fits the lists the title names, with the reserve kept: the table counts what the game counts.</summary>
    [RequiresTitleDataFact("igi1")]
    public void Retail_missions_fit_the_games_lists()
    {
        OpenIGI.Scripting.Vocabulary.ScriptVocabulary vocabulary = new OpenIGI.Game.Igi1.Igi1Runtime().CreateScriptVocabulary();
        Assert.NotEmpty(vocabulary.TaskLists);
        foreach (string path in GameDataSource.Igi1.EnumerateFiles("objects.qvm"))
        {
            MissionDocument document = MissionDocument.Read(File.ReadAllBytes(path));
            foreach ((OpenIGI.Scripting.Vocabulary.TaskListLimit list, int used) in MissionValidator.ListUsage(document, vocabulary))
            {
                Assert.True(used <= list.Room, $"{path}: {used} {list.Name}, room for {list.Room}");
            }
        }
    }

    /// <summary>An export mirrors the project's files and replaces an earlier one.</summary>
    [Fact]
    public void An_export_mirrors_the_project()
    {
        EditorProject project = EditorProject.OpenOrCreate(Path.Combine(_root, "p"), "igi1", "Mine");
        project.Write("missions/location0/level1/objects.qvm", [1, 2]);
        string target = Path.Combine(_root, "out");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "stale.txt"), "old");

        Assert.Equal(["missions/location0/level1/objects.qvm"], project.ExportTo(target));
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(Path.Combine(target, "missions", "location0", "level1", "objects.qvm")));
        Assert.False(File.Exists(Path.Combine(target, "stale.txt")));
        Assert.Throws<ArgumentException>(() => project.ExportTo(Path.Combine(project.Root, "inside")));
    }

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class Facts(double groundMetres) : IMissionFacts
    {
        public MissionTarget Target { get; private set; } = MissionTarget.Retail;

        public bool? HasModel(string model) => true;

        public double? GroundUnder(Vector3d pointMetres) => groundMetres;

        public IReadOnlyList<OpenIGI.Scripting.Vocabulary.TaskListLimit> Lists { get; init; } = [];

        public IReadOnlyList<string>? Files { get; init; }

        public bool? HasMissionFile(string path) => Files?.Contains(path);

        public OpenIGI.Scripting.Vocabulary.ScriptVocabulary? Vocabulary => Lists.Count == 0 ? null : new OpenIGI.Scripting.Vocabulary.ScriptVocabulary().Add(Lists);

        public IEditorCommand? MakeOpenIgiOnly() => new DelegateCommand("OpenIGI", () => Target = MissionTarget.OpenIgi, () => Target = MissionTarget.Retail);
    }
}
