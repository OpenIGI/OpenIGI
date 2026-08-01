using System.Diagnostics.CodeAnalysis;

namespace OpenIGI.Core.Configuration;

/// <summary>
/// The set of console commands the running game knows about.
/// </summary>
/// <remarks>
/// Kept separate from <see cref="CVarRegistry"/> because the two have different lifetimes: variables
/// are declared statically and persist, while commands are added by whichever subsystems are alive —
/// the shell registers <c>map</c> only once a game profile is loaded.
/// </remarks>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, ConsoleCommand> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ConsoleCommand> _ordered = [];

    /// <summary>Gets the registered commands, in registration order.</summary>
    public IReadOnlyList<ConsoleCommand> All => _ordered;

    /// <summary>Adds a command.</summary>
    /// <param name="command">The command to add.</param>
    /// <returns><paramref name="command"/>, so registration can be chained.</returns>
    /// <exception cref="ArgumentException">A command with the same name is already registered.</exception>
    public ConsoleCommand Add(ConsoleCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!_byName.TryAdd(command.Name, command))
        {
            throw new ArgumentException($"Console command '{command.Name}' is already registered.", nameof(command));
        }

        _ordered.Add(command);
        return command;
    }

    /// <summary>Adds a command from its parts.</summary>
    /// <param name="name">The name the command is invoked by.</param>
    /// <param name="help">The one-line description.</param>
    /// <param name="execute">What the command does.</param>
    /// <param name="usage">The argument shape. May be empty.</param>
    /// <param name="flags">The command's behavioural attributes.</param>
    /// <returns>The registered command.</returns>
    public ConsoleCommand Add(
        string name,
        string help,
        Action<ConsoleCommandContext> execute,
        string usage = "",
        CommandOptions flags = CommandOptions.None) =>
        Add(new ConsoleCommand(name, help, usage, execute, flags));

    /// <summary>Removes a command.</summary>
    /// <param name="name">The name to remove. Case-insensitive.</param>
    /// <returns><see langword="true"/> when a command was removed.</returns>
    /// <remarks>Subsystems that register commands conditionally remove them again when they shut down.</remarks>
    public bool Remove(string name)
    {
        if (!_byName.Remove(name ?? string.Empty, out ConsoleCommand? command))
        {
            return false;
        }

        _ordered.Remove(command);
        return true;
    }

    /// <summary>Looks a command up by name.</summary>
    /// <param name="name">The name to look up. Case-insensitive.</param>
    /// <param name="command">Receives the command.</param>
    /// <returns><see langword="true"/> when a command with that name is registered.</returns>
    public bool TryGet(string name, [NotNullWhen(true)] out ConsoleCommand? command) =>
        _byName.TryGetValue(name ?? string.Empty, out command);

    /// <summary>Enumerates commands whose name contains <paramref name="fragment"/>, ordered by name.</summary>
    /// <param name="fragment">The substring to match, or an empty string to match everything.</param>
    /// <param name="includeHidden">Whether commands flagged <see cref="CommandOptions.Hidden"/> are included.</param>
    /// <returns>The matching commands.</returns>
    public IEnumerable<ConsoleCommand> Search(string fragment, bool includeHidden = false)
    {
        string needle = fragment ?? string.Empty;

        return _ordered
            .Where(command => includeHidden || (command.Flags & CommandOptions.Hidden) == 0)
            .Where(command => needle.Length == 0 || command.Name.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase);
    }
}
