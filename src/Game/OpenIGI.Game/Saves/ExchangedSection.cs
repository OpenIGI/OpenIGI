namespace OpenIGI.Game.Saves;

/// <summary>
/// A statement of a title's own in a save, written and read through one exchange (<see cref="StateExchange"/>): state that
/// belongs to the level as a whole rather than to one of its tasks.
/// </summary>
/// <param name="callName">The name the statement calls.</param>
/// <param name="exchange">Passes the state's fields through, in an order that never changes.</param>
/// <param name="done">What is done once the whole save is read, or <see langword="null"/>.</param>
public sealed class ExchangedSection(string callName, Action<StateExchange> exchange, Action? done = null) : ISaveSection
{
    private readonly Action<StateExchange> _exchange = exchange ?? throw new ArgumentNullException(nameof(exchange));

    /// <inheritdoc/>
    public string CallName { get; } = callName ?? throw new ArgumentNullException(nameof(callName));

    /// <inheritdoc/>
    public string Write()
    {
        StateWriter writer = new();
        _exchange(StateExchange.Writing(writer));
        string arguments = writer.Arguments;
        return $"{CallName}({(arguments.Length > 2 ? arguments[2..] : string.Empty)});\r\n";
    }

    /// <inheritdoc/>
    public void Read(ISaveArguments arguments) => _exchange(StateExchange.Reading(new StateReader(arguments, 0)));

    /// <inheritdoc/>
    public void LoadDone() => done?.Invoke();
}
