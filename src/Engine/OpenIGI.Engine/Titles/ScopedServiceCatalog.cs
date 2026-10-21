using System.Diagnostics.CodeAnalysis;

namespace OpenIGI.Engine.Titles;

/// <summary>Services for a narrower lifetime than a session's -- one level's -- that read through to the session's.</summary>
/// <remarks>
/// What is provided here lives as long as this catalogue: a level's worlds and tables go when the level does, and never reach the
/// next level or the session. A lookup finds this catalogue's own first, then its parent's.
/// </remarks>
/// <param name="parent">The wider catalogue lookups fall back to.</param>
public sealed class ScopedServiceCatalog(IServiceCatalog parent) : IServiceCollector, IServiceCatalog
{
    private readonly IServiceCatalog _parent = parent ?? throw new ArgumentNullException(nameof(parent));
    private readonly Dictionary<Type, object> _services = [];

    /// <inheritdoc/>
    /// <remarks>A service this scope provides stands in front of the parent's of the same type.</remarks>
    public void Provide<T>(T service)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(service);
        if (!_services.TryAdd(typeof(T), service))
        {
            throw new InvalidOperationException($"A {typeof(T).Name} is already provided for this scope.");
        }
    }

    /// <inheritdoc/>
    public T GetOrProvide<T>(Func<T> factory)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (TryGet(out T? found))
        {
            return found;
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
        if (_services.TryGetValue(typeof(T), out object? own))
        {
            service = (T)own;
            return true;
        }

        return _parent.TryGet(out service);
    }

    /// <inheritdoc/>
    public T Require<T>()
        where T : class =>
        TryGet(out T? service)
            ? service
            : throw new InvalidOperationException($"No {typeof(T).Name} is provided for this scope or its parent.");
}
