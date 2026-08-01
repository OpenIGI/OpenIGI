namespace OpenIGI.Core.Configuration;

/// <summary>
/// One line of console output.
/// </summary>
/// <param name="Text">The text, without a trailing newline.</param>
/// <param name="Severity">How the line should be presented.</param>
public readonly record struct ConsoleLine(string Text, ConsoleSeverity Severity = ConsoleSeverity.Output)
{
    /// <summary>Returns the line's text.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => Text;
}
