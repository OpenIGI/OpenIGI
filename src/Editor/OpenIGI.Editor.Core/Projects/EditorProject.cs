using System.Text.Json;

namespace OpenIGI.Editor.Core.Projects;

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

    private const int Schema = 1;

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
        return new EditorProject(root, storedName, storedTitle);
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
    public void Write(string gamePath, ReadOnlySpan<byte> bytes)
    {
        string target = PathFor(gamePath);
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

    private void WriteManifest()
    {
        using FileStream stream = new(Path.Combine(Root, ManifestName), FileMode.Create, FileAccess.Write, FileShare.None);
        using Utf8JsonWriter json = new(stream, new JsonWriterOptions { Indented = true });
        json.WriteStartObject();
        json.WriteNumber("schema", Schema);
        json.WriteString("name", Name);
        json.WriteString("title", TitleId);
        json.WriteEndObject();
    }
}
