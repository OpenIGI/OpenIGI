using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Collections;
using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Textures;
using OpenIGI.Host.Editor;
using OpenIGI.Scripting.Qsc;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Rhi;

namespace OpenIGI.Editor.Panels;

/// <summary>One thing the content browser lists: a folder, a file, or an entry inside a <c>.res</c> archive.</summary>
internal sealed class ContentNode
{
    private List<ContentNode>? _children;

    public ContentNode(string name, string path, ContentNode? parent, bool isFolder, IresEntry? entry = null)
    {
        Name = name;
        Path = path;
        Parent = parent;
        IsFolder = isFolder;
        Entry = entry;
    }

    /// <summary>Gets the name shown.</summary>
    public string Name { get; }

    /// <summary>Gets the game path (<c>LOCAL:common/textures</c>); for an archive entry, its archive's path.</summary>
    public string Path { get; }

    /// <summary>Gets the containing node.</summary>
    public ContentNode? Parent { get; }

    /// <summary>Gets whether the node is a folder.</summary>
    public bool IsFolder { get; }

    /// <summary>Gets the archive entry the node is, or <see langword="null"/>.</summary>
    public IresEntry? Entry { get; }

    /// <summary>Gets whether the node is an archive that can be opened like a folder.</summary>
    public bool IsArchive => !IsFolder && Entry is null && Name.EndsWith(".res", StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets the lower-cased extension of the file or entry.</summary>
    public string Extension => System.IO.Path.GetExtension(Name).ToLowerInvariant();

    /// <summary>Gets the children, listed the first time they are asked for.</summary>
    /// <param name="list">Lists a node's children.</param>
    /// <returns>The children.</returns>
    public IReadOnlyList<ContentNode> Children(Func<ContentNode, List<ContentNode>> list) => _children ??= list(this);
}

/// <summary>
/// The game's files as the game sees them, layers and archives included: folders, files and the entries inside
/// <c>.res</c> archives, with a preview of whatever is selected and a search over every loose file.
/// </summary>
internal sealed class ContentPanel : EditorElement
{
    private const string LocalRoot = "LOCAL:";

    private readonly IEditorHost _host;
    private readonly Action<string> _openScript;
    private readonly TextField _search;
    private readonly TreeView<ContentNode> _tree;
    private readonly Button _open;
    private readonly CodeEditor _text;
    private readonly Selection<ContentNode> _selection = new();
    private readonly RhiImageFactory _images;
    private readonly List<ContentNode> _roots = [];
    private List<ContentNode>? _allFiles;
    private List<ContentNode> _results = [];
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
        _search = new TextField(theme) { Placeholder = "Search files", SelectAllOnFocus = false };
        _search.TextChanged += Search;
        _tree = new TreeView<ContentNode>(
            theme,
            new TreeSource<ContentNode>
            {
                Roots = () => _search.Text.Length >= 2 ? _results : Roots(),
                Children = node => node.Children(List),
                HasChildren = static node => node.IsFolder || node.IsArchive,
                Parent = static node => node.Parent,
                Label = static node => node.Name,
                Icon = Icon,
                Detail = node => _search.Text.Length >= 2 && node.Parent is { } parent ? parent.Path[LocalRoot.Length..] : null,
            },
            _selection)
        { AllowDrag = false };
        _tree.ItemActivated += Activate;
        _open = new Button(theme, "Open in Script Panel", KitIcon.Script) { IsEnabled = false };
        _open.Clicked += () =>
        {
            if (_previewed is { Entry: null, IsFolder: false } file)
            {
                _openScript(file.Path);
            }
        };
        _text = new CodeEditor(theme) { IsReadOnly = true, Visibility = Visibility.Collapsed };
        AddChild(_search);
        AddChild(_tree);
        AddChild(_open);
        AddChild(_text);
    }

    /// <summary>Searches the files and selects the first match, as typing in the search box and clicking it would.</summary>
    /// <param name="query">The text to find in file names.</param>
    /// <returns>Whether anything matched.</returns>
    public bool Find(string query)
    {
        _search.Text = query;
        Search(query);
        if (_results.Count == 0)
        {
            return false;
        }

        _selection.Set(_results[0]);
        return true;
    }

