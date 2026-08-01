namespace OpenIGI.Core.Configuration;

/// <summary>
/// Where the console reads script files from.
/// </summary>
/// <remarks>
/// Abstracted so that <c>exec</c> can be tested without touching a disk, and so that a future
/// browser or mobile host can back configuration with whatever storage it has.
/// </remarks>
public interface IConsoleFileSystem
{
    /// <summary>Reads a console script.</summary>
    /// <param name="name">The file name, relative to whatever root the implementation uses.</param>
    /// <param name="text">Receives the file's contents.</param>
    /// <returns><see langword="true"/> when the file existed and was read.</returns>
    bool TryReadText(string name, out string text);
}
