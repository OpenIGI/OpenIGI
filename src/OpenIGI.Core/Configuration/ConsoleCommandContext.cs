namespace OpenIGI.Core.Configuration;

/// <summary>
/// What a command is given when it runs.
/// </summary>
/// <remarks>
/// A command writes through this rather than to a stream, so the same command works in the on-screen
/// console, in a config file being executed at start-up and in a test.
/// </remarks>
/// <param name="console">The console the command is running in.</param>
/// <param name="name">The name the command was invoked under.</param>
/// <param name="arguments">The arguments, excluding the command name.</param>
public sealed class ConsoleCommandContext(ConsoleSystem console, string name, IReadOnlyList<string> arguments)
{
    /// <summary>Gets the console the command is running in.</summary>
    public ConsoleSystem Console { get; } = console;

    /// <summary>Gets the name the command was invoked under.</summary>
    /// <remarks>May differ from the registered name when the command was reached through an alias.</remarks>
    public string Name { get; } = name;

    /// <summary>Gets the arguments, excluding the command name.</summary>
    public IReadOnlyList<string> Arguments { get; } = arguments;

    /// <summary>Gets the number of arguments.</summary>
    public int Count => Arguments.Count;

    /// <summary>Gets the argument at <paramref name="index"/>, or an empty string when there is none.</summary>
    /// <param name="index">The zero-based argument index.</param>
    /// <returns>The argument, or an empty string.</returns>
    public string this[int index] => index >= 0 && index < Arguments.Count ? Arguments[index] : string.Empty;

    /// <summary>Joins the arguments from <paramref name="start"/> onwards with single spaces.</summary>
    /// <param name="start">The zero-based index to join from.</param>
    /// <returns>The joined text, or an empty string when there is nothing to join.</returns>
    /// <remarks>Commands that take a free-text tail, such as <c>echo</c> and <c>alias</c>, use this.</remarks>
    public string Rest(int start) =>
        start >= Arguments.Count ? string.Empty : string.Join(' ', Arguments.Skip(start));

    /// <summary>Writes an ordinary line of output.</summary>
    /// <param name="text">The text to write.</param>
    public void Print(string text) => Console.Write(new ConsoleLine(text));

    /// <summary>Writes a line that is worth reading but does not indicate a failure.</summary>
    /// <param name="text">The text to write.</param>
    public void Notice(string text) => Console.Write(new ConsoleLine(text, ConsoleSeverity.Notice));

    /// <summary>Writes a warning.</summary>
    /// <param name="text">The text to write.</param>
    public void Warn(string text) => Console.Write(new ConsoleLine(text, ConsoleSeverity.Warning));

    /// <summary>Writes an error.</summary>
    /// <param name="text">The text to write.</param>
    public void Error(string text) => Console.Write(new ConsoleLine(text, ConsoleSeverity.Error));
}
