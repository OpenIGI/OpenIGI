namespace OpenIGI.Game.Computer;

/// <summary>A word of computer text, where its line put it.</summary>
/// <param name="Text">The word, with the space after it.</param>
/// <param name="X">Its left, in the screen's pixels.</param>
/// <param name="Y">Its top.</param>
public readonly record struct ComputerWord(string Text, int X, int Y);

/// <summary>
/// How both games set a computer's text over lines (Project I.G.I.'s <c>0x469450</c>, I.G.I. 2's
/// <c>ComputerObjectives_Print</c>): word by word, each word carrying the space after it, and a word that does not fit on what
/// is left of the line starting the next.
/// </summary>
public static class ComputerTextWrap
{
    /// <summary>Gets the measure used before a presentation has loaded its fonts.</summary>
    public static IComputerFonts Estimated { get; } = new EstimatedComputerFonts();

    /// <summary>Sets a text over lines.</summary>
    /// <param name="fonts">How text is measured.</param>
    /// <param name="x">The left of every line.</param>
    /// <param name="y">The top of the first.</param>
    /// <param name="text">The text.</param>
    /// <param name="font">Its font.</param>
    /// <param name="lineWidth">How wide a line may run.</param>
    /// <param name="words">Where its words are put, in order.</param>
    /// <returns>The Y under its last line.</returns>
    /// <remarks>
    /// A word only as wide as what is left still moves down, and the first word of all does when it is the whole width: the
    /// test is that the word is narrower than what is left.
    /// </remarks>
    public static int Wrap(IComputerFonts fonts, int x, int y, string text, string font, int lineWidth, ICollection<ComputerWord> words)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(words);
        int lineHeight = fonts.LineHeight(font);
        int printX = x, left = lineWidth, index = 0;
        while (true)
        {
            int space = text.IndexOf(' ', index);
            bool complete = space < 0;
            string word = complete ? text[index..] : text[index..(space + 1)];
            index = complete ? text.Length : space + 1;
            int width = fonts.StringWidth(font, word);
            if (width >= left)
            {
                printX = x;
                y += lineHeight;
                left = lineWidth;
            }

            left -= width;
            words.Add(new ComputerWord(word, printX, y));
            printX += width;
            if (complete)
            {
                return y + lineHeight;
            }
        }
    }
}
