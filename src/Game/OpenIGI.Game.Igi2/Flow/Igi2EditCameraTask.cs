using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>One camera of a cutscene, I.G.I. 2's <c>EditCamera</c>: where it looks from, and for how long.</summary>
/// <remarks>
/// A cutscene's cameras are its <c>EditCamera</c> children and theirs, taken one after another, each for its
/// <see cref="Duration"/> scaled by the cutscene's time scale (<c>CutScene_RegisterCutSceneCamera</c>, <c>igi2 0x438D50</c>).
/// </remarks>
public sealed partial class Igi2EditCameraTask : GameTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "EditCamera";

    /// <summary>Gets or sets where the camera is.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets its pitch.</summary>
    [ScriptParam(ScriptParamType.Angle, "Alpha")]
    public float Alpha { get; set; }

    /// <summary>Gets or sets its roll.</summary>
    [ScriptParam(ScriptParamType.Angle, "Beta")]
    public float Beta { get; set; }

    /// <summary>Gets or sets its heading.</summary>
    [ScriptParam(ScriptParamType.Angle, "Gamma")]
    public float Gamma { get; set; }

    /// <summary>Gets or sets its field of view.</summary>
    [ScriptParam(ScriptParamType.Real32, "FOV")]
    public float FieldOfView { get; set; }

    /// <summary>Gets or sets how long it is held, in seconds, <c>vDuration</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Duration")]
    public float Duration { get; set; }

    /// <summary>Gets or sets the task it rides with, or -1.</summary>
    [ScriptParam(ScriptParamType.Int32, "Link task ID")]
    public int LinkTaskId { get; set; }

    /// <summary>Gets or sets a value indicating whether it follows the link each tick.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Update link continously")]
    public bool UpdateLinkContinuously { get; set; }

    /// <summary>Gets or sets the task it looks at, or -1.</summary>
    [ScriptParam(ScriptParamType.Int32, "Target task ID")]
    public int TargetTaskId { get; set; }

    /// <summary>Gets or sets a value indicating whether it follows the target each tick.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Update target continously")]
    public bool UpdateTargetContinuously { get; set; }

    /// <summary>Gets or sets a value indicating whether it blends into the next camera, <c>isSmoothToNext</c>.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Smooth to next")]
    public bool SmoothToNext { get; set; }

    /// <summary>Gets or sets the time of day it shows, or -1 for the level's.</summary>
    [ScriptParam(ScriptParamType.Real32, "Time of day (-1 means use default)")]
    public float TimeOfDay { get; set; }

    /// <summary>Gets or sets the filter over the picture.</summary>
    [ScriptParam(ScriptParamType.EnumString32, "FILTER")]
    public string Filter { get; set; } = string.Empty;

    /// <summary>Gets or sets the picture's noise.</summary>
    [ScriptParam(ScriptParamType.Real32, "Noise")]
    public float Noise { get; set; }

    /// <summary>Gets or sets the filter's colour.</summary>
    [ScriptParam(ScriptParamType.Rgb, "Filter color")]
    public Vector3f FilterColour { get; set; }

    /// <summary>Gets or sets how much the camera shakes.</summary>
    [ScriptParam(ScriptParamType.Real32, "Camera shake")]
    public float CameraShake { get; set; }
}
