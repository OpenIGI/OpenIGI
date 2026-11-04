using OpenIGI.Core.Configuration;

namespace OpenIGI.Host.Editor;

/// <summary>The editor's settings: preferences a person sets once and expects to find again, so all of them are archived.</summary>
[CVarContainer]
public static class EditorCVars
{
    /// <summary>How large the editor draws.</summary>
    /// <remarks>
    /// Zero picks a size from the screen (<see cref="Core.Graphics.UiScale.Desktop"/>): one-to-one up to 1440 rows, growing
    /// toward 4K, as a desktop tool is sized.
    /// </remarks>
    public static readonly NumericCVar<float> UiScale = CVar.Float(
        "ed_ui_scale",
        0f,
        0f,
        4f,
        CVarOptions.Archive,
        "How large the editor draws: 0 picks a size from the screen, otherwise a multiple of its normal size.");

    /// <summary>Whether the editor uses its light theme.</summary>
    public static readonly BoolCVar LightTheme = CVar.Bool(
        "ed_light_theme",
        false,
        CVarOptions.Archive,
        "Whether the editor uses its light theme rather than its dark one.");

    /// <summary>The shortcuts rebound in Preferences, as <c>action=keys</c> pairs; only those that differ from the defaults.</summary>
    public static readonly StringCVar KeyBindings = CVar.Text(
        "ed_keybinds",
        string.Empty,
        CVarOptions.Archive,
        "Editor shortcuts changed from their defaults, as action=keys separated by semicolons (keys empty for none).");

    /// <summary>The folder the last retail export was written to, which the next one's folder picker opens on.</summary>
    public static readonly StringCVar ExportFolder = CVar.Text(
        "ed_export_folder",
        string.Empty,
        CVarOptions.Archive,
        "The folder the last export for the retail game went to; the next export asks starting there.");

    /// <summary>The content browser's favourites, as game paths separated by semicolons; folders end in a slash.</summary>
    public static readonly StringCVar ContentFavourites = CVar.Text(
        "ed_content_favourites",
        string.Empty,
        CVarOptions.Archive,
        "The content browser's favourite files and folders, as game paths separated by semicolons (folders end in /).");

    /// <summary>How fast the mouse turns the viewport camera, as a multiple of the normal rate.</summary>
    public static readonly NumericCVar<float> MouseSensitivity = CVar.Float(
        "ed_mouse_sensitivity",
        1f,
        0.1f,
        5f,
        CVarOptions.Archive,
        "How fast the mouse turns the viewport camera, as a multiple of the normal rate.");

    /// <summary>How long the flying camera takes to catch up with the keys, in seconds; zero for none.</summary>
    public static readonly NumericCVar<float> CameraSmoothing = CVar.Float(
        "ed_camera_smoothing",
        0.06f,
        0f,
        0.5f,
        CVarOptions.Archive,
        "How long the viewport camera takes to catch up when flying, in seconds; 0 moves it at once.");

    /// <summary>Whether a middle-button pan moves the view with the mouse rather than the world.</summary>
    public static readonly BoolCVar InvertPan = CVar.Bool(
        "ed_invert_pan",
        false,
        CVarOptions.Archive,
        "Whether panning with the middle button moves the view the way the mouse goes, rather than dragging the world.");

    /// <summary>How large the gizmos are, as a multiple of their normal size.</summary>
    public static readonly NumericCVar<float> GizmoScale = CVar.Float(
        "ed_gizmo_size",
        1f,
        0.5f,
        3f,
        CVarOptions.Archive,
        "How large the viewport's gizmos are drawn, as a multiple of their normal size.");

    /// <summary>Whether moving snaps to the grid.</summary>
    public static readonly BoolCVar GridSnap = CVar.Bool(
        "ed_grid_snap",
        false,
        CVarOptions.Archive,
        "Whether the move gizmo snaps to the grid; Ctrl while dragging does the opposite.");

    /// <summary>The grid's step, in metres.</summary>
    public static readonly NumericCVar<float> GridStep = CVar.Float(
        "ed_grid_step",
        0.5f,
        0.0625f,
        64f,
        CVarOptions.Archive,
        "The grid step the move gizmo snaps to and the viewport grid is drawn at, in metres.");

    /// <summary>Whether turning snaps to steps.</summary>
    public static readonly BoolCVar AngleSnap = CVar.Bool(
        "ed_angle_snap",
        true,
        CVarOptions.Archive,
        "Whether the rotate gizmo turns in steps.");

    /// <summary>The turning step, in degrees.</summary>
    public static readonly NumericCVar<float> AngleStep = CVar.Float(
        "ed_angle_step",
        15f,
        1f,
        90f,
        CVarOptions.Archive,
        "The step the rotate gizmo turns in, in degrees.");

    /// <summary>Whether the gizmos follow the object's own axes rather than the world's.</summary>
    public static readonly BoolCVar LocalSpace = CVar.Bool(
        "ed_local_space",
        false,
        CVarOptions.Archive,
        "Whether the move and rotate gizmos use the object's own axes rather than the world's.");

    /// <summary>Whether the ground grid is drawn.</summary>
    public static readonly BoolCVar ShowGrid = CVar.Bool(
        "ed_show_grid",
        true,
        CVarOptions.Archive,
        "Whether the viewport draws a grid at the height of the selection, or of what is in front of the camera.");

    /// <summary>Whether gizmos and outlines are drawn over the world.</summary>
    public static readonly BoolCVar ShowGizmos = CVar.Bool(
        "ed_show_gizmos",
        true,
        CVarOptions.Archive,
        "Whether the viewport draws gizmos, outlines and graph overlays.");

    /// <summary>Whether moving or turning an object takes what is placed under it, inside it or standing on it along.</summary>
    public static readonly BoolCVar MoveContents = CVar.Bool(
        "ed_move_contents",
        true,
        CVarOptions.Archive,
        "Whether moving or turning an object also moves its children and the loose objects inside it or standing on it.");

    /// <summary>Whether the game plays the editor's project outside the editor too: its edits and the missions made in it.</summary>
    public static readonly BoolCVar PlayProject = CVar.Bool(
        "ed_play_project",
        true,
        CVarOptions.Archive,
        "Whether the game plays the editor's project (its edited levels and new missions) outside the editor too.");
}
