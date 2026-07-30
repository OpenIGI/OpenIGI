using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Covers the waypoint steering step and the patrol step machine's state rules.</summary>
public sealed class AiMovementStepTests
{
    private static readonly Matrix3f FacingY = new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    /// <summary>A distant waypoint keeps the AI travelling and writes full forward into the frame.</summary>
    [Fact]
    public void ADistantWaypointKeepsTravelling()
    {
        var step = new AiMovementStep();

        var outcome = step.Step(
            default, FacingY, new Vector3d(0, 40_000, 0), 0.0f, 0.0f, out AiSteeringFrame frame);

        Assert.Equal(AiMovementOutcome.Travelling, outcome);
        Assert.Equal(AiMovementStep.ForwardFull, frame.Forward);
        Assert.Equal(0.0f, frame.Turn);
    }

    /// <summary>
    /// The arrival test is a <b>box on the two horizontal axes</b>, not a radius — so a point at the threshold
    /// on both axes (further away than the threshold in a straight line) still counts as reached.
    /// </summary>
    [Fact]
    public void ArrivalIsABoxNotARadius()
    {
        var step = new AiMovementStep();
        const double t = AiMovementStep.ArrivalThreshold;

        // On the threshold in both axes: distance is t*sqrt(2), well outside a radius of t, but inside the box.
        Assert.Equal(
            AiMovementOutcome.WaypointReached,
            step.Step(default, FacingY, new Vector3d(t, t, 0), 0, 0, out _));

        // A hair over on one axis and it is not reached.
        Assert.Equal(
            AiMovementOutcome.Travelling,
            step.Step(default, FacingY, new Vector3d(t + 0.1, 0, 0), 0, 0, out _));
    }

    /// <summary>Z is ignored entirely — a waypoint straight overhead counts as reached.</summary>
    [Fact]
    public void HeightIsIgnored()
    {
        var step = new AiMovementStep();

        Assert.Equal(
            AiMovementOutcome.WaypointReached,
            step.Step(default, FacingY, new Vector3d(0, 0, 100_000), 0, 0, out _));
    }

    /// <summary>The steering angles are divided by π on the way into the frame.</summary>
    [Fact]
    public void TheAnglesAreScaledIntoTheFrame()
    {
        var step = new AiMovementStep();

        step.Step(default, FacingY, new Vector3d(0, 40_000, 0), 1.0f, -0.5f, out AiSteeringFrame frame);

        Assert.Equal(1.0f * AiMovementStep.AngleToFrame, frame.Turn);
        Assert.Equal(-0.5f * AiMovementStep.AngleToFrame, frame.Pitch);
    }

    /// <summary>
    /// The clamps are what make one steering routine serve both "walk over there" and "aim at that": the
    /// move-to-node entry sets them to π, the look-at entry to about 1.47°.
    /// </summary>
    [Fact]
    public void TheClampsAreSymmetricAndBound()
    {
        var loose = new AiMovementStep { YawClamp = MathF.PI, PitchClamp = MathF.PI };
        loose.Step(default, FacingY, new Vector3d(0, 40_000, 0), 10.0f, -10.0f, out AiSteeringFrame wide);
        Assert.Equal(MathF.PI * AiMovementStep.AngleToFrame, wide.Turn, 5);
        Assert.Equal(-MathF.PI * AiMovementStep.AngleToFrame, wide.Pitch, 5);

        var tight = new AiMovementStep { YawClamp = 0.0257f, PitchClamp = 0.0257f };
        tight.Step(default, FacingY, new Vector3d(0, 40_000, 0), 10.0f, -10.0f, out AiSteeringFrame narrow);
        Assert.Equal(0.0257f * AiMovementStep.AngleToFrame, narrow.Turn, 6);
        Assert.Equal(-0.0257f * AiMovementStep.AngleToFrame, narrow.Pitch, 6);

        // A value inside the clamp passes through untouched.
        tight.Step(default, FacingY, new Vector3d(0, 40_000, 0), 0.01f, 0.01f, out AiSteeringFrame inside);
        Assert.Equal(0.01f * AiMovementStep.AngleToFrame, inside.Turn, 6);
    }

    /// <summary>The steering is relative to the AI's own facing, so a turned AI reads the waypoint differently.</summary>
    [Fact]
    public void SteeringIsInTheAisOwnFrame()
    {
        var step = new AiMovementStep();

        // Yawed 90 degrees: local Y points along world +X.
        var yawed = new Matrix3f(0, 1, 0, -1, 0, 0, 0, 0, 1);

        // A waypoint at world +Y is 40000 to the AI's local -X, so still travelling.
        Assert.Equal(
            AiMovementOutcome.Travelling,
            step.Step(default, yawed, new Vector3d(0, 40_000, 0), 0, 0, out _));

        // One close by in both world axes is inside the box on both local axes.
        Assert.Equal(
            AiMovementOutcome.WaypointReached,
            step.Step(default, yawed, new Vector3d(1000, 1000, 0), 0, 0, out _));
    }

