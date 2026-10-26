using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Ai;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The parts of a HumanAI's own update (<c>0x44F410</c>) that run on its countdowns: the idle animation it posts to
/// its script, and how long a settled body stays before it leaves the world.
/// </summary>
public sealed class Igi1HumanAiTickTests
{
    private static readonly Vector3d FarAway = new(1.0e7, 1.0e7, 0.0);

    private static Igi1HumanAi Brain() => new(graph: null, Vector3d.Zero, facing: 0f, phase: 0, seed: 1);

    /// <summary>A settled body stays 301 ticks after its clip's last, then leaves the first tick no render drew it.</summary>
    [Fact]
    public void BodyOutOfSightLeavesTheWorldOnTheTickItsStayRunsOut()
    {
        Igi1HumanAi brain = Brain();
        brain.Kill();
        Assert.False(brain.TickDeadAi(drawnLastRender: false, generated: false));
        brain.TickDeadBody(default, deathClipEnded: true);

        for (int tick = 1; tick <= Igi1HumanAi.BodyStayTicks + 1; tick++)
        {
            Assert.False(brain.TickDeadAi(drawnLastRender: false, generated: false));
        }

        Assert.True(brain.TickDeadAi(drawnLastRender: false, generated: false));
        Assert.True(brain.HasLeftWorld);
        Assert.False(brain.TickDeadAi(drawnLastRender: false, generated: false));
    }

    /// <summary>A body the last render drew stays in the world; it goes the first tick a render misses it.</summary>
    [Fact]
    public void BodyInSightStaysUntilARenderMissesIt()
    {
        Igi1HumanAi brain = Brain();
        brain.Kill();
        brain.TickDeadBody(default, deathClipEnded: true);
        for (int tick = 0; tick < Igi1HumanAi.BodyStayTicks + 500; tick++)
        {
            Assert.False(brain.TickDeadAi(drawnLastRender: true, generated: false));
        }

        Assert.True(brain.TickDeadAi(drawnLastRender: false, generated: false));
    }

    /// <summary>A GuardGenerator's body leaves when its stay runs out, in sight or not (<c>0x488610</c> parent test).</summary>
    [Fact]
    public void GeneratedBodyLeavesInSight()
    {
        Igi1HumanAi brain = Brain();
        brain.Kill();
        brain.TickDeadBody(default, deathClipEnded: true);
        for (int tick = 1; tick <= Igi1HumanAi.BodyStayTicks + 1; tick++)
        {
            Assert.False(brain.TickDeadAi(drawnLastRender: true, generated: true));
        }

        Assert.True(brain.TickDeadAi(drawnLastRender: true, generated: true));
    }

    /// <summary>The stay does not start before the death clip has run (Human+828).</summary>
    [Fact]
    public void BodyStayWaitsForTheDeathClip()
    {
        Igi1HumanAi brain = Brain();
        brain.Kill();
        for (int tick = 0; tick < Igi1HumanAi.BodyStayTicks * 2; tick++)
        {
            Assert.False(brain.TickDeadAi(drawnLastRender: false, generated: true));
        }
    }

    /// <summary>
    /// The idle-animation countdown posts at nought and re-arms, so with no random span the gap is the interval plus
    /// one tick (<c>0x44F410</c>, armed by <c>AIFunction_SetAnimationInterval</c> <c>0x44EAF0</c>).
    /// </summary>
    [Fact]
    public void IdleAnimationPostsEveryIntervalPlusOneTick()
    {
        Igi1HumanAi brain = Brain();
        List<int> posted = [];
        int now = 0;
        brain.Events.Queued = record =>
        {
            if (record.Type == AiEvent.Animation)
            {
                posted.Add(now);
            }
        };

        brain.AddAnimationEntry(5, 1.0);
        brain.SetAnimationInterval(1.0f, 0f);

        // The first tick is the creation's only, and runs no countdown.
        for (now = 0; now <= 100; now++)
        {
            brain.Tick(now, FarAway, rootMotionThisTick: 0.0);
        }

        Assert.Equal([31, 62, 93], posted);
    }

    /// <summary>An AI owns 32 event records: a 33rd waiting event is dropped, and the pump hands them all back (<c>0x450520</c>).</summary>
    [Fact]
    public void EventPoolHoldsThirtyTwoRecords()
    {
        AiEventQueues queues = new() { IsInitialised = true };
        queues.Declare(AiEvent.Gunshot);
        queues.Declare(AiEvent.Walk);
        for (int i = 0; i < AiEventQueues.RecordPool; i++)
        {
            Assert.True(queues.Post(AiEventRecord.Simple(i % 2 == 0 ? AiEvent.Gunshot : AiEvent.Walk), owner: 0, worldTests: null));
        }

        Assert.False(queues.Post(AiEventRecord.Simple(AiEvent.Gunshot), owner: 0, worldTests: null));

        queues.Pump(AiEventQueues.UnboundedPriorityCap, (_, _) => false);
        Assert.True(queues.Post(AiEventRecord.Simple(AiEvent.Gunshot), owner: 0, worldTests: null));
    }

    /// <summary>An armed countdown with no entries never posts: <c>0x44F410</c> tests the entry count (+15204) first.</summary>
    [Fact]
    public void IdleAnimationNeedsAnEntry()
    {
        Igi1HumanAi brain = Brain();
        int posts = 0;
        brain.Events.Queued = record => posts += record.Type == AiEvent.Animation ? 1 : 0;
        brain.SetAnimationInterval(1.0f, 0f);

        for (int tick = 0; tick <= 100; tick++)
        {
            brain.Tick(tick, FarAway, rootMotionThisTick: 0.0);
        }

        Assert.Equal(0, posts);
    }
}
