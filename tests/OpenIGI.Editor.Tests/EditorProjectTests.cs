using OpenIGI.Editor.Core.Projects;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>The project folder: created with a manifest, files written where the game's paths say, never outside.</summary>
public sealed class EditorProjectTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("openigi-project").FullName;

    /// <summary>A new project writes its manifest, and opening it again reads the same name and game.</summary>
    [Fact]
    public void A_project_is_created_and_reopened()
    {
        string folder = Path.Combine(_root, "p");
        EditorProject created = EditorProject.OpenOrCreate(folder, "igi1", "Mine");
        Assert.True(File.Exists(Path.Combine(folder, EditorProject.ManifestName)));
        Assert.True(Directory.Exists(created.FilesDirectory));

        EditorProject reopened = EditorProject.OpenOrCreate(folder, "igi1", "Ignored");
        Assert.Equal("Mine", reopened.Name);
        Assert.Equal("editor-p", reopened.LayerId);

        Assert.Throws<InvalidDataException>(() => EditorProject.OpenOrCreate(folder, "igi2", "Other"));
    }

    /// <summary>A save lands at the game path inside files, replaces what was there, and reverts cleanly.</summary>
    [Fact]
    public void Files_are_written_at_game_paths_and_reverted()
    {
        EditorProject project = EditorProject.OpenOrCreate(Path.Combine(_root, "p"), "igi1", "Mine");
        const string objects = "missions/location0/level1/objects.qvm";

        project.Write(objects, [1, 2, 3]);
        project.Write(objects, [4]);
        Assert.Equal(new byte[] { 4 }, File.ReadAllBytes(project.PathFor(objects)));
        Assert.True(project.Has(objects));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(project.PathFor(objects))!, "*.saving"));

        Assert.True(project.Revert(objects));
        Assert.False(project.Has(objects));
        Assert.False(project.Revert(objects));
    }

    /// <summary>A path that climbs out of the project is refused.</summary>
    [Fact]
    public void Paths_cannot_escape_the_project()
    {
        EditorProject project = EditorProject.OpenOrCreate(Path.Combine(_root, "p"), "igi1", "Mine");
        Assert.Throws<ArgumentException>(() => project.PathFor("../../outside.qvm"));
    }

    /// <summary>A mission's target is kept in the manifest across reopening; a mission never set is retail.</summary>
    [Fact]
    public void Mission_targets_are_kept()
    {
        EditorProject project = EditorProject.OpenOrCreate(_root, "igi1", "Targets");
        Assert.Equal(MissionTarget.Retail, project.TargetOf(1));
        project.SetTarget(15, MissionTarget.OpenIgi);
        project.SetTarget(16, MissionTarget.Retail);

        EditorProject reopened = EditorProject.OpenOrCreate(_root, "igi1", "Targets");
        Assert.Equal(MissionTarget.OpenIgi, reopened.TargetOf(15));
        Assert.Equal(MissionTarget.Retail, reopened.TargetOf(16));
        Assert.Equal(MissionTarget.Retail, reopened.TargetOf(3));
        Assert.Equal("Targets", reopened.Name);
    }

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
