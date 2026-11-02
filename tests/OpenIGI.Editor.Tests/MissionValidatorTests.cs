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
}
