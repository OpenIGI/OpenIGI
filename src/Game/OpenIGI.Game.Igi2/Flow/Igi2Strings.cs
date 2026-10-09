using OpenIGI.Engine.Resources;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>
/// The game's localised text, <c>Config_GetLocalizedStringForLanguage</c> (<c>igi2 0x424BD0</c>): a key is a resource, looked up
/// first as <c>GLOBAL:</c> and then under the language's own folder, and a key nothing holds is its own text.
/// </summary>
/// <remarks>
/// <para>
/// The strings come from the language's archives the screens load: <c>LANGUAGE:messages.res</c>, the status messages'
/// (<c>StatusMessageScreen_LoadResource</c>, <c>0x43DC40</c>), and <c>LANGUAGE:objectives.res</c>, the computer's
/// (<c>ComputerMenu_LoadComputerMenuResource</c>, <c>0x532CD0</c>). Each names its strings <c>LOCAL:english/&lt;key&gt;</c>.
/// </para>
/// <para>
/// Unlike the menus' table, which shows a missing key marked, the game shows it as it is written: retail remembers the
/// key as a missing resource whose text is the key, so <c>"Quicksave done"</c>, which no archive holds, reads as itself.
/// </para>
/// </remarks>
/// <param name="resources">The registry the archives were registered into.</param>
/// <param name="language">The language's name, <c>Config_azGameLanguage</c>'s entry.</param>
public sealed class Igi2Strings(ResourceRegistry resources, string language = Igi2Strings.DefaultLanguage)
{
    /// <summary>The language a retail English game resolves its keys under.</summary>
    public const string DefaultLanguage = "ENGLISH";

    /// <summary>The archives the game's own text is read from, for the default language.</summary>
    public static IReadOnlyList<string> Archives { get; } =
    [
        "LOCAL:language/english/messages.res",
        "LOCAL:language/english/objectives.res",
    ];

    private readonly ResourceRegistry _resources = resources ?? throw new ArgumentNullException(nameof(resources));

    /// <summary>Gets the text of a key.</summary>
    /// <param name="key">The key.</param>
    /// <returns>Its text in the language, or the key itself where nothing holds it.</returns>
    public string Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        // Only what is loaded is looked in (Resource_IsLoadedInternal): a key is never read from a loose file.
        if (_resources.TryGetEntry("GLOBAL:" + key, out ResourceEntry? entry)
            || _resources.TryGetEntry("LOCAL:" + language + "/" + key, out entry))
        {
            ReadOnlySpan<byte> text = entry.Data.Span;
            int end = text.IndexOf((byte)0);
            return System.Text.Encoding.Latin1.GetString(end < 0 ? text : text[..end]);
        }

        return key;
    }
}
