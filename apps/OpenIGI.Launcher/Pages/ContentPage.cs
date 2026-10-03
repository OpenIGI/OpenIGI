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
/// its drive beside it, then the game's LOAD ORDER -- its own overrides folder and every installed pack, one
/// glass row each, dragged into the order they stack in (the top wins) -- and under it the packs AVAILABLE to
/// install.
/// </summary>
/// <remarks>
/// A row is the design's: picture, name and summary, size, state, and the one thing to do about it now, with
/// the lesser actions under it. A load-order row adds a grip with its position on the left, an ON/OFF switch
/// under its state, and a line saying where its files overlap the others.
/// </remarks>
internal sealed class ContentPage
{
    private static readonly Bgra32 Conflict = Bgra32.FromRgb(255, 138, 107);
    private static readonly Bgra32 Caption = Bgra32.FromRgb(110, 112, 115);

    private readonly UiTheme _theme;
    private readonly LauncherModel _model;
    private readonly StackPanel _list;
    private readonly ReorderList _ordered;
    private readonly StackPanel _available;
    private readonly SectionCaption _orderCaption;
    private readonly SectionCaption _availableCaption;
    private readonly Text _empty;
    private readonly SectionTitle _title;
    private readonly DiskMeter _disk;
    private readonly Func<string, BackdropPicture?> _pictures;

