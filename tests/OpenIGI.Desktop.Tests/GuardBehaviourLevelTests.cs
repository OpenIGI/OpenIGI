using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.TestSupport;
using Xunit;
using static OpenIGI.Desktop.Tests.SoldierRendererScenario;
using Scenario = OpenIGI.Desktop.Tests.SoldierRendererScenario;

namespace OpenIGI.Desktop.Tests;

/// <summary>A level's guards, every one given behaviours that say nothing, play exactly as they do with none.</summary>
[Collection(SoldierRendererScenario.CollectionName)]
public sealed class GuardBehaviourLevelTests
{
    private const int Ticks = 600;

    [RequiresGameDataFact]
    public void Every_guard_given_behaviours_that_say_nothing_plays_as_with_none()
    {
        List<(int Id, Vector3d Position, float Facing, string? Action)> plain = Play(attach: false, out int guards, out int moved);
        List<(int Id, Vector3d Position, float Facing, string? Action)> given = Play(attach: true, out int attached, out _);

        Assert.True(guards > 0, "the level has no guards");
        Assert.True(moved > 0, "no guard moved, so the comparison says nothing");
        Assert.Equal(guards, attached);
        Assert.Equal(plain, given);
    }

    // Every guard of mission one, ticked headless; with the behaviours, each guard has both of them.
    private static List<(int Id, Vector3d Position, float Facing, string? Action)> Play(bool attach, out int attached, out int moved)
    {
        using Scenario scenario = CreateScenario(prepareContainers: true, missionNumber: 1);
        SoldierSimulation soldiers = scenario.Renderer.Simulation;
        attached = 0;
        if (attach)
        {
            soldiers.AttachGuardBehaviour(static _ => new SaysNothing());
            soldiers.AttachGuardBehaviour(static _ => new SaysNothing());
            attached = soldiers.Brains.Values.Count(static brain => brain.BehaviourOf<SaysNothing>() is not null);
        }

        int guards = soldiers.Brains.Count;
        Dictionary<Igi1HumanAi, Vector3d> start = soldiers.Brains.Values.ToDictionary(static brain => brain, static brain => brain.Position);
        for (int tick = 0; tick < Ticks; tick++)
        {
            scenario.Renderer.Tick(enemyAlive: false);
        }

        if (!attach)
        {
            attached = guards;
        }

        moved = soldiers.Brains.Values.Count(brain => start.TryGetValue(brain, out Vector3d from) && Vector3d.Distance(from, brain.Position) > 4096.0);

        return [.. soldiers.Brains
            .Select(static pair => (pair.Key.Soldier.Id, pair.Value.Position, pair.Value.Facing, pair.Value.RunningActionName))
            .OrderBy(static guard => guard.Id)];
    }

    private sealed class SaysNothing : IGuardLife, IGuardSenses, IGuardMovement, IGuardCombat
    {
    }
}
