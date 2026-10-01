using OpenIGI.Core.Graphics;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Widgets;

/// <summary>How an <see cref="IconButton"/> is drawn.</summary>
public enum IconButtonStyle
{
    /// <summary>A window-chrome button: no frame, a faint wash under the pointer.</summary>
    Chrome,

    /// <summary>A window-chrome close button: turns red under the pointer.</summary>
    ChromeClose,

    /// <summary>A square glass tile with the icon over a caption: the launcher's VERIFY / FOLDER / OPTIONS.</summary>
    Tile,
}

/// <summary>A button that is an icon, with an optional caption under it.</summary>
public class IconButton : Control
{
    private string _caption = string.Empty;

    /// <summary>Initializes a new instance of the <see cref="IconButton"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="icon">The icon.</param>
    /// <param name="caption">The caption under the icon, shown in capitals; empty for none.</param>
    public IconButton(UiTheme theme, KitIcon icon, string caption = "")
        : base(theme)
    {
        Icon = icon;
        Caption = caption;
    }

    /// <summary>Gets or sets the icon.</summary>
    public KitIcon Icon { get; set; }

    /// <summary>Gets or sets the caption.</summary>
    public string Caption
    {
        get => _caption;
        set => _caption = (value ?? string.Empty).ToUpperInvariant();
    }

    /// <summary>Gets or sets the style.</summary>
    public IconButtonStyle Style { get; set; } = IconButtonStyle.Chrome;

    /// <summary>Gets or sets the size of the button in design units, used when the layout does not stretch it.</summary>
    public UiVec2 Size { get; set; } = new(46f, 36f);

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        new(context.Px(Size.X), context.Px(Size.Y));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        float hot = MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f);
        Bgra32 ink;

        switch (Style)
        {
            case IconButtonStyle.ChromeClose:
                if (hot > 0.001f)
                {
                    painter.FillRect(Bounds, Bgra32.FromRgba(196, 55, 42, (byte)(255 * hot * appear)));
                }

                ink = Interpolate.Lerp(Theme.TextMuted, Theme.TextBright, hot);
                break;

            case IconButtonStyle.Tile:
                KitDraw.Glass(painter, Theme, Bounds, UiPalette.Ink.WithAlpha(140), 14f, Interpolate.Lerp(UiPalette.White(0.12f), Theme.Accent, hot));
                ink = Interpolate.Lerp(Theme.Text, Theme.Accent, hot);
                break;

            default:
                if (hot > 0.001f)
                {
                    painter.FillRect(Bounds, UiPalette.White(0.06f * hot * appear));
                }

                ink = Interpolate.Lerp(Theme.TextMuted, Theme.TextBright, hot);
                break;
        }

        ink = UiPalette.Fade(IsEnabled ? ink : Theme.TextDisabled, appear);
        if (_caption.Length == 0)
        {
            KitIcons.Draw(painter, Icon, Bounds.Center, context.Px(Style == IconButtonStyle.Tile ? 20f : 11f), ink);
            return;
        }

        float iconSize = context.Px(20f);
        float captionSize = context.Px(16f);
        float gap = context.Px(8f);
        float block = iconSize + gap + (captionSize * 0.7f);
        float top = Bounds.Center.Y - (block * 0.5f);
        KitIcons.Draw(painter, Icon, new UiVec2(Bounds.Center.X, top + (iconSize * 0.5f)), iconSize, ink);
        float width = UiPainter.MeasureLabel(Theme.Heading, _caption, captionSize, 0.18f);
        var box = new UiRect(Bounds.X, top + iconSize + gap, Bounds.Width, captionSize * 0.7f);
        painter.DrawLabel(Theme.Heading, _caption, new UiVec2(Bounds.Center.X - (width * 0.5f), UiRowStyle.CapsCenterY(Theme.Heading, captionSize, box)), captionSize, ink, 0.18f);
    }
}
