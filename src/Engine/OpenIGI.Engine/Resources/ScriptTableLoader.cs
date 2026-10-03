using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Engine.Resources;

/// <summary>
/// Runs every script of a given name under a directory tree, so that each publishes its rows through a
/// native the caller has already registered.
/// </summary>
/// <remarks>
/// <para>
/// This is <c>File_WalkTree</c> (<c>0x4B1420</c>) driving a per-directory <c>*_Open</c>, which is the
/// shape of five separate tables in the original: missions (<c>0x484F30</c>), weapons
/// (<c>0x413B70</c>), materials, magic objects and AI settings. Each had its own copy of the walk.
/// </para>
/// <para>
/// Depth is capped because a symlinked or cyclic tree would otherwise walk forever, which is not a
/// failure mode worth inheriting.
/// </para>
/// </remarks>
public static class ScriptTableLoader
{
    private const string LogCategory = "Resources";

    /// <summary>The deepest the walk descends. Retail nests two levels below the root.</summary>
    public const int DefaultMaxDepth = 8;

    /// <summary>Runs every matching script below a root, in the order the walk finds them.</summary>
    /// <param name="fileSystem">The file system the tree is walked on.</param>
    /// <param name="resources">The registry the scripts are loaded through.</param>
    /// <param name="natives">The script namespace the scripts call back into.</param>
    /// <param name="root">The resource path of the directory to walk, for example <c>LOCAL:missions</c>.</param>
    /// <param name="scriptName">The file name to run, for example <c>mission.qvm</c>.</param>
    /// <param name="logger">Where diagnostics go; defaults to a sink that discards them.</param>
    /// <param name="maxDepth">How deep to descend.</param>
    /// <returns>How many scripts ran.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="EngineFatalException">A script failed to compile or faulted while running.</exception>
    public static int WalkAndRun(
        VirtualFileSystem fileSystem,
        ResourceRegistry resources,
        NativeRegistry natives,
        string root,
        string scriptName,
        IOpenIgiLogger? logger = null,
        int maxDepth = DefaultMaxDepth)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentException.ThrowIfNullOrEmpty(scriptName);

        IOpenIgiLogger log = logger ?? NullLogger.Instance;
        List<string> scripts = [];
        Walk(fileSystem, root, scriptName, scripts, 0, maxDepth);

        int ran = 0;

        foreach (string script in scripts)
        {
            string name = ToResourceName(script, root);

            if (!resources.TryLoad(name, out ReadOnlyMemory<byte> bytes))
            {
                log.Warning(LogCategory, $"Script '{name}' vanished between the scan and the load.");
                continue;
            }

            QvmProgram program;
            try
            {
                program = QvmProgram.Read(bytes);
            }
            catch (QvmFormatException ex)
            {
                throw new EngineFatalException($"Couldn't load script: {name}", ex);
            }

            QvmInterpreter interpreter = new(program, natives);
            QvmError error = interpreter.Run();
            if (error != QvmError.None)
            {
                throw new EngineFatalException($"{name} faulted with {error}: {interpreter.ErrorMessage}");
            }

            ran++;
        }

        return ran;
    }

    /// <summary>Turns a walked path back into a resource name under the alias the walk started from.</summary>
    /// <param name="path">The path the walk produced.</param>
    /// <param name="root">The resource path the walk started at, alias included.</param>
    /// <returns>The resource name to load by.</returns>
    /// <remarks>
    /// <para>
    /// Derived from the root that was walked rather than by searching the path for a known directory
    /// name. The previous version looked for the literal substring <c>missions/</c> and re-prefixed
    /// <c>LOCAL:</c>, which is correct only when the tree is rooted exactly there.
    /// </para>
    /// <para>
    /// It is also why this is worth fixing before it bites rather than after: for a game whose data sits
    /// under a subdirectory the search would drop the prefix, and the load would fail with a
    /// "vanished between the scan and the load" warning naming a path nobody typed.
    /// </para>
    /// </remarks>
    public static string ToResourceName(string path, string root)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentException.ThrowIfNullOrEmpty(root);

        string normalized = path.Replace('\\', '/');
        if (normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase) || normalized.Contains(':', StringComparison.Ordinal))
        {
            return normalized;
        }

        // The walk yielded a path relative to whatever device answered; put it back under the alias the
        // caller asked for, so the load resolves through the same mount the walk came from.
        int alias = root.IndexOf(':', StringComparison.Ordinal);
        string prefix = alias >= 0 ? root[..(alias + 1)] : string.Empty;
        return prefix + normalized.TrimStart('/');
    }

    private static void Walk(
        VirtualFileSystem fileSystem,
        string directory,
        string scriptName,
        List<string> scripts,
        int depth,
        int maxDepth)
    {
        if (depth > maxDepth)
        {
            return;
        }

        scripts.AddRange(fileSystem.EnumerateFiles(directory, scriptName));

        foreach (string child in fileSystem.EnumerateDirectories(directory))
        {
            Walk(fileSystem, child, scriptName, scripts, depth + 1, maxDepth);
        }
    }
}
