using OpenIGI.Editor.Core.Missions;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Qvm;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>Objects copied out of a mission as script text and pasted back in.</summary>
public sealed class MissionClipboardTests
{
    private const string Sample = """
        Task_DeclareParameters("EditRigidObj", "Position", "ObjectPos", "Orientation", "Real32x9", "Model", "String16");
        Task_New(-1, "Container", "Root",
        	Task_New(7, "EditRigidObj", "Crate", 1.0, 2.0, 0, 0.0, 0.0, 0.5, "4_crate"));
        """;

    /// <summary>A pasted copy is the original's script with the id made anonymous, children included.</summary>
    [Fact]
    public void Copy_then_paste_gives_a_clone()
    {
        MissionDocument document = Read(Sample);
        MissionTask root = document.RootTasks.Single();

        string text = document.WriteTasks([root]);
        Assert.Contains("Task_DeclareParameters(\"EditRigidObj\"", text, StringComparison.Ordinal);

        PastedTasks pasted = document.ReadTasks(text);
        Assert.Empty(pasted.Skipped);
        MissionTask copy = Assert.Single(pasted.Tasks);
        Assert.Null(copy.Parent);
        Assert.Equal(-1, copy.Children.Single().Id);
        Assert.Equal(Print(document, root.Clone()), Print(document, copy));
    }

    /// <summary>A type the receiving mission does not declare is left out and named, not declared for it.</summary>
    [Fact]
    public void Undeclared_types_are_skipped()
    {
        MissionDocument source = Read(Sample);
        MissionDocument target = Read("""Task_New(-1, "Container", "Other");""");

        PastedTasks pasted = target.ReadTasks(source.WriteTasks([source.EnumerateTasks().Single(static t => t.Label == "Crate")]));

        Assert.Empty(pasted.Tasks);
        Assert.Equal(["EditRigidObj"], pasted.Skipped);
        Assert.Empty(target.Declarations);
    }

    /// <summary>
    /// Logic pasted as new gets free ids, and what the copies say about each other follows them: a condition naming a copied
    /// trigger, a reference parameter. What they say about the rest of the mission stays as it was.
    /// </summary>
    [Fact]
    public void Logic_pasted_as_new_reads_its_own_copies()
    {
        MissionDocument document = Read("""
            Task_DeclareParameters("AreaActivate", "Criteria", "VarString");
            Task_DeclareParameters("StatusMessage", "Send", "VarString", "Text", "VarString");
            Task_DeclareParameters("DefineComputerObjective", "Objective 1 Link To Position", "Int32");
            Task_New(-1, "Container", "Root",
            	Task_New(1, "AreaActivate", "Gate", "CRITERIA_HUMAN0"),
            	Task_New(2, "StatusMessage", "Hello", "AreaActivate_1.nActive && AreaActivate_10.nActive", "M1_MSG_1"),
            	Task_New(3, "DefineComputerObjective", "Go", 1),
            	Task_New(10, "AreaActivate", "Elsewhere", "CRITERIA_HUMAN0"));
            """);
        MissionTask[] copied = [.. document.EnumerateTasks().Where(static t => t.Label is "Gate" or "Hello" or "Go")];

        PastedTasks pasted = document.ReadTasksAsNew(document.WriteTasks(copied), out IReadOnlyDictionary<int, int> original);

        Assert.Equal(3, pasted.Tasks.Count);
        MissionTask gate = pasted.Tasks.Single(static t => t.Label == "Gate");
        MissionTask hello = pasted.Tasks.Single(static t => t.Label == "Hello");
        MissionTask go = pasted.Tasks.Single(static t => t.Label == "Go");
        Assert.DoesNotContain(gate.Id, (int[])[-1, 1, 2, 3, 10]);
        Assert.Equal(1, original[gate.Id]);
        Assert.Equal($"\"AreaActivate_{gate.Id}.nActive && AreaActivate_10.nActive\"", QscPrinter.Print(hello.FindParameter("Send")!.Arguments[0]));
        Assert.True(go.FindParameter("Objective 1 Link To Position")!.TryGetNumbers(out double[] link));
        Assert.Equal(gate.Id, (int)link[0]);
    }

    /// <summary>Text that is not script is reported as such.</summary>
    [Fact]
    public void Plain_text_is_not_objects() =>
        Assert.Throws<QscParseException>(() => Read(Sample).ReadTasks("hello there ("));

    private static string Print(MissionDocument document, MissionTask task) => document.WriteTasks([task]);

    private static MissionDocument Read(string source) => MissionDocument.Read(QscParser.ParseScript(source, QvmProgram.IgiVersion));
}
