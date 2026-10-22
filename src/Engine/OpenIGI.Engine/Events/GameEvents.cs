using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Engine.Events;

/// <summary>Marks a record as something that happened in play, which <see cref="GameEvents"/> tells its subscribers of.</summary>
public interface IGameEvent;

/// <summary>Handles one kind of game event.</summary>
/// <typeparam name="TEvent">The kind.</typeparam>
/// <param name="gameEvent">What happened.</param>
public delegate void GameEventListener<TEvent>(in TEvent gameEvent)
    where TEvent : struct, IGameEvent;

/// <summary>What happened in play, told to whoever asked to hear it: mods, and the game's own tools.</summary>
/// <remarks>
/// <para>
/// A session has one, published among its services. The games raise their events once a tick, after the level has run,
/// from what changed during it; nothing in the simulation waits on a subscriber or reads what one does, so a game played
/// with subscribers plays as one without.
/// </para>
/// <para>
/// Subscribers are told in the order they subscribed. One that throws is logged and the rest are still told.
/// </para>
/// </remarks>
/// <param name="logger">Where a subscriber's failure is reported.</param>
public sealed class GameEvents(IOpenIgiLogger? logger = null)
{
    private const string LogCategory = "Events";

    private readonly Dictionary<Type, Delegate[]> _handlers = [];
    private readonly IOpenIgiLogger _logger = logger ?? NullLogger.Instance;

    /// <summary>Asks to be told of one kind of event.</summary>
    /// <typeparam name="TEvent">The kind.</typeparam>
    /// <param name="handler">What is told.</param>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is <see langword="null"/>.</exception>
    public void Subscribe<TEvent>(GameEventListener<TEvent> handler)
        where TEvent : struct, IGameEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[typeof(TEvent)] = _handlers.TryGetValue(typeof(TEvent), out Delegate[]? existing) ? [.. existing, handler] : [handler];
    }

    /// <summary>Gets whether anything asked to be told of one kind of event, so a game can skip working one out.</summary>
    /// <typeparam name="TEvent">The kind.</typeparam>
    /// <returns>Whether anything did.</returns>
    public bool IsHeard<TEvent>()
        where TEvent : struct, IGameEvent => _handlers.ContainsKey(typeof(TEvent));

    /// <summary>Tells every subscriber of an event's kind that it happened.</summary>
    /// <typeparam name="TEvent">The kind.</typeparam>
    /// <param name="gameEvent">What happened.</param>
    public void Raise<TEvent>(in TEvent gameEvent)
        where TEvent : struct, IGameEvent
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out Delegate[]? handlers))
        {
            return;
        }

        foreach (Delegate handler in handlers)
        {
            try
            {
                ((GameEventListener<TEvent>)handler)(in gameEvent);
            }
#pragma warning disable CA1031 // A subscriber's failure is its own; the game and the other subscribers go on.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.Error(LogCategory, $"A handler of {typeof(TEvent).Name} failed: {ex.Message}");
            }
        }
    }
}