    public ContentPage(UiTheme theme, LauncherModel model, Func<string, BackdropPicture?> pictures)
    {
        _theme = theme;
        _model = model;
        _pictures = pictures;

        _orderCaption = new SectionCaption(theme, "LOAD ORDER", "TOP WINS · DRAG TO REORDER");
        _ordered = new ReorderList { Spacing = 8f };
        _ordered.Moved += (from, to) => _model.MoveContent(from, to);
        _availableCaption = new SectionCaption(theme, "AVAILABLE", string.Empty) { Margin = new Thickness(0f, 20f, 0f, 0f) };
        _available = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8f };
        _empty = new Text(theme.Mono, "No optional content is published yet", 15f, theme.TextDim, 0.2f, uppercase: true)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0f, 40f, 0f, 0f),
        };

        _list = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10f, VerticalAlignment = VerticalAlignment.Top };
        _list.AddChild(_orderCaption);
        _list.AddChild(_ordered);
        _list.AddChild(_availableCaption);
        _list.AddChild(_available);
        _list.AddChild(_empty);

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

    /// <summary>Gets a value indicating whether a row is picked up, so the screen's own keys leave it alone.</summary>
    public bool IsReordering => _ordered.Carried is not null;

    /// <summary>Re-reads the packs: rebuilds the rows when the list changed, then what each row says.</summary>
    public void Refresh()
    {
        _title.Eyebrow = _model.Current.Title.ToUpperInvariant();
        _disk.Describe(_model.Settings.Library, _model.LibraryBytes);

        List<Control> arrived = [];

        // The list rebuilt only when what is in it changed. A drop has already put the rows in the order the
        // model now reports, so dragging never rebuilds -- and a row being carried is never pulled away.
        if (!IsReordering
            && !_ordered.Items.OfType<ContentRow>().Select(static r => r.EntryId).SequenceEqual(_model.LoadOrder.Select(static i => i.Id)))
        {
            _ordered.Clear();
            foreach (LoadOrderItem item in _model.LoadOrder)
            {
                var row = new ContentRow(_theme, _model, item.Id, ordered: true, _pictures);
                _ordered.Add(row);
                arrived.AddRange(row.Descendants().OfType<Control>());
            }
        }

        if (!_available.Children.OfType<ContentRow>().Select(static r => r.EntryId).SequenceEqual(_model.AvailableContent.Select(static p => p.Pack.Id)))
        {
            _available.ClearChildren();
            foreach (ContentEntry entry in _model.AvailableContent)
            {
                var row = new ContentRow(_theme, _model, entry.Pack.Id, ordered: false, _pictures);
                _available.AddChild(row);
                arrived.AddRange(row.Descendants().OfType<Control>());
            }
        }

        bool hasGame = _model.Current.Retail is not null;
        _orderCaption.Visibility = hasGame ? Visibility.Visible : Visibility.Collapsed;
        _ordered.Visibility = hasGame ? Visibility.Visible : Visibility.Collapsed;
        _availableCaption.Visibility = _model.AvailableContent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _empty.Visibility = hasGame && _model.Packs.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        _empty.Value = hasGame ? "No optional content is published yet" : "This game has no content yet";

        // The screen plays its controls' entrance once, when it opens; rows that arrive with the content list
        // come later and play their own, or they would stay invisible.
        for (int index = 0; index < arrived.Count; index++)
        {
            arrived[index].PlayEnter(MathF.Min(index * 0.03f, 0.3f));
        }

        foreach (ContentRow row in Rows)
        {
            row.Refresh();
        }
    }

    /// <summary>Updates what changes frame to frame: a running install's progress, and the positions while a row is carried.</summary>
    public void Update()
    {
        int position = 1;
        foreach (ContentRow row in _ordered.Items.OfType<ContentRow>())
        {
            row.Position = position++;
        }

        foreach (ContentRow row in Rows)
        {
            row.Update();
        }
    }

    private IEnumerable<ContentRow> Rows => _ordered.Items.OfType<ContentRow>().Concat(_available.Children.OfType<ContentRow>());

    // A caption over a group of rows, with a hint on the right: the design's mono captions.
    private sealed class SectionCaption(UiTheme theme, string caption, string hint) : UiElement
    {
        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
            new(available.X, theme.Mono.LineHeight(context.Px(13f)) + context.Px(10f));

        protected override void OnDraw(UiPainter painter)
        {
            LayoutContext context = painter.Context;
            float size = context.Px(13f);
            float y = Bounds.Y;
            float end = painter.DrawLabel(theme.Mono, caption, new UiVec2(Bounds.X, y), size, Caption, 0.22f).X;
            float hintWidth = UiPainter.MeasureLabel(theme.Mono, hint, size, 0.22f);
            if (hint.Length > 0)
            {
                painter.DrawLabel(theme.Mono, hint, new UiVec2(Bounds.Right - hintWidth, y), size, Caption, 0.22f);
            }

            // A hairline from the caption to the hint, the way the design rules off its groups.
            float line = y + (theme.Mono.LineHeight(size) * 0.5f);
            float from = end + context.Px(14f);
            float to = Bounds.Right - hintWidth - (hint.Length > 0 ? context.Px(14f) : 0f);
            if (to > from)
            {
                painter.FillRect(new UiRect(from, line, to - from, context.Px(1f)), UiPalette.White(0.08f));
            }
        }
    }

    // One entry: grip, picture, name and summary, size, state with its switch, actions. A load-order row is
    // carried by its grip; an available row has an empty grip column so the two groups line up.
    private sealed class ContentRow : Grid, IReorderItem
    {
        private readonly UiTheme _theme;
        private readonly LauncherModel _model;
        private readonly bool _ordered;
        private readonly Func<string, BackdropPicture?> _pictures;
        private readonly ReorderHandle? _handle;
        private readonly Thumbnail _thumbnail;
        private readonly PackInfo _info;
        private readonly Labelled _size;
        private readonly StateLabel _state;
        private readonly OnOffSwitch? _switch;
        private readonly BusyBar _busy;
        private readonly CutButton _main;
        private readonly FlexPanel _links;
        private readonly List<(LinkButton Button, Action Act)> _linkSlots = [];
        private Action? _mainAction;
        private bool _working;
        private float _lift;

        public ContentRow(UiTheme theme, LauncherModel model, string id, bool ordered, Func<string, BackdropPicture?> pictures)
        {
            _theme = theme;
            _model = model;
            _ordered = ordered;
            _pictures = pictures;
            EntryId = id;

            ColumnSpacing = 24f;
            Padding = new Thickness(4f, 12f, 20f, 12f);
            MinHeight = 104f;
            ColumnDefinitions.Add(GridLength.Absolute(44f));
            ColumnDefinitions.Add(GridLength.Absolute(150f));
            ColumnDefinitions.Add(GridLength.Star);
            ColumnDefinitions.Add(GridLength.Absolute(110f));
            ColumnDefinitions.Add(GridLength.Absolute(180f));
            ColumnDefinitions.Add(GridLength.Absolute(200f));
            RowDefinitions.Add(GridLength.Auto);

            if (ordered)
            {
                _handle = new ReorderHandle(theme) { VerticalAlignment = VerticalAlignment.Center };
                Add(_handle, 0, 0);
            }

            _thumbnail = new Thumbnail(theme) { Height = 72f, VerticalAlignment = VerticalAlignment.Center };
            _info = new PackInfo(theme) { VerticalAlignment = VerticalAlignment.Center };
            _size = new Labelled(theme, "SIZE") { VerticalAlignment = VerticalAlignment.Center };
            _state = new StateLabel(theme);

            var state = new StackPanel { Orientation = Orientation.Vertical, Spacing = 12f, VerticalAlignment = VerticalAlignment.Center };
            state.AddChild(_state);
            if (ordered)
            {
                _switch = new OnOffSwitch(theme) { HorizontalAlignment = HorizontalAlignment.Left };
                _switch.Activated += _ => Toggle();
                state.AddChild(_switch);
            }

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

            Add(_thumbnail, 0, 1);
            Add(_info, 0, 2);
            Add(_size, 0, 3);
            Add(state, 0, 4);
            Add(actions, 0, 5);
        }

        public string EntryId { get; }

        /// <summary>Sets the row's place in the load order, as the player counts.</summary>
        public int Position
        {
            set
            {
                if (_handle is not null)
                {
                    _handle.Label = value.ToString("00", CultureInfo.InvariantCulture);
                }
            }
        }

        private LoadOrderItem? Item => _ordered ? _model.LoadOrder.FirstOrDefault(i => i.Id == EntryId) : null;

        private ContentEntry? Entry => _ordered ? Item?.Entry : _model.Packs.FirstOrDefault(p => p.Pack.Id == EntryId);

        public void SetLift(float amount) => _lift = amount;

        public void Refresh()
        {
            if (Item is { IsLocal: true } local)
            {
                _thumbnail.Picture = null;
                _thumbnail.Short = "LOCAL";
                _thumbnail.Local = true;
                _thumbnail.Dimmed = !local.Enabled;
                _info.Name = "Local overrides";
                _info.Tags = ["GAME FOLDER", .. ConflictTag(local)];
                _info.Description = local.Folder is null
                    ? "Files you drop in the game's own overrides folder. Choose the game's folder in Settings to use it."
                    : local.LocalFiles == 0
                        ? "Files you drop in the game's own overrides folder. It is empty."
                        : $"Files you drop in the game's own overrides folder: {local.LocalFiles.ToString("N0", CultureInfo.InvariantCulture)} so far.";
                (_info.Detail, _info.DetailColor) = Describe(local, local.Folder?.ToUpperInvariant() ?? string.Empty);
                _size.Value = local.LocalBytes == 0 ? "—" : LauncherModel.FormatSize(local.LocalBytes / 1e6);
                Update();
                return;
            }

            if (Entry is not { } entry)
            {
                return;
            }

            bool installed = entry.Installed is not null;
            _thumbnail.Picture = entry.ThumbnailPath is { } path ? _pictures(path) : null;
            _thumbnail.Short = Short(entry.Pack.Id);
            _thumbnail.Local = false;
            _thumbnail.Dimmed = !installed || (Item is { } item && !item.Enabled);
            _info.Name = entry.Pack.Title;
            _info.Tags = Item is { } ordered ? ConflictTag(ordered) : [];
            _info.Description = entry.Pack.Summary;
            (_info.Detail, _info.DetailColor) = Item is { } listed ? Describe(listed, string.Empty) : (string.Empty, Caption);
            _size.Value = LauncherModel.FormatSize((entry.Installed?.Size ?? entry.Pack.Size) / 1e6);
            Update();
        }

        public void Update()
        {
            LoadOrderItem? item = Item;
            if (_switch is not null && item is not null)
            {
                _switch.IsOn = item.Enabled;
            }

            if (item is { IsLocal: true } local)
            {
                _working = false;
                _busy.Visibility = Visibility.Collapsed;
                _main.Visibility = Visibility.Visible;
                _state.Set(local.Enabled ? "IN USE" : "TURNED OFF", local.Enabled ? _theme.Success : _theme.TextMuted);
                Main("OPEN FOLDER", CutButtonStyle.Outline, _model.OpenLocalOverrides);
                Links();
                return;
            }

            if (Entry is not { } entry)
            {
                return;
            }

            DownloadJob? job = _model.Job is { } j && j.ContentId == EntryId ? j : null;
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
            bool enabled = item?.Enabled ?? false;
            string update = entry.DownloadBytes < 0 ? string.Empty : $" · {LauncherModel.FormatSize(entry.DownloadBytes / 1e6)}";
            switch (entry.State)
            {
                case ContentState.Available:
                    _state.Set("NOT INSTALLED", _theme.TextMuted);
                    Main("INSTALL", CutButtonStyle.Filled, () => _model.InstallContent(EntryId));
                    Links();
                    break;
                case ContentState.UpdateAvailable:
                    _state.Set("UPDATE" + update, accent);
                    Main("UPDATE", CutButtonStyle.Filled, () => _model.InstallContent(EntryId));
                    Links(("VERIFY", () => _model.VerifyContent(EntryId)), ("FOLDER", () => _model.OpenContentFolder(EntryId)), ("REMOVE", () => _model.RemoveContent(EntryId)));
                    break;
                case ContentState.Damaged:
                    _state.Set("DAMAGED", _theme.Danger);
                    Main("REPAIR", CutButtonStyle.Filled, () => _model.InstallContent(EntryId));
                    Links(("REMOVE", () => _model.RemoveContent(EntryId)));
                    break;
                case ContentState.Incompatible:
                    _state.Set("NEEDS NEWER GAME", _theme.TextMuted);
                    Main($"NEEDS {entry.Pack.MinGameVersion}", CutButtonStyle.Disabled, null);
                    Links(entry.Installed is not null ? [("REMOVE", () => _model.RemoveContent(EntryId))] : []);
                    break;
                case ContentState.Working:
                    _state.Set("WORKING", accent);
                    Main("WORKING", CutButtonStyle.Disabled, null);
                    Links();
                    break;
                default:
                    _state.Set(
                        !entry.Listed ? "NO LONGER OFFERED" : enabled ? "IN USE" : "TURNED OFF",
                        enabled && entry.Listed ? _theme.Success : _theme.TextMuted);
                    Main("REMOVE", CutButtonStyle.Danger, () => _model.RemoveContent(EntryId));
                    Links(("VERIFY", () => _model.VerifyContent(EntryId)), ("FOLDER", () => _model.OpenContentFolder(EntryId)));
                    break;
            }
        }

        protected override void OnDraw(UiPainter painter)
        {
            LayoutContext context = painter.Context;
            UiRect box = Bounds;

            // Carried: a soft shadow under it and a lighter glass, so it reads as lifted off the list.
            if (_lift > 0f)
            {
                for (int step = 4; step >= 1; step--)
                {
                    float spread = context.Px(4f * step) * _lift;
                    var shadow = new UiRect(box.X - spread, box.Y - spread + (context.Px(10f) * _lift), box.Width + (spread * 2f), box.Height + (spread * 2f));
                    painter.FillRect(shadow, UiPalette.Ink.WithAlpha((byte)(34 * _lift)));
                }
            }

            // The design's row: dark glass, a hairline that warms while the pack is being worked on or carried.
            float warm = MathF.Max(_lift, _working ? 0.5f : 0f);
            Bgra32 border = Interpolate.Lerp(UiPalette.White(0.08f), _theme.Accent.WithAlpha(160), warm);
            Bgra32 tint = Interpolate.Lerp(UiPalette.Ink.WithAlpha(168), Bgra32.FromRgba(26, 28, 30, 220), _lift);
            KitDraw.Glass(painter, _theme, box, tint, null, border);

            // The accent bar the design marks the chosen row with: the one carried, or whose grip has focus.
            float bar = MathF.Max(_lift, _handle?.Visual.Focus ?? 0f);
            if (bar > 0.01f)
            {
                painter.FillRect(new UiRect(box.X, box.Y, context.Px(3f), box.Height), _theme.Accent.WithAlpha((byte)(255 * bar)));
            }
        }

        private void Toggle()
        {
            if (Item is { } item)
            {
                _model.SetContentEnabled(EntryId, !item.Enabled);
            }
        }

        // A CONFLICT tag on a row another enabled layer wins files from.
        private static string[] ConflictTag(LoadOrderItem item) => item.Enabled && item.Conflict.CoveredFiles > 0 ? ["CONFLICT"] : [];

        // The third line: what this layer loses to the ones above it, else what it wins over the ones below.
        private static (string Text, Bgra32 Color) Describe(LoadOrderItem item, string fallback)
        {
            if (!item.Enabled)
            {
                return ("OFF · THE GAME STARTS WITHOUT IT", Caption);
            }

            if (item.Conflict.CoveredBy.Count > 0)
            {
                (string name, int files) = item.Conflict.CoveredBy[0];
                string more = item.Conflict.CoveredBy.Count > 1 ? $" AND {item.Conflict.CoveredBy.Count - 1} MORE" : string.Empty;
                return ($"{Files(item.Conflict.CoveredFiles)} COVERED BY {name.ToUpperInvariant()}{more}", Conflict);
            }

            if (item.Conflict.Covers.Count > 0)
            {
                (string name, int files) = item.Conflict.Covers[0];
                string more = item.Conflict.Covers.Count > 1 ? $" AND {item.Conflict.Covers.Count - 1} MORE" : string.Empty;
                return ($"WINS {Files(files)} OVER {name.ToUpperInvariant()}{more}", Caption);
            }

            return (fallback, Caption);
        }

        private static string Files(int count) =>
            count == 1 ? "1 FILE" : $"{count.ToString("N0", CultureInfo.InvariantCulture)} FILES";

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

    // The ON/OFF switch the design gives a load-order entry: a cut-corner track with the two words, and an
    // amber block that slides under whichever is in force.
    private sealed class OnOffSwitch(UiTheme theme) : Control(theme)
    {
        private float _position;
        private bool _isOn;
        private bool _known;

        /// <summary>Gets or sets whether the switch is on.</summary>
        /// <remarks>
        /// The first value snaps rather than slides: a switch is created whenever its row is (the screen is
        /// rebuilt on coming back from minimised, for one), and sliding from OFF there showed every entry as
        /// switched off for a moment. Only a change after that is animated.
        /// </remarks>
        public bool IsOn
        {
            get => _isOn;
            set
            {
                _isOn = value;
                if (!_known)
                {
                    _known = true;
                    _position = value ? 1f : 0f;
                }
            }
        }

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(context.Px(112f), context.Px(30f));

        protected override void OnUpdate(float elapsedSeconds)
        {
            base.OnUpdate(elapsedSeconds);
            float wanted = IsOn ? 1f : 0f;
            _position += (wanted - _position) * (1f - MathF.Exp(-elapsedSeconds * 16f));
            if (MathF.Abs(_position - wanted) < 0.002f)
            {
                _position = wanted;
            }
        }

        protected override void OnDraw(UiPainter painter)
        {
            LayoutContext context = painter.Context;
            float appear = Visual.Appear;
            float hot = MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f);
            float cut = context.Px(8f);
            const UiCorners corners = UiCorners.TopLeft | UiCorners.BottomRight;
            UiRect box = Bounds;

            painter.FillCutRect(box, cut, corners, UiPalette.Fade(UiPalette.Ink.WithAlpha(150), appear));
            painter.StrokeCutRect(box, cut, corners, context.Px(1f), UiPalette.Fade(Interpolate.Lerp(UiPalette.White(0.18f), Theme.Accent, hot * 0.7f), appear));

            float half = box.Width * 0.5f;
            float inset = context.Px(3f);
            var thumb = new UiRect(box.X + inset + ((half - inset) * _position), box.Y + inset, half - inset, box.Height - (inset * 2f));
            Bgra32 fill = Interpolate.Lerp(UiPalette.White(0.1f), Theme.Accent, _position);
            painter.FillCutRect(thumb, cut * 0.6f, corners, UiPalette.Fade(fill, appear));

            float size = context.Px(15f);
            DrawWord("OFF", new UiRect(box.X, box.Y, half, box.Height), 1f - _position, size, painter, appear);
            DrawWord("ON", new UiRect(box.X + half, box.Y, half, box.Height), _position, size, painter, appear);
        }

        private void DrawWord(string word, UiRect half, float chosen, float size, UiPainter painter, float appear)
        {
            // The word over the amber block is dark; the other is muted, so the state reads at a glance.
            Bgra32 on = word == "ON" ? Theme.OnAccent : Theme.Text;
            Bgra32 ink = Interpolate.Lerp(Bgra32.FromRgb(110, 112, 115), on, chosen);
            float width = UiPainter.MeasureLabel(Theme.Display, word, size, 0.18f);
            var at = new UiVec2(half.Center.X - (width * 0.5f), UiRowStyle.CapsCenterY(Theme.Display, size, half));
            painter.DrawLabel(Theme.Display, word, at, size, UiPalette.Fade(ink, appear), 0.18f);
        }
    }

    // The pack's picture, cropped to fill its box, its short code over the corner; dimmed while it is not in use.
    private sealed class Thumbnail(UiTheme theme) : UiElement
    {
        public BackdropPicture? Picture { get; set; }

        public string Short { get; set; } = string.Empty;

        public bool Dimmed { get; set; }

        /// <summary>Gets or sets whether this is the game folder's own entry, drawn as a folder rather than a picture.</summary>
        public bool Local { get; set; }

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(72f));

        protected override void OnDraw(UiPainter painter)
        {
            LayoutContext context = painter.Context;
            UiRect box = Bounds;
            painter.FillRect(box, Bgra32.FromRgb(20, 22, 24));
            if (Picture is { } picture)
            {
                picture.DrawCover(painter, box);
            }
            else if (Local)
            {
                // The game's own folder: a cooler panel with a folder outline, so it never passes for a pack.
                painter.FillGradient(box, [new(0f, Bgra32.FromRgba(48, 58, 64, 160)), new(1f, Bgra32.FromRgba(22, 26, 28, 160))], UiGradientDirection.Horizontal);
                float w = context.Px(30f);
                float h = context.Px(22f);
                var folder = new UiRect(box.Right - w - context.Px(14f), box.Y + context.Px(12f), w, h);
                var tab = new UiRect(folder.X, folder.Y - context.Px(4f), w * 0.42f, context.Px(5f));
                painter.FillRect(tab, UiPalette.White(0.22f));
                painter.StrokeRect(folder, context.Px(1.5f), UiPalette.White(0.32f));
            }
            else
            {
                painter.FillGradient(box, [new(0f, Bgra32.FromRgba(150, 112, 60, 110)), new(1f, Bgra32.FromRgba(60, 74, 80, 110))], UiGradientDirection.Horizontal);
            }

            if (Dimmed)
            {
                painter.FillRect(box, UiPalette.Ink.WithAlpha(130));
            }

            // The short name stands in for a picture; a pack's own picture is left to speak for itself.
            if (Picture is null)
            {
                float size = context.Px(17f);
                var at = new UiVec2(box.X + context.Px(8f), box.Bottom - context.Px(6f) - theme.Display.LineHeight(size));
                painter.DrawTextShadowed(theme.Display, Short, at, size, Bgra32.FromRgb(255, 255, 255), UiPalette.Ink.WithAlpha(200), new UiVec2(0f, context.Px(1f)));
            }
        }
    }

    // Name and tags over the summary, then a mono detail line; each cut with an ellipsis where the column ends.
    private sealed class PackInfo(UiTheme theme) : UiElement
    {
        public string Name { get; set; } = string.Empty;

        public IReadOnlyList<string> Tags { get; set; } = [];

        public string Description { get; set; } = string.Empty;

        public string Detail { get; set; } = string.Empty;

        public Bgra32 DetailColor { get; set; }

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
            new(available.X, Block(context));

        protected override void OnDraw(UiPainter painter)
        {
            LayoutContext context = painter.Context;
            float nameSize = context.Px(23f);
            float bodySize = context.Px(17f);
            float monoSize = context.Px(13f);
            float y = Bounds.Center.Y - (Block(context) * 0.5f);
            painter.PushClip(Bounds);

            float x = painter.DrawLabel(theme.Heading, Name.ToUpperInvariant(), new UiVec2(Bounds.X, y), nameSize, theme.Text, 0.06f).X;
            float nameHeight = theme.Heading.LineHeight(nameSize);
            foreach (string tag in Tags)
            {
                // The design's tag: mono in a hairline box; CONFLICT in the conflict colour.
                Bgra32 ink = tag == "CONFLICT" ? Conflict : Bgra32.FromRgb(140, 142, 144);
                float width = UiPainter.MeasureLabel(theme.Mono, tag, monoSize, 0.16f);
                float boxHeight = theme.Mono.LineHeight(monoSize) + context.Px(4f);
                var box = new UiRect(x + context.Px(12f), y + ((nameHeight - boxHeight) * 0.5f), width + context.Px(14f), boxHeight);
                painter.StrokeRect(box, context.Px(1f), tag == "CONFLICT" ? Conflict.WithAlpha(150) : UiPalette.White(0.2f));
                painter.DrawLabel(theme.Mono, tag, new UiVec2(box.X + context.Px(7f), UiRowStyle.CenterY(theme.Mono, monoSize, box)), monoSize, ink, 0.16f);
                x = box.Right - context.Px(4f);
            }

            y += nameHeight + context.Px(6f);
            painter.DrawLabel(theme.Body, UI.Text.TextFit.ToWidth(theme.Body, Description, bodySize, Bounds.Width), new UiVec2(Bounds.X, y), bodySize, Bgra32.FromRgb(169, 168, 163), 0f);

            if (Detail.Length > 0)
            {
                y += theme.Body.LineHeight(bodySize) + context.Px(6f);
                painter.DrawLabel(theme.Mono, UI.Text.TextFit.ToWidth(theme.Mono, Detail, monoSize, Bounds.Width), new UiVec2(Bounds.X, y), monoSize, DetailColor, 0.14f);
            }

            painter.PopClip();
        }

        private float Block(LayoutContext context) =>
            theme.Heading.LineHeight(context.Px(23f)) + context.Px(6f) + theme.Body.LineHeight(context.Px(17f))
            + (Detail.Length > 0 ? context.Px(6f) + theme.Mono.LineHeight(context.Px(13f)) : 0f);
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
