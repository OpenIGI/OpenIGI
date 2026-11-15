using OpenIGI.Formats.Ilff;
using OpenIGI.Host.Editor;
using OpenIGI.UI.Editor.Widgets;

namespace OpenIGI.Editor;

/// <summary>What a name-like value is: the list a field's dropdown offers.</summary>
internal enum ValueKind
{
    /// <summary>Nothing to offer.</summary>
    None,

    /// <summary>A model, by its bare name (<c>106_01_1</c>).</summary>
    Model,

    /// <summary>A sound, by its bare name (<c>deagle_shot_1</c>).</summary>
    Sound,

    /// <summary>A texture or sprite, by its file name.</summary>
    Texture,

    /// <summary>A localised text key written in angle brackets (<c>&lt;GLOCK_INC&gt;</c>), as configs write them.</summary>
    BracketedTextKey,

    /// <summary>A mission's text key, bare (<c>M1_MSG_3</c>), as status messages and objectives name them.</summary>
    TextKey,

    /// <summary>A constant the game's scripts know, of a prefix (<c>WEAPON_ID_</c>).</summary>
    Constant,
}

/// <summary>
/// The valid values the editor's dropdowns offer, read from the game as it stands: models and sounds from the open
/// mission's archives and the shared folders, text keys (with what they say) from the language archives, and constants from
/// the names the running game's scripts know.
/// </summary>
/// <remarks>Read once per open mission, the first time a list is asked for; anything typed outside the lists is still taken.</remarks>
/// <param name="host">The editor host.</param>
internal sealed class ValueCatalog(IEditorHost host)
{
    // The shared folders whose archives hold what every mission can use.
    private static readonly string[] Shared = ["common", "weapons", "ammo", "humanplayer", "magicobj"];

    private int? _mission;
    private Dictionary<string, List<ComboItem>>? _byExtension;
    private List<ComboItem>? _keys;

    /// <summary>The values of a kind.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="prefix">For constants, the prefix they share; for criteria, <c>CRITERIA_</c>.</param>
    /// <returns>The values, sorted.</returns>
    public IReadOnlyList<ComboItem> For(ValueKind kind, string? prefix = null)
    {
        if (host.Mission?.Id != _mission)
        {
            _mission = host.Mission?.Id;
            _byExtension = null;
        }

        return kind switch
        {
            ValueKind.Model => Files(".mef"),
            ValueKind.Sound => Files(".wav"),
            ValueKind.Texture => [.. Files(".tex"), .. Files(".tga"), .. Files(".bmp")],
            ValueKind.TextKey => Keys(),
            ValueKind.BracketedTextKey => [.. Keys().Select(static k => k with { Value = $"<{k.Value}>" })],
            ValueKind.Constant when prefix is { Length: > 0 } => Constants(prefix),
            _ => [],
        };
    }

    // The constants of a prefix: the title's own families first, in the order and with the words the title gives them (its
    // area criteria, which the first game reads as text and never registers), then whatever else the running game knows.
    private List<ComboItem> Constants(string prefix)
    {
        List<ComboItem> items = [];
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (Scripting.Vocabulary.ConstantFamily family in host.Vocabulary.Families.Values)
        {
            foreach (string name in family.Values.Select(static c => c.Key).Where(n => n.StartsWith(prefix, StringComparison.Ordinal)))
            {
                if (names.Add(name))
                {
                    items.Add(new ComboItem(name, family.Notes.GetValueOrDefault(name)));
                }
            }
        }

        bool described = items.Exists(static i => i.Detail is not null);
        items.AddRange(host.ScriptNames.Where(n => n.StartsWith(prefix, StringComparison.Ordinal) && names.Add(n)).Select(static n => new ComboItem(n)));

        // A family that says what its constants mean keeps the title's order; a bare list of names reads best sorted.
        return described ? items : [.. items.OrderBy(static i => i.Value, StringComparer.Ordinal)];
    }

