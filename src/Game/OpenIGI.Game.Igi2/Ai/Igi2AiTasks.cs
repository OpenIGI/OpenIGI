using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Ai;

/// <summary>An enemy soldier's body as level data places it, I.G.I. 2's <c>HumanSoldier</c>.</summary>
/// <remarks>Its parameters are the <c>Human</c> ones it inherits; its <c>HumanAI</c> is a child.</remarks>
public sealed partial class Igi2HumanSoldierTask : GameTask, IIgi2PlacedTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "HumanSoldier";

    /// <summary>Gets or sets where the soldier stands.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets the heading the soldier faces.</summary>
    [ScriptParam(ScriptParamType.Angle, "Gamma")]
    public float Gamma { get; set; }

    /// <summary>Gets or sets the character the soldier is drawn as.</summary>
    [ScriptParam(ScriptParamType.String16, "Model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets the side the soldier is on.</summary>
    [ScriptParam(ScriptParamType.Int32, "Team")]
    public int Team { get; set; }

    /// <summary>Gets or sets the script that arms the soldier.</summary>
    [ScriptParam(ScriptParamType.VarString, "Weapon")]
    public string WeaponScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the skeleton the soldier is animated on.</summary>
    [ScriptParam(ScriptParamType.Int32, "Bone Heirachy")]
    public int BoneHierarchy { get; set; }

    /// <summary>Gets or sets the animation the soldier stands in.</summary>
    [ScriptParam(ScriptParamType.Int32, "Stand Animation")]
    public int StandAnimation { get; set; }

    /// <summary>Gets the soldier's mind, its <c>HumanAI</c> child.</summary>
    public Igi2HumanAiTask? Ai => Children.OfType<Igi2HumanAiTask>().FirstOrDefault();

    /// <inheritdoc/>
    public bool TryGetValue(string name, out object? value)
    {
        value = name switch
        {
            "Position" => Position,
            "Gamma" => Gamma,
            "Model" => Model,
            "Team" => Team,
            "Weapon" => WeaponScript,
            "Bone Heirachy" => BoneHierarchy,
            "Stand Animation" => StandAnimation,
            _ => null,
        };
        return value is not null;
    }
}

/// <summary>An enemy soldier's mind, I.G.I. 2's <c>HumanAI</c>, a child of its <c>HumanSoldier</c>.</summary>
/// <remarks>
/// Made as a rifleman of the first type with no graph (<c>HumanAI_CreateHandler</c>, <c>igi2 0x45DB40</c>). Its types are read
/// from their names as one-line scripts and its own script is <c>MISSION:AI/&lt;id&gt;.qsc</c>
/// (<c>HumanAI_UpdateInternalDataHandler</c>, <c>0x460030</c>).
/// </remarks>
public sealed partial class Igi2HumanAiTask : GameTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "HumanAI";

    /// <summary>Gets or sets the soldier type, a <c>HUMANAI_TYPE_*</c> name.</summary>
    [ScriptParam(ScriptParamType.String32, "AI Type")]
    public string AiTypeName { get; set; } = Igi2AiConstants.AiTypes[0];

    /// <summary>Gets or sets the animation set, a <c>HUMANAI_ANIMTYPE_*</c> name.</summary>
    [ScriptParam(ScriptParamType.String32, "Anim Type")]
    public string AnimTypeName { get; set; } = Igi2AiConstants.AnimTypes[2];

    /// <summary>Gets or sets the task id of the graph the soldier walks, or -1 for none.</summary>
    [ScriptParam(ScriptParamType.Int32, "Graph ID")]
    public int GraphId { get; set; } = -1;

    /// <summary>Gets the soldier type, <c>nAIType</c>, once resolved.</summary>
    public int AiType { get; private set; }

    /// <summary>Gets the animation set, <c>nAnimType</c>, once resolved.</summary>
    public int AnimType { get; private set; } = 2;

    /// <summary>Gets the script the mind's events run, by its task id.</summary>
    public string ScriptName => ScriptNameFor(Id);

    /// <summary>The script a soldier's events run, <c>MISSION:AI/&lt;id&gt;.qsc</c>.</summary>
    /// <param name="taskId">The <c>HumanAI</c>'s level id.</param>
    /// <returns>The resource name.</returns>
    public static string ScriptNameFor(int taskId) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"MISSION:AI/{taskId}.qsc");

    /// <summary>Reads the types from their names, as one-line scripts.</summary>
    /// <param name="natives">The script namespace, with the AI's constants.</param>
    /// <param name="functions">The natives script text may call.</param>
    public void ResolveTypes(NativeRegistry natives, Igi2ScriptFunctions functions)
    {
        AiType = Igi2LineScript.Evaluate(AiTypeName, natives, functions);
        AnimType = Igi2LineScript.Evaluate(AnimTypeName, natives, functions);
    }
}

