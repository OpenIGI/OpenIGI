using System.Text.Json;

namespace OpenIGI.Editor.Core.Projects;

/// <summary>What a mission is made to run in, which decides what the editor lets it use.</summary>
public enum MissionTarget
{
    /// <summary>The original game as well as OpenIGI: nothing the retail executable cannot load.</summary>
    Retail,

    /// <summary>OpenIGI only: free to use what the remaster adds, such as mountain areas.</summary>
    OpenIgi,
}

/// <summary>
/// An editor project: a folder holding a manifest and a <c>files</c> tree that mirrors the game's installation.
/// </summary>
/// <remarks>
/// <para>
/// The <c>files</c> tree is a content layer: mounted over the game, every file in it replaces the game's file at
/// the same path, so a saved mission is loaded by the game exactly as the editor wrote it, and the installation is
/// never written to. The same folder is what a pack is published from.
/// </para>
/// <para>
/// Files are written to a temporary name and moved into place, so a crash or a full disk mid-save never leaves a
/// half-written script for the game to load.
/// </para>
/// </remarks>
public sealed class EditorProject
{
    /// <summary>The manifest's file name.</summary>
    public const string ManifestName = "project.json";

    /// <summary>The name of the folder that mirrors the installation.</summary>
    public const string FilesFolder = "files";

    /// <summary>The folder under the project root that holds each script's graph layout.</summary>
    public const string GraphsFolder = "graphs";

    /// <summary>The extension a graph layout takes in place of its script's.</summary>
    public const string GraphExtension = ".graph.json";

    private const int Schema = 1;

    private readonly Dictionary<int, MissionTarget> _targets = [];

    private EditorProject(string root, string name, string titleId)
    {
        Root = root;
        Name = name;
        TitleId = titleId;
    }

    /// <summary>Gets the project folder.</summary>
    public string Root { get; }

    /// <summary>Gets the project's name.</summary>
    public string Name { get; }

    /// <summary>Gets the id of the game the project is for (<c>igi1</c>, <c>igi2</c>).</summary>
    public string TitleId { get; }

    /// <summary>Gets the folder that mirrors the installation and is mounted as a content layer.</summary>
    public string FilesDirectory => Path.Combine(Root, FilesFolder);

    /// <summary>Gets the content layer id the project mounts under.</summary>
    public string LayerId => "editor-" + Path.GetFileName(Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    /// <summary>Opens a project folder, creating it with a manifest when it does not exist.</summary>
    /// <param name="root">The project folder.</param>
    /// <param name="titleId">The game the project is for, used when creating it.</param>
    /// <param name="name">The name, used when creating it.</param>
    /// <returns>The project.</returns>
    /// <exception cref="IOException">The folder cannot be created or read.</exception>
    /// <exception cref="InvalidDataException">The manifest is not a project manifest, or is for another game.</exception>
    public static EditorProject OpenOrCreate(string root, string titleId, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(titleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        root = Path.GetFullPath(root);
        string manifest = Path.Combine(root, ManifestName);
        if (!File.Exists(manifest))
        {
            Directory.CreateDirectory(Path.Combine(root, FilesFolder));
            EditorProject created = new(root, name, titleId);
            created.WriteManifest();
            return created;
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(manifest));
        JsonElement json = document.RootElement;
        if (!json.TryGetProperty("schema", out JsonElement schema) || schema.GetInt32() != Schema
            || !json.TryGetProperty("title", out JsonElement title) || title.GetString() is not { } storedTitle)
        {
            throw new InvalidDataException($"'{manifest}' is not an editor project manifest.");
        }

        if (!string.Equals(storedTitle, titleId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"'{root}' is a project for '{storedTitle}', not '{titleId}'.");
        }

        string storedName = json.TryGetProperty("name", out JsonElement n) && n.GetString() is { Length: > 0 } s ? s : name;
        Directory.CreateDirectory(Path.Combine(root, FilesFolder));
        EditorProject project = new(root, storedName, storedTitle);
        if (json.TryGetProperty("missions", out JsonElement missions) && missions.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty mission in missions.EnumerateObject())
            {
                if (int.TryParse(mission.Name, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int id)
                    && mission.Value.TryGetProperty("target", out JsonElement target)
                    && Enum.TryParse(target.GetString(), ignoreCase: true, out MissionTarget kept))
                {
                    project._targets[id] = kept;
                }
            }
        }

        return project;
    }

    /// <summary>Gets what a mission is made to run in; a mission the project has not been told about is a retail one.</summary>
    /// <param name="missionId">The mission's id.</param>
    /// <returns>Its target.</returns>
    public MissionTarget TargetOf(int missionId) => _targets.GetValueOrDefault(missionId, MissionTarget.Retail);

    /// <summary>Sets what a mission is made to run in, kept in the manifest.</summary>
    /// <param name="missionId">The mission's id.</param>
    /// <param name="target">Its target.</param>
    /// <exception cref="IOException">The manifest cannot be written.</exception>
    public void SetTarget(int missionId, MissionTarget target)
    {
        if (TargetOf(missionId) == target && _targets.ContainsKey(missionId))
        {
            return;
        }

        _targets[missionId] = target;
        WriteManifest();
    }

    /// <summary>Gets the project path a game path is written to.</summary>
    /// <param name="gamePath">A path relative to the installation, with either slash (<c>missions/location0/level1/objects.qvm</c>).</param>
    /// <returns>The full path inside <see cref="FilesDirectory"/>.</returns>
    /// <exception cref="ArgumentException">The path is rooted or climbs out of the project.</exception>
    public string PathFor(string gamePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);
        string relative = gamePath.Replace('\\', '/').TrimStart('/');
        string full = Path.GetFullPath(Path.Combine(FilesDirectory, relative));
        string files = Path.GetFullPath(FilesDirectory) + Path.DirectorySeparatorChar;
        return Path.IsPathRooted(relative) || !full.StartsWith(files, StringComparison.OrdinalIgnoreCase)
            ? throw new ArgumentException($"'{gamePath}' is not a path inside the game's installation.", nameof(gamePath))
            : full;
    }

