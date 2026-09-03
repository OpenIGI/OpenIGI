using OpenIGI.Desktop;
using Xunit;

namespace OpenIGI.Desktop.Tests;

public sealed class MissionResultArbitrationTests
{
    [Fact]
    public void PlayerDeathFailsImmediatelyAndOverridesSameTickCompletion()
    {
        MissionResultDecision decision = MissionResultArbitration.Evaluate(
            playerDead: true,
            objectivesFailed: false,
            objectivesComplete: true,
            failureMenuOpen: false,
            resultHandled: false,
            failureTicks: 0);

        Assert.Equal(MissionResult.PlayerDeathFailure, decision.Result);
        Assert.False(decision.ShouldHandleCompletion);
        Assert.Equal(0, decision.FailureTicks);
    }

    [Fact]
    public void AuthoredFailureKeepsTheOriginalOneHundredAndFiftyOneTickGracePeriod()
    {
        MissionResultDecision graceTick = MissionResultArbitration.Evaluate(
            playerDead: false,
            objectivesFailed: true,
            objectivesComplete: false,
            failureMenuOpen: false,
            resultHandled: false,
            failureTicks: 150);
        MissionResultDecision failureTick = MissionResultArbitration.Evaluate(
            playerDead: false,
            objectivesFailed: true,
            objectivesComplete: false,
            failureMenuOpen: false,
            resultHandled: false,
            failureTicks: graceTick.FailureTicks);

        Assert.Equal(MissionResult.None, graceTick.Result);
        Assert.Equal(151, graceTick.FailureTicks);
        Assert.Equal(MissionResult.AuthoredFailure, failureTick.Result);
        Assert.Equal(152, failureTick.FailureTicks);
    }
}
