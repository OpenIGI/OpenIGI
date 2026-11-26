using OpenIGI.Formats.Ilff;
using OpenIGI.Scripting.Qsc;

namespace OpenIGI.Editor.Core.Missions;

/// <summary>
/// A new mission made from an existing one: its level copied into a folder of its own, a <c>mission.qvm</c> that defines it,
/// and the campaign's last mission pointed at it so that the game's mission menu lists it.
/// </summary>
/// <remarks>
/// <para>
/// The original finds its missions by running every <c>mission.qvm</c> under <c>LOCAL:missions</c>
/// (<c>Mission_LoadAll</c>), each one a single <c>DefineMission</c> call, and its menus walk the campaign from mission 1 by
/// each one's next mission. So a mission needs nothing registered anywhere else: its folder, its script, and a link from the
/// mission before it. The script is written from the base mission's own, every argument kept but the id, the name, the
/// next mission and the two folders, so it compiles in the base's version and runs in the original.
/// </para>
/// <para>
/// The level's files are copied under their own names, so the level pack the base names (<c>level1</c> for
/// <c>level1.dat</c>) is found in the new folder too.
/// </para>
/// </remarks>
public static class NewMission
{
    /// <summary>The script function a <c>mission.qvm</c> defines its mission with.</summary>
    public const string DefineFunction = "DefineMission";

    /// <summary>The name of a mission's script.</summary>
    public const string ScriptName = "mission.qvm";

    /// <summary>The longest name the original keeps (its record holds 32 bytes with the terminator).</summary>
    public const int MaximumNameLength = 31;

    private const int IdArgument = 0;
    private const int NameArgument = 1;
    private const int DescriptionArgument = 2;
    private const int NextArgument = 4;
    private const int DirectoryArgument = 5;
    private const int LevelPackDirectoryArgument = 7;
    private const int ArgumentCount = 11;

