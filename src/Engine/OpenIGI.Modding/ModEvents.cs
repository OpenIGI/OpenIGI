using OpenIGI.Engine.Events;

namespace OpenIGI.Modding;

/// <summary>The game's events, for a mod to be told of: one of <c>OpenIGI.Game.Events</c>, as each happens in play.</summary>
/// <remarks>
/// A handler is told inside the tick, in the order the mods subscribed, and is told nothing the game would not have done
/// without it: an event no mod hears costs the game nothing, and one a mod hears changes nothing of play by being heard.
/// </remarks>
public sealed class ModEvents
{
    private readonly Func<ModContentModule> _module;

    internal ModEvents(Func<ModContentModule> module) => _module = module;

    /// <summary>Asks to be told of one kind of event in every level the game plays.</summary>
    /// <typeparam name="TEvent">The kind.</typeparam>
    /// <param name="handler">What is told.</param>
    /// <returns>This group.</returns>
    public ModEvents On<TEvent>(Action<TEvent> handler)
        where TEvent : struct, IGameEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        _module().Registrations.Add(context =>
            context.Services.GetOrProvide(() => new GameEvents(context.Logger)).Subscribe((in TEvent gameEvent) => handler(gameEvent)));
        return this;
    }
}
