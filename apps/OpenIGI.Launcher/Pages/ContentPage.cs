using OpenIGI.UI;
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
/// <para>
/// A row is the design's: picture, name and summary, size, state, and the one thing to do about it now, with
/// the lesser actions under it. A load-order row adds a grip with its position on the left, an ON/OFF switch
/// under its state, and a line saying where its files overlap the others.
/// </para>
/// <para>
/// The Mods page is the same page over the game's mods (<see cref="ContentPageKind.Mods"/>): its PLAY ORDER, where a
/// later mod's changes are made over an earlier one's, the mods a developer dropped in the library's local folder among
/// them, and the published mods AVAILABLE under it. A mod with code says so, and asks once before it is installed.
/// </para>
/// </remarks>
internal sealed class ContentPage
{
    private readonly UiTheme _theme;
    private readonly LauncherModel _model;
    private readonly StackPanel _list;
    private readonly ReorderList _ordered;
    private readonly StackPanel _available;
    private readonly StackPanel _runtime;
    private readonly SectionCaption _orderCaption;
    private readonly SectionCaption _availableCaption;
    private readonly SectionCaption _runtimeCaption;
    private readonly Text _empty;
    private readonly SectionTitle _title;
    private readonly DiskMeter _disk;
    private readonly Func<string, BackdropPicture?> _pictures;
    private readonly ContentPageKind _kind;
    private readonly OnOffSwitch? _playsMods;
    private readonly UiElement? _modsBar;

    public ContentPage(UiTheme theme, LauncherModel model, Func<string, BackdropPicture?> pictures, ContentPageKind kind = ContentPageKind.Content)
    {
        _theme = theme;
        _model = model;
        _pictures = pictures;
        _kind = kind;
        bool mods = kind == ContentPageKind.Mods;

        _orderCaption = mods
            ? new SectionCaption(theme, "PLAY ORDER", "LATER WINS · DRAG TO REORDER")
            : new SectionCaption(theme, "LOAD ORDER", "TOP WINS · DRAG TO REORDER");
        _ordered = new ReorderList { Spacing = 8f };
        _ordered.Moved += (from, to) =>
        {
            if (mods)
            {
                _model.MoveMod(from, to);
            }
            else
            {
                _model.MoveContent(from, to);
            }
        };
        _availableCaption = new SectionCaption(theme, "AVAILABLE", string.Empty) { Margin = new Thickness(0f, 20f, 0f, 0f) };
        _available = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8f };

