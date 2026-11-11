using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Level;

namespace OpenIGI.Game.World;

/// <summary>
/// A message a level sends the player when its script holds: Project I.G.I.'s <c>StatusMessage</c>, exporting
/// <c>isSendt</c>, <c>nTickSendt</c>, <c>isFinishedDisplay</c>, <c>nFinishedDisplay</c> and <c>nTicksSinceFinishedDisplay</c>.
/// </summary>
/// <remarks>
/// <para>
/// The first game splits the work between two types: this one decides when a message is sent (<c>StatusMessage_Run</c>,
/// <c>0x4855F0</c>) and hands it to the status screen's message list, which types it out, holds it and lets it go
/// (<c>StatusMessageScreen</c>, <c>0x486650</c>). <see cref="MissionStatusMessages"/> is that list. I.G.I. 2 types the text
/// in the message's own task, which is why the two titles do not share this class.
/// </para>
/// <para>
/// The parameter list is <c>StatusMessage_Init</c>'s. The exported fields are the message's own, bound when the level is
/// built, long before there is a screen to send it to.
/// </para>
/// </remarks>
public sealed partial class Igi1StatusMessageTask : TickedTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "StatusMessage";

    private StatusMessage? _message;

    /// <summary>Gets or sets where the editor shows it.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets its orientation, which nothing reads.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets the script that sends it.</summary>
    [ScriptParam(ScriptParamType.VarString, "Send")]
    public string SendScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the resource of the text it shows.</summary>
    [ScriptParam(ScriptParamType.VarString, "Text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>Gets or sets the portrait drawn beside it, by name.</summary>
    [ScriptParam(ScriptParamType.String256, "Sprite")]
    public string Sprite { get; set; } = string.Empty;

    /// <summary>Gets or sets the sound played as it goes up.</summary>
    [ScriptParam(ScriptParamType.String16, "Sound")]
    public string Sound { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether it is sent only once.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Is send once")]
    public bool IsSendOnce { get; set; }

    /// <summary>Gets or sets a value indicating whether it is a cutscene's subtitle: whole at once, and kept out of the computer's log.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Cutscene message")]
    public bool IsCutsceneMessage { get; set; }

    /// <summary>Gets or sets how long it holds once typed out, in seconds.</summary>
    [ScriptParam(ScriptParamType.Real32, "Duration")]
    public float Duration { get; set; } = 2f;

    /// <summary>Gets the message's send latch and what the screen shows of it, made from the parameters the first time it is asked for.</summary>
    internal StatusMessage Message => _message ??= new StatusMessage(
        this, SendScript.Trim(), Text.Trim(), IsSendOnce, IsCutsceneMessage, Duration)
    {
        Sound = Sound.Trim(),
    };

    /// <summary>Gets or sets the screen's message list, which sends the message; none until the mission's is built.</summary>
    internal MissionStatusMessages? Screen { get; set; }

    /// <inheritdoc/>
    /// <remarks>
    /// The original's handler runs no children. The walk this replaced ran them, and no census of the levels has yet said none
    /// hangs a task beneath a message, so they are still run.
    /// </remarks>
    public override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        Screen?.RunFromWalk(Message);
        level.RunChildren(this);
    }

    /// <inheritdoc/>
    /// <remarks><c>StatusMessage_Activate</c> (<c>0x485770</c>): five fields, which a level's flow reads to finish a mission.</remarks>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindBool(this, "isSendt", () => Message.IsSent, value => Message.IsSent = value);
        variables.BindInt(this, "nTickSendt", () => Message.SentTick, value => Message.SentTick = value);
        variables.BindBool(this, "isFinishedDisplay", () => Message.IsFinishedDisplay, value => Message.IsFinishedDisplay = value);
        variables.BindInt(this, "nFinishedDisplay", () => Message.FinishedDisplayTick, value => Message.FinishedDisplayTick = value);
        variables.BindInt(
            this, "nTicksSinceFinishedDisplay", () => Message.TicksSinceFinishedDisplay, value => Message.TicksSinceFinishedDisplay = value);
    }
}
