using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>An AI event sent down the level's human list, as the engine's broadcast sends it.</summary>
public sealed class HumanAiEventsTests
{
    [Fact]
    public void Every_human_takes_it_in_list_order_but_one_that_is_both_its_source_and_its_subject()
    {
        List<string> heard = [];
        Listener shooter = new("shooter", heard), near = new("near", heard), far = new("far", heard);

        HumanAiEvents.Send([near, shooter, far], new HumanAiEvent(1, -1, 0, 0f, null, shooter, shooter, null));
        Assert.Equal(["near", "far"], heard);

        heard.Clear();
        HumanAiEvents.Send([near, shooter, far], new HumanAiEvent(1, -1, 0, 0f, null, shooter, far, null));
        Assert.Equal(["near", "shooter", "far"], heard);
    }

    private sealed class Listener(string name, List<string> heard) : GameTask, IHumanAiEventTarget
    {
        public void SendEvent(in HumanAiEvent aiEvent) => heard.Add(name);
    }
}
