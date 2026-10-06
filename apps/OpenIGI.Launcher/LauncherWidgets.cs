using OpenIGI.Core.Graphics;
using OpenIGI.UI;
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
