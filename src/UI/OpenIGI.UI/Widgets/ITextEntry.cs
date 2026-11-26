namespace OpenIGI.UI.Widgets;

/// <summary>A control that takes typed characters while it has the focus.</summary>
/// <remarks>
/// The screen feeds it the platform's decoded characters and Backspace; the control decides what it keeps. A host reads
/// <see cref="IsTakingText"/> to raise a touch screen's keyboard.
/// </remarks>
public interface ITextEntry
{
    /// <summary>Gets a value indicating whether the control wants typed characters now.</summary>
    bool IsTakingText { get; }

    /// <summary>Gets a value indicating whether, while it takes text, the control holds the whole keyboard and pointer.</summary>
    /// <remarks>
    /// A field that is always open -- a name box -- is not: the page's keys still work around it. A number being typed into
    /// a slider is: Enter keeps it, Escape drops it, a press anywhere keeps it and then lands as it would have, and nothing
    /// else on the page hears a key until the entry is over.
    /// </remarks>
    bool IsModal { get; }

    /// <summary>Takes one typed character.</summary>
    /// <param name="character">The character the platform decoded.</param>
    /// <returns><see langword="true"/> when the text changed.</returns>
    bool Type(char character);

    /// <summary>Deletes the character before the caret.</summary>
    /// <returns><see langword="true"/> when the text changed.</returns>
    bool Backspace();

    /// <summary>Ends a modal entry.</summary>
    /// <param name="commit"><see langword="true"/> to keep what was typed, <see langword="false"/> to drop it.</param>
    void EndEntry(bool commit);
}
