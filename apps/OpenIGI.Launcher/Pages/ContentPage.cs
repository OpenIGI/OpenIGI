using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Launcher.Pages;

/// <summary>
/// The optional content, as the design lays it out: the game's name over CONTENT with the library's share of
/// its drive beside it, then one glass row per pack — its picture, name and summary, size, state, and the one
/// thing to do about it now, with the lesser actions (verify, turn off, pause, cancel) under it.
/// </summary>
internal sealed class ContentPage
{
    private readonly UiTheme _theme;
    private readonly LauncherModel _model;
    private readonly StackPanel _list;
    private readonly Text _empty;
    private readonly SectionTitle _title;
    private readonly DiskMeter _disk;
    private readonly List<PackRow> _rows = [];
    private readonly Func<string, BackdropPicture?> _pictures;

    public ContentPage(UiTheme theme, LauncherModel model, Func<string, BackdropPicture?> pictures)
    {
        _theme = theme;
        _model = model;
        _pictures = pictures;

        _list = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8f, VerticalAlignment = VerticalAlignment.Top };
        _empty = new Text(theme.Mono, "No optional content is published yet", 15f, theme.TextDim, 0.2f, uppercase: true)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0f, 40f, 0f, 0f),
        };

        _title = new SectionTitle(theme, model.Current.Title.ToUpperInvariant(), "Content")
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        _disk = new DiskMeter(theme) { Width = 680f, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };

        var header = new Grid { ColumnSpacing = 40f };
        header.ColumnDefinitions.Add(GridLength.Star);
        header.ColumnDefinitions.Add(GridLength.Auto);
        header.RowDefinitions.Add(GridLength.Auto);
        header.Add(_title, 0, 0);
        header.Add(_disk, 0, 1);

        var scroll = new ScrollView(theme);
        scroll.SetContent(_list);

        var page = new Grid { RowSpacing = 24f, Padding = new Thickness(48f, 36f, 48f, 28f) };
        page.ColumnDefinitions.Add(GridLength.Star);
        page.RowDefinitions.Add(GridLength.Auto);
        page.RowDefinitions.Add(GridLength.Star);
        page.Add(header, 0, 0);
        page.Add(scroll, 1, 0);
        Root = page;
        Refresh();
    }

    public UiElement Root { get; }

    /// <summary>Re-reads the packs: rebuilds the rows when the list changed, then what each row says.</summary>
    public void Refresh()
    {
        _title.Eyebrow = _model.Current.Title.ToUpperInvariant();
        _disk.Describe(_model.Settings.Library, _model.LibraryBytes);

        if (!_rows.Select(static r => r.PackId).SequenceEqual(_model.Packs.Select(static p => p.Pack.Id)))
        {
            _list.ClearChildren();
            _rows.Clear();
            foreach (ContentEntry entry in _model.Packs)
            {
                var row = new PackRow(_theme, _model, entry.Pack.Id, _pictures);
                _rows.Add(row);
                _list.AddChild(row);
            }

            if (_rows.Count == 0)
            {
                _list.AddChild(_empty);
            }

            // The screen plays its controls' entrance once, when it opens; rows that arrive with the content
            // list come later and play their own, or they would stay invisible.
            int index = 0;
            foreach (Control control in _list.Descendants().OfType<Control>())
            {
                control.PlayEnter(MathF.Min(index++ * 0.03f, 0.3f));
            }
        }

        foreach (PackRow row in _rows)
        {
            row.Refresh();
        }
    }

    /// <summary>Updates what changes frame to frame: a running install's progress.</summary>
    public void Update()
    {
        foreach (PackRow row in _rows)
        {
            row.Update();
        }
    }

    // One pack: picture, name and summary, size, state, actions — the design's five columns.
    private sealed class PackRow : Grid
    {
        private readonly UiTheme _theme;
        private readonly LauncherModel _model;
        private readonly Func<string, BackdropPicture?> _pictures;
        private readonly Thumbnail _thumbnail;
        private readonly PackInfo _info;
        private readonly Labelled _size;
        private readonly StateLabel _state;
        private readonly BusyBar _busy;
        private readonly CutButton _main;
        private readonly FlexPanel _links;
        private readonly List<(LinkButton Button, Action Act)> _linkSlots = [];
        private Action? _mainAction;
        private bool _working;

        public PackRow(UiTheme theme, LauncherModel model, string id, Func<string, BackdropPicture?> pictures)
        {
            _theme = theme;
            _model = model;
            _pictures = pictures;
            PackId = id;

            ColumnSpacing = 24f;
            Padding = new Thickness(12f, 12f, 20f, 12f);
            MinHeight = 96f;
            ColumnDefinitions.Add(GridLength.Absolute(150f));
            ColumnDefinitions.Add(GridLength.Star);
            ColumnDefinitions.Add(GridLength.Absolute(110f));
            ColumnDefinitions.Add(GridLength.Absolute(180f));
            ColumnDefinitions.Add(GridLength.Absolute(200f));
            RowDefinitions.Add(GridLength.Auto);

            _thumbnail = new Thumbnail(theme) { Height = 72f, VerticalAlignment = VerticalAlignment.Center };
            _info = new PackInfo(theme) { VerticalAlignment = VerticalAlignment.Center };
            _size = new Labelled(theme, "SIZE") { VerticalAlignment = VerticalAlignment.Center };
            _state = new StateLabel(theme) { VerticalAlignment = VerticalAlignment.Center };

            _busy = new BusyBar(theme) { Visibility = Visibility.Collapsed };
            _main = new CutButton(theme) { Height = 44f };
            _main.Activated += _ => _mainAction?.Invoke();
            _links = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 14f, HorizontalAlignment = HorizontalAlignment.Left };
            for (int i = 0; i < 3; i++)
            {
                int slot = i;
                var link = new LinkButton(theme);
                link.Activated += _ => _linkSlots[slot].Act();
                _linkSlots.Add((link, static () => { }));
                _links.Add(link);
            }

            var actions = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6f, VerticalAlignment = VerticalAlignment.Center };
            actions.AddChild(_busy);
            actions.AddChild(_main);
            actions.AddChild(_links);

            Add(_thumbnail, 0, 0);
            Add(_info, 0, 1);
            Add(_size, 0, 2);
            Add(_state, 0, 3);
            Add(actions, 0, 4);
        }

        public string PackId { get; }

        private ContentEntry? Entry => _model.Packs.FirstOrDefault(p => p.Pack.Id == PackId);

        public void Refresh()
        {
            if (Entry is not { } entry)
            {
                return;
            }

            bool installed = entry.Installed is not null;
            _thumbnail.Picture = entry.ThumbnailPath is { } path ? _pictures(path) : null;
            _thumbnail.Short = Short(entry.Pack.Id);
            _thumbnail.Dimmed = !installed || !entry.Enabled;
            _info.Name = entry.Pack.Title;
            _info.Description = entry.Pack.Summary;
            _size.Value = LauncherModel.FormatSize((entry.Installed?.Size ?? entry.Pack.Size) / 1e6);
            Update();
        }

        public void Update()
        {
            if (Entry is not { } entry)
            {
                return;
            }

            DownloadJob? job = _model.Job is { } j && j.ContentId == PackId ? j : null;
            _working = job is not null || entry.State == ContentState.Working;
            Bgra32 accent = _theme.Accent;

            if (job is not null)
            {
                _state.Set(job.Paused ? "PAUSED" : job.Stage == Services.InstallStage.Installing ? "INSTALLING" : "DOWNLOADING", accent);
                _busy.Visibility = Visibility.Visible;
                _busy.Value = job.Progress;
                _busy.Left = job.Paused ? "Paused" : $"{LauncherModel.FormatSize(job.Done)} / {LauncherModel.FormatSize(job.Size)}";
                _busy.Right = string.Create(CultureInfo.InvariantCulture, $"{Math.Round(job.Progress * 100f)}%");
                _main.Visibility = Visibility.Collapsed;
                Links((job.Paused ? "RESUME" : "PAUSE", _model.TogglePause), ("CANCEL", _model.Cancel));
                return;
            }

            _busy.Visibility = Visibility.Collapsed;
            _main.Visibility = Visibility.Visible;
            string update = entry.DownloadBytes < 0 ? string.Empty : $" · {LauncherModel.FormatSize(entry.DownloadBytes / 1e6)}";
            switch (entry.State)
            {
                case ContentState.Available:
                    _state.Set("NOT INSTALLED", _theme.TextMuted);
                    Main("INSTALL", CutButtonStyle.Filled, () => _model.InstallContent(PackId));
                    Links();
                    break;
                case ContentState.UpdateAvailable:
                    _state.Set("UPDATE" + update, accent);
                    Main("UPDATE", CutButtonStyle.Filled, () => _model.InstallContent(PackId));
                    Links(("VERIFY", () => _model.VerifyContent(PackId)), Toggle(entry), ("REMOVE", () => _model.RemoveContent(PackId)));
                    break;
                case ContentState.Damaged:
                    _state.Set("DAMAGED", _theme.Danger);
                    Main("REPAIR", CutButtonStyle.Filled, () => _model.InstallContent(PackId));
                    Links(("REMOVE", () => _model.RemoveContent(PackId)));
                    break;
                case ContentState.Incompatible:
                    _state.Set("NEEDS NEWER GAME", _theme.TextMuted);
                    Main($"NEEDS {entry.Pack.MinGameVersion}", CutButtonStyle.Disabled, null);
                    Links(entry.Installed is not null ? [("REMOVE", () => _model.RemoveContent(PackId))] : []);
                    break;
                case ContentState.Working:
                    _state.Set("WORKING", accent);
                    Main("WORKING", CutButtonStyle.Disabled, null);
                    Links();
                    break;
                default:
                    _state.Set(
                        !entry.Listed ? "NO LONGER OFFERED" : entry.Enabled ? "INSTALLED" : "TURNED OFF",
                        entry.Enabled && entry.Listed ? _theme.Success : _theme.TextMuted);
                    Main("REMOVE", CutButtonStyle.Danger, () => _model.RemoveContent(PackId));
                    Links(("VERIFY", () => _model.VerifyContent(PackId)), Toggle(entry), ("FOLDER", () => _model.OpenContentFolder(PackId)));
                    break;
            }
        }

        protected override void OnDraw(UiPainter painter)
        {
            // The design's row: dark glass, a hairline that warms while the pack is being worked on.
            Bgra32 border = _working ? _theme.Accent.WithAlpha(128) : UiPalette.White(0.08f);
            KitDraw.Glass(painter, _theme, Bounds, UiPalette.Ink.WithAlpha(168), null, border);
        }

        private (string, Action) Toggle(ContentEntry entry) =>
            entry.Enabled ? ("TURN OFF", () => _model.SetContentEnabled(PackId, false)) : ("TURN ON", () => _model.SetContentEnabled(PackId, true));

        private void Main(string text, CutButtonStyle style, Action? act)
        {
            _main.Text = text;
            _main.Style = style;
            _main.IsEnabled = act is not null;
            _mainAction = act;
        }

        private void Links(params (string Text, Action Act)[] links)
        {
            for (int i = 0; i < _linkSlots.Count; i++)
            {
                LinkButton button = _linkSlots[i].Button;
                if (i < links.Length)
                {
                    button.Text = links[i].Text;
                    button.Visibility = Visibility.Visible;
                    _linkSlots[i] = (button, links[i].Act);
                }
                else
                {
                    button.Visibility = Visibility.Collapsed;
                }
            }

            _links.Visibility = links.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        // The label over the picture: the id's first word, as the design's short codes ("HD").
        private static string Short(string id) => id.Split('-')[0].ToUpperInvariant();
    }

    // The pack's picture, cropped to fill its box, its short code over the corner; dimmed while it is not in use.
    private sealed class Thumbnail(UiTheme theme) : UiElement
    {
        public BackdropPicture? Picture { get; set; }

        public string Short { get; set; } = string.Empty;

        public bool Dimmed { get; set; }

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(72f));

        protected override void OnDraw(UiPainter painter)
        {
            LayoutContext context = painter.Context;
            UiRect box = Bounds;
            painter.FillRect(box, Bgra32.FromRgb(20, 22, 24));
            if (Picture is { } picture)
            {
                float aspect = box.Width / MathF.Max(1f, box.Height);
                UiRect source = picture.Aspect > aspect
                    ? new UiRect((1f - (aspect / picture.Aspect)) * 0.5f, 0f, aspect / picture.Aspect, 1f)
                    : new UiRect(0f, (1f - (picture.Aspect / aspect)) * 0.5f, 1f, picture.Aspect / aspect);
                painter.DrawSprite(picture.Image, box, source, Bgra32.FromRgb(255, 255, 255));
            }
            else
            {
                painter.FillGradient(box, [new(0f, Bgra32.FromRgba(150, 112, 60, 110)), new(1f, Bgra32.FromRgba(60, 74, 80, 110))], UiGradientDirection.Horizontal);
            }

            if (Dimmed)
            {
                painter.FillRect(box, UiPalette.Ink.WithAlpha(130));
            }

            float size = context.Px(17f);
            var at = new UiVec2(box.X + context.Px(8f), box.Bottom - context.Px(6f) - theme.Display.LineHeight(size));
            painter.DrawTextShadowed(theme.Display, Short, at, size, Bgra32.FromRgb(255, 255, 255), UiPalette.Ink.WithAlpha(200), new UiVec2(0f, context.Px(1f)));
        }
    }

    // Name over summary; the summary is cut with an ellipsis where the column ends.
    private sealed class PackInfo(UiTheme theme) : UiElement
    {
        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
            new(available.X, theme.Heading.LineHeight(context.Px(23f)) + context.Px(6f) + theme.Body.LineHeight(context.Px(17f)));

        protected override void OnDraw(UiPainter painter)
        {
            LayoutContext context = painter.Context;
            float nameSize = context.Px(23f);
            float bodySize = context.Px(17f);
            float block = theme.Heading.LineHeight(nameSize) + context.Px(6f) + theme.Body.LineHeight(bodySize);
            float y = Bounds.Center.Y - (block * 0.5f);
            painter.PushClip(Bounds);
            painter.DrawLabel(theme.Heading, Name.ToUpperInvariant(), new UiVec2(Bounds.X, y), nameSize, theme.Text, 0.06f);
            string text = Fit(theme.Body, Description, bodySize, Bounds.Width);
            painter.DrawLabel(theme.Body, text, new UiVec2(Bounds.X, y + theme.Heading.LineHeight(nameSize) + context.Px(6f)), bodySize, Bgra32.FromRgb(169, 168, 163), 0f);
            painter.PopClip();
        }

        private static string Fit(UI.Text.UiFont font, string text, float size, float width)
        {
            if (UiPainter.MeasureLabel(font, text, size, 0f) <= width)
            {
                return text;
            }

            int length = text.Length;
            while (length > 0 && UiPainter.MeasureLabel(font, string.Concat(text.AsSpan(0, length), "…"), size, 0f) > width)
            {
                length--;
            }

            return string.Concat(text.AsSpan(0, length).TrimEnd(), "…");
        }
    }

    // A small grey caption over a value: the design's SIZE column.
    private sealed class Labelled(UiTheme theme, string caption) : UiElement
    {
        public string Value { get; set; } = string.Empty;

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
            new(available.X, theme.Mono.LineHeight(context.Px(13f)) + context.Px(4f) + theme.Heading.LineHeight(context.Px(20f)));

        protected override void OnDraw(UiPainter painter)
        {
            LayoutContext context = painter.Context;
            float captionSize = context.Px(13f);
            float valueSize = context.Px(20f);
            float block = theme.Mono.LineHeight(captionSize) + context.Px(4f) + theme.Heading.LineHeight(valueSize);
            float y = Bounds.Center.Y - (block * 0.5f);
            painter.DrawLabel(theme.Mono, caption, new UiVec2(Bounds.X, y), captionSize, Bgra32.FromRgb(110, 112, 115), 0.22f);
            painter.DrawLabel(theme.Heading, Value, new UiVec2(Bounds.X, y + theme.Mono.LineHeight(captionSize) + context.Px(4f)), valueSize, theme.Text, 0.06f);
        }
    }

    // A diamond and a word, in the state's colour.
    private sealed class StateLabel(UiTheme theme) : UiElement
    {
        private string _text = string.Empty;
        private Bgra32 _color;

        public void Set(string text, Bgra32 color)
        {
            _text = text;
            _color = color;
        }

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, theme.Mono.LineHeight(context.Px(14f)));

        protected override void OnDraw(UiPainter painter)
        {
            LayoutContext context = painter.Context;
            float size = context.Px(14f);
            painter.FillDiamond(new UiVec2(Bounds.X + context.Px(4f), Bounds.Center.Y), context.Px(6f), _color);
            painter.DrawLabel(theme.Mono, _text, new UiVec2(Bounds.X + context.Px(16f), UiRowStyle.CenterY(theme.Mono, size, Bounds)), size, _color, 0.18f);
        }
    }

    // While a pack is downloading or installing: how much, and a thin bar.
    private sealed class BusyBar(UiTheme theme) : UiElement
    {
        public float Value { get; set; }

        public string Left { get; set; } = string.Empty;

        public string Right { get; set; } = string.Empty;

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
            new(available.X, theme.Mono.LineHeight(context.Px(14f)) + context.Px(10f));

        protected override void OnDraw(UiPainter painter)
        {
            LayoutContext context = painter.Context;
            float size = context.Px(14f);
            painter.DrawLabel(theme.Mono, Left, new UiVec2(Bounds.X, Bounds.Y), size, theme.Text, 0f);
            float width = UiPainter.MeasureLabel(theme.Mono, Right, size, 0f);
            painter.DrawLabel(theme.Mono, Right, new UiVec2(Bounds.Right - width, Bounds.Y), size, theme.Text, 0f);
            var track = new UiRect(Bounds.X, Bounds.Bottom - context.Px(4f), Bounds.Width, context.Px(4f));
            painter.FillRect(track, UiPalette.White(0.1f));
            painter.FillRect(new UiRect(track.X, track.Y, track.Width * Math.Clamp(Value, 0f, 1f), track.Height), theme.Accent);
        }
    }

    private enum CutButtonStyle
    {
        Filled,
        Outline,
        Danger,
        Disabled,
    }

    // The design's action button: 44 high, its bottom-right corner cut, filled amber or outlined.
    private sealed class CutButton(UiTheme theme) : Control(theme)
    {
        public string Text { get; set; } = string.Empty;

        public CutButtonStyle Style { get; set; }

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(44f));

        protected override void OnDraw(UiPainter painter)
        {
            LayoutContext context = painter.Context;
            float appear = Visual.Appear;
            float hot = IsEnabled ? MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f) : 0f;
            float cut = context.Px(10f);
            (Bgra32 Fill, Bgra32 Border, Bgra32 Ink) colors = Style switch
            {
                CutButtonStyle.Filled => (Interpolate.Lerp(Theme.Accent, Theme.AccentHover, hot), Theme.Accent, Theme.OnAccent),
                CutButtonStyle.Danger => (Theme.Danger.WithAlpha((byte)(30 * hot)), Bgra32.FromRgba(214, 92, 72, 128), Bgra32.FromRgb(224, 138, 120)),
                CutButtonStyle.Outline => (UiPalette.White(0.06f * hot), UiPalette.White(0.2f), Theme.Text),
                _ => (UiPalette.White(0f), UiPalette.White(0.12f), Bgra32.FromRgb(110, 112, 115)),
            };
            (Bgra32 fill, Bgra32 border, Bgra32 ink) = colors;

            painter.FillCutRect(Bounds, cut, UiCorners.BottomRight, UiPalette.Fade(fill, appear));
            painter.StrokeCutRect(Bounds, cut, UiCorners.BottomRight, context.Px(1f), UiPalette.Fade(border, appear));
            float size = context.Px(18f);
            float width = UiPainter.MeasureLabel(Theme.Display, Text, size, 0.2f);
            var at = new UiVec2(Bounds.Center.X - (width * 0.5f), UiRowStyle.CapsCenterY(Theme.Display, size, Bounds));
            painter.DrawLabel(Theme.Display, Text, at, size, UiPalette.Fade(ink, appear), 0.2f);
        }
    }

    // A lesser action under the button: a small word that lights up under the pointer.
    private sealed class LinkButton(UiTheme theme) : Control(theme)
    {
        public string Text { get; set; } = string.Empty;

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
            new(UiPainter.MeasureLabel(Theme.Mono, Text, context.Px(12f), 0.16f), Theme.Mono.LineHeight(context.Px(12f)) + context.Px(4f));

        protected override void OnDraw(UiPainter painter)
        {
            float hot = MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f);
            float size = painter.Context.Px(12f);
            Bgra32 ink = Interpolate.Lerp(Theme.TextMuted, Theme.Accent, hot);
            painter.DrawLabel(Theme.Mono, Text, new UiVec2(Bounds.X, UiRowStyle.CenterY(Theme.Mono, size, Bounds)), size, UiPalette.Fade(ink, Visual.Appear), 0.16f);
        }
    }

    // The library's share of its drive: used and free over a bar, this game's part in amber.
    private sealed class DiskMeter(UiTheme theme) : UiElement
    {
        private string _used = string.Empty;
        private string _free = string.Empty;
        private float _other;
        private float _game;

        public void Describe(string library, long libraryBytes)
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(library.Trim())) ?? library);
                double total = drive.TotalSize;
                double free = drive.AvailableFreeSpace;
                _used = $"LIBRARY {LauncherModel.FormatSize(libraryBytes / 1e6)} · DRIVE {drive.Name.TrimEnd('\\', '/')}";
                _free = $"{LauncherModel.FormatSize(free / 1e6)} FREE OF {LauncherModel.FormatSize(total / 1e6)}";
                _game = (float)Math.Clamp(libraryBytes / total, 0d, 1d);
                _other = (float)Math.Clamp(((total - free) / total) - _game, 0d, 1d);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                _used = $"LIBRARY {LauncherModel.FormatSize(libraryBytes / 1e6)}";
                _free = string.Empty;
                _game = _other = 0f;
            }
        }

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
            new(context.Px(680f), theme.Mono.LineHeight(context.Px(14f)) + context.Px(8f + 6f + 8f) + theme.Mono.LineHeight(context.Px(13f)));

        protected override void OnDraw(UiPainter painter)
        {
            LayoutContext context = painter.Context;
            float size = context.Px(14f);
            float small = context.Px(13f);
            Bgra32 muted = Bgra32.FromRgb(140, 142, 144);
            float y = Bounds.Y;
            painter.DrawLabel(theme.Mono, _used, new UiVec2(Bounds.X, y), size, muted, 0.16f);
            float freeWidth = UiPainter.MeasureLabel(theme.Mono, _free, size, 0.16f);
            painter.DrawLabel(theme.Mono, _free, new UiVec2(Bounds.Right - freeWidth, y), size, muted, 0.16f);

            y += theme.Mono.LineHeight(size) + context.Px(8f);
            var bar = new UiRect(Bounds.X, y, Bounds.Width, context.Px(6f));
            painter.FillRect(bar, UiPalette.White(0.1f));
            painter.FillRect(new UiRect(bar.X, bar.Y, bar.Width * _other, bar.Height), UiPalette.White(0.28f));
            painter.FillRect(new UiRect(bar.X + (bar.Width * _other), bar.Y, MathF.Max(context.Px(2f), bar.Width * _game), bar.Height), theme.Accent);

            y += context.Px(6f + 8f);
            float x = Bounds.X;
            float square = context.Px(8f);
            foreach ((string label, Bgra32 color) in new[] { ("THIS GAME", theme.Accent), ("OTHER", UiPalette.White(0.28f)) })
            {
                float lineHeight = theme.Mono.LineHeight(small);
                painter.FillRect(new UiRect(x, y + ((lineHeight - square) * 0.5f), square, square), color);
                x += square + context.Px(6f);
                x = painter.DrawLabel(theme.Mono, label, new UiVec2(x, y), small, muted, 0.16f).X + context.Px(18f);
            }
        }
    }
}
