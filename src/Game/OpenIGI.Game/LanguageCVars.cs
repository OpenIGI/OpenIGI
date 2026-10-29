using OpenIGI.Core.Configuration;

namespace OpenIGI.Game;

/// <summary>The language the games' text is read in.</summary>
/// <remarks>
/// <para>
/// Both games ship each language as a folder of archives, <c>language/&lt;name&gt;/</c>, and every archive names its strings after
/// its own language: German's <c>missions.res</c> holds <c>LOCAL:german/Mission 1 Name</c>. So the folder and the name a key is
/// asked for go together, and swapping two folders by hand leaves the menus asking one language for another's keys -- the
/// <c>RM: Mission 1 Name</c> the mission list showed. The original picks both from its configuration; here it is this variable.
/// </para>
/// <para>
/// A change takes effect at once: the host swaps one language's archives for the other's between frames, and everything that
/// shows text looks its keys up as it draws.
/// </para>
/// </remarks>
[CVarContainer]
public static class LanguageCVars
{
    /// <summary>The language the games' text is read in.</summary>
    public static readonly EnumCVar<GameLanguage> Language = CVar.Choice(
        "language",
        GameLanguage.English,
        CVarOptions.Archive,
        "The language of the games' text: english, german, french, italian, spanish or usa.");

    /// <summary>Gets the folder under <c>language/</c> the chosen language's archives are in, and the name its keys carry.</summary>
    /// <remarks>Every installation names its folders the same, <c>USA</c> in capitals; the lookups are case-blind.</remarks>
    public static string Directory => Language.Value.ToString().ToLowerInvariant();
}

/// <summary>The languages the games ship, in the order the original's language constants number them.</summary>
public enum GameLanguage
{
    /// <summary>British English, the default.</summary>
    English,

    /// <summary>German.</summary>
    German,

    /// <summary>French.</summary>
    French,

    /// <summary>Italian.</summary>
    Italian,

    /// <summary>Spanish.</summary>
    Spanish,

    /// <summary>American English, a separate set of strings.</summary>
    Usa,
}
