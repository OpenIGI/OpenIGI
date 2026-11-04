using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Host.Editor;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Rhi;

namespace OpenIGI.Editor.Panels;

/// <summary>
/// Small pictures of the content browser's images, made as tiles come into view: a few a frame so scrolling stays smooth,
/// from the smallest mip level that still fills a tile, the least recently shown dropped once there are too many.
/// </summary>
internal sealed class ContentThumbnails(IEditorHost host, RhiImageFactory images)
{
    private const int Edge = 128;
    private const int Capacity = 400;
    private const int DecodesPerFrame = 4;

    private readonly Dictionary<ContentNode, Entry> _made = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ContentNode> _failed = new(ReferenceEqualityComparer.Instance);
    private readonly Queue<ContentNode> _wanted = new();
    private long _clock;

    /// <summary>Gets whether a node can have a thumbnail: a picture, or a model drawn small.</summary>
    /// <param name="node">The node.</param>
    /// <returns><see langword="true"/> for image and model files and entries.</returns>
    public static bool IsPicture(ContentNode node) => !node.IsFolder && (ContentPictures.Extensions.Contains(node.Extension) || node.Extension == ".mef");

    /// <summary>Gets a node's thumbnail, asking for it to be made when it has none yet.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The picture and its size, or <see langword="null"/> while it is not ready or cannot be made.</returns>
    public (UiImage Image, int Width, int Height)? Get(ContentNode node)
    {
        if (_made.TryGetValue(node, out Entry? entry))
        {
            entry.LastShown = _clock;
            return (entry.Image, entry.Width, entry.Height);
        }

        if (IsPicture(node) && !_failed.Contains(node) && !_wanted.Contains(node))
        {
            _wanted.Enqueue(node);
        }

        return null;
    }

    /// <summary>Makes a few of the thumbnails asked for. Called once a frame.</summary>
    public void Pump()
    {
        _clock++;
        for (int made = 0; made < DecodesPerFrame && _wanted.TryDequeue(out ContentNode? node);)
        {
            if (_made.ContainsKey(node))
            {
                continue;
            }

            if (Make(node))
            {
                made++;
            }
            else
            {
                _failed.Add(node);
            }
        }

        // Asked-for tiles scrolled away are not worth decoding: what is still wanted is asked for again next frame.
        _wanted.Clear();
        while (_made.Count > Capacity)
        {
            ContentNode oldest = _made.MinBy(static pair => pair.Value.LastShown).Key;
            images.DestroyImage(_made[oldest].Image);
            _made.Remove(oldest);
        }
    }

    /// <summary>Frees every thumbnail.</summary>
    public void Release()
    {
        foreach (Entry entry in _made.Values)
        {
            images.DestroyImage(entry.Image);
        }

        _made.Clear();
        _wanted.Clear();
    }

    private bool Make(ContentNode node)
    {
        ReadOnlyMemory<byte> bytes;
        if (node.Entry is { } archived)
        {
            bytes = archived.Data;
        }
        else if (!host.TryReadFile(node.Path, out bytes))
        {
            return false;
        }

        try
        {
            if (node.Extension == ".mef")
            {
                // The model textured as the level binds it, at the tile's size.
                MefModel model = MefModel.Read(bytes, AssetFormatProfile.Permissive);
                string name = Path.GetFileNameWithoutExtension(node.Name);
                if (ModelThumbnail.Render(model, Edge, record => ModelTextures.Read(host, name, record, Edge), host.AnimateModel(model, name)) is not { } drawn)
                {
                    return false;
                }

                _made[node] = new Entry(images.CreateImage(Edge, Edge, drawn), Edge, Edge) { LastShown = _clock };
                return true;
            }

            if (!ContentPictures.TryDecode(node.Extension, bytes, out TextureImage? picture, out _) || picture is null)
            {
                return false;
            }

            // The smallest stored level still at least a tile across, or a point-sampled copy of the top one.
            int level = 0;
            while (level + 1 < picture.MipLevels && Math.Max(picture.GetLevelSize(level + 1).Width, picture.GetLevelSize(level + 1).Height) >= Edge)
            {
                level++;
            }

            (int width, int height) = picture.GetLevelSize(level);
            Bgra32[] pixels = [.. picture.GetLevel(level)];
            if (Math.Max(width, height) > Edge * 2)
            {
                (pixels, width, height) = Shrink(pixels, width, height);
            }

            _made[node] = new Entry(images.CreateImage(width, height, pixels), width, height) { LastShown = _clock };
            return true;
        }
        catch (Exception error) when (error is TextureFormatException or BitmapFormatException or IlffFormatException or ArgumentException or PackFormatException or InvalidDataException)
        {
            return false;
        }
    }

    private static (Bgra32[] Pixels, int Width, int Height) Shrink(Bgra32[] pixels, int width, int height)
    {
        double scale = (double)Edge / Math.Max(width, height);
        int w = Math.Max(1, (int)(width * scale));
        int h = Math.Max(1, (int)(height * scale));
        Bgra32[] shrunk = new Bgra32[w * h];
        for (int y = 0; y < h; y++)
        {
            int sy = Math.Min(height - 1, (int)((y + 0.5) / scale));
            for (int x = 0; x < w; x++)
            {
                shrunk[(y * w) + x] = pixels[(sy * width) + Math.Min(width - 1, (int)((x + 0.5) / scale))];
            }
        }

        return (shrunk, w, h);
    }

    private sealed record Entry(UiImage Image, int Width, int Height)
    {
        public long LastShown { get; set; }
    }
}
