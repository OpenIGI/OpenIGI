using OpenIGI.Editor.Core.Missions;
using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Qvm;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>The mission document: lossless on retail data, and edits land where the engine reads them.</summary>
public sealed class MissionDocumentTests
{
    private const string Sample = """
        Task_DeclareParameters("EditRigidObj", "Position", "ObjectPos", "Orientation", "Real32x9", "Model", "String16");
        Task_DeclareParameters("AnimTask", "Run", "VarString", "AnimData", "AnimData");
        Task_New(-1, "Container", "Root",
        	Task_New(7, "EditRigidObj", "Crate", 1.0, 2.0, 0, 0.0, 0.0, 0.5, "4_crate"),
        	Task_New(-1, "AnimTask", "Door", "go", 2, 1, 0.5, 32, 3.0));
        Task_New(-1, "Unknown", "Kept", 1, 2, 3);
        """;

    /// <summary>Declarations, ids, parameters, AnimData and children are split out; undeclared types stay opaque.</summary>
    [Fact]
    public void Reads_the_object_tree()
    {
        MissionDocument document = Read(Sample);

        Assert.Equal(2, document.Declarations.Count);
        MissionTask[] tasks = [.. document.EnumerateTasks()];
        Assert.Equal(["Root", "Crate", "Door", "Kept"], tasks.Select(static t => t.Label));

        // Container is never declared, yet its children still form the tree; Unknown's values are kept verbatim.
        Assert.Equal(2, tasks[0].Children.Count);
        Assert.Empty(tasks[0].UnnamedArguments!);
        Assert.Equal(3, tasks[3].UnnamedArguments!.Count);

        MissionTask crate = tasks[1];
        Assert.Equal(7, crate.Id);
        Assert.True(crate.FindParameter(ScriptParamType.ObjectPos)!.TryGetNumbers(out double[] position));
        Assert.Equal([1.0, 2.0, 0.0], position);
        Assert.Equal(5, Assert.Single(tasks[2].Parameters, static p => p.Name == "AnimData").Arguments.Count);
    }

    /// <summary>Moving an object rewrites only the coordinates that changed, in the retail literal shapes.</summary>
    [Fact]
    public void Editing_a_position_keeps_untouched_spellings()
    {
        MissionDocument document = Read(Sample);
        MissionTask crate = document.EnumerateTasks().Single(static t => t.Label == "Crate");

        crate.FindParameter("Position")!.SetNumbers([1.0, -3.5, 0.0]);

        string saved = QscPrinter.Print(document.ToScript());
        Assert.Contains("Task_New(7, \"EditRigidObj\", \"Crate\", 1.0, -3.5, 0, ", saved, StringComparison.Ordinal);
        Assert.Equal(saved, QscPrinter.Print(MissionDocument.Read(document.ToImage()).ToScript()));
    }

    /// <summary>A new task gets its declared parameters and is written where it was inserted.</summary>
    [Fact]
    public void A_new_task_takes_its_declaration()
    {
        MissionDocument document = Read(Sample);
        MissionTask door = document.EnumerateTasks().Single(static t => t.Label == "Door");
        MissionTask root = door.Parent!;

        MissionTask added = new("EditRigidObj", "Barrel", id: 12);
        Assert.True(document.ApplyDeclaration(added));
        added.FindParameter("Model")!.Arguments[0] = new QscStringLiteral("4_barrel");
        root.AddChild(added, root.RemoveChild(door));

        MissionDocument reread = MissionDocument.Read(document.ToImage());
        MissionTask barrel = reread.EnumerateTasks().Single(static t => t.Label == "Barrel");
        Assert.Equal(12, barrel.Id);
        Assert.Equal(3, barrel.FindParameter("Position")!.Arguments.Count);
        Assert.DoesNotContain(reread.EnumerateTasks(), static t => t.Label == "Door");
    }

    /// <summary>Every retail I.G.I. script reads into a document and saves back byte for byte.</summary>
    [RequiresTitleDataFact("igi1")]
    public void Every_igi1_script_saves_unchanged() => AssertLossless(GameDataSource.Igi1);

    /// <summary>Every retail I.G.I. 2 script reads into a document and saves back byte for byte.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Every_igi2_script_saves_unchanged() => AssertLossless(GameDataSource.Igi2);

    private static MissionDocument Read(string source) =>
        MissionDocument.Read(QscParser.ParseScript(source, QvmProgram.IgiVersion));

    private static void AssertLossless(GameDataSource source)
    {
        List<string> failures = [];
        int objectScripts = 0;
        foreach (string path in source.EnumerateFiles("*.qvm"))
        {
            if (Path.GetFileName(path).Equals("openigi-config.qvm", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            byte[] original = File.ReadAllBytes(path);
            MissionDocument document = MissionDocument.Read(original);
            if (!original.AsSpan().SequenceEqual(document.ToImage()))
            {
                failures.Add(path);
            }

            if (Path.GetFileName(path).Equals("objects.qvm", StringComparison.OrdinalIgnoreCase))
            {
                objectScripts++;
                Assert.NotEmpty(document.RootTasks);
            }
        }

        Assert.True(objectScripts > 0, "No objects.qvm was found.");
        Assert.True(failures.Count == 0, $"{failures.Count} scripts changed:\n{string.Join('\n', failures.Take(20))}");
    }
}
