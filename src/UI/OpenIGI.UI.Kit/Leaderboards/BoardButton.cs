using OpenIGI.Core.Graphics;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Leaderboards;

/// <summary>How a <see cref="BoardButton"/> stands out.</summary>
public enum BoardButtonLook
{
    /// <summary>Filled in the accent: the page's one thing to do.</summary>
    Solid,

    /// <summary>Outlined in the text's colour on dark: a second thing to do.</summary>
    Outline,

    /// <summary>Outlined quietly: a thing seldom done.</summary>
    Quiet,

    /// <summary>Outlined in the accent, washed with it: one of a row's actions.</summary>
    Accent,
}

/// <summary>
/// A board's button: a caption in capitals, an icon before it and the key that presses it after it, an arrow at the end of the
/// page's main one, and a line under the caption where it says more. Solid in the accent, or outlined.
/// </summary>
public sealed class BoardButton : Control
{
    /// <summary>Initializes a new instance of the <see cref="BoardButton"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="caption">What it says.</param>
    /// <param name="look">How it stands out.</param>
    public BoardButton(UiTheme theme, string caption, BoardButtonLook look = BoardButtonLook.Outline)
        : base(theme)
    {
        Caption = caption ?? throw new ArgumentNullException(nameof(caption));
        Look = look;
    }

    /// <summary>Gets or sets what it says.</summary>
    public string Caption { get; set; }

    /// <summary>Gets or sets the line under the caption; empty for none.</summary>
    public string Detail { get; set; } = string.Empty;

    /// <summary>Gets or sets how it stands out.</summary>
    public BoardButtonLook Look { get; set; }

    /// <summary>Gets or sets the icon before the caption.</summary>
    public KitIcon Icon { get; set; }

    /// <summary>Gets or sets the key that presses it, shown on a cap; empty for none.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether an arrow ends it, and the caption stands at its left rather than its middle.</summary>
    public bool Arrow { get; set; }

    /// <summary>Gets or sets the caption's size, in design units.</summary>
    public float TextSize { get; set; } = 13f;

    /// <summary>Gets or sets its height, in design units.</summary>
    public float ButtonHeight { get; set; } = 36f;

    /// <summary>Gets or sets its width, in design units; nought to fit what it says.</summary>
    public float ButtonWidth { get; set; }

    /// <summary>Gets or sets a value indicating whether it is drawn as the row's lit one: solid, though its look is another.</summary>
    public bool Lit { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float height = context.Px(ButtonHeight);
        if (ButtonWidth > 0f)
        {
            return new UiVec2(context.Px(ButtonWidth), height);
        }

        if (Arrow && HorizontalAlignment == HorizontalAlignment.Stretch)
        {
            return new UiVec2(available.X, height);
        }

        return new UiVec2(ContentWidth(context), height);
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float appear = Visual.Appear * (IsEnabled ? 1f : 0.45f);
        UiRect box = Bounds;
        float hot = MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f);
        bool solid = Look == BoardButtonLook.Solid || Lit;
        Bgra32 ink;
        if (solid)
        {
            painter.FillRect(box, UiPalette.Fade(hot > 0.5f ? Theme.AccentHover : Theme.Accent, appear));
            ink = BoardLook.OnAccent;
        }
        else
        {
            Bgra32 edge = Look switch
            {
                BoardButtonLook.Outline => Theme.TextBright,
                BoardButtonLook.Accent => Theme.Accent.WithAlpha(140),
                _ => UiPalette.White(0.22f),
            };
            painter.FillRect(box, Look == BoardButtonLook.Accent ? UiPalette.AccentAt((0.08f + (0.12f * hot)) * appear) : Bgra32.FromRgba(10, 11, 12, (byte)(153f * appear)));
            if (hot > 0.001f && Look != BoardButtonLook.Accent)
            {
                painter.FillRect(box, UiPalette.White(0.06f * hot * appear));
            }

            painter.StrokeRect(box, context.Px(1f), UiPalette.Fade(hot > 0.5f ? Theme.Accent : edge, appear));
            ink = Look == BoardButtonLook.Quiet ? Theme.TextSecondary : Theme.TextBright;
        }