    /// <summary>Frees the preview's texture.</summary>
    public void Release()
    {
        if (_image is { } image)
        {
            _images.DestroyImage(image);
            _image = null;
        }
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        float pad = Theme.Px(6f);
        float split = MathF.Round(contentRect.Width * 0.45f);
        UiRect left = new(contentRect.X, contentRect.Y, split, contentRect.Height);
        UiRect search = new(left.X + pad, left.Y + pad, left.Width - (pad * 2f), Theme.ControlHeight);
        Place(context, _search, search);
        Place(context, _tree, UiRect.FromEdges(left.X, search.Bottom + pad, left.Right, left.Bottom));

        UiRect right = UiRect.FromEdges(left.Right + Theme.SplitterSize, contentRect.Y, contentRect.Right, contentRect.Bottom);
        _open.Measure(context, new UiVec2(float.PositiveInfinity, Theme.ControlHeight));
        _open.Arrange(new UiRect(right.X + pad, right.Y + pad, _open.DesiredSize.X, Theme.ControlHeight));
        Place(context, _text, UiRect.FromEdges(right.X, right.Y + Theme.ControlHeight + (pad * 2f) + Theme.RowHeight, right.Right, right.Bottom));
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        if (!ReferenceEquals(_selection.Primary, _previewed))
        {
            Preview(_selection.Primary);
        }
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        painter.FillRect(Bounds, Theme.Panel);
        UiRect right = PreviewArea;
        painter.FillRect(new UiRect(right.X - Theme.SplitterSize, Bounds.Y, Theme.SplitterSize, Bounds.Height), Theme.Background);
        EditorDraw.Label(painter, Theme, _facts, new UiRect(right.X + Theme.Px(8f), _open.Bounds.Bottom + Theme.Px(4f), right.Width - Theme.Px(16f), Theme.RowHeight), Theme.TextMuted);
        if (_image is { } image)
        {
            DrawImage(painter, image, UiRect.FromEdges(right.X + Theme.Px(8f), _open.Bounds.Bottom + Theme.RowHeight + Theme.Px(8f), right.Right - Theme.Px(8f), right.Bottom - Theme.Px(8f)));
        }
        else if (_previewed is null)
        {
            EditorDraw.Label(painter, Theme, "Select a file to preview it.", new UiRect(right.X + Theme.Px(8f), right.Center.Y, right.Width, Theme.RowHeight), Theme.TextDisabled);
        }
    }

    private UiRect PreviewArea => UiRect.FromEdges(_tree.Bounds.Right + Theme.SplitterSize, Bounds.Y, Bounds.Right, Bounds.Bottom);

    private static void Place(LayoutContext context, UiElement element, UiRect rect)
    {
        element.Measure(context, rect.Size);
        element.Arrange(rect);
    }

    private static KitIcon Icon(ContentNode node) => node switch
    {
        { IsFolder: true } => KitIcon.Folder,
        { IsArchive: true } => KitIcon.Copy,
        _ => node.Extension switch
        {
            ".tex" or ".tga" or ".jpg" or ".jpeg" or ".spr" or ".bmp" => KitIcon.Image,
            ".qvm" or ".qsc" => KitIcon.Script,
            ".wav" or ".mp3" or ".ils" => KitIcon.Sound,
            ".mef" => KitIcon.Cube,
            ".grf" or ".dat" => KitIcon.Graph,
            ".lmp" or ".ctr" or ".trn" => KitIcon.Terrain,
            _ => KitIcon.Document,
        },
    };

    private List<ContentNode> Roots()
    {
        if (_roots.Count == 0)
        {
            _roots.AddRange(List(new ContentNode(_host.Title.DisplayName, LocalRoot, null, isFolder: true)));
        }

        return _roots;
    }

