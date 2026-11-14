using OpenIGI.Engine.Tasks;
using Xunit;

namespace OpenIGI.Engine.Tests.Tasks;

/// <summary>
/// Task events as the original sends them: to the tasks whose type links a handler, through the same cursor-safe walk the
/// fixed messages use.
/// </summary>
public sealed class TaskEventsTests
{
    [Fact]
    public void ATaskThatHandlesAnEventIsGivenItsContextAndCanAnswerThroughIt()
    {
        Fixture fixture = new();
        ListenerTask task = fixture.Listener(fixture.Tree.Root, "a");
        Ping ping = new() { Amount = 2 };

        bool handled = task.Trigger(ref ping);

        Assert.True(handled);
        Assert.Equal(["a"], ping.Heard);
        Assert.Equal(3, ping.Amount);
    }

    [Fact]
    public void ATaskWhoseTypeLinksNoHandlerIsPassedOver()
    {
        Fixture fixture = new();
        GameTask task = fixture.Deaf(fixture.Tree.Root);
        Ping ping = new();

        Assert.False(task.Trigger(ref ping));
        Assert.Empty(ping.Heard);
    }

    [Fact]
    public void ADerivedTypeAnswersWithItsParentsHandlerUnlessItLinksItsOwn()
    {
        Fixture fixture = new();
        ListenerTask inherits = fixture.Derived(fixture.Tree.Root, "inherits");
        ListenerTask overrides = fixture.Overriding(fixture.Tree.Root, "overrides");
        Ping ping = new();

        inherits.Trigger(ref ping);
        overrides.Trigger(ref ping);

        Assert.Equal(["inherits", "own:overrides"], ping.Heard);
    }

    [Fact]
    public void ARecursiveTriggerReachesEveryHandlerParentFirstAndGoesOnBeneathATaskThatTakesNoEvent()
    {
        Fixture fixture = new();
        ListenerTask a = fixture.Listener(fixture.Tree.Root, "a");
        GameTask deaf = fixture.Deaf(a);
        fixture.Listener(deaf, "a-deaf-1");
        fixture.Listener(a, "a2");
        fixture.Listener(fixture.Tree.Root, "b");
        Ping ping = new();

        fixture.Tree.Root.TriggerRecursively(ref ping);

        Assert.Equal(["a", "a-deaf-1", "a2", "b"], ping.Heard);
    }

    [Fact]
    public void TriggeringDescendantsLeavesTheTaskItselfOut()
    {
        Fixture fixture = new();
        ListenerTask a = fixture.Listener(fixture.Tree.Root, "a");
        fixture.Listener(a, "a1");
        fixture.Listener(a, "a2");
        Ping ping = new();

        a.TriggerDescendants(ref ping);

        Assert.Equal(["a1", "a2"], ping.Heard);
    }

    [Fact]
    public void ATriggerForOneTypeReachesThatTypeExactlyAndNotTheTypesDerivedFromIt()
    {
        Fixture fixture = new();
        fixture.Listener(fixture.Tree.Root, "base");
        fixture.Derived(fixture.Tree.Root, "derived");
        Ping ping = new();

        fixture.Tree.Root.TriggerRecursively(fixture.ListenerType, ref ping);

        Assert.Equal(["base"], ping.Heard);
    }

    [Fact]
    public void AHandlerThatDeletesItsOwnTaskStopsTheWalkBeneathItButNotAfterIt()
    {
        Fixture fixture = new();
        ListenerTask a = fixture.Listener(fixture.Tree.Root, "a");
        fixture.Listener(a, "a1");
        fixture.Listener(fixture.Tree.Root, "b");
        a.OnHandled = () => fixture.Tree.Delete(a);
        Ping ping = new();

        fixture.Tree.Root.TriggerRecursively(ref ping);

        Assert.Equal(["a", "b"], ping.Heard);
    }

    [Fact]
    public void AHandlerThatDeletesTheNextSiblingResumesAfterIt()
    {
        Fixture fixture = new();
        ListenerTask a = fixture.Listener(fixture.Tree.Root, "a");
        ListenerTask b = fixture.Listener(fixture.Tree.Root, "b");
        fixture.Listener(fixture.Tree.Root, "c");
        a.OnHandled = () => fixture.Tree.Delete(b);
        Ping ping = new();

        fixture.Tree.Root.TriggerDescendants(ref ping);

        Assert.Equal(["a", "c"], ping.Heard);
    }

    [Fact]
    public void AnEventTriggeredFromInsideAHandlerRunsToItsEndBeforeTheOuterWalkGoesOn()
    {
        Fixture fixture = new();
        ListenerTask a = fixture.Listener(fixture.Tree.Root, "a");
        ListenerTask inner = fixture.Listener(a, "a1");
        fixture.Listener(fixture.Tree.Root, "b");
        Pong pong = new();
        a.OnHandled = () => inner.TriggerRecursively(ref pong);
        Ping ping = new();

        fixture.Tree.Root.TriggerRecursively(ref ping);

        Assert.Equal(["a", "a1", "b"], ping.Heard);
        Assert.Equal(1, pong.Count);
        Assert.False(fixture.Tree.Walker.IsWalking);
    }

    private struct Ping : ITaskEvent
    {
        public Ping()
        {
        }

        public List<string> Heard { get; } = [];

        public int Amount { get; set; }
    }

    private struct Pong : ITaskEvent
    {
        public int Count { get; set; }
    }

    private class ListenerTask : GameTask, ITaskEventTarget<Ping>, ITaskEventTarget<Pong>
    {
        public string Tag { get; set; } = string.Empty;

        public Action? OnHandled { get; set; }

        public virtual void Handle(ref Ping context)
        {
            context.Heard.Add(Tag);
            context.Amount++;
            OnHandled?.Invoke();
        }

        public void Handle(ref Pong context) => context.Count++;
    }

    private sealed class DerivedListenerTask : ListenerTask;

    private sealed class OverridingListenerTask : ListenerTask
    {
        public override void Handle(ref Ping context) => context.Heard.Add("own:" + Tag);
    }

    private sealed class DeafTask : GameTask;

    private sealed class Fixture
    {
        private readonly TaskType _derived;
        private readonly TaskType _overriding;
        private readonly TaskType _deaf;

        public Fixture()
        {
            TaskTypeRegistry types = new();
            ListenerType = types.Register("Listener", () => new ListenerTask());
            _derived = types.Register("DerivedListener", () => new DerivedListenerTask(), ListenerType);
            _overriding = types.Register("OverridingListener", () => new OverridingListenerTask(), ListenerType);
            _deaf = types.Register("Deaf", () => new DeafTask());
            Tree = new TaskTree(types);
        }

        public TaskTree Tree { get; }

        public TaskType ListenerType { get; }

        public ListenerTask Listener(GameTask parent, string tag) => Tagged(parent, ListenerType, tag);

        public ListenerTask Derived(GameTask parent, string tag) => Tagged(parent, _derived, tag);

        public ListenerTask Overriding(GameTask parent, string tag) => Tagged(parent, _overriding, tag);

        public GameTask Deaf(GameTask parent) => Tree.New(parent, _deaf);

        private ListenerTask Tagged(GameTask parent, TaskType type, string tag)
        {
            ListenerTask task = (ListenerTask)Tree.New(parent, type);
            task.Tag = tag;
            return task;
        }
    }
}
