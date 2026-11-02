using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Collections;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Editor.Core.Projects;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;
using OpenIGI.Host.Editor;
using OpenIGI.UI.Kit;

namespace OpenIGI.Editor;

/// <summary>
/// What the editor is working on: the open mission's object script as a document, its edit history and the
/// selection every panel shares.
/// </summary>
internal sealed class MissionWorkspace
{
    private readonly IEditorHost _host;
    private MissionDescriptor? _loadedFor;

    public MissionWorkspace(IEditorHost host, EditorProject project)
    {
        _host = host;
        Project = project;
        History.Changed += () => Changed?.Invoke();
    }

    /// <summary>Gets the project edits are saved into.</summary>
    public EditorProject Project { get; }

    /// <summary>Gets whether the open mission's objects come from the project rather than the game's files.</summary>
    public bool MissionIsInProject => Mission is { } mission && Project.Has(ObjectsGamePath(mission));

    /// <summary>Raised when the document is replaced or edited, or the history moves.</summary>
    public event Action? Changed;

    /// <summary>Gets the open document, or <see langword="null"/> before a mission is open.</summary>
    public MissionDocument? Document { get; private set; }

    /// <summary>Gets the mission the document belongs to.</summary>
    public MissionDescriptor? Mission => Document is null ? null : _loadedFor;

    /// <summary>Gets the open mission's name as the menus show it, or empty.</summary>
    public string MissionName => Mission is { } mission ? _host.MissionName(mission) : string.Empty;

    /// <summary>Gets the edit history.</summary>
    public EditHistory History { get; } = new();

    /// <summary>Gets the selection.</summary>
    public Selection<MissionTask> Selection { get; } = new();

    /// <summary>Gets the reason the document could not be read, if it could not.</summary>
    public string? LoadError { get; private set; }

    /// <summary>Follows the host: reads the mission's object script once the host has a new mission up.</summary>
    public void Sync()
    {
        MissionDescriptor? mission = _host.Mission;
        if (ReferenceEquals(mission, _loadedFor) || _host.IsWarmingUp)
        {
            return;
        }

        _loadedFor = mission;
        Selection.Clear();
        History.Clear();
        Document = null;
        LoadError = null;
        if (mission is null)
        {
            Changed?.Invoke();
            return;
        }

        try
        {
            if (!_host.TryReadFile("LOCAL:" + ObjectsGamePath(mission), out ReadOnlyMemory<byte> image))
            {
                throw new IOException($"{ObjectsGamePath(mission)} was not found.");
            }

            Document = MissionDocument.Read(image);
            Document.Changed += OnDocumentChanged;
            _host.Logger.Information("Editor", $"Opened {_host.MissionName(mission)}: {Document.EnumerateTasks().Count()} tasks.");
        }
        catch (Exception error) when (error is IOException or Scripting.Qvm.QvmFormatException or Scripting.Qsc.QscDecompileException)
        {
            LoadError = error.Message;
            _host.Logger.Warning("Editor", $"Could not open {_host.MissionName(mission)}'s objects: {error.Message}");
        }

        Changed?.Invoke();
    }

    /// <summary>Saves the document into the project, where the game will load it from.</summary>
    /// <returns>Whether there was a document to save.</returns>
    /// <exception cref="IOException">The project cannot be written.</exception>
    public bool Save()
    {
        if (Document is not { } document || Mission is not { } mission)
        {
            return false;
        }

        WriteToProject(ObjectsGamePath(mission), document.ToImage());
        History.MarkSaved();
        return true;
    }

    /// <summary>Writes a file into the project and rescans it, so the game reads the new copy from then on.</summary>
    /// <param name="gamePath">The game path, relative to the installation.</param>
    /// <param name="bytes">The contents.</param>
    /// <exception cref="IOException">The project cannot be written.</exception>
    public void WriteToProject(string gamePath, ReadOnlySpan<byte> bytes)
    {
        Project.Write(gamePath, bytes);
        _host.MountContent(Project.LayerId, Project.Name, Project.FilesDirectory);
        _host.Logger.Information("Editor", $"Saved {Project.PathFor(gamePath)}.");
    }

    /// <summary>Reads the open mission's object script again on the next frame, dropping the history.</summary>
    public void Reload() => _loadedFor = null;

