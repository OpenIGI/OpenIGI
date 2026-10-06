using OpenIGI.Core.Titles;
using OpenIGI.Input;
using OpenIGI.Launcher.Services;
using OpenIGI.Releases;
using OpenIGI.UI;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Screens;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Launcher.Pages;

/// <summary>
/// The first-run guide: where the player's own copy of the game is, then which channel to install from.
/// </summary>
/// <remarks>
/// <para>
/// Shown once, over the launcher, to a player whose game the launcher could not find (see
/// <see cref="LauncherModel.NeedsOnboarding"/>). Without it, a new player downloaded the whole build first and
/// only then learned it needs the original game, from a button that sent them to Settings.
/// </para>
/// <para>
/// The search runs off the frame (<see cref="GameFinder"/>): Steam's libraries, GOG's installs and the usual
/// folders. What it finds is offered to pick from; Browse and a typed path are always there too, and a folder
/// counts only when the game's own signature file is in it. Either step can be left: Skip and Later finish the
/// guide without changing anything a player has not chosen, and Back (Escape) steps back or skips.
/// </para>
/// </remarks>
internal sealed class OnboardingScreen : Screen
{
    private const int MostShown = 4;

    private readonly UiTheme _theme;
    private readonly UiTheme _rowTheme;
    private readonly LauncherModel _model;
    private readonly ILauncherWindow _window;
    private readonly Task<IReadOnlyList<string>> _search;

    private SectionDeck? _deck;
    private StackPanel? _found;
    private Paragraph? _searching;
    private TextRow? _folderRow;
    private Text? _verdict;
    private Button? _continue;
    private Dropdown? _channel;
    private Paragraph? _channelNote;
    private Paragraph? _installNote;
    private Button? _install;
    private Button? _later;
    private bool _listed;
    private int _step;
    private bool _done;

    /// <summary>Initialises a new instance of the <see cref="OnboardingScreen"/> class, and starts looking for the game.</summary>
    /// <param name="theme">The launcher's theme.</param>
    /// <param name="rowTheme">The theme the settings rows are drawn in.</param>
    /// <param name="model">The launcher's model.</param>
    /// <param name="window">The window, for the folder picker.</param>
    public OnboardingScreen(UiTheme theme, UiTheme rowTheme, LauncherModel model, ILauncherWindow window)
    {
        _theme = theme;
        _rowTheme = rowTheme;
        _model = model;
        _window = window;
        _search = Task.Run(() => GameFinder.Find(GameTitle.Igi1));
        Name = nameof(OnboardingScreen);
    }

    private string Folder => (_folderRow?.Text ?? string.Empty).Trim().Trim('"');

    private bool FolderValid => GameStarter.IsInstallation(GameTitle.Igi1, Folder);

    /// <inheritdoc/>
    protected override Control? InitialFocus => _continue;