    /// <summary>Gets whether the project overrides a game file.</summary>
    /// <param name="gamePath">The game path.</param>
    /// <returns><see langword="true"/> when the project carries its own copy.</returns>
    public bool Has(string gamePath) => File.Exists(PathFor(gamePath));

    /// <summary>Writes a file into the project, atomically.</summary>
    /// <param name="gamePath">The game path it replaces.</param>
    /// <param name="bytes">The contents.</param>
    /// <exception cref="IOException">The file cannot be written.</exception>
    public void Write(string gamePath, ReadOnlySpan<byte> bytes) => WriteAtomically(PathFor(gamePath), bytes);

    /// <summary>Where the graph layout for a script is kept: beside the project's files, never among them.</summary>
    /// <param name="gamePath">The script's game path, such as <c>missions/location0/level1/objects.qvm</c>.</param>
    /// <returns>The sidecar's full path, <c>graphs/&lt;game path&gt;.graph.json</c> under the project root.</returns>
    /// <remarks>
    /// The <see cref="FilesFolder"/> is mounted over the game and exported for retail, so a layout file there would be shipped.
    /// Graphs are the editor's own: node positions, comments and frame sizes, which the game never reads.
    /// </remarks>
    public string GraphPathFor(string gamePath)
    {
        string script = PathFor(gamePath);
        string relative = Path.GetRelativePath(FilesDirectory, script);
        return Path.Combine(Root, GraphsFolder, Path.ChangeExtension(relative, GraphExtension));
    }

    /// <summary>Reads a script's graph layout.</summary>
    /// <param name="gamePath">The script's game path.</param>
    /// <returns>The sidecar's bytes, or <see langword="null"/> when the script has none yet.</returns>
    public byte[]? ReadGraph(string gamePath)
    {
        string path = GraphPathFor(gamePath);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    /// <summary>Saves a script's graph layout.</summary>
    /// <param name="gamePath">The script's game path.</param>
    /// <param name="bytes">The sidecar's bytes.</param>
    public void WriteGraph(string gamePath, ReadOnlySpan<byte> bytes) => WriteAtomically(GraphPathFor(gamePath), bytes);

    private static void WriteAtomically(string target, ReadOnlySpan<byte> bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary = target + ".saving";
        using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporary, target, overwrite: true);
    }

    /// <summary>Removes the project's copy of a game file, so the game's own is used again.</summary>
    /// <param name="gamePath">The game path.</param>
    /// <returns>Whether there was a copy to remove.</returns>
    public bool Revert(string gamePath)
    {
        string target = PathFor(gamePath);
        if (!File.Exists(target))
        {
            return false;
        }

        File.Delete(target);
        return true;
    }

    /// <summary>
    /// Copies every file the project carries into a folder laid out like the installation, ready to be copied into a
    /// retail game.
    /// </summary>
    /// <param name="target">The folder; created if missing, and emptied of an earlier export first.</param>
    /// <returns>The game paths written, relative to the installation.</returns>
    /// <exception cref="IOException">A file cannot be written.</exception>
    /// <exception cref="ArgumentException">The target is inside the project.</exception>
    public IReadOnlyList<string> ExportTo(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        string full = Path.GetFullPath(target);
        string root = Path.GetFullPath(Root) + Path.DirectorySeparatorChar;
        if ((full + Path.DirectorySeparatorChar).StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("An export cannot be written inside its own project.", nameof(target));
        }

        if (Directory.Exists(full))
        {
            Directory.Delete(full, recursive: true);
        }

        List<string> written = [];
        foreach (string file in Directory.EnumerateFiles(FilesDirectory, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".saving", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relative = Path.GetRelativePath(FilesDirectory, file);
            string destination = Path.Combine(full, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
            written.Add(relative.Replace('\\', '/'));
        }

        return written;
    }

    private void WriteManifest()
    {
        using FileStream stream = new(Path.Combine(Root, ManifestName), FileMode.Create, FileAccess.Write, FileShare.None);
        using Utf8JsonWriter json = new(stream, new JsonWriterOptions { Indented = true });
        json.WriteStartObject();
        json.WriteNumber("schema", Schema);
        json.WriteString("name", Name);
        json.WriteString("title", TitleId);
        if (_targets.Count > 0)
        {
            json.WriteStartObject("missions");
            foreach ((int id, MissionTarget target) in _targets.OrderBy(static t => t.Key))
            {
                json.WriteStartObject(id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                json.WriteString("target", target == MissionTarget.Retail ? "retail" : "openigi");
                json.WriteEndObject();
            }

            json.WriteEndObject();
        }

        json.WriteEndObject();
    }
}
