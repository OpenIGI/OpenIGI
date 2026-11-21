using System.Globalization;
using OpenIGI.Core.Configuration;
using OpenIGI.Frontend.Settings;
using OpenIGI.Rendering;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>The settings page's Display category: its panels, as <see cref="DisplayMenu.Panels"/> lays them out.</summary>
/// <remarks>
/// Two of them are lists this page builds, because their variables' types do not spell the choices out:
/// the resolutions the display offers, and the frame-rate caps.
/// </remarks>
internal static class DisplaySettingsTab
{
    /// <summary>Builds the category's groups.</summary>
    /// <param name="rowBuilder">What the rows are built with; its lookup goes through the draft.</param>
    /// <param name="theme">The theme.</param>
    public static List<SettingsGroup> Build(SettingsRowBuilder rowBuilder, UiTheme theme) =>
        SettingsPanelBuilder.Build(rowBuilder, theme, "Display", DisplayMenu.Panels, setting => BuildRow(rowBuilder, theme, setting));

    // The rows whose choices the page lists itself; null for the rest, which the panel builds as usual.
    private static Dropdown? BuildRow(SettingsRowBuilder rowBuilder, UiTheme theme, SettingRow setting)
    {
        switch (setting.Variable)
        {
            case "r_resolution":
                if (ChoiceRow(rowBuilder, theme, setting, ResolutionChoices(rowBuilder.Context)) is ({ } resolution, { } cvar))
                {
                    // Borderless has no resolution of its own to choose, so the row follows the window mode.
                    rowBuilder.WhileShown("r_window_mode", () => rowBuilder.DescribeRow(resolution, cvar, setting));
                    return resolution;
                }

                return null;

            case "r_renderer" when setting.Choices is { } apis:
                // No Automatic to choose: a setting still on auto reads as the API it starts, which is what it is.
                return ChoiceRow(
                    rowBuilder,
                    theme,
                    setting,
                    [.. apis.Select(static pair => (pair.Key, pair.Value))],
                    static stored => Enum.TryParse(stored, ignoreCase: true, out RendererBackend api)
                        ? RendererBackends.Resolve(api).ToString().ToLowerInvariant()
                        : stored).Control;

            case "r_frame_limit":
                return ChoiceRow(rowBuilder, theme, setting, FrameRateCapChoices()).Control;

            default:
                return null;
        }
    }

    /// <summary>A row choosing among a list of a variable's values, and the variable; nulls where it is not registered.</summary>
    private static (Dropdown? Control, ICVar? Cvar) ChoiceRow(
        SettingsRowBuilder rowBuilder,
        UiTheme theme,
        SettingRow setting,
        IReadOnlyList<(string Value, string Caption)> choices,
        Func<string, string>? reads = null)
    {
        if (rowBuilder.Find(setting.Variable) is not { } cvar)
        {
            return (null, null);
        }

        Dropdown control = CVarBinding.CreateChoiceControl(theme, cvar, setting.Caption, choices, rowBuilder.Bindings, reads);
        rowBuilder.DescribeRow(control, cvar, setting);
        rowBuilder.TabCvars.Add(cvar);
        return (control, cvar);
    }

    /// <summary>The desktop's own resolution, then every one the display offers, largest first.</summary>
    private static List<(string Value, string Caption)> ResolutionChoices(ShellContext context)
    {
        List<(string Value, string Caption)> choices = [(string.Empty, "Desktop")];
        foreach ((int width, int height) in context.Host.DisplayResolutions())
        {
            choices.Add((
                ResolutionText.Format(width, height),
                string.Create(CultureInfo.InvariantCulture, $"{width} × {height}")));
        }

        return choices;
    }

    /// <summary>No cap, then the caps <see cref="DisplayMenu.FrameRateCaps"/> offers.</summary>
    private static List<(string Value, string Caption)> FrameRateCapChoices() =>
        [
            .. DisplayMenu.FrameRateCaps.Select(static fps => fps <= 0
                ? ("0", "Unlimited")
                : (fps.ToString(CultureInfo.InvariantCulture), string.Create(CultureInfo.InvariantCulture, $"{fps} fps"))),
        ];
}
