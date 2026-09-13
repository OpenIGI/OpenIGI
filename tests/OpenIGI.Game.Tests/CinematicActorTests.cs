using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Time;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

public sealed class CinematicActorTests
{
    [Theory]
    [InlineData("CutScene_702.isRun && !CutScene_702.isFinished", true)]
    [InlineData("CutScene_701.isFinished && !CutScene_702.isFinished", true)]
    [InlineData("(CutScene_702.isRun)", true)]
    [InlineData("CutScene_701.isFinished", false)]
    [InlineData("!CutScene_702.isRun", false)]
    [InlineData("! CutScene_702.isRun", false)]
    [InlineData("EditVariable_1001.nValue == 1", false)]
    public void ScriptGatesDistinguishActiveCinematicsFromOrdinaryPostIntroContent(
        string condition, bool expected)
    {
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        ConditionalContainerTask container = (ConditionalContainerTask)loop.Tasks.New(
            loop.Tasks.Root, loop.Types.Get(ConditionalContainerTask.TypeName));
        container.Condition = condition;
        HumanSoldierTask human = (HumanSoldierTask)loop.Tasks.New(
            container, loop.Types.Get("HumanSoldier"));

        Assert.Equal(expected, CinematicActor.IsStaged(human));
    }
}
