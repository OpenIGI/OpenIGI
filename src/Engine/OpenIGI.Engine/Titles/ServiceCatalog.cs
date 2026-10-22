using System.Diagnostics.CodeAnalysis;

namespace OpenIGI.Engine.Titles;

/// <summary>Receives the tables a content module builds, so the rest of the title can find them.</summary>
/// <remarks>
/// <b>Not a dependency-injection container.</b> Nothing is constructed here, nothing is discovered by
/// reflection and nothing is scoped. It exists so a module can hand the session a table it built without
/// the composition root having to know which modules a given title ships — and it is a dictionary keyed
/// by <see cref="Type"/>, which is AOT-safe on the targets a container's activation would not be.
/// </remarks>
public interface IServiceCollector
{
    /// <summary>Publishes a table under its own type.</summary>
    /// <typeparam name="T">The type callers will ask for it by.</typeparam>
    /// <param name="service">The table.</param>
    /// <exception cref="ArgumentNullException"><paramref name="service"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Something is already published under that type.</exception>
    void Provide<T>(T service)
        where T : class;

    /// <summary>Publishes a table, or hands back one a previous session already built.</summary>
    /// <typeparam name="T">The type callers will ask for it by.</typeparam>
    /// <param name="factory">Builds the table when there is not one already.</param>
    /// <returns>The table, new or inherited.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// For a session that runs beside another rather than instead of it. The mission-preview loader
    /// builds a level in its own loop, with its own script namespace, but must read the <em>same</em>
    /// tables the playing session loaded — a second, empty magic-object table would silently give the
    /// preview a level with no attachments. Seed a session with
    /// <see cref="TitleSession.Start"/>'s inherited catalogue and modules reuse rather than rebuild.
    /// </remarks>
    T GetOrProvide<T>(Func<T> factory)
        where T : class;
}

/// <summary>Reads back what a title's content modules published.</summary>
public interface IServiceCatalog
{
    /// <summary>Looks up a published table.</summary>
    /// <typeparam name="T">The type it was published under.</typeparam>
    /// <param name="service">Receives the table when one is published.</param>
    /// <returns><see langword="false"/> when no module published one.</returns>
    bool TryGet<T>([NotNullWhen(true)] out T? service)
        where T : class;

    /// <summary>Looks up a published table, failing when it is absent.</summary>
    /// <typeparam name="T">The type it was published under.</typeparam>
    /// <returns>The table.</returns>
    /// <exception cref="InvalidOperationException">No module published one.</exception>
    T Require<T>()
        where T : class;
}

/// <summary>The tables a title's content modules published.</summary>
/// <remarks>
/// One object implements both halves. A module writing during registration and a caller reading during
/// play are the same store seen from two sides, and splitting the interfaces is about saying which side
/// a given piece of code is on rather than about keeping two things in step.
/// </remarks>
public sealed class ServiceCatalog : IServiceCollector, IServiceCatalog
{
    private readonly Dictionary<Type, object> _services;

    /// <summary>Initializes an empty catalogue.</summary>
    public ServiceCatalog() => _services = [];

    /// <summary>Initializes a catalogue holding everything another one holds.</summary>
    /// <param name="inherited">The catalogue to copy; its tables are shared, not cloned.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inherited"/> is <see langword="null"/>.</exception>
    public ServiceCatalog(ServiceCatalog inherited)
    {
        ArgumentNullException.ThrowIfNull(inherited);
        _services = new Dictionary<Type, object>(inherited._services);
    }

    /// <inheritdoc/>
    public void Provide<T>(T service)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(service);

        if (!_services.TryAdd(typeof(T), service))
        {
            throw new InvalidOperationException(
                $"A {typeof(T).Name} is already published; two content modules cannot both own one.");
        }
    }

    /// <summary>Publishes a service of the session's own, in place of one it inherited.</summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <param name="service">The service.</param>
    internal void Own<T>(T service)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(service);
        _services[typeof(T)] = service;
    }

    /// <inheritdoc/>
    public T GetOrProvide<T>(Func<T> factory)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(factory);

        if (_services.TryGetValue(typeof(T), out object? found))
        {
            return (T)found;
        }

        T created = factory();
        ArgumentNullException.ThrowIfNull(created);
        _services[typeof(T)] = created;
        return created;
    }

    /// <inheritdoc/>
    public bool TryGet<T>([NotNullWhen(true)] out T? service)
        where T : class
    {
        if (_services.TryGetValue(typeof(T), out object? found))
        {
            service = (T)found;
            return true;
        }

        service = null;
        return false;
    }

    /// <inheritdoc/>
    public T Require<T>()
        where T : class =>
        TryGet(out T? service)
            ? service
            : throw new InvalidOperationException(
                $"No content module published a {typeof(T).Name}. Is its module missing from the title?");
}
