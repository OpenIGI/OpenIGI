using OpenIGI.Core.Numerics;
using OpenIGI.Game.Flow;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Steps a level's flow tasks on their own, for tests that isolate what the objectives' expressions bind to: every area,
/// counter, objective definition, status message and level flow in the tree's order, whatever container it stands in.
/// </summary>
/// <remarks>
/// The game has one path -- the level's walk, which runs a task only where its containers are open. These tests feed
/// deaths and occupants to a retail level without playing it, so no script ever opens the containers their tasks stand in;
/// this runs those tasks' own handlers directly instead. It is a test's shortcut and no part of the runtime.
/// </remarks>
internal static class FlowAlone
{
    /// <summary>Feeds the player's place to the areas, counts each area and runs each counter.</summary>
    public static void StepLevelState(
        this MissionObjectiveRuntime objectives,
        Vector3d? playerPosition = null,
        int playerTeam = 0,
        Vector3d? extraPlayerPosition = null,
        bool playerAlive = true)
    {
        objectives.SetLevelStateInputs(playerPosition, playerTeam, extraPlayerPosition, playerAlive);
        LevelUpdateWalker walk = objectives.OwnWalk;
        foreach (AreaActivateTask area in walk.Root.Descendants().OfType<AreaActivateTask>().ToArray())
        {
            area.Recount(walk);
        }

        foreach (EditVariableTask edit in walk.Root.Descendants().OfType<EditVariableTask>().ToArray())
        {
            edit.Run(walk);
        }
    }

    /// <summary>Runs each objective definition and status message, closes the objectives' tick, then concludes each level flow.</summary>
    public static void StepAfterLevelState(this MissionObjectiveRuntime objectives)
    {
        LevelUpdateWalker walk = objectives.OwnWalk;
        foreach (DefineComputerObjectiveTask definition in walk.Root.Descendants().OfType<DefineComputerObjectiveTask>().ToArray())
        {
            definition.Run(walk);
        }

        foreach (Igi1StatusMessageTask message in walk.Root.Descendants().OfType<Igi1StatusMessageTask>().ToArray())
        {
            message.Run(walk);
        }

        objectives.TickAfterLevelState();
        foreach (LevelFlowTask flow in walk.Root.Descendants().OfType<LevelFlowTask>().ToArray())
        {
            flow.Conclude(walk);
        }
    }

    /// <summary>One whole step: the level's state, then what follows it.</summary>
    public static void StepFlow(this MissionObjectiveRuntime objectives, Vector3d? playerPosition = null)
    {
        objectives.StepLevelState(playerPosition);
        objectives.StepAfterLevelState();
    }
}
