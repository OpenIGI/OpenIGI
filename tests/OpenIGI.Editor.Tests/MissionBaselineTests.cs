using OpenIGI.Editor.Core.Missions;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Qvm;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>Values as the mission was loaded, which changes are marked against and reverted to.</summary>
public sealed class MissionBaselineTests
{
    private const string Sample = """
        Task_DeclareParameters("EditRigidObj", "Position", "ObjectPos", "Orientation", "Real32x9", "Model", "String16");
        Task_New(7, "EditRigidObj", "Crate", 1.0, 2.0, 0, 0.0, 0.0, 0.5, "4_crate");
        """;

    /// <summary>A changed value is marked; putting the original back clears the mark; a new task is never marked.</summary>
    [Fact]
    public void Marks_changes_against_the_loaded_values()
    {
        MissionDocument document = MissionDocument.Read(QscParser.ParseScript(Sample, QvmProgram.IgiVersion));
        MissionTask crate = document.RootTasks.Single();
        MissionBaseline baseline = new();
        baseline.Capture(document);
        Assert.Empty(baseline.ModifiedParameters(crate));

        crate.FindParameter("Position")!.SetNumbers([5.0, 2.0, 0.0]);
        Assert.Equal(["Position"], baseline.ModifiedParameters(crate));

        MissionParameter position = crate.FindParameter("Position")!;
        position.Arguments.Clear();
        position.Arguments.AddRange(baseline.Original(crate, "Position")!);
        Assert.False(baseline.IsModified(crate, "Position"));

        MissionTask made = crate.Clone();
        made.FindParameter("Position")!.SetNumbers([9.0, 9.0, 9.0]);
        Assert.Empty(baseline.ModifiedParameters(made));
        Assert.Null(baseline.Original(made, "Position"));
    }
}
