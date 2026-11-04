using OpenIGI.Editor.Terrain;
using OpenIGI.UI.Editor.Actions;

namespace OpenIGI.Editor;

/// <summary>Laying new sculptable ground, for a title whose ground can only be shaped in places.</summary>
internal sealed partial class EditorSession
{
    private void RegisterTerrainActions(ActionTable actions)
    {
        EditorAction addArea = new("terrain.addarea", "Add Sculptable Area Here", AddTerrainArea)
        {
            Category = "Tools",
            CanExecute = () => _workspace.Document is not null && _host.Terrain is { IsPartial: true },
        };
        actions.Add(addArea);
        _terrainPanel.AddAreaAction = addArea;

        EditorAction addMountain = new("terrain.addmountain", "Add Mountain Area Here", AddMountainArea)
        {
            Category = "Tools",

            // The original game loads a mission's own rebuilt octree (MISSION:terrain/terrain.ctr), so a retail mission may
            // have them too, as long as the whole octree stays within the original's 16-bit child index.
            CanExecute = () => _workspace.Document is not null && _host.Terrain is { IsPartial: true },
        };
        actions.Add(addMountain);
        _terrainPanel.AddMountainAction = addMountain;
    }

    // A 512 m square around the middle of the view whose ground is built again with no height limit, as one step.
    private void AddMountainArea()
    {
        if (_host.Terrain is not { IsPartial: true } terrain || _viewport.PlacementPoint() is not { } at)
        {
            return;
        }

        AddMountainCommand command = new(terrain, at);
        if (!command.TryApply())
        {
            Notify("The middle of the view is in a mountain area already, there is no ground under it, or a heightmap there reaches past its square.", isError: true);
            return;
        }

        _workspace.History.Record(command);
        _viewport.Tool = Panels.ViewportTool.Terrain;
        _dock.Show("terrain");
        Notify("Added a mountain area, outlined on the ground: shape it as high as you like inside the outline. Save to keep it.");
    }

    // A grid over the ground in the middle of the view, and the task that keeps it, as one step.
    private void AddTerrainArea()
    {
        if (_workspace.Document is not { } document || _host.Terrain is not { IsPartial: true } terrain || _viewport.PlacementPoint() is not { } at)
        {
            return;
        }

        if (terrain.AddArea(at) is not { } area)
        {
            Notify("The middle of the view is on sculptable ground already, or there is no ground under it.", isError: true);
            return;
        }

        _workspace.Execute(AddTerrainAreaCommand.For(document, terrain, area));
        _viewport.Tool = Panels.ViewportTool.Terrain;
        _dock.Show("terrain");
        Notify($"Added a sculptable area ({area.Name}), outlined on the ground. Save to keep it with the mission.");
    }
}
