using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
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
/// A map's plate at the top of the page's side panel: the mission's picture filling it, shaded for the words to stand on, the
/// map's name large along its foot under a small caption, and -- when a mode is played on it -- the mode's badge in its top
/// corner and the mode's colour along its lower edge. A map with no picture is dark hatching instead, the mission's number
/// large in its corner.
/// </summary>
internal sealed class MissionPlate : UiElement
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
    public MissionPicture? Picture { get; set; }

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

        float pad = context.Px(18f);
        float edge = context.Px(3f);
        painter.FillRect(new UiRect(box.X, box.Bottom - edge, box.Width, edge), Mode is null ? UiPalette.White(0.1f) : colour);

        float titleSize = context.Px(34f);
        float captionSize = context.Px(10f);
        float titleTop = box.Bottom - edge - context.Px(12f) - (titleSize * 0.72f);
        var titleBox = new UiRect(box.X, titleTop, box.Width, titleSize * 0.72f);
        string title = ServerRow.Fit(Title, _theme.Display, titleSize, box.Width - (pad * 2f));
        painter.DrawLabel(_theme.Display, title, new UiVec2(box.X + pad, UiRowStyle.CapsCenterY(_theme.Display, titleSize, titleBox)), titleSize, _theme.TextBright, 0.04f);
        painter.DrawLabel(_theme.Mono, Caption, new UiVec2(box.X + pad, titleTop - context.Px(8f) - _theme.Mono.LineHeight(captionSize)), captionSize, _theme.TextMuted, 0.24f);

        if (Mode is { } played)
        {
            // The mode's badge: its code on its colour.
            float size = context.Px(13f);
            string code = ModeLook.Code(played);
            float width = UiPainter.MeasureLabel(_theme.Heading, code, size, 0.12f) + context.Px(20f);
            var badge = new UiRect(box.X + pad, box.Y + pad, width, context.Px(24f));
            painter.FillRect(badge, colour);
            painter.DrawLabel(_theme.Heading, code, new UiVec2(badge.X + context.Px(10f), UiRowStyle.CapsCenterY(_theme.Heading, size, badge)), size, _theme.OnAccent, 0.12f);
        }
    }

    // The picture, filling the plate: scaled to cover it and cut to it about its middle, then shaded from the foot and from
    // the left so the name reads over whatever the picture is of.
    private static void DrawPicture(UiPainter painter, UiRect box, MissionPicture picture)
    {
        float plate = box.Width / MathF.Max(1f, box.Height);
        float shot = picture.Width / (float)picture.Height;
        UiRect source = shot > plate
            ? new UiRect((1f - (plate / shot)) / 2f, 0f, plate / shot, 1f)
            : new UiRect(0f, (1f - (shot / plate)) / 2f, 1f, shot / plate);
        painter.DrawSprite(picture.Image, box, source, Bgra32.FromRgb(255, 255, 255));

        Span<UiGradientStop> foot = [new(0f, UiPalette.InkAt(0.18f)), new(0.45f, UiPalette.InkAt(0.1f)), new(1f, UiPalette.InkAt(0.82f))];
        painter.FillGradient(box, foot, UiGradientDirection.Vertical);
        Span<UiGradientStop> side = [new(0f, UiPalette.InkAt(0.5f)), new(0.6f, UiPalette.InkAt(0f))];
        painter.FillGradient(box, side, UiGradientDirection.Horizontal);
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

/// <summary>
/// The places in a game, a square for each: the ones taken filled in the game's colour, the open ones outlined. It wraps to
/// as many rows as it needs, and a game too large to show a square for each shows as many as fit in two rows.
/// </summary>
internal sealed class PlayerSlots : UiElement
{
    private const float Size = 20f;
    private const float Gap = 5f;
    private const int MostRows = 2;

    /// <summary>Gets or sets how many play.</summary>
    public int Players { get; set; }

    /// <summary>Gets or sets how many may.</summary>
    public int MaxPlayers { get; set; }

    /// <summary>Gets or sets the colour a taken place is filled in.</summary>
    public Bgra32 Colour { get; set; } = UiPalette.Accent;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        (int _, int rows) = Grid(context, available.X);
        return new UiVec2(available.X, (rows * context.Px(Size)) + (Math.Max(0, rows - 1) * context.Px(Gap)));
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        (int across, int rows) = Grid(context, Bounds.Width);
        int places = Math.Min(Math.Max(MaxPlayers, 0), across * rows);
        int taken = MaxPlayers <= places ? Math.Min(Players, places) : (int)MathF.Ceiling(Players * places / (float)MaxPlayers);
        float size = context.Px(Size);
        float pitch = size + context.Px(Gap);
        for (int i = 0; i < places; i++)
        {
            var box = new UiRect(Bounds.X + (i % across * pitch), Bounds.Y + (i / across * pitch), size, size);
            if (i < taken)
            {
                painter.FillRect(box, Colour);
            }
            else
            {
                painter.StrokeRect(box, context.Px(1f), UiPalette.White(0.16f));
            }
        }
    }

    // How many places a row holds in a width, and how many rows the game's places take, up to the most shown.
    private (int Across, int Rows) Grid(LayoutContext context, float width)
    {
        float pitch = context.Px(Size + Gap);
        int across = Math.Max(1, (int)((width + context.Px(Gap)) / pitch));
        int rows = Math.Clamp((Math.Max(MaxPlayers, 1) + across - 1) / across, 1, MostRows);
        return (across, rows);
    }
}
