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
    /// <returns>Each file's path relative to the installation, and its contents.</returns>
    /// <exception cref="ArgumentException">The name is empty or too long, or a script is not one <c>DefineMission</c> call.</exception>
    public static IReadOnlyList<(string GamePath, byte[] Bytes)> Build(
        string baseDirectory,
        ReadOnlyMemory<byte> baseScript,
        IEnumerable<(string RelativePath, ReadOnlyMemory<byte> Bytes)> baseFiles,
        int id,
        string name,
        (string Directory, ReadOnlyMemory<byte> Script)? previous)
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

        files.Add(($"{directory}/{ScriptName}", Rewrite(baseScript, arguments =>
        {
            arguments[IdArgument] = new QscIntLiteral(id);
            arguments[NameArgument] = new QscStringLiteral(name);
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
