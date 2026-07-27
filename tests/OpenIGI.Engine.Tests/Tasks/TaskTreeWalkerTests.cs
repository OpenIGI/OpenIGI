using OpenIGI.Engine.Tasks;
using Xunit;

namespace OpenIGI.Engine.Tests.Tasks;

/// <summary>
/// The cases that make the explicit cursor stack necessary. Each one is a walk during which a handler
/// mutates the very list being walked; a naive foreach over a child collection either throws or visits
/// tasks that no longer exist.
/// </summary>
public sealed class TaskTreeWalkerTests
{
    [Fact]
    public void APlainWalkVisitsParentsBeforeChildrenInOrder()
    {
        Fixture fixture = new();
        WalkerTask root = fixture.Add(fixture.Tree.Root, "root");
        WalkerTask a = fixture.Add(root, "a");
        fixture.Add(a, "a1");
        fixture.Add(a, "a2");
        fixture.Add(root, "b");

        fixture.Tree.Update();

        Assert.Equal(["root", "a", "a1", "a2", "b"], fixture.Visited);
    }

    [Fact]
    public void ATaskThatDeletesItselfStopsItsOwnSubtreeButNotTheWalk()
    {
        Fixture fixture = new();
        WalkerTask a = fixture.Add(fixture.Tree.Root, "a");
        WalkerTask b = fixture.Add(fixture.Tree.Root, "b");
        fixture.Add(b, "b1");
        fixture.Add(fixture.Tree.Root, "c");

        b.OnUpdated = () => fixture.Tree.Delete(b);

        fixture.Tree.Update();

        // b runs, deletes itself, and its child is never visited — the walker sees its entry cursor was
        // rewritten. The walk then resumes at c rather than following a freed link.
        Assert.Equal(["a", "b", "c"], fixture.Visited);
        Assert.DoesNotContain(b, fixture.Tree.Root.Children);
        Assert.Same(a, fixture.Tree.Root.FirstChild);
    }

    [Fact]
    public void ATaskThatDeletesItsNextSiblingResumesAfterIt()
    {
        Fixture fixture = new();
        WalkerTask a = fixture.Add(fixture.Tree.Root, "a");
        WalkerTask b = fixture.Add(fixture.Tree.Root, "b");
        fixture.Add(fixture.Tree.Root, "c");

        a.OnUpdated = () => fixture.Tree.Delete(b);

        fixture.Tree.Update();

        // The parent's pending cursor named b; deleting it advances that cursor to c.
        Assert.Equal(["a", "c"], fixture.Visited);
    }

    [Fact]
    public void ATaskThatDeletesAWholeLaterSubtreeSkipsAllOfIt()
    {
        Fixture fixture = new();
        WalkerTask a = fixture.Add(fixture.Tree.Root, "a");
        WalkerTask b = fixture.Add(fixture.Tree.Root, "b");
        fixture.Add(b, "b1");
        fixture.Add(b, "b2");
        fixture.Add(fixture.Tree.Root, "c");

        a.OnUpdated = () => fixture.Tree.Delete(b);

        fixture.Tree.Update();

        Assert.Equal(["a", "c"], fixture.Visited);
    }

    [Fact]
    public void ATaskThatDeletesAnEarlierSiblingNeedsNoPatch()
    {
        Fixture fixture = new();
        WalkerTask a = fixture.Add(fixture.Tree.Root, "a");
        WalkerTask b = fixture.Add(fixture.Tree.Root, "b");
        fixture.Add(fixture.Tree.Root, "c");

        b.OnUpdated = () => fixture.Tree.Delete(a);

        fixture.Tree.Update();

        Assert.Equal(["a", "b", "c"], fixture.Visited);
    }

    [Fact]
    public void ATaskThatDeletesItsOwnChildDoesNotVisitIt()
    {
        Fixture fixture = new();
        WalkerTask a = fixture.Add(fixture.Tree.Root, "a");
        WalkerTask a1 = fixture.Add(a, "a1");
        fixture.Add(a, "a2");

        a.OnUpdated = () => fixture.Tree.Delete(a1);

        fixture.Tree.Update();

        Assert.Equal(["a", "a2"], fixture.Visited);
    }

