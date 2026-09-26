using OpenIGI.Game.Ai;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Covers the AI event pump's admission rules and its arbitration.</summary>
public sealed class AiEventPumpTests
{
    /// <summary>Before the first tick only <c>AIEVENT_CREATE</c> gets in, whatever the enable mask says.</summary>
    [Fact]
    public void BeforeInitialisationOnlyCreateIsAdmitted()
    {
        var queues = new AiEventQueues();
        foreach (AiEvent type in Enum.GetValues<AiEvent>())
        {
            queues.SetEnabled(type, true);
        }

        Assert.True(queues.Post(AiEventRecord.Simple(AiEvent.Create), owner: 0, worldTests: null));
        Assert.False(queues.Post(AiEventRecord.Simple(AiEvent.Gunshot), owner: 0, worldTests: null));
        Assert.Equal(1, queues.PendingCount(AiEvent.Create));
        Assert.Equal(0, queues.PendingCount(AiEvent.Gunshot));
    }

    /// <summary>After the first tick a type has to be enabled, and every type starts disabled.</summary>
    [Fact]
    public void AfterInitialisationATypeMustBeEnabled()
    {
        var queues = new AiEventQueues { IsInitialised = true };

        Assert.False(queues.IsEnabled(AiEvent.Gunshot));
        Assert.False(queues.Post(AiEventRecord.Simple(AiEvent.Gunshot), owner: 0, worldTests: null));

        queues.SetEnabled(AiEvent.Gunshot, true);
        Assert.True(queues.Post(AiEventRecord.Simple(AiEvent.Gunshot), owner: 0, worldTests: null));
    }

    /// <summary>A dead AI admits nothing at all — the very first gate in the handler.</summary>
    [Fact]
    public void ADeadAiAdmitsNothing()
    {
        var queues = new AiEventQueues { IsInitialised = true, IsDead = true };
        queues.SetEnabled(AiEvent.Gunshot, true);

        Assert.False(queues.Post(AiEventRecord.Simple(AiEvent.Gunshot), owner: 0, worldTests: null));
    }

    /// <summary>An AI never hears an event that names it as either the source or the subject.</summary>
    [Fact]
    public void AnAiNeverHearsItsOwnEvents()
    {
        var queues = new AiEventQueues { IsInitialised = true };
        queues.SetEnabled(AiEvent.Gunshot, true);

        AiEventRecord asSubject = AiEventRecord.Simple(AiEvent.Gunshot) with { Subject = 7 };
        AiEventRecord asSource = AiEventRecord.Simple(AiEvent.Gunshot) with { Source = 7 };
        AiEventRecord fromElsewhere = AiEventRecord.Simple(AiEvent.Gunshot) with { Subject = 8, Source = 9 };

        Assert.False(queues.Post(asSubject, owner: 7, worldTests: null));
        Assert.False(queues.Post(asSource, owner: 7, worldTests: null));
        Assert.True(queues.Post(fromElsewhere, owner: 7, worldTests: null));
    }

    /// <summary>A filter of zero skips the world tests entirely — the short path the engine's own posts take.</summary>
    [Fact]
    public void AnUnfilteredEventSkipsTheWorldTests()
    {
        var queues = new AiEventQueues { IsInitialised = true };
        queues.SetEnabled(AiEvent.AlarmOn, true);
        bool asked = false;

        Assert.True(queues.Post(
            AiEventRecord.Simple(AiEvent.AlarmOn),
            owner: 1,
            worldTests: _ =>
            {
                asked = true;
                return false;
            }));
        Assert.False(asked);
    }

    /// <summary>A filtered event is refused when the world tests say so, and the tests see the whole word.</summary>
    [Fact]
    public void AFilteredEventConsultsTheWorldOnce()
    {
        var queues = new AiEventQueues { IsInitialised = true };
        queues.SetEnabled(AiEvent.EnemyDetection, true);

        AiEventFilter seen = AiEventFilter.None;
        int calls = 0;
        AiEventRecord record = AiEventRecord.Simple(AiEvent.EnemyDetection) with
        {
            Filter = AiEventFilter.InView | AiEventFilter.InRange | AiEventFilter.SubjectIsEnemy,
            Subject = 12,
        };

        Assert.False(queues.Post(record, owner: 1, worldTests: f =>
        {
            seen = f;
            calls++;
            return false;
        }));

        Assert.Equal(1, calls);
        Assert.Equal(AiEventFilter.InView | AiEventFilter.InRange | AiEventFilter.SubjectIsEnemy, seen);
    }

