using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The panel beside the settings that says what the row in focus does: its name, its explanation in full, and
/// a tag or two for when a change takes hold and what the setting started as.
/// </summary>
/// <remarks>
/// A line of help under the list had room for a sentence, cut where the line ran out; the rest was in a tooltip a
/// pad cannot raise. Here the whole explanation is on the page beside the row it belongs to, for whichever of the
/// four ways the focus was moved.
/// </remarks>
internal sealed class SettingsAbout : UiElement
{
    private readonly UiTheme _theme;
    private readonly List<(string Text, Bgra32 Ink)> _tags = [];
    private string _title = string.Empty;
    private string _body = string.Empty;
    private string _wrapped = string.Empty;
    private float _wrappedAt = -1f;
    private string[] _options = [];
    private int _current = -1;

    /// <summary>Initializes a new instance of the <see cref="SettingsAbout"/> class.</summary>
    /// <param name="theme">The theme.</param>
    public SettingsAbout(UiTheme theme)
    {
        _theme = theme;
    }

    /// <summary>Shows a setting.</summary>
    /// <param name="title">Its name.</param>
    /// <param name="body">What it does.</param>
    /// <param name="tags">Its tags, each with the colour it is outlined and written in.</param>
    /// <param name="options">What it chooses between, or none.</param>
    /// <param name="current">Which of those it holds.</param>
    public void Show(string title, string body, IReadOnlyList<(string Text, Bgra32 Ink)> tags, IReadOnlyList<string> options, int current)
    {
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(options);
        string upper = (title ?? string.Empty).ToUpperInvariant();
        _current = current;
        if (options.Count != _options.Length || !options.SequenceEqual(_options, StringComparer.OrdinalIgnoreCase))
        {
            _options = [.. options.Select(static option => option.ToUpperInvariant())];
        }

        if (upper == _title && body == _body && tags.Count == _tags.Count && tags.SequenceEqual(_tags))
        {
            return;
        }

        _title = upper;
        _body = body ?? string.Empty;
        _wrappedAt = -1f;
        _tags.Clear();
        _tags.AddRange(tags);
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        // Laid out once to find how tall it is -- the panel ends under what it says -- and then drawn.
        LayoutContext context = painter.Context;
        float bottom = MathF.Min(Bounds.Bottom, Lay(null, context) + context.Px(28f));
        var panel = new UiRect(Bounds.X, Bounds.Y, Bounds.Width, bottom - Bounds.Y);
        painter.FillRect(panel, Bgra32.FromRgba(12, 14, 15, 199));
        painter.StrokeRect(panel, context.Px(1f), UiPalette.White(0.12f));
        Lay(painter, context);
    }

