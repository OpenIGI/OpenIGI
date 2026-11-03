using OpenIGI.Core.Collections;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Ilff;
using OpenIGI.Host.Editor;
using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Rhi;

namespace OpenIGI.Editor.Panels;

/// <summary>Which kind of file the content browser shows.</summary>
internal enum ContentFilter
{
    /// <summary>Everything.</summary>
    All,

    /// <summary>Pictures.</summary>
    Textures,

    /// <summary>Models.</summary>
    Models,

    /// <summary>Sounds.</summary>
    Sounds,

    /// <summary>Scripts.</summary>
    Scripts,
}

/// <summary>
/// The game's files as the game sees them, layers and archives included, laid out as scene editors' asset browsers are:
/// folders down the left, the open folder's files as tiles with pictures in the middle (with back, forward, up, a clickable
/// path, a search over every loose file and filters by kind), and a preview of the selected file on the right. Models drag
/// into the viewport.
/// </summary>
internal sealed partial class ContentPanel : EditorElement
{
    private const string LocalRoot = "LOCAL:";

    private static readonly ContentFilter[] Filters = Enum.GetValues<ContentFilter>();

    private readonly IEditorHost _host;
    private readonly Action<string> _openScript;
    private readonly TextField _search;
    private readonly TreeView<ContentNode> _folders;
    private readonly TileView<ContentNode> _tiles;
    private readonly Button _back;
    private readonly Button _forward;
    private readonly Button _up;
    private readonly Button[] _filterButtons;
    private readonly Button _open;
    private readonly Button _openForm;
    private string? _formName;
    private readonly CodeEditor _text;
    private readonly ModelPreview _model;
    private readonly SoundPreview _sound;
    private readonly Selection<ContentNode> _folderSelection = new();
    private readonly Selection<ContentNode> _selection = new();
    private readonly RhiImageFactory _images;
    private readonly ContentThumbnails _thumbnails;
    private readonly List<ContentNode> _roots = [];
    private readonly Stack<ContentNode> _backStack = new();
    private readonly Stack<ContentNode> _forwardStack = new();
    private readonly List<(UiRect Box, ContentNode Node)> _crumbs = [];
    private ContentNode? _current;
    private ContentFilter _filter;
    private List<ContentNode>? _allFiles;
    private List<ContentNode> _results = [];
    private IReadOnlyList<ContentNode>? _shown;
    private (ContentNode? Folder, string Query, ContentFilter Filter) _shownFor;
    private ContentNode? _previewed;
    private UiImage? _image;
    private (int Width, int Height, int Mips) _imageSize;
    private string _facts = string.Empty;

