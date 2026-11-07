using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Ai;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>A mod's say in a guard (<see cref="IGuardBehaviour"/>): none changes nothing, and each answer it gives is taken.</summary>
public sealed class GuardBehaviourTests
{
    private const double Metre = 4096.0;
    private static readonly Vector3d FarAway = new(1.0e7, 1.0e7, 0.0);
    private static readonly Vector3d Shot = new(10 * Metre, 0.0, 0.0);

    private static Igi1HumanAi Brain(IGuardBehaviour? behaviour = null)
    {
        Igi1HumanAi brain = new(graph: null, Vector3d.Zero, facing: 0f, phase: 0, seed: 1);
        if (behaviour is not null)
        {
            brain.BehaviourFactory = _ => behaviour;
        }

        return brain;
    }

    /// <summary>A behaviour that answers nothing hears a shot exactly as a guard with none does.</summary>
    [Fact]
    public void ABehaviourOfDefaultsHearsAsTheGameDoes()
    {
        Igi1HumanAi plain = Brain(), given = Brain(new Defaults());

        Assert.Equal(plain.HearGunshot(Shot, 30 * Metre, fromSameSide: false, shooter: -1), given.HearGunshot(Shot, 30 * Metre, fromSameSide: false, shooter: -1));
        Assert.Equal(plain.Events.HeadOf(AiEvent.Gunshot), given.Events.HeadOf(AiEvent.Gunshot));
        Assert.Equal(plain.EyePosition(), given.EyePosition());
    }

    /// <summary>A stimulus the behaviour answers goes no further: no event reaches the script.</summary>
    [Fact]
    public void AStimulusTheBehaviourAnswersIsConsumed()
    {
        Igi1HumanAi brain = Brain(new Answering(false, moveBy: default));

        Assert.False(brain.HearGunshot(Shot, 30 * Metre, fromSameSide: false, shooter: -1));
        Assert.Equal(0, brain.Events.PendingCount(AiEvent.Gunshot));
    }

    /// <summary>Where the behaviour says a shot came from is where the script hears it.</summary>
    [Fact]
    public void AStimulusTheBehaviourMovesIsHeardThere()
    {
        Vector3d by = new(0.0, 3 * Metre, 0.0);
        Igi1HumanAi brain = Brain(new Answering(null, moveBy: by));

        Assert.True(brain.HearGunshot(Shot, 30 * Metre, fromSameSide: false, shooter: -1));
        Assert.Equal(Shot + by, brain.Events.HeadOf(AiEvent.Gunshot)?.Position);
    }

    /// <summary>His own side's fire, which the game leaves unheard, reaches a behaviour, marked as his side's.</summary>
    [Fact]
    public void HisOwnSidesFireReachesTheBehaviourOnly()
    {
        Answering hears = new(true, moveBy: default);

        Assert.False(Brain().HearGunshot(Shot, 30 * Metre, fromSameSide: true, shooter: 7));
        Assert.True(Brain(hears).HearGunshot(Shot, 30 * Metre, fromSameSide: true, shooter: 7));
        Assert.True(hears.Last.SameSide);
        Assert.Equal(GuardStimulusKind.Gunshot, hears.Last.Kind);
        Assert.Equal(7, hears.Last.Source);
    }

    /// <summary>A body a behaviour keeps stays in the world after its stay runs out, unseen; a guard with none leaves.</summary>
    [Fact]
    public void ABodyTheBehaviourKeepsStaysOutOfView()
    {
        Igi1HumanAi brain = Brain(new Keeping());
        brain.Kill();
        brain.TickDeadAi(drawnLastRender: false, generated: false);
        brain.TickDeadBody(default, deathClipEnded: true);

        for (int tick = 0; tick <= Igi1HumanAi.BodyStayTicks + 10; tick++)
        {
            Assert.False(brain.TickDeadAi(drawnLastRender: false, generated: false));
        }

        Assert.False(brain.HasLeftWorld);
    }

    /// <summary>A behaviour that forbids idle animations has none posted; the game's interval would post three.</summary>
    [Fact]
    public void ABehaviourCanForbidIdleAnimations()
    {
        Igi1HumanAi brain = Brain(new Keeping());
        int posted = 0;
        brain.Events.Queued = record => posted += record.Type == AiEvent.Animation ? 1 : 0;
        brain.AddAnimationEntry(5, 1.0);
        brain.SetAnimationInterval(1.0f, 0f);

        for (int now = 0; now <= 100; now++)
        {
            brain.Tick(now, FarAway, rootMotionThisTick: 0.0);
        }

        Assert.Equal(0, posted);
    }

    /// <summary>A lean moves the eye by just what the behaviour says.</summary>
    [Fact]
    public void ALeanMovesTheEye()
    {
        Vector3d lean = new(0.3 * Metre, 0.0, 0.0);

        Assert.Equal(Brain().EyePosition() + lean, Brain(new Keeping { Lean = lean }).EyePosition());
    }

    private sealed class Defaults : IGuardBehaviour
    {
    }

    private sealed class Answering(bool? answer, Vector3d moveBy) : IGuardBehaviour
    {
        public GuardStimulus Last { get; private set; }

        public bool? Stimulus(ref GuardStimulus stimulus)
        {
            stimulus.Position += moveBy;
            Last = stimulus;
            return answer;
        }
    }

    private sealed class Keeping : IGuardBehaviour
    {
        public Vector3d Lean { get; init; }

        public bool KeepsBodyOutOfView => true;

        public bool AllowsIdleAnimation => false;

        public Vector3d EyeOffset => Lean;
    }
}