    private List<ContentNode> List(ContentNode node)
    {
        List<ContentNode> children = [];
        try
        {
            if (node.IsArchive && _host.TryReadFile(node.Path, out ReadOnlyMemory<byte> bytes))
            {
                IresArchive archive = IresArchive.Read(bytes);
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
            _tree.Invalidate();
            return;
        }

        // Every loose file, gathered once on the first search; archive entries are found by opening the archive.
        if (_allFiles is null)
        {
            _allFiles = [];
            Stack<ContentNode> folders = new(Roots().Where(static n => n.IsFolder));
            foreach (ContentNode file in Roots().Where(static n => !n.IsFolder))
            {
                _allFiles.Add(file);
            }

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

        _results = [.. _allFiles.Where(n => n.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(500)];
        _tree.Invalidate();
    }

    private void Activate(ContentNode node)
    {
        if (node is { Entry: null, IsFolder: false } && node.Extension is ".qvm")
        {
            _openScript(node.Path);
        }
        else if (node.IsFolder || node.IsArchive)
        {
            _tree.SetExpanded(node, !_tree.IsExpanded(node));
        }
    }

    private void Preview(ContentNode? node)
    {
        _previewed = node;
        Release();
        _text.Visibility = Visibility.Collapsed;
        _open.IsEnabled = node is { Entry: null, IsFolder: false } && node.Extension == ".qvm";
        _facts = string.Empty;
        if (node is null || node.IsFolder)
        {
            return;
        }

        ReadOnlyMemory<byte> bytes;
        if (node.Entry is { } entry)
        {
            bytes = entry.Data;
        }
        else if (!_host.TryReadFile(node.Path, out bytes))
        {
            _facts = "The file could not be read.";
            return;
        }

        _facts = $"{node.Name}  •  {Size(bytes.Length)}";
        try
        {
            if (ImageSniffer.TryDecode(bytes.Span, out TextureImage? picture) && picture is not null)
            {
                Bgra32[] pixels = [.. picture.GetLevel(0)];
                _image = _images.CreateImage(picture.Width, picture.Height, pixels);
                _imageSize = (picture.Width, picture.Height, picture.MipLevels);
                _facts += FormattableString.Invariant($"  •  {picture.Width} × {picture.Height}, {picture.MipLevels} mip level(s)");
                return;
            }

            if (node.Extension == ".qvm" && Scripting.Qvm.QvmProgram.IsQvm(bytes.Span))
            {
                ShowText(QscPrinter.Print(QscDecompiler.Decompile(bytes)));
                _facts += "  •  script";
                return;
            }

            if (node.Extension is ".txt" or ".qsc" or ".ini" or ".cfg" || LooksLikeText(bytes.Span))
            {
                ShowText(System.Text.Encoding.Latin1.GetString(bytes.Span));
                return;
            }
        }
        catch (Exception error) when (error is TextureFormatException or Scripting.Qvm.QvmFormatException or QscDecompileException)
        {
            _facts += $"  •  could not be decoded: {error.Message}";
        }
    }

    private void ShowText(string text)
    {
        _text.Text = text;
        _text.Visibility = Visibility.Visible;
    }

    private static bool LooksLikeText(ReadOnlySpan<byte> bytes)
    {
        int sample = Math.Min(bytes.Length, 512);
        for (int i = 0; i < sample; i++)
        {
            byte b = bytes[i];
            if (b < 9 || (b > 13 && b < 32))
            {
                return false;
            }
        }

        return sample > 0;
    }

    private static string Size(int bytes) => bytes switch
    {
        < 1024 => FormattableString.Invariant($"{bytes} B"),
        < 1024 * 1024 => FormattableString.Invariant($"{bytes / 1024.0:0.#} KB"),
        _ => FormattableString.Invariant($"{bytes / (1024.0 * 1024.0):0.#} MB"),
    };

    private void DrawImage(UiPainter painter, UiImage image, UiRect area)
    {
        (int width, int height, _) = _imageSize;
        float scale = MathF.Min(1f * MathF.Max(1f, Theme.Scale), MathF.Min(area.Width / width, area.Height / height));
        float w = MathF.Round(width * scale);
        float h = MathF.Round(height * scale);
        UiRect shown = new(MathF.Round(area.Center.X - (w * 0.5f)), MathF.Round(area.Y), w, h);

        // A checkerboard behind, so transparency reads as transparency.
        float cell = Theme.Px(8f);
        painter.PushClip(shown);
        for (float y = shown.Y; y < shown.Bottom; y += cell)
        {
            for (float x = shown.X; x < shown.Right; x += cell)
            {
                bool dark = ((int)((x - shown.X) / cell) + (int)((y - shown.Y) / cell)) % 2 == 0;
                painter.FillRect(new UiRect(x, y, cell, cell), dark ? Bgra32.FromRgb(60, 62, 66) : Bgra32.FromRgb(90, 92, 96));
            }
        }

        painter.PopClip();
        painter.DrawSprite(image, shown, new UiRect(0f, 0f, 1f, 1f), Bgra32.White);
        painter.StrokeRect(shown, Theme.Hairline, Theme.Border);
    }
}