/// <summary>A squad of soldiers, I.G.I. 2's <c>AISquad</c>: the soldiers under it act together.</summary>
/// <remarks>
/// <c>AISquad_CreateHandler</c> (<c>igi2 0x4989B0</c>) makes it of type 1 with no alarm or gun, five seconds to lose a target,
/// sixty to calm down, two metres between soldiers and thirty to run to an alarm. Its formation distance is held to [0, 10]
/// and its script is <c>MISSION:AI/Squad_&lt;id&gt;.qsc</c> (<c>AISquad_UpdateInternalDataHandler</c>, <c>0x4990E0</c>).
/// </remarks>
public sealed partial class Igi2AiSquadTask : GameTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "AISquad";

    /// <summary>Gets or sets the distance between the squad's soldiers, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "Formation Distance")]
    public float FormationDistance { get; set; } = 2f;

    /// <summary>Gets or sets the kind of squad.</summary>
    [ScriptParam(ScriptParamType.EnumInt32, "SquadType")]
    public int SquadType { get; set; } = 1;

    /// <summary>Gets or sets the task id of the alarm the squad raises, or -1.</summary>
    [ScriptParam(ScriptParamType.Int32, "AlarmTriggerID")]
    public int AlarmTriggerId { get; set; } = -1;

    /// <summary>Gets or sets the task id of the alarm terminal the squad runs to, or -1.</summary>
    [ScriptParam(ScriptParamType.Int32, "AlarmControlID")]
    public int AlarmControlId { get; set; } = -1;

    /// <summary>Gets or sets the task id of the mounted gun the squad mans, or -1.</summary>
    [ScriptParam(ScriptParamType.Int32, "StationaryGunID")]
    public int StationaryGunId { get; set; } = -1;

    /// <summary>Gets or sets how far a soldier runs to the alarm or the gun, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "Max Run Distance Alarm/Gun (m)")]
    public float MaxRunDistance { get; set; } = 30f;

    /// <summary>Gets or sets the seconds before a lost target is given up.</summary>
    [ScriptParam(ScriptParamType.Int32, "TargetTimeout (sec)")]
    public int TargetTimeout { get; set; } = 5;

    /// <summary>Gets or sets the seconds before the squad calms down.</summary>
    [ScriptParam(ScriptParamType.Int32, "TenseTimeout (sec)")]
    public int TenseTimeout { get; set; } = 60;

    /// <summary>Gets the script the squad's events run, by its task id.</summary>
    public string ScriptName => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"MISSION:AI/Squad_{Id}.qsc");

    /// <summary>Gets the squad's soldiers, the <c>HumanSoldier</c> tasks under it, in order.</summary>
    public IEnumerable<Igi2HumanSoldierTask> Soldiers => Igi2TaskTree.Descendants(this).OfType<Igi2HumanSoldierTask>();

    /// <inheritdoc/>
    protected override void OnParsed()
    {
        float distance = FormationDistance;
        FormationDistance = distance >= 0f ? (distance <= 10f ? distance : 10f) : 0f;
    }
}

/// <summary>A route a soldier patrols, I.G.I. 2's <c>PatrolPath</c>: its <c>PatrolPathCommand</c> children, in order.</summary>
/// <remarks>
/// Its length is its count of children (<c>PatrolPath_UpdateInternalDataAfterLoadHandler</c>, <c>igi2 0x51BB40</c>); a soldier
/// finds it by its task id (<c>PatrolPath_GetPatrolPathQTaskFromID</c>, <c>0x51BA00</c>).
/// </remarks>
public sealed partial class Igi2PatrolPathTask : GameTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "PatrolPath";

    /// <summary>Gets or sets the task id of the graph whose nodes the commands name.</summary>
    [ScriptParam(ScriptParamType.Int32, "Graph ID")]
    public int GraphId { get; set; }

    /// <summary>Gets the commands, in the order they run.</summary>
    public IReadOnlyList<Igi2PatrolPathCommandTask> Commands => [.. Children.OfType<Igi2PatrolPathCommandTask>()];
}

