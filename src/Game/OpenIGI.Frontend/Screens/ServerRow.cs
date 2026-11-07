using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>The server browser's columns, shared by its header and its rows so the two line up.</summary>
internal static class ServerColumns
{
    /// <summary>Each column's share of the row's width: name, mode, map, players, ping, region.</summary>
    public static readonly float[] Weights = [3.4f, 1.5f, 1.9f, 0.9f, 0.8f, 0.8f];

    /// <summary>The columns' headings.</summary>
    public static readonly string[] Headings = ["NAME", "MODE", "MAP", "PLAYERS", "PING", "REGION"];

    /// <summary>Where each column starts across a row, from its left, given the row's width.</summary>
    /// <param name="width">The row's width.</param>
    /// <returns>Each column's left edge.</returns>
    public static float[] Lefts(float width)
    {
        float total = Weights.Sum();
        float[] lefts = new float[Weights.Length];
        float x = 0f;
        for (int i = 0; i < Weights.Length; i++)
        {
            lefts[i] = x;
            x += width * Weights[i] / total;
        }

        return lefts;
    }
}

/// <summary>One line of the server browser: a server or room in columns, focused and clicked as a list card is.</summary>
internal sealed class ServerRow : Control
{
    private readonly string[] _cells;
    private readonly string _tag;
    private readonly int? _ping;
    private readonly bool _full;

    /// <summary>Initializes a new instance of the <see cref="ServerRow"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="cells">What each column shows.</param>
    /// <param name="tag">A short mark after the name (<c>SERVER</c>, <c>LAN</c>, a lock), or empty.</param>
    /// <param name="ping">The round trip in milliseconds, which colours the ping column; <see langword="null"/> before it is known.</param>
    /// <param name="full">Whether it has no room left, which mutes the players column.</param>
    public ServerRow(UiTheme theme, string[] cells, string tag, int? ping, bool full)
        : base(theme)
    {
        _cells = cells;
        _tag = tag;
        _ping = ping;
        _full = full;
    }

    /// <summary>Gets or sets a value indicating whether it is the line last played on.</summary>
    public bool Highlighted { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(46f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        float hot = MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f);
        UiRect bounds = Bounds;
        if (Highlighted)
        {
            painter.FillRect(new UiRect(bounds.X, bounds.Y, context.Px(3f), bounds.Height), UiPalette.Fade(Theme.Accent, appear));
        }

        if (hot > 0.001f && IsEnabled)
        {
            painter.FillRect(bounds, UiPalette.White(0.06f * hot * appear));
        }

        painter.FillRect(new UiRect(bounds.X, bounds.Bottom - context.Px(1f), bounds.Width, context.Px(1f)), UiPalette.White(0.06f * appear));

        float pad = context.Px(14f);
        float[] lefts = ServerColumns.Lefts(bounds.Width);
        float size = context.Px(15f);
        for (int i = 0; i < _cells.Length && i < lefts.Length; i++)
        {
            Bgra32 colour = !IsEnabled ? Theme.TextDisabled
                : i == 0 ? Theme.TextBright
                : i == 3 && _full ? Theme.TextDim
                : i == 4 && _ping is { } ms ? (ms < 70 ? Theme.Success : ms < 140 ? Theme.Accent : Theme.Danger)
                : Theme.Text;
            float x = bounds.X + lefts[i] + (i == 0 ? pad : 0f);
            float limit = (i + 1 < lefts.Length ? bounds.X + lefts[i + 1] : bounds.Right) - x - context.Px(10f);
            string text = Fit(_cells[i], i == 0 ? Theme.Body : Theme.Mono, size, limit);
            if (i == 0)
            {
                painter.DrawText(Theme.Body, text, new UiVec2(x, UiRowStyle.CenterY(Theme.Body, size, bounds)), size, UiPalette.Fade(colour, appear));
                if (_tag.Length > 0)
                {
                    float after = x + Theme.Body.MeasureWidth(text, size) + context.Px(10f);
                    float tagSize = context.Px(10f);
                    if (after + UiPainter.MeasureLabel(Theme.Mono, _tag, tagSize, 0.16f) < bounds.X + lefts[1] - context.Px(8f))
                    {
                        painter.DrawLabel(Theme.Mono, _tag, new UiVec2(after, UiRowStyle.CenterY(Theme.Mono, tagSize, bounds)), tagSize, UiPalette.Fade(Theme.Accent, appear), 0.16f);
                    }
                }
            }
            else
            {
                painter.DrawLabel(Theme.Mono, text, new UiVec2(x, UiRowStyle.CenterY(Theme.Mono, size * 0.86f, bounds)), size * 0.86f, UiPalette.Fade(colour, appear), 0.06f);
            }
        }
    }

    // The text cut to the room it has, with an ellipsis.
    private static string Fit(string text, UiFont font, float size, float width)
    {
        if (width <= 0f || font.MeasureWidth(text, size) <= width)
        {
            return text;
        }

        for (int length = text.Length - 1; length > 1; length--)
        {
            string cut = string.Concat(text.AsSpan(0, length).TrimEnd(), "…");
            if (font.MeasureWidth(cut, size) <= width)
            {
                return cut;
            }
        }

        return "…";
    }
}
