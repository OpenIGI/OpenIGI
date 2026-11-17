namespace OpenIGI.Game.Igi2.Flow;

/// <summary>
/// How a status message's text is broken into the lines its box shows, <c>StatusMessageScreen_FormatMessageText</c>
/// (statusmessagescreen.c): wrapped to a width, at most nine lines made, the last three shown.
/// </summary>
public static class Igi2StatusMessageLayout
{
    /// <summary>The lines a message shows at most, its box's three texts.</summary>
    public const int ShownLines = 3;

    /// <summary>The lines a message is broken into at most, its ten line buffers less the one the last is left in.</summary>
    public const int MaxLines = 9;

    /// <summary>How much of the display's width a cutscene's line takes, <c>0.96875</c>.</summary>
    public const float CutsceneWidthShare = 0.96875f;

    /// <summary>The string a box's width is forty of, <c>"A"</c> (<c>StatusMessageScreen_RegisterMessage</c>).</summary>
    public const string BoxWidthString = "A";

    /// <summary>How many of <see cref="BoxWidthString"/> a box is wide.</summary>
    public const int BoxWidthCount = 40;

    /// <summary>What a box's text width is short of its width, <c>6</c> (<c>StatusMessageScreen_UpdateMessage</c>).</summary>
    public const float BoxMargin = 6f;

    /// <summary>Breaks text into the lines its box shows.</summary>
    /// <param name="text">The text printed so far.</param>
    /// <param name="width">The width of a run of text in the message's font.</param>
    /// <param name="maxWidth">The width a line may reach.</param>
    /// <returns>The last <see cref="ShownLines"/> lines at most.</returns>
    /// <remarks>
    /// A line ends where the text up to the character before reaches the width, or at a line break: at the last space within it,
    /// which is dropped, or else before that character. What is left after the ninth line is not shown.
    /// </remarks>
    public static IReadOnlyList<string> Wrap(string text, Func<string, float> width, float maxWidth)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(width);
        List<string> lines = [];
        int start = 0;
        int space = -1;
        int at = 0;
        for (; at < text.Length && lines.Count < MaxLines; at++)
        {
            char c = text[at];
            if (width(text[start..at]) >= maxWidth || c == '\n')
            {
                int end = space >= start ? space : Math.Max(start, at - 1);
                lines.Add(Line(text[start..end]));
                start = Math.Min(end + 1, text.Length);
                space = -1;
            }

            if (c == ' ')
            {
                space = at;
            }
        }

        if (lines.Count < MaxLines && start < text.Length)
        {
            lines.Add(Line(text[start..]));
        }

        return lines.Count <= ShownLines ? lines : lines[^ShownLines..];
    }

    // A line as a text draws it: a break the wrap kept in it is no glyph.
    private static string Line(string text) => text.Replace("\n", string.Empty, StringComparison.Ordinal);
}
