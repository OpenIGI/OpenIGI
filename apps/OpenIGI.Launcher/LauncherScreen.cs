using System.Globalization;
using OpenIGI.Input;
using OpenIGI.Launcher.Pages;
using OpenIGI.UI;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Screens;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Launcher;

/// <summary>What the launcher's own window chrome asks the window to do.</summary>
internal interface ILauncherWindow
{
    void Minimize();

    void ToggleMaximize();

    void Close();

    /// <summary>Gets or sets the system clipboard's text: read to paste a folder into a field, written to copy the log.</summary>
    string ClipboardText { get; set; }

    /// <summary>Decodes a picture file for drawing, once per file.</summary>
    /// <param name="path">The picture: PNG, JPEG or WebP.</param>
    /// <returns>The picture, or <see langword="null"/> when it is missing or cannot be decoded.</returns>
    BackdropPicture? Picture(string path);
}

/// <summary>
/// The launcher's one screen: its own window chrome, the header with the game switcher and section tabs,
/// the current section, and the footer reporting what is downloading.
/// </summary>
/// <remarks>
/// Built from the same widgets as the game's menus — <see cref="GameSwitcher"/>, <see cref="HeaderTab"/>,
/// <see cref="PrimaryAction"/>, the settings rows — and driven by the same <see cref="Screen"/> focus and
/// input handling, so the launcher answers a mouse, a keyboard and a pad the way the menus do.
/// </remarks>
internal sealed class LauncherScreen : Screen
{
    private const float ChromeHeight = 36f;
    private const float HeaderHeight = 76f;
    private const float FooterHeight = 60f;

    private static readonly string[] Sections = ["Home", "Content", "Mods", "Logs", "Settings"];

    private readonly UiTheme _theme;
    private readonly UiTheme _rowTheme;
    private readonly LauncherModel _model;
    private readonly ILauncherWindow _window;
    private readonly BackdropPicture? _picture;
    private readonly List<HeaderTab> _tabs = [];
    private readonly List<UiElement> _pages = [];

    private LauncherBackdrop? _backdrop;
    private HomePage? _home;
    private ContentPage? _content;
    private LogsPage? _logs;
    private GameSwitcher? _switcher;
    private UiElement? _chrome;
    private FooterBar? _footer;
    private int _section;
    private int _revision = -1;

    public LauncherScreen(UiTheme theme, UiTheme rowTheme, LauncherModel model, ILauncherWindow window, BackdropPicture? picture)
    {
        _theme = theme;
        _rowTheme = rowTheme;
        _model = model;
        _window = window;
        _picture = picture;
        Name = nameof(LauncherScreen);
    }

    /// <summary>Gets a value indicating whether a point is on the chrome bar's empty part, where a drag moves the window.</summary>
    /// <param name="point">The point, in window pixels.</param>
    /// <returns><see langword="true"/> over the draggable part of the title bar.</returns>
    public bool IsDragRegion(UiVec2 point) =>
        _chrome is { } chrome
        && chrome.Bounds.Contains(point)
        && Root?.HitTest(point)?.FindAncestor<Control>() is null;

    protected override UiElement BuildRoot(LayoutContext context)
    {
        _tabs.Clear();
        _pages.Clear();

        var root = new Canvas();
        _backdrop = new LauncherBackdrop(_picture) { Focus = BackdropFocus() };
        root.Add(_backdrop);

        var layout = new Grid();
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.RowDefinitions.Add(GridLength.Absolute(ChromeHeight));
        layout.RowDefinitions.Add(GridLength.Absolute(HeaderHeight));
        layout.RowDefinitions.Add(GridLength.Star);
        layout.RowDefinitions.Add(GridLength.Absolute(FooterHeight));

        // The main area first in tree order, so focus starts on the page rather than on the chrome.
        var main = new Panel();
        _home = new HomePage(_theme, _model, () => ShowSection(4), () => ShowSection(1));
        AddPage(main, _home.Root);
        _content = new ContentPage(_rowTheme, _model, _window.Picture);
        AddPage(main, _content.Root);
        AddPage(main, PlaceholderPage.Build(_theme, "Mods & plugins", "Mods", "Mod management arrives with the mod loader"));
        _logs = new LogsPage(_theme, _model, _window);
        AddPage(main, _logs.Root);
        AddPage(main, SettingsPage.Build(_rowTheme, _model));
        layout.Add(main, 2, 0);

        _chrome = BuildChrome();
        layout.Add(_chrome, 0, 0);
        layout.Add(BuildHeader(), 1, 0);

        _footer = new FooterBar(_theme, _model, RestartIntoUpdate);
        layout.Add(_footer, 3, 0);

        root.Add(layout);
        ShowSection(_section, refocus: false);
        return root;
    }