/// <summary>What a patrol command does, the <c>PATROLPATHCOMMAND_*</c> constants (<c>PatrolPath_RegisterCommands</c>, <c>igi2 0x51B9A0</c>).</summary>
public enum Igi2PatrolCommand
{
    /// <summary>Nothing.</summary>
    None = -1,

    /// <summary>Plays an animation.</summary>
    Animation = 0,

    /// <summary>Waits a number of ticks.</summary>
    Delay = 1,

    /// <summary>Walks to a node.</summary>
    WalkTo = 2,

    /// <summary>Runs to a node.</summary>
    RunTo = 3,

    /// <summary>Crouches.</summary>
    Crouch = 4,

    /// <summary>Looks at a node.</summary>
    LookAtNode = 5,

    /// <summary>Starts the path again.</summary>
    End = 6,

    /// <summary>Stops the path.</summary>
    Quit = 7,

    /// <summary>Sets the walking speed.</summary>
    SetSpeed = 8,

    /// <summary>Waits for a person.</summary>
    WaitForPerson = 9,

    /// <summary>Waits for an expression to hold.</summary>
    WaitForExpression = 10,

    /// <summary>Sets the upper body's animation.</summary>
    SetUpperBodyAnim = 11,
}

/// <summary>One step of a patrol, I.G.I. 2's <c>PatrolPathCommand</c>.</summary>
/// <remarks>
/// Made as nothing, with no parameter and no node (<c>PatrolPathCommand_CreateHandler</c>, <c>igi2 0x51B6F0</c>); a command
/// outside the known ones reads as nothing (<c>PatrolPathCommand_UpdateInternalDataHandler</c>, <c>0x51B740</c>).
/// </remarks>
public sealed partial class Igi2PatrolPathCommandTask : GameTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "PatrolPathCommand";

    /// <summary>The names of the commands, <c>PatrolPath_azCommandNames</c> (<c>igi2 0x63CDE4</c>), from <see cref="Igi2PatrolCommand.None"/>.</summary>
    public static IReadOnlyList<string> CommandNames { get; } =
    [
        "PATROLPATHCOMMAND_NONE", "PATROLPATHCOMMAND_ANIMATION", "PATROLPATHCOMMAND_DELAY", "PATROLPATHCOMMAND_WALKTO",
        "PATROLPATHCOMMAND_RUNTO", "PATROLPATHCOMMAND_CROUCH", "PATROLPATHCOMMAND_LOOKATNODE", "PATROLPATHCOMMAND_END",
        "PATROLPATHCOMMAND_QUIT", "PATROLPATHCOMMAND_SETSPEED", "PATROLPATHCOMMAND_WAIT_FOR_PERSON",
        "PATROLPATHCOMMAND_WAIT_FOR_EXPRESSION", "PATROLPATHCOMMAND_SET_UPPERBODY_ANIM",
    ];

    /// <summary>Gets or sets the command, a <see cref="Igi2PatrolCommand"/>.</summary>
    [ScriptParam(ScriptParamType.Int32, "Command")]
    public int CommandValue { get; set; } = -1;

    /// <summary>Gets or sets its number: ticks, an animation, a speed.</summary>
    [ScriptParam(ScriptParamType.Int32, "Command Parameter")]
    public int Parameter { get; set; } = -1;

    /// <summary>Gets or sets the node it names, or -1.</summary>
    [ScriptParam(ScriptParamType.Int32, "eNodeId")]
    public int NodeId { get; set; } = -1;

    /// <summary>Gets or sets the level expression it waits on.</summary>
    [ScriptParam(ScriptParamType.VarString, "Command Expression")]
    public string Expression { get; set; } = string.Empty;

    /// <summary>Gets or sets the editor's description of it.</summary>
    [ScriptParam(ScriptParamType.String32, "Command Text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>Gets the command.</summary>
    public Igi2PatrolCommand Command => (Igi2PatrolCommand)CommandValue;

    /// <summary>Registers the command names, each one less than its place.</summary>
    /// <param name="natives">The script namespace.</param>
    public static void RegisterCommands(NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(natives);
        for (int i = 0; i < CommandNames.Count; i++)
        {
            natives.RegisterConstant(CommandNames[i], i - 1);
        }
    }

    /// <inheritdoc/>
    protected override void OnParsed()
    {
        if (CommandValue < -1 || CommandValue >= 12)
        {
            CommandValue = -1;
        }
    }
}