        ink = UiPalette.Fade(ink, appear);
        float mid = box.Center.Y;
        float captionSize = context.Px(TextSize);
        string caption = Caption.ToUpperInvariant();
        float captionWidth = UiPainter.MeasureLabel(Theme.Heading, caption, captionSize, 0.12f);
        float iconSize = context.Px(TextSize + 1f);
        float gap = context.Px(8f);
        float keyWidth = Key.Length > 0 ? BoardLook.KeyWidth(context, Theme, Key) : 0f;
        float x;
        if (Arrow)
        {
            x = box.X + context.Px(20f);
        }
        else
        {
            // An icon alone stands in the middle, with no gap kept for a caption.
            float iconRoom = Icon != KitIcon.None ? iconSize + (caption.Length > 0 ? gap : 0f) : 0f;
            float inner = iconRoom + captionWidth + (Key.Length > 0 ? gap + keyWidth : 0f);
            x = box.Center.X - (inner / 2f);
        }

        if (Icon != KitIcon.None)
        {
            BoardLook.Icon(painter, Theme, Icon, new UiVec2(x + (iconSize / 2f), mid), iconSize, ink);
            x += iconSize + gap + (Arrow ? context.Px(4f) : 0f);
        }

        // The words keep clear of what stands at the right: the arrow and the key's cap.
        float arrowRoom = Arrow ? context.Px(20f + 18f + 14f) + (Key.Length > 0 ? keyWidth + context.Px(14f) : 0f) : 0f;
        float room = Arrow ? MathF.Max(0f, box.Right - arrowRoom - x) : box.Right - x;
        float detailSize = context.Px(11f);
        bool detail = Detail.Length > 0;
        float captionTop = detail
            ? mid - ((captionSize * 1.05f) + context.Px(3f) + Theme.Mono.LineHeight(detailSize)) / 2f
            : UiRowStyle.CapsCenterY(Theme.Heading, captionSize, box);
        string shown = Arrow ? Theme.Heading.Fit(caption, captionSize, room) : caption;
        float end = painter.DrawLabel(Theme.Heading, shown, new UiVec2(x, captionTop), captionSize, ink, 0.12f).X;
        if (detail)
        {
            Bgra32 detailInk = solid ? Bgra32.FromRgba(74, 57, 22, (byte)(255f * appear)) : UiPalette.Fade(Theme.TextSecondary, appear);
            painter.DrawText(Theme.Mono, Theme.Mono.Fit(Detail, detailSize, room), new UiVec2(x, captionTop + (captionSize * 1.05f) + context.Px(3f)), detailSize, detailInk);
        }

        if (Arrow)
        {
            float arrow = context.Px(18f);
            float right = box.Right - context.Px(20f);
            BoardLook.Icon(painter, Theme, KitIcon.ArrowRight, new UiVec2(right - (arrow / 2f), mid), arrow, ink);
            if (Key.Length > 0)
            {
                BoardLook.Key(painter, Theme, Key, right - arrow - context.Px(14f) - keyWidth, mid, onAccent: solid, appear);
            }
        }
        else if (Key.Length > 0)
        {
            BoardLook.Key(painter, Theme, Key, end + gap, mid, onAccent: solid, appear);
        }
    }

    private float ContentWidth(LayoutContext context)
    {
        float captionWidth = UiPainter.MeasureLabel(Theme.Heading, Caption.ToUpperInvariant(), context.Px(TextSize), 0.12f);
        float icon = Icon != KitIcon.None ? context.Px(TextSize + 1f) + context.Px(8f) : 0f;
        float key = Key.Length > 0 ? BoardLook.KeyWidth(context, Theme, Key) + context.Px(8f) : 0f;
        float arrow = Arrow ? context.Px(18f + 14f) : 0f;
        return captionWidth + icon + key + arrow + context.Px(Arrow ? 40f : 28f);
    }
}
