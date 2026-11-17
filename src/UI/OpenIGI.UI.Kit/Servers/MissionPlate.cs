using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Servers;

/// <summary>
/// A map's plate at the top of the page's side panel: the mission's picture filling it, shaded for the words to stand on, the
/// map's name large along its foot under a small caption, and -- when a mode is played on it -- the mode's badge in its top
/// corner and the mode's colour along its lower edge. A map with no picture is dark hatching instead, the mission's number
/// large in its corner.
/// </summary>
public sealed class MissionPlate : UiElement
{
    private readonly UiTheme _theme;

    /// <summary>Initializes a new instance of the <see cref="MissionPlate"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="number">The mission's number, or empty.</param>
    /// <param name="caption">The small line over the title.</param>
    /// <param name="title">The map's name, or what stands for it.</param>
    /// <param name="height">Its height, in design units.</param>
    public MissionPlate(UiTheme theme, string number, string caption, string title, float height)
    {
        _theme = theme;
        Number = number;
        Caption = caption.ToUpperInvariant();
        Title = title.ToUpperInvariant();
        PlateHeight = height;
    }

    /// <summary>Gets the mission's number.</summary>
    public string Number { get; }

    /// <summary>Gets the small line over the title.</summary>
    public string Caption { get; }

    /// <summary>Gets the map's name.</summary>
    public string Title { get; }

    /// <summary>Gets its height, in design units.</summary>
    public float PlateHeight { get; }

    /// <summary>Gets or sets the mode played on the map, by its id, or <see langword="null"/> for none: its badge and colour.</summary>
    public string? Mode { get; set; }

    /// <summary>Gets or sets the mission's picture, or <see langword="null"/> for the hatching.</summary>
    public UiPicture? Picture { get; set; }

    /// <summary>Gets or sets the size of the map's name, in design units.</summary>
    public float TitleSize { get; set; } = 34f;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(PlateHeight));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        UiRect box = Bounds;
        Bgra32 colour = Mode is { } mode ? ModeLook.Colour(mode) : UiPalette.White(0.4f);
        if (Picture is { } picture && picture.Width > 0 && picture.Height > 0)
        {
            DrawPicture(painter, box, picture);
        }
        else
        {
            DrawHatching(painter, box, colour);
        }

        float pad = context.Px(20f);
        float titleSize = context.Px(TitleSize);
        float captionSize = context.Px(14f);
        float titleTop = box.Bottom - context.Px(16f) - (titleSize * 0.72f);
        var titleBox = new UiRect(box.X, titleTop, box.Width, titleSize * 0.72f);
        string title = _theme.Display.Fit(Title, titleSize, box.Width - (pad * 2f));
        painter.DrawLabel(_theme.Display, title, new UiVec2(box.X + pad, UiRowStyle.CapsCenterY(_theme.Display, titleSize, titleBox)), titleSize, _theme.Text, 0.03f);
        var captionBox = new UiRect(box.X, titleTop - context.Px(6f) - (captionSize * 0.72f), box.Width, captionSize * 0.72f);
        painter.DrawLabel(_theme.Heading, Caption, new UiVec2(box.X + pad, UiRowStyle.CapsCenterY(_theme.Heading, captionSize, captionBox)), captionSize, _theme.TextSecondary, 0.14f);

        if (Mode is { } played)
        {
            ModeLook.DrawBadge(painter, _theme, new UiVec2(box.X + pad, box.Y + context.Px(16f)), played);
        }
    }

    // The picture, filling the plate: scaled to cover it and cut to it about its middle, then shaded from the foot and from
    // the left so the name reads over whatever the picture is of.
    private static void DrawPicture(UiPainter painter, UiRect box, UiPicture picture)
    {
        picture.DrawCover(painter, box);

        Span<UiGradientStop> foot = [new(0f, UiPalette.InkAt(0.1f)), new(0.3f, UiPalette.InkAt(0.1f)), new(1f, UiPalette.InkAt(0.92f))];
        painter.FillGradient(box, foot, UiGradientDirection.Vertical);
    }

    // No picture: dark hatching, the mode's colour rising faintly from the foot, and the mission's number large in the corner.
    private void DrawHatching(UiPainter painter, UiRect box, Bgra32 colour)
    {
        LayoutContext context = painter.Context;
        painter.FillRect(box, Bgra32.FromRgba(30, 30, 27, 235));
        painter.PushClip(box);
        float step = context.Px(20f);
        Bgra32 stripe = Bgra32.FromRgba(48, 47, 42, 200);
        for (float x = box.X - box.Height; x < box.Right; x += step)
        {
            painter.StrokeLine(new UiVec2(x, box.Bottom), new UiVec2(x + box.Height, box.Y), context.Px(7f), stripe);
        }

        Span<UiGradientStop> glow = [new(0f, colour.WithAlpha(0)), new(1f, colour.WithAlpha((byte)(Mode is null ? 0 : 46)))];
        painter.FillGradient(box, glow, UiGradientDirection.Vertical);
        Span<UiGradientStop> shade = [new(0f, UiPalette.InkAt(0.62f)), new(0.7f, UiPalette.InkAt(0f))];
        painter.FillGradient(box, shade, UiGradientDirection.Horizontal);

        if (Number.Length > 0)
        {
            float big = context.Px(PlateHeight * 0.86f);
            float width = _theme.Display.MeasureWidth(Number, big);
            painter.DrawText(_theme.Display, Number, new UiVec2(box.Right - width - context.Px(16f), box.Y + ((box.Height - _theme.Display.LineHeight(big)) / 2f)), big, colour.WithAlpha((byte)(Mode is null ? 22 : 44)));
        }

        painter.PopClip();
    }
}
