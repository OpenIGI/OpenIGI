using System.Collections.Concurrent;
using System.Reflection;

namespace OpenIGI.Core.Simulation;

/// <summary>
/// Which objects a <see cref="StateCheckpoint"/> records: the simulation's own, and the collections and arrays they hold. Everything
/// else -- services, resources, renderers, devices -- is outside it: a reference to one is kept as it is and never walked.
/// </summary>
/// <param name="isState">Whether objects of a type the rules below leave open are the simulation's.</param>
/// <remarks>
/// Always outside: strings, primitives and enums (they are values), reflection and threading objects, anything marked
/// <see cref="NotSimulationStateAttribute"/>. Always inside: arrays reached from the state, and the BCL's own collections
/// (<c>System.Collections.Generic</c> and the like, not the concurrent ones), which are plain objects holding the state's
/// elements. Delegates are neither: never copied (they cannot change), but the objects they are bound to are walked.
/// </remarks>
public sealed class StateBoundary(Func<Type, bool> isState)
{
    private readonly ConcurrentDictionary<Type, bool> _known = new();

    /// <summary>Gets whether objects of a type are recorded.</summary>
    /// <param name="type">The object's runtime type.</param>
    /// <returns><see langword="true"/> for the simulation's.</returns>
    public bool IsState(Type type) => _known.GetOrAdd(type, Decide, isState);

    private static bool Decide(Type type, Func<Type, bool> isState)
    {
        if (type.IsArray)
        {
            return true;
        }

        if (type.IsPrimitive || type.IsEnum || type.IsPointer || type == typeof(string) || type.IsValueType
            || typeof(Delegate).IsAssignableFrom(type) || typeof(MemberInfo).IsAssignableFrom(type)
            || type.IsDefined(typeof(NotSimulationStateAttribute), inherit: true))
        {
            return false;
        }

        string space = type.Namespace ?? string.Empty;
        if (space.StartsWith("System.Collections", StringComparison.Ordinal))
        {
            return !space.StartsWith("System.Collections.Concurrent", StringComparison.Ordinal);
        }

        return !space.StartsWith("System", StringComparison.Ordinal) && isState(type);
    }
}
