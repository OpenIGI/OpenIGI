using OpenIGI.Core.Configuration;

namespace OpenIGI.Host.Editor;

/// <summary>Where the editor keeps a game's project, for the editor that saves into it and the game that plays it.</summary>
/// <remarks>
/// One project per game for now, beside the user's settings. The editor mounts it over the game while it is open; with
/// <see cref="EditorCVars.PlayProject"/> on, the game mounts it too, before it looks for missions, so what was made in the
/// editor (a reshaped level, a new mission) is what is played.
/// </remarks>
public static class EditorProjects
{
    /// <summary>The folder inside a project that holds its game files, laid out as the installation is.</summary>
    public const string FilesFolder = "files";

    /// <summary>The content layer a game's default project is mounted as.</summary>
    public const string DefaultLayerId = "editor-default";

    /// <summary>Gets a game's default project folder.</summary>
    /// <param name="titleId">The game's id.</param>
    /// <returns>The folder, which may not exist yet.</returns>
    public static string DefaultRoot(string titleId) =>
        Path.Combine(SettingsPaths.GetUserDirectory(), "editor", "projects", titleId, "default");

    /// <summary>Gets the folder of a game's default project that holds its game files.</summary>
    /// <param name="titleId">The game's id.</param>
    /// <returns>The folder, which may not exist yet.</returns>
    public static string DefaultFiles(string titleId) => Path.Combine(DefaultRoot(titleId), FilesFolder);
}
