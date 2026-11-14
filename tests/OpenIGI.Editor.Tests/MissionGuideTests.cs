using OpenIGI.Editor.Core.Missions;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Qvm;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>The guide's steps are judged from the mission: each ticks when the mission shows it done, however it was done.</summary>
public sealed class MissionGuideTests
{
    private const string Declarations = """
        Task_DeclareParameters("HumanSoldier", "Position", "ObjectPos");
        Task_DeclareParameters("PatrolPath", "Waypoints", "VarString");
        Task_DeclareParameters("DefineComputerObjective", "Objective 1 Complete Expression", "VarString");
        Task_DeclareParameters("LevelFlow", "Complete", "VarString", "Failed", "VarString");
        """;

    /// <summary>An empty mission has nothing done; a guard, an objective and an ending tick theirs; a play ticks the last.</summary>
    [Fact]
    public void Steps_tick_as_the_mission_shows_them_done()
    {
        MissionDocument empty = Read("""
            Task_New(-1, "Container", "Root",
                Task_New(1, "DefineComputerObjective", "objectives", ""),
                Task_New(2, "LevelFlow", "flow", "0", ""));
            """);
        Assert.Equal(0, MissionGuide.Done(MissionGuide.Igi1, new GuideFacts(empty, HasPlayed: false)));

        MissionDocument made = Read("""
            Task_New(-1, "Container", "Root",
                Task_New(10, "HumanSoldier", "guard", 0, 0, 0),
                Task_New(1, "DefineComputerObjective", "objectives", "HumanSoldier_10.isDead"),
                Task_New(2, "LevelFlow", "flow", "DefineComputerObjective_1.isObjective1Complete", ""));
            """);
        bool[] done = [.. MissionGuide.Igi1.Select(s => s.IsDone(new GuideFacts(made, HasPlayed: true)))];
        Assert.Equal([true, false, true, true, true], done);
    }

    private static MissionDocument Read(string tasks) => MissionDocument.Read(QscParser.ParseScript(Declarations + tasks, QvmProgram.IgiVersion));
}
