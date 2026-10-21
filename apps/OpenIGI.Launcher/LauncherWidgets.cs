using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Launcher;

/// <summary>The window chrome's caption: a small diamond and OPENIGI in monospace.</summary>
internal sealed class ChromeCaption(UiTheme theme) : UiElement
{
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        new(context.Px(8f + 10f) + UiPainter.MeasureLabel(theme.Mono, "OPENIGI", context.Px(14f), 0.28f), available.Y);

    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        painter.FillDiamond(new UiVec2(Bounds.X + context.Px(4f), Bounds.Center.Y), context.Px(8f), theme.Accent);
        float size = context.Px(14f);
        painter.DrawLabel(theme.Mono, "OPENIGI", new UiVec2(Bounds.X + context.Px(18f), UiRowStyle.CenterY(theme.Mono, size, Bounds)), size, theme.TextMuted, 0.28f);
    }
}

/// <summary>The header's connection status: a glowing dot and a word.</summary>
internal sealed class StatusIndicator(UiTheme theme) : UiElement
{
    public string Text { get; set; } = "ONLINE";

    public Bgra32 Color { get; set; } = UiPalette.Success;

    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        new(context.Px(7f + 10f) + UiPainter.MeasureLabel(theme.Mono, Text, context.Px(14f), 0.2f), available.Y);

    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        var dot = new UiVec2(Bounds.X + context.Px(3.5f), Bounds.Center.Y);
        Span<UI.Drawing.UiGradientStop> glow = [new(0f, Color.WithAlpha(110)), new(1f, Color.WithAlpha(0))];
        float reach = context.Px(9f);
        painter.FillRadialGradient(new UiRect(dot.X - reach, dot.Y - reach, reach * 2f, reach * 2f), dot, new UiVec2(reach, reach), glow);
        painter.FillDiamond(dot, context.Px(5f), Color);
        float size = context.Px(14f);
        painter.DrawLabel(theme.Mono, Text, new UiVec2(Bounds.X + context.Px(17f), UiRowStyle.CenterY(theme.Mono, size, Bounds)), size, theme.TextMuted, 0.2f);
    }
}

/// <summary>A bar of glass with a hairline along one edge: the chrome strip, the header, the footer.</summary>
internal sealed class Bar(UiTheme theme, Bgra32 tint, float blur, bool lineAtBottom) : Panel
{
    protected override void OnDraw(UiPainter painter)
    {
        painter.DrawBackdrop(Bounds, painter.Context.Px(blur), tint);
        float t = painter.Context.Px(1f);
        float y = lineAtBottom ? Bounds.Bottom - t : Bounds.Y;
        painter.FillRect(new UiRect(Bounds.X, y, Bounds.Width, t), lineAtBottom ? UiPalette.White(0.07f) : theme.Border);
    }
}

/// <summary>A run of text drawn the design's way: a face, a size, a colour, letterspacing, optional capitals.</summary>
internal sealed class Text : UiElement
{
    private readonly UI.Text.UiFont _font;
    private string _shown = string.Empty;
    private string _source = string.Empty;

    public Text(UI.Text.UiFont font, string text, float size, Bgra32 color, float tracking = 0f, bool uppercase = false)
    {
        _font = font;
        Size = size;
        Color = color;
        Tracking = tracking;
        Uppercase = uppercase;
        Value = text;
    }

    public string Value
    {
        get => _source;
        set
        {
            _source = value ?? string.Empty;
            _shown = Uppercase ? _source.ToUpperInvariant() : _source;
        }
    }

    public float Size { get; set; }

    public Bgra32 Color { get; set; }

    public float Tracking { get; set; }

    public bool Uppercase { get; }

    /// <summary>Gets or sets the text's alignment within its box.</summary>
    public HorizontalAlignment Align { get; set; } = HorizontalAlignment.Left;

    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        new(UiPainter.MeasureLabel(_font, _shown, context.Px(Size), Tracking), _font.LineHeight(context.Px(Size)));

    protected override void OnDraw(UiPainter painter)
    {
        float size = painter.Context.Px(Size);
        float width = UiPainter.MeasureLabel(_font, _shown, size, Tracking);
        float x = Align switch
        {
            HorizontalAlignment.Right => Bounds.Right - width,
            HorizontalAlignment.Center => Bounds.Center.X - (width * 0.5f),
            _ => Bounds.X,
        };

        painter.DrawLabel(_font, _shown, new UiVec2(x, UiRowStyle.CenterY(_font, size, Bounds)), size, Color, Tracking);
    }
}