    /// <summary>Gets the folder a new mission's level goes in: beside its base, named for its id.</summary>
    /// <param name="baseDirectory">The base mission's folder, such as <c>missions/location0/level1</c>.</param>
    /// <param name="id">The new mission's id.</param>
    /// <returns>The folder, such as <c>missions/location0/level15</c>.</returns>
    public static string DirectoryFor(string baseDirectory, int id)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseDirectory);
        string trimmed = baseDirectory.Replace('\\', '/').TrimEnd('/');
        int slash = trimmed.LastIndexOf('/');
        return slash < 0 ? $"level{id}" : $"{trimmed[..slash]}/level{id}";
    }

    /// <summary>Gets the files that make a new mission.</summary>
    /// <param name="baseDirectory">The base mission's folder, relative to the installation.</param>
    /// <param name="baseScript">The base mission's <c>mission.qvm</c>.</param>
    /// <param name="baseFiles">Every file of the base mission's folder, each with its path relative to that folder.</param>
    /// <param name="id">The new mission's id.</param>
    /// <param name="name">Its name, as the menus show it.</param>
    /// <param name="previous">The mission to point at the new one, and its <c>mission.qvm</c>; none to leave the campaign as it is.</param>
    /// <param name="missionTexts">
    /// Each language's archive of mission names and descriptions (<c>language/&lt;language&gt;/missions.res</c>), with its path
    /// relative to the installation; none to write the name into the script as it is.
    /// </param>
    /// <returns>Each file's path relative to the installation, and its contents.</returns>
    /// <exception cref="ArgumentException">The name is empty or too long, or a script is not one <c>DefineMission</c> call.</exception>
    /// <remarks>
    /// A mission's name and description are not words but keys: the original's menus look both up in the language's
    /// <c>missions.res</c> (<c>MenuManager_GetString</c>, <c>0x418EA0</c>) and show <c>RM: &lt;key&gt;</c> for one that is not
    /// there. So the new mission is given keys of its own, named as the base's are (<c>Mission 15 Name</c> after
    /// <c>Mission 1 Name</c>), and every language's archive that has the base's gets them: the name as typed, the
    /// description the base's own.
    /// </remarks>
    public static IReadOnlyList<(string GamePath, byte[] Bytes)> Build(
        string baseDirectory,
        ReadOnlyMemory<byte> baseScript,
        IEnumerable<(string RelativePath, ReadOnlyMemory<byte> Bytes)> baseFiles,
        int id,
        string name,
        (string Directory, ReadOnlyMemory<byte> Script)? previous,
        IEnumerable<(string GamePath, ReadOnlyMemory<byte> Bytes)>? missionTexts = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(baseFiles);
        if (name.Length > MaximumNameLength)
        {
            throw new ArgumentException($"A mission's name is at most {MaximumNameLength} characters.", nameof(name));
        }

        string directory = DirectoryFor(baseDirectory, id);
        List<(string, byte[])> files = [];
        foreach ((string relative, ReadOnlyMemory<byte> bytes) in baseFiles)
        {
            if (!relative.Equals(ScriptName, StringComparison.OrdinalIgnoreCase))
            {
                files.Add(($"{directory}/{relative.Replace('\\', '/')}", Relocated(relative, bytes, baseDirectory, directory)));
            }
        }

        (string Name, string Description)? keys = AddTexts(files, baseScript, id, name, missionTexts);
        files.Add(($"{directory}/{ScriptName}", Rewrite(baseScript, arguments =>
        {
            arguments[IdArgument] = new QscIntLiteral(id);
            arguments[NameArgument] = new QscStringLiteral(keys?.Name ?? name);
            if (keys is { } named)
            {
                arguments[DescriptionArgument] = new QscStringLiteral(named.Description);
            }

            arguments[NextArgument] = new QscSymbol("MISSION_NEXT_MISSION_UNDEFINED");
            arguments[DirectoryArgument] = new QscStringLiteral(directory);
            arguments[LevelPackDirectoryArgument] = new QscStringLiteral(directory);
        })));

        if (previous is { } before)
        {
            files.Add(($"{before.Directory.Replace('\\', '/').TrimEnd('/')}/{ScriptName}", Rewrite(before.Script, arguments => arguments[NextArgument] = new QscIntLiteral(id))));
        }

        return files;
    }

    /// <summary>
    /// A mission's script with its name and description written in as they read, not as keys into the language archives: for a
    /// mission that travels without those archives (a community map), which would otherwise show its keys.
    /// </summary>
    /// <param name="script">The mission's <c>mission.qvm</c>.</param>
    /// <param name="name">Its name, cut to the <see cref="MaximumNameLength"/> the original keeps.</param>
    /// <param name="description">What it is.</param>
    /// <returns>The script, or <see langword="null"/> when it is not one this game's <c>DefineMission</c> call.</returns>
    public static byte[]? Named(ReadOnlyMemory<byte> script, string name, string description)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(description);
        try
        {
            if (Arguments(script) is null)
            {
                return null;
            }
        }
        catch (OpenIGI.Scripting.Qvm.QvmFormatException)
        {
            return null;
        }

        return Rewrite(script, arguments =>
        {
            arguments[NameArgument] = new QscStringLiteral(name.Length > MaximumNameLength ? name[..MaximumNameLength] : name);
            arguments[DescriptionArgument] = new QscStringLiteral(description);
        });
    }

    /// <summary>Gets the key a mission's name or description is kept under, made from the base's by putting the new id for its own.</summary>
    /// <param name="baseKey">The base mission's key, such as <c>Mission 1 Name</c>.</param>
    /// <param name="baseId">The base mission's id.</param>
    /// <param name="id">The new mission's id.</param>
    /// <returns>The key, or <see langword="null"/> when the base's key does not hold its id as a word of its own.</returns>
    public static string? KeyFor(string baseKey, int baseId, int id)
    {
        ArgumentNullException.ThrowIfNull(baseKey);
        string number = baseId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        for (int at = baseKey.IndexOf(number, StringComparison.Ordinal); at >= 0; at = baseKey.IndexOf(number, at + 1, StringComparison.Ordinal))
        {
            bool startsWord = at == 0 || !char.IsAsciiDigit(baseKey[at - 1]);
            bool endsWord = at + number.Length == baseKey.Length || !char.IsAsciiDigit(baseKey[at + number.Length]);
            if (startsWord && endsWord)
            {
                return string.Concat(baseKey.AsSpan(0, at), id.ToString(System.Globalization.CultureInfo.InvariantCulture), baseKey.AsSpan(at + number.Length));
            }
        }

        return null;
    }

    // The new mission's name and description added to every language archive that has the base's, and the keys they are
    // under; null when no archive has them, so the script keeps the name as typed.
    private static (string Name, string Description)? AddTexts(
        List<(string, byte[])> files,
        ReadOnlyMemory<byte> baseScript,
        int id,
        string name,
        IEnumerable<(string GamePath, ReadOnlyMemory<byte> Bytes)>? missionTexts)
    {
        if (missionTexts is null || Arguments(baseScript) is not { } arguments
            || arguments[IdArgument] is not QscIntLiteral { Value: int baseId }
            || arguments[NameArgument] is not QscStringLiteral { Value: { Length: > 0 } baseName }
            || arguments[DescriptionArgument] is not QscStringLiteral { Value: { Length: > 0 } baseDescription }
            || KeyFor(baseName, baseId, id) is not { Length: <= MaximumNameLength } nameKey
            || KeyFor(baseDescription, baseId, id) is not { } descriptionKey)
        {
            return null;
        }

        bool added = false;
        foreach ((string path, ReadOnlyMemory<byte> bytes) in missionTexts)
        {
            IresArchive archive;
            try
            {
                archive = IresArchive.Read(bytes);
            }
            catch (IlffFormatException)
            {
                continue;
            }

            // Named as the archive names the base's: a bare key in some, a path that ends in it in others.
            if (archive.Entries.FirstOrDefault(e => Holds(e.Name, baseName)) is not { } nameEntry
                || archive.Entries.FirstOrDefault(e => Holds(e.Name, baseDescription)) is not { } descriptionEntry)
            {
                continue;
            }

            string newName = string.Concat(nameEntry.Name.AsSpan(0, nameEntry.Name.Length - baseName.Length), nameKey);
            string newDescription = string.Concat(descriptionEntry.Name.AsSpan(0, descriptionEntry.Name.Length - baseDescription.Length), descriptionKey);
            List<IresWriter.Entry> entries =
            [
                .. archive.Entries
                    .Where(e => !e.Name.Equals(newName, StringComparison.OrdinalIgnoreCase) && !e.Name.Equals(newDescription, StringComparison.OrdinalIgnoreCase))
                    .Select(static e => new IresWriter.Entry(e.Name, e.Kind, e.Data)),
                new IresWriter.Entry(newName, nameEntry.Kind, System.Text.Encoding.Latin1.GetBytes(name + "\0")),
                new IresWriter.Entry(newDescription, descriptionEntry.Kind, descriptionEntry.Data),
            ];
            files.Add((path.Replace('\\', '/'), IresWriter.Write(entries)));
            added = true;
        }

        return added ? (nameKey, descriptionKey) : null;

        static bool Holds(string entry, string key) =>
            entry.EndsWith(key, StringComparison.OrdinalIgnoreCase) && (entry.Length == key.Length || entry[^(key.Length + 1)] is '/' or ':');
    }

    // A mission script's DefineMission arguments, or null when it is not one such call.
    private static System.Collections.ObjectModel.ReadOnlyCollection<QscExpression>? Arguments(ReadOnlyMemory<byte> image) =>
        QscDecompiler.Decompile(image).Statements is [QscExpressionStatement { Expression: QscCall { Name: DefineFunction } call }]
        && call.Arguments.Count == ArgumentCount
            ? call.Arguments
            : null;

    // A file of the level as it goes in the new folder: an archive whose entries are named by the base's folder (the object
    // lightmaps are, LOCAL:missions/location0/level1/lightmaps/...) renamed into the new one, or the original would look them
    // up under the new folder (MISSION:lightmaps/...) and find none; anything else as it is.
    private static byte[] Relocated(string relative, ReadOnlyMemory<byte> bytes, string baseDirectory, string directory)
    {
        if (!relative.EndsWith(".res", StringComparison.OrdinalIgnoreCase))
        {
            return bytes.ToArray();
        }

        string from = baseDirectory.Replace('\\', '/').TrimEnd('/') + "/";
        string to = directory + "/";
        IresArchive archive;
        try
        {
            archive = IresArchive.Read(bytes);
        }
        catch (IlffFormatException)
        {
            return bytes.ToArray();
        }

        bool renamed = false;
        List<IresWriter.Entry> entries = [];
        foreach (IresEntry entry in archive.Entries)
        {
            string name = entry.Name;
            int at = name.Replace('\\', '/').IndexOf(from, StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
            {
                name = name[..at] + to + name[(at + from.Length)..];
                renamed = true;
            }

            entries.Add(new IresWriter.Entry(name, entry.Kind, entry.Data));
        }

        return renamed ? IresWriter.Write(entries) : bytes.ToArray();
    }

    // A mission script with its DefineMission call's arguments changed, compiled in the script's own version.
    private static byte[] Rewrite(ReadOnlyMemory<byte> image, Action<QscExpression[]> change)
    {
        QscScript script = QscDecompiler.Decompile(image);
        if (script.Statements is not [QscExpressionStatement { Expression: QscCall { Name: DefineFunction } call }]
            || call.Arguments.Count != ArgumentCount)
        {
            throw new ArgumentException($"A mission script is one {DefineFunction} call of {ArgumentCount} arguments.", nameof(image));
        }

        QscExpression[] arguments = [.. call.Arguments];
        change(arguments);
        QscScript rewritten = new([new QscExpressionStatement(new QscCall(DefineFunction, arguments))], script.Version)
        {
            RequestedStackSizeBytes = script.RequestedStackSizeBytes,
            RequestedMaxLocals = script.RequestedMaxLocals,
            FileInfo = script.FileInfo,
        };
        return QscCompiler.Compile(rewritten);
    }
}
