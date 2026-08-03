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

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        _font.Measure(Text, context.Px(SizeInDesignUnits));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        float sizePx = painter.Context.Px(SizeInDesignUnits);
        float textWidth = _font.MeasureWidth(Text, sizePx);
        float x = TextAlignment switch
        {
            HorizontalAlignment.Center => Bounds.X + ((Bounds.Width - textWidth) * 0.5f),
            HorizontalAlignment.Right => Bounds.Right - textWidth,
            _ => Bounds.X,
        };

        float y = Bounds.Y;
        if (CenterTextVertically)
        {
            float textHeight = _font.Measure(Text, sizePx).Y;
            y = Bounds.Y + MathF.Max(0f, (Bounds.Height - textHeight) * 0.5f);
        }

        var origin = new UiVec2(x, y);
        if (Shadow is { } shadow)
        {
            float offset = MathF.Max(1f, painter.Context.Px(1.5f));
            painter.DrawTextShadowed(_font, Text, origin, sizePx, Color, shadow, new UiVec2(offset, offset));
        }
        else
        {
            painter.DrawText(_font, Text, origin, sizePx, Color);
        }
    }
}