    public ContentPanel(EditorTheme theme, IEditorHost host, Action<string> openScript)
        : base(theme)
    {
        _host = host;
        _openScript = openScript;
        _images = new RhiImageFactory(host.Device);
        _thumbnails = new ContentThumbnails(host, _images);
        _search = new TextField(theme) { Placeholder = "Search every file", SelectAllOnFocus = false };
        _search.TextChanged += Search;
        _folders = new TreeView<ContentNode>(
            theme,
            new TreeSource<ContentNode>
            {
                Roots = Roots,
                Children = node => [.. node.Children(List).Where(static c => c.IsContainer)],
                HasChildren = static node => node.IsFolder,
                Parent = static node => node.Parent,
                Label = static node => node.Name,
                Icon = Icon,
                IconTint = Tint,
            },
            _folderSelection)
        { AllowDrag = false };
        _folderSelection.Changed += () =>
        {
            if (_folderSelection.Primary is { } folder && !ReferenceEquals(folder, _current))
            {
                Navigate(folder);
            }
        };
        _tiles = new TileView<ContentNode>(
            theme,
            new TileSource<ContentNode>
            {
                Items = Shown,
                Label = static node => node.Name,
                Icon = Icon,
                IconTint = Tint,
                Thumbnail = _thumbnails.Get,
                Detail = node => _search.Text.Length >= 2 && node.Parent is { } parent ? parent.Name : Kind(node),
                Drag = static node => node.Extension == ".mef" && !node.IsFolder
                    ? new DragDropData(node, Path.GetFileNameWithoutExtension(node.Name))
                    : null,
            },
            _selection);
        _tiles.ItemActivated += Activate;
        _tiles.ContextRequested += OpenMenu;
        _folders.ContextRequested += OpenMenu;
        _back = NavButton(KitIcon.ArrowLeft, "Back", GoBack);
        _forward = NavButton(KitIcon.ArrowRight, "Forward", GoForward);
        _up = NavButton(KitIcon.ArrowUp, "Up a folder", () =>
        {
            if (_current?.Parent is { } parent)
            {
                Navigate(parent);
            }
        });
        _filterButtons = [.. Filters.Select(filter =>
        {
            Button chip = new(theme, filter.ToString()) { Style = ButtonStyle.Flat, IsToggle = true, IsChecked = filter == ContentFilter.All };
            chip.Clicked += () => SetFilter(filter);
            return chip;
        })];
        _open = new Button(theme, "Open in Script Panel", KitIcon.Script) { IsEnabled = false };
        _open.Clicked += () =>
        {
            if (_previewed is { Entry: null, IsFolder: false } file)
            {
                _openScript(file.Path);
            }
        };
        _openForm = new Button(theme, "Open in Editor", KitIcon.Document) { Style = ButtonStyle.Primary, Visibility = Visibility.Collapsed };
        _openForm.Clicked += () =>
        {
            if (_previewed is { Entry: null, IsFolder: false } file)
            {
                OpenConfig?.Invoke(file.Path);
            }
        };
        _text = new CodeEditor(theme) { IsReadOnly = true, Visibility = Visibility.Collapsed };
        _model = new ModelPreview(theme, host, _images) { Visibility = Visibility.Collapsed };
        _sound = new SoundPreview(theme, host.Audio) { Visibility = Visibility.Collapsed };
        foreach (UiElement child in (UiElement[])[_back, _forward, _up, _search, .. _filterButtons, _folders, _tiles, _open, _openForm, _text, _model, _sound])
        {
            AddChild(child);
        }
    }

    /// <summary>Gets the line describing what the preview shows, for scripts and tests.</summary>
    public string PreviewFacts => _facts;

    /// <summary>Gets the selected file, or null.</summary>
    public ContentNode? Selected => _selection.Primary;