    [Fact]
    public void ATaskThatDeletesTheLastSiblingEndsTheLoopCleanly()
    {
        Fixture fixture = new();
        WalkerTask a = fixture.Add(fixture.Tree.Root, "a");
        WalkerTask b = fixture.Add(fixture.Tree.Root, "b");

        a.OnUpdated = () => fixture.Tree.Delete(b);

        fixture.Tree.Update();

        Assert.Equal(["a"], fixture.Visited);
        Assert.Same(a, fixture.Tree.Root.FirstChild);
        Assert.Same(a, fixture.Tree.Root.LastChild);
    }

    [Fact]
    public void TheCursorStackUnwindsCompletelyAfterAWalk()
    {
        Fixture fixture = new();
        WalkerTask a = fixture.Add(fixture.Tree.Root, "a");
        fixture.Add(a, "a1");

        fixture.Tree.Update();

        Assert.Equal(0, fixture.Tree.Walker.Depth);
        Assert.False(fixture.Tree.Walker.IsWalking);
    }

    [Fact]
    public void DeletingOutsideAWalkPatchesNothing()
    {
        Fixture fixture = new();
        WalkerTask a = fixture.Add(fixture.Tree.Root, "a");

        fixture.Tree.Delete(a);

        Assert.Equal(0, fixture.Tree.Walker.Depth);
        Assert.Empty(fixture.Tree.Root.Children);
    }

    [Fact]
    public void TheTickReachesOnlyTheRootsChildrenOnItsOwn()
    {
        Fixture fixture = new();
        WalkerTask gate = fixture.Add(fixture.Tree.Root, "gate");
        WalkerTask under = fixture.Add(gate, "under");
        fixture.Add(under, "deeper");
        gate.ForwardsToChildren = false;

        fixture.Tree.Update();

        // The update message is not a broadcast: the arbiter hands it to the root's children and each of
        // them decides whether its own subtree ticks. A type that declines — Static and Idle install a
        // do-nothing update for exactly this reason — freezes everything beneath it, and nothing throws.
        Assert.Equal(["gate"], fixture.Visited);
    }

    [Fact]
    public void ABroadcastReachesTasksTheTickNeverWould()
    {
        Fixture fixture = new();
        WalkerTask gate = fixture.Add(fixture.Tree.Root, "gate");
        WalkerTask under = fixture.Add(gate, "under");
        gate.ForwardsToChildren = false;
        under.ForwardsToChildren = false;

        fixture.Tree.Walker.Send(fixture.Tree.Root, TaskMessage.Update);

        // Task_SendMessage recurses on its own, which is why PostLoad reaches a whole level and the tick
        // does not. Both walks exist in the original and they are not interchangeable.
        Assert.Equal(["gate", "under"], fixture.Visited);
    }

    [Fact]
    public void RenderAndUpdateAreTheSameWalkWithDifferentMessages()
    {
        Fixture fixture = new();
        WalkerTask a = fixture.Add(fixture.Tree.Root, "a");
        fixture.Add(a, "a1");

        fixture.Tree.Render();

        Assert.Equal(["a", "a1"], fixture.Rendered);
        Assert.Empty(fixture.Visited);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Types = new TaskTypeRegistry();
            Type = Types.Register("Walker", () => new WalkerTask());
            Tree = new TaskTree(Types);
        }

        public TaskTypeRegistry Types { get; }

        public TaskType Type { get; }

        public TaskTree Tree { get; }

        public List<string> Visited { get; } = [];

        public List<string> Rendered { get; } = [];

        public WalkerTask Add(GameTask parent, string tag)
        {
            var task = (WalkerTask)Tree.New(parent, Type);
            task.Tag = tag;
            task.Visited = Visited;
            task.Rendered = Rendered;
            return task;
        }
    }
}