        // The engine's own downloads, apart from the game's content: native libraries the engine loads, in no
        // load order and the same whichever game is selected.
        _runtimeCaption = new SectionCaption(theme, "RUNTIME COMPONENTS", "FOR EVERY GAME") { Margin = new Thickness(0f, 20f, 0f, 0f) };
        _runtime = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8f };
        _empty = new Text(theme.Mono, "No optional content is published yet", 15f, theme.TextDim, 0.2f, uppercase: true)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0f, 40f, 0f, 0f),
        };

        _list = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10f, VerticalAlignment = VerticalAlignment.Top };
        if (mods)
        {
            // Over the order: whether the game is started with its mods at all, and where a mod's author drops a build.
            _playsMods = new OnOffSwitch(theme) { VerticalAlignment = VerticalAlignment.Center };
            _playsMods.Activated += _ => _model.SetPlaysMods(!_model.PlaysMods);
            var local = new LinkButton(theme) { Text = "OPEN LOCAL MODS FOLDER", VerticalAlignment = VerticalAlignment.Center };
            local.Activated += _ => _model.OpenLocalMods();
            var reread = new LinkButton(theme) { Text = "READ IT AGAIN", VerticalAlignment = VerticalAlignment.Center };
            reread.Activated += _ => _model.RefreshLocalMods();
            var bar = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 18f, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0f, 0f, 0f, 10f) };
            bar.Add(new Text(theme.Mono, "PLAY WITH MODS", 13f, ContentInk.Caption, 0.22f) { VerticalAlignment = VerticalAlignment.Center });
            bar.Add(_playsMods);
            bar.Add(local);
            bar.Add(reread);
            _modsBar = bar;
            _list.AddChild(bar);
        }

        _list.AddChild(_orderCaption);
        _list.AddChild(_ordered);
        _list.AddChild(_availableCaption);
        _list.AddChild(_available);
        _list.AddChild(_runtimeCaption);
        _list.AddChild(_runtime);
        _list.AddChild(_empty);

        _title = new SectionTitle(theme, model.Current.Title.ToUpperInvariant(), mods ? "Mods" : "Content")
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
        bool mods = _kind == ContentPageKind.Mods;
        IReadOnlyList<LoadOrderItem> order = mods ? _model.ModOrder : _model.LoadOrder;
        IReadOnlyList<ContentEntry> available = mods ? _model.AvailableMods : _model.AvailableContent;

        // The list rebuilt only when what is in it changed. A drop has already put the rows in the order the
        // model now reports, so dragging never rebuilds -- and a row being carried is never pulled away.
        if (!IsReordering
            && !_ordered.Items.OfType<ContentRow>().Select(static r => r.EntryId).SequenceEqual(order.Select(static i => i.Id)))
        {
            _ordered.Clear();
            foreach (LoadOrderItem item in order)
            {
                var row = new ContentRow(_theme, _model, item.Id, ordered: true, _pictures, _kind);
                _ordered.Add(row);
                arrived.AddRange(row.Descendants().OfType<Control>());
            }
        }

        if (!_available.Children.OfType<ContentRow>().Select(static r => r.EntryId).SequenceEqual(available.Select(static p => p.Pack.Id)))
        {
            _available.ClearChildren();
            foreach (ContentEntry entry in available)
            {
                var row = new ContentRow(_theme, _model, entry.Pack.Id, ordered: false, _pictures, _kind);
                _available.AddChild(row);
                arrived.AddRange(row.Descendants().OfType<Control>());
            }
        }

        IReadOnlyList<ContentEntry> runtime = mods ? [] : _model.RuntimeContent;
        if (!_runtime.Children.OfType<ContentRow>().Select(static r => r.EntryId).SequenceEqual(runtime.Select(static p => p.Pack.Id)))
        {
            _runtime.ClearChildren();
            foreach (ContentEntry entry in runtime)
            {
                var row = new ContentRow(_theme, _model, entry.Pack.Id, ordered: false, _pictures, _kind);
                _runtime.AddChild(row);
                arrived.AddRange(row.Descendants().OfType<Control>());
            }
        }

        _runtimeCaption.Visibility = runtime.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        bool hasGame = _model.Current.Retail is not null;
        _orderCaption.Visibility = hasGame && (!mods || order.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
        _ordered.Visibility = hasGame ? Visibility.Visible : Visibility.Collapsed;
        _availableCaption.Visibility = available.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (mods)
        {
            _modsBar!.Visibility = hasGame ? Visibility.Visible : Visibility.Collapsed;
            _playsMods!.IsOn = _model.PlaysMods;
            _empty.Visibility = hasGame && order.Count + available.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            _empty.Value = hasGame ? "No mods are published for this game yet" : "This game has no mods yet";
        }
        else
        {
            _empty.Visibility = hasGame && _model.Packs.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            _empty.Value = hasGame ? "No optional content is published yet" : "This game has no content yet";
        }

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

    private IEnumerable<ContentRow> Rows => _ordered.Items.OfType<ContentRow>()
        .Concat(_available.Children.OfType<ContentRow>())
        .Concat(_runtime.Children.OfType<ContentRow>());

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
            float end = painter.DrawLabel(theme.Mono, caption, new UiVec2(Bounds.X, y), size, ContentInk.Caption, 0.22f).X;
            float hintWidth = UiPainter.MeasureLabel(theme.Mono, hint, size, 0.22f);
            if (hint.Length > 0)
            {
                painter.DrawLabel(theme.Mono, hint, new UiVec2(Bounds.Right - hintWidth, y), size, ContentInk.Caption, 0.22f);
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
}

/// <summary>What a <see cref="ContentPage"/> lists: the game's content, or its mods.</summary>
internal enum ContentPageKind
{
    /// <summary>The asset overlays in their load order, the runtimes, and the packs available.</summary>
    Content,

    /// <summary>The mods in their play order, local ones among them, and the mods available.</summary>
    Mods,
}
