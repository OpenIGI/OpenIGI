using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Time;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Project I.G.I.'s player as any game's AI perceives it.</summary>
public sealed class Igi1PlayerBodyTests
{
    [Fact]
    public void A_player_its_container_holds_out_of_the_world_is_no_target_until_it_is_put_in()
    {
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        ConditionalContainerTask container = (ConditionalContainerTask)loop.Tasks.New(
            loop.Tasks.Root, loop.Types.Get(ConditionalContainerTask.TypeName));
        HumanPlayerTask task = (HumanPlayerTask)loop.Tasks.New(container, loop.Types.Get("HumanPlayer"));
        MissionWorld mission = new() { Player = new PlayerCamera(), PlayerTask = task };
        task.Body = new Igi1PlayerBody(mission);

        container.IsActive = false;
        Assert.False(task.DetectionInfo().IsActive);

        container.IsActive = true;
        Assert.True(task.DetectionInfo().IsActive);
    }
}
