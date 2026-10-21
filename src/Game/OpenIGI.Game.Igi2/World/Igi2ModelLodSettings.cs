using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// The distances each model switches its levels of detail at, <c>LODVirModel_s.avLODDistance</c> with <c>isLODDistanceSpecified</c>, as
/// the model LOD settings set them: the game's own file at start-up, and a level's over it.
/// </summary>
public sealed class Igi2ModelLodTable
{
    /// <summary>The game's file, which the game's container loads as it is made (<c>ModelLODSettingsContainer_CreateHandler</c>).</summary>
    public const string GameFile = "LOCAL:lod.qsc";

    private readonly Dictionary<string, float[]> _distances = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, float[]> _game = new Dictionary<string, float[]>();

    /// <summary>Notes the distances as they stand as the game's own, which each level starts from.</summary>
    public void KeepAsGame() => _game = Snapshot();

    /// <summary>Puts the game's own distances back, as a level starts.</summary>
    public void BeginLevel() => Reset(_game);

    /// <summary>Gets the models with distances of their own.</summary>
    public int Count => _distances.Count;

    /// <summary>Gets a model's distances, in world units: to its second to fifth levels and its cutoff.</summary>
    /// <param name="model">The model.</param>
    /// <param name="distances">Receives its distances.</param>
    /// <returns>Whether it has distances of its own.</returns>
    public bool TryGet(string model, out IReadOnlyList<float> distances)
    {
        bool found = _distances.TryGetValue(model, out float[]? stored);
        distances = stored ?? [];
        return found;
    }

    /// <summary>Gives a model its distances.</summary>
    /// <param name="model">The model.</param>
    /// <param name="distances">Its distances, in world units.</param>
    public void Set(string model, float[] distances)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(distances);
        _distances[model] = distances;
    }

    /// <summary>Forgets the distances a level gave, keeping the game's.</summary>
    /// <param name="game">The game's distances, as they stood after start-up.</param>
    public void Reset(IReadOnlyDictionary<string, float[]> game)
    {
        ArgumentNullException.ThrowIfNull(game);
        _distances.Clear();
        foreach ((string model, float[] distances) in game)
        {
            _distances[model] = distances;
        }
    }

    /// <summary>Takes a copy of the distances as they stand.</summary>
    /// <returns>The copy.</returns>
    public IReadOnlyDictionary<string, float[]> Snapshot() => new Dictionary<string, float[]>(_distances, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Loads a settings file into a container, <c>ModelLODSettingsContainer_LoadSettings</c>: its <c>Task_New</c> calls made under the
    /// container (<c>Script_SetSymbolContext</c>), then each setting made applied. A file that is not there loads nothing.
    /// </summary>
    /// <param name="container">The container.</param>
    /// <param name="path">The file, a <c>.qsc</c> read from its compiled <c>.qvm</c>.</param>
    /// <param name="resources">Where the file is read from.</param>
    /// <param name="natives">The script namespace, with <c>Task_New</c>.</param>
    public void Load(GameTask container, string path, ResourceRegistry resources, NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(natives);
        string compiled = path.EndsWith(".qsc", StringComparison.OrdinalIgnoreCase) ? path[..^4] + ".qvm" : path;
        if (!resources.TryLoad(compiled, out _) || !natives.TryResolve(Engine.Tasks.TaskScriptCommands.TaskNew, out QvmSymbol? taskNew))
        {
            return;
        }

        object? previous = taskNew.Context;
        natives.SetSymbolContext(Engine.Tasks.TaskScriptCommands.TaskNew, container);
        try
        {
            Igi2SessionScript.Run(resources, natives, compiled);
        }
        finally
        {
            natives.SetSymbolContext(Engine.Tasks.TaskScriptCommands.TaskNew, previous);
        }

        foreach (Igi2ModelLodSettingsTask setting in container.Children.OfType<Igi2ModelLodSettingsTask>())
        {
            setting.Apply(this);
        }
    }
}

/// <summary>A container of model LOD settings, I.G.I. 2's <c>ModelLODSettingsContainer</c> (lodsettings.c), the game's own.</summary>
public partial class Igi2ModelLodSettingsContainerTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "ModelLODSettingsContainer";
}

/// <summary>
/// A level's container of model LOD settings, I.G.I. 2's <c>LocalModelLODSettingsContainer</c> (lodsettings.c): once the level is loaded
/// it loads the file at its path into itself (<c>LocalModelLODSettingsContainer_UpdateInternalDataAfterLoadHandler</c>), its settings over
/// the game's.
/// </summary>
public sealed partial class Igi2LocalModelLodSettingsContainerTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "LocalModelLODSettingsContainer";

    /// <summary>Gets or sets the settings file.</summary>
    [ScriptParam(ScriptParamType.String256, "Path")]
    public string Path { get; set; } = string.Empty;
}

/// <summary>
/// One model's LOD distances, I.G.I. 2's <c>ModelLODSettings</c> (lodsettings.c): in metres, to its second to fifth levels and its cutoff.
/// </summary>
/// <remarks>
/// <c>ModelLODSettings_UpdateInternalDataHandler</c>: the model's name loses its trailing spaces; a setting for a model another of its
/// container already sets is left out; each distance is made no less than the one before it, and the model takes them in world units.
/// </remarks>
public sealed partial class Igi2ModelLodSettingsTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "ModelLODSettings";

    /// <summary>Gets or sets the model.</summary>
    [ScriptParam(ScriptParamType.String16, "Model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets the distance to its second level.</summary>
    [ScriptParam(ScriptParamType.Real32, "Distance to LOD 2")]
    public float ToLod2 { get; set; }

    /// <summary>Gets or sets the distance to its third level.</summary>
    [ScriptParam(ScriptParamType.Real32, "Distance to LOD 3")]
    public float ToLod3 { get; set; }

    /// <summary>Gets or sets the distance to its fourth level.</summary>
    [ScriptParam(ScriptParamType.Real32, "Distance to LOD 4")]
    public float ToLod4 { get; set; }

    /// <summary>Gets or sets the distance to its fifth level.</summary>
    [ScriptParam(ScriptParamType.Real32, "Distance to LOD 5")]
    public float ToLod5 { get; set; }

    /// <summary>Gets or sets the distance it is no longer drawn at.</summary>
    [ScriptParam(ScriptParamType.Real32, "Distance to cutoff")]
    public float ToCutoff { get; set; }

    /// <summary>Gives the model its distances, unless another setting of its container sets that model.</summary>
    /// <param name="table">The table.</param>
    public void Apply(Igi2ModelLodTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        string model = Model.TrimEnd(' ');
        Model = model;
        if (model.Length > 0 && Parent is { } container
            && container.Children.OfType<Igi2ModelLodSettingsTask>().Any(other =>
                !ReferenceEquals(other, this) && string.Equals(other.Model.TrimEnd(' '), model, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        ToLod3 = Math.Max(ToLod3, ToLod2);
        ToLod4 = Math.Max(ToLod4, ToLod3);
        ToLod5 = Math.Max(ToLod5, ToLod4);
        ToCutoff = Math.Max(ToCutoff, ToLod5);
        if (model.Length > 0)
        {
            table.Set(model, [((Fpu)ToLod2 * 4096.0).F, ((Fpu)ToLod3 * 4096.0).F, ((Fpu)ToLod4 * 4096.0).F, ((Fpu)ToLod5 * 4096.0).F, ((Fpu)ToCutoff * 4096.0).F]);
        }
    }
}
