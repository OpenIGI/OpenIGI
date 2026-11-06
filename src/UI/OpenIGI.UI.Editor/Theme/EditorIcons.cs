using OpenIGI.Core.Graphics;
using OpenIGI.UI.Kit;

namespace OpenIGI.UI.Editor.Theme;

/// <summary>
/// The names in the icon face (Google's Material Icons, the set scene editors such as s&amp;box draw theirs from) that the
/// shared <see cref="KitIcon"/> set is drawn with in the editor.
/// </summary>
public static class EditorIcons
{
    /// <summary>
    /// Gets every icon name the editor draws, beyond those of the shared set: what the icon face's atlases hold, so each
    /// size is a few dozen glyphs rather than the face's two thousand.
    /// </summary>
    public static IReadOnlyList<string> Extra { get; } =
    [
        "visibility_off", "content_cut", "content_paste", "edit", "add_box", "create_new_folder", "folder_open", "inventory_2",
        "star", "star_border", "filter_list", "view_module", "view_list", "refresh", "more_vert", "more_horiz", "info",
        "warning", "error", "check_circle", "notifications", "keyboard", "tune", "palette", "fullscreen", "fullscreen_exit",
        "lock", "lock_open", "link", "place", "flag", "directions_car", "flight", "person_pin", "security", "sensor_door",
        "lightbulb_outline", "landscape", "category", "label", "schedule", "timeline", "route", "polyline", "push_pin",
        "zoom_in", "zoom_out", "center_focus_strong", "3d_rotation", "straighten", "photo_camera", "music_note", "movie",
        "texture", "layers", "travel_explore", "terminal", "data_object", "help_outline", "close_fullscreen", "open_in_new",
        "arrow_drop_down", "arrow_right", "expand_more", "expand_less", "chevron_left", "drag_indicator", "space_dashboard",
    ];

    /// <summary>Gets every icon name the editor draws: the shared set's and <see cref="Extra"/>.</summary>
    public static IEnumerable<string> All =>
        Enum.GetValues<KitIcon>().Select(NameOf).Where(static name => name.Length > 0).Concat(Extra).Distinct(StringComparer.Ordinal);

    /// <summary>
    /// Gets the colour an icon of a kind of thing is drawn in, as s&amp;box's browser and tree colour theirs: yellow folders
    /// and groups, blue models, green pictures, pink sounds, violet scripts.
    /// </summary>
    /// <param name="icon">The icon.</param>
    /// <returns>The colour, or <see langword="null"/> for the theme's icon colour.</returns>
    public static Bgra32? KindColour(KitIcon icon) => icon switch
    {
        KitIcon.Folder or KitIcon.Hierarchy or KitIcon.Star => Bgra32.FromRgb(0xE6, 0xDB, 0x74),
        KitIcon.Copy => Bgra32.FromRgb(0xE0, 0xA4, 0x5C),
        KitIcon.Cube => Bgra32.FromRgb(0x6C, 0x9C, 0xF0),
        KitIcon.Image => Bgra32.FromRgb(0xB0, 0xE2, 0x4D),
        KitIcon.Sound => Bgra32.FromRgb(0xDF, 0x91, 0x94),
        KitIcon.Script => Bgra32.FromRgb(0xC0, 0x9C, 0xF0),
        KitIcon.Terrain => Bgra32.FromRgb(0x8F, 0xC9, 0x8A),
        KitIcon.Person => Bgra32.FromRgb(0xF0, 0xB0, 0x70),
        KitIcon.Light => Bgra32.FromRgb(0xFF, 0xD8, 0x6A),
        KitIcon.Graph => Bgra32.FromRgb(0x7F, 0xD6, 0xD0),
        _ => null,
    };

    /// <summary>Gets the icon face's name for a shared icon.</summary>
    /// <param name="icon">The icon.</param>
    /// <returns>The name, or an empty string for <see cref="KitIcon.None"/>.</returns>
    public static string NameOf(KitIcon icon) => icon switch
    {
        KitIcon.Check => "check",
        KitIcon.Folder => "folder",
        KitIcon.Gear => "settings",
        KitIcon.Close => "close",
        KitIcon.Minimize => "remove",
        KitIcon.Maximize => "crop_square",
        KitIcon.Search => "search",
        KitIcon.Plus => "add",
        KitIcon.ArrowUp => "arrow_upward",
        KitIcon.ArrowDown => "arrow_downward",
        KitIcon.ArrowRight => "arrow_forward",
        KitIcon.ArrowLeft => "arrow_back",
        KitIcon.Star => "star",
        KitIcon.ChevronRight => "chevron_right",
        KitIcon.Microphone => "mic",
        KitIcon.Undo => "undo",
        KitIcon.Redo => "redo",
        KitIcon.Save => "save",
        KitIcon.Play => "play_arrow",
        KitIcon.Pause => "pause",
        KitIcon.Stop => "stop",
        KitIcon.Select => "near_me",
        KitIcon.Move => "open_with",
        KitIcon.Rotate => "rotate_right",
        KitIcon.Scale => "open_in_full",
        KitIcon.Cube => "view_in_ar",
        KitIcon.Trash => "delete",
        KitIcon.Copy => "content_copy",
        KitIcon.Eye => "visibility",
        KitIcon.Script => "code",
        KitIcon.Image => "image",
        KitIcon.Sound => "volume_up",
        KitIcon.Person => "person",
        KitIcon.Light => "lightbulb",
        KitIcon.Camera => "videocam",
        KitIcon.Grid => "grid_on",
        KitIcon.Globe => "public",
        KitIcon.Document => "description",
        KitIcon.Hierarchy => "account_tree",
        KitIcon.History => "history",
        KitIcon.Terrain => "terrain",
        KitIcon.Graph => "hub",
        _ => string.Empty,
    };
}