    /// <inheritdoc/>
    protected override UiElement BuildRoot(LayoutContext context)
    {
        // Over the whole window, so the launcher behind waits until the guide is done.
        var scrim = new GlassPanel(_theme) { Tint = UiPalette.Ink.WithAlpha(205), Blur = 8f, Border = UiPalette.Ink.WithAlpha(0) };

        _deck = new SectionDeck();
        _deck.AddChild(BuildFindStep());
        _deck.AddChild(BuildInstallStep());
        _deck.Show(_step);

        var panel = new GlassPanel(_theme) { Tint = UiPalette.Ink.WithAlpha(242), Border = UiPalette.White(0.12f) };
        panel.AddChild(_deck);

        var layout = new Grid();
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.ColumnDefinitions.Add(GridLength.Absolute(760f));
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.RowDefinitions.Add(GridLength.Star);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Star);
        layout.Add(scrim, 0, 0, rowSpan: 3, columnSpan: 3);
        layout.Add(panel, 1, 1);
        Refresh();
        return layout;
    }

    /// <inheritdoc/>
    protected override bool HandleInput(UiNavigation nav, InputState input)
    {
        ArgumentNullException.ThrowIfNull(nav);
        if (nav.Back && Focus?.Focused is not TextBox)
        {
            if (_step == 1)
            {
                ShowStep(0);
            }
            else
            {
                Finish(install: false, keepFolder: false);
            }

            return _done;
        }

        base.HandleInput(nav, input);
        return _done;
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        base.OnUpdate(elapsedSeconds);
        if (!_listed && _search.IsCompleted)
        {
            _listed = true;
            ListFound(_search.IsCompletedSuccessfully ? _search.Result : []);
        }
    }

    private StackPanel BuildFindStep()
    {
        _found = new StackPanel { Spacing = 6f };
        _searching = new Paragraph(_theme.Body, "Looking in your Steam and GOG libraries and the usual install folders…", 16f, UiPalette.TextBody);
        _found.AddChild(_searching);

        _folderRow = new TextRow(
            _rowTheme,
            "Game folder",
            string.Empty,
            "Choose the folder holding " + GameTitle.Igi1.SignatureFile,
            browse: current => _window.PickFolder($"Choose the folder holding {GameTitle.Igi1.SignatureFile}", current));
        _folderRow.TextChanged += _ => Refresh();
        _verdict = new Text(_theme.Mono, string.Empty, 13f, _theme.TextMuted, 0.12f, uppercase: true);

        _continue = new Button(_theme, "Continue") { Accent = ButtonAccent.Solid, TextSize = 17f, Padding = new Thickness(26f, 12f) };
        _continue.Activated += _ => ShowStep(1);
        var skip = new Button(_theme, "Skip for now") { Accent = ButtonAccent.Text, TextSize = 16f, Padding = new Thickness(14f, 12f) };
        skip.Activated += _ => Finish(install: false, keepFolder: false);

        var buttons = new FlexPanel { Gap = 10f };
        buttons.Add(_continue);
        buttons.Add(new Panel(), grow: 1f);
        buttons.Add(skip);

        var step = new StackPanel { Spacing = 16f, Padding = new Thickness(36f, 30f) };
        step.AddChild(new SectionTitle(_theme, "Welcome to OpenIGI · Step 1 of 2", "Where is your game?") { HorizontalAlignment = HorizontalAlignment.Left });
        step.AddChild(new Paragraph(
            _theme.Body,
            "OpenIGI is a new engine for Project I.G.I. that runs on your own copy of the original game; it ships none of its files. "
                + $"Point it to the folder holding {GameTitle.Igi1.SignatureFile}.",
            17f,
            UiPalette.TextBody));
        step.AddChild(_found);
        step.AddChild(_folderRow);
        step.AddChild(_verdict);
        step.AddChild(buttons);
        return step;
    }

    private StackPanel BuildInstallStep()
    {
        bool beta = _model.Settings.Channel == ReleaseChannel.Beta;
        _channel = new Dropdown(_rowTheme, "Update channel", ["Stable", "Beta"], beta ? 1 : 0);
        _channel.SelectionChanged += _ => Refresh();
        _channelNote = new Paragraph(_theme.Body, string.Empty, 15f, _theme.TextMuted);
        _installNote = new Paragraph(_theme.Body, string.Empty, 16f, UiPalette.TextBody);

        _install = new Button(_theme, "Install") { Accent = ButtonAccent.Solid, TextSize = 17f, Padding = new Thickness(26f, 12f) };
        _install.Activated += _ => Finish(install: true, keepFolder: true);
        var back = new Button(_theme, "Back") { Accent = ButtonAccent.Ghost, TextSize = 16f, Padding = new Thickness(20f, 12f) };
        back.Activated += _ => ShowStep(0);
        _later = new Button(_theme, "Install later") { Accent = ButtonAccent.Text, TextSize = 16f, Padding = new Thickness(14f, 12f) };
        _later.Activated += _ => Finish(install: false, keepFolder: true);

        var buttons = new FlexPanel { Gap = 10f };
        buttons.Add(_install);
        buttons.Add(back);
        buttons.Add(new Panel(), grow: 1f);
        buttons.Add(_later);

        var step = new StackPanel { Spacing = 16f, Padding = new Thickness(36f, 30f) };
        step.AddChild(new SectionTitle(_theme, "Welcome to OpenIGI · Step 2 of 2", "Install OpenIGI") { HorizontalAlignment = HorizontalAlignment.Left });
        step.AddChild(_channel);
        step.AddChild(_channelNote);
        step.AddChild(_installNote);
        step.AddChild(buttons);
        return step;
    }

    // What the search found: each one a choice, the first chosen for the player.
    private void ListFound(IReadOnlyList<string> found)
    {
        if (_found is null || _folderRow is null)
        {
            return;
        }

        _found.ClearChildren();
        if (found.Count == 0)
        {
            _found.AddChild(new Paragraph(
                _theme.Body,
                "It was not in your Steam or GOG libraries or the usual folders. Choose its folder below.",
                16f,
                UiPalette.TextBody));
        }
        else
        {
            _found.AddChild(new Text(_theme.Mono, found.Count == 1 ? "Found on this computer" : "Found on this computer · choose one", 13f, _theme.TextMuted, 0.12f, uppercase: true));
            float delay = 0f;
            foreach (string folder in found.Take(MostShown))
            {
                var choice = new Button(_theme, folder) { Uppercase = false, TextSize = 16f, Padding = new Thickness(16f, 10f), HorizontalAlignment = HorizontalAlignment.Stretch };

                // Built after the screen was, so its entrance is played here: one after another as they arrive.
                choice.PlayEnter(delay);
                delay += 0.06f;
                choice.Activated += _ =>
                {
                    _folderRow.Text = folder;
                    Refresh();
                };
                _found.AddChild(choice);
            }

            if (Folder.Length == 0)
            {
                _folderRow.Text = found[0];
            }
        }

        Refresh();
        Focus?.Validate();
    }

    internal void ShowStep(int step)
    {
        if (step == 1 && !FolderValid)
        {
            return;
        }

        _step = step;
        _deck?.Show(step);
        Refresh();
        Focus?.Validate();
        Focus?.FocusFirst();
    }

    // Everything that follows from the folder typed and the channel picked.
    private void Refresh()
    {
        bool valid = FolderValid;
        if (_verdict is not null)
        {
            _verdict.Value = Folder.Length == 0 ? string.Empty
                : valid ? "◆ Project I.G.I. found"
                : $"No {GameTitle.Igi1.SignatureFile} in that folder";
            _verdict.Color = valid ? _theme.Success : _theme.TextMuted;
        }

        if (_continue is not null)
        {
            _continue.IsEnabled = valid;
        }

        if (_found is not null)
        {
            foreach (Button choice in _found.Children.OfType<Button>())
            {
                choice.Selected = string.Equals(choice.Text, Folder, StringComparison.OrdinalIgnoreCase);
            }
        }

        bool beta = _channel?.SelectedIndex == 1;
        if (_channelNote is not null)
        {
            _channelNote.Value = beta
                ? "Beta gets every new build as soon as it is out, before it is called finished. You can switch back in Settings."
                : "Stable gets the finished releases. You can switch to Beta in Settings at any time.";
        }

        LauncherGame game = _model.Games.First(static g => g.Retail == GameTitle.Igi1);
        bool installed = game.State is not GameState.NotInstalled;
        if (_installNote is not null)
        {
            _installNote.Value = installed
                ? "OpenIGI is already installed in your library."
                : game.SizeMegabytes > 0
                    ? $"The download is {LauncherModel.FormatSize(game.SizeMegabytes)}. You can pause it, or play your game's missions as soon as it finishes."
                    : "The launcher is asking the update service for the download size.";
        }

        if (_install is not null)
        {
            _install.Text = installed ? "Finish" : "Install";
        }

        // Nothing to put off when the build is already there.
        if (_later is not null)
        {
            _later.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
        }

        RequestLayout();
    }

    private void Finish(bool install, bool keepFolder)
    {
        if (_done)
        {
            return;
        }

        _done = true;
        ReleaseChannel channel = _channel?.SelectedIndex == 1 ? ReleaseChannel.Beta : ReleaseChannel.Stable;
        _model.CompleteOnboarding(keepFolder && FolderValid ? Folder : null, keepFolder ? channel : _model.Settings.Channel, install);
    }
}
