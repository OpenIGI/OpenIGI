using OpenIGI.Engine.Tasks;
using Xunit;

namespace OpenIGI.Engine.Tests.Tasks;

public sealed class TaskTreeTests
{
    [Fact]
    public void NewAppendsChildrenInInsertionOrder()
    {
        (TaskTree tree, TaskType type) = Build();

        GameTask a = tree.New(tree.Root, type);
        GameTask b = tree.New(tree.Root, type);
        GameTask c = tree.New(tree.Root, type);

        Assert.Equal([a, b, c], tree.Root.Children);
        Assert.Same(a, tree.Root.FirstChild);
        Assert.Same(c, tree.Root.LastChild);
        Assert.Same(b, a.NextSibling);
        Assert.Same(b, c.PreviousSibling);
    }

    [Fact]
    public void NewSiblingInsertsAtTheHead()
    {
        (TaskTree tree, TaskType type) = Build();

        GameTask a = tree.New(tree.Root, type);
        GameTask b = tree.NewSibling(tree.Root, type);

        Assert.Equal([b, a], tree.Root.Children);
    }

    [Fact]
    public void ConstructionRunsAfterTheTaskIsLinked()
    {
        TaskTypeRegistry types = new();
        TaskType type = types.Register<ProbeTask>("Probe");
        TaskTree tree = new(types);

        var task = (ProbeTask)tree.New(tree.Root, type);

        Assert.Same(tree.Root, task.ParentAtCreate);
    }

    [Fact]
    public void DeleteDestroysChildrenLastToFirstBetweenPreDestroyAndDestroy()
    {
        TaskTypeRegistry types = new();
        TaskType type = types.Register<ProbeTask>("Probe");
        TaskTree tree = new(types);
        List<string> log = [];

        var parent = (ProbeTask)tree.New(tree.Root, type);
        parent.Log = log;
        parent.Tag = "parent";

        foreach (string tag in (string[])["a", "b", "c"])
        {
            var child = (ProbeTask)tree.New(parent, type);
            child.Log = log;
            child.Tag = tag;
        }

        tree.Delete(parent);

        // PreDestroy on the parent first, then children last-to-first (each complete), then the
        // parent's destructor. This is the order 0x4015F0 uses.
        Assert.Equal(
            [
                "pre:parent",
                "pre:c", "dtor:c",
                "pre:b", "dtor:b",
                "pre:a", "dtor:a",
                "dtor:parent",
            ],
            log);
    }

    [Fact]
    public void ADestructorStillSeesItselfAttachedToItsParent()
    {
        TaskTypeRegistry types = new();
        TaskType type = types.Register<ProbeTask>("Probe");
        TaskTree tree = new(types);

        var task = (ProbeTask)tree.New(tree.Root, type);
        tree.Delete(task);

        Assert.Same(tree.Root, task.ParentAtDestroy);
        Assert.Null(task.Parent);
    }

    [Fact]
    public void IdsResolveBothWaysAndAreReleasedOnDelete()
    {
        (TaskTree tree, TaskType type) = Build();
        GameTask task = tree.New(tree.Root, type);

        Assert.True(tree.TryBindId(98, task));
        Assert.Same(task, tree.FromId(98));
        Assert.Equal(98, tree.GetId(task));
        Assert.Equal(98, task.Id);

        tree.Delete(task);

        Assert.Null(tree.FromId(98));
        Assert.Equal(GameTask.NoId, tree.GetId(task));
    }

    [Fact]
    public void BindingAnIdThatIsTakenFails()
    {
        (TaskTree tree, TaskType type) = Build();
        GameTask first = tree.New(tree.Root, type);
        GameTask second = tree.New(tree.Root, type);

        Assert.True(tree.TryBindId(7, first));
        Assert.False(tree.TryBindId(7, second));
        Assert.Same(first, tree.FromId(7));
        Assert.Equal(GameTask.NoId, second.Id);
    }

    [Fact]
    public void IdsOutsideTheTableReadAsUnbound()
    {
        (TaskTree tree, _) = Build();

        // Task_FromId guards on id < 0x1000 rather than faulting, and level data relies on that.
        Assert.Null(tree.FromId(TaskTree.IdCapacity));
        Assert.Null(tree.FromId(-1));
        Assert.Equal(4096, TaskTree.IdCapacity);
    }

    [Fact]
    public void ClearIdsReleasesEveryIdWithoutDeletingTasks()
    {
        (TaskTree tree, TaskType type) = Build();
        GameTask task = tree.New(tree.Root, type);
        tree.TryBindId(12, task);

        tree.ClearIds();

        Assert.Null(tree.FromId(12));
        Assert.Equal(GameTask.NoId, task.Id);
        Assert.False(task.IsDeleted);
        Assert.Contains(task, tree.Root.Children);
    }

    [Fact]
    public void NameIsTheTypeNameAndId()
    {
        (TaskTree tree, TaskType type) = Build();
        GameTask task = tree.New(tree.Root, type);
        tree.TryBindId(98, task);

        // Scripts see TypeName_id, never the editor label the Task_New call carried.
        Assert.Equal("Container_98", task.Name);
    }

    [Fact]
    public void AnUnregisteredTypeNameIsFatal()
    {
        TaskTypeRegistry types = new();

        Core.Diagnostics.EngineFatalException error =
            Assert.Throws<Core.Diagnostics.EngineFatalException>(() => types.Get("Nope"));

        Assert.Equal("QTask type \"Nope\" not defined", error.Message);
    }

    private static (TaskTree Tree, TaskType Type) Build()
    {
        TaskTypeRegistry types = new();
        TaskType type = types.Register<RootTask>("Container");
        return (new TaskTree(types), type);
    }

    /// <summary>A task nobody registered still has a name, and still refuses a type.</summary>
    /// <remarks>
    /// <para>
    /// Not every task is a level's. The player's third-person body is a <c>HumanSoldier</c> the renderer
    /// builds by hand so it can pose a mesh, and it is never in a tree, never given a script type and
    /// never binds a variable.
    /// </para>
    /// <para>
    /// Asking such a task for its <see cref="GameTask.Type"/> is still a mistake and still throws. Asking
    /// for its <see cref="GameTask.Name"/> is not: a name is what a diagnostic prints, and this one used
    /// to throw from inside a warning about a missing model -- turning "this level does not ship that
    /// mesh" into an unhandled exception on the frame the mission came up.
    /// </para>
    /// </remarks>
    [Fact]
    public void ATaskWithNoTypeStillHasAName()
    {
        SwitchTask loose = new();

        Assert.Throws<InvalidOperationException>(() => loose.Type);
        Assert.Equal("SwitchTask_-1", loose.Name);
    }
}