    /// <summary>
    /// The order is the declaration order: <c>AIFunction_SetEventPriority</c> (<c>0x44E0C0</c>) enables a type and
    /// appends it the first time it is named, and naming it again changes nothing.
    /// </summary>
    [Fact]
    public void TheOrderIsTheDeclarationOrder()
    {
        var queues = new AiEventQueues { IsInitialised = true };
        Assert.Empty(queues.PriorityOrder);

        queues.Declare(AiEvent.Gunshot);
        queues.Declare(AiEvent.Idle);
        queues.Declare(AiEvent.Gunshot);

        Assert.Equal([AiEvent.Gunshot, AiEvent.Idle], queues.PriorityOrder);
        Assert.True(queues.IsEnabled(AiEvent.Gunshot));
        Assert.False(queues.IsEnabled(AiEvent.Combat));
    }

    /// <summary>
    /// Creation is dispatched directly on the first tick, at position -1, and initialises the AI; the ordered
    /// pump could not reach it, since the order is empty until the creation branch declares it.
    /// </summary>
    [Fact]
    public void CreationIsDispatchedDirectlyAndInitialises()
    {
        var queues = new AiEventQueues();
        AiEvent? seen = null;
        int slot = 0;

        queues.DispatchCreate((type, position) =>
        {
            seen = type;
            slot = position;
            queues.Declare(AiEvent.Gunshot);
            return true;
        });

        Assert.Equal(AiEvent.Create, seen);
        Assert.Equal(-1, slot);
        Assert.True(queues.IsInitialised);
        Assert.True(queues.Post(AiEventRecord.Simple(AiEvent.Gunshot), 1, null));
        Assert.False(queues.Post(AiEventRecord.Simple(AiEvent.Combat), 1, null));
    }

    /// <summary>
    /// At most one event type dispatches per tick, and it is the highest-priority non-empty one — but a type
    /// whose script produces nothing does not stop a later one from being tried.
    /// </summary>
    [Fact]
    public void OneTypeDispatchesPerTickButAnEmptyHandlerDoesNotBlock()
    {
        var queues = new AiEventQueues { IsInitialised = true };
        queues.Declare(AiEvent.Gunshot);
        queues.Declare(AiEvent.Combat);
        queues.Post(AiEventRecord.Simple(AiEvent.Gunshot), 1, null);
        queues.Post(AiEventRecord.Simple(AiEvent.Combat), 1, null);

        List<AiEvent> tried = [];

        // Gunshot's handler produces nothing, so Combat still gets its turn and is the one that "handles".
        AiEvent? dispatched = queues.Pump(AiEventQueues.UnboundedPriorityCap, (type, _) =>
        {
            tried.Add(type);
            return type == AiEvent.Combat;
        });

        Assert.Equal([AiEvent.Gunshot, AiEvent.Combat], tried);
        Assert.Equal(AiEvent.Combat, dispatched);
    }

    /// <summary>Once a type has handled the tick, nothing later in the order is even offered.</summary>
    [Fact]
    public void NothingIsOfferedAfterATypeHandlesTheTick()
    {
        var queues = new AiEventQueues { IsInitialised = true };
        queues.Declare(AiEvent.Gunshot);
        queues.Declare(AiEvent.Combat);
        queues.Post(AiEventRecord.Simple(AiEvent.Gunshot), 1, null);
        queues.Post(AiEventRecord.Simple(AiEvent.Combat), 1, null);

        List<AiEvent> tried = [];
        AiEvent? dispatched = queues.Pump(AiEventQueues.UnboundedPriorityCap, (type, _) =>
        {
            tried.Add(type);
            return true;
        });

        Assert.Equal([AiEvent.Gunshot], tried);
        Assert.Equal(AiEvent.Gunshot, dispatched);
    }

    /// <summary>
    /// <b>An undispatched event is discarded, not deferred.</b> Every queue is emptied at the end of the tick
    /// whether or not it got a turn, so a busy guard never learns about the shot it heard.
    /// </summary>
    [Fact]
    public void AnUndispatchedEventIsDiscardedNotDeferred()
    {
        var queues = new AiEventQueues { IsInitialised = true };
        queues.Declare(AiEvent.Gunshot);
        queues.Declare(AiEvent.Combat);
        queues.Post(AiEventRecord.Simple(AiEvent.Gunshot), 1, null);
        queues.Post(AiEventRecord.Simple(AiEvent.Combat), 1, null);
        queues.Post(AiEventRecord.Simple(AiEvent.Combat), 1, null);

        queues.Pump(AiEventQueues.UnboundedPriorityCap, (_, _) => true);

        Assert.Equal(0, queues.PendingCount(AiEvent.Gunshot));
        Assert.Equal(0, queues.PendingCount(AiEvent.Combat));
        Assert.Equal(1, queues.DrainedCount(AiEvent.Gunshot));
        Assert.Equal(2, queues.DrainedCount(AiEvent.Combat));

        // A second tick has nothing left to give anyone.
        Assert.Null(queues.Pump(AiEventQueues.UnboundedPriorityCap, (_, _) => true));
    }

