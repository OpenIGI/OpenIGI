using System.Globalization;
using OpenIGI.Core.Configuration;
using OpenIGI.Frontend.Settings;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>The settings page's Display category: its rows, in <see cref="GraphicsMenu.DisplaySettings"/>'s order.</summary>
/// <remarks>
/// Two of them are lists this page builds, because their variables' types do not spell the choices out:
/// the resolutions the display offers, and the frame-rate caps.
/// </remarks>
internal static class DisplaySettingsTab
{
    /// <summary>Builds the category's rows.</summary>
    /// <param name="rowBuilder">What the rows are built with; its lookup goes through the draft.</param>
    /// <param name="theme">The theme.</param>
    public static List<Control> Build(SettingsRowBuilder rowBuilder, UiTheme theme)
    {
        var rows = new List<Control>();
        foreach (GraphicsSetting setting in GraphicsMenu.DisplaySettings)
        {
            switch (setting.Variable)
            {
                case "r_resolution":
                    if (ChoiceRow(rowBuilder, theme, setting, ResolutionChoices(rowBuilder.Context)) is ({ } resolution, { } cvar))
                    {
                        rows.Add(resolution);

                        // Borderless has no resolution of its own to choose, so the row follows the window mode.
                        rowBuilder.WhileShown("r_window_mode", () => rowBuilder.DescribeRow(resolution, cvar, setting));
                    }

                    break;

                case "r_frame_limit":
                    if (ChoiceRow(rowBuilder, theme, setting, FrameRateCapChoices()) is ({ } cap, _))
                    {
                        rows.Add(cap);
                    }

                    break;

                default:
                    rows.AddRange(rowBuilder.SettingRows(theme, [setting]));
                    break;
            }
        }

        return rows;
    }

    /// <summary>A row choosing among a list of a variable's values, and the variable; nulls where it is not registered.</summary>
    private static (Dropdown? Control, ICVar? Cvar) ChoiceRow(
        SettingsRowBuilder rowBuilder,
        UiTheme theme,
        GraphicsSetting setting,
        IReadOnlyList<(string Value, string Caption)> choices)
    {
        if (rowBuilder.Find(setting.Variable) is not { } cvar)
        {
            return (null, null);
        }

        Dropdown control = CVarBinding.CreateChoiceControl(theme, cvar, setting.Caption, choices, rowBuilder.Bindings);
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

    /// <summary>No cap, then the caps <see cref="GraphicsMenu.FrameRateCaps"/> offers.</summary>
    private static List<(string Value, string Caption)> FrameRateCapChoices() =>
        [
            .. GraphicsMenu.FrameRateCaps.Select(static fps => fps <= 0
                ? ("0", "Unlimited")
                : (fps.ToString(CultureInfo.InvariantCulture), string.Create(CultureInfo.InvariantCulture, $"{fps} fps"))),
        ];
}
