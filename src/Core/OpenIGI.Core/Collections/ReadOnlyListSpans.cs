using System.Runtime.InteropServices;

namespace OpenIGI.Core.Collections;

/// <summary>A read-only list of structs read as a span, in place, where it is an array or a list underneath.</summary>
/// <remarks>
/// <para>
/// A struct read through <see cref="IReadOnlyList{T}"/> is an interface call per element, and in the browser build one the
/// ahead-of-time compiler leaves to its interpreter: a loop over every vertex, bone or tile doing that ran several times slower than
/// the same loop over a span. A type that keeps its data in an array or a reused list, and hands it out as a list, gives its hot
/// readers this instead.
/// </para>
/// <para>
/// Anything else is copied once into an array, so the answer is right whatever the list is; the point is that the lists that matter
/// are arrays and lists.
/// </para>
/// </remarks>
public static class ReadOnlyListSpans
{
    /// <summary>Gets a list's elements as a span.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="list">The list.</param>
    /// <returns>The elements, read in place for an array or a <see cref="List{T}"/>.</returns>
    public static ReadOnlySpan<T> AsReadOnlySpan<T>(this IReadOnlyList<T> list)
    {
        ArgumentNullException.ThrowIfNull(list);
        return list switch
        {
            T[] array => array,
            List<T> listed => CollectionsMarshal.AsSpan(listed),
            _ => list.ToArray(),
        };
    }
}
