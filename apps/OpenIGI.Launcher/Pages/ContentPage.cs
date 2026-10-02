using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Launcher.Pages;

/// <summary>
/// The optional content: one glass panel per pack, in the settings' ruled row style — its picture, what it
/// is, where it stands, the one thing to do about it now (install, update, repair, pause), whether the game
/// uses it, and checking or removing its files.
/// </summary>
internal sealed class ContentPage
{
    private readonly UiTheme _theme;
    private readonly LauncherModel _model;
    private readonly StackPanel _list;
    private readonly Text _empty;
    private readonly List<PackCard> _cards = [];
    private readonly Func<string, BackdropPicture?> _pictures;

    public ContentPage(UiTheme theme, LauncherModel model, Func<string, BackdropPicture?> pictures)
    {
        _theme = theme;
        _model = model;
        _pictures = pictures;

        _list = new StackPanel { Orientation = Orientation.Vertical, Spacing = 20f, VerticalAlignment = VerticalAlignment.Top };
        _empty = new Text(theme.Mono, "No optional content is published yet", 15f, theme.TextDim, 0.2f, uppercase: true)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0f, 40f, 0f, 0f),
        };

        var scroll = new ScrollView(theme);
        scroll.SetContent(_list);

        var page = new Grid { RowSpacing = 24f, Padding = new Thickness(48f, 36f, 48f, 28f) };
        page.ColumnDefinitions.Add(GridLength.Star);
        page.RowDefinitions.Add(GridLength.Auto);
        page.RowDefinitions.Add(GridLength.Star);
        page.Add(new SectionTitle(theme, "Optional content", "Content") { HorizontalAlignment = HorizontalAlignment.Left }, 0, 0);
        page.Add(scroll, 1, 0);
        Root = page;
        Refresh();
    }

    public UiElement Root { get; }

    /// <summary>Re-reads the packs: rebuilds the panels when the list changed, then their rows.</summary>
    public void Refresh()
    {
        if (!_cards.Select(static c => c.Id).SequenceEqual(_model.Packs.Select(static p => p.Pack.Id)))
        {
            _list.ClearChildren();
            _cards.Clear();
            foreach (ContentEntry entry in _model.Packs)
            {
                var card = new PackCard(_theme, _model, entry.Pack.Id, _pictures);
                _cards.Add(card);
                _list.AddChild(card.Root);
            }

            if (_cards.Count == 0)
            {
                _list.AddChild(_empty);
            }
        }

        foreach (PackCard card in _cards)
        {
            card.Refresh();
        }
    }

    /// <summary>Updates what changes frame to frame: a running install's progress.</summary>
    public void Update()
    {
        foreach (PackCard card in _cards)
        {
            card.Update();
        }
    }

    private sealed class PackCard
    {
        private readonly LauncherModel _model;
        private readonly Text _summary;
        private readonly Text _status;
        private readonly ProgressBar _progress;
        private readonly ActionRow _main;
        private readonly Toggle _enabled;
        private readonly ActionRow _verify;
        private readonly ActionRow _remove;
        private readonly GroupHeader _header;
        private readonly PackPicture _picture;
        private readonly Func<string, BackdropPicture?> _pictures;
        private bool _syncing;

        public PackCard(UiTheme theme, LauncherModel model, string id, Func<string, BackdropPicture?> pictures)
        {
            _model = model;
            _pictures = pictures;
            Id = id;
            _picture = new PackPicture(theme) { Width = 256f, Height = 144f, VerticalAlignment = VerticalAlignment.Top };

            _header = new GroupHeader(theme, id) { Margin = new Thickness(0f, 0f, 0f, 8f) };
            _summary = new Text(theme.Body, string.Empty, 15f, theme.Text) { Margin = new Thickness(0f, 0f, 0f, 6f) };
            _status = new Text(theme.Mono, string.Empty, 12f, theme.TextMuted, 0.14f, uppercase: true) { Margin = new Thickness(0f, 0f, 0f, 10f) };
            _progress = new ProgressBar(theme) { Margin = new Thickness(0f, 0f, 0f, 10f), Visibility = Visibility.Collapsed };

            _main = new ActionRow(theme, "Install", "Install");
            _main.Activated += _ => Act();

            _enabled = new Toggle(theme, "Use in game", false) { Hint = "Start the game with this pack; turning it off keeps its files" };
            _enabled.ValueChanged += on =>
            {
                if (!_syncing)
                {
                    _model.SetContentEnabled(Id, on);
                }
            };

            _verify = new ActionRow(theme, "Check files", "Verify") { Hint = "Re-checks every file against the published pack" };
            _verify.Activated += _ => _model.VerifyContent(Id);

            _remove = new ActionRow(theme, "Remove from disk", "Remove") { Hint = "Deletes the pack's files; it can be installed again at any time" };
            _remove.Activated += _ => _model.RemoveContent(Id);

            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0f };
            stack.AddChild(_header);
            stack.AddChild(_summary);
            stack.AddChild(_status);
            stack.AddChild(_progress);
            foreach (UiElement row in new UiElement[] { _main, _enabled, _verify, _remove })
            {
                row.Margin = new Thickness(-10f, 0f);
                stack.AddChild(row);
            }

            // The picture beside everything else, as a store lists a game.
            var layout = new Grid { ColumnSpacing = 26f };
            layout.ColumnDefinitions.Add(GridLength.Auto);
            layout.ColumnDefinitions.Add(GridLength.Star);
            layout.RowDefinitions.Add(GridLength.Auto);
            layout.Add(_picture, 0, 0);
            layout.Add(stack, 0, 1);

            var panel = new GlassPanel(theme)
            {
                Tint = UiPalette.Ink.WithAlpha(179),
                Padding = new Thickness(22f, 22f, 26f, 14f),
                VerticalAlignment = VerticalAlignment.Top,
            };
            panel.AddChild(layout);
            Root = panel;
        }

        public string Id { get; }

        public UiElement Root { get; }

        private ContentEntry? Entry => _model.Packs.FirstOrDefault(p => p.Pack.Id == Id);

        public void Refresh()
        {
            if (Entry is not { } entry)
            {
                return;
            }

            _header.Title = entry.Pack.Title;
            _summary.Value = entry.Pack.Summary;
            _picture.Picture = entry.ThumbnailPath is { } path ? _pictures(path) : null;

            List<string> facts = [];
            facts.Add(entry.Installed is { } installed ? $"Version {installed.Version}" : $"Version {entry.Pack.Version}");
            facts.Add(entry.Installed is null
                ? $"{LauncherModel.FormatSize(entry.Pack.Size / 1e6)} installed"
                : $"{LauncherModel.FormatSize(entry.Installed.Size / 1e6)} on disk");
            facts.Add(entry.State switch
            {
                ContentState.UpdateAvailable => $"Update {entry.Pack.Version} available",
                ContentState.Damaged => $"{entry.Damaged.Count} damaged file(s)",
                ContentState.Incompatible => $"Needs OpenIGI {entry.Pack.MinGameVersion} or newer",
                ContentState.Installed when !entry.Listed => "No longer offered",
                ContentState.Installed => entry.Enabled ? "In use" : "Turned off",
                ContentState.Working => "Working",
                _ => "Not installed",
            });
            _status.Value = string.Join("  ·  ", facts);

            _syncing = true;
            _enabled.Value = entry.Enabled;
            _syncing = false;
            _enabled.IsEnabled = entry.Installed is not null;
            _verify.IsEnabled = entry.Installed is not null && entry.State != ContentState.Working;
            _remove.IsEnabled = entry.Installed is not null && entry.State != ContentState.Working;
            Update();
        }

        public void Update()
        {
            if (Entry is not { } entry)
            {
                return;
            }

            DownloadJob? job = _model.Job is { } j && j.ContentId == Id ? j : null;
            _progress.Visibility = job is null ? Visibility.Collapsed : Visibility.Visible;

            if (job is not null)
            {
                _progress.Value = job.Progress;
                _main.Caption = job.Paused ? "Paused" : job.Stage == Services.InstallStage.Installing ? "Installing" : "Downloading";
                _main.Hint = string.Create(CultureInfo.InvariantCulture,
                    $"{LauncherModel.FormatSize(job.Done)} of {LauncherModel.FormatSize(job.Size)}{(job.Paused ? string.Empty : $"  ·  {job.Speed:0.0} MB/s")}");
                _main.Action = job.Paused ? "Resume" : "Pause";
                _main.IsEnabled = true;
                return;
            }

            string size = entry.DownloadBytes < 0 ? "…" : LauncherModel.FormatSize(entry.DownloadBytes / 1e6);
            (string caption, string action, string hint, bool enabled) = entry.State switch
            {
                ContentState.Available => ("Install", $"Install · {size}", "Downloads the pack into the launcher's library", true),
                ContentState.UpdateAvailable => ("Update", $"Update · {size}", $"Only what changed since {entry.Installed?.Version} is downloaded", true),
                ContentState.Damaged => ("Repair", $"Repair · {size}", "Restores the damaged files, reusing every intact part", true),
                ContentState.Incompatible => ("Not available", "Update the game", $"This pack needs OpenIGI {entry.Pack.MinGameVersion} or newer", false),
                ContentState.Working => ("Working", "Busy", "Checking or removing the pack's files", false),
                _ => ("Installed", "Up to date", $"Version {entry.Installed?.Version}", false),
            };
            _main.Caption = caption;
            _main.Action = action;
            _main.Hint = hint;
            _main.IsEnabled = enabled;
        }

        private void Act()
        {
            if (_model.Job is { } job && job.ContentId == Id)
            {
                _model.TogglePause();
            }
            else
            {
                _model.InstallContent(Id);
            }
        }
    }

    // A pack's picture, cropped to fill its box; a quiet gradient while it has none.
    private sealed class PackPicture(UiTheme theme) : UiElement
    {
        public BackdropPicture? Picture { get; set; }

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => UiVec2.Zero;

        protected override void OnDraw(UiPainter painter)
        {
            UiRect box = Bounds;
            if (Picture is not { } picture)
            {
                painter.FillRect(box, Bgra32.FromRgba(60, 74, 80, 120));
                painter.FillRect(new UiRect(box.X, box.Bottom - painter.Context.Px(3f), box.Width, painter.Context.Px(3f)), theme.Accent.WithAlpha(110));
                return;
            }

            // Cover: the picture's own shape, cut down to the box's on whichever side it is longer.
            float aspect = box.Width / MathF.Max(1f, box.Height);
            UiRect source = picture.Aspect > aspect
                ? new UiRect((1f - (aspect / picture.Aspect)) * 0.5f, 0f, aspect / picture.Aspect, 1f)
                : new UiRect(0f, (1f - (picture.Aspect / aspect)) * 0.5f, 1f, picture.Aspect / aspect);
            painter.DrawSprite(picture.Image, box, source, Bgra32.FromRgb(255, 255, 255));
            painter.FillRect(new UiRect(box.X, box.Bottom - painter.Context.Px(3f), box.Width, painter.Context.Px(3f)), theme.Accent.WithAlpha(160));
        }
    }
}
