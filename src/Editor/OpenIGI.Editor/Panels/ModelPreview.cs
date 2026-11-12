using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Viewport;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
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

/// <summary>
/// A model as the game shows it: textured as the running level binds it, lit, and, for a skinned model, playing any clip its
/// rig has. Drag to turn it, the wheel to zoom; a bar picks the clip, plays and pauses it, scrubs it, and overlays the wireframe.
/// </summary>
internal sealed class ModelPreview : EditorElement
{
    // The picture's longest side; a bigger panel scales it up.
    private const int MaximumSide = 720;

    private readonly IEditorHost _host;
    private readonly RhiImageFactory _images;
    private readonly ModelRaster _raster = new();
    private readonly Dropdown _clip;
    private readonly Button _play;
    private readonly Button _wire;
    private readonly List<RasterTexture?> _textures = [];
    private readonly List<(int A, int B, int C)[]> _triangles = [];
    private MefModel? _model;
    private IModelAnimator? _animator;
    private Vertex3D[] _vertices = [];
    private Vertex3D[] _rest = [];
    private UiImage? _image;
    private (int Width, int Height) _imageSize;
    private Vector3f _centre;
    private float _radius = 1f;
    private float _yaw = 0.6f;
    private float _pitch = 0.3f;
    private float _zoom = 1f;
    private float _time;
    private bool _playing = true;
    private bool _dirty = true;
    private UiVec2 _grab;
    private (float Yaw, float Pitch) _grabAngles;
    private UiRect _view;
    private UiRect _scrub;
    private bool _scrubbing;
    private int _texturedRecords;

    /// <summary>Initializes a new instance of the <see cref="ModelPreview"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="host">The running game, whose level binds the model's textures and clips.</param>
    /// <param name="images">Where the picture is uploaded.</param>
    public ModelPreview(EditorTheme theme, IEditorHost host, RhiImageFactory images)
        : base(theme)
    {
        _host = host;
        _images = images;
        _clip = new Dropdown(theme) { Placeholder = "Still", Tooltip = "The clip the model plays" };
        _clip.SelectionChanged += _ =>
        {
            _time = 0f;
            _dirty = true;
        };
        _play = new Button(theme, string.Empty, KitIcon.Pause) { Style = ButtonStyle.Flat, Tooltip = "Play or pause the clip" };
        _play.Clicked += () =>
        {
            _playing = !_playing;
            _play.Icon = _playing ? KitIcon.Pause : KitIcon.Play;
        };
        _wire = new Button(theme, "Wire") { IsToggle = true, Style = ButtonStyle.Flat, Tooltip = "Draw the triangles' edges over the model" };
        _wire.Clicked += () => _wire.IsChecked = !_wire.IsChecked;
        AddChild(_clip);
        AddChild(_play);
        AddChild(_wire);
    }