    /// <summary>Searches the files and selects the first match, as typing in the search box and clicking it would.</summary>
    /// <param name="query">The text to find in file names; "archive.res/entry" reaches inside an archive.</param>
    /// <returns>Whether anything matched.</returns>
    public bool Find(string query)
    {
        int split = query.IndexOf(".res/", StringComparison.OrdinalIgnoreCase);
        if (split > 0)
        {
            string archiveName = query[..(split + 4)];
            string entryName = query[(split + 5)..];
            EnsureIndex();

            // Many archives share a name (a level's textures and models are both levelN.res): the first that holds the
            // entry wins.
            ContentNode? entry = _allFiles!
                .Where(n => n.IsArchive && n.Name.Equals(archiveName, StringComparison.OrdinalIgnoreCase))
                .Select(archive => archive.Children(List).FirstOrDefault(n => n.Name.Equals(entryName, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault(static found => found is not null);
            if (entry?.Parent is not { } archiveNode)
            {
                return false;
            }

            _search.Text = string.Empty;
            Navigate(archiveNode);
            _selection.Set(entry);
            _tiles.Reveal(entry);
            return true;
        }

        _search.Text = query;
        Search(query);
        if (_results.Count == 0)
        {
            return false;
        }

        _selection.Set(_results[0]);
        return true;
    }

    /// <summary>Frees the preview's texture and the thumbnails.</summary>
    public void Release()
    {
        ReleasePreview();
        _thumbnails.Release();
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        float pad = Theme.Px(6f);
        float control = Theme.ControlHeight;

        // The bar across the top: back, forward, up, the path (drawn), the search on the right.
        float x = contentRect.X + pad;
        float y = contentRect.Y + pad;
        foreach (Button button in (Button[])[_back, _forward, _up])
        {
            button.Place(context, new UiRect(x, y, control, control));
            x += control + Theme.Px(2f);
        }

        float searchWidth = MathF.Min(Theme.Px(260f), contentRect.Width * 0.3f);
        _search.Place(context, new UiRect(contentRect.Right - pad - searchWidth, y, searchWidth, control));
        PathBar = UiRect.FromEdges(x + Theme.Px(6f), y, _search.Bounds.X - pad, y + control);

        // The filters under it.
        float chipsY = y + control + Theme.Px(4f);
        float chipX = contentRect.X + pad;
        foreach (Button chip in _filterButtons)
        {
            chip.Measure(context, new UiVec2(float.PositiveInfinity, control));
            chip.Place(context, new UiRect(chipX, chipsY, chip.DesiredSize.X, control));
            chipX += chip.DesiredSize.X + Theme.Px(2f);
        }

        float top = chipsY + control + pad;
        float left = MathF.Round(contentRect.X + (contentRect.Width * 0.18f));
        float right = MathF.Round(contentRect.Right - (contentRect.Width * 0.3f));
        _folders.Place(context, UiRect.FromEdges(contentRect.X, top, left, contentRect.Bottom));
        _tiles.Place(context, UiRect.FromEdges(left + Theme.SplitterSize, top, right, contentRect.Bottom));

        UiRect preview = UiRect.FromEdges(right + Theme.SplitterSize, top, contentRect.Right, contentRect.Bottom);
        // A config script opens in its form first, the script second.
        float openX = preview.X + pad;
        if (_openForm.Visibility == Visibility.Visible)
        {
            _openForm.Measure(context, new UiVec2(float.PositiveInfinity, control));
            _openForm.Arrange(new UiRect(openX, preview.Y, _openForm.DesiredSize.X, control));
            openX += _openForm.DesiredSize.X + Theme.Px(4f);
        }

        _open.Measure(context, new UiVec2(float.PositiveInfinity, control));
        _open.Arrange(new UiRect(openX, preview.Y, _open.DesiredSize.X, control));
        UiRect body = UiRect.FromEdges(preview.X, preview.Y + control + pad + Theme.RowHeight, preview.Right, preview.Bottom);
        _text.Place(context, body);
        _model.Place(context, body);
        _sound.Place(context, body);
        PreviewArea = preview;
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        if (_current is null)
        {
            Navigate(RootFolder, remember: false);
        }

        _back.IsEnabled = _backStack.Count > 0;
        _forward.IsEnabled = _forwardStack.Count > 0;
        _up.IsEnabled = _current?.Parent is not null;
        _thumbnails.Pump();
        if (!ReferenceEquals(_selection.Primary, _previewed))
        {
            Preview(_selection.Primary);
        }
    }

    /// <inheritdoc/>
    protected override bool OnPointerDown(in PointerEvent e)
    {
        UiVec2 at = e.Position;
        if (e.Button == MouseButton.Left && _crumbs.FirstOrDefault(c => c.Box.Contains(at)) is { Node: { } crumb })
        {
            Navigate(crumb);
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        painter.FillRect(Bounds, Theme.Panel);
        float splitter = Theme.SplitterSize;
        painter.FillRect(new UiRect(_tiles.Bounds.X - splitter, _tiles.Bounds.Y, splitter, _tiles.Bounds.Height), Theme.Background);
        painter.FillRect(new UiRect(PreviewArea.X - splitter, PreviewArea.Y, splitter, PreviewArea.Height), Theme.Background);
        DrawPath(painter);
        DrawPreview(painter);
    }

    // The root of the game's files, made once.
    private ContentNode RootFolder => _rootFolder ??= new ContentNode(_host.Title.DisplayName, LocalRoot, null, isFolder: true);

    private ContentNode? _rootFolder;
    private readonly ContentNode _favouritesFolder = new("Favourites", "@favourites", null, isFolder: true);
    private readonly ContentNode _recentFolder = new("Recent", "@recent", null, isFolder: true);
    private readonly List<ContentNode> _recent = [];

    private UiRect PathBar { get; set; }

    private UiRect PreviewArea { get; set; }

    private static KitIcon Icon(ContentNode node) => node switch
    {
        { Path: "@favourites" } => KitIcon.Star,
        { Path: "@recent" } => KitIcon.History,
        { IsFolder: true } => KitIcon.Folder,
        { IsArchive: true } => KitIcon.Copy,
        _ => node.Extension switch
        {
            ".tex" or ".tga" or ".jpg" or ".jpeg" or ".spr" or ".pic" or ".fnt" or ".bmp" or ".png" => KitIcon.Image,
            ".qvm" or ".qsc" => KitIcon.Script,
            ".wav" or ".mp3" or ".ils" => KitIcon.Sound,
            ".mef" => KitIcon.Cube,
            ".grf" or ".dat" => KitIcon.Graph,
            ".lmp" or ".ctr" or ".trn" => KitIcon.Terrain,
            _ => KitIcon.Document,
        },
    };

    private static Bgra32? Tint(ContentNode node) => EditorIcons.KindColour(Icon(node));

    private static ContentFilter? FilterOf(ContentNode node) => Icon(node) switch
    {
        KitIcon.Image => ContentFilter.Textures,
        KitIcon.Cube => ContentFilter.Models,
        KitIcon.Sound => ContentFilter.Sounds,
        KitIcon.Script => ContentFilter.Scripts,
        _ => null,
    };

    private static string? Kind(ContentNode node) => node switch
    {
        { IsFolder: true } => null,
        { IsArchive: true } => "archive",
        _ => node.Extension.Length > 1 ? node.Extension[1..] : null,
    };

    private Button NavButton(KitIcon icon, string tooltip, Action click)
    {
        Button button = new(Theme, string.Empty, icon) { Style = ButtonStyle.Flat, Tooltip = tooltip };
        button.Clicked += click;
        return button;
    }

    // What the tiles show: the search's results, or the open folder's contents, folders first, cut down by the filter.
    private IReadOnlyList<ContentNode> Shown()
    {
        string query = _search.Text.Length >= 2 ? _search.Text : string.Empty;
        if (_shown is not null && ReferenceEquals(_shownFor.Folder, _current) && _shownFor.Query == query && _shownFor.Filter == _filter)
        {
            return _shown;
        }

        IEnumerable<ContentNode> all = query.Length > 0 ? _results : _current?.Children(List) ?? [];
        _shown = [.. all.Where(n => _filter == ContentFilter.All || (query.Length == 0 && n.IsContainer) || FilterOf(n) == _filter)];
        _shownFor = (_current, query, _filter);
        return _shown;
    }

    private void Navigate(ContentNode folder, bool remember = true)
    {
        if (ReferenceEquals(folder, _current))
        {
            return;
        }

        if (remember && _current is { } from)
        {
            _backStack.Push(from);
            _forwardStack.Clear();
        }

        _current = folder;
        _search.Text = string.Empty;
        _selection.Clear();
        _tiles.ScrollToTop();
        if (folder.IsFolder && !ReferenceEquals(_folderSelection.Primary, folder))
        {
            _folderSelection.Set(folder);
            _folders.Reveal(folder);
        }

        // The folder opened shows what is in it in the tree as well.
        if (folder.IsFolder)
        {
            _folders.SetExpanded(folder, true);
        }
    }

    private void GoBack()
    {
        if (_backStack.TryPop(out ContentNode? folder))
        {
            if (_current is { } from)
            {
                _forwardStack.Push(from);
            }

            Navigate(folder, remember: false);
        }
    }

    private void GoForward()
    {
        if (_forwardStack.TryPop(out ContentNode? folder))
        {
            if (_current is { } from)
            {
                _backStack.Push(from);
            }

            Navigate(folder, remember: false);
        }
    }

    private void SetFilter(ContentFilter filter)
    {
        _filter = filter;
        for (int i = 0; i < Filters.Length; i++)
        {
            _filterButtons[i].IsChecked = Filters[i] == filter;
        }
    }

    // The open folder's path as clickable parts, shortened from the left when it does not fit.
    private void DrawPath(UiPainter painter)
    {
        _crumbs.Clear();
        List<ContentNode> parts = [];
        for (ContentNode? node = _current; node is not null; node = node.Parent)
        {
            parts.Insert(0, node);
        }

        float separator = Theme.Px(14f);
        float[] widths = [.. parts.Select((p, i) => (i == parts.Count - 1 ? Theme.StrongFont : Theme.Font).MeasureWidth(p.Name, Theme.TextSize) + Theme.Px(12f))];
        int first = 0;
        while (first < parts.Count - 1 && widths.Skip(first).Sum() + (separator * (parts.Count - first)) > PathBar.Width)
        {
            first++;
        }

        float x = PathBar.X;
        UiVec2 pointer = Root?.PointerPosition ?? default;
        for (int i = first; i < parts.Count; i++)
        {
            UiRect box = new(x, PathBar.Y, widths[i], PathBar.Height);
            bool last = i == parts.Count - 1;
            if (!last && IsPointerOver && box.Contains(pointer))
            {
                EditorDraw.Fill(painter, Theme, box, Theme.Hover);
            }

            EditorDraw.Label(painter, Theme, parts[i].Name, box.Deflate(new Thickness(Theme.Px(5f), 0f)), last ? Theme.Text : Theme.TextMuted, strong: last);
            _crumbs.Add((box, parts[i]));
            x = box.Right;
            if (!last)
            {
                painter.DrawChevron(new UiVec2(x + (separator * 0.5f), PathBar.Center.Y), Theme.Px(3f), UiDirection.Right, Theme.Hairline * 1.5f, Theme.TextDisabled);
                x += separator;
            }
        }
    }

    private List<ContentNode> Roots()
    {
        if (_roots.Count == 0)
        {
            _roots.Add(_favouritesFolder);
            _roots.Add(_recentFolder);
            _roots.Add(RootFolder);
        }

        return _roots;
    }

    private List<ContentNode> List(ContentNode node)
    {
        if (ReferenceEquals(node, _favouritesFolder))
        {
            return [.. Favourites().Select(path => path.EndsWith('/')
                ? new ContentNode(LastPart(path), path.TrimEnd('/'), node, isFolder: true)
                : new ContentNode(LastPart(path), path, node, isFolder: false))];
        }

        if (ReferenceEquals(node, _recentFolder))
        {
            return [.. _recent];
        }

        List<ContentNode> children = [];
        try
        {
            if (node.IsArchive)
            {
                // The archive itself, from the file system: the resource registry mounts archives rather than handing
                // their bytes over.
                IresArchive archive = IresArchive.Read(_host.Files.ReadAllBytes(node.Path));
                children.AddRange(archive.Entries
                    .Where(static e => e.Kind == IresEntryKind.Body)
                    .Select(e => new ContentNode(System.IO.Path.GetFileName(e.Name.Replace('\\', '/')), node.Path, node, isFolder: false, e)));
                return children;
            }

            if (!node.IsFolder)
            {
                return children;
            }

            string folder = node.Path.TrimEnd('/');
            folder = folder == LocalRoot.TrimEnd('/') ? LocalRoot : folder;
            children.AddRange(_host.Files.EnumerateDirectories(folder)
                .Select(path => new ContentNode(LastPart(path), path.Replace('\\', '/'), node, isFolder: true))
                .OrderBy(static n => n.Name, StringComparer.OrdinalIgnoreCase));
            children.AddRange(_host.Files.EnumerateFiles(folder, "*")
                .Select(path => new ContentNode(LastPart(path), path.Replace('\\', '/'), node, isFolder: false))
                .OrderBy(static n => n.Name, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or IlffFormatException)
        {
            _host.Logger.Warning("Editor", $"Could not list {node.Path}: {error.Message}");
        }

        return [.. children.DistinctBy(static n => n.Path + "|" + n.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private static string LastPart(string path)
    {
        string trimmed = path.Replace('\\', '/').TrimEnd('/');
        int slash = Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf(':'));
        return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
    }

    private void Search(string query)
    {
        if (query.Length < 2)
        {
            return;
        }

        EnsureIndex();
        _results = [.. _allFiles!.Where(n => n.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(500)];
        _shown = null;
        _tiles.ScrollToTop();
    }

    // Every loose file, gathered once on the first search; archive entries are found by opening the archive.
    private void EnsureIndex()
    {
        if (_allFiles is not null)
        {
            return;
        }

        _allFiles = [];
        Stack<ContentNode> folders = new([RootFolder]);
        while (folders.TryPop(out ContentNode? folder))
        {
            foreach (ContentNode child in folder.Children(List))
            {
                if (child.IsFolder)
                {
                    folders.Push(child);
                }
                else
                {
                    _allFiles.Add(child);
                }
            }
        }
    }

    // The favourites, as remembered.
    private static string[] Favourites() =>
        EditorCVars.ContentFavourites.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string FavouriteKey(ContentNode node) => node.IsFolder ? node.Path.TrimEnd('/') + "/" : node.Path;

    private static bool IsFavourite(ContentNode node) => node.Entry is null && Favourites().Contains(FavouriteKey(node), StringComparer.OrdinalIgnoreCase);

    private void ToggleFavourite(ContentNode node)
    {
        string key = FavouriteKey(node);
        List<string> favourites = [.. Favourites()];
        if (favourites.RemoveAll(f => string.Equals(f, key, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            favourites.Add(key);
        }

        EditorCVars.ContentFavourites.Value = string.Join(';', favourites);
        _favouritesFolder.Forget();
        _folders.Invalidate();
        _shown = null;
    }

    // A file looked at goes to the top of Recent, which keeps the last twenty.
    private void Remember(ContentNode node)
    {
        if (node.IsFolder || ReferenceEquals(node.Parent, _recentFolder))
        {
            return;
        }

        _recent.RemoveAll(r => r.Path == node.Path && r.Name == node.Name);
        _recent.Insert(0, new ContentNode(node.Name, node.Path, _recentFolder, isFolder: false, node.Entry));
        if (_recent.Count > 20)
        {
            _recent.RemoveAt(_recent.Count - 1);
        }

        _recentFolder.Forget();
        if (ReferenceEquals(_current, _recentFolder))
        {
            _shown = null;
        }
    }

    private void OpenMenu(ContentNode? node, UiVec2 at)
    {
        if (Root is not { } root || node is null)
        {
            return;
        }

        List<MenuItem> items = [MenuItem.Command(node.IsContainer ? "Open" : "Preview", () => Activate(node))];
        if (node.Entry is null && node.Parent is not null && !ReferenceEquals(node, _favouritesFolder) && !ReferenceEquals(node, _recentFolder))
        {
            items.Add(MenuItem.Command(IsFavourite(node) ? "Remove from Favourites" : "Add to Favourites", () => ToggleFavourite(node)));
        }

        items.Add(MenuItem.Command("Copy Path", () => root.Clipboard.Text = node.Entry is null ? node.Path : $"{node.Path}/{node.Name}"));
        if (node.Parent is { } parent && !ReferenceEquals(parent, _current))
        {
            items.Add(MenuItem.Command("Show in Folder", () =>
            {
                Navigate(FolderOf(node));
                _selection.Set(node);
            }));
        }

        if (FormFor(node) is { } form)
        {
            items.Add(MenuItem.Submenu("Open With", OpenWith(node, form)));
        }

        items.AddRange(FileMenu(node));
        MenuPopup.OpenAt(root, at, items);
    }

    // The real folder a node is in, for one listed under Favourites or Recent.
    private ContentNode FolderOf(ContentNode node)
    {
        if (node.Parent is { } parent && !ReferenceEquals(parent, _favouritesFolder) && !ReferenceEquals(parent, _recentFolder))
        {
            return parent;
        }

        string path = node.Path;
        int slash = path.LastIndexOf('/');
        string folder = slash > LocalRoot.Length ? path[..slash] : LocalRoot;
        return new ContentNode(LastPart(folder), folder, null, isFolder: true);
    }

    /// <summary>Gets or sets what opens a config script in its form editor, by game path.</summary>
    public Action<string>? OpenConfig { get; set; }

    private void Activate(ContentNode node)
    {
        if (node.IsContainer)
        {
            Navigate(node);
        }
        else if (node is { Entry: null } && node.Extension is ".qvm" && FormFor(node) is { } form && Root is { } root)
        {
            // A config script can be opened either way: ask which, its form first.
            MenuPopup.OpenAt(root, root.PointerPosition, OpenWith(node, form));
        }
        else if (node is { Entry: null } && (node.Extension is ".qvm" || ScriptPanel.IsTextFile(node.Name)))
        {
            _openScript(node.Path);
        }
    }

    // The editors a config script can be opened in.
    private List<MenuItem> OpenWith(ContentNode node, string form) =>
    [
        MenuItem.Command($"Open in {form}", () => OpenConfig?.Invoke(node.Path)),
        MenuItem.Command("Open in Script Panel", () => _openScript(node.Path)),
    ];

    // The form editor a loose script opens in, or null for one that is not a config.
    private string? FormFor(ContentNode node) =>
        OpenConfig is not null && node is { Entry: null, IsFolder: false, Extension: ".qvm" } && _host.TryReadFile(node.Path, out ReadOnlyMemory<byte> bytes)
            ? ConfigPanel.EditorFor(bytes)
            : null;
}
