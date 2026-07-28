using OpenIGI.Engine.Resources;

namespace OpenIGI.Game.Menus;

/// <summary>
/// Resolves a menu item's <c>Text Resource</c> key to the localised string it names.
/// </summary>
/// <remarks>
/// <para>
/// Transcribed from <c>Menu_GetString</c> (<c>0x418EA0</c>). A key is not a label: <c>MenuText</c>
/// carries <c>"Single Player"</c>, and the text is fetched from the language archive under
/// <c>LOCAL:&lt;languageDirectory&gt;/&lt;key&gt;</c>, where the directory is the <em>language name</em>.
/// The retail archive is <c>language/&lt;lang&gt;/menusystem.res</c>, whose <c>NAME</c>/<c>CSTR</c>
/// pairs are registered as <c>LOCAL:english/Single Player</c> and so on, so the resource registry
/// resolves them once the archive is registered.
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
    /// <summary>The language the retail English build resolves keys under.</summary>
    /// <remarks>
    /// This is a <em>language name</em>, not a path. <c>menusystem/english.res</c> also exists and
    /// registers a similar-looking set under <c>LOCAL:menusystem/language/…</c>, but it is a smaller,
    /// stale set — it is missing <c>Select Player</c>, among others — and is not what the menu reads.
    /// </remarks>
    public const string DefaultLanguageDirectory = "english";

    /// <summary>The archive holding the menu's strings for a language.</summary>
    /// <param name="language">The language name, for example <c>english</c>.</param>
    /// <returns>The resource name of that language's menu archive.</returns>
    public static string GetArchiveName(string language = DefaultLanguageDirectory) =>
        $"LOCAL:language/{language}/menusystem.res";

    /// <summary>The language constants a menu script may pass to <c>MenuManager_SetLanguage</c>.</summary>
    /// <remarks>
    /// The original registers these six as integer constants 0–5, from a table of 16-byte entries at
    /// <c>0x53331C</c>. <c>USA</c> is a sixth <em>language</em>, not a region flag: it selects a
    /// separate string set from British English.
    /// </remarks>
    public static IReadOnlyList<string> LanguageNames { get; } =
        ["english", "german", "french", "italian", "spanish", "usa"];

    /// <summary>Maps a language constant to its directory name.</summary>
    /// <param name="language">The constant, as <c>MenuManager_SetLanguage</c> receives it.</param>
    /// <returns>The directory name, or <see cref="DefaultLanguageDirectory"/> for an unknown value.</returns>
    public static string LanguageNameOf(int language) =>
        language >= 0 && language < LanguageNames.Count ? LanguageNames[language] : DefaultLanguageDirectory;

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
