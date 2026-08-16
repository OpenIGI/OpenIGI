namespace OpenIGI.Core.Configuration;

/// <summary>
/// One line of console output.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Category"/> and <see cref="Timestamp"/> are carried beside the text rather than being
/// formatted into it. Mirrored log output used to arrive as <c>"[Render] built 12 cascades"</c>, which
/// meant the console could neither filter by subsystem nor search the message without matching the
/// category as well — and copying a line pasted the decoration along with it. Keeping them apart lets
/// the view decide whether to show either, and lets a filter match exactly what the reader means.
/// </para>
/// <para>
/// Both are optional and last, so the existing <c>new ConsoleLine(text)</c> and
/// <c>new ConsoleLine(text, severity)</c> call sites are untouched.
/// </para>
/// </remarks>
/// <param name="Text">The text, without a trailing newline.</param>
/// <param name="Severity">How the line should be presented.</param>
/// <param name="Category">The subsystem that produced the line, or an empty string when it came from the console itself.</param>
/// <param name="Timestamp">Seconds since the console started, or zero when the line was not stamped.</param>
public readonly record struct ConsoleLine(
    string Text,
    ConsoleSeverity Severity = ConsoleSeverity.Output,
    string Category = "",
    double Timestamp = 0.0)
{
    /// <summary>Returns the line's text.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => Text;
}
