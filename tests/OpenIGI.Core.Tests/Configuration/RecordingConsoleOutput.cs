using OpenIGI.Core.Configuration;

namespace OpenIGI.Core.Tests.Configuration;

/// <summary>
/// Collects console output so a test can assert on what the console said.
/// </summary>
internal sealed class RecordingConsoleOutput : IConsoleOutput
{
    private readonly List<ConsoleLine> _lines = [];

    /// <summary>Gets every line written, oldest first.</summary>
    public IReadOnlyList<ConsoleLine> Lines => _lines;

    /// <summary>Gets the text of every line written, oldest first.</summary>
    public IEnumerable<string> Text => _lines.Select(line => line.Text);

    /// <summary>Gets the text of every line written at <see cref="ConsoleSeverity.Error"/>.</summary>
    public IEnumerable<string> Errors =>
        _lines.Where(line => line.Severity == ConsoleSeverity.Error).Select(line => line.Text);

    /// <inheritdoc/>
    public void Write(ConsoleLine line) => _lines.Add(line);

    /// <summary>Determines whether any line contains <paramref name="fragment"/>.</summary>
    /// <param name="fragment">The substring to look for.</param>
    /// <returns><see langword="true"/> when a line contains it.</returns>
    public bool Contains(string fragment) =>
        _lines.Exists(line => line.Text.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    /// <summary>Forgets everything written so far.</summary>
    public void Clear() => _lines.Clear();
}
