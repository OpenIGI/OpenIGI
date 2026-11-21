using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;

namespace OpenIGI.Presentation.Hud.Remastered.Widgets;

/// <summary>
/// The session overlays' text: the interface's distance-field faces, crisp at any size and tinted true in any colour, set the
/// heads-up display's way -- its condensed heading face for what is named and counted, its body face for what was said.
/// </summary>
internal sealed class OverlayText
{
    // How far apart the heading face's letters stand: a hair, so capitals read as a readout's.
    private const float Tracking = 0.04f;

    private readonly UiFont _face;
    private readonly UiFont _prose;
    private UiPainter? _painter;

    /// <summary>Initializes a new instance of the <see cref="OverlayText"/> class.</summary>
    /// <param name="face">The face names, labels and figures are set in.</param>
    /// <param name="prose">The face running text -- what was said in the chat -- is set in.</param>
    public OverlayText(UiFont face, UiFont prose)
    {
        _face = face;
        _prose = prose;
    }

    /// <summary>Starts a frame's drawing.</summary>
    /// <param name="painter">The painter the text is drawn with.</param>
    public void Begin(UiPainter painter) => _painter = painter;

    /// <summary>Gets the height of a line set at a size.</summary>
    /// <param name="size">The size, in pixels.</param>
    /// <returns>The line's height, in pixels.</returns>
    public float LineHeightAt(float size) => MathF.Ceiling(_face.LineHeight(size));

    /// <summary>A string in capitals.</summary>
    /// <param name="text">The string.</param>
    /// <returns>It in capitals.</returns>
    public static string Capitals(string text) => string.IsNullOrEmpty(text) ? string.Empty : text.ToUpperInvariant();

    /// <summary>Running text as it is set: as written.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The text.</returns>
    public static string Prose(string text) => text;

    /// <summary>Measures a line set at a size.</summary>
    /// <param name="text">The line.</param>
    /// <param name="size">The size, in pixels.</param>
    /// <returns>Its width in pixels.</returns>
    public float MeasureAt(string text, float size) => string.IsNullOrEmpty(text) ? 0f : UiPainter.MeasureLabel(_face, text, size, Tracking);

    /// <summary>Draws a line set at a size with its top left at a point.</summary>
    /// <param name="text">The line.</param>
    /// <param name="x">Its left edge, in pixels.</param>
    /// <param name="y">Its top edge, in pixels.</param>
    /// <param name="size">The size, in pixels.</param>
    /// <param name="colour">Its colour.</param>
    /// <param name="shadow">Whether a dark copy is laid a hair down and right under it, for a line set straight on the picture.</param>
    /// <returns>Its width in pixels.</returns>
    public float DrawAt(string text, float x, float y, float size, Bgra32 colour, bool shadow = false)
    {
        if (string.IsNullOrEmpty(text) || _painter is null)
        {
            return 0f;
        }

        if (shadow)
        {
            float offset = MathF.Max(1f, MathF.Round(size / 14f));
            _painter.DrawLabel(_face, text, new UiVec2(x + offset, y + offset), size, Bgra32.FromRgba(0, 0, 0, (byte)(210 * colour.A / 255)), Tracking);
        }

        _painter.DrawLabel(_face, text, new UiVec2(x, y), size, colour, Tracking);
        return MeasureAt(text, size);
    }

    /// <summary>Draws a line of running text -- what was said -- in the body face, with its top left at a point.</summary>
    /// <param name="text">The line.</param>
    /// <param name="x">Its left edge, in pixels.</param>
    /// <param name="y">Its top edge, in pixels.</param>
    /// <param name="size">The size, in pixels.</param>
    /// <param name="colour">Its colour.</param>
    /// <param name="shadow">Whether it is shadowed, as <see cref="DrawAt"/> says.</param>
    /// <returns>Its width in pixels.</returns>
    public float DrawProseAt(string text, float x, float y, float size, Bgra32 colour, bool shadow = false)
    {
        if (string.IsNullOrEmpty(text) || _painter is null)
        {
            return 0f;
        }

        // Set on the heading face's line, so a said line sits level with the name before it.
        float top = y + ((LineHeightAt(size) - _prose.LineHeight(size)) * 0.5f);
        if (shadow)
        {
            float offset = MathF.Max(1f, MathF.Round(size / 14f));
            _painter.DrawLabel(_prose, text, new UiVec2(x + offset, top + offset), size, Bgra32.FromRgba(0, 0, 0, (byte)(210 * colour.A / 255)), 0f);
        }

        _painter.DrawLabel(_prose, text, new UiVec2(x, top), size, colour, 0f);
        return MeasureProseAt(text, size);
    }