    // Sets the panel's parts down its length, drawing them when given a painter; returns where the last ends.
    private float Lay(UiPainter? painter, LayoutContext context)
    {
        float pad = context.Px(28f);
        float x = Bounds.X + pad;
        float width = Bounds.Width - (pad * 2f);
        float y = Bounds.Y + pad;

        float eyebrow = context.Px(14f);
        var line = new UiRect(x, y, width, eyebrow * 1.2f);
        painter?.DrawLabel(_theme.Heading, "ABOUT THIS SETTING", new UiVec2(x, UiRowStyle.CapsCenterY(_theme.Heading, eyebrow, line)), eyebrow, _theme.TextMuted, 0.14f);
        y = line.Bottom + context.Px(14f);
        if (_title.Length == 0)
        {
            float note = context.Px(17f);
            painter?.DrawText(_theme.Body, "Select a setting to see what it does.", new UiVec2(x, y), note, _theme.TextSecondary);
            return y + _theme.Body.LineHeight(note);
        }

        // The name, set smaller where it is too long for the panel.
        float titleSize = context.Px(34f);
        float natural = UiPainter.MeasureLabel(_theme.Display, _title, titleSize, 0.03f);
        if (natural > width && natural > 0f)
        {
            titleSize *= width / natural;
        }

        var titleBox = new UiRect(x, y, width, context.Px(34f) * 0.74f);
        painter?.DrawLabel(_theme.Display, _title, new UiVec2(x, UiRowStyle.CapsCenterY(_theme.Display, titleSize, titleBox)), titleSize, _theme.Text, 0.03f);
        y = titleBox.Bottom + context.Px(14f);

        float bodySize = context.Px(17f);
        if (_wrappedAt != width)
        {
            _wrappedAt = width;
            _wrapped = _body.Length == 0 ? string.Empty : _theme.Body.WrapText(_body, bodySize, width);
        }

        if (_wrapped.Length > 0)
        {
            // A line and a quarter apart: a paragraph to be read, not a caption.
            float step = _theme.Body.LineHeight(bodySize) * 1.18f;
            foreach (string paragraph in _wrapped.Split('\n'))
            {
                if (y + step > Bounds.Bottom - pad)
                {
                    break;
                }

                painter?.DrawText(_theme.Body, paragraph, new UiVec2(x, y), bodySize, _theme.Text);
                y += step;
            }

            y += context.Px(14f) - (step - _theme.Body.LineHeight(bodySize));
        }

        // The tags, wrapping onto a second line where the panel is narrow.
        float tagSize = context.Px(13f);
        float tagHeight = context.Px(24f);
        float pen = x;
        bool tagged = false;
        foreach ((string text, Bgra32 ink) in _tags)
        {
            float tagWidth = UiPainter.MeasureLabel(_theme.Heading, text, tagSize, 0.08f) + context.Px(16f);
            if (pen + tagWidth > x + width && pen > x)
            {
                pen = x;
                y += tagHeight + context.Px(8f);
            }

            if (y + tagHeight > Bounds.Bottom - context.Px(8f))
            {
                break;
            }

            var tag = new UiRect(pen, y, tagWidth, tagHeight);
            painter?.FillRect(tag, UiPalette.InkAt(0.35f));
            painter?.StrokeRect(tag, context.Px(1f), ink);
            painter?.DrawLabel(_theme.Heading, TextFit.ToWidth(_theme.Heading, text, tagSize, width - context.Px(16f)), new UiVec2(tag.X + context.Px(8f), UiRowStyle.CapsCenterY(_theme.Heading, tagSize, tag)), tagSize, ink, 0.08f);
            pen += tagWidth + context.Px(8f);
            tagged = true;
        }

        if (tagged)
        {
            y += tagHeight;
        }

        // What the row chooses between, the one it holds marked.
        if (_options.Length == 0)
        {
            return y;
        }

        y += context.Px(22f);
        var heading = new UiRect(x, y, width, eyebrow * 1.2f);
        if (heading.Bottom > Bounds.Bottom - pad)
        {
            return y - context.Px(22f);
        }

        painter?.DrawLabel(_theme.Heading, "OPTIONS", new UiVec2(x, UiRowStyle.CapsCenterY(_theme.Heading, eyebrow, heading)), eyebrow, _theme.TextMuted, 0.14f);
        y = heading.Bottom + context.Px(8f);
        float rowHeight = context.Px(40f);
        float optionSize = context.Px(17f);
        float inset = context.Px(14f);
        for (int i = 0; i < _options.Length; i++)
        {
            if (y + rowHeight > Bounds.Bottom - pad)
            {
                break;
            }

            var row = new UiRect(x, y, width, rowHeight);
            bool current = i == _current;
            if (painter is not null)
            {
                if (current)
                {
                    painter.FillRect(row, UiPalette.AccentAt(0.13f));
                    painter.FillRect(new UiRect(row.X, row.Y, context.Px(3f), row.Height), _theme.Accent);
                    float small = context.Px(13f);
                    float noteWidth = _theme.Mono.MeasureWidth("current", small);
                    painter.DrawText(_theme.Mono, "current", new UiVec2(row.Right - inset - noteWidth, UiRowStyle.CenterY(_theme.Mono, small, row)), small, _theme.TextMuted);
                }
                else
                {
                    painter.FillRect(new UiRect(row.X, row.Bottom - context.Px(1f), row.Width, context.Px(1f)), UiPalette.White(0.07f));
                }

                painter.DrawLabel(_theme.Heading, TextFit.ToWidth(_theme.Heading, _options[i], optionSize, width - (inset * 2f) - context.Px(70f)), new UiVec2(row.X + inset, UiRowStyle.CapsCenterY(_theme.Heading, optionSize, row)), optionSize, current ? _theme.Text : _theme.TextSecondary, 0.1f);
            }

            y += rowHeight + context.Px(2f);
        }

        return y - context.Px(2f);
    }
}
