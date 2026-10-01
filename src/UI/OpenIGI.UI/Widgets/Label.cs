using OpenIGI.Core.Graphics;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;

namespace OpenIGI.UI.Widgets;

/// <summary>A run of text, measured into the layout and drawn with an optional soft shadow.</summary>
/// <remarks>
/// The simplest leaf and the one every other widget leans on for its caption. It measures its text
/// through the font so a stack or grid sizes to it, and it is not focusable — text is read, not
/// operated.
/// </remarks>
public class Label : UiElement
{
    private readonly UiFont _font;
    private string? _wrapped;
    private string? _wrappedFrom;
    private float _wrapWidth;

    /// <summary>Initializes a new instance of the <see cref="Label"/> class.</summary>
    /// <param name="font">The font to measure and draw with.</param>
    /// <param name="text">The text to show.</param>
    /// <param name="sizeInDesignUnits">The text size in design units.</param>
    /// <param name="color">The text colour.</param>
    /// <exception cref="ArgumentNullException"><paramref name="font"/> is <see langword="null"/>.</exception>
    public Label(UiFont font, string text, float sizeInDesignUnits, Bgra32 color)
    {
        ArgumentNullException.ThrowIfNull(font);
        _font = font;
        Text = text ?? string.Empty;
        SizeInDesignUnits = sizeInDesignUnits;
        Color = color;
    }

    /// <summary>Gets or sets the text to show.</summary>
    public string Text { get; set; }

    /// <summary>Gets or sets the text size in design units.</summary>
    public float SizeInDesignUnits { get; set; }

    /// <summary>Gets or sets the text colour.</summary>
    public Bgra32 Color { get; set; }

    /// <summary>Gets or sets the shadow colour, or <see langword="null"/> for no shadow.</summary>
    public Bgra32? Shadow { get; set; }

    /// <summary>Gets or sets how the text sits horizontally when the label is wider than its text.</summary>
    public HorizontalAlignment TextAlignment { get; set; } = HorizontalAlignment.Left;

    /// <summary>Gets or sets a value indicating whether the text is centred vertically in the label's bounds.</summary>
    /// <remarks>Only matters when a container stretches the label taller than its text — a nav bar row.</remarks>
    public bool CenterTextVertically { get; set; }

    /// <summary>Gets or sets a value indicating whether the text is broken onto lines to fit the width offered.</summary>
    /// <remarks>
    /// A wrapping label measures to the width it is given and reports the height the wrapped text needs,
    /// so a stack below it moves down as the text grows. It is what a mission briefing or a credits
    /// paragraph wants; a caption never needs it.
    /// </remarks>
    public bool Wrap { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float sizePx = context.Px(SizeInDesignUnits);
        string drawn = Resolve(sizePx, available.X);
        UiVec2 measured = _font.Measure(drawn, sizePx);
        return _wrapped is null ? measured : new UiVec2(available.X, measured.Y);
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        float sizePx = painter.Context.Px(SizeInDesignUnits);
        string drawn = _wrapped ?? Text;

        float y = Bounds.Y;
        if (CenterTextVertically)
        {
            float textHeight = _font.Measure(drawn, sizePx).Y;
            y = Bounds.Y + MathF.Max(0f, (Bounds.Height - textHeight) * 0.5f);
        }

        // Each line is placed on its own so alignment is per line, which is the only reading that makes
        // sense once the text has been broken onto several.
        float lineHeight = _font.LineHeight(sizePx);
        foreach (ReadOnlyMemory<char> line in Lines(drawn))
        {
            float lineWidth = _font.MeasureWidth(line.Span, sizePx);
            float x = TextAlignment switch
            {
                HorizontalAlignment.Center => Bounds.X + ((Bounds.Width - lineWidth) * 0.5f),
                HorizontalAlignment.Right => Bounds.Right - lineWidth,
                _ => Bounds.X,
            };

            var origin = new UiVec2(x, y);
            if (Shadow is { } shadow)
            {
                float offset = MathF.Max(1f, painter.Context.Px(1.5f));
                painter.DrawTextShadowed(_font, line.Span, origin, sizePx, Color, shadow, new UiVec2(offset, offset));
            }
            else
            {
                painter.DrawText(_font, line.Span, origin, sizePx, Color);
            }

            y += lineHeight;
        }
    }

    // The text as it will be drawn, wrapping it when asked and only when the text or the width changed.
    private string Resolve(float sizePx, float availableWidth)
    {
        if (!Wrap || float.IsPositiveInfinity(availableWidth) || availableWidth <= 0f)
        {
            _wrapped = null;
            _wrappedFrom = null;
            return Text;
        }

        if (_wrapped is null || _wrappedFrom != Text || MathF.Abs(_wrapWidth - availableWidth) > 0.5f)
        {
            _wrapped = _font.WrapText(Text, sizePx, availableWidth);
            _wrappedFrom = Text;
            _wrapWidth = availableWidth;
        }

        return _wrapped;
    }

    private static IEnumerable<ReadOnlyMemory<char>> Lines(string text)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                yield return text.AsMemory(start, i - start);
                start = i + 1;
            }
        }

        yield return text.AsMemory(start);
    }
}