    protected override bool HandleInput(UiNavigation nav, InputState input)
    {
        ArgumentNullException.ThrowIfNull(nav);
        ArgumentNullException.ThrowIfNull(input);

        // A row being dragged owns the input until it is dropped; one carried with the keys is put back by Back.
        if (IsPointerCaptured)
        {
            base.HandleInput(nav, input);
            return false;
        }

        if (nav.Back && Focus?.Focused is ReorderHandle handle && handle.CancelKeyboardMove())
        {
            return false;
        }

        // An open game list takes Back for itself: shutting it is what the player meant.
        if (nav.Back && _switcher is { IsOpen: true } open)
        {
            open.Close();
            return false;
        }

        // Ctrl+V into a text field: a folder path is far easier pasted than typed.
        bool control = input.Keyboard.IsDown(KeyCode.LeftControl) || input.Keyboard.IsDown(KeyCode.RightControl);
        if (control && input.Keyboard.WasPressed(KeyCode.V) && Focus?.Focused is TextBox field)
        {
            field.Text += _window.ClipboardText.ReplaceLineEndings(string.Empty);
            return false;
        }

        // Q and E step through the sections from anywhere, the same keys the game's menus use — except
        // while a text field has the keyboard, where they are letters.
        bool typing = Focus?.Focused is TextBox;
        if (!typing && input.Keyboard.WasPressed(KeyCode.Q))
        {
            ShowSection((_section + Sections.Length - 1) % Sections.Length);
        }
        else if (!typing && input.Keyboard.WasPressed(KeyCode.E))
        {
            ShowSection((_section + 1) % Sections.Length);
        }

        base.HandleInput(nav, input);

        // Back goes home rather than out: the launcher is a window, closed from its chrome.
        if (nav.Back && _section != 0)
        {
            ShowSection(0);
        }

        return false;
    }

    protected override void OnUpdate(float elapsedSeconds)
    {
        _model.Tick(elapsedSeconds);
        if (_model.Revision != _revision)
        {
            _revision = _model.Revision;
            _home?.Refresh();
            _content?.Refresh();
            if (_switcher is not null)
            {
                _switcher.Text = _model.Current.Title;
            }

            // The page's chips and notes were rebuilt after this frame's layout.
            RequestLayout();
        }

        if (_model.CloseRequested)
        {
            _window.Close();
            return;
        }

        _home?.Update();
        _content?.Update();
        _logs?.Update();
        if (_backdrop is not null)
        {
            _backdrop.Focus = BackdropFocus();
        }

        if (_tabs.Count > 0)
        {
            _tabs[0].Badge = _model.UpdatesAvailable;
            _tabs[1].Badge = _model.Job is null ? 0 : 1;
            _tabs[3].Badge = _logs?.Errors ?? 0;
        }
    }

    private void AddPage(Panel host, UiElement page)
    {
        page.Visibility = Visibility.Collapsed;
        _pages.Add(page);
        host.AddChild(page);
    }

    private void ShowSection(int index, bool refocus = true)
    {
        _section = Math.Clamp(index, 0, Sections.Length - 1);
        for (int i = 0; i < _pages.Count; i++)
        {
            _pages[i].Visibility = i == _section ? Visibility.Visible : Visibility.Collapsed;
        }

        for (int i = 0; i < _tabs.Count; i++)
        {
            _tabs[i].Selected = i == _section;
        }

        // The page just shown was hidden when this frame was laid out; lay it out before it is drawn.
        RequestLayout();

        if (refocus)
        {
            Focus?.Validate();
            Focus?.FocusFirst();
        }
    }

    private void RestartIntoUpdate()
    {
        if (_model.RestartIntoUpdate())
        {
            _window.Close();
        }
    }

    private float BackdropFocus() => _model.Selected switch
    {
        1 => 0.85f,
        2 => 0.5f,
        _ => 0.15f,
    };