    // Every file of an extension in the mission's folder and the shared ones, loose or inside an archive, by bare name.
    private List<ComboItem> Files(string extension)
    {
        if (_byExtension is null)
        {
            _byExtension = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, (string Name, string From)> seen = new(StringComparer.OrdinalIgnoreCase);
            List<string> roots = [.. Shared.Select(static s => "LOCAL:" + s)];
            if (host.Mission is { } mission)
            {
                roots.Insert(0, "LOCAL:" + mission.MissionDirectory.TrimEnd('/'));
            }

            foreach (string root in roots)
            {
                foreach (string file in Walk(root))
                {
                    string from = file[(file.LastIndexOf('/') + 1)..];
                    if (from.EndsWith(".res", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (string entry in Entries(file))
                        {
                            Note(seen, entry, from);
                        }
                    }
                    else
                    {
                        Note(seen, from, root[(root.IndexOf(':') + 1)..]);
                    }
                }
            }

            foreach ((string ext, IGrouping<string, (string Name, string From)> group) in seen.Values.GroupBy(static v => Path.GetExtension(v.Name)).Select(static g => (g.Key, g)))
            {
                _byExtension[ext] = [.. group.Select(static v => new ComboItem(Path.GetFileNameWithoutExtension(v.Name), v.From)).OrderBy(static i => i.Value, StringComparer.OrdinalIgnoreCase)];
            }
        }

        return _byExtension.GetValueOrDefault(extension) ?? [];

        static void Note(Dictionary<string, (string, string)> seen, string name, string from) => seen.TryAdd(name.Replace('\\', '/').Split('/')[^1], (name.Replace('\\', '/').Split('/')[^1], from));
    }

    // The language's text keys with what they say: the first language folder holding text, English first.
    private List<ComboItem> Keys()
    {
        if (_keys is not null)
        {
            return _keys;
        }

        _keys = [];
        string[] languages = [.. Safe(() => host.Files.EnumerateDirectories("LOCAL:language"))];
        string? language = languages.FirstOrDefault(static l => l.EndsWith("english", StringComparison.OrdinalIgnoreCase)) ?? languages.FirstOrDefault();
        if (language is null)
        {
            return _keys;
        }

        Dictionary<string, string> said = new(StringComparer.Ordinal);
        foreach (string file in Safe(() => host.Files.EnumerateFiles(language, "*.res")))
        {
            try
            {
                foreach (IresEntry entry in IresArchive.Read(host.Files.ReadAllBytes(file)).Entries.Where(static e => e.Kind == IresEntryKind.Body))
                {
                    string text = entry.AsText().Split('\0')[0].ReplaceLineEndings(" ");
                    said.TryAdd(entry.Name, text.Length > 80 ? text[..80] + "…" : text);
                }
            }
            catch (Exception error) when (error is IOException or IlffFormatException or InvalidDataException)
            {
                // A text archive that does not read offers nothing.
            }
        }

        _keys = [.. said.OrderBy(static p => p.Key, StringComparer.Ordinal).Select(static p => new ComboItem(p.Key, p.Value))];
        return _keys;
    }

    // Every file under a folder, recursively.
    private IEnumerable<string> Walk(string root)
    {
        Stack<string> folders = new([root]);
        while (folders.TryPop(out string? folder))
        {
            foreach (string sub in Safe(() => host.Files.EnumerateDirectories(folder)))
            {
                folders.Push(sub.Replace('\\', '/'));
            }

            foreach (string file in Safe(() => host.Files.EnumerateFiles(folder, "*")))
            {
                yield return file.Replace('\\', '/');
            }
        }
    }

    private IEnumerable<string> Entries(string archive)
    {
        try
        {
            return [.. IresArchive.Read(host.Files.ReadAllBytes(archive)).Entries.Where(static e => e.Kind == IresEntryKind.Body).Select(static e => e.Name)];
        }
        catch (Exception error) when (error is IOException or IlffFormatException or InvalidDataException)
        {
            return [];
        }
    }

    private static IEnumerable<string> Safe(Func<IEnumerable<string>> list)
    {
        try
        {
            return [.. list()];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return [];
        }
    }
}
