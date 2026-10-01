using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Launcher.Pages;

/// <summary>
/// The launcher's home: the selected game's name large over its facts and its one main action, with the
/// latest release notes and the optional content beside it.
/// </summary>
internal sealed class HomePage
{
    private readonly UiTheme _theme;
    private readonly LauncherModel _model;
    private readonly HeroHeading _heading;
    private readonly FlexPanel _chips;
    private readonly PrimaryAction _main;
    private readonly PatchNotesCard _notes;
    private readonly ContentCard _content;

    public HomePage(UiTheme theme, LauncherModel model, Action openSettings, Action openContent)
    {
        _theme = theme;
        _model = model;

        _heading = new HeroHeading(theme);
        _chips = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 10f, HorizontalAlignment = HorizontalAlignment.Left };

        _main = new PrimaryAction(theme, "PLAY") { Width = 380f, Height = 88f, TitleSize = 38f, SubtitleSize = 13f, CutCorner = 20f };
        _main.Activated += _ =>
        {
            if (_model.Job is not null)
            {
                _model.TogglePause();
            }
            else
            {
                _model.StartMainAction(openSettings);
            }
        };

        var verify = new IconButton(theme, KitIcon.Check, "Verify") { Style = IconButtonStyle.Tile, Size = new UiVec2(104f, 88f) };
        verify.Activated += _ => _model.Verify();
        var folder = new IconButton(theme, KitIcon.Folder, "Folder") { Style = IconButtonStyle.Tile, Size = new UiVec2(104f, 88f) };
        folder.Activated += _ => _model.OpenFolder();
        var options = new IconButton(theme, KitIcon.Gear, "Options") { Style = IconButtonStyle.Tile, Size = new UiVec2(104f, 88f) };
        options.Activated += _ => openSettings();

