using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// One magic object type, <c>MagicObjType_s</c>: what an attach point of that name becomes, the model it wears and the task type it is
/// made as, and the tag items its type reads its settings from.
/// </summary>
/// <param name="Name">The attach point's name.</param>
/// <param name="Model">The model it wears; <c>none</c> for a point that is only a mark.</param>
/// <param name="TaskType">The name of the task type it is made as.</param>
/// <param name="Settings">The tag items under its config, which its type reads (<c>MagicObjUpdateTagItems</c>).</param>
public sealed record Igi2MagicObjType(string Name, string Model, string TaskType, IReadOnlyList<GameTask> Settings);

/// <summary>
/// The magic object types, <c>MagicObj_atMagicObjType</c>, as <c>magicobjconfig.qvm</c> defines them at start-up through its
/// <c>MagicObjConfig</c> tasks (<c>MagicObjConfigContainer_Load</c>).
/// </summary>
/// <remarks>
/// <para>
/// <c>ModelObj_CreateAttachObject</c> looks each of a model's attach points up by name, exactly (<c>MagicObj_GetMagicObjType</c>,
/// a <c>strcmp</c> over the table), and makes the type's task wearing the type's model. A name the table does not hold is a model of
/// its own, made as a <c>RigidMagicObj</c>.
/// </para>
/// <para>
/// A <c>ShadowVolume</c> is no model: it is drawn into the shadow-poly group and stood on by nothing. A model named <c>none</c> is
/// a mark that wears nothing.
/// </para>
/// </remarks>
public sealed class Igi2MagicObjTypes
{
    /// <summary>The script that defines the table.</summary>
    public const string Script = "LOCAL:magicobjconfig.qvm";

    /// <summary>The type an attach point the table does not hold is made as.</summary>
    public const string RigidType = "RigidMagicObj";

    /// <summary>The type that is drawn only as a shadow.</summary>
    public const string ShadowVolumeType = "ShadowVolume";

    /// <summary>The model a mark wears.</summary>
    public const string NoModel = "none";

    private readonly Dictionary<string, Igi2MagicObjType> _types = new(StringComparer.Ordinal);

    /// <summary>Gets how many types the table holds.</summary>
    public int Count => _types.Count;

    /// <summary>
    /// Defines a type, replacing one of the same name, as a config's <c>UpdateInternalData</c> does (<c>MagicObjConfig_AllocMagicObj</c>);
    /// a config with no name defines nothing.
    /// </summary>
    /// <param name="type">The type.</param>
    public void Define(Igi2MagicObjType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.Name.Length > 0)
        {
            _types[type.Name] = type;
        }
    }

    /// <summary>Finds the type an attach point of a name becomes.</summary>
    /// <param name="name">The attach point's name.</param>
    /// <returns>The type, or <see langword="null"/> when the name is a model of its own.</returns>
    public Igi2MagicObjType? Find(string name) => _types.GetValueOrDefault(name);

    /// <summary>
    /// The model an attach point is drawn and stood on as: its type's model, or its own name when the table does not hold it; or
    /// <see langword="null"/> for a shadow volume or a mark.
    /// </summary>
    /// <param name="name">The attach point's name.</param>
    /// <returns>The model, or <see langword="null"/>.</returns>
    public string? ModelOf(string name)
    {
        if (Find(name) is not { } type)
        {
            return name;
        }

        return type.TaskType == ShadowVolumeType || string.Equals(type.Model, NoModel, StringComparison.Ordinal) ? null : type.Model;
    }

    /// <summary>Runs the script and defines a type of every config it makes, in the order made.</summary>
    /// <param name="resources">Where the script is read from.</param>
    /// <param name="natives">The script namespace.</param>
    /// <param name="tree">The task tree the configs are made into.</param>
    public void Load(ResourceRegistry resources, NativeRegistry natives, TaskTree tree)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(tree);
        // MagicObjConfigContainer_Load loads it as a level would: a game without it has an empty table.
        if (!resources.TryLoad(Script, out _))
        {
            return;
        }

        Igi2SessionScript.Run(resources, natives, Script);
        foreach (Igi2MagicObjConfigTask config in Igi2TaskTree.Descendants(tree.Root).OfType<Igi2MagicObjConfigTask>())
        {
            Define(config.ToType(tree.Types));
        }
    }
}

/// <summary>The container <c>magicobjconfig.qvm</c> makes its configs in, <c>MagicObjConfigContainer</c>.</summary>
public sealed partial class Igi2MagicObjConfigContainerTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "MagicObjConfigContainer";
}

/// <summary>One magic object type's definition, <c>MagicObjConfig</c> (magicobjconfig.c); its tag items are its type's settings.</summary>
public sealed partial class Igi2MagicObjConfigTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "MagicObjConfig";

    /// <summary>Gets or sets the attach point's name.</summary>
    [ScriptParam(ScriptParamType.String16, "Name")]
    public string TypeNameValue { get; set; } = string.Empty;

    /// <summary>Gets or sets the model it wears.</summary>
    [ScriptParam(ScriptParamType.String16, "Model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets the id of the task type it is made as, a <c>TASKTYPE_*</c> value.</summary>
    [ScriptParam(ScriptParamType.EnumInt32, "TaskType")]
    public int TaskType { get; set; }

    /// <summary>The type it defines.</summary>
    /// <param name="types">The task-type registry its id is resolved in.</param>
    /// <returns>The type.</returns>
    public Igi2MagicObjType ToType(TaskTypeRegistry types)
    {
        ArgumentNullException.ThrowIfNull(types);
        string type = (uint)TaskType < (uint)types.Types.Count ? types.Types[TaskType].Name : Igi2MagicObjTypes.RigidType;
        return new Igi2MagicObjType(TypeNameValue, Model, type, [.. Children]);
    }
}
