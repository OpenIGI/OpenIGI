using System.Runtime.InteropServices;

namespace OpenIGI.Core.Collections;

/// <summary>What a dictionary is asked for over and over: the value under a key, made the first time it is asked for.</summary>
public static class DictionaryExtensions
{
    /// <summary>Gets the value under a key, adding a new one the first time the key is asked for.</summary>
    /// <typeparam name="TKey">The key's type.</typeparam>
    /// <typeparam name="TValue">The value's type, made by its parameterless constructor.</typeparam>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="key">The key.</param>
    /// <returns>The value the dictionary holds under the key.</returns>
    public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key)
        where TKey : notnull
        where TValue : new()
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        ref TValue? slot = ref CollectionsMarshal.GetValueRefOrAddDefault(dictionary, key, out bool exists);
        if (!exists)
        {
            slot = new TValue();
        }

        return slot!;
    }

    /// <summary>Gets the value under a key, making it from the key the first time the key is asked for.</summary>
    /// <typeparam name="TKey">The key's type.</typeparam>
    /// <typeparam name="TValue">The value's type.</typeparam>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="key">The key.</param>
    /// <param name="make">Makes the value for a key the dictionary does not hold; it must not change the dictionary.</param>
    /// <returns>The value the dictionary holds under the key.</returns>
    public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, Func<TKey, TValue> make)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(make);
        if (!dictionary.TryGetValue(key, out TValue? value))
        {
            value = make(key);
            dictionary[key] = value;
        }

        return value;
    }
}
