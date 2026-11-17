using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Kit;
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

    /// <summary>Gets or sets the square's side, in design units.</summary>
    public float Side { get; set; } = 40f;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(context.Px(Side), context.Px(Side));

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
            Dashes(painter, box, line, context.Px(4f), UiPalette.Fade(hot > 0f ? UiPalette.AccentAt(0.7f) : UiPalette.White(0.3f), appear));
            OpenIGI.UI.Kit.KitIcons.Draw(painter, OpenIGI.UI.Kit.KitIcon.Plus, box.Center, context.Px(14f), UiPalette.Fade(hot > 0f ? Theme.Accent : Theme.TextMuted, appear));
            return;
        }

        bool strong = Leads || IsYou || Selected;
        painter.FillRect(box, UiPalette.Fade(Selected ? UiPalette.AccentAt(0.16f) : Bgra32.FromRgba(10, 12, 13, 128), appear));
        painter.StrokeRect(box, line, UiPalette.Fade(strong || hot > 0f ? Theme.Accent : UiPalette.White(0.3f), appear));

        float size = context.Px(Side * 0.45f);
        Bgra32 ink = strong ? Theme.Accent : Theme.Text;
        float width = Theme.Display.MeasureWidth(_mark, size);
        var caps = Leads ? new UiRect(box.X, box.Y, box.Width, box.Height - context.Px(6f)) : box;
        painter.DrawText(Theme.Display, _mark, new UiVec2(box.X + ((box.Width - width) / 2f), UiRowStyle.CapsCenterY(Theme.Display, size, caps)), size, UiPalette.Fade(ink, appear));
        if (Leads)
        {
            // The leader's is underlined.
            float bar = context.Px(12f);
            painter.FillRect(new UiRect(box.X + ((box.Width - bar) / 2f), box.Bottom - context.Px(8f), bar, context.Px(2f)), UiPalette.Fade(Theme.Accent, appear));
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

