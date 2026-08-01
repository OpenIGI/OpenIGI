namespace OpenIGI.Core.Configuration;

/// <summary>
/// One console command: a name, what it does, and how to explain it.
/// </summary>
/// <param name="Name">The name the command is invoked by. Case-insensitive.</param>
/// <param name="Help">The one-line description shown by <c>help</c> and by completion.</param>
/// <param name="Usage">The argument shape, for example <c>"&lt;key&gt; &lt;command&gt;"</c>. May be empty.</param>
/// <param name="Execute">What the command does.</param>
/// <param name="Flags">The command's behavioural attributes.</param>
public sealed record ConsoleCommand(
    string Name,
    string Help,
    string Usage,
    Action<ConsoleCommandContext> Execute,
    CommandOptions Flags = CommandOptions.None)
{
    /// <summary>Gets the command's name followed by its usage, as completion shows it.</summary>
    public string Signature => Usage.Length == 0 ? Name : $"{Name} {Usage}";
}