    /// <summary>Gets a line about what the preview could find for the model: its textures and clips.</summary>
    public string Facts { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public override EditorCursor Cursor => EditorCursor.Move;

    /// <summary>Shows a model, or nothing.</summary>
    /// <param name="model">The model.</param>
    /// <param name="name">Its name as level data spells it (<c>435_01_1</c>), for its textures; null when not known.</param>
    /// <param name="animator">The clips to play it in, when they are not the level's own for it (an animation file's); null for those.</param>
    /// <param name="openClip">The clip to open on, or -1 for the one it stands in.</param>
    public void Show(MefModel? model, string? name = null, Game.World.IModelAnimator? animator = null, int openClip = -1)
    {
        _model = model;
        _textures.Clear();
        _triangles.Clear();
        _animator = null;
        _texturedRecords = 0;
        _time = 0f;
        _zoom = 1f;
        _dirty = true;
        Facts = string.Empty;
        if (model is null)
        {
            ReleaseImage();
            return;
        }

        _rest = [.. model.Vertices.ToArray().Select(static v => new Vertex3D(v.Position, v.Normal, v.DiffuseU, v.DiffuseV))];
        _vertices = (Vertex3D[])_rest.Clone();
        foreach (MefDrawRecord record in model.DrawRecords)
        {
            _triangles.Add([.. model.Triangles(record)]);
            RasterTexture? texture = name is null ? null : ModelTextures.Read(_host, name, record);
            _textures.Add(texture);
            _texturedRecords += texture is null ? 0 : 1;
        }

        Frame(_rest);
        _animator = animator ?? _host.AnimateModel(model, name);
        _clip.Options = _animator?.Clips ?? [];
        _clip.SelectedIndex = _animator is null ? -1
            : openClip >= 0 && openClip < _animator.Clips.Count ? openClip
            : ModelPoses.Standing(_animator, _rest, _triangles.SelectMany(static t => t));
        if (_animator is not null && _clip.SelectedIndex >= 0)
        {
            // A skinned model's vertices sit about their bones until posed, so it is framed as it stands in its clip.
            _animator.Pose(_clip.SelectedIndex, 0f, _vertices);
            Frame(_vertices);
        }
        string unused = name is null || _texturedRecords > 0 ? string.Empty : " (the level does not use this model)";
        string clips = _animator is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"  •  {_animator.Clips.Count} clip(s)");
        Facts = string.Create(CultureInfo.InvariantCulture, $"{_texturedRecords} of {model.DrawRecords.Count} part(s) textured{unused}{clips}");
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        if (_animator is not null && _clip.SelectedIndex >= 0 && _playing && !_scrubbing)
        {
            float length = MathF.Max(1e-3f, _animator.Seconds(_clip.SelectedIndex));
            _time = (_time + elapsedSeconds) % length;
            _dirty = true;
        }
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        float bar = Theme.ControlHeight;
        float y = contentRect.Bottom - bar;
        bool animated = _animator is not null;
        float scrubY = y - Theme.Px(10f);
        _clip.Visibility = animated ? Visibility.Visible : Visibility.Collapsed;
        _play.Visibility = _clip.Visibility;
        _play.Place(context, new UiRect(contentRect.X, y, Theme.Px(24f), bar));
        _clip.Place(context, new UiRect(contentRect.X + Theme.Px(28f), y, MathF.Max(Theme.Px(60f), MathF.Min(Theme.Px(220f), contentRect.Width - Theme.Px(88f))), bar));
        _wire.Place(context, new UiRect(contentRect.Right - Theme.Px(52f), y, Theme.Px(52f), bar));
        // The scrub bar on its own line above the buttons, the width of the picture.
        _scrub = animated
            ? UiRect.FromEdges(contentRect.X + Theme.Px(4f), scrubY - Theme.Px(3f), contentRect.Right - Theme.Px(4f), scrubY + Theme.Px(3f))
            : default;
        UiRect view = UiRect.FromEdges(contentRect.X, contentRect.Y, contentRect.Right, (animated ? scrubY - Theme.Px(10f) : y - Theme.Px(4f)));
        if (view != _view)
        {
            _view = view;
            _dirty = true;
        }
    }

    /// <inheritdoc/>
    protected override bool OnPointerDown(in PointerEvent e)
    {
        if (e.Button != MouseButton.Left)
        {
            return false;
        }

        _scrubbing = _scrub.Width > 0 && UiRect.FromEdges(_scrub.X, _scrub.Y - Theme.Px(6f), _scrub.Right, _scrub.Bottom + Theme.Px(6f)).Contains(e.Position);
        _grab = e.Position;
        _grabAngles = (_yaw, _pitch);
        Scrub(e.Position);
        return true;
    }

