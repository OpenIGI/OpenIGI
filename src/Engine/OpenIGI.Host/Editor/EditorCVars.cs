using OpenIGI.Core.Configuration;

namespace OpenIGI.Host.Editor;

/// <summary>The editor's settings.</summary>
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
}