/// <summary>Prose that wraps to the width it is given, at spaces.</summary>
internal sealed class Paragraph(UI.Text.UiFont font, string text, float size, Bgra32 color) : UiElement
{
    private string[] _lines = [];

    public string Value { get; set; } = text;

    public Bgra32 Color { get; set; } = color;

    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float sizePx = context.Px(size);
        _lines = font.WrapText(Value, sizePx, available.X).Split('\n');
        float widest = _lines.Length == 0 ? 0f : _lines.Max(line => font.MeasureWidth(line, sizePx));
        return new UiVec2(MathF.Min(widest, available.X), _lines.Length * font.LineHeight(sizePx));
    }

    protected override void OnDraw(UiPainter painter)
    {
        float sizePx = painter.Context.Px(size);
        float line = font.LineHeight(sizePx);
        for (int i = 0; i < _lines.Length; i++)
        {
            painter.DrawText(font, _lines[i], new UiVec2(Bounds.X, Bounds.Y + (i * line)), sizePx, Color);
        }
    }
}

/// <summary>The content rows' two mono inks: the grey of a caption, and the warm colour of a conflict.</summary>
internal static class ContentInk
{
    /// <summary>The colour a conflict, or a missing game or mod, is called out in.</summary>
    public static readonly Bgra32 Conflict = Bgra32.FromRgb(255, 138, 107);

    /// <summary>The grey of the design's mono captions and detail lines.</summary>
    public static readonly Bgra32 Caption = Bgra32.FromRgb(110, 112, 115);
}

/// <summary>
/// The ON/OFF switch the design gives a load-order entry: a cut-corner track with the two words, and an
/// amber block that slides under whichever is in force.
/// </summary>
internal sealed class OnOffSwitch(UiTheme theme) : Control(theme)
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

/// <summary>
/// The pack's picture, cropped to fill its box, its short code over the corner; dimmed while it is not in use.
/// </summary>
internal sealed class Thumbnail(UiTheme theme) : UiElement
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

/// <summary>
/// Name and tags over the summary, then a mono detail line; each cut with an ellipsis where the column ends.
/// </summary>
internal sealed class PackInfo(UiTheme theme) : UiElement
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
            Bgra32 ink = tag == "CONFLICT" ? ContentInk.Conflict : Bgra32.FromRgb(140, 142, 144);
            float width = UiPainter.MeasureLabel(theme.Mono, tag, monoSize, 0.16f);
            float boxHeight = theme.Mono.LineHeight(monoSize) + context.Px(4f);
            var box = new UiRect(x + context.Px(12f), y + ((nameHeight - boxHeight) * 0.5f), width + context.Px(14f), boxHeight);
            painter.StrokeRect(box, context.Px(1f), tag == "CONFLICT" ? ContentInk.Conflict.WithAlpha(150) : UiPalette.White(0.2f));
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

/// <summary>
/// A small grey caption over a value: the design's SIZE column.
/// </summary>
internal sealed class Labelled(UiTheme theme, string caption) : UiElement
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

/// <summary>
/// A diamond and a word, in the state's colour.
/// </summary>
internal sealed class StateLabel(UiTheme theme) : UiElement
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

/// <summary>
/// While a pack is downloading or installing: how much, and a thin bar.
/// </summary>
internal sealed class BusyBar(UiTheme theme) : UiElement
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

/// <summary>How a <see cref="CutButton"/> is drawn: filled amber, outlined, as a danger, or greyed out.</summary>
internal enum CutButtonStyle
{
    Filled,
    Outline,
    Danger,
    Disabled,
}

/// <summary>
/// The design's action button: 44 high, its bottom-right corner cut, filled amber or outlined.
/// </summary>
internal sealed class CutButton(UiTheme theme) : Control(theme)
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

/// <summary>
/// A lesser action under the button: a small word that lights up under the pointer.
/// </summary>
internal sealed class LinkButton(UiTheme theme) : Control(theme)
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

/// <summary>
/// The library's share of its drive: used and free over a bar, this game's part in amber.
/// </summary>
internal sealed class DiskMeter(UiTheme theme) : UiElement
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
