using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.UI.Editor.Actions;

namespace OpenIGI.Editor;

/// <summary>Navigation graphs built over the ground, for soldiers to walk.</summary>
internal sealed partial class EditorSession
{
    private void RegisterNavGraphActions(ActionTable actions) =>
        actions.Add(new EditorAction("ai.buildgraph", "Build AI Graph Here", () => BuildNavGraph(_viewport.PlacementPoint(), announce: true))
        {
            Category = "Tools",
            CanExecute = () => _workspace.Document?.Declarations.ContainsKey("AIGraph") == true && _host.Terrain is not null && _host.Title.Id == "igi1",
        });

    /// <summary>
    /// Builds a navigation graph over the ground round a point and adds its AIGraph task beside the player, as one step; the
    /// graph file is written when the mission is saved.
    /// </summary>
    /// <param name="centreMetres">The middle of the graph's square, in world metres; null builds nothing.</param>
    /// <param name="announce">Whether to say what was built.</param>
    /// <returns>The new AIGraph task, or null when nothing could be built there.</returns>
    private MissionTask? BuildNavGraph(Vector3d? centreMetres, bool announce)
    {
        if (centreMetres is not { } centre || _workspace.Document is not { } document || _workspace.Mission is not { } mission || _host.Terrain is not { } terrain)
        {
            return null;
        }

        if (NavGraphBuild.Build(document, terrain, new NavGraphRequest(centre)) is not { } built)
        {
            Notify("No AI graph could be built here: the mission does not declare AIGraph, has no free task id, or there is no walkable ground.", isError: true);
            return null;
        }

        // Beside the player when the mission has one, where soldiers and their graphs usually sit; else at the end.
        MissionTask? player = document.EnumerateTasks().FirstOrDefault(static t => t.TypeName == "HumanPlayer");
        TaskLocation at = player is not null
            ? document.Locate(player) with { Index = document.Locate(player).Index + 1 }
            : new TaskLocation(null, document.Statements.Count);
        _workspace.Graphs.Adopt(mission, built.Task.Id, built.File);
        _workspace.Execute(new InsertTaskCommand(document, built.Task, at));
        if (announce)
        {
            Notify($"Built AI graph {built.Task.Id}: {built.Nodes} nodes, {built.Links} links. A soldier walks it with HumanAI \"Graph ID\" {built.Task.Id}; save to write it.");
        }

        return built.Task;
    }
}
