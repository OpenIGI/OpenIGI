using System.Globalization;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Editor.Viewport;
using OpenIGI.UI.Editor.Actions;

namespace OpenIGI.Editor;

/// <summary>
/// Fence runs: the selected fence panel carried on toward where the view looks, panels end to end along the line, each a
/// copy of it (its model, its electric expression), turned so its long side follows the line and stood on the ground.
/// </summary>
internal sealed partial class EditorSession
{
    private const string FenceType = "Fence";

    private void RegisterFenceActions(ActionTable actions) =>
        actions.Add(new EditorAction("tools.fencerun", "Run Fence to Here", () => RunFence(null))
        {
            Category = "Tools",
            CanExecute = () => _workspace.Selection.Primary is { TypeName: FenceType },
        });

    /// <summary>Runs the selected fence panel on toward where the view looks, or as far as an offset from it.</summary>
    /// <param name="offset">How far east and north of the panel the run goes, in metres; null for the middle of the view.</param>
    private void RunFence(Vector3d? offset)
    {
        if (_workspace.Document is not { } document || _workspace.Selection.Primary is not { TypeName: FenceType } panel
            || TaskPresentation.Position(panel) is not { } position || !position.TryGetNumbers(out double[] at) || at.Length < 3
            || (offset is { } by ? new Vector3d((at[0] / WorldUnits.PerMetre) + by.X, (at[1] / WorldUnits.PerMetre) + by.Y, 0) : _viewport.PointAtCentre()) is not { } target || _live.Boxes(panel).ToArray() is not [var box, ..])
        {
            Notify("Select a fence panel the level draws, and look where the fence should run to.", isError: true);
            return;
        }

        Vector3d half = box.Half;
        Vector3d start = new(at[0] / WorldUnits.PerMetre, at[1] / WorldUnits.PerMetre, at[2] / WorldUnits.PerMetre);
        IReadOnlyList<FencePanel> panels = FenceRun.Layout(start.X, start.Y, half.X, half.Y, target.X, target.Y);
        if (panels.Count < 2)
        {
            Notify("Look further along: the fence runs to the point in the middle of the view, a panel's length at least away.", isError: true);
            return;
        }

        // Each panel as high over the ground as the first stands over it.
        double? Ground(double x, double y) => _host.Terrain is { } terrain && terrain.TryGetHeight(x, y, out double z) ? z : null;
        double lift = Ground(start.X, start.Y) is { } under ? start.Z - under : 0;
        List<IEditorCommand> steps = [Turn(document, panel, panels[0].Gamma)];
        TaskLocation place = document.Locate(panel);
        for (int i = 1; i < panels.Count; i++)
        {
            FencePanel next = panels[i];
            MissionTask copy = panel.Clone();
            double z = (Ground(next.X, next.Y) ?? start.Z - lift) + lift;
            TaskPresentation.Position(copy)!.SetNumbers([next.X * WorldUnits.PerMetre, next.Y * WorldUnits.PerMetre, z * WorldUnits.PerMetre]);
            TaskPresentation.Orientation(copy)?.SetNumbers([next.Gamma]);
            steps.Add(new InsertTaskCommand(document, copy, place with { Index = place.Index + i }));
        }

        _workspace.Execute(new CompositeCommand("Run Fence", steps));
        _host.Logger.Information("Editor", string.Create(CultureInfo.InvariantCulture, $"Ran a fence of {panels.Count} panels from ({start.X:0.##}, {start.Y:0.##}) to ({target.X:0.##}, {target.Y:0.##}), each {2 * Math.Max(half.X, half.Y):0.##} m, Gamma {panels[0].Gamma:0.###} (box half {half.X:0.##} x {half.Y:0.##})."));
        Notify(string.Create(CultureInfo.InvariantCulture, $"Ran the fence on {panels.Count - 1} panel(s), {2 * Math.Max(half.X, half.Y):0.#} m each."));
    }

    private static IEditorCommand Turn(MissionDocument document, MissionTask panel, double gamma) =>
        TaskPresentation.Orientation(panel) is { } orientation
            ? SetParameterCommand.Numbers(document, panel, orientation.Name, [gamma])
            : new CompositeCommand("Turn", []);
}
