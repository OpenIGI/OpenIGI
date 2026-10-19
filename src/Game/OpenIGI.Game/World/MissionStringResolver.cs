using OpenIGI.Engine.Resources;

namespace OpenIGI.Game.World;

/// <summary>
/// Resolves the string keys a mission's own data carries — objective rows, computer highlights, status
/// messages — to the localized text behind them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not just <see cref="MenuStringTable"/>.</b> That table resolves a menu label, and a menu
/// label is addressed the way <c>Menu_GetString</c> (<c>0x418EA0</c>) addresses one: by building
/// <c>LOCAL:&lt;language&gt;/&lt;key&gt;</c> and asking the registry for that. Mission data does not work
/// that way. Its archives — <c>objectives.res</c>, <c>messages.res</c>, <c>computer.res</c> — are
/// registered whole, and the entries inside them keep their own names, so a status message's
/// <c>M4_MSG_1</c> is registered as <c>M4_MSG_1</c> and nothing else. Asked for
/// <c>LOCAL:english/M4_MSG_1</c> the table misses and hands back its <c>RM:</c> placeholder, which is
/// exactly what a mission's messages showed before this existed.
/// </para>
/// <para>
/// So the registry is asked first, by the raw key, and the menu table is the fallback. That order also
/// keeps the retail behaviour for a key that genuinely is missing: <see cref="MenuStringTable.Get"/>
/// still produces <c>RM: &lt;key&gt;</c>, which is what the original puts on screen rather than a blank.
/// </para>
/// </remarks>
public sealed class MissionStringResolver
{
    private readonly ResourceRegistry _resources;
    private readonly MenuStringTable _menuStrings;

    /// <summary>Initialises a new instance of the <see cref="MissionStringResolver"/> class.</summary>
    /// <param name="resources">The registry the mission's language archives were registered into.</param>
    /// <param name="menuStrings">The menu table, used when the registry has no such entry.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public MissionStringResolver(ResourceRegistry resources, MenuStringTable menuStrings)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(menuStrings);
        _resources = resources;
        _menuStrings = menuStrings;
    }

    /// <summary>Resolves a key to its localized text.</summary>
    /// <param name="key">The resource key a mission task names.</param>
    /// <returns>The text, or the menu table's placeholder when nothing carries it.</returns>
    public string Get(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        // The archives hold CSTR entries: Latin-1 bytes to the first NUL, with whatever the packer left
        // after it. Taking the whole span would append that tail to every line.
        if (_resources.TryGetEntry(key, out ResourceEntry? entry))
        {
            ReadOnlySpan<byte> bytes = entry.Data.Span;
            int terminator = bytes.IndexOf((byte)0);
            return System.Text.Encoding.Latin1.GetString(terminator < 0 ? bytes : bytes[..terminator]);
        }

        return _menuStrings.Get(key);
    }
}