    private Bar BuildChrome()
    {
        var bar = new Bar(_theme, UiPalette.Backdrop.WithAlpha(179), 0f, lineAtBottom: true);
        var row = new FlexPanel { Orientation = Orientation.Horizontal, Padding = new Thickness(20f, 0f, 0f, 0f) };
        row.Add(new ChromeCaption(_theme));
        row.Add(new Panel(), grow: 1f);

        var minimize = new IconButton(_theme, KitIcon.Minimize) { Tooltip = "Minimise" };
        minimize.Activated += _ => _window.Minimize();
        var maximize = new IconButton(_theme, KitIcon.Maximize) { Tooltip = "Maximise" };
        maximize.Activated += _ => _window.ToggleMaximize();
        var close = new IconButton(_theme, KitIcon.Close) { Style = IconButtonStyle.ChromeClose, Tooltip = "Close" };
        close.Activated += _ => _window.Close();
        row.Add(minimize);
        row.Add(maximize);
        row.Add(close);

        bar.AddChild(row);
        return bar;
    }

    private Bar BuildHeader()
    {
        var bar = new Bar(_theme, UiPalette.Ink.WithAlpha(0), 0f, lineAtBottom: true);
        var row = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 48f, Padding = new Thickness(48f, 0f) };

        string[] titles = [.. _model.Games.Select(g => g.Title)];
        _switcher = new GameSwitcher(_theme, _model.Current.Title, titles)
        {
            Subtitle = _model.Current.Tagline,
            ItemEnabled = i => _model.Games[i].Released,
            TitleSize = 24f,
            CurrentIndex = _model.Selected,
            ItemDetail = i => StateLine(_model.Games[i]),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _switcher.ItemSelected += index =>
        {
            _model.Selected = index;
            _switcher.Text = _model.Current.Title;
            _switcher.Subtitle = _model.Current.Tagline;
            _home?.Refresh();
            RequestLayout();
        };
        row.Add(_switcher);

        var nav = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 4f };
        for (int i = 0; i < Sections.Length; i++)
        {
            int index = i;
            var tab = new HeaderTab(_theme, Sections[i]) { TextSize = 19f, Padding = new Thickness(20f, 0f) };
            if (i == 3)
            {
                tab.BadgeColor = _theme.Danger;
            }

            tab.Activated += _ => ShowSection(index);
            _tabs.Add(tab);
            nav.Add(tab);
        }

