using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// One place in the party, a square in the page's header: a member's initial, outlined in the accent and underlined when it
/// leads; or an open place, dashed, with a plus, which invites a friend.
/// </summary>
internal sealed class PartySlot : Control
{
    private readonly string _mark;

    /// <summary>Initializes a new instance of the <see cref="PartySlot"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="mark">The member's initial, or <see langword="null"/> for an open place.</param>
    public PartySlot(UiTheme theme, string? mark)
        : base(theme)
    {
        _mark = mark ?? "+";
        Open = mark is null;
    }

    /// <summary>Gets a value indicating whether the place is open.</summary>
    public bool Open { get; }

    /// <summary>Gets or sets a value indicating whether its member leads the party.</summary>
    public bool Leads { get; set; }

    /// <summary>Gets or sets a value indicating whether it is this player.</summary>
    public bool IsYou { get; set; }

    /// <summary>Gets or sets a value indicating whether it is the member picked, whose line shows under the header.</summary>
    public bool Selected { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(context.Px(52f), context.Px(52f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        float hot = MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f);
        UiRect box = Bounds;
        float line = context.Px(1f);
        if (Open)
        {
            Bgra32 dash = UiPalette.Fade(hot > 0f ? UiPalette.AccentAt(0.6f) : UiPalette.White(0.22f), appear);
            Dashes(painter, box, line, context.Px(5f), dash);
        }
        else
        {
            bool strong = Leads || Selected;
            painter.FillRect(box, UiPalette.Fade(strong ? UiPalette.AccentAt(0.12f) : UiPalette.White(0.04f + (0.04f * hot)), appear));
            painter.StrokeRect(box, line, UiPalette.Fade(strong || hot > 0f ? Theme.Accent : UiPalette.White(0.3f), appear));
        }

        float size = context.Px(20f);
        Bgra32 ink = Open ? Theme.TextDim : Leads || IsYou ? Theme.Accent : Theme.Text;
        float width = Theme.Heading.MeasureWidth(_mark, size);
        float y = Leads ? box.Y + context.Px(9f) : UiRowStyle.CapsCenterY(Theme.Heading, size, box);
        painter.DrawText(Theme.Heading, _mark, new UiVec2(box.X + ((box.Width - width) / 2f), y), size, UiPalette.Fade(ink, appear));
        if (Leads)
        {
            float bar = context.Px(14f);
            painter.FillRect(new UiRect(box.X + ((box.Width - bar) / 2f), box.Bottom - context.Px(12f), bar, context.Px(2f)), UiPalette.Fade(Theme.Accent, appear));
        }
    }

    // A dashed outline: each side in short strokes.
    private static void Dashes(UiPainter painter, UiRect box, float thickness, float step, Bgra32 colour)
    {
        for (float x = box.X; x < box.Right; x += step * 2f)
        {
            float length = MathF.Min(step, box.Right - x);
            painter.FillRect(new UiRect(x, box.Y, length, thickness), colour);
            painter.FillRect(new UiRect(x, box.Bottom - thickness, length, thickness), colour);
        }

        for (float y = box.Y; y < box.Bottom; y += step * 2f)
        {
            float length = MathF.Min(step, box.Bottom - y);
            painter.FillRect(new UiRect(box.X, y, thickness, length), colour);
            painter.FillRect(new UiRect(box.Right - thickness, y, thickness, length), colour);
        }
    }
}

/// <summary>A filter that is on or off, as a box ticked in the accent before its name: HIDE FULL.</summary>
internal sealed class CheckChip : Control
{
    private readonly string _text;

    /// <summary>Initializes a new instance of the <see cref="CheckChip"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="text">Its name, shown in capitals.</param>
    public CheckChip(UiTheme theme, string text)
        : base(theme)
    {
        _text = text.ToUpperInvariant();
    }

    /// <summary>Gets or sets a value indicating whether it is on.</summary>
    public bool Checked { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        new(context.Px(12f + 10f + 24f) + UiPainter.MeasureLabel(Theme.Heading, _text, context.Px(14f), 0.22f), context.Px(34f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        float hot = MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f);
        float line = context.Px(1f);
        painter.StrokeRect(Bounds, line, UiPalette.Fade(hot > 0f ? UiPalette.AccentAt(0.6f) : UiPalette.White(0.18f), appear));
        float tick = context.Px(12f);
        var box = new UiRect(Bounds.X + context.Px(12f), Bounds.Y + ((Bounds.Height - tick) / 2f), tick, tick);
        if (Checked)
        {
            painter.FillRect(box, UiPalette.Fade(Theme.Accent, appear));
        }
        else
        {
            painter.StrokeRect(box, line, UiPalette.Fade(Theme.TextMuted, appear));
        }

        float size = context.Px(14f);
        painter.DrawLabel(Theme.Heading, _text, new UiVec2(box.Right + context.Px(10f), UiRowStyle.CapsCenterY(Theme.Heading, size, Bounds)), size, UiPalette.Fade(Checked ? Theme.Text : Theme.TextSecondary, appear), 0.22f);
    }
}

/// <summary>
/// A mission's plate at the top of the page's side panel: dark hatching, the mission's number large in its corner, and a
/// caption naming it. It stands in for a picture of the map.
/// </summary>
internal sealed class MissionPlate : UiElement
{
    private readonly UiTheme _theme;

    /// <summary>Initializes a new instance of the <see cref="MissionPlate"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="number">The mission's number, or empty.</param>
    /// <param name="caption">The line along its foot.</param>
    /// <param name="height">Its height, in design units.</param>
    public MissionPlate(UiTheme theme, string number, string caption, float height)
    {
        _theme = theme;
        Number = number;
        Caption = caption.ToUpperInvariant();
        PlateHeight = height;
    }

    /// <summary>Gets the mission's number.</summary>
    public string Number { get; }

    /// <summary>Gets the line along its foot.</summary>
    public string Caption { get; }

    /// <summary>Gets its height, in design units.</summary>
    public float PlateHeight { get; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(PlateHeight));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        UiRect box = Bounds;
        painter.FillRect(box, Bgra32.FromRgba(36, 35, 31, 220));
        painter.PushClip(box);
        float step = context.Px(20f);
        Bgra32 stripe = Bgra32.FromRgba(48, 47, 42, 200);
        for (float x = box.X - box.Height; x < box.Right; x += step)
        {
            painter.StrokeLine(new UiVec2(x, box.Bottom), new UiVec2(x + box.Height, box.Y), context.Px(7f), stripe);
        }

        if (Number.Length > 0)
        {
            float big = context.Px(PlateHeight * 0.62f);
            float width = _theme.Display.MeasureWidth(Number, big);
            painter.DrawText(_theme.Display, Number, new UiVec2(box.Right - width - context.Px(18f), box.Y + context.Px(6f)), big, UiPalette.White(0.07f));
        }

        painter.PopClip();
        float size = context.Px(11f);
        painter.DrawLabel(_theme.Mono, Caption, new UiVec2(box.X + context.Px(14f), box.Bottom - context.Px(14f) - _theme.Mono.LineHeight(size)), size, _theme.TextMuted, 0.15f);
    }
}
