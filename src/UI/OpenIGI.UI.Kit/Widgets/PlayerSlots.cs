using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Widgets;

/// <summary>
/// The places in a game, a square for each: the ones taken filled in the game's colour, the open ones outlined. It wraps to
/// as many rows as it needs, and a game too large to show a square for each shows as many as fit in two rows.
/// </summary>
public sealed class PlayerSlots : UiElement
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
