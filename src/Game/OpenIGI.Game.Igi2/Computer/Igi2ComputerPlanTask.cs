using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.Computer;

/// <summary>
/// A building plan on the computer, I.G.I. 2's <c>ComputerPlan</c> (computerplan.c): a picture the computer's plan screen shows whole,
/// stretched over the display, listed in the order the plans are made (<c>ComputerPlan_ptComputerPlanVector</c>). It does nothing as the
/// level runs.
/// </summary>
/// <remarks>
/// <c>ComputerPlan_UpdateInternalDataHandler</c> loads the picture when the file is there; a missing one leaves the plan without a
/// picture, drawn as nothing.
/// </remarks>
public sealed partial class Igi2ComputerPlanTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "ComputerPlan";

    /// <summary>Gets or sets the picture's file.</summary>
    [ScriptParam(ScriptParamType.String256, "Picture filename")]
    public string PictureFile { get; set; } = string.Empty;

    /// <summary>Lists it on the computer, as its create handler does.</summary>
    /// <param name="computer">The computer.</param>
    public void Attach(Igi2Computer computer)
    {
        ArgumentNullException.ThrowIfNull(computer);
        computer.RegisterPlan(this);
    }
}
