using System.Globalization;
using OpenIGI.Host.Editor;
using OpenIGI.UI.Editor.Actions;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Kit;

namespace OpenIGI.Editor;

/// <summary>The viewport's toolbar and the actions behind it: space, snapping, gizmos and the maximised view.</summary>
internal sealed partial class EditorSession
{
    private static readonly double[] GridSteps = [0.125, 0.25, 0.5, 1, 2, 4, 8, 16];
    private static readonly double[] AngleSteps = [5, 10, 15, 22.5, 30, 45, 90];

    // The layout to go back to while the viewport fills the window (F3); null while it does not.
    private string? _maximizedLayout;

    private void RegisterVisibilityActions(ActionTable actions)
    {
        actions.Add(new EditorAction("edit.hide", "Hide / Show", () =>
        {
            IReadOnlyList<Core.Missions.MissionTask> selected = [.. _workspace.Selection.Items];
            _live.SetHidden(selected, hidden: !selected.All(_live.IsHidden));
        })
        {
            Category = "Edit",
            Shortcut = KeyChord.Parse("H"),
            CanExecute = () => _workspace.Selection.Count > 0,
        });
        actions.Add(new EditorAction("edit.show-all", "Show All", () => _live.SetHidden([.. _workspace.Document?.EnumerateTasks() ?? []], hidden: false))
        {
            Category = "Edit",
            Shortcut = KeyChord.Parse("Alt+H"),
            CanExecute = () => _workspace.Document is not null,
        });
        actions.Add(new EditorAction("edit.isolate", "Isolate Selection", () =>
        {
            _live.Isolate(_live.IsIsolating ? null : [.. _workspace.Selection.Items]);
        })
        {
            Category = "Edit",
            Shortcut = KeyChord.Parse("I"),
            CanExecute = () => _workspace.Selection.Count > 0 || _live.IsIsolating,
            IsChecked = () => _live.IsIsolating,
        });
    }

    private void RegisterViewportActions(ActionTable actions)
    {
        actions.Add(new EditorAction("view.local-space", "Local Space", () => EditorCVars.LocalSpace.Value = !EditorCVars.LocalSpace.Value)
        {
            Category = "View",
            DynamicTitle = () => EditorCVars.LocalSpace.Value ? "Local" : "World",
            IsChecked = () => EditorCVars.LocalSpace.Value,
        });
        actions.Add(new EditorAction("view.grid-snap", "Grid Snap", () => EditorCVars.GridSnap.Value = !EditorCVars.GridSnap.Value)
        {
            Category = "View",
            IsChecked = () => EditorCVars.GridSnap.Value,
        });
        actions.Add(new EditorAction("view.grid-step", "Grid Step", () => OpenStepMenu(GridSteps, EditorCVars.GridStep.Value, step => EditorCVars.GridStep.Value = (float)step, Metres))
        {
            Category = "View",
            DynamicTitle = () => Metres(EditorCVars.GridStep.Value),
        });
        actions.Add(new EditorAction("view.angle-snap", "Angle Snap", () => EditorCVars.AngleSnap.Value = !EditorCVars.AngleSnap.Value)
        {
            Category = "View",
            IsChecked = () => EditorCVars.AngleSnap.Value,
        });
        actions.Add(new EditorAction("view.angle-step", "Angle Step", () => OpenStepMenu(AngleSteps, EditorCVars.AngleStep.Value, step => EditorCVars.AngleStep.Value = (float)step, Degrees))
        {
            Category = "View",
            DynamicTitle = () => Degrees(EditorCVars.AngleStep.Value),
        });
        actions.Add(new EditorAction("view.grid", "Show Grid", () => EditorCVars.ShowGrid.Value = !EditorCVars.ShowGrid.Value)
        {
            Category = "View",
            IsChecked = () => EditorCVars.ShowGrid.Value,
        });
        actions.Add(new EditorAction("view.gizmos", "Show Gizmos", () => EditorCVars.ShowGizmos.Value = !EditorCVars.ShowGizmos.Value)
        {
            Category = "View",
            IsChecked = () => EditorCVars.ShowGizmos.Value,
        });
        actions.Add(new EditorAction("view.fullscreen", "Maximize Viewport", ToggleMaximized)
        {
            Category = "View",
            Shortcut = KeyChord.Parse("F3"),
            IsChecked = () => _maximizedLayout is not null,
        });
    }

    // The toolbar along the viewport's top, as scene editors have it: the tools, the gizmos' space and snapping, play, and
    // the maximised view.
    private Toolbar BuildViewportToolbar()
    {
        ActionTable actions = _root.Actions;
        Toolbar toolbar = new(_theme);
        toolbar.Add(actions["tool.move"], KitIcon.Move);
        toolbar.Add(actions["tool.rotate"], KitIcon.Rotate);
        toolbar.Add(actions["tool.scale"], KitIcon.Scale);
        toolbar.Add(actions["tool.terrain"], KitIcon.Terrain);
        toolbar.AddSeparator();
        toolbar.Add(actions["view.local-space"], KitIcon.Globe, showText: true);
        toolbar.AddSeparator();
        toolbar.Add(actions["view.grid-snap"], KitIcon.Grid);
        toolbar.Add(actions["view.grid-step"], KitIcon.None, showText: true);
        toolbar.Add(actions["view.angle-snap"], KitIcon.Rotate);
        toolbar.Add(actions["view.angle-step"], KitIcon.None, showText: true);
        toolbar.AddSeparator();
        toolbar.Add(actions["view.gizmos"], KitIcon.Eye);
        toolbar.AddSpring();
        toolbar.Add(actions["play.start"], KitIcon.Play, showText: true);
        toolbar.AddSpring();
        toolbar.Add(actions["view.fullscreen"], KitIcon.Image);
        return toolbar;
    }

    private void OpenStepMenu(double[] steps, double current, Action<double> choose, Func<double, string> name) =>
        MenuPopup.OpenAt(_root, _root.PointerPosition,
        [
            .. steps.Select(step => MenuItem.Command((Math.Abs(step - current) < 1e-6 ? "• " : "   ") + name(step), () => choose(step))),
        ]);

    private void ToggleMaximized()
    {
        if (_maximizedLayout is { } layout)
        {
            _maximizedLayout = null;
            _dock.LoadLayout(layout);
            return;
        }

        string saved = _dock.SaveLayout();
        _dock.LoadLayout("T(viewport*)");
        _maximizedLayout = saved;
    }

    private static string Metres(double metres) => metres.ToString("0.###", CultureInfo.InvariantCulture) + " m";

    private static string Degrees(double degrees) => degrees.ToString("0.#", CultureInfo.InvariantCulture) + "°";
}
