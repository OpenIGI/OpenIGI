using OpenIGI.Engine.Events;
using Xunit;

namespace OpenIGI.Engine.Tests.Events;

/// <summary>Covers who the event bus tells, in what order, and that one failing listener stops no other.</summary>
public sealed class GameEventsTests
{
    [Fact]
    public void ListenersAreToldInTheOrderTheySubscribed()
    {
        GameEvents events = new();
        List<string> heard = [];
        events.Subscribe((in Ping ping) => heard.Add($"first {ping.Value}"));
        events.Subscribe((in Ping ping) => heard.Add($"second {ping.Value}"));

        events.Raise(new Ping(7));

        Assert.Equal(["first 7", "second 7"], heard);
    }

    [Fact]
    public void AnEventIsHeardOnlyByItsOwnKind()
    {
        GameEvents events = new();
        int pings = 0;
        events.Subscribe((in Ping _) => pings++);

        events.Raise(new Pong());

        Assert.Equal(0, pings);
        Assert.True(events.IsHeard<Ping>());
        Assert.False(events.IsHeard<Pong>());
    }

    [Fact]
    public void AFailingListenerDoesNotStopTheRest()
    {
        GameEvents events = new();
        bool second = false;
        events.Subscribe((in Ping _) => throw new InvalidOperationException("broken"));
        events.Subscribe((in Ping _) => second = true);

        events.Raise(new Ping(1));

        Assert.True(second);
    }

    private readonly record struct Ping(int Value) : IGameEvent;

    private readonly record struct Pong : IGameEvent;
}
