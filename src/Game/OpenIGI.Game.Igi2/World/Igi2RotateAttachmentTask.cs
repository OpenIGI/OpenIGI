using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Flow;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// A part of an object that turns on the spot, I.G.I. 2's <c>RotateAttachment</c> (rotateattachment.c): it names the part by model and,
/// where an object carries several, by which one, and gives it three speeds in radians per tick.
/// </summary>
/// <remarks>
/// <c>RotateAttachment_UpdateInternalDataHandler</c> finds the part on the first object above it; its run handler turns the part's
/// local orientation by the three speeds every tick. The part is drawn, and so turned, by the scene's spin registry
/// (<see cref="Igi2RotateAttachments"/>), which reads these parameters and turns the part by the same amount a tick.
/// </remarks>
public sealed partial class Igi2RotateAttachmentTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = Igi2RotateAttachments.TaskType;

    /// <summary>Gets or sets the turn about the first axis, in radians a tick.</summary>
    [ScriptParam(ScriptParamType.Real32, "AlphaRotationSpeed")]
    public float AlphaSpeed { get; set; }

    /// <summary>Gets or sets the turn about the second axis, in radians a tick.</summary>
    [ScriptParam(ScriptParamType.Real32, "BetaRotationSpeed")]
    public float BetaSpeed { get; set; }

    /// <summary>Gets or sets the turn about the third axis, in radians a tick.</summary>
    [ScriptParam(ScriptParamType.Real32, "GammaRotationSpeed")]
    public float GammaSpeed { get; set; }

    /// <summary>Gets or sets the model of the part it turns.</summary>
    [ScriptParam(ScriptParamType.String16, "Attachment model")]
    public string AttachmentModel { get; set; } = string.Empty;

    /// <summary>Gets or sets which of the object's parts of that model it turns, from nought.</summary>
    [ScriptParam(ScriptParamType.Int32, "Attachment model instance")]
    public int AttachmentInstance { get; set; }

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        level.RunChildren(this);
    }
}
