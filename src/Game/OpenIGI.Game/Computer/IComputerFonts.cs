namespace OpenIGI.Game.Computer;

/// <summary>How a computer measures its text: the fonts the presentation loaded, in both games.</summary>
public interface IComputerFonts
{
    /// <summary>The width of a string in a font, in the screen's pixels: each character's advance added up.</summary>
    /// <param name="font">The font's file name.</param>
    /// <param name="text">The string.</param>
    /// <returns>Its width.</returns>
    int StringWidth(string font, string text);

    /// <summary>The height of a line of a font.</summary>
    /// <param name="font">The font's file name.</param>
    /// <returns>Its height.</returns>
    int LineHeight(string font);
}

/// <summary>The measure a computer uses before its presentation has loaded the fonts: eight pixels a letter, sixteen a line.</summary>
public sealed class EstimatedComputerFonts : IComputerFonts
{
    /// <inheritdoc/>
    public int StringWidth(string font, string text) => (text ?? string.Empty).Length * 8;

    /// <inheritdoc/>
    public int LineHeight(string font) => 16;
}
