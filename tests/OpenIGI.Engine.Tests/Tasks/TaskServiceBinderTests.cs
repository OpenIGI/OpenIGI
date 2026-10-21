using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Titles;
using Xunit;

namespace OpenIGI.Engine.Tests.Tasks;

public sealed partial class TaskServiceBinderTests
{
    [Fact]
    public void ATaskIsHandedTheServicesItsPropertiesAskFor()
    {
        TaskTree tree = new(new TaskTypeRegistry());
        TaskTypeRegistry types = tree.Types;
        ServiceTask task = (ServiceTask)tree.New(tree.Root, types.Register<ServiceTask>("ServiceTask"));
        ScopedServiceCatalog services = new(new ServiceCatalog());
        Clock clock = new();
        services.Provide(clock);

        TaskServiceBinder.Bind(tree.Root, services);

        Assert.Same(clock, task.Clock);
        Assert.Null(task.Ledger);
    }

    [Fact]
    public void AnUnprovidedServiceLeavesWhatTheTaskHad()
    {
        TaskTree tree = new(new TaskTypeRegistry());
        ServiceTask task = (ServiceTask)tree.New(tree.Root, tree.Types.Register<ServiceTask>("ServiceTask"));
        Ledger own = new();
        task.Ledger = own;

        TaskServiceBinder.Bind(tree.Root, new ScopedServiceCatalog(new ServiceCatalog()));

        Assert.Same(own, task.Ledger);
    }

    [Fact]
    public void ADerivedTaskIsHandedItsBasesServicesToo()
    {
        TaskTree tree = new(new TaskTypeRegistry());
        DerivedServiceTask task = (DerivedServiceTask)tree.New(tree.Root, tree.Types.Register<DerivedServiceTask>("DerivedServiceTask"));
        ServiceCatalog session = new();
        Ledger ledger = new();
        session.Provide(ledger);
        ScopedServiceCatalog level = new(session);
        Clock clock = new();
        level.Provide(clock);

        TaskServiceBinder.Bind(tree.Root, level);

        Assert.Same(clock, task.Clock);
        Assert.Same(ledger, task.Ledger);
        Assert.Same(ledger, task.Audit);
    }

    [Fact]
    public void ALevelsServicesStandInFrontOfTheSessionsAndGoWithIt()
    {
        ServiceCatalog session = new();
        Clock sessionClock = new();
        session.Provide(sessionClock);
        ScopedServiceCatalog level = new(session);
        Clock levelClock = new();
        level.Provide(levelClock);

        Assert.Same(levelClock, level.Require<Clock>());
        Assert.Same(sessionClock, session.Require<Clock>());
        Assert.Throws<InvalidOperationException>(() => level.Provide(new Clock()));
    }

    internal sealed class Clock;

    internal sealed class Ledger;

    internal partial class ServiceTask : GameTask
    {
        [LevelService]
        public Clock? Clock { get; set; }

        [LevelService]
        public Ledger? Ledger { get; set; }
    }

    internal sealed partial class DerivedServiceTask : ServiceTask
    {
        [LevelService]
        public Ledger? Audit { get; set; }
    }
}
