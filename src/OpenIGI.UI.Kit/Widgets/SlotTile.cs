using OpenIGI.Core.Graphics;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Widgets;

/// <summary>
/// One save slot in a grid: its number at the top, what it holds — or EMPTY — at the bottom, outlined in
/// amber when it is the one selected.
/// </summary>
public class SlotTile : Control
{
    private string _content = "EMPTY";

    /// <summary>Initializes a new instance of the <see cref="SlotTile"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="caption">The slot's caption, such as SLOT 01.</param>
    public SlotTile(UiTheme theme, string caption)
        : base(theme)
    {
        Caption = (caption ?? string.Empty).ToUpperInvariant();
    }

    /// <summary>Gets the slot's caption.</summary>
    public string Caption { get; }

    /// <summary>Gets or sets what the slot holds, or <see langword="null"/> for an empty slot.</summary>
    public string? Content
    {
        get => IsEmpty ? null : _content;
        set
        {
            IsEmpty = string.IsNullOrEmpty(value);
            _content = IsEmpty ? "EMPTY" : value!.ToUpperInvariant();
        }
    }

    /// <summary>Gets or sets a second line under the content, such as when it was saved.</summary>
    public string Detail { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether the slot holds nothing.</summary>
    public bool IsEmpty { get; private set; } = true;

    /// <summary>Gets or sets a value indicating whether this is the selected slot.</summary>
    public bool Selected { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        new(available.X, MathF.Max(context.Px(96f), available.Y));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        float hot = MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f);

        painter.FillRect(Bounds, UiPalette.Fade(Selected ? UiPalette.AccentAt(0.08f) : UiPalette.White(0.02f), appear));
        Bgra32 border = Selected ? Theme.Accent : Interpolate.Lerp(Theme.Border, UiPalette.AccentAt(0.45f), hot);
        painter.StrokeRect(Bounds, context.Px(1f), UiPalette.Fade(border, appear));

        UiRect inner = Bounds.Deflate(context.Px(new Thickness(20f, 18f)));
        painter.DrawLabel(Theme.Mono, Caption, inner.TopLeft, context.Px(12f), UiPalette.Fade(Selected ? Theme.Accent : Theme.TextMuted, appear), 0.2f);

        float contentSize = context.Px(22f);
        float detailSize = context.Px(11f);
        float bottom = inner.Bottom;
        if (Detail.Length > 0)
        {
            painter.DrawLabel(Theme.Mono, Detail, new UiVec2(inner.X, bottom - Theme.Mono.LineHeight(detailSize)), detailSize, UiPalette.Fade(Theme.TextMuted, appear), 0.12f);
            bottom -= Theme.Mono.LineHeight(detailSize) + context.Px(4f);
        }

        var contentBox = new UiRect(inner.X, bottom - contentSize, inner.Width, contentSize);
        Bgra32 content = IsEmpty ? Theme.TextDim : Theme.Text;
        painter.PushClip(Bounds);
        painter.DrawLabel(Theme.Heading, _content, new UiVec2(inner.X, UiRowStyle.CapsCenterY(Theme.Heading, contentSize, contentBox)), contentSize, UiPalette.Fade(content, appear), 0.14f);
        painter.PopClip();
    }
}