    /// <summary>A patrol walks by default, and the gait outlives the command that set it.</summary>
    [Fact]
    public void APatrolWalksByDefaultAndTheGaitPersists()
    {
        AiPatrolRoute route = Route(
            new AiPatrolCommand(AiPatrolCommandKind.RunTo, 1),
            new AiPatrolCommand(AiPatrolCommandKind.Delay, 5),
            new AiPatrolCommand(AiPatrolCommandKind.WalkTo, 2));

        Assert.True(route.IsWalking);

        route.Begin(0);
        Assert.False(route.IsWalking);

        // A delay does not change the gait - the AI is still "running" while it waits.
        route.Advance();
        route.Begin(0);
        Assert.False(route.IsWalking);

        route.Advance();
        route.Begin(0);
        Assert.True(route.IsWalking);
    }

    /// <summary>Both movement kinds clear the crouch flag; only <c>CROUCH</c> sets it, and it never waits.</summary>
    [Fact]
    public void OnlyCrouchSetsTheCrouchFlagAndItAdvancesAtOnce()
    {
        AiPatrolRoute route = Route(
            new AiPatrolCommand(AiPatrolCommandKind.Crouch, -1),
            new AiPatrolCommand(AiPatrolCommandKind.WalkTo, 1),
            new AiPatrolCommand(AiPatrolCommandKind.Crouch, -1),
            new AiPatrolCommand(AiPatrolCommandKind.RunTo, 2));

        Assert.True(route.Begin(0));
        Assert.True(route.IsCrouching);

        route.Advance();
        Assert.False(route.Begin(0));
        Assert.False(route.IsCrouching);

        route.Advance();
        route.Begin(0);
        Assert.True(route.IsCrouching);

        route.Advance();
        Assert.False(route.Begin(0));
        Assert.False(route.IsCrouching);
    }

    /// <summary>The deadline doubles as "has this command begun", and advancing resets it.</summary>
    [Fact]
    public void TheDeadlineDoublesAsAStartedFlag()
    {
        AiPatrolRoute route = Route(
            new AiPatrolCommand(AiPatrolCommandKind.Delay, 5),
            new AiPatrolCommand(AiPatrolCommandKind.Delay, 5));

        Assert.Equal(AiPatrolRoute.NoDeadline, route.Deadline);

        // The deadline is computed once, from the tick the command began on.
        Assert.False(route.Begin(100));
        Assert.Equal(105, route.Deadline);
        Assert.False(route.Begin(103));
        Assert.Equal(105, route.Deadline);

        // Strictly greater, so a delay of five occupies six ticks.
        Assert.False(route.Begin(105));
        Assert.True(route.Begin(106));

        route.Advance();
        Assert.Equal(AiPatrolRoute.NoDeadline, route.Deadline);
    }

    /// <summary>Kind 6 is <c>END</c> — the loop marker — and kind 8 is the one a human cannot execute.</summary>
    [Fact]
    public void KindSixIsEndAndKindEightIsTheUnexecutableOne()
    {
        Assert.Equal(6, (int)AiPatrolCommandKind.End);
        Assert.Equal(7, (int)AiPatrolCommandKind.Quit);
        Assert.Equal(8, (int)AiPatrolCommandKind.SetSpeed);
        Assert.Equal(-1, (int)AiPatrolCommandKind.None);

        // Every kind but SetSpeed has an arm in the human walker; END never reaches the switch at all.
        foreach (AiPatrolCommandKind kind in Enum.GetValues<AiPatrolCommandKind>())
        {
            bool expected = kind is not (AiPatrolCommandKind.SetSpeed
                or AiPatrolCommandKind.End
                or AiPatrolCommandKind.None);
            Assert.Equal(expected, kind.IsExecutableByAHuman());
        }
    }

    /// <summary>The two named tolerances are the angles the original spells out.</summary>
    [Fact]
    public void TheTolerancesAreThreeDegreesAndTwo()
    {
        Assert.Equal(3.0, AiPatrolRoute.LookAtToleranceRadians / AiViewCone.DegreesToRadians, 3);
        Assert.Equal(2.0, AiMovementStep.DoorFacingTolerance / AiViewCone.DegreesToRadians, 3);
        Assert.Equal(0.3, AiMovementStep.ArrivalThreshold / 4096.0, 6);
    }

    private static AiPatrolRoute Route(params AiPatrolCommand[] commands) =>
        AiPatrolRoute.Start(new AiPatrolPath(1, commands))
        ?? throw new InvalidOperationException("the path has no startable command");
}