    /// <summary>Measures a line of running text set at a size.</summary>
    /// <param name="text">The line.</param>
    /// <param name="size">The size, in pixels.</param>
    /// <returns>Its width in pixels.</returns>
    public float MeasureProseAt(string text, float size) => string.IsNullOrEmpty(text) ? 0f : _prose.MeasureWidth(text, size);

    /// <summary>Draws a line set at a size, ending at a point.</summary>
    /// <param name="text">The line.</param>
    /// <param name="right">Its right edge, in pixels.</param>
    /// <param name="y">Its top edge, in pixels.</param>
    /// <param name="size">The size, in pixels.</param>
    /// <param name="colour">Its colour.</param>
    /// <param name="shadow">Whether it is shadowed, as <see cref="DrawAt"/> says.</param>
    /// <returns>Its left edge, in pixels.</returns>
    public float DrawRightAt(string text, float right, float y, float size, Bgra32 colour, bool shadow = false)
    {
        float left = right - MeasureAt(text, size);
        DrawAt(text, left, y, size, colour, shadow);
        return left;
    }

    /// <summary>Draws a line set at a size, centred across a point.</summary>
    /// <param name="text">The line.</param>
    /// <param name="centre">Its middle, across, in pixels.</param>
    /// <param name="y">Its top edge, in pixels.</param>
    /// <param name="size">The size, in pixels.</param>
    /// <param name="colour">Its colour.</param>
    /// <param name="shadow">Whether it is shadowed, as <see cref="DrawAt"/> says.</param>
    /// <returns>Its left edge, in pixels.</returns>
    public float DrawCentredAt(string text, float centre, float y, float size, Bgra32 colour, bool shadow = false)
    {
        float left = centre - (MeasureAt(text, size) * 0.5f);
        DrawAt(text, left, y, size, colour, shadow);
        return left;
    }

    /// <summary>A line set at a size cut to a width, an ellipsis where it was cut.</summary>
    /// <param name="text">The line.</param>
    /// <param name="size">The size, in pixels.</param>
    /// <param name="width">The widest it may be, in pixels.</param>
    /// <returns>The line, whole or cut.</returns>
    public string ClipAt(string text, float size, float width)
    {
        if (width <= 0f || MeasureAt(text, size) <= width)
        {
            return text;
        }

        for (int length = text.Length - 1; length > 0; length--)
        {
            string cut = string.Concat(text.AsSpan(0, length).TrimEnd(), "…");
            if (MeasureAt(cut, size) <= width)
            {
                return cut;
            }
        }

        return string.Empty;
    }

    /// <summary>Breaks running text set at a size into rows no wider than a width, the first narrower, a word too long for a row cut.</summary>
    /// <param name="text">The text.</param>
    /// <param name="size">The size, in pixels.</param>
    /// <param name="first">The first row's width, in pixels.</param>
    /// <param name="rest">The later rows' width, in pixels.</param>
    /// <returns>The rows.</returns>
    public List<string> WrapAt(string text, float size, float first, float rest)
    {
        var rows = new List<string>(2);
        int start = 0;
        float room = Math.Max(size, first);
        while (start < text.Length)
        {
            if (_prose.MeasureWidth(text.AsSpan(start), size) <= room)
            {
                rows.Add(text[start..]);
                break;
            }

            // The most that fits, found by halving; then back to the last space within it, if any.
            int low = 1;
            int high = text.Length - start;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                if (_prose.MeasureWidth(text.AsSpan(start, mid), size) <= room)
                {
                    low = mid;
                }
                else
                {
                    high = mid - 1;
                }
            }

            int space = text.LastIndexOf(' ', start + low - 1, low);
            int end = space > start ? space : start + low;
            rows.Add(text[start..end]);
            start = end;
            while (start < text.Length && text[start] == ' ')
            {
                start++;
            }

            room = Math.Max(size, rest);
        }

        if (rows.Count == 0)
        {
            rows.Add(string.Empty);
        }

        return rows;
    }
}