    /// <inheritdoc/>
    protected override void OnPointerMove(in PointerEvent e)
    {
        if (!IsCapturing)
        {
            return;
        }

        if (_scrubbing)
        {
            Scrub(e.Position);
            return;
        }

        _yaw = _grabAngles.Yaw + ((e.Position.X - _grab.X) * 0.01f);
        _pitch = Math.Clamp(_grabAngles.Pitch + ((e.Position.Y - _grab.Y) * 0.01f), -1.5f, 1.5f);
        _dirty = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerUp(in PointerEvent e) => _scrubbing = false;

    /// <inheritdoc/>
    protected override bool OnWheel(UiVec2 position, float notches, KeyModifiers modifiers)
    {
        _zoom = Math.Clamp(_zoom * MathF.Pow(1.15f, notches), 0.2f, 20f);
        _dirty = true;
        return true;
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        if (_model is null || _view.Width < 2f || _view.Height < 2f)
        {
            return;
        }

        if (_dirty)
        {
            Render();
            _dirty = false;
        }

        if (_image is { } image)
        {
            painter.DrawSprite(image, _view, new UiRect(0f, 0f, 1f, 1f), Bgra32.White);
        }

        painter.StrokeRect(_view, Theme.Hairline, Theme.Border);
        if (_wire.IsChecked)
        {
            DrawWire(painter);
        }

        if (_scrub.Width > 0 && _animator is not null && _clip.SelectedIndex >= 0)
        {
            float length = MathF.Max(1e-3f, _animator.Seconds(_clip.SelectedIndex));
            EditorDraw.Fill(painter, Theme, _scrub, Theme.Input);
            UiRect done = _scrub with { Width = _scrub.Width * (_time / length) };
            EditorDraw.Fill(painter, Theme, done, Theme.Accent);
            painter.FillDot(new UiVec2(done.Right, _scrub.Center.Y), Theme.Px(5f), Theme.Text);
            string clock = string.Create(CultureInfo.InvariantCulture, $"{_time:0.00} / {length:0.00} s");
            EditorDraw.Label(painter, Theme, clock, new UiRect(_view.X + Theme.Px(6f), _view.Bottom - Theme.RowHeight - Theme.Px(2f), Theme.Px(140f), Theme.RowHeight), Theme.TextMuted);
        }
    }

    private void Scrub(UiVec2 at)
    {
        if (_scrubbing && _animator is not null && _clip.SelectedIndex >= 0)
        {
            float t = Math.Clamp((at.X - _scrub.X) / MathF.Max(1f, _scrub.Width), 0f, 1f);
            _time = t * _animator.Seconds(_clip.SelectedIndex);
            _dirty = true;
        }
    }

    // The picture: posed when a clip plays, textured part by part, at the panel's size or the cap.
    private void Render()
    {
        if (_model is null)
        {
            return;
        }

        if (_animator is not null && _clip.SelectedIndex >= 0)
        {
            _rest.CopyTo(_vertices, 0);
            _animator.Pose(_clip.SelectedIndex, _time, _vertices);
        }

        float fit = MathF.Min(1f, MaximumSide / MathF.Max(_view.Width, _view.Height));
        int width = Math.Max(1, (int)(_view.Width * fit));
        int height = Math.Max(1, (int)(_view.Height * fit));
        (Matrix3f view, Vector3f eye, float focal) = Camera(width, height);
        _raster.Begin(width, height, Theme.Input);
        for (int r = 0; r < _triangles.Count; r++)
        {
            _raster.Draw(_vertices, _triangles[r], _textures[r], view, eye, focal);
        }

        if (_image is { } image && _imageSize == (width, height))
        {
            _images.UpdateImage(image, _raster.Pixels);
        }
        else
        {
            ReleaseImage();
            _image = _images.CreateImage(width, height, _raster.Pixels);
            _imageSize = (width, height);
        }
    }

    // An orbit about the model's centre: turn about Z (the games' up), tilt, and stand back far enough to fit it.
    private (Matrix3f View, Vector3f Eye, float Focal) Camera(int width, int height)
    {
        Matrix3f turn = Matrix3f.RotationZ(-_yaw) * Matrix3f.RotationX(-_pitch);
        float focal = MathF.Min(width, height) * 1.2f;
        float distance = _radius * 2.6f / _zoom;
        Vector3f eye = turn.Transform(_centre) + new Vector3f(0f, -distance, 0f);
        return (turn, eye, focal);
    }

    private void DrawWire(UiPainter painter)
    {
        float fit = MathF.Min(1f, MaximumSide / MathF.Max(_view.Width, _view.Height));
        (Matrix3f view, Vector3f eye, float focal) = Camera((int)(_view.Width * fit), (int)(_view.Height * fit));
        float scale = 1f / fit;
        UiVec2? Project(Vector3f p)
        {
            Vector3f v = view.Transform(p) - eye;
            return v.Y < 0.01f ? null : new UiVec2(_view.X + ((((_view.Width * fit) * 0.5f) + (v.X * focal / v.Y)) * scale), _view.Y + ((((_view.Height * fit) * 0.5f) - (v.Z * focal / v.Y)) * scale));
        }

        painter.PushClip(_view);
        int drawn = 0;
        foreach ((int a, int b, int c) in _triangles.SelectMany(static t => t))
        {
            if (++drawn > 20_000)
            {
                break;
            }

            if (Project(_vertices[a].Position) is { } pa && Project(_vertices[b].Position) is { } pb && Project(_vertices[c].Position) is { } pc)
            {
                Bgra32 edge = Theme.Accent.WithAlpha(120);
                painter.StrokeLine(pa, pb, Theme.Hairline, edge);
                painter.StrokeLine(pb, pc, Theme.Hairline, edge);
                painter.StrokeLine(pc, pa, Theme.Hairline, edge);
            }
        }

        painter.PopClip();
    }

    // Frames what the triangles draw: a skinned model's extra influence entries are not drawn and sit about their bones.
    private void Frame(ReadOnlySpan<Vertex3D> vertices)
    {
        Vector3f min = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3f max = new(float.MinValue, float.MinValue, float.MinValue);
        foreach (int index in _triangles.SelectMany(static t => t).SelectMany(static t => (int[])[t.A, t.B, t.C]).Distinct())
        {
            Vector3f p = vertices[index].Position;
            min = new Vector3f(MathF.Min(min.X, p.X), MathF.Min(min.Y, p.Y), MathF.Min(min.Z, p.Z));
            max = new Vector3f(MathF.Max(max.X, p.X), MathF.Max(max.Y, p.Y), MathF.Max(max.Z, p.Z));
        }

        _centre = (min + max) * 0.5f;
        _radius = MathF.Max(1e-3f, ((max - min) * 0.5f).Length);
    }

    private void ReleaseImage()
    {
        if (_image is { } image)
        {
            _images.DestroyImage(image);
            _image = null;
        }
    }
}
