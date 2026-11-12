using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Tasks;
using Xunit;

namespace OpenIGI.Engine.Tests.Tasks;

/// <summary>The engine's task list (<c>QTaskList</c>): fixed in size, and re-ordered by a removal.</summary>
public sealed class TaskListTests
{
    [Fact]
    public void TasksAreListedInTheOrderTheyRegister()
    {
        TaskList<string> list = new("test", 4);
        list.Register("a");
        list.Register("b");
        list.Register("c");

        Assert.Equal(3, list.Count);
        Assert.Equal(["a", "b", "c"], list.AsSpan().ToArray());
        Assert.Equal("b", list[1]);
    }

    [Fact]
    public void RemovingATaskMovesTheLastOneIntoItsPlace()
    {
        TaskList<string> list = new("test", 4);
        list.Register("a");
        list.Register("b");
        list.Register("c");
        list.Register("d");

        Assert.True(list.Remove("b"));

        Assert.Equal(["a", "d", "c"], list.AsSpan().ToArray());
    }

    [Fact]
    public void RemovingTheLastTaskOnlyShortensTheList()
    {
        TaskList<string> list = new("test", 2);
        list.Register("a");
        list.Register("b");

        Assert.True(list.Remove("b"));

        Assert.Equal(["a"], list.AsSpan().ToArray());
    }

    [Fact]
    public void RemovingATaskThatIsNotListedChangesNothing()
    {
        TaskList<string> list = new("test", 2);
        list.Register("a");

        Assert.False(list.Remove("z"));
        Assert.Equal(1, list.Count);
    }

    [Fact]
    public void ATaskRegisteredTwiceIsListedTwiceUnlessItIsLookedForFirst()
    {
        TaskList<string> list = new("test", 4);
        string task = "a";
        list.Register(task);
        list.Register(task);

        Assert.Equal(2, list.Count);
        Assert.False(list.TryRegister(task));
        Assert.True(list.TryRegister("b"));
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void FillingTheListIsFatal()
    {
        TaskList<string> list = new("test", 1);
        list.Register("a");

        Assert.Throws<EngineFatalException>(() => list.Register("b"));
    }

    [Fact]
    public void AnEmptiedListHoldsNothing()
    {
        TaskList<string> list = new("test", 2);
        list.Register("a");
        list.Clear();

        Assert.Equal(0, list.Count);
        Assert.False(list.Contains("a"));
        Assert.Throws<ArgumentOutOfRangeException>(() => list[0]);
    }
}
