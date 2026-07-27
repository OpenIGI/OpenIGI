using OpenIGI.Engine.Resources;

namespace OpenIGI.Game.Menus;

/// <summary>
/// Resolves a menu item's <c>Text Resource</c> key to the localised string it names.
/// </summary>
/// <remarks>
/// <para>
/// Transcribed from <c>Menu_GetString</c> (<c>0x418EA0</c>). A key is not a label: <c>MenuText</c>
/// carries <c>"Single Player"</c>, and the text is fetched from the language archive under
/// <c>LOCAL:&lt;languageDirectory&gt;/&lt;key&gt;</c>. The retail archives (<c>english.res</c> and its
/// siblings) are pure <c>NAME</c>/<c>CSTR</c> pairs registered under exactly those names, so the
/// resource registry resolves them once the archive is registered.
/// </para>
/// <para>
/// <b>A missing key is not an error.</b> The original formats <c>"RM: &lt;key&gt;"</c> — resource
/// missing — caches it, and draws that on screen. Translators saw the untranslated entries in place
/// rather than a blank menu, and reproducing it keeps a partial language pack usable instead of
/// silently empty.
/// </para>
/// </remarks>
public sealed class MenuStringTable
{
    /// <summary>The directory the retail menu scripts keep their language archives under.</summary>
    public const string DefaultLanguageDirectory = "menusystem/language";

    private readonly Dictionary<string, string> _missing = new(StringComparer.OrdinalIgnoreCase);
    private readonly ResourceRegistry _resources;

    /// <summary>Initializes a new instance of the <see cref="MenuStringTable"/> class.</summary>
    /// <param name="resources">The registry the language archive was registered into.</param>
    /// <param name="languageDirectory">The directory holding the current language's strings.</param>
    /// <exception cref="ArgumentNullException"><paramref name="resources"/> is <see langword="null"/>.</exception>
    public MenuStringTable(ResourceRegistry resources, string languageDirectory = DefaultLanguageDirectory)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentException.ThrowIfNullOrWhiteSpace(languageDirectory);

        _resources = resources;
        LanguageDirectory = languageDirectory.Replace('\\', '/').Trim('/');
    }

    /// <summary>Gets the directory keys are resolved under.</summary>
    public string LanguageDirectory { get; }

    /// <summary>Gets the number of keys that failed to resolve.</summary>
    public int MissingCount => _missing.Count;

    /// <summary>Gets the keys that failed to resolve, for a translation report.</summary>
    public IReadOnlyCollection<string> MissingKeys => _missing.Keys;

    /// <summary>Builds the resource name a key resolves through.</summary>
    /// <param name="key">The <c>Text Resource</c> key.</param>
    /// <returns>The full resource name.</returns>
    /// <remarks>The original's format string is <c>"LOCAL:%s/%s"</c>.</remarks>
    public string GetResourceName(string key) => $"LOCAL:{LanguageDirectory}/{key}";

    /// <summary>Resolves a key.</summary>
    /// <param name="key">The <c>Text Resource</c> key. An empty key yields an empty string.</param>
    /// <returns>The localised text, or <c>"RM: &lt;key&gt;"</c> when the key is not in the archive.</returns>
    public string Get(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        if (_resources.TryLoad(GetResourceName(key), out ReadOnlyMemory<byte> data))
        {
            return DecodeContextString(data.Span);
        }

        if (_missing.TryGetValue(key, out string? placeholder))
        {
            return placeholder;
        }

        placeholder = $"RM: {key}";
        _missing[key] = placeholder;
        return placeholder;
    }

    /// <summary>Determines whether a key resolves without falling back.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when the archive holds the key.</returns>
    public bool Contains(string key) =>
        !string.IsNullOrEmpty(key) && _resources.TryLoad(GetResourceName(key), out _);

    /// <summary>Decodes a <c>CSTR</c> payload, which is NUL-terminated single-byte text.</summary>
    private static string DecodeContextString(ReadOnlySpan<byte> data)
    {
        int terminator = data.IndexOf((byte)0);
        ReadOnlySpan<byte> text = terminator < 0 ? data : data[..terminator];
        return System.Text.Encoding.Latin1.GetString(text);
    }
}