        var actions = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 10f, Height = 88f, Margin = new Thickness(0f, 10f, 0f, 0f) };
        actions.Add(_main);
        actions.Add(verify);
        actions.Add(folder);
        actions.Add(options);

        var left = new StackPanel { Orientation = Orientation.Vertical, Spacing = 22f, VerticalAlignment = VerticalAlignment.Bottom };
        left.AddChild(_heading);
        left.AddChild(_chips);
        left.AddChild(actions);

        _notes = new PatchNotesCard(theme, model.OpenNotes);
        _content = new ContentCard(theme);
        _content.Activated += _ => openContent();

        var right = new StackPanel { Orientation = Orientation.Vertical, Spacing = 14f, VerticalAlignment = VerticalAlignment.Bottom };
        right.AddChild(_notes);
        right.AddChild(_content);

        var grid = new Grid { ColumnSpacing = 32f, Padding = new Thickness(48f, 32f) };
        grid.ColumnDefinitions.Add(GridLength.Star);
        grid.ColumnDefinitions.Add(GridLength.Absolute(420f));
        grid.RowDefinitions.Add(GridLength.Star);
        grid.Add(left, 0, 0);
        grid.Add(right, 0, 1);
        Root = grid;
        Refresh();
    }

    public UiElement Root { get; }

    /// <summary>Re-reads the model into the widgets: the selected game, its state, any download.</summary>
    public void Refresh()
    {
        LauncherGame game = _model.Current;
        _heading.Title = game.Title;

        _chips.ClearChildren();
        if (game.State == GameState.Unavailable)
        {
            _chips.Add(new Chip(_theme, "Status", "In development"));
        }
        else if (game.State == GameState.NotInstalled)
        {
            _chips.Add(new Chip(_theme, "Download size", game.SizeMegabytes > 0 ? LauncherModel.FormatSize(game.SizeMegabytes) : "—"));
            _chips.Add(new Chip(_theme, "Channel", _model.Settings.Channel == OpenIGI.Releases.ReleaseChannel.Beta ? "Beta" : "Stable"));
        }
        else
        {
            _chips.Add(new Chip(_theme, "Version", game.Version));
            _chips.Add(new Chip(_theme, "On disk", LauncherModel.FormatSize(game.SizeMegabytes)));
            _chips.Add(new Chip(_theme, "Channel", _model.Settings.Channel == OpenIGI.Releases.ReleaseChannel.Beta ? "Beta" : "Stable"));
        }

        _notes.Notes = game.Notes;
        _notes.Tag = game.State is GameState.UpdateAvailable or GameState.NotInstalled ? "NEW UPDATE" : "LATEST RELEASE";
        _notes.Visibility = game.Notes is null ? Visibility.Collapsed : Visibility.Visible;
        _content.Game = game;
        _content.Visibility = game.OptionalPacks.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        Update();
    }

    /// <summary>Updates what changes frame to frame: the main button's progress.</summary>
    public void Update()
    {
        LauncherGame game = _model.Current;
        DownloadJob? job = _model.Job is { } j && j.Covers(game) ? j : null;

        _main.IsEnabled = game.State is not (GameState.Unavailable or GameState.Playing);
        if (job is not null)
        {
            _heading.State = job.Paused ? "PAUSED" : job.Stage switch
            {
                Services.InstallStage.Verifying => "VERIFYING",
                Services.InstallStage.Installing => "INSTALLING",
                _ => "DOWNLOADING",
            };
            _heading.StateColor = _theme.Accent;
            _main.Progress = job.Progress;
            _main.Subtitle = job.Paused ? "CLICK TO RESUME" : "CLICK TO PAUSE";
            return;
        }

        _main.Progress = null;
        switch (game.State)
        {
            case GameState.Unavailable:
                _heading.State = "COMING LATER";
                _heading.StateColor = _theme.TextMuted;
                _main.Title = "SOON";
                _main.Icon = PrimaryActionIcon.Arrow;
                _main.Subtitle = "NOT RELEASED YET";
                break;
            case GameState.Playing:
                _heading.State = "RUNNING";
                _heading.StateColor = _theme.Success;
                _main.Title = "PLAYING";
                _main.Icon = PrimaryActionIcon.Arrow;
                _main.Subtitle = $"VERSION {game.Version}";
                break;
            case GameState.NeedsGameFolder:
                _heading.State = "GAME FILES NEEDED";
                _heading.StateColor = _theme.Accent;
                _main.Title = "SET FOLDER";
                _main.Icon = PrimaryActionIcon.Arrow;
                _main.Subtitle = $"POINT TO YOUR {game.Title.ToUpperInvariant()} INSTALL";
                break;
            case GameState.Damaged:
                _heading.State = "FILES DAMAGED";
                _heading.StateColor = _theme.Danger;
                _main.Title = "REPAIR";
                _main.Icon = PrimaryActionIcon.Download;
                _main.Subtitle = $"DOWNLOAD {game.LatestVersion} AGAIN · {LauncherModel.FormatSize(game.UpdateMegabytes)}";
                break;
            case GameState.NotInstalled:
                _heading.State = "NOT INSTALLED";
                _heading.StateColor = _theme.TextMuted;
                _main.Title = "INSTALL";
                _main.Icon = PrimaryActionIcon.Download;
                _main.Subtitle = game.LatestVersion.Length == 0
                    ? "CHECKING FOR THE LATEST RELEASE"
                    : $"{LauncherModel.FormatSize(game.SizeMegabytes)} · VERSION {game.LatestVersion}";
                break;
            case GameState.UpdateAvailable:
                _heading.State = "UPDATE AVAILABLE";
                _heading.StateColor = _theme.Accent;
                _main.Title = "UPDATE";
                _main.Icon = PrimaryActionIcon.Download;
                _main.Subtitle = $"{game.Version} → {game.LatestVersion} · {LauncherModel.FormatSize(game.UpdateMegabytes)}";
                break;
            default:
                _heading.State = "READY";
                _heading.StateColor = _theme.Success;
                _main.Title = "PLAY";
                _main.Icon = PrimaryActionIcon.Arrow;
                _main.Subtitle = string.Create(CultureInfo.InvariantCulture, $"VERSION {game.Version}");
                break;
        }
    }

    /// <summary>The eyebrow line and the game's name, large.</summary>
    private sealed class HeroHeading(UiTheme theme) : UiElement
    {
        private const string Eyebrow = "IGI 1 · IGI 2 · EDITOR";
        private string _title = string.Empty;

        public string Title
        {
            get => _title;
            set => _title = (value ?? string.Empty).ToUpperInvariant();
        }

        public string State { get; set; } = string.Empty;

        public Bgra32 StateColor { get; set; }

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
            new(available.X, theme.Mono.LineHeight(context.Px(15f)) + context.Px(22f) + context.Px(128f * 0.85f));

        protected override void OnDraw(UiPainter painter)
        {
            LayoutContext context = painter.Context;
            float eyebrowSize = context.Px(15f);
            UiVec2 pen = painter.DrawLabel(theme.Mono, Eyebrow, Bounds.TopLeft, eyebrowSize, theme.Accent, 0.28f);
            float lineY = Bounds.Y + (theme.Mono.LineHeight(eyebrowSize) * 0.5f);
            float ruleX = pen.X + context.Px(14f);
            painter.FillRect(new UiRect(ruleX, lineY, context.Px(40f), context.Px(1f)), UiPalette.AccentAt(0.6f));
            painter.DrawLabel(theme.Mono, State, new UiVec2(ruleX + context.Px(54f), Bounds.Y), eyebrowSize, StateColor, 0.28f);

            float size = context.Px(128f);
            var box = new UiRect(Bounds.X, Bounds.Bottom - (size * 0.85f), Bounds.Width, size * 0.85f);
            var at = new UiVec2(Bounds.X, UiRowStyle.CapsCenterY(theme.Display, size, box));

            // text-shadow: 0 4px 40px rgba(0,0,0,.5) — a soft, low shadow that lifts the name off the picture.
            KitDraw.BlurredText(painter, theme.Display, _title, at + new UiVec2(0f, context.Px(4f)), size, UiPalette.InkAt(0.35f), 0f, context.Px(14f));
            painter.DrawLabel(theme.Display, _title, at, size, theme.Text, 0f);
        }
    }

    /// <summary>The latest release's notes on glass, under a banner tagged with whether it is new to the player.</summary>
    private sealed class PatchNotesCard : UiElement
    {
        /// <summary>The release banner's height; short enough that the card and the content strip fit a 720-high window.</summary>
        private const float BannerHeight = 132f;

        private readonly UiTheme _theme;
        private readonly Button _more;

        public PatchNotesCard(UiTheme theme, Action openNotes)
        {
            _theme = theme;
            _more = new Button(theme, "Full patch notes ›") { Accent = ButtonAccent.Link, TextSize = 16f, Tracking = 0.2f, Padding = new Thickness(0f, 4f) };
            _more.Activated += _ => openNotes();
            AddChild(_more);
        }

        public PatchNotes? Notes { get; set; }

        /// <summary>Gets or sets the banner's tag: NEW UPDATE when there is something to get, LATEST RELEASE otherwise.</summary>
        public string Tag { get; set; } = "LATEST RELEASE";

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
        {
            _more.Measure(context, available);
            int lines = Notes?.Items.Count ?? 0;
            float body = (lines * (_theme.Body.LineHeight(context.Px(16f)) + context.Px(12f))) + context.Px(4f) + _more.DesiredSize.Y;
            return new UiVec2(available.X, context.Px(BannerHeight) + context.Px(16f + 20f) + body);
        }

        protected override void ArrangeContent(LayoutContext context, UiRect contentRect) =>
            _more.Arrange(new UiRect(contentRect.X + context.Px(24f), contentRect.Bottom - context.Px(20f) - _more.DesiredSize.Y, _more.DesiredSize.X, _more.DesiredSize.Y));

        protected override void OnDraw(UiPainter painter)
        {
            LayoutContext context = painter.Context;
            KitDraw.Glass(painter, _theme, Bounds, UiPalette.Ink.WithAlpha(158), 22f);

            // The banner: where the release's key art will go, a warm gradient until then.
            var banner = new UiRect(Bounds.X, Bounds.Y, Bounds.Width, context.Px(BannerHeight));
            Span<UiGradientStop> art = [new(0f, Bgra32.FromRgba(150, 112, 60, 120)), new(0.6f, Bgra32.FromRgba(70, 80, 84, 90)), new(1f, Bgra32.FromRgba(40, 52, 58, 80))];
            painter.FillGradient(banner, art, UiGradientDirection.Horizontal);
            Span<UiGradientStop> fade = [new(0.2f, UiPalette.Ink.WithAlpha(0)), new(1f, UiPalette.Ink.WithAlpha(242))];
            painter.FillGradient(banner, fade, UiGradientDirection.Vertical);

            // NEW UPDATE: an amber tag with its corner cut.
            float tagSize = context.Px(12f);
            float tagWidth = UiPainter.MeasureLabel(_theme.Mono, Tag, tagSize, 0.2f) + context.Px(20f);
            var tag = new UiRect(banner.X + context.Px(24f), banner.Y + context.Px(18f), tagWidth, context.Px(24f));
            painter.FillCutRect(tag, context.Px(6f), UiCorners.BottomRight, _theme.Accent);
            painter.DrawLabel(_theme.Mono, Tag, new UiVec2(tag.X + context.Px(10f), UiRowStyle.CenterY(_theme.Mono, tagSize, tag)), tagSize, _theme.OnAccent, 0.2f);

            if (Notes is not { } notes)
            {
                return;
            }

            float titleSize = context.Px(36f);
            var titleBox = new UiRect(banner.X + context.Px(24f), banner.Bottom - context.Px(14f) - titleSize, banner.Width, titleSize);
            painter.DrawLabel(_theme.Display, notes.Title, new UiVec2(titleBox.X, UiRowStyle.CapsCenterY(_theme.Display, titleSize, titleBox)), titleSize, _theme.Text, 0.04f);
            float dateSize = context.Px(13f);
            float dateWidth = UiPainter.MeasureLabel(_theme.Mono, notes.Date, dateSize, 0.16f);
            painter.DrawLabel(_theme.Mono, notes.Date, new UiVec2(banner.Right - context.Px(24f) - dateWidth, titleBox.Bottom - _theme.Mono.LineHeight(dateSize)), dateSize, _theme.TextSecondary, 0.16f);

            float textSize = context.Px(16f);
            float y = banner.Bottom + context.Px(16f);
            float x = Bounds.X + context.Px(24f);
            foreach (PatchNote note in notes.Items)
            {
                Bgra32 color = note.Kind switch
                {
                    PatchNoteKind.Change => _theme.Info,
                    PatchNoteKind.Fix => _theme.Success,
                    _ => _theme.Accent,
                };
                float line = _theme.Body.LineHeight(textSize);
                painter.FillDiamond(new UiVec2(x + context.Px(3.5f), y + (line * 0.5f)), context.Px(7f), color);
                painter.DrawText(_theme.Body, note.Text, new UiVec2(x + context.Px(21f), y), textSize, _theme.TextLabel);
                y += line + context.Px(12f);
            }
        }
    }

    /// <summary>The optional-content summary: how many packs are in, with a strip of their tiles.</summary>
    private sealed class ContentCard(UiTheme theme) : Control(theme)
    {
        public LauncherGame? Game { get; set; }

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
            new(available.X, context.Px(16f + 19f + 3f) + Theme.Mono.LineHeight(context.Px(13f)) + context.Px(14f + 44f + 16f));

        protected override void OnDraw(UiPainter painter)
        {
            LayoutContext context = painter.Context;
            float hot = MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f);
            KitDraw.Glass(painter, Theme, Bounds, UiPalette.Ink.WithAlpha(158), 22f, UI.Animation.Interpolate.Lerp(Theme.Border, UiPalette.AccentAt(0.5f), hot));
            if (Game is not { } game)
            {
                return;
            }

            float x = Bounds.X + context.Px(20f);
            float y = Bounds.Y + context.Px(16f);
            float titleSize = context.Px(19f);
            painter.DrawLabel(Theme.Heading, "OPTIONAL CONTENT", new UiVec2(x, UiRowStyle.CapsCenterY(Theme.Heading, titleSize, new UiRect(x, y, 0f, titleSize))), titleSize, Theme.Text, 0.08f);
            float summarySize = context.Px(13f);
            string summary = string.Create(CultureInfo.InvariantCulture, $"{game.PacksInstalled} of {game.OptionalPacks.Count} packs installed");
            painter.DrawLabel(Theme.Mono, summary, new UiVec2(x, y + titleSize + context.Px(3f)), summarySize, Theme.TextMuted, 0.12f);
            KitIcons.Draw(painter, KitIcon.ChevronRight, new UiVec2(Bounds.Right - context.Px(26f), y + context.Px(18f)), context.Px(14f), Theme.Accent);

            int count = Math.Max(1, game.OptionalPacks.Count);
            float gap = context.Px(6f);
            float tileTop = Bounds.Bottom - context.Px(16f) - context.Px(44f);
            float tileWidth = ((Bounds.Width - context.Px(40f)) - (gap * 4f)) / 5f;
            for (int i = 0; i < Math.Min(5, count); i++)
            {
                bool installed = i < game.PacksInstalled;
                var tile = new UiRect(x + (i * (tileWidth + gap)), tileTop, tileWidth, context.Px(44f));
                Span<UiGradientStop> art = installed
                    ? [new(0f, Bgra32.FromRgba(150, 112, 60, 150)), new(1f, Bgra32.FromRgba(60, 74, 80, 150))]
                    : [new(0f, Bgra32.FromRgba(60, 62, 64, 150)), new(1f, Bgra32.FromRgba(30, 32, 34, 150))];
                painter.FillGradient(tile, art, (i & 1) == 0 ? UiGradientDirection.Horizontal : UiGradientDirection.Vertical);
                painter.StrokeRect(tile, context.Px(1f), installed ? UiPalette.AccentAt(0.6f) : UiPalette.White(0.1f));
            }
        }
    }
}
