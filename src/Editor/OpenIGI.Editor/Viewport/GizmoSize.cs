namespace OpenIGI.Editor.Viewport;

/// <summary>How large the viewport's gizmos are drawn and grabbed: the interface scale times the preference.</summary>
/// <remarks>One value for every gizmo, set by the viewport once a frame, so the move arrows and the rotate rings grow together.</remarks>
internal static class GizmoSize
{
    /// <summary>Gets or sets the multiple of each gizmo's normal size.</summary>
    public static float Scale { get; set; } = 1f;
}
