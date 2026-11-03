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
    private string _missionName = string.Empty;
    private int? _taskCount;

    public MissionWorkspace(IEditorHost host, EditorProject project)
    {
        _host = host;
        Project = project;
        Graphs = new MissionGraphs(host);
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

    /// <summary>Gets every task's values as last loaded or saved, which the inspector marks changes against.</summary>
    public MissionBaseline Baseline { get; } = new();

    /// <summary>Gets the mission the document belongs to.</summary>
    public MissionDescriptor? Mission => Document is null ? null : _loadedFor;

    /// <summary>Gets the open mission's name as the menus show it, or empty.</summary>
    /// <remarks>Read once when the mission opens: the host looks it up in the game's text files, too slow for every frame.</remarks>
    public string MissionName => Mission is not null ? _missionName : string.Empty;

    /// <summary>Gets how many tasks the document holds, counted when it changes rather than whenever asked.</summary>
    public int TaskCount => Document is null ? 0 : _taskCount ??= Document.EnumerateTasks().Count();

    /// <summary>Gets the open mission's AI graphs.</summary>
    public MissionGraphs Graphs { get; }

    /// <summary>Gets the edit history.</summary>
    public EditHistory History { get; } = new();

    /// <summary>Gets the selection.</summary>
    public Selection<MissionTask> Selection { get; } = new();

    /// <summary>Gets the reason the document could not be read, if it could not.</summary>
    public string? LoadError { get; private set; }

    /// <summary>Follows the host: reads the mission's object script once the host has a new mission up.</summary>
    public void Sync()
    {
        // Whatever the selection was handed, nothing that has left the mission stays selected: every action on the selection
        // assumes its tasks are in the document.
        if (Document is { } current && Selection.Items.Any(task => !current.Contains(task)))
        {
            Selection.Prune(current.Contains);
        }

        MissionDescriptor? mission = _host.Mission;
        if (ReferenceEquals(mission, _loadedFor) || _host.IsWarmingUp)
        {
            return;
        }

        // The same mission loaded again (a reload, or a title that loads its first mission twice) keeps what was selected.
        string? previous = _loadedFor is { } was && Document is { } old ? ObjectsGamePath(was) : null;
        List<int[]> kept = previous is not null && mission is not null && previous == ObjectsGamePath(mission)
            ? [.. Selection.Items.Select(task => PathOf(Document!, task))]
            : [];
        _loadedFor = mission;
        _missionName = mission is null ? string.Empty : _host.MissionName(mission);
        _taskCount = null;
        Selection.Clear();
        History.Clear();
        Graphs.Clear();
        Document = null;
        Baseline.Capture(null);
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
            Baseline.Capture(Document);
            Selection.Set([.. kept.Select(path => AtPath(Document, path)).OfType<MissionTask>()]);
            _host.Logger.Information("Editor", $"Opened {_host.MissionName(mission)}: {Document.EnumerateTasks().Count()} tasks.");
        }
        catch (Exception error) when (error is IOException or Scripting.Qvm.QvmFormatException or Scripting.Qsc.QscDecompileException)
        {
            LoadError = error.Message;
            _host.Logger.Warning("Editor", $"Could not open {_host.MissionName(mission)}'s objects: {error.Message}");
        }

        Changed?.Invoke();
    }

    // A task's place in the tree: its root's statement index, then each child index down to it.
    private static int[] PathOf(MissionDocument document, MissionTask task)
    {
        List<int> path = [];
        MissionTask at = task;
        while (at.Parent is { } parent)
        {
            path.Add(parent.Children.IndexOf(at));
            at = parent;
        }

        path.Add(document.Locate(at).Index);
        path.Reverse();
        return [.. path];
    }

    private static MissionTask? AtPath(MissionDocument document, int[] path)
    {
        if (path.Length == 0 || path[0] < 0 || path[0] >= document.Statements.Count
            || document.Statements[path[0]] is not MissionTaskStatement { Task: var task })
        {
            return null;
        }

        foreach (int index in path.Skip(1))
        {
            if (index < 0 || index >= task.Children.Count)
            {
                return null;
            }

            task = task.Children[index];
        }

        return task;
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
        Graphs.Save(this);
        SaveTerrain(mission);
        History.MarkSaved();
        Baseline.Capture(document);
        return true;
    }

    // The ground's changed files, rebuilt from the copies the game reads now (the project's own, once it has saved one).
    private void SaveTerrain(MissionDescriptor mission)
    {
        if (_host.Terrain is not { } terrain)
        {
            return;
        }

        foreach ((string path, byte[] bytes) in terrain.ChangedFiles(
            path => _host.TryReadFile("LOCAL:" + GamePath(mission, path), out ReadOnlyMemory<byte> found) ? found : null))
        {
            WriteToProject(GamePath(mission, path), bytes);
        }

        terrain.MarkSaved();
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
    public static string ObjectsGamePath(MissionDescriptor mission) => GamePath(mission, LevelLoader.ObjectsScriptName);

    /// <summary>Gets the game path of a file in a mission's folder, relative to the installation.</summary>
    /// <param name="mission">The mission.</param>
    /// <param name="missionPath">The file's path relative to the mission's folder.</param>
    /// <returns>The path.</returns>
    public static string GamePath(MissionDescriptor mission, string missionPath) =>
        $"{mission.MissionDirectory.TrimEnd('/', '\\')}/{missionPath}";

    /// <summary>Applies a command through the history.</summary>
    /// <param name="command">The command.</param>
    public void Execute(IEditorCommand command) => History.Execute(command);

    private void OnDocumentChanged(MissionChange change)
    {
        _taskCount = null;
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

    /// <summary>
    /// Gets the parameter that orients a task, if it has one: the three engine Euler angles, or for a type that only turns
    /// about the up axis (I.G.I. 2's cabinets), its one heading.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <returns>The orientation parameter, or <see langword="null"/>.</returns>
    public static MissionParameter? Orientation(MissionTask task) =>
        task.FindParameter(ScriptParamType.Real32x9) ?? (task.FindParameter("Orientation") is { Declaration.Type: ScriptParamType.Angle } heading ? heading : null);

    /// <summary>Gets whether a task's orientation is a heading alone, turning about the up axis and nothing else.</summary>
    /// <param name="task">The task.</param>
    /// <returns><see langword="true"/> for a heading.</returns>
    public static bool TurnsAboutUpOnly(MissionTask task) => Orientation(task) is { Declaration.Type: ScriptParamType.Angle };

    /// <summary>Gets a task's orientation as a rotation, identity when it has none.</summary>
    /// <param name="task">The task.</param>
    /// <returns>The rotation, composed as the engine composes it.</returns>
    public static Matrix3f OrientationMatrix(MissionTask task)
    {
        if (Orientation(task) is not { } orientation || !orientation.TryGetNumbers(out double[] angles))
        {
            return Matrix3f.Identity;
        }

        return angles.Length switch
        {
            3 => Matrix3f.FromEngineEulerAngles((float)angles[0], (float)angles[1], (float)angles[2]),
            1 => Matrix3f.FromEngineEulerAngles(0f, 0f, (float)angles[0]),
            _ => Matrix3f.Identity,
        };
    }

    private static bool Has(string type, string part) => type.Contains(part, StringComparison.OrdinalIgnoreCase);
}
