using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Saves;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests.Saves;

/// <summary>
/// The save engine both titles share (<see cref="SaveScript"/>): which tasks a save keeps and against what, how a load finds them
/// again, and what a text save's arguments read back as.
/// </summary>
public sealed class SaveScriptTests
{
    private static readonly SaveContext Context = new() { Version = 7 };

    /// <summary>
    /// A task with a level id is written against itself; one without is not written, unless its type asks to be kept with its
    /// parent, and then it is written against the nearest task above it that has an id -- even when it has an id of its own.
    /// </summary>
    [Fact]
    public void A_task_is_written_against_itself_or_its_nearest_ancestor_with_an_id()
    {
        Level level = new();
        Counter named = level.Add(level.Tree.Root, "Counter", id: 5, value: 11);
        level.Add(named, "Counter", id: GameTask.NoId, value: 12);
        Counter kept = level.Add(named, "Kept", id: GameTask.NoId, value: 13, withParent: true);
        level.Add(kept, "Kept", id: 9, value: 14, withParent: true);

        string script = SaveScript.Create(level.Tree.Root, Context.Info(3, 40), Context)!;

        Assert.StartsWith("SaveGameInfo(7, 3, 40);\r\nBreakScript();\r\n", script, StringComparison.Ordinal);
        Assert.Equal(
            [
                "DefineTaskState(5, \"Counter\", 2, \"Kept\", 0, \"Kept\", 0, 9, 14);",
                "DefineTaskState(5, \"Counter\", 1, \"Kept\", 0, -1, 13);",
                "DefineTaskState(5, \"Counter\", 0, 5, 11);",
            ],
            script.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(2));
    }

    /// <summary>A save read onto a level built the same way puts each task's state back, and tells each state once all are read.</summary>
    [Fact]
    public void A_save_is_read_back_onto_the_tasks_it_names()
    {
        Level saved = new();
        Counter first = saved.Add(saved.Tree.Root, "Counter", id: 5, value: 11);
        saved.Add(first, "Kept", id: GameTask.NoId, value: 13, withParent: true);
        string script = SaveScript.Create(saved.Tree.Root, Context.Info(3, 40), Context)!;

        Level fresh = new();
        Counter into = fresh.Add(fresh.Tree.Root, "Counter", id: 5, value: 0);
        Counter child = fresh.Add(into, "Kept", id: GameTask.NoId, value: 0, withParent: true);
        List<string> misread = [];
        SaveInfo info = SaveScript.Load(script, fresh.Tree.Root, Context, misread);

        Assert.Equal(new SaveInfo(7, 3, 40, true), info);
        Assert.Empty(misread);
        Assert.Equal((11, 13), (into.Value, child.Value));
        Assert.Equal(["Kept", "Counter"], fresh.Done);
    }

    /// <summary>A header older than the format's version is not valid, and nothing of the save is read.</summary>
    [Fact]
    public void A_save_of_an_older_version_is_not_loaded()
    {
        Level level = new();
        Counter task = level.Add(level.Tree.Root, "Counter", id: 5, value: 1);
        string script = SaveScript.Create(level.Tree.Root, Context.Info(3, 40), Context)!;
        task.Value = 2;

        SaveContext newer = new() { Version = 8 };
        Assert.False(SaveScript.Check(script, newer).IsValid);
        Assert.False(SaveScript.Load(script, level.Tree.Root, newer).IsValid);
        Assert.Equal(2, task.Value);
    }

    /// <summary>
    /// A part of a task that is no task here is written one level below it, after the tasks of its type, and a load that finds no
    /// such child asks for it to be made, with the id the save names.
    /// </summary>
    [Fact]
    public void A_part_is_written_below_its_task_and_made_anew_when_missing()
    {
        Level level = new();
        Counter holder = level.Add(level.Tree.Root, "Counter", id: 5, value: 1);
        Parts states = new(holder);
        states.Made.Add((new Counter { Value = 21, WithParent = true }, 77));
        SaveContext context = new() { Version = 7, States = states };

        string script = SaveScript.Create(level.Tree.Root, context.Info(3, 0), context)!;
        Assert.Contains("DefineTaskState(5, \"Counter\", 1, \"Loose\", 0, 77, 21);\n", script, StringComparison.Ordinal);

        states.Made.Clear();
        SaveScript.Load(script, level.Tree.Root, context);
        (Counter made, int id) = Assert.Single(states.Made);
        Assert.Equal((21, 77), (made.Value, id));
    }

