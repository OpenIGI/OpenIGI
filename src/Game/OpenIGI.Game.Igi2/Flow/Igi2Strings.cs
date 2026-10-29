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
/// <param name="language">The language's name, <c>Config_azGameLanguage</c>'s entry; by default the one the player has chosen now.</param>
public sealed class Igi2Strings(ResourceRegistry resources, string? language = null)
{
    /// <summary>The archives the game's own text is read from.</summary>
    /// <param name="language">The language folder, for example <c>english</c>.</param>
    /// <returns>The archives' resource names.</returns>
    public static IReadOnlyList<string> Archives(string language) =>
    [
        $"LOCAL:language/{language}/messages.res",
        $"LOCAL:language/{language}/objectives.res",
        $"LOCAL:language/{language}/computer.res",
    ];

    private readonly ResourceRegistry _resources = resources ?? throw new ArgumentNullException(nameof(resources));

    /// <summary>Gets the text of a key.</summary>
    /// <param name="key">The key.</param>
    /// <returns>Its text in the language, or the key itself where nothing holds it.</returns>
    public string Get(string key) => TryGet(key, out string? text) ? text : key;

    /// <summary>Gets the text of a key, where something holds it.</summary>
    /// <param name="key">The key.</param>
    /// <param name="text">Receives its text in the language.</param>
    /// <returns>Whether something holds it; <c>Config_GetLocalizedStringForLanguage</c>'s null.</returns>
    public bool TryGet(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? text)
    {
        ArgumentNullException.ThrowIfNull(key);
        // Only what is loaded is looked in (Resource_IsLoadedInternal): a key is never read from a loose file.
        if (_resources.TryGetEntry("GLOBAL:" + key, out ResourceEntry? entry)
            || _resources.TryGetEntry("LOCAL:" + (language ?? LanguageCVars.Directory) + "/" + key, out entry))
        {
            ReadOnlySpan<byte> bytes = entry.Data.Span;
            int end = bytes.IndexOf((byte)0);
            text = System.Text.Encoding.Latin1.GetString(end < 0 ? bytes : bytes[..end]);
            return true;
        }

        text = null;
        return false;
    }

    /// <summary>
    /// A weapon's short name, as the inventory shows it (<c>HumanPlayer_StatusDisplayHandler</c>): its short string, else its
    /// string, else its name as written.
    /// </summary>
    /// <param name="name">The weapon type's name.</param>
    /// <returns>The text.</returns>
    public string WeaponShortName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return TryGet("weapons/strings/short/" + name, out string? text) || TryGet("weapons/strings/" + name, out text) ? text : name;
    }
}
