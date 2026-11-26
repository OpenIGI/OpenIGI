using OpenIGI.Core.Diagnostics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Cutscenes;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Host.Editor;

namespace OpenIGI.Editor;

/// <summary>
/// The open mission's cutscene rigs: their settings read from the project's sidecar as the mission opens, changed only through the
/// history (so an undo puts a rig back with its keys), and saved beside the mission when it is -- a rig whose keys are gone left out.
/// </summary>
internal sealed class CutsceneRigStore
{
    private readonly MissionWorkspace _workspace;
    private readonly IEditorHost _host;
    private List<CutsceneRig> _rigs = [];
    private MissionDocument? _readFor;

    /// <summary>Initializes a new instance of the <see cref="CutsceneRigStore"/> class.</summary>
    /// <param name="workspace">The open mission.</param>
    /// <param name="host">The editor host, for its log.</param>
    public CutsceneRigStore(MissionWorkspace workspace, IEditorHost host)
    {
        _workspace = workspace;
        _host = host;
        workspace.Saving += Save;
    }

    /// <summary>The rig a key of a scene belongs to, or null.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="key">The key.</param>
    /// <returns>The rig.</returns>
    public CutsceneRig? Of(MissionTask scene, MissionTask key)
    {
        Load();
        return key.Label.Length == 0 ? null : _rigs.FirstOrDefault(r => r.SceneId == scene.Id && r.Name == key.Label);
    }

    /// <summary>A rig of a scene by its name, or null.</summary>
    /// <param name="sceneId">The scene's id.</param>
    /// <param name="name">The rig's name.</param>
    /// <returns>The rig as kept.</returns>
    public CutsceneRig? Find(int sceneId, string name)
    {
        Load();
        return _rigs.FirstOrDefault(r => r.SceneId == sceneId && r.Name == name);
    }

    /// <summary>The step that changes a rig's settings as kept (a new one added, one dropped), for the history beside its keys' edit.</summary>
    /// <param name="before">The rig as kept now, or null for a new one.</param>
    /// <param name="after">The rig as it is to be kept, or null to drop it.</param>
    /// <returns>The step.</returns>
    public IEditorCommand Change(CutsceneRig? before, CutsceneRig? after) =>
        new DelegateCommand("Rig", () => Swap(before, after), () => Swap(after, before));

    private void Swap(CutsceneRig? from, CutsceneRig? to)
    {
        Load();
        if (from is not null)
        {
            _rigs.RemoveAll(r => r.SceneId == from.SceneId && r.Name == from.Name);
        }

        if (to is not null)
        {
            _rigs.RemoveAll(r => r.SceneId == to.SceneId && r.Name == to.Name);
            _rigs.Add(to);
        }
    }

    // The sidecar read once a mission is open, afresh for each mission.
    private void Load()
    {
        if (ReferenceEquals(_readFor, _workspace.Document))
        {
            return;
        }

        _readFor = _workspace.Document;
        _rigs = [];
        if (_workspace.Mission is not { } mission || _workspace.Project.ReadRigs(MissionWorkspace.ObjectsGamePath(mission)) is not { } bytes)
        {
            return;
        }

        try
        {
            _rigs = [.. CutsceneRigs.Read(bytes)];
        }
        catch (System.Text.Json.JsonException error)
        {
            _host.Logger.Warning("Editor", $"The cutscene rigs could not be read, so their keys are plain keys: {error.Message}");
        }
    }

    private void Save()
    {
        Load();
        if (_workspace.Mission is not { } mission || _workspace.Document is not { } document)
        {
            return;
        }

        string gamePath = MissionWorkspace.ObjectsGamePath(mission);
        Dictionary<int, MissionTask> scenes = CutsceneModel.Scenes(document).Where(static s => s.Id >= 0).ToDictionary(static s => s.Id);
        List<CutsceneRig> kept = [.. _rigs.Where(r => scenes.TryGetValue(r.SceneId, out MissionTask? scene) && CutsceneRigs.KeysOf(scene, r.Name).Count > 0)];
        if (kept.Count > 0 || _workspace.Project.ReadRigs(gamePath) is not null)
        {
            _workspace.Project.WriteRigs(gamePath, CutsceneRigs.Write(kept));
        }
    }
}
