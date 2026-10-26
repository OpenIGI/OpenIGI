using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Igi2.Computer;

/// <summary>
/// A mark on the map computer, I.G.I. 2's <c>ComputerHilight</c> (computerhilight.c): the placed tasks it draws, or else a sprite at
/// its position, with a marker, two texts and the objective it belongs to.
/// </summary>
/// <remarks>
/// <para>
/// Its <c>TaskID</c> text names up to 64 tasks, as numbers or ranges written <c>a-b</c> (<c>ComputerHilight_ExtractTaskID</c>); its
/// marker mesh and colour are one-line scripts naming <c>MARKER_*</c> constants. <c>ComputerHilight_RunHandler</c>: while it has a
/// <c>Hilight</c> expression, it is shown as that expression's value; one with none is shown only once an objective sets its sprite
/// (<c>ComputerHilight_SetSprite</c>).
/// </para>
/// <para>The computer screen draws it (<c>ComputerHilight_DrawComputerObjectHandler</c>); that screen is not ported.</para>
/// </remarks>
public sealed partial class Igi2ComputerHilightTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "ComputerHilight";

    /// <summary>The most tasks one highlight names.</summary>
    public const int MaxTasks = OpenIGI.Game.Computer.ComputerHilightIds.MaxTasks;

    private LevelExpression? _hilight;

    /// <summary>Gets or sets where the sprite is drawn when it names no task.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets the expression it is shown under.</summary>
    [ScriptParam(ScriptParamType.VarString, "Hilight")]
    public string HilightExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets the tasks it draws, as numbers and ranges.</summary>
    [ScriptParam(ScriptParamType.String256, "TaskID")]
    public string TaskIdText { get; set; } = string.Empty;

    /// <summary>Gets or sets the computer sprite drawn when it names no task.</summary>
    [ScriptParam(ScriptParamType.DropDownCombo, "Click to select sprite")]
    public string Sprite { get; set; } = string.Empty;

    /// <summary>Gets or sets the marker mesh, a <c>MARKER_*</c> script.</summary>
    [ScriptParam(ScriptParamType.String32, "Marker mesh")]
    public string MarkerMesh { get; set; } = "MARKER_NONE";

    /// <summary>Gets or sets the marker colour, a <c>MARKER_COLOR_*</c> script.</summary>
    [ScriptParam(ScriptParamType.String32, "Marker color")]
    public string MarkerColour { get; set; } = "MARKER_COLOR_NONE";

    /// <summary>Gets or sets the title's text resource.</summary>
    [ScriptParam(ScriptParamType.String256, "Title text resource")]
    public string TitleText { get; set; } = string.Empty;

    /// <summary>Gets or sets the information's text resource.</summary>
    [ScriptParam(ScriptParamType.String256, "Info text resource")]
    public string InfoText { get; set; } = string.Empty;

    /// <summary>Gets or sets the objective it belongs to, -1 for none.</summary>
    [ScriptParam(ScriptParamType.Int32, "Objective")]
    public int Objective { get; set; } = -1;

    /// <summary>Gets a value indicating whether it is shown.</summary>
    public bool IsOn { get; private set; }

    /// <summary>Gets the tasks it names.</summary>
    public IReadOnlyList<int> TaskIds => ExtractTaskIds(TaskIdText);

    /// <summary>Gets the task an objective's position is taken from: the first it names, or -1.</summary>
    public int ObjectiveTaskId => TaskIds is [var first, ..] ? first : -1;

    /// <summary>Gets the marker mesh's value, once read.</summary>
    public int MarkerMeshValue { get; private set; } = -1;

    /// <summary>Gets the marker colour's value, once read.</summary>
    public int MarkerColourValue { get; private set; } = -1;

    /// <summary>Lists it with the computer, as its create handler does (<c>QTaskList_Register</c>).</summary>
    /// <param name="computer">The computer.</param>
    public void Attach(Igi2Computer computer)
    {
        ArgumentNullException.ThrowIfNull(computer);
        computer.Register(this);
    }

    /// <summary>The tasks a <c>TaskID</c> text names (<c>ComputerHilight_ExtractTaskID</c>), as both games read them.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The ids, at most <see cref="MaxTasks"/>.</returns>
    public static IReadOnlyList<int> ExtractTaskIds(string text) => OpenIGI.Game.Computer.ComputerHilightIds.Parse(text);

    /// <summary>Reads its marker scripts, as its update-internal-data handler does.</summary>
    /// <param name="natives">The namespace.</param>
    /// <param name="functions">The natives scripts may call.</param>
    public void ReadMarkers(OpenIGI.Scripting.Runtime.NativeRegistry natives, Igi2ScriptFunctions functions)
    {
        MarkerMeshValue = Igi2LineScript.Evaluate(MarkerMesh, natives, functions);
        MarkerColourValue = Igi2LineScript.Evaluate(MarkerColour, natives, functions);
    }

    /// <summary>Shows it with a sprite, as an objective's position does (<c>ComputerHilight_SetSprite</c>).</summary>
    /// <param name="sprite">The sprite.</param>
    public void SetSprite(string sprite)
    {
        IsOn = true;
        Sprite = sprite;
    }

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        _hilight ??= Igi2LevelExpressions.Create(HilightExpression, Name, level.Natives);
        if (!_hilight.IsEmpty)
        {
            IsOn = (long)level.Evaluate(_hilight) != 0;
        }
    }
}
