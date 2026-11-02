using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Collections;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
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

    public MissionWorkspace(IEditorHost host)
    {
        _host = host;
        History.Changed += () => Changed?.Invoke();
    }

    /// <summary>Raised when the document is replaced or edited, or the history moves.</summary>
    public event Action? Changed;

    /// <summary>Gets the open document, or <see langword="null"/> before a mission is open.</summary>
    public MissionDocument? Document { get; private set; }

    /// <summary>Gets the mission the document belongs to.</summary>
    public MissionDescriptor? Mission => Document is null ? null : _loadedFor;

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
            byte[] image = _host.Files.ReadAllBytes(VirtualFileSystem.MissionAlias + LevelLoader.ObjectsScriptName);
            Document = MissionDocument.Read(image);
            Document.Changed += OnDocumentChanged;
            _host.Logger.Information("Editor", $"Opened {mission.Name}: {Document.EnumerateTasks().Count()} tasks.");
        }
        catch (Exception error) when (error is IOException or Scripting.Qvm.QvmFormatException or Scripting.Qsc.QscDecompileException)
        {
            LoadError = error.Message;
            _host.Logger.Warning("Editor", $"Could not open {mission.Name}'s objects: {error.Message}");
        }

        Changed?.Invoke();
    }

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

    private static bool Has(string type, string part) => type.Contains(part, StringComparison.OrdinalIgnoreCase);
}