    /// <summary>Removes the project's copy of the open mission, so the game's own is used again, and reloads it.</summary>
    /// <returns>Whether the project had a copy.</returns>
    public bool RevertMission()
    {
        if (Mission is not { } mission || !Project.Revert(ObjectsGamePath(mission)))
        {
            return false;
        }

        _host.MountContent(Project.LayerId, Project.Name, Project.FilesDirectory);
        Reload();
        _host.ReloadMission();
        return true;
    }

    /// <summary>Gets the game path of a mission's object script, relative to the installation.</summary>
    /// <param name="mission">The mission.</param>
    /// <returns>The path.</returns>
    public static string ObjectsGamePath(MissionDescriptor mission) =>
        $"{mission.MissionDirectory.TrimEnd('/', '\\')}/{LevelLoader.ObjectsScriptName}";

    /// <summary>Applies a command through the history.</summary>
    /// <param name="command">The command.</param>
    public void Execute(IEditorCommand command) => History.Execute(command);

    private void OnDocumentChanged(MissionChange change)
    {
        if (change.Kind == MissionChangeKind.Removed)
        {
            Selection.Prune(task => Document?.Contains(task) == true);
        }

        Changed?.Invoke();
    }
}

/// <summary>How a task is shown in every panel: its icon, its name and whether it has a place in the world.</summary>
internal static class TaskPresentation
{
    /// <summary>Gets the icon for a task's type.</summary>
    /// <param name="task">The task.</param>
    /// <returns>The icon.</returns>
    public static KitIcon Icon(MissionTask task)
    {
        string type = task.TypeName;
        return type switch
        {
            _ when Has(type, "Human") || Has(type, "Soldier") || Has(type, "Player") => KitIcon.Person,
            _ when Has(type, "Light") => KitIcon.Light,
            _ when Has(type, "Camera") => KitIcon.Camera,
            _ when Has(type, "Terrain") || Has(type, "HeightMap") => KitIcon.Terrain,
            _ when Has(type, "Graph") || Has(type, "Spline") || Has(type, "Path") => KitIcon.Graph,
            _ when Has(type, "Sound") || Has(type, "Music") || Has(type, "Ambient") => KitIcon.Sound,
            _ when Has(type, "Container") || type == TaskTree.RootTypeName => KitIcon.Hierarchy,
            _ when Position(task) is not null => KitIcon.Cube,
            _ => KitIcon.Document,
        };
    }

    /// <summary>Gets the name a task is listed under: its label, or its type when the label is empty.</summary>
    /// <param name="task">The task.</param>
    /// <returns>The name.</returns>
    public static string Name(MissionTask task) => task.Label.Length > 0 ? task.Label : task.TypeName;

    /// <summary>Gets the parameter that places a task in the world, if it has one.</summary>
    /// <param name="task">The task.</param>
    /// <returns>The position parameter, or <see langword="null"/>.</returns>
    public static MissionParameter? Position(MissionTask task) =>
        task.FindParameter(ScriptParamType.ObjectPos) ?? task.FindParameter("Position");

    /// <summary>Gets where a task stands in the world, in metres, if it has a constant position.</summary>
    /// <param name="task">The task.</param>
    /// <returns>The position, or <see langword="null"/>.</returns>
    public static Vector3d? PositionMetres(MissionTask task) =>
        Position(task) is { } position && position.TryGetNumbers(out double[] units) && units.Length == 3
            ? new Vector3d(WorldUnits.ToMetres(units[0]), WorldUnits.ToMetres(units[1]), WorldUnits.ToMetres(units[2]))
            : null;

    /// <summary>Gets the parameter that orients a task, if it has one: the three engine Euler angles.</summary>
    /// <param name="task">The task.</param>
    /// <returns>The orientation parameter, or <see langword="null"/>.</returns>
    public static MissionParameter? Orientation(MissionTask task) => task.FindParameter(ScriptParamType.Real32x9);

    /// <summary>Gets a task's orientation as a rotation, identity when it has none.</summary>
    /// <param name="task">The task.</param>
    /// <returns>The rotation, composed as the engine composes it.</returns>
    public static Matrix3f OrientationMatrix(MissionTask task) =>
        Orientation(task) is { } orientation && orientation.TryGetNumbers(out double[] angles) && angles.Length == 3
            ? Matrix3f.FromEngineEulerAngles((float)angles[0], (float)angles[1], (float)angles[2])
            : Matrix3f.Identity;

    private static bool Has(string type, string part) => type.Contains(part, StringComparison.OrdinalIgnoreCase);
}
