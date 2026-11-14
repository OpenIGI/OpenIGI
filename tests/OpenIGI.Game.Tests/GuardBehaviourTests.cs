using OpenIGI.Core.Diagnostics;
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

    private static Igi1HumanAi Brain(params IGuardBehaviour[] behaviours) => Brain(NullLogger.Instance, behaviours);

    private static Igi1HumanAi Brain(IOpenIgiLogger logger, params IGuardBehaviour[] behaviours)
    {
        Igi1HumanAi brain = new(graph: null, Vector3d.Zero, facing: 0f, phase: 0, seed: 1);
        foreach (IGuardBehaviour behaviour in behaviours)
        {
            brain.Attach(_ => behaviour, logger);
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

    /// <summary>His own side's fire within reach, which the game leaves unheard, reaches a behaviour, marked as his side's.</summary>
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

    /// <summary>His own side's fire out of its reach is asked of no behaviour, as any shot out of reach is heard by nobody.</summary>
    [Fact]
    public void HisOwnSidesFireOutOfReachReachesNoBehaviour()
    {
        Answering hears = new(true, moveBy: default);

        Assert.False(Brain(hears).HearGunshot(Shot, 5 * Metre, fromSameSide: true, shooter: 7));
        Assert.Equal(default, hears.Last);
    }

    /// <summary>Each behaviour is asked of what the last left, until one answers; the ones after it are not asked.</summary>
    [Fact]
    public void BehavioursAreAskedInTurnUntilOneAnswers()
    {
        Vector3d by = new(0.0, 3 * Metre, 0.0);
        Answering moves = new(null, moveBy: by), answers = new(false, moveBy: default), after = new(true, moveBy: default);

        Assert.False(Brain(moves, answers, after).HearGunshot(Shot, 30 * Metre, fromSameSide: false, shooter: -1));
        Assert.Equal(Shot + by, answers.Last.Position);
        Assert.Equal(default, after.Last);
    }

    /// <summary>Leans and glances add up; an idle animation and a round need every behaviour's leave, a body only one's keeping.</summary>
    [Fact]
    public void BehavioursAnswersAreCombined()
    {
        Vector3d lean = new(0.3 * Metre, 0.0, 0.0);
        Holding holds = new(), lets = new() { Lets = true };
        Igi1HumanAi brain = Brain(new Keeping { Lean = lean }, new Keeping { Lean = lean, Keeps = false, Allows = true }, holds, lets);

        Assert.Equal(Brain().EyePosition() + lean + lean, brain.EyePosition());
        Assert.True(brain.Behaviours!.KeepsUnseenBody);
        Assert.False(brain.Behaviours.AllowsIdleAnimation);
        Assert.False(brain.Behaviours.MayFire);

        // Every behaviour is asked whether he may fire, whatever the one before said: one may count what it holds back.
        Assert.Equal(1, holds.Asked);
        Assert.Equal(1, lets.Asked);
    }

    /// <summary>The first behaviour to fight for him has the tick; the ones after it are not asked.</summary>
    [Fact]
    public void TheFirstBehaviourToFightHasTheTick()
    {
        Holding first = new() { Fights = true }, second = new() { Fights = true };

        Assert.True(Brain(first, second).Behaviours!.Fight(Shot));
        Assert.Equal(1, first.Fought);
        Assert.Equal(0, second.Fought);
    }

    /// <summary>A behaviour that throws is logged and counts as having said nothing; the others are still asked.</summary>
    [Fact]
    public void ABehaviourThatThrowsIsLoggedAndTheOthersStillAsked()
    {
        MemorySink log = new();
        Vector3d by = new(0.0, 3 * Metre, 0.0);
        Answering after = new(null, moveBy: by);
        Igi1HumanAi brain = Brain(log, new Throwing(), after);

        Assert.True(brain.HearGunshot(Shot, 30 * Metre, fromSameSide: false, shooter: -1));
        Assert.Equal(Shot + by, brain.Events.HeadOf(AiEvent.Gunshot)?.Position);
        Assert.Contains(log.Snapshot(), record => record.Message == "A guard behaviour's Stimulus failed: broken");
    }

    /// <summary>A factory is handed his control; one that declines him attaches nothing, and a detached one is asked no more.</summary>
    [Fact]
    public void AFactoryIsHandedHisControlAndCanBeLetGoOf()
    {
        Igi1HumanAi brain = Brain();
        GuardControl? handed = null;
        Keeping keeping = new();
        Func<GuardControl, IGuardBehaviour?> declines = _ => null;
        Func<GuardControl, IGuardBehaviour?> gives = control =>
        {
            handed = control;
            return keeping;
        };

        brain.Attach(declines, NullLogger.Instance);
        Assert.Null(brain.Behaviours);

        brain.Attach(gives, NullLogger.Instance);
        Assert.Same(brain, handed?.Guard);
        Assert.Same(keeping, brain.BehaviourOf<Keeping>());
        Assert.Same(keeping, brain.BehaviourOf<IGuardSenses>());

        brain.Detach(gives);
        Assert.Null(brain.Behaviours);
        Assert.Null(brain.BehaviourOf<Keeping>());
    }

    /// <summary>What the control has him do is what the game's own operations do.</summary>
    [Fact]
    public void TheControlDrivesHim()
    {
        Igi1HumanAi brain = Brain(), twin = Brain();
        GuardControl control = new(brain);
        Vector3d point = new(2 * Metre, 0.0, 0.0);

        control.LookingAt = point;
        Assert.Equal(point, control.LookingAt);

        control.HoldStill();
        Assert.Equal(AiSoldierActivity.Idle, brain.Activity);
        Assert.Equal(-1, brain.RequestedAnimation);

        control.StopRoute();
        Assert.Empty(control.Waypoints);
        Assert.Equal(-1, brain.DestinationNode);

        // His own stream of chance: drawn through the control, it is the stream the brain itself draws.
        Assert.Equal(twin.NextBounded(1000), control.NextRandom(1000));

        // No patrol fight, no grenade.
        Assert.False(control.ThrowGrenade());

        control.SetHitProbability(0.25f);
        Assert.Equal(0.25f, brain.HitProbability);

        brain.SetLineOfSight((_, to, seen) => to.X < Metre && seen is null);
        Assert.True(control.CanSeeBetween(default, new Vector3d(0.5 * Metre, 0.0, 0.0)));
        Assert.False(control.CanSeeBetween(default, new Vector3d(0.5 * Metre, 0.0, 0.0), seen: 3));
        Assert.False(control.CanSeeBetween(default, point));
    }

    /// <summary>A glance turns the eye about its own up: right and forward swung together, as they were swung by hand before.</summary>
    [Fact]
    public void AGlanceTurnsTheEyeAboutItsOwnUp()
    {
        Matrix3f eye = Matrix3f.FromEngineEulerAngles(0.2f, -0.1f, 0.7f);
        foreach (float turn in (float[])[0.3f, -1.1f, MathF.PI / 2f])
        {
            (float sin, float cos) = MathF.SinCos(turn);
            Vector3f r = eye.Column1, f = eye.Column2;
            Vector3f right = new((r.X * cos) - (f.X * sin), (r.Y * cos) - (f.Y * sin), (r.Z * cos) - (f.Z * sin));
            Vector3f ahead = new((f.X * cos) + (r.X * sin), (f.Y * cos) + (r.Y * sin), (f.Z * cos) + (r.Z * sin));
            Matrix3f turned = Igi1HumanAi.TurnedBy(turn, eye);

            Assert.True(Vector3f.Distance(right, turned.Column1) < 1e-5f, $"right at {turn}");
            Assert.True(Vector3f.Distance(ahead, turned.Column2) < 1e-5f, $"ahead at {turn}");
            Assert.True(Vector3f.Distance(eye.Column3, turned.Column3) < 1e-5f, $"up at {turn}");
        }

        Assert.Equal(eye, Igi1HumanAi.TurnedBy(0f, eye));
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

    private sealed class Defaults : IGuardLife, IGuardSenses, IGuardMovement, IGuardCombat
    {
    }

    private sealed class Answering(bool? answer, Vector3d moveBy) : IGuardSenses
    {
        public GuardStimulus Last { get; private set; }

        public bool? Stimulus(ref GuardStimulus stimulus)
        {
            stimulus.Position += moveBy;
            Last = stimulus;
            return answer;
        }
    }

    private sealed class Keeping : IGuardLife, IGuardSenses
    {
        public Vector3d Lean { get; init; }

        public bool Keeps { get; init; } = true;

        public bool Allows { get; init; }

        public bool KeepsUnseenBody => Keeps;

        public bool AllowsIdleAnimation => Allows;

        public Vector3d EyeOffset => Lean;
    }

    private sealed class Holding : IGuardCombat
    {
        public bool Lets { get; init; }

        public bool Fights { get; init; }

        public int Asked { get; private set; }

        public int Fought { get; private set; }

        public bool MayFire
        {
            get
            {
                Asked++;
                return Lets;
            }
        }

        public bool Fight(Vector3d enemy)
        {
            Fought++;
            return Fights;
        }
    }

    private sealed class Throwing : IGuardSenses
    {
        public bool? Stimulus(ref GuardStimulus stimulus) => throw new InvalidOperationException("broken");
    }
}
