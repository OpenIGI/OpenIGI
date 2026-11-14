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
        if (Theme.Large)
        {
            DrawLarge(painter, appear, hot);
            return;
        }

        painter.FillRect(Bounds, UiPalette.Fade(Selected ? UiPalette.AccentAt(0.08f) : UiPalette.White(0.02f), appear));
        Bgra32 border = Selected ? Theme.Accent : Interpolate.Lerp(Theme.Border, UiPalette.AccentAt(0.45f), hot);
        painter.StrokeRect(Bounds, context.Px(1f), UiPalette.Fade(border, appear));

        bool tight = Bounds.Height < context.Px(110f);
        UiRect inner = Bounds.Deflate(context.Px(tight ? new Thickness(12f, 10f) : new Thickness(20f, 18f)));
        painter.DrawLabel(Theme.Mono, Caption, inner.TopLeft, context.Px(Theme.Large ? 13f : 12f), UiPalette.Fade(Selected ? Theme.Accent : Theme.Large ? Theme.TextSecondary : Theme.TextMuted, appear), Theme.Large ? 0.08f : 0.2f);

        float contentSize = context.Px(tight ? 17f : 22f);
        float detailSize = context.Px(Theme.Large ? 13f : 11f);
        float bottom = inner.Bottom;
        if (Detail.Length > 0)
        {
            painter.DrawLabel(Theme.Mono, Detail, new UiVec2(inner.X, bottom - Theme.Mono.LineHeight(detailSize)), detailSize, UiPalette.Fade(Theme.Large ? Theme.TextSecondary : Theme.TextMuted, appear), Theme.Large ? 0.04f : 0.12f);
            bottom -= Theme.Mono.LineHeight(detailSize) + context.Px(4f);
        }

        var contentBox = new UiRect(inner.X, bottom - contentSize, inner.Width, contentSize);
        Bgra32 content = IsEmpty ? (Theme.Large ? Theme.TextMuted : Theme.TextDim) : Theme.Text;
        painter.PushClip(Bounds);
        painter.DrawLabel(Theme.Heading, _content, new UiVec2(inner.X, UiRowStyle.CapsCenterY(Theme.Heading, contentSize, contentBox)), contentSize, UiPalette.Fade(content, appear), 0.14f);
        painter.PopClip();
    }

    // The large look. An empty place is a dashed outline with EMPTY in its middle behind a plus; a save is a dark plate,
    // its slot in a chip at the top, its mission along the foot over when it was made. The one picked is ringed in the accent.
    private void DrawLarge(UiPainter painter, float appear, float hot)
    {
        LayoutContext context = painter.Context;
        UiRect box = Bounds;
        float line = context.Px(1f);
        float pad = context.Px(12f);
        float captionSize = context.Px(13f);
        if (IsEmpty)
        {
            painter.FillRect(box, UiPalette.Fade(Selected ? UiPalette.AccentAt(0.08f) : UiPalette.InkAt(0.25f), appear));
            Bgra32 dash = UiPalette.Fade(Selected ? Theme.Accent : Interpolate.Lerp(UiPalette.White(0.22f), UiPalette.AccentAt(0.6f), hot), appear);
            float step = context.Px(4f);
            for (float x = box.X; x < box.Right; x += step * 2f)
            {
                float length = MathF.Min(step, box.Right - x);
                painter.FillRect(new UiRect(x, box.Y, length, line), dash);
                painter.FillRect(new UiRect(x, box.Bottom - line, length, line), dash);
            }

            for (float y = box.Y; y < box.Bottom; y += step * 2f)
            {
                float length = MathF.Min(step, box.Bottom - y);
                painter.FillRect(new UiRect(box.X, y, line, length), dash);
                painter.FillRect(new UiRect(box.Right - line, y, line, length), dash);
            }

            painter.DrawLabel(Theme.Mono, Caption, new UiVec2(box.X + pad, box.Y + pad), captionSize, UiPalette.Fade(Selected ? Theme.Accent : Theme.TextSecondary, appear), 0.04f);

            float emptySize = context.Px(15f);
            float width = UiPainter.MeasureLabel(Theme.Heading, _content, emptySize, 0.12f);
            float mark = context.Px(12f);
            float total = mark + context.Px(8f) + width;
            float left = box.Center.X - (total * 0.5f);
            Bgra32 ink = UiPalette.Fade(Theme.TextMuted, appear);
            KitIcons.Draw(painter, KitIcon.Plus, new UiVec2(left + (mark * 0.5f), box.Center.Y), mark, ink);
            painter.DrawLabel(Theme.Heading, _content, new UiVec2(left + mark + context.Px(8f), UiRowStyle.CapsCenterY(Theme.Heading, emptySize, box)), emptySize, ink, 0.12f);
            return;
        }

        Span<OpenIGI.UI.Drawing.UiGradientStop> plate = [new(0f, Bgra32.FromRgba(34, 36, 36, (byte)(235f * appear))), new(1f, Bgra32.FromRgba(12, 14, 15, (byte)(245f * appear)))];
        painter.FillGradient(box, plate, OpenIGI.UI.Drawing.UiGradientDirection.Vertical);
        painter.StrokeRect(box, context.Px(Selected ? 2f : 1f), UiPalette.Fade(Selected ? Theme.Accent : Interpolate.Lerp(UiPalette.White(0.16f), UiPalette.AccentAt(0.6f), hot), appear));

        float chipWidth = UiPainter.MeasureLabel(Theme.Mono, Caption, captionSize, 0.04f) + context.Px(12f);
        var chip = new UiRect(box.X + context.Px(8f), box.Y + context.Px(8f), chipWidth, context.Px(22f));
        painter.FillRect(chip, UiPalette.InkAt(0.7f * appear));
        painter.DrawLabel(Theme.Mono, Caption, new UiVec2(chip.X + context.Px(6f), UiRowStyle.CenterY(Theme.Mono, captionSize, chip)), captionSize, UiPalette.Fade(Selected ? Theme.Accent : Theme.Text, appear), 0.04f);

        float detailSize = context.Px(13f);
        float bottom = box.Bottom - pad;
        if (Detail.Length > 0)
        {
            painter.DrawLabel(Theme.Mono, OpenIGI.UI.Text.TextFit.ToWidth(Theme.Mono, Detail, detailSize, box.Width - (pad * 2f)), new UiVec2(box.X + pad, bottom - Theme.Mono.LineHeight(detailSize)), detailSize, UiPalette.Fade(Theme.TextSecondary, appear), 0.02f);
            bottom -= Theme.Mono.LineHeight(detailSize) + context.Px(4f);
        }

        float contentSize = context.Px(18f);
        var contentBox = new UiRect(box.X + pad, bottom - (contentSize * 0.74f), box.Width - (pad * 2f), contentSize * 0.74f);
        painter.DrawLabel(Theme.Display, OpenIGI.UI.Text.TextFit.ToWidth(Theme.Display, _content, contentSize, contentBox.Width, 0.04f), new UiVec2(contentBox.X, UiRowStyle.CapsCenterY(Theme.Display, contentSize, contentBox)), contentSize, UiPalette.Fade(Theme.Text, appear), 0.04f);
    }
}
