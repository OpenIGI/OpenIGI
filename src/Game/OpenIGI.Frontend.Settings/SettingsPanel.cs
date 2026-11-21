namespace OpenIGI.Frontend.Settings;

/// <summary>A titled panel of rows on a settings page.</summary>
/// <param name="Title">The heading shown above the rows.</param>
/// <param name="Groups">The quality choices in this panel, in the order they are shown, before its single rows.</param>
/// <param name="Rows">The single-variable rows in this panel, in the order they are shown.</param>
public sealed record SettingsPanel(
    string Title,
    IReadOnlyList<QualityGroup> Groups,
    IReadOnlyList<SettingRow> Rows)
{
    /// <summary>Initializes a new instance of the <see cref="SettingsPanel"/> class with single rows only.</summary>
    /// <param name="title">The heading.</param>
    /// <param name="rows">The rows.</param>
    public SettingsPanel(string title, params SettingRow[] rows)
        : this(title, [], rows)
    {
    }
}
