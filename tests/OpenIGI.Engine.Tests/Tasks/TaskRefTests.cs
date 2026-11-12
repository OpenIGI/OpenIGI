using OpenIGI.Engine.Tasks;
using Xunit;

namespace OpenIGI.Engine.Tests.Tasks;

/// <summary>A task reference, which the original clears when the task it names is deleted.</summary>
public sealed class TaskRefTests
{
    [Fact]
    public void AReferenceReadsAsTheTaskWhileItLives()
    {
        TaskTypeRegistry types = new();
        TaskTree tree = new(types);
        ProbeTask task = (ProbeTask)tree.New(tree.Root, types.Register("Probe", () => new ProbeTask()));

        TaskRef<ProbeTask> reference = task;

        Assert.True(reference.IsAlive);
        Assert.Same(task, reference.Target);
        Assert.True(reference.TryGet(out ProbeTask? found));
        Assert.Same(task, found);
    }

    [Fact]
    public void AReferenceReadsAsEmptyOnceTheTaskIsDeleted()
    {
        TaskTypeRegistry types = new();
        TaskTree tree = new(types);
        ProbeTask task = (ProbeTask)tree.New(tree.Root, types.Register("Probe", () => new ProbeTask()));
        TaskRef<ProbeTask> reference = task;

        tree.Delete(task);

        Assert.False(reference.IsAlive);
        Assert.Null(reference.Target);
        Assert.False(reference.TryGet(out _));
    }

    [Fact]
    public void AReferenceToATaskDeletedWithItsParentIsEmptyToo()
    {
        TaskTypeRegistry types = new();
        TaskTree tree = new(types);
        TaskType type = types.Register("Probe", () => new ProbeTask());
        GameTask parent = tree.New(tree.Root, type);
        TaskRef<GameTask> child = tree.New(parent, type);

        tree.Delete(parent);

        Assert.False(child.IsAlive);
    }

    [Fact]
    public void TheDefaultReferenceNamesNothing()
    {
        TaskRef<GameTask> reference = default;

        Assert.False(reference.IsAlive);
        Assert.Null(reference.Target);
        Assert.Equal(default, reference);
    }

    [Fact]
    public void TwoReferencesToOneTaskAreEqualBeforeAndAfterItIsDeleted()
    {
        TaskTypeRegistry types = new();
        TaskTree tree = new(types);
        GameTask task = tree.New(tree.Root, types.Register("Probe", () => new ProbeTask()));
        TaskRef<GameTask> one = task;
        TaskRef<GameTask> other = new(task);

        Assert.True(one == other);
        tree.Delete(task);
        Assert.True(one == other);
        Assert.False(one != other);
    }
}
