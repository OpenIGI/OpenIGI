using OpenIGI.Editor.Core.Missions;
using OpenIGI.Scripting.Qsc;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Desktop.Tests;

/// <summary>A blank mission made from every retail level of Project I.G.I.: the place and the player kept, an exit to end it, nothing else.</summary>
public sealed class BlankMissionRetailTests(ITestOutputHelper output)
{
    /// <summary>Each level's blank compiles, keeps its ground, sky and player, and ends at an exit the objective names.</summary>
    [RequiresTitleDataFact("igi1")]
    public void Every_level_makes_a_blank_that_starts_and_ends()
    {
        int levels = 0;
        foreach (string path in GameDataSource.Igi1.EnumerateFiles("objects.qvm").Where(static p => p.Replace(Path.DirectorySeparatorChar, '/').Contains("/location", StringComparison.OrdinalIgnoreCase)))
        {
            MissionDocument original = MissionDocument.Read(File.ReadAllBytes(path));
            MissionDocument blank = BlankMission.Make(File.ReadAllBytes(path));
            MissionDocument read = MissionDocument.Read(blank.ToImage());
            string level = Path.GetFileName(Path.GetDirectoryName(path))!;
            levels++;

            MissionTask[] tasks = [.. read.EnumerateTasks()];
            MissionTask player = Assert.Single(tasks, static t => t.TypeName == "HumanPlayer");
            Assert.Equal(original.EnumerateTasks().First(static t => t.TypeName == "HumanPlayer").Id, player.Id);
            Assert.Contains(player.Children, static c => c.TypeName == "HumanPlayerInput");
            Assert.Contains(tasks, static t => t.Label == "EnvironmentalEffects");
            Assert.Equal(
                original.EnumerateTasks().Count(static t => t.TypeName is "HeightMap" or "TerrainLightMap" or "TextureModifier"),
                tasks.Count(static t => t.TypeName is "HeightMap" or "TerrainLightMap" or "TextureModifier"));
            Assert.DoesNotContain(tasks, static t => t.TypeName is "HumanSoldier" or "CutScene" or "Building" or "Door");
            Assert.Equal(2, tasks.Count(static t => t.TypeName == "StatusMessage"));

            // The ending reads tasks the blank has, as the shipped levels end: reaching the exit or dying is announced, and
            // the level ends once the line has shown.
            MissionTask exit = Assert.Single(tasks, static t => t.TypeName == "AreaActivate");
            MissionTask flow = Assert.Single(tasks, static t => t.TypeName == "LevelFlow");
            MissionTask complete = Assert.Single(tasks, static t => t.TypeName == "StatusMessage" && Printed(t, "Text") == $"\"{BlankMission.CompleteText}\"");
            MissionTask failed = Assert.Single(tasks, static t => t.TypeName == "StatusMessage" && Printed(t, "Text") == $"\"{BlankMission.FailedText}\"");
            Assert.Equal($"\"AreaActivate_{exit.Id}.nActive\"", Printed(complete, "Send"));
            Assert.Equal($"\"HumanPlayer_{player.Id}.isDead\"", Printed(failed, "Send"));
            Assert.Equal("\"fail\"", Printed(failed, "Sound"));
            Assert.Equal($"\"StatusMessage_{complete.Id}.nTicksSinceFinishedDisplay > GAME_FREQUENCY\"", Printed(flow, "Complete"));
            Assert.Equal($"\"StatusMessage_{failed.Id}.nTicksSinceFinishedDisplay > GAME_FREQUENCY\"", Printed(flow, "Failed"));
            Assert.Equal(tasks.Length, tasks.Select(static t => t.Id).Where(static id => id >= 0).Distinct().Count() + tasks.Count(static t => t.Id < 0));

            // The exit is straight ahead: the original walks a player of Gamma g along (-sin g, cos g), measured in retail.
            Assert.True(player.FindParameter("Position")!.TryGetNumbers(out double[] start));
            Assert.True(player.FindParameter("Gamma")!.TryGetNumbers(out double[] gamma));
            Assert.True(exit.FindParameter("Position")!.TryGetNumbers(out double[] at));
            double dx = at[0] - start[0], dy = at[1] - start[1];
            Assert.InRange(Math.Sqrt((dx * dx) + (dy * dy)) / 4096.0, BlankMission.ExitDistanceMetres - 0.01, BlankMission.ExitDistanceMetres + 0.01);
            Assert.InRange(((-Math.Sin(gamma[0]) * dx) + (Math.Cos(gamma[0]) * dy)) / Math.Sqrt((dx * dx) + (dy * dy)), 0.999, 1.001);

            MissionTask objective = Assert.Single(tasks, static t => t.TypeName == "DefineComputerObjective");
            Assert.Equal($"\"{BlankMission.ObjectiveText}\"", QscPrinter.Print(objective.FindParameter("Objective 1 Text Resource")!.Arguments[0]));
            output.WriteLine($"{level}: {original.EnumerateTasks().Count()} tasks -> {tasks.Length}");
        }

        Assert.True(levels >= 14, $"only {levels} levels");
    }

    private static string Printed(MissionTask task, string parameter) => QscPrinter.Print(task.FindParameter(parameter)!.Arguments[0]);
}