    /// <summary>
    /// The cap is a position in the priority order, so a running action with a low cap lets only the events
    /// ranked above it interrupt.
    /// </summary>
    [Fact]
    public void ThePriorityCapKeepsLowRankedEventsOut()
    {
        var queues = new AiEventQueues { IsInitialised = true };
        foreach (AiEvent ahead in new[] { AiEvent.Gunshot, AiEvent.Walk, AiEvent.Alert, AiEvent.Idle, AiEvent.Door })
        {
            queues.Declare(ahead);
        }

        queues.Declare(AiEvent.Combat);
        queues.Post(AiEventRecord.Simple(AiEvent.Combat), 1, null);

        // Cap 5 means positions 0..4 may dispatch; Combat sits at 5 and is shut out - and still drained.
        Assert.Null(queues.Pump(5, (_, _) => true));
        Assert.Equal(1, queues.DrainedCount(AiEvent.Combat));

        queues.Post(AiEventRecord.Simple(AiEvent.Combat), 1, null);
        Assert.Equal(AiEvent.Combat, queues.Pump(6, (_, _) => true));
    }

    /// <summary>The cap comes from the head pending action, and is unbounded when nothing is pending.</summary>
    [Fact]
    public void TheCapComesFromTheHeadPendingAction()
    {
        var list = new AiPendingActionList();
        Assert.False(list.HasPending);
        Assert.Equal(AiEventQueues.UnboundedPriorityCap, list.PriorityCap);

        list.Add(new AiPendingAction(priority: 3, countdown: 0, run: null));
        list.Add(new AiPendingAction(priority: 9, countdown: 0, run: null));

        Assert.True(list.HasPending);
        Assert.Equal(3, list.PriorityCap);
    }

    /// <summary>
    /// Only one countdown decrements per tick across the whole list, so a run of delays takes their sum
    /// rather than their maximum.
    /// </summary>
    [Fact]
    public void OnlyOneCountdownDecrementsPerTick()
    {
        var first = new AiPendingAction(0, 2, null);
        var second = new AiPendingAction(0, 2, null);
        var list = new AiPendingActionList();
        list.Add(first);
        list.Add(second);

        Assert.Null(list.Tick());
        Assert.Equal(1, first.Countdown);
        Assert.Equal(2, second.Countdown);

        Assert.Null(list.Tick());
        Assert.Equal(0, first.Countdown);
        Assert.Equal(2, second.Countdown);

        // Only now does the second one start counting - four ticks for two delays of two.
        Assert.Null(list.Tick());
        Assert.Equal(1, second.Countdown);
        Assert.Null(list.Tick());
        Assert.Equal(0, second.Countdown);
    }

    /// <summary>
    /// The walk stops at the first entry that has something to run, and runs exactly that one — even if a
    /// later entry is also runnable, and even while an earlier delay is still counting down.
    /// </summary>
    [Fact]
    public void TheFirstRunnableActionRunsAndNothingAfterIt()
    {
        int ranFirst = 0;
        int ranSecond = 0;
        var delay = new AiPendingAction(0, 5, null);
        var runnable = new AiPendingAction(0, 0, () => ranFirst++);
        var later = new AiPendingAction(0, 0, () => ranSecond++);

        var list = new AiPendingActionList();
        list.Add(delay);
        list.Add(runnable);
        list.Add(later);

        Assert.Same(runnable, list.Tick());
        Assert.Equal(1, ranFirst);
        Assert.Equal(0, ranSecond);

        // The delay in front of it still ticked down on the same pass.
        Assert.Equal(4, delay.Countdown);
    }

    /// <summary>An empty list runs nothing, which is the state that makes an AI idle.</summary>
    [Fact]
    public void AnEmptyListRunsNothing()
    {
        var list = new AiPendingActionList();
        Assert.Null(list.Tick());
        Assert.False(list.HasPending);
    }
}
