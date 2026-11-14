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
    public void Show(string title, string body, IReadOnlyList<(string Text, Bgra32 Ink)> tags)
    {
        string upper = (title ?? string.Empty).ToUpperInvariant();
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
        LayoutContext context = painter.Context;
        painter.FillRect(Bounds, Bgra32.FromRgba(12, 14, 15, 199));
        painter.StrokeRect(Bounds, context.Px(1f), UiPalette.White(0.12f));

        float pad = context.Px(28f);
        float x = Bounds.X + pad;
        float width = Bounds.Width - (pad * 2f);
        float y = Bounds.Y + pad;

        float eyebrow = context.Px(14f);
        var line = new UiRect(x, y, width, eyebrow * 1.2f);
        painter.DrawLabel(_theme.Heading, "ABOUT THIS SETTING", new UiVec2(x, UiRowStyle.CapsCenterY(_theme.Heading, eyebrow, line)), eyebrow, _theme.TextMuted, 0.14f);
        y = line.Bottom + context.Px(14f);
        if (_title.Length == 0)
        {
            float note = context.Px(17f);
            painter.DrawText(_theme.Body, "Select a setting to see what it does.", new UiVec2(x, y), note, _theme.TextSecondary);
            return;
        }

        // The name, set smaller where it is too long for the panel.
        float titleSize = context.Px(34f);
        float natural = UiPainter.MeasureLabel(_theme.Display, _title, titleSize, 0.03f);
        if (natural > width && natural > 0f)
        {
            titleSize *= width / natural;
        }

        var titleBox = new UiRect(x, y, width, context.Px(34f) * 0.74f);
        painter.DrawLabel(_theme.Display, _title, new UiVec2(x, UiRowStyle.CapsCenterY(_theme.Display, titleSize, titleBox)), titleSize, _theme.Text, 0.03f);
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

                painter.DrawText(_theme.Body, paragraph, new UiVec2(x, y), bodySize, _theme.Text);
                y += step;
            }

            y += context.Px(14f) - (step - _theme.Body.LineHeight(bodySize));
        }

        // The tags, wrapping onto a second line where the panel is narrow.
        float tagSize = context.Px(13f);
        float tagHeight = context.Px(24f);
        float pen = x;
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
            painter.FillRect(tag, UiPalette.InkAt(0.35f));
            painter.StrokeRect(tag, context.Px(1f), ink);
            painter.DrawLabel(_theme.Heading, TextFit.ToWidth(_theme.Heading, text, tagSize, width - context.Px(16f)), new UiVec2(tag.X + context.Px(8f), UiRowStyle.CapsCenterY(_theme.Heading, tagSize, tag)), tagSize, ink, 0.08f);
            pen += tagWidth + context.Px(8f);
        }
    }
}
