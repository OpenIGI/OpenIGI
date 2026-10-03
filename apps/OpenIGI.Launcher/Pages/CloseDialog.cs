using OpenIGI.Input;
using OpenIGI.Launcher.Services;
using OpenIGI.UI;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Screens;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Launcher.Pages;

/// <summary>
/// Asked when the close button is pressed and the player has not said what it should do: go to the tray, or
/// exit. "Remember my choice" makes the answer the close button's setting, which Settings can change later.
/// </summary>
/// <remarks>Back (Escape, or the pad's B) closes the question and leaves the launcher as it was.</remarks>
internal sealed class CloseDialog : Screen
{
    private readonly UiTheme _theme;
    private readonly string _minimizeText;
    private readonly Action<CloseAction, bool> _choose;
    private Button? _minimize;
    private Toggle? _remember;
    private bool _done;

    /// <summary>Initialises a new instance of the <see cref="CloseDialog"/> class.</summary>
    /// <param name="theme">The launcher's theme.</param>
    /// <param name="hasTray">Whether this system has a notification area; without one, the launcher minimises instead.</param>
    /// <param name="choose">Told the choice and whether to remember it.</param>
    public CloseDialog(UiTheme theme, bool hasTray, Action<CloseAction, bool> choose)
    {
        _theme = theme;
        _minimizeText = hasTray ? "Minimize to tray" : "Minimize";
        _choose = choose;
        Name = nameof(CloseDialog);
    }

    /// <inheritdoc/>
    protected override Control? InitialFocus => _minimize;

    /// <inheritdoc/>
    protected override UiElement BuildRoot(LayoutContext context)
    {
        // Over the whole window, so nothing behind the question can be clicked while it is asked.
        var scrim = new GlassPanel(_theme) { Tint = UiPalette.Ink.WithAlpha(190), Blur = 6f, Border = UiPalette.Ink.WithAlpha(0) };

        _minimize = new Button(_theme, _minimizeText) { Accent = ButtonAccent.Solid, TextSize = 17f, Padding = new Thickness(22f, 12f) };
        _minimize.Activated += _ => Choose(CloseAction.MinimizeToTray);
        var exit = new Button(_theme, "Exit") { Accent = ButtonAccent.Ghost, TextSize = 17f, Padding = new Thickness(22f, 12f) };
        exit.Activated += _ => Choose(CloseAction.Exit);
        var cancel = new Button(_theme, "Cancel") { Accent = ButtonAccent.Text, TextSize = 17f, Padding = new Thickness(16f, 12f) };
        cancel.Activated += _ => _done = true;

        _remember = new Toggle(_theme, "Remember my choice") { Hint = "Change it later in Settings" };

        var buttons = new FlexPanel { Gap = 10f };
        buttons.Add(_minimize);
        buttons.Add(exit);
        buttons.Add(new Panel(), grow: 1f);
        buttons.Add(cancel);

        var body = new StackPanel { Spacing = 18f, Padding = new Thickness(32f, 28f) };
        body.AddChild(new SectionTitle(_theme, "Closing the launcher", "Keep it running?") { HorizontalAlignment = HorizontalAlignment.Left });
        body.AddChild(new Text(_theme.Body, "Left running, it keeps checking for updates and tells you when one is out.", 17f, UiPalette.TextBody));
        body.AddChild(_remember);
        body.AddChild(buttons);

        var panel = new GlassPanel(_theme) { Tint = UiPalette.Ink.WithAlpha(240), Border = UiPalette.White(0.12f) };
        panel.AddChild(body);

        var layout = new Grid();
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.ColumnDefinitions.Add(GridLength.Absolute(640f));
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.RowDefinitions.Add(GridLength.Star);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Star);
        layout.Add(scrim, 0, 0, rowSpan: 3, columnSpan: 3);
        layout.Add(panel, 1, 1);
        return layout;
    }

    /// <inheritdoc/>
    protected override bool HandleInput(UiNavigation nav, InputState input) => base.HandleInput(nav, input) || _done;

    private void Choose(CloseAction action)
    {
        _done = true;
        _choose(action, _remember?.Value ?? false);
    }
}
