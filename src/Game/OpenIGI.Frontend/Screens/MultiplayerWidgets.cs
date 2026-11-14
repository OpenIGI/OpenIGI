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

/// <summary>A panel's edge in a colour: the accent along the top of the side panel.</summary>
internal sealed class ColourEdge : UiElement
{
    /// <summary>Gets or sets the colour.</summary>
    public Bgra32 Colour { get; set; } = UiPalette.Accent;

    /// <summary>Gets or sets how thick it is, in design units.</summary>
    public float Thickness { get; set; } = 2f;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(Thickness));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter) => painter.FillRect(Bounds, Colour);
}

/// <summary>A filter that is on or off, as a small switch before its name: HIDE FULL.</summary>
internal sealed class CheckChip : Control
{
    private const float TextSize = 17f;
    private const float Tracking = 0.12f;

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
        new(context.Px(44f + 10f) + UiPainter.MeasureLabel(Theme.Heading, _text, context.Px(TextSize), Tracking), context.Px(44f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        float hot = MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f);
        var track = new UiRect(Bounds.X, Bounds.Center.Y - context.Px(11.5f), context.Px(44f), context.Px(23f));
        painter.FillRect(track, UiPalette.Fade(Checked ? Theme.Accent : UiPalette.White(0.06f), appear));
        painter.StrokeRect(track, context.Px(1f), UiPalette.Fade(Checked || hot > 0f ? Theme.Accent : UiPalette.White(0.28f), appear));
        float knob = context.Px(15f);
        float knobX = Checked ? track.Right - context.Px(4f) - knob : track.X + context.Px(4f);
        painter.FillRect(new UiRect(knobX, track.Center.Y - (knob / 2f), knob, knob), UiPalette.Fade(Checked ? Theme.OnAccent : Theme.TextSecondary, appear));

        float size = context.Px(TextSize);
        painter.DrawLabel(Theme.Heading, _text, new UiVec2(track.Right + context.Px(10f), UiRowStyle.CapsCenterY(Theme.Heading, size, Bounds)), size, UiPalette.Fade(Checked || hot > 0f ? Theme.Text : Theme.TextSecondary, appear), Tracking);
        if (Visual.Focus > 0.001f)
        {
            painter.StrokeRect(Bounds.Inflate(new Thickness(context.Px(4f), 0f)), context.Px(2f), UiPalette.Fade(Theme.AccentHover, Visual.Focus * appear));
        }
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
        string title = ServerRow.Fit(Title, _theme.Display, titleSize, box.Width - (pad * 2f));
        painter.DrawLabel(_theme.Display, title, new UiVec2(box.X + pad, UiRowStyle.CapsCenterY(_theme.Display, titleSize, titleBox)), titleSize, _theme.Text, 0.03f);
        var captionBox = new UiRect(box.X, titleTop - context.Px(6f) - (captionSize * 0.72f), box.Width, captionSize * 0.72f);
        painter.DrawLabel(_theme.Heading, Caption, new UiVec2(box.X + pad, UiRowStyle.CapsCenterY(_theme.Heading, captionSize, captionBox)), captionSize, _theme.TextSecondary, 0.14f);

        if (Mode is { } played)
        {
            // The mode's badge: its code in its colour, in a box outlined in it.
            float size = context.Px(13f);
            string code = ModeLook.Code(played);
            float width = UiPainter.MeasureLabel(_theme.Heading, code, size, 0.08f) + context.Px(16f);
            var badge = new UiRect(box.X + pad, box.Y + context.Px(16f), width, context.Px(24f));
            painter.FillRect(badge, Bgra32.FromRgba(10, 12, 13, 89));
            painter.StrokeRect(badge, context.Px(1f), colour);
            painter.DrawLabel(_theme.Heading, code, new UiVec2(badge.X + context.Px(8f), UiRowStyle.CapsCenterY(_theme.Heading, size, badge)), size, colour, 0.08f);
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
