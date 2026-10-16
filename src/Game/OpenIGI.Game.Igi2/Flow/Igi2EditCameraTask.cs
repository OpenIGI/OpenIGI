using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>One camera of a cutscene, I.G.I. 2's <c>EditCamera</c>: where it looks from, and for how long.</summary>
/// <remarks>
/// A cutscene's cameras are its <c>EditCamera</c> children and theirs, taken one after another, each for its
/// <see cref="Duration"/> scaled by the cutscene's time scale (<c>CutScene_RegisterCutSceneCamera</c>, <c>igi2 0x438D50</c>).
/// </remarks>
public sealed partial class Igi2EditCameraTask : Igi2Task
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
    public float FieldOfView { get; set; } = 1f;

    /// <summary>Gets or sets how long it is held, in seconds, <c>vDuration</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Duration")]
    public float Duration { get; set; }

    /// <summary>Gets or sets the task it rides with, or -1.</summary>
    [ScriptParam(ScriptParamType.Int32, "Link task ID")]
    public int LinkTaskId { get; set; } = -1;

    /// <summary>Gets or sets a value indicating whether it follows the link each tick.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Update link continously")]
    public bool UpdateLinkContinuously { get; set; } = true;

    /// <summary>Gets or sets the task it looks at, or -1.</summary>
    [ScriptParam(ScriptParamType.Int32, "Target task ID")]
    public int TargetTaskId { get; set; } = -1;

    /// <summary>Gets or sets a value indicating whether it follows the target each tick.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Update target continously")]
    public bool UpdateTargetContinuously { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether it blends into the next camera, <c>isSmoothToNext</c>.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Smooth to next")]
    public bool SmoothToNext { get; set; } = true;

    /// <summary>Gets or sets the time of day it shows, or -1 for the level's.</summary>
    [ScriptParam(ScriptParamType.Real32, "Time of day (-1 means use default)")]
    public float TimeOfDay { get; set; } = -1f;

    /// <summary>Gets or sets the filter over the picture.</summary>
    [ScriptParam(ScriptParamType.EnumString32, "FILTER")]
    public string Filter { get; set; } = "CAMERAFILTER_TYPE_NONE";

    /// <summary>Gets or sets the picture's noise.</summary>
    [ScriptParam(ScriptParamType.Real32, "Noise")]
    public float Noise { get; set; } = 0.5f;

    /// <summary>Gets or sets the filter's colour.</summary>
    [ScriptParam(ScriptParamType.Rgb, "Filter color")]
    public Vector3f FilterColour { get; set; }

    /// <summary>Gets or sets how much the camera shakes.</summary>
    [ScriptParam(ScriptParamType.Real32, "Camera shake")]
    public float CameraShake { get; set; }

    /// <summary>Gets the filter its <see cref="Filter"/> names, <c>nFilterId</c>.</summary>
    public int FilterId { get; private set; }

    /// <inheritdoc/>
    /// <remarks><c>EditCamera_RunHandler</c> (<c>igi2 0x580BB0</c>): its children run.</remarks>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        level.RunChildren(this);
    }

    /// <summary>Gets where the link stood when the level was made, <c>tTargetPos</c>.</summary>
    public Vector3d LinkedPosition { get; private set; }

    /// <summary>Gets which way the link faced then, row by row, <c>tTargetOrientation</c>.</summary>
    public IReadOnlyList<float> LinkedOrientation { get; private set; } = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];

    /// <summary>
    /// Notes the link's pose as the level stands made (<c>EditCamera_UpdateInternalDataHandler</c>): a link that cannot tell where
    /// it is (no camera-target handler) leaves the camera unlinked.
    /// </summary>
    /// <param name="link">The task the link names, or <see langword="null"/>.</param>
    public void NoteLink(object? link)
    {
        if (link is IIgi2CameraTarget target)
        {
            LinkedPosition = target.CameraTargetPosition;
            LinkedOrientation = [.. target.CameraTargetOrientation];
        }
        else
        {
            LinkTaskId = -1;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>EditCamera_UpdateInternalDataHandler</c> (<c>igi2 0x580C20</c>): its filter's name read as the constant it is, its field
    /// of view held between a thousandth and three, and a target that is itself dropped. Its link's pose is noted once the level is
    /// made (<see cref="NoteLink"/>).
    /// </remarks>
    protected override void OnParsed()
    {
        FilterId = Igi2CutsceneCameras.FilterId(Filter);
        float fov = FieldOfView;
        FieldOfView = fov > 0.001f || fov == 0.001f ? (fov <= 3f ? fov : 3f) : 0.001f;
        if (TargetTaskId == Id && Id != NoId)
        {
            TargetTaskId = -1;
        }
    }
}
