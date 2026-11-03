using OpenIGI.Host.Editor;
using OpenIGI.Presentation.Cameras;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Actions;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>
/// The editor's preferences, as scene editors gather theirs: the look, how the viewport behaves, and every shortcut, each
/// rebindable. Everything is a remembered setting, saved as it changes.
/// </summary>
internal sealed class PreferencesPanel : EditorElement
{
    private static readonly string[] Themes = ["Dark", "Light"];

    private readonly PropertyGrid _grid;
    private readonly ActionTable _actions;
    private readonly IEditorHost _host;
    private readonly Action<string, bool> _notify;

    public PreferencesPanel(EditorTheme theme, ActionTable actions, IEditorHost host, Action<string, bool> notify)
        : base(theme)
    {
        _actions = actions;
        _host = host;
        _notify = notify;
        _grid = new PropertyGrid(theme);
        AddChild(_grid);
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        // Built when first shown, by which time every action is registered.
        if (_grid.Count == 0)
        {
            Build();
        }
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        _grid.Measure(context, available);
        return available;
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect) => _grid.Arrange(contentRect);

    private void Build()
    {
        _grid.Section("General");
        _grid.Choice("Theme", Themes, () => EditorCVars.LightTheme.Value ? 1 : 0, choice => EditorCVars.LightTheme.Value = choice == 1);
        NumberField scale = _grid.Number(
            "Interface Size",
            () => EditorCVars.UiScale.Value,
            (value, _) => EditorCVars.UiScale.Value = (float)value,
            "0 sizes the editor from the screen; otherwise a multiple of its normal size.");
        scale.Minimum = 0;
        scale.Maximum = 2.5;
        scale.Step = 0.05;
        scale.Decimals = 2;

        _grid.Section("Scene View");
        NumberField speed = _grid.Number(
            "Camera Speed",
            () => _host.Camera?.Speed ?? 0f,
            (value, _) =>
            {
                if (_host.Camera is { } camera)
                {
                    camera.Speed = (float)Math.Clamp(value, FreeCamera.MinimumSpeed, FreeCamera.MaximumSpeed);
                }
            },
            "Metres a second while flying (right mouse and W A S D); the wheel changes it while flying.");
        speed.Minimum = FreeCamera.MinimumSpeed;
        speed.Maximum = FreeCamera.MaximumSpeed;
        speed.Decimals = 1;
        NumberField sensitivity = _grid.Number("Mouse Sensitivity", () => EditorCVars.MouseSensitivity.Value, (value, _) => EditorCVars.MouseSensitivity.Value = (float)value, "How fast the mouse turns the camera, as a multiple of the normal rate.");
        sensitivity.Minimum = 0.1;
        sensitivity.Maximum = 5;
        sensitivity.Step = 0.05;
        sensitivity.Decimals = 2;
        NumberField smoothing = _grid.Number("Camera Smoothing", () => EditorCVars.CameraSmoothing.Value, (value, _) => EditorCVars.CameraSmoothing.Value = (float)value, "Seconds the camera takes to catch up when flying; 0 moves it at once.");
        smoothing.Minimum = 0;
        smoothing.Maximum = 0.5;
        smoothing.Step = 0.01;
        smoothing.Decimals = 2;
        _grid.Bool("Invert Pan", () => EditorCVars.InvertPan.Value, value => EditorCVars.InvertPan.Value = value, "Middle-button panning moves the view the way the mouse goes, rather than dragging the world.");
        NumberField gizmo = _grid.Number("Gizmo Size", () => EditorCVars.GizmoScale.Value, (value, _) => EditorCVars.GizmoScale.Value = (float)value, "How large the move and rotate gizmos are drawn.");
        gizmo.Minimum = 0.5;
        gizmo.Maximum = 3;
        gizmo.Step = 0.05;
        gizmo.Decimals = 2;
        NumberField grid = _grid.Number("Grid Step", () => EditorCVars.GridStep.Value, (value, _) => EditorCVars.GridStep.Value = (float)value, "Metres: what moving snaps to, and the viewport grid's spacing.");
        grid.Minimum = 0.0625;
        grid.Maximum = 64;
        grid.Step = 0.0625;
        grid.Decimals = 4;
        NumberField angle = _grid.Number("Angle Step", () => EditorCVars.AngleStep.Value, (value, _) => EditorCVars.AngleStep.Value = (float)value, "Degrees: what turning snaps to.");
        angle.Minimum = 1;
        angle.Maximum = 90;
        angle.Decimals = 1;
        _grid.Bool("Snap to Grid", () => EditorCVars.GridSnap.Value, value => EditorCVars.GridSnap.Value = value);
        _grid.Bool("Snap Angles", () => EditorCVars.AngleSnap.Value, value => EditorCVars.AngleSnap.Value = value);
        _grid.Bool("Local Space", () => EditorCVars.LocalSpace.Value, value => EditorCVars.LocalSpace.Value = value);
        _grid.Bool("Show Grid", () => EditorCVars.ShowGrid.Value, value => EditorCVars.ShowGrid.Value = value);
        _grid.Bool("Show Gizmos", () => EditorCVars.ShowGizmos.Value, value => EditorCVars.ShowGizmos.Value = value);
        _grid.Bool("Move Contents With Objects", () => EditorCVars.MoveContents.Value, value => EditorCVars.MoveContents.Value = value);

        // A section of shortcuts per menu, each one a field that rebinds on a click and a key press.
        foreach (IGrouping<string, EditorAction> category in _actions.All
            .Where(static a => a.Category.Length > 0 && a.Id != "edit.redo-alt")
            .GroupBy(static a => a.Category)
            .OrderBy(static g => g.Key, StringComparer.Ordinal))
        {
            _grid.Section($"Keys: {category.Key}");
            foreach (EditorAction action in category.OrderBy(static a => a.Title, StringComparer.Ordinal))
            {
                KeyChordField field = new(Theme) { Tooltip = $"{action.Title}. Click, then press the keys." };
                field.Changed += chord => Rebind(action, chord);
                _grid.Add(action.Title, field, () =>
                {
                    field.Chord = action.Shortcut;
                    field.IsModified = action.Shortcut != _actions.DefaultShortcut(action.Id);
                });
                _grid.Revertible(
                    () => action.Shortcut != _actions.DefaultShortcut(action.Id),
                    () => Rebind(action, _actions.DefaultShortcut(action.Id)));
            }
        }

        _grid.RevertAll = () =>
        {
            _actions.ResetBindings();
            EditorCVars.KeyBindings.Value = string.Empty;
            _notify("Every shortcut is back to its default.", false);
        };
    }

    private void Rebind(EditorAction action, KeyChord? chord)
    {
        IReadOnlyList<EditorAction> clashes = _actions.Rebind(action.Id, chord);
        EditorCVars.KeyBindings.Value = _actions.SaveBindings();
        if (clashes.Count > 0)
        {
            _notify($"{chord} is also {string.Join(", ", clashes.Select(static c => c.Title))}; whichever is listed first runs.", true);
        }
    }
}