        row.Add(nav);
        row.Add(new Panel(), grow: 1f);
        row.Add(new StatusIndicator(_theme));
        bar.AddChild(row);
        return bar;
    }

    private static string StateLine(LauncherGame game) => game.State switch
    {
        GameState.NotInstalled => "NOT INSTALLED",
        GameState.UpdateAvailable => "UPDATE AVAILABLE",
        GameState.NeedsGameFolder => "GAME FILES NEEDED",
        GameState.Damaged => "REPAIR NEEDED",
        GameState.Playing => "RUNNING",
        GameState.Unavailable => "COMING SOON",
        _ => "READY · " + game.Version,
    };

    /// <summary>The footer: what is downloading and how far it has got, or that everything is up to date.</summary>
    private sealed class FooterBar : Panel
    {
        private readonly UiTheme _theme;
        private readonly LauncherModel _model;
        private readonly Button _pause;
        private readonly Button _cancel;
        private readonly ProgressBar _bar;
        private readonly Button _restart;

        public FooterBar(UiTheme theme, LauncherModel model, Action restart)
        {
            _theme = theme;
            _model = model;
            _restart = new Button(theme, "Restart to update launcher") { Accent = ButtonAccent.Ghost, TextSize = 16f, Padding = new Thickness(16f, 8f), VerticalAlignment = VerticalAlignment.Center };
            _restart.Activated += _ => restart();
            _bar = new ProgressBar(theme) { VerticalAlignment = VerticalAlignment.Center };
            _pause = new Button(theme, "Pause") { Accent = ButtonAccent.Ghost, TextSize = 16f, Padding = new Thickness(16f, 8f), VerticalAlignment = VerticalAlignment.Center };
            _pause.Activated += _ => _model.TogglePause();
            _cancel = new Button(theme, "Cancel") { Accent = ButtonAccent.Text, TextSize = 16f, Padding = new Thickness(12f, 8f), VerticalAlignment = VerticalAlignment.Center };
            _cancel.Activated += _ => _model.Cancel();

            var row = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 22f, Padding = new Thickness(48f + 300f + 22f, 0f, 48f + 160f, 0f) };
            row.Add(_bar, grow: 1f);
            row.Add(new Panel { Width = 230f });
            row.Add(_pause);
            row.Add(_cancel);
            row.Add(_restart);
            AddChild(row);
        }

        protected override void OnUpdate(float elapsedSeconds)
        {
            bool busy = _model.Job is not null;
            Visibility shown = busy ? Visibility.Visible : Visibility.Hidden;
            _bar.Visibility = _pause.Visibility = _cancel.Visibility = shown;
            _restart.Visibility = _model.LauncherUpdateReady is null ? Visibility.Collapsed : Visibility.Visible;
            if (_model.Job is { } job)
            {
                _bar.Value = job.Progress;
                _bar.Fill = job.Paused ? _theme.TextMuted : _theme.Accent;
                _pause.Text = job.Paused ? "Resume" : "Pause";
            }
        }

        protected override void OnDraw(UiPainter painter)
        {
            LayoutContext context = painter.Context;
            painter.DrawBackdrop(Bounds, context.Px(18f), UiPalette.Backdrop.WithAlpha(204));
            painter.FillRect(new UiRect(Bounds.X, Bounds.Y, Bounds.Width, context.Px(1f)), UiPalette.White(0.07f));

            float x = Bounds.X + context.Px(48f);
            float mono = context.Px(14f);
            if (_model.Job is { } job)
            {
                float labelSize = context.Px(19f);
                float subSize = context.Px(13f);
                float block = (labelSize * 0.7f) + context.Px(6f) + _theme.Mono.LineHeight(subSize);
                float top = Bounds.Center.Y - (block * 0.5f);
                var labelBox = new UiRect(x, top, context.Px(300f), labelSize * 0.7f);
                painter.PushClip(new UiRect(x, Bounds.Y, context.Px(300f), Bounds.Height));
                painter.DrawLabel(_theme.Heading, job.Label.ToUpperInvariant(), new UiVec2(x, UiRowStyle.CapsCenterY(_theme.Heading, labelSize, labelBox)), labelSize, _theme.Text, 0.1f);
                painter.PopClip();
                string stage = job.Stage == Services.InstallStage.Installing ? "INSTALLING" : "DOWNLOADING";
                painter.DrawLabel(_theme.Mono, stage, new UiVec2(x, top + (labelSize * 0.7f) + context.Px(6f)), subSize, _theme.TextMuted, 0.16f);

                string stats = job.Paused
                    ? $"PAUSED · {LauncherModel.FormatSize(job.Done)} / {LauncherModel.FormatSize(job.Size)}"
                    : string.Create(CultureInfo.InvariantCulture, $"{job.Speed:0} MB/s · {LauncherModel.FormatSize(job.Done)} / {LauncherModel.FormatSize(job.Size)}");
                float statsSize = context.Px(15f);
                float statsRight = _pause.Bounds.X - context.Px(22f);
                float statsWidth = _theme.Mono.MeasureWidth(stats, statsSize);
                painter.DrawText(_theme.Mono, stats, new UiVec2(statsRight - statsWidth, UiRowStyle.CenterY(_theme.Mono, statsSize, Bounds)), statsSize, UiPalette.TextBody);
            }
            else
            {
                string idle = _model.Problem is { } problem ? problem.ToUpperInvariant()
                    : _model.LauncherUpdateReady is { } ready ? $"LAUNCHER {ready} IS READY"
                    : _model.UpdatesAvailable > 0 ? "UPDATE AVAILABLE"
                    : "UP TO DATE";
                painter.FillDiamond(new UiVec2(x + context.Px(3f), Bounds.Center.Y), context.Px(6f), _theme.TextMuted);
                painter.DrawLabel(_theme.Mono, idle, new UiVec2(x + context.Px(16f), UiRowStyle.CenterY(_theme.Mono, mono, Bounds)), mono, _theme.TextMuted, 0.2f);
            }

            string version = "LAUNCHER " + LauncherVersion;
            float versionWidth = UiPainter.MeasureLabel(_theme.Mono, version, mono, 0.16f);
            painter.DrawLabel(_theme.Mono, version, new UiVec2(Bounds.Right - context.Px(48f) - versionWidth, UiRowStyle.CenterY(_theme.Mono, mono, Bounds)), mono, _theme.TextDim, 0.16f);
        }

        private static string LauncherVersion => Services.LauncherUpdater.OwnVersion.ToString().ToUpperInvariant();
    }
}