    /// <summary>A text save's arguments read back as they were written: integers, reals, booleans, words of bytes and strings.</summary>
    [Fact]
    public void Arguments_read_back_as_they_were_written()
    {
        Level level = new();
        Mixed task = (Mixed)level.Tree.New(level.Tree.Root, level.Tree.Types.Register("Mixed", static () => new Mixed()));
        level.Tree.TryBindId(3, task);
        task.Text = "a \"quoted\", line\nand a tab\t; here";
        task.Real = 0.1f;
        task.Flag = true;
        task.Bytes = [1, 2, 3, 4, 5];

        string script = SaveScript.Create(level.Tree.Root, Context.Info(1, 0), Context)!;
        (task.Text, task.Real, task.Flag, task.Bytes) = (string.Empty, 0f, false, []);
        List<string> misread = [];
        SaveScript.Load(script, level.Tree.Root, Context, misread);

        Assert.Empty(misread);
        Assert.Equal("a \"quoted\", line\nand a tab\t; here", task.Text);
        Assert.Equal(0.1f, task.Real);
        Assert.True(task.Flag);
        Assert.Equal([1, 2, 3, 4, 5], task.Bytes);
    }

    /// <summary>A door saved half open and commanded open is, read back, where it was and still heading the same way.</summary>
    [Fact]
    public void A_door_is_read_back_as_it_stood()
    {
        DoorState door = new(90f, 1f);
        door.Toggle();
        for (int tick = 0; tick < 10; tick++)
        {
            door.Tick();
        }

        StateWriter writer = new();
        ((ISavedState)door).WriteState(writer);

        DoorState other = new(90f, 1f);
        Level level = new();
        Holder holder = (Holder)level.Tree.New(level.Tree.Root, level.Tree.Types.Register("Holder", static () => new Holder()));
        level.Tree.TryBindId(4, holder);
        holder.State = door;
        string script = SaveScript.Create(level.Tree.Root, Context.Info(1, 0), Context)!;
        holder.State = other;
        SaveScript.Load(script, level.Tree.Root, Context);

        Assert.Equal(door.AngleDegrees, other.AngleDegrees);
        Assert.Equal(door.IsCommandedOpen, other.IsCommandedOpen);
        StateWriter again = new();
        ((ISavedState)other).WriteState(again);
        Assert.Equal(writer.Arguments, again.Arguments);
    }

    // A level of counters: tasks that keep one number.
    private sealed class Level
    {
        public Level() => Tree = new TaskTree(new TaskTypeRegistry());

        public TaskTree Tree { get; }

        public List<string> Done { get; } = [];

        public Counter Add(GameTask parent, string type, int id, int value, bool withParent = false)
        {
            TaskType taskType = Tree.Types.TryGet(type, out TaskType? known) ? known : Tree.Types.Register(type, static () => new Counter());
            Counter task = (Counter)Tree.New(parent, taskType);
            task.Value = value;
            task.WithParent = withParent;
            task.Done = Done;
            if (id != GameTask.NoId)
            {
                Tree.TryBindId(id, task);
            }

            return task;
        }
    }

    private sealed class Counter : GameTask, ISavedState
    {
        public int Value { get; set; }

        public bool WithParent { get; set; }

        public List<string>? Done { get; set; }

        public bool IsSavedWithParent => WithParent;

        public void WriteState(StateWriter writer) => writer.Int(Value);

        public void ReadState(StateReader reader) => Value = reader.Int();

        public void LoadDone() => Done?.Add(HasTree ? Type.Name : "Loose");
    }

    private sealed class Mixed : GameTask, ISavedState
    {
        public string Text { get; set; } = string.Empty;

        public float Real { get; set; }

        public bool Flag { get; set; }

        public byte[] Bytes { get; set; } = [];

        public void WriteState(StateWriter writer)
        {
            writer.Text(Text);
            writer.Real(Real);
            writer.Bool(Flag);
            writer.Array(Bytes);
        }

        public void ReadState(StateReader reader)
        {
            Text = reader.Text();
            Real = reader.Real();
            Flag = reader.Bool();
            Bytes = reader.Array();
        }
    }

    // A task whose state lies beside it, as Project I.G.I.'s lie in its registries.
    private sealed class Holder : GameTask, ISavedState
    {
        public DoorState? State { get; set; }

        public void WriteState(StateWriter writer) => ((ISavedState?)State)?.WriteState(writer);

        public void ReadState(StateReader reader) => ((ISavedState?)State)?.ReadState(reader);
    }

    // The states of a level one of whose tasks has things beneath it that are no tasks.
    private sealed class Parts(GameTask holder) : ISaveStates
    {
        public List<(Counter State, int Id)> Made { get; } = [];

        public IReadOnlyList<SavedPart> PartsOf(GameTask task) =>
            ReferenceEquals(task, holder) ? [.. Made.Select(static m => new SavedPart("Loose", m.State, m.Id))] : [];

        public ISavedState? Recreate(GameTask parent, string typeName, int id)
        {
            Counter made = new() { WithParent = true };
            Made.Add((made, id));
            return made;
        }
    }
}
