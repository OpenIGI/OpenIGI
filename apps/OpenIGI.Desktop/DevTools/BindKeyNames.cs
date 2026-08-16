using OpenIGI.Input;

namespace OpenIGI.Desktop;

/// <summary>
/// Translates between <see cref="KeyCode"/> and the names <c>bind</c> uses.
/// </summary>
/// <remarks>
/// <para>
/// The names have to be stable and readable, because they are written into <c>settings.cfg</c> and
/// typed by hand. Most of them are just the enumeration member in lower case, which keeps the table
/// from drifting as keys are added; the exceptions are the ones whose member name is an implementation
/// detail rather than what is printed on the key — <c>D1</c> is the <c>1</c> key, and nobody would
/// guess to write <c>bind d1</c>.
/// </para>
/// <para>
/// Parsing accepts either spelling, so a configuration file written by hand in the obvious way works
/// and one written by the game round-trips exactly.
/// </para>
/// </remarks>
public static class BindKeyNames
{
    private static readonly Dictionary<KeyCode, string> Special = new()
    {
        [KeyCode.D0] = "0",
        [KeyCode.D1] = "1",
        [KeyCode.D2] = "2",
        [KeyCode.D3] = "3",
        [KeyCode.D4] = "4",
        [KeyCode.D5] = "5",
        [KeyCode.D6] = "6",
        [KeyCode.D7] = "7",
        [KeyCode.D8] = "8",
        [KeyCode.D9] = "9",
    };

    private static readonly Dictionary<string, KeyCode> ByName = BuildReverse();

    /// <summary>Returns the bind name for a key.</summary>
    /// <param name="key">The key to name.</param>
    /// <returns>The name, or an empty string for <see cref="KeyCode.None"/>.</returns>
    public static string NameOf(KeyCode key)
    {
        if (key == KeyCode.None)
        {
            return string.Empty;
        }

        return Special.TryGetValue(key, out string? special)
            ? special
            : (Enum.GetName(key)?.ToLowerInvariant() ?? string.Empty);
    }

    /// <summary>Resolves a bind name back to a key.</summary>
    /// <param name="name">The name, in either spelling and any case.</param>
    /// <param name="key">Receives the key.</param>
    /// <returns><see langword="true"/> when the name is known.</returns>
    public static bool TryParse(string? name, out KeyCode key)
    {
        key = KeyCode.None;
        string trimmed = (name ?? string.Empty).Trim();

        return trimmed.Length != 0 &&
            ByName.TryGetValue(trimmed, out key);
    }

    private static Dictionary<string, KeyCode> BuildReverse()
    {
        Dictionary<string, KeyCode> map = new(StringComparer.OrdinalIgnoreCase);

        foreach (KeyCode key in Enum.GetValues<KeyCode>())
        {
            if (key == KeyCode.None)
            {
                continue;
            }

            // The member name is always accepted, so `bind d1` keeps working alongside `bind 1`.
            if (Enum.GetName(key) is { } member)
            {
                map.TryAdd(member, key);
            }

            map.TryAdd(NameOf(key), key);
        }

        return map;
    }
}
