namespace OpenIGI.UI.Text;

/// <summary>Shortens text with an ellipsis so it fits the room it is given.</summary>
/// <remarks>
/// Every caption, hint and value that can meet a narrow window goes through here, so a smaller window shows
/// "A desktop notification when a…" rather than text running under the control beside it.
/// </remarks>
public static class TextFit
{
    private const string Ellipsis = "…";

    /// <summary>Shortens text set in a font until it fits a width, ending it with an ellipsis when cut.</summary>
    /// <param name="font">The font.</param>
    /// <param name="text">The text.</param>
    /// <param name="sizePx">The text size in pixels.</param>
    /// <param name="maxWidth">The width it must fit, in pixels.</param>
    /// <param name="trackingEm">The letterspacing in ems.</param>
    /// <returns>The text, or the longest start of it that fits with an ellipsis; empty when not even that fits.</returns>
    public static string ToWidth(UiFont font, string text, float sizePx, float maxWidth, float trackingEm = 0f)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);
        if (UiPainter.MeasureLabel(font, text, sizePx, trackingEm) <= maxWidth)
        {
            return text;
        }

        // The longest start that fits with the ellipsis, found by halving: a long hint is measured a dozen
        // times, not once per character.
        int low = 0;
        int high = text.Length - 1;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (UiPainter.MeasureLabel(font, string.Concat(text.AsSpan(0, middle).TrimEnd(), Ellipsis), sizePx, trackingEm) <= maxWidth)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        if (low == 0)
        {
            return UiPainter.MeasureLabel(font, Ellipsis, sizePx, trackingEm) <= maxWidth ? Ellipsis : string.Empty;
        }

        return string.Concat(text.AsSpan(0, low).TrimEnd(), Ellipsis);
    }

    /// <summary>Shortens text in a fixed-width font to a number of columns, ending it with an ellipsis when cut.</summary>
    /// <param name="text">The text.</param>
    /// <param name="columns">The columns available.</param>
    /// <returns>The text, or its first <paramref name="columns"/> - 1 characters and an ellipsis.</returns>
    /// <remarks>
    /// Elided rather than wrapped. A wrapped log line destroys the vertical scan that is the only reason to
    /// read a log at speed, and a wrapped table row stops being a table.
    /// </remarks>
    public static string ToColumns(string text, int columns)
    {
        ArgumentNullException.ThrowIfNull(text);
        return columns <= 1 || text.Length <= columns ? text : string.Concat(text.AsSpan(0, columns - 1), Ellipsis);
    }
}
