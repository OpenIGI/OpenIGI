using OpenIGI.Editor.Core;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Qvm;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>Undo, redo, transactions, gesture merging, the dirty flag, selection, and the mission commands on top.</summary>
public sealed class EditHistoryTests
{
    private const string Sample = """
        Task_DeclareParameters("EditRigidObj", "Position", "ObjectPos", "Model", "String16");
        Task_New(-1, "Container", "Root",
        	Task_New(1, "EditRigidObj", "A", 0, 0, 0, "m"),
        	Task_New(2, "EditRigidObj", "B", 5.0, 0, 0, "m"));
        """;

    /// <summary>Undo and redo walk the history; a new command after an undo drops the redo branch.</summary>
    [Fact]
    public void Undo_and_redo()
    {
        int value = 0;
        EditHistory history = new();
        history.Execute(new DelegateCommand("+1", () => value++, () => value--));
        history.Execute(new DelegateCommand("+10", () => value += 10, () => value -= 10));
        Assert.Equal(11, value);

        Assert.True(history.Undo());
        Assert.Equal(1, value);
        Assert.Equal("+10", history.RedoName);

        history.Execute(new DelegateCommand("+100", () => value += 100, () => value -= 100));
        Assert.False(history.CanRedo);
        Assert.True(history.Undo());
        Assert.True(history.Undo());
        Assert.Equal(0, value);
        Assert.False(history.Undo());
    }

    /// <summary>Everything inside a transaction is one entry.</summary>
    [Fact]
    public void A_transaction_is_one_entry()
    {
        int value = 0;
        EditHistory history = new();
        using (history.BeginTransaction("Both"))
        {
            history.Execute(new DelegateCommand("a", () => value++, () => value--));
            history.Execute(new DelegateCommand("b", () => value++, () => value--));
        }

        Assert.Equal("Both", history.UndoName);
        history.Undo();
        Assert.Equal(0, value);
    }

    /// <summary>The frames of one drag merge; a break starts a new step.</summary>
    [Fact]
    public void A_gesture_merges_until_broken()
    {
        MissionDocument document = Read();
        MissionTask a = document.EnumerateTasks().Single(static t => t.Label == "A");
        EditHistory history = new();

        history.Execute(SetParameterCommand.Numbers(document, a, "Position", [1, 0, 0]));
        history.Execute(SetParameterCommand.Numbers(document, a, "Position", [2, 0, 0]));
        Assert.Single(history.Done);

        history.BreakMerge();
        history.Execute(SetParameterCommand.Numbers(document, a, "Position", [3, 0, 0]));
        Assert.Equal(2, history.Done.Count);

        history.Undo();
        Assert.Equal(2.0, X(a));
        history.Undo();
        Assert.Equal(0.0, X(a));
    }

    /// <summary>Dirty follows the save point through undo and redo, and is lost for good when its branch is dropped.</summary>
    [Fact]
    public void Dirty_tracks_the_save_point()
    {
        EditHistory history = new();
        Assert.False(history.IsDirty);
        history.Execute(new DelegateCommand("x", static () => { }, static () => { }));
        history.MarkSaved();
        Assert.False(history.IsDirty);

        history.Undo();
        Assert.True(history.IsDirty);
        history.Redo();
        Assert.False(history.IsDirty);

        history.Undo();
        history.Execute(new DelegateCommand("y", static () => { }, static () => { }));
        Assert.True(history.IsDirty);
    }

    /// <summary>Delete, move and their undos restore the exact file, and every edit is announced.</summary>
    [Fact]
    public void Structural_edits_undo_to_the_same_bytes()
    {
        MissionDocument document = Read();
        byte[] original = document.ToImage();
        List<MissionChange> changes = [];
        document.Changed += changes.Add;

        MissionTask a = document.EnumerateTasks().Single(static t => t.Label == "A");
        MissionTask b = document.EnumerateTasks().Single(static t => t.Label == "B");
        EditHistory history = new();

        history.Execute(new MoveTaskCommand(document, b, new TaskLocation(a, 0)));
        Assert.Same(a, b.Parent);
        history.Execute(new RemoveTaskCommand(document, a));
        Assert.False(document.Contains(b));
        history.Execute(new SetIdentityCommand(document, document.RootTasks.Single(), "Renamed", 9));

        Assert.Equal(
            [MissionChangeKind.Moved, MissionChangeKind.Removed, MissionChangeKind.Identity],
            changes.Select(static c => c.Kind));

        while (history.Undo())
        {
        }

        Assert.Equal(original, document.ToImage());
    }

    /// <summary>A task cannot be moved into its own subtree.</summary>
    [Fact]
    public void A_task_cannot_contain_itself()
    {
        MissionDocument document = Read();
        MissionTask root = document.RootTasks.Single();
        Assert.Throws<InvalidOperationException>(() => document.Move(root, new TaskLocation(root.Children[0], 0)));
    }

    /// <summary>The primary item is the last added; toggling and pruning keep the rest in order.</summary>
    [Fact]
    public void Selection_keeps_a_primary()
    {
        Selection<string> selection = new();
        int raised = 0;
        selection.Changed += () => raised++;

        selection.Set("a");
        selection.Add("b");
        selection.Toggle("a");
        Assert.Equal("b", selection.Primary);
        selection.Toggle("a");
        Assert.Equal("a", selection.Primary);
        selection.Prune(static s => s != "a");
        Assert.Equal(["b"], selection.Items);
        Assert.Equal(5, raised);
    }

    private static double X(MissionTask task)
    {
        Assert.True(task.FindParameter("Position")!.TryGetNumbers(out double[] values));
        return values[0];
    }

    private static MissionDocument Read() => MissionDocument.Read(QscParser.ParseScript(Sample, QvmProgram.IgiVersion));
}
